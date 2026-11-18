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
        public const int VisionRadius = 8;
        /// <summary>Unexplored edges this close are checked without a discussion. Balance value.</summary>
        public const int PeekDistance = 2;
        /// <summary>Ticks a hero needs to cross one tile. Balance value.</summary>
        public const int HeroMoveTicks = 3;

        private readonly TilePoint[] heroTiles = new TilePoint[4];
        private readonly List<TilePoint> trail = new List<TilePoint>();
        private readonly Queue<TilePoint> route = new Queue<TilePoint>();
        private readonly List<HashSet<TilePoint>> skipped = new List<HashSet<TilePoint>>();
        /// <summary>The way the party last chose to take; it grows like a skipped way and outranks them.</summary>
        private readonly HashSet<TilePoint> heading = new HashSet<TilePoint>();
        private bool[,] seen;
        private bool[,] visible;
        private bool routeToBoss;
        /// <summary>The route ends at a chest or spring rather than at the edge of what the party has seen.</summary>
        private bool routeToFeature;
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
            routeToFeature = false;
            skipped.Clear();
            heading.Clear();
            PendingFork = null;
            ReachedBoss = false;
            Array.Clear(tactics, 0, tactics.Length);
            ClearTraitState();
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
            VisitFeatures(next);
            if (PendingLoot != null) return;

            // Re-plan once the edge we were heading for has been seen, as soon as the boss room shows up, or when a
            // chest or spring comes into view.
            bool sawFeature = freshFeature;
            freshFeature = false;
            if (routeToFeature) return;
            if (sawFeature || (!routeToBoss && (!IsFrontier(routeTarget) || (!bossWasSeen && IsSeen(Map.BossTile)))))
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
            var fresh = new HashSet<TilePoint>();
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
                    fresh.Add(tile);
                }
            }
            if (fresh.Count > 0)
            {
                SeenVersion++;
                NoticeFeatures(fresh);
                GrowSkipped(fresh);
            }
            foreach (var monster in monsters)
                if (monster.Alive && IsVisible(monster.Tile)) monster.Spotted = true;
        }

        /// <summary>Returns false when it opened a fork discussion instead of setting a route.</summary>
        private bool PlanRoute()
        {
            route.Clear();
            routeToBoss = false;
            routeToFeature = false;
            var leader = LeaderTile;
            // A chest, or a spring when tired, a few steps away is simply visited, not argued over.
            var near = FeatureWays(true).Where(way => way.Path.Count - 1 <= FeatureDetour).OrderBy(way => way.Path.Count).ToList();
            if (near.Count > 0)
            {
                FollowFeature(near[0].Path);
                return true;
            }
            var far = FeatureWays(false);
            if (IsSeen(Map.BossTile))
            {
                var toBoss = Map.FindPath(leader, Map.BossTile, Known);
                if (toBoss.Count >= 2)
                {
                    if (far.Count == 0) FollowPath(toBoss, true);
                    else
                    {
                        OpenFork(new List<Frontier>(), false, far, toBoss);
                        return false;
                    }
                    return true;
                }
            }

            var options = FrontierOptions(leader);
            var ahead = new List<Frontier>();
            var behind = new List<Frontier>();
            foreach (var option in options)
                (MatchesSkipped(option.Tiles) && !Touches(heading, option.Tiles) ? behind : ahead).Add(option);
            RefreshSkipped(options);

            // A corner a couple of steps away is simply looked around, not argued over.
            var peek = ahead.Where(way => way.Path.Count - 1 <= PeekDistance).OrderBy(way => way.Path.Count).FirstOrDefault();
            if ((ahead.Count == 1 && far.Count == 0) || peek != null)
            {
                FollowPath((peek ?? ahead[0]).Path, false);
                return true;
            }
            if (ahead.Count > 0)
            {
                OpenFork(ahead, false, far);
                return false;
            }
            if (behind.Count + far.Count >= 2)
            {
                OpenFork(behind, true, far);
                return false;
            }
            if (far.Count == 1)
            {
                FollowFeature(far[0].Path);
                return true;
            }
            if (behind.Count == 1)
            {
                log.Add($"막다른 길 • 지나쳐 온 {Describe(leader, behind[0].Path)}로 되돌아갑니다.");
                ForgetSkipped(behind[0].Tiles);
                Head(behind[0].Tiles);
                FollowPath(behind[0].Path, false);
                return true;
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
            routeToFeature = false;
        }

        /// <summary>Walks onto a chest or spring; the route holds until the party stands on it.</summary>
        private void FollowFeature(IReadOnlyList<TilePoint> path)
        {
            FollowPath(path, false);
            routeToFeature = true;
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

        /// <summary>
        /// A skipped way stays the same way while the party glimpses more of it from elsewhere: floor seen for the
        /// first time that joins a skipped way joins its memory, so its edge can move off without being mistaken for a
        /// new way to argue over.
        /// </summary>
        private void GrowSkipped(HashSet<TilePoint> fresh)
        {
            if (skipped.Count == 0) return;
            fresh.RemoveWhere(tile => !Map.IsFloor(tile));
            // The chosen way claims what it opens onto first, so a room both ways reach stays the chosen one.
            fresh.ExceptWith(Grow(heading, fresh));
            foreach (var way in skipped) Grow(way, fresh);
        }

        /// <summary>Adds to a way the fresh floor joined to it; returns the tiles it took.</summary>
        private static List<TilePoint> Grow(HashSet<TilePoint> way, HashSet<TilePoint> fresh)
        {
            var taken = new List<TilePoint>();
            if (way.Count == 0) return taken;
            var queue = new Queue<TilePoint>(fresh.Where(tile => Borders(way, tile)));
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                if (!way.Add(tile)) continue;
                taken.Add(tile);
                foreach (var next in Neighbours(tile))
                    if (fresh.Contains(next) && !way.Contains(next)) queue.Enqueue(next);
            }
            return taken;
        }

        private void Head(HashSet<TilePoint> way)
        {
            heading.Clear();
            heading.UnionWith(way);
        }

        private static IEnumerable<TilePoint> Neighbours(TilePoint tile)
        {
            yield return new TilePoint(tile.X, tile.Y - 1);
            yield return new TilePoint(tile.X + 1, tile.Y);
            yield return new TilePoint(tile.X, tile.Y + 1);
            yield return new TilePoint(tile.X - 1, tile.Y);
        }

        private static bool Borders(HashSet<TilePoint> way, TilePoint tile) =>
            !way.Contains(tile) && Neighbours(tile).Any(way.Contains);

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
                skipped[i].UnionWith(matches.SelectMany(option => option.Tiles));
            }
        }

        private void ForgetSkipped(HashSet<TilePoint> tiles) => skipped.RemoveAll(set => Touches(set, tiles));

        /// <summary>
        /// Opens a discussion over unexplored ways, then the boss room when <paramref name="toBoss"/> is given, then the
        /// remembered chests and springs in <paramref name="features"/>.
        /// </summary>
        private void OpenFork(List<Frontier> ways, bool backtracking,
            List<(ForkFeature Feature, int Index, IReadOnlyList<TilePoint> Path)> features = null, IReadOnlyList<TilePoint> toBoss = null)
        {
            var leader = LeaderTile;
            var fromEntrance = Map.Distances(Map.Entrance, Known);
            var marker = BossMarker;
            var all = ways.Select(way => (Feature: ForkFeature.Way, Index: -1, way.Path, way.Tiles)).ToList();
            if (toBoss != null) all.Add((ForkFeature.Boss, -1, toBoss, new HashSet<TilePoint> { Map.BossTile }));
            foreach (var feature in features ?? new List<(ForkFeature, int, IReadOnlyList<TilePoint>)>())
                all.Add((feature.Feature, feature.Index, feature.Path, new HashSet<TilePoint> { feature.Path[feature.Path.Count - 1] }));
            string Noun(ForkFeature feature) =>
                feature == ForkFeature.Chest ? "상자 " : feature == ForkFeature.Spring ? "샘물 " : feature == ForkFeature.Boss ? "보스방 " : "";
            var labels = all.Select(way => Direction(leader, way.Path[way.Path.Count - 1]) + "쪽 " + Noun(way.Feature) + "길").ToList();
            var options = new List<ForkOption>();
            for (int i = 0; i < all.Count; i++)
            {
                string label = labels.Count(other => other == labels[i]) > 1
                    ? labels[i].Replace(" 길", $" {labels.Take(i + 1).Count(other => other == labels[i])}번 길")
                    : labels[i];
                int seenMonsters = VisibleMonstersAlong(new Frontier { Path = all[i].Path, Tiles = all[i].Tiles });
                var end = all[i].Path[all[i].Path.Count - 1];
                fromEntrance.TryGetValue(end, out int depth);
                int markerDistance = marker.HasValue ? Math.Max(Math.Abs(end.X - marker.Value.X), Math.Abs(end.Y - marker.Value.Y)) : -1;
                options.Add(new ForkOption(i, label, all[i].Path, all[i].Tiles, seenMonsters, depth, markerDistance,
                    all[i].Feature, all[i].Index));
            }
            log.Add((backtracking ? "막다른 길 • 어디로 되돌아갈지 논의합니다: " : "갈림길 • 용사들이 길을 논의합니다: ") +
                    string.Join(" / ", options.Select(option => option.Label)));
            var grumbling = RollGrumblers(MarchOrder.Where(role => party[(int)role].Hp > 0));
            var local = new List<ForkOpinion>();
            foreach (var role in MarchOrder)
            {
                var hero = party[(int)role];
                if (hero.Hp > 0) local.Add(LocalOpinion(hero, options, grumbling.Contains(role)));
            }
            PendingFork = new ForkDiscussion(++nextForkToken, backtracking, options, local, grumbling);
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

        private static string Describe(TilePoint from, IReadOnlyList<TilePoint> path) =>
            Direction(from, path[path.Count - 1]) + "쪽 길";

        /// <summary>The compass direction from one tile to another, north up: "동", "북동", ….</summary>
        private static string Direction(TilePoint from, TilePoint to)
        {
            // Rows grow southward, so flip y to measure the angle with north up.
            double angle = Math.Atan2(-(to.Y - from.Y), to.X - from.X) * 180 / Math.PI;
            string[] names = { "동", "북동", "북", "북서", "서", "남서", "남", "남동" };
            int sector = (int)Math.Floor(((angle + 360 + 22.5) % 360) / 45);
            return names[sector];
        }

        /// <summary>
        /// The boss-room marker the party remembers: of the boss-room floor it has seen this stage, the tile nearest
        /// the leader (ties by row, then column). Null before it has seen any.
        /// </summary>
        public TilePoint? BossMarker
        {
            get
            {
                var leader = LeaderTile;
                TilePoint? best = null;
                int bestDistance = int.MaxValue;
                for (int y = 0; y < Map.Height; y++)
                for (int x = 0; x < Map.Width; x++)
                {
                    var tile = new TilePoint(x, y);
                    if (!seen[x, y] || !Map.IsBossRoom(tile)) continue;
                    int distance = Math.Max(Math.Abs(x - leader.X), Math.Abs(y - leader.Y));
                    if (distance >= bestDistance) continue;
                    best = tile;
                    bestDistance = distance;
                }
                return best;
            }
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

        /// <summary>
        /// What a hero argues for from personality, strength, the visible danger and the party's health. A
        /// grumbling hero argues for the same way, only sourly.
        /// </summary>
        public ForkOpinion LocalOpinion(HeroState hero, IReadOnlyList<ForkOption> options, bool grumbling = false)
        {
            bool hurt = PartyHurt();
            int best = 0;
            double bestScore = double.MinValue;
            foreach (var option in options)
            {
                double score = -.15 * option.Distance + .5 * hero.Modifier(Ability.Strength) * option.VisibleMonsters;
                switch (hero.Voice)
                {
                    case Voice.Bold: score += 3 * option.VisibleMonsters + .25 * option.Depth; break;
                    case Voice.Careful: score -= (hurt ? 6 : 3) * option.VisibleMonsters + .2 * option.Distance; break;
                    case Voice.Curious: score += (option.Feature == ForkFeature.Way ? .4 * option.Openness : 0) + .1 * option.Depth; break;
                }
                score += FeatureAppeal(hero, option, hurt);
                // The navigator heads for the marker they remember; with none seen they argue like anyone else.
                if (hero.Personality == PersonalityTrait.Navigator && option.MarkerDistance >= 0) score -= 2 * option.MarkerDistance;
                if (score <= bestScore) continue;
                bestScore = score;
                best = option.Index;
            }
            var choice = options[best];
            // Fatigue colours what the heroes say, never which way they vote.
            var fatigue = Fatigue;
            bool exhausted = fatigue == FatigueStage.Exhausted;
            if (grumbling) return new ForkOpinion(hero.Role, best, DialogueLines.GrumbleOpinion(hero.Voice, choice.Label, exhausted));
            if (choice.Feature != ForkFeature.Way) return new ForkOpinion(hero.Role, best, FeatureLine(hero, choice, hurt));
            if (hero.Personality == PersonalityTrait.Navigator && choice.MarkerDistance >= 0)
                return new ForkOpinion(hero.Role, best, fatigue == FatigueStage.Rested
                    ? $"아까 본 보스방 표식, {choice.Label}로 이어져."
                    : $"지쳐도 길은 기억해. 표식은 {choice.Label} 쪽이야.");
            string line;
            switch (hero.Voice)
            {
                case Voice.Bold:
                    if (fatigue != FatigueStage.Rested)
                        line = choice.VisibleMonsters > 0
                            ? (exhausted ? $"숨이 턱까지 찼지만 {choice.Label}의 적은 내가 벤다!" : $"다리가 무거워도 {choice.Label}의 적쯤은 문제없어!")
                            : (exhausted ? $"헉헉… 그래도 멈출 순 없지. {choice.Label}로 간다!" : $"좀 지쳤지만 {choice.Label}로 밀고 나가자!");
                    else
                        line = choice.VisibleMonsters > 0 ? $"{choice.Label}에 적이 있어. 정면 돌파다!" : $"{choice.Label}로 깊이 들어가자!";
                    break;
                case Voice.Careful:
                    if (hurt) line = $"다들 지쳤어요. {choice.Label}로 조심히 가요.";
                    else if (fatigue != FatigueStage.Rested)
                        line = exhausted ? $"이대로 더 헤매면 쓰러져요. {choice.Label}로 가요." :
                            choice.VisibleMonsters == 0 ? $"발이 무거워요. 조용한 {choice.Label}로 가요." : $"지친 몸으로는 위험해요. 적을 살피며 {choice.Label}로 가죠.";
                    else
                        line = choice.VisibleMonsters == 0 ? $"{choice.Label}은 조용해 보여요." : $"적을 살피며 {choice.Label}로 가죠.";
                    break;
                case Voice.Curious:
                    line = fatigue == FatigueStage.Rested ? $"{choice.Label}이 더 넓게 트였어. 가 보자." :
                        exhausted ? $"구경할 기운도 없지만… {choice.Label}이 제일 트였어." : $"좀 지치긴 했어도 {choice.Label}은 궁금한걸.";
                    break;
                default:
                    line = fatigue == FatigueStage.Rested ? $"{choice.Label}로 가자. 그쪽이 나아 보여." :
                        exhausted ? $"더는 못 헤매. {choice.Label}로 가자." : $"좀 지쳤어. {choice.Label}로 가는 게 좋겠어.";
                    break;
            }
            return new ForkOpinion(hero.Role, best, line);
        }

        /// <summary>
        /// How much a hero leans toward a chest, spring or the boss room over plain exploring. Everyone likes a chest a
        /// little; the greedy and collectors much more; a careful hero turns it down when the party is hurt. A spring
        /// draws the party only when it is tired, the careful most. Once the boss room is in sight most want to finish.
        /// </summary>
        private double FeatureAppeal(HeroState hero, ForkOption option, bool hurt)
        {
            switch (option.Feature)
            {
                case ForkFeature.Chest:
                {
                    double appeal = 3;
                    if (hero.Personality == PersonalityTrait.Greedy) appeal += 6;
                    if (hero.Personality == PersonalityTrait.Collector) appeal += 4;
                    if (hero.Voice == Voice.Curious) appeal += 1.5;
                    if (hero.Voice == Voice.Careful && hurt) appeal -= 5;
                    return appeal;
                }
                case ForkFeature.Spring:
                {
                    double appeal = Fatigue == FatigueStage.Exhausted ? 7 : Fatigue == FatigueStage.Tired ? 3.5 : 0;
                    if (hero.Voice == Voice.Careful) appeal += 3;
                    return appeal;
                }
                case ForkFeature.Boss:
                {
                    double appeal = 4;
                    if (hero.Voice == Voice.Bold) appeal += 2;
                    if (hero.Personality == PersonalityTrait.Navigator) appeal += 2;
                    return appeal;
                }
                default: return 0;
            }
        }

        private string FeatureLine(HeroState hero, ForkOption choice, bool hurt)
        {
            switch (choice.Feature)
            {
                case ForkFeature.Chest:
                    if (hero.Personality == PersonalityTrait.Greedy) return $"저 상자, 그냥 두고 갈 순 없지. {choice.Label}로!";
                    if (hero.Personality == PersonalityTrait.Collector) return $"희귀한 게 들었을지도 몰라. {choice.Label}부터 가 보자.";
                    return hero.Voice == Voice.Careful ? $"상자 정도는 챙겨 가요. {choice.Label}로 조심히요." : $"{choice.Label} 끝에 상자가 있었어. 열어 보자.";
                case ForkFeature.Spring:
                    return Fatigue == FatigueStage.Exhausted ? $"더는 못 걸어… {choice.Label}에서 물 좀 마시자." : $"{choice.Label}에 샘물이 있었어요. 잠깐 쉬어 가요.";
                default:
                    return hurt ? $"다쳤어도 끝이 보여. {choice.Label}로 가자." : $"보스방이 저기야. {choice.Label}로 끝내자!";
            }
        }
    }
}
