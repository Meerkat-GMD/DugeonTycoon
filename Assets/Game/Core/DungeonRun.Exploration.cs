using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungeonTycoon.Core
{
    public sealed partial class DungeonRun
    {
        /// <summary>
        /// How far each hero sees, in tiles. Wider than a monster's notice so the party can spot danger and
        /// argue about it before the monster reacts. Balance value.
        /// </summary>
        public const int VisionRadius = 6;
        /// <summary>Unexplored edges this close are checked without a discussion. Balance value.</summary>
        public const int PeekDistance = 2;
        /// <summary>Ticks a hero needs to cross one tile. Balance value.</summary>
        public const int HeroMoveTicks = 3;

        private readonly TilePoint[] heroTiles = new TilePoint[4];
        private readonly List<TilePoint> trail = new List<TilePoint>();
        private readonly Queue<TilePoint> route = new Queue<TilePoint>();
        private readonly List<HashSet<TilePoint>> skipped = new List<HashSet<TilePoint>>();
        private bool[,] seen;
        private bool[,] visible;
        private bool routeToBoss;
        private TilePoint routeTarget;
        private int nextForkToken;

        /// <summary>The fork the party is discussing, or null.</summary>
        public ForkDiscussion PendingFork { get; private set; }
        /// <summary>Summary of the most recent fork decision.</summary>
        public string LastFork { get; private set; } = "";
        public int ForkCount { get; private set; }
        public int StepsTaken { get; private set; }
        /// <summary>Changes whenever the party sees tiles it had not seen before.</summary>
        public int SeenVersion { get; private set; }
        public TilePoint HeroTile(HeroRole role) => heroTiles[(int)role];
        public TilePoint LeaderTile => heroTiles[(int)Leader().Role];
        /// <summary>The party has seen this tile at some point during the stage.</summary>
        public bool IsSeen(TilePoint tile) => Map.Contains(tile) && seen[tile.X, tile.Y];
        /// <summary>A living hero can see this tile right now.</summary>
        public bool IsVisible(TilePoint tile) => Map.Contains(tile) && visible[tile.X, tile.Y];
        public int DefeatedCount => monsters.Count(m => !m.IsBoss && !m.Alive);
        public int SpottedCount => monsters.Count(m => !m.IsBoss && m.Spotted);

        private void ResetExploration()
        {
            seen = new bool[Map.Width, Map.Height];
            visible = new bool[Map.Width, Map.Height];
            for (int i = 0; i < heroTiles.Length; i++) heroTiles[i] = Map.Entrance;
            trail.Clear();
            trail.Add(Map.Entrance);
            route.Clear();
            routeToBoss = false;
            skipped.Clear();
            PendingFork = null;
            ReachedBoss = false;
            Array.Clear(tactics, 0, tactics.Length);
            SeenVersion++;
        }

        private HeroState Leader()
        {
            foreach (var role in MarchOrder)
                if (party[(int)role].Hp > 0) return party[(int)role];
            return party[(int)HeroRole.Tank];
        }

        /// <summary>The leader walks one tile when ready; the others follow its trail in marching order.</summary>
        private void ExploreTick()
        {
            var leader = Leader();
            if (PendingFork != null || leader.MoveReady > Ticks) return;
            if (route.Count == 0 && !PlanRoute()) return;
            if (route.Count == 0) return;

            var next = route.Dequeue();
            trail.Add(next);
            var marching = MarchOrder.Where(role => party[(int)role].Hp > 0).ToList();
            for (int rank = 0; rank < marching.Count; rank++)
                heroTiles[(int)marching[rank]] = trail[Math.Max(0, trail.Count - 1 - rank)];
            leader.MoveReady = Ticks + HeroMoveTicks;
            StepsTaken++;
            TireFromStep();
            bool bossWasSeen = IsSeen(Map.BossTile);
            Reveal();

            // Re-plan once the edge we were heading for has been seen, or as soon as the boss room shows up.
            if (!routeToBoss && (!IsFrontier(routeTarget) || (!bossWasSeen && IsSeen(Map.BossTile))))
                route.Clear();
        }

        /// <summary>After a fight the heroes stand wherever they fought; line up behind the leader again.</summary>
        private void Regroup()
        {
            trail.Clear();
            var marching = MarchOrder.Where(role => party[(int)role].Hp > 0).ToList();
            for (int rank = marching.Count - 1; rank >= 0; rank--) trail.Add(heroTiles[(int)marching[rank]]);
            if (trail.Count == 0) trail.Add(Map.Entrance);
            route.Clear();
        }

        private bool Known(TilePoint tile) => Map.IsFloor(tile) && seen[tile.X, tile.Y];

        /// <summary>
        /// A known floor tile that opens onto floor the party has not seen yet. Heroes standing at the edge of
        /// what they have seen can tell an opening from a wall, so an unseen wall does not count as a way on.
        /// </summary>
        private bool IsFrontier(TilePoint tile)
        {
            if (!Known(tile)) return false;
            bool Opens(int dx, int dy)
            {
                var next = new TilePoint(tile.X + dx, tile.Y + dy);
                return Map.IsFloor(next) && !seen[next.X, next.Y];
            }
            return Opens(0, -1) || Opens(1, 0) || Opens(0, 1) || Opens(-1, 0);
        }

        private void Reveal()
        {
            Array.Clear(visible, 0, visible.Length);
            bool grew = false;
            foreach (var hero in party)
            {
                if (hero.Hp == 0) continue;
                var eye = heroTiles[(int)hero.Role];
                for (int y = eye.Y - VisionRadius; y <= eye.Y + VisionRadius; y++)
                for (int x = eye.X - VisionRadius; x <= eye.X + VisionRadius; x++)
                {
                    var tile = new TilePoint(x, y);
                    if (!Map.Contains(tile) || visible[x, y]) continue;
                    int dx = x - eye.X, dy = y - eye.Y;
                    if (dx * dx + dy * dy > VisionRadius * VisionRadius || !Map.HasLineOfSight(eye, tile)) continue;
                    visible[x, y] = true;
                    if (seen[x, y]) continue;
                    seen[x, y] = true;
                    grew = true;
                }
            }
            if (grew) SeenVersion++;
            foreach (var monster in monsters)
                if (monster.Alive && IsVisible(monster.Tile)) monster.Spotted = true;
        }

        /// <summary>Returns false when it opened a fork discussion instead of setting a route.</summary>
        private bool PlanRoute()
        {
            route.Clear();
            routeToBoss = false;
            var leader = LeaderTile;
            if (IsSeen(Map.BossTile))
            {
                var toBoss = Map.FindPath(leader, Map.BossTile, Known);
                if (toBoss.Count >= 2)
                {
                    FollowPath(toBoss, true);
                    return true;
                }
            }

            var options = FrontierOptions(leader);
            var ahead = new List<Frontier>();
            var behind = new List<Frontier>();
            foreach (var option in options) (MatchesSkipped(option.Tiles) ? behind : ahead).Add(option);
            RefreshSkipped(options);

            // A corner a couple of steps away is simply looked around, not argued over.
            var peek = ahead.Where(way => way.Path.Count - 1 <= PeekDistance).OrderBy(way => way.Path.Count).FirstOrDefault();
            if (ahead.Count == 1 || peek != null)
            {
                FollowPath((peek ?? ahead[0]).Path, false);
                return true;
            }
            if (ahead.Count >= 2)
            {
                OpenFork(ahead, false);
                return false;
            }
            if (behind.Count == 1)
            {
                log.Add($"막다른 길 • 지나쳐 온 {Describe(leader, behind[0].Path)}로 되돌아갑니다.");
                ForgetSkipped(behind[0].Tiles);
                FollowPath(behind[0].Path, false);
                return true;
            }
            if (behind.Count >= 2)
            {
                OpenFork(behind, true);
                return false;
            }

            // Every reachable edge is explored but the boss room is still unseen. Valid maps cannot get here;
            // walk on by the real map so the expedition never stalls.
            var fallback = Map.FindPath(leader, Map.BossTile);
            if (fallback.Count >= 2)
            {
                log.Add("길을 더듬어 • 용사들이 보스방 쪽으로 나아갑니다.");
                FollowPath(fallback, true);
            }
            return true;
        }

        private void FollowPath(IReadOnlyList<TilePoint> path, bool toBoss)
        {
            int end = toBoss ? path.Count - 1 : path.Count;
            for (int i = 1; i < end; i++) route.Enqueue(path[i]);
            routeTarget = path[path.Count - 1];
            routeToBoss = toBoss;
        }

        private sealed class Frontier
        {
            public HashSet<TilePoint> Tiles;
            public IReadOnlyList<TilePoint> Path;
        }

        /// <summary>Groups the reachable edges of the known map; each group is one way the party could go.</summary>
        private List<Frontier> FrontierOptions(TilePoint leader)
        {
            var reach = Map.Distances(leader, Known);
            var edge = new HashSet<TilePoint>(reach.Keys.Where(IsFrontier));
            var groups = new List<Frontier>();
            var claimed = new HashSet<TilePoint>();
            foreach (var start in edge.OrderBy(tile => reach[tile]).ThenBy(tile => tile.Y).ThenBy(tile => tile.X))
            {
                if (!claimed.Add(start)) continue;
                var tiles = new HashSet<TilePoint> { start };
                var queue = new Queue<TilePoint>();
                queue.Enqueue(start);
                // Edge tiles up to two apart belong to the same way, so a half-seen room is one option.
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        var next = new TilePoint(current.X + dx, current.Y + dy);
                        if (!edge.Contains(next) || !claimed.Add(next)) continue;
                        tiles.Add(next);
                        queue.Enqueue(next);
                    }
                }
                // The edge tile we reach first is where we head; the leader's own tile is never a destination.
                var target = tiles.Where(tile => tile != leader).OrderBy(tile => reach[tile]).ThenBy(tile => tile.Y)
                    .ThenBy(tile => tile.X).Cast<TilePoint?>().FirstOrDefault();
                if (target == null) continue;
                groups.Add(new Frontier { Tiles = tiles, Path = Map.FindPath(leader, target.Value, Known) });
            }
            return groups;
        }

        private bool MatchesSkipped(HashSet<TilePoint> tiles) => skipped.Any(set => Touches(set, tiles));

        private static bool Touches(HashSet<TilePoint> a, HashSet<TilePoint> b)
        {
            foreach (var tile in b)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (a.Contains(new TilePoint(tile.X + dx, tile.Y + dy))) return true;
            return false;
        }

        /// <summary>Follows each skipped way as its edge shifts; drops the ones that have been fully seen.</summary>
        private void RefreshSkipped(List<Frontier> current)
        {
            for (int i = skipped.Count - 1; i >= 0; i--)
            {
                var matches = current.Where(option => Touches(skipped[i], option.Tiles)).ToList();
                if (matches.Count == 0)
                {
                    skipped.RemoveAt(i);
                    continue;
                }
                skipped[i] = new HashSet<TilePoint>(matches.SelectMany(option => option.Tiles));
            }
        }

        private void ForgetSkipped(HashSet<TilePoint> tiles) => skipped.RemoveAll(set => Touches(set, tiles));

        private void OpenFork(List<Frontier> ways, bool backtracking)
        {
            var leader = LeaderTile;
            var fromEntrance = Map.Distances(Map.Entrance, Known);
            var labels = ways.Select(way => Describe(leader, way.Path)).ToList();
            var options = new List<ForkOption>();
            for (int i = 0; i < ways.Count; i++)
            {
                string label = labels.Count(other => other == labels[i]) > 1
                    ? labels[i].Replace(" 길", $" {labels.Take(i + 1).Count(other => other == labels[i])}번 길")
                    : labels[i];
                int seenMonsters = VisibleMonstersAlong(ways[i]);
                fromEntrance.TryGetValue(ways[i].Path[ways[i].Path.Count - 1], out int depth);
                options.Add(new ForkOption(i, label, ways[i].Path, ways[i].Tiles, seenMonsters, depth));
            }
            var local = new List<ForkOpinion>();
            foreach (var role in MarchOrder)
            {
                var hero = party[(int)role];
                if (hero.Hp > 0) local.Add(LocalOpinion(hero, options));
            }
            PendingFork = new ForkDiscussion(++nextForkToken, backtracking, options, local);
            log.Add((backtracking ? "막다른 길 • 어디로 되돌아갈지 논의합니다: " : "갈림길 • 용사들이 길을 논의합니다: ") +
                    string.Join(" / ", options.Select(option => option.Label)));
        }

        private void CancelFork()
        {
            if (PendingFork == null) return;
            PendingFork = null;
            log.Add("논의 중단 • 적이 다가와 용사들이 무기를 듭니다.");
        }

        private int VisibleMonstersAlong(Frontier way)
        {
            int count = 0;
            foreach (var monster in monsters)
            {
                if (!monster.Alive || monster.IsBoss || !IsVisible(monster.Tile)) continue;
                var tile = monster.Tile;
                bool near = way.Path.Concat(way.Tiles).Any(step =>
                    Math.Max(Math.Abs(step.X - tile.X), Math.Abs(step.Y - tile.Y)) <= 2);
                if (near) count++;
            }
            return count;
        }

        private static string Describe(TilePoint from, IReadOnlyList<TilePoint> path)
        {
            var to = path[path.Count - 1];
            // Rows grow southward, so flip y to measure the angle with north up.
            double angle = Math.Atan2(-(to.Y - from.Y), to.X - from.X) * 180 / Math.PI;
            string[] names = { "동", "북동", "북", "북서", "서", "남서", "남", "남동" };
            int sector = (int)Math.Floor(((angle + 360 + 22.5) % 360) / 45);
            return names[sector] + "쪽 길";
        }

        private bool PartyHurt()
        {
            int hp = 0, max = 0;
            foreach (var hero in party)
            {
                if (hero.Hp == 0) continue;
                hp += hero.Hp;
                max += hero.MaxHp;
            }
            return max > 0 && hp * 10 < max * 6;
        }

        /// <summary>What a hero argues for from personality, strength, the visible danger and the party's health.</summary>
        public ForkOpinion LocalOpinion(HeroState hero, IReadOnlyList<ForkOption> options)
        {
            bool hurt = PartyHurt();
            int best = 0;
            double bestScore = double.MinValue;
            foreach (var option in options)
            {
                double score = -.15 * option.Distance + .5 * hero.Modifier(Ability.Strength) * option.VisibleMonsters;
                foreach (var tag in hero.Tags)
                {
                    switch (tag)
                    {
                        case PersonalityTag.Reckless: score += 3 * option.VisibleMonsters + .25 * option.Depth; break;
                        case PersonalityTag.Cautious: score -= (hurt ? 6 : 3) * option.VisibleMonsters + .2 * option.Distance; break;
                        case PersonalityTag.Curious: score += .4 * option.Openness + .1 * option.Depth; break;
                    }
                }
                if (score <= bestScore) continue;
                bestScore = score;
                best = option.Index;
            }
            var choice = options[best];
            // Fatigue colours what the heroes say, never which way they vote.
            var fatigue = Fatigue;
            bool exhausted = fatigue == FatigueStage.Exhausted;
            string line;
            switch (hero.Tags[0])
            {
                case PersonalityTag.Reckless:
                    if (fatigue != FatigueStage.Rested)
                        line = choice.VisibleMonsters > 0
                            ? (exhausted ? $"숨이 턱까지 찼지만 {choice.Label}의 적은 내가 벤다!" : $"다리가 무거워도 {choice.Label}의 적쯤은 문제없어!")
                            : (exhausted ? $"헉헉… 그래도 멈출 순 없지. {choice.Label}로 간다!" : $"좀 지쳤지만 {choice.Label}로 밀고 나가자!");
                    else
                        line = choice.VisibleMonsters > 0 ? $"{choice.Label}에 적이 있어. 정면 돌파다!" : $"{choice.Label}로 깊이 들어가자!";
                    break;
                case PersonalityTag.Cautious:
                    if (hurt) line = $"다들 지쳤어요. {choice.Label}로 조심히 가요.";
                    else if (fatigue != FatigueStage.Rested)
                        line = exhausted ? $"이대로 더 헤매면 쓰러져요. {choice.Label}로 가요." :
                            choice.VisibleMonsters == 0 ? $"발이 무거워요. 조용한 {choice.Label}로 가요." : $"지친 몸으로는 위험해요. 적을 살피며 {choice.Label}로 가죠.";
                    else
                        line = choice.VisibleMonsters == 0 ? $"{choice.Label}은 조용해 보여요." : $"적을 살피며 {choice.Label}로 가죠.";
                    break;
                default:
                    line = fatigue == FatigueStage.Rested ? $"{choice.Label}이 더 넓게 트였어. 가 보자." :
                        exhausted ? $"구경할 기운도 없지만… {choice.Label}이 제일 트였어." : $"좀 지치긴 했어도 {choice.Label}은 궁금한걸.";
                    break;
            }
            return new ForkOpinion(hero.Role, best, line);
        }

        /// <summary>
        /// Settles the pending fork. Valid proposals (from the AI) replace a hero's local opinion; anything
        /// missing or invalid falls back to it. Split votes are settled by d20 + Charisma.
        /// </summary>
        public bool ResolveFork(int token, IReadOnlyList<ForkOpinion> proposals = null)
        {
            var fork = PendingFork;
            if (fork == null || token != fork.Token || Phase != RunPhase.Travel) return false;
            var opinions = new List<ForkOpinion>();
            foreach (var local in fork.LocalOpinions)
            {
                if (party[(int)local.Role].Hp == 0) continue;
                var proposal = proposals?.FirstOrDefault(p => p != null && p.Role == local.Role);
                bool valid = proposal != null && proposal.Option >= 0 && proposal.Option < fork.Options.Count &&
                             !string.IsNullOrWhiteSpace(proposal.Line);
                var opinion = valid ? proposal : local;
                opinions.Add(opinion);
                var hero = party[(int)opinion.Role];
                hero.LastLine = opinion.Line;
                log.Add($"{hero.Name} → {fork.Options[opinion.Option].Label} “{opinion.Line}”");
            }
            if (opinions.Count == 0) return false;

            // The stage shows the debate front to back, the way the party marches.
            opinions = opinions.OrderBy(o => Array.IndexOf(MarchOrder, o.Role)).ToList();
            var scene = opinions.Select(o => DialogueBeat.Line(o.Role, o.Line)).ToList();
            int chosen;
            var summary = new StringBuilder();
            if (opinions.Select(o => o.Option).Distinct().Count() == 1)
            {
                chosen = opinions[0].Option;
                summary.Append($"의견 일치 → {fork.Options[chosen].Label}");
                scene.Add(DialogueBeat.Narration($"의견이 모였다. {fork.Options[chosen].Label}로 간다."));
            }
            else
            {
                string charisma = HeroTraits.Name(Ability.Charisma);
                var rolls = opinions.Select(opinion =>
                {
                    int face = dice.Next(1, 21);
                    return (Opinion: opinion, Die: new DiceRoll(opinion.Role, face, party[(int)opinion.Role].Modifier(Ability.Charisma), charisma));
                }).ToList();
                int top = rolls.Max(r => r.Die.Total);
                var leaders = rolls.Where(r => r.Die.Total == top).ToList();
                int Supporters(int option) => opinions.Count(o => o.Option == option);
                var winner = leaders.OrderByDescending(r => Supporters(r.Opinion.Option))
                    .ThenBy(r => Array.IndexOf(MarchOrder, r.Opinion.Role)).First();
                chosen = winner.Opinion.Option;
                var speaker = party[(int)winner.Opinion.Role];
                string way = fork.Options[chosen].Label;

                summary.Append("설득 d20 + 매력  ");
                summary.Append(string.Join("  ", rolls.Select(r =>
                    $"{party[(int)r.Die.Role].Name} [{r.Die.Face}]{(r.Die.Modifier >= 0 ? "+" : "")}{r.Die.Modifier}={r.Die.Total}")));
                summary.Append($"\n→ {speaker.Name}의 의견대로 {way}");

                scene.Add(DialogueBeat.Contest(DialogueLines.PersuasionTitle, rolls.Select(r => r.Die).ToList(), speaker.Role));
                if (leaders.Select(r => r.Opinion.Option).Distinct().Count() > 1)
                {
                    bool bySupport = leaders.Count(r => Supporters(r.Opinion.Option) == Supporters(chosen)) <
                                     leaders.Count;
                    scene.Add(DialogueBeat.Narration(bySupport
                        ? $"합계가 같다. 지지한 용사가 더 많은 {way}로 정한다."
                        : $"합계도, 지지한 용사 수도 같다. 대형 앞쪽 {speaker.Name}의 의견을 따른다."));
                }
                string closing = DialogueLines.Closing(speaker.Role);
                scene.Add(DialogueBeat.Line(speaker.Role, closing));
                scene.Add(DialogueBeat.Narration($"{speaker.Name}의 의견대로 {way}로 간다."));
                log.Add($"{speaker.Name} “{closing}”");
            }
            LastFork = summary.ToString();
            LastScene = scene.AsReadOnly();
            log.Add("결정 • " + LastFork.Replace('\n', ' '));
            ForkCount++;
            PendingFork = null;

            // Ways not taken are remembered so the party comes back to them only from a dead end.
            for (int i = 0; i < fork.Options.Count; i++)
                if (i != chosen && !MatchesSkipped(fork.Options[i].Frontier)) skipped.Add(new HashSet<TilePoint>(fork.Options[i].Frontier));
            ForgetSkipped(fork.Options[chosen].Frontier);
            route.Clear();
            var path = Map.FindPath(LeaderTile, fork.Options[chosen].Target, Known);
            if (path.Count >= 2) FollowPath(path, false);
            return true;
        }
    }
}
