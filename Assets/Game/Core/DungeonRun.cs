using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungeonTycoon.Core
{
    public sealed class DungeonRun
    {
        public const int BlessCost = 1;
        /// <summary>Tiles this many steps or fewer from the entrance cannot hold monsters. Balance value.</summary>
        public const int EntranceClearance = 4;
        /// <summary>How far each hero sees, in tiles. Balance value.</summary>
        public const int VisionRadius = 5;
        /// <summary>
        /// Interim encounter rule until monsters chase: a visible monster this many steps or fewer from the
        /// party leader starts a fight. Balance value.
        /// </summary>
        public const int EncounterReach = 2;
        /// <summary>Unexplored edges this close are checked without a discussion. Balance value.</summary>
        public const int PeekDistance = 2;
        private static readonly HeroRole[] MarchOrder =
            { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.Healer, HeroRole.RangedDamage };

        private readonly List<MonsterPlacement> placements = new List<MonsterPlacement>();
        private readonly List<bool> defeated = new List<bool>();
        private readonly List<bool> spotted = new List<bool>();
        private readonly List<string> log = new List<string>();
        private readonly HeroState[] party =
        {
            new HeroState(HeroRole.Tank, "가론", 46, 5, new[] { 3, 0, 1, 1 }, PersonalityTag.Cautious),
            new HeroState(HeroRole.Healer, "세레나", 28, 3, new[] { 0, 1, 2, 2 }, PersonalityTag.Cautious, PersonalityTag.Curious),
            new HeroState(HeroRole.MeleeDamage, "카이", 36, 8, new[] { 3, 2, -1, 0 }, PersonalityTag.Reckless),
            new HeroState(HeroRole.RangedDamage, "리아", 32, 7, new[] { 0, 2, 1, 1 }, PersonalityTag.Curious)
        };
        private readonly TilePoint[] heroTiles = new TilePoint[4];
        private readonly List<TilePoint> trail = new List<TilePoint>();
        private readonly Queue<TilePoint> route = new Queue<TilePoint>();
        private readonly List<HashSet<TilePoint>> skipped = new List<HashSet<TilePoint>>();
        private readonly Random dice;
        private bool[,] seen;
        private bool[,] visible;
        private bool routeToBoss;
        private TilePoint routeTarget;
        private int nextTrialToken;
        private int pendingTrial;
        private int nextForkToken;
        private int encounterRound;

        public DungeonRun(int seed = 20261118)
        {
            dice = new Random(seed);
            Placements = placements.AsReadOnly();
            Party = Array.AsReadOnly(party);
            Log = log.AsReadOnly();
            LoadMap(DungeonMap.ForStage(1));
            log.Add("첫 원정 • 성문으로 가는 던전에 몬스터를 배치하세요.");
        }

        public RunPhase Phase { get; private set; } = RunPhase.Planning;
        public int Stage { get; private set; } = 1;
        public DungeonMap Map { get; private set; }
        /// <summary>Monsters in the order the GM placed them.</summary>
        public IReadOnlyList<MonsterPlacement> Placements { get; }
        /// <summary>Index used for the stage boss wherever a monster index is expected.</summary>
        public int BossIndex => placements.Count;
        /// <summary>Placement index of the monster being fought, <see cref="BossIndex"/> for the boss, or -1.</summary>
        public int FoeIndex { get; private set; } = -1;
        public bool FightingBoss => Phase == RunPhase.Combat && FoeIndex == BossIndex;
        /// <summary>True once the party has engaged this stage's boss.</summary>
        public bool ReachedBoss { get; private set; }
        public int Budget { get; private set; } = 6;
        public int Faith { get; private set; } = 1;
        public int TrialsLeft { get; private set; } = 2;
        public int Experience { get; private set; }
        public int Level => 1 + Experience / 5;
        public int RoundToken { get; private set; }
        public bool PendingAdversity => pendingTrial != 0;
        public int TrialDifficulty => Stage == 1 ? 13 : 15;
        public string LastTrial { get; private set; } = "";
        public IReadOnlyList<HeroState> Party { get; }
        public IReadOnlyList<string> Log { get; }
        public EnemyState Enemy { get; private set; }
        private bool InDungeon => Phase == RunPhase.Travel || Phase == RunPhase.Combat;
        public bool CanBless => InDungeon && Faith >= BlessCost && WoundedHero() != null;

        // ── Exploration state ────────────────────────────────────────────

        /// <summary>The fork the party is discussing, or null.</summary>
        public ForkDiscussion PendingFork { get; private set; }
        /// <summary>Summary of the most recent fork decision.</summary>
        public string LastFork { get; private set; } = "";
        public int ForkCount { get; private set; }
        public int StepsTaken { get; private set; }
        public int StepsSinceFork { get; private set; } = int.MaxValue;
        /// <summary>Changes whenever the party sees tiles it had not seen before.</summary>
        public int SeenVersion { get; private set; }
        public TilePoint HeroTile(HeroRole role) => heroTiles[(int)role];
        public TilePoint LeaderTile => heroTiles[(int)Leader().Role];
        /// <summary>The party has seen this tile at some point during the stage.</summary>
        public bool IsSeen(TilePoint tile) => Map.Contains(tile) && seen[tile.X, tile.Y];
        /// <summary>A living hero can see this tile right now.</summary>
        public bool IsVisible(TilePoint tile) => Map.Contains(tile) && visible[tile.X, tile.Y];
        public bool IsDefeated(int monster) => defeated[monster];
        public bool IsSpotted(int monster) => spotted[monster];
        public int DefeatedCount => defeated.Count(done => done);
        public int SpottedCount => spotted.Count(done => done);

        public static int Cost(MonsterKind kind)
        {
            switch (kind)
            {
                case MonsterKind.None: return 0;
                case MonsterKind.Goblin: return 1;
                case MonsterKind.Ogre: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static string MonsterName(MonsterKind kind)
        {
            switch (kind)
            {
                case MonsterKind.None: return "빈 길";
                case MonsterKind.Goblin: return "고블린";
                case MonsterKind.Ogre: return "오우거";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        // ── Planning ─────────────────────────────────────────────────────

        public int CountAt(TilePoint tile)
        {
            int count = 0;
            foreach (var placement in placements)
                if (placement.Tile == tile) count++;
            return count;
        }

        /// <summary>Floor tile far enough from the entrance; ignores phase and budget.</summary>
        public bool CanHoldMonster(TilePoint tile) =>
            Map.IsFloor(tile) && Map.WalkDistance(tile) > EntranceClearance;

        public PlacementCheck CheckPlace(TilePoint tile, MonsterKind kind)
        {
            if (Phase != RunPhase.Planning) return PlacementCheck.Locked;
            if (kind != MonsterKind.Goblin && kind != MonsterKind.Ogre) return PlacementCheck.InvalidKind;
            if (!Map.IsFloor(tile)) return PlacementCheck.NotFloor;
            if (!CanHoldMonster(tile)) return PlacementCheck.NearEntrance;
            if (Cost(kind) > Budget) return PlacementCheck.OverBudget;
            return PlacementCheck.Allowed;
        }

        public bool Place(TilePoint tile, MonsterKind kind)
        {
            if (CheckPlace(tile, kind) != PlacementCheck.Allowed) return false;
            placements.Add(new MonsterPlacement(tile, kind));
            defeated.Add(false);
            spotted.Add(false);
            Budget -= Cost(kind);
            return true;
        }

        public PlacementCheck CheckRemove(TilePoint tile)
        {
            if (Phase != RunPhase.Planning) return PlacementCheck.Locked;
            return CountAt(tile) == 0 ? PlacementCheck.NothingToRemove : PlacementCheck.Allowed;
        }

        /// <summary>Takes back the most recently placed monster on the tile and refunds its cost.</summary>
        public bool Remove(TilePoint tile)
        {
            if (CheckRemove(tile) != PlacementCheck.Allowed) return false;
            int index = placements.FindLastIndex(placement => placement.Tile == tile);
            Budget += Cost(placements[index].Kind);
            placements.RemoveAt(index);
            defeated.RemoveAt(index);
            spotted.RemoveAt(index);
            return true;
        }

        public bool Start()
        {
            if (Phase != RunPhase.Planning) return false;
            Phase = RunPhase.Travel;
            log.Add($"{Stage} 스테이지 출발 • 용사들이 던전에 들어섰습니다.");
            Reveal();
            return true;
        }

        private void LoadMap(DungeonMap map)
        {
            Map = map;
            seen = new bool[map.Width, map.Height];
            visible = new bool[map.Width, map.Height];
            for (int i = 0; i < heroTiles.Length; i++) heroTiles[i] = map.Entrance;
            trail.Clear();
            trail.Add(map.Entrance);
            route.Clear();
            routeToBoss = false;
            skipped.Clear();
            PendingFork = null;
            StepsSinceFork = int.MaxValue;
            ReachedBoss = false;
            SeenVersion++;
        }

        // ── Exploration ──────────────────────────────────────────────────

        /// <summary>
        /// Advances the expedition by one tick: plans a way if needed, walks the leader one tile and lets the
        /// others follow in marching order, then updates what the party sees. Returns false when nothing can
        /// happen (not travelling, a fork is being discussed, or an adversity is pending).
        /// </summary>
        public bool Step()
        {
            if (Phase != RunPhase.Travel || PendingAdversity || PendingFork != null) return false;
            if (TryStartEncounter()) return true;
            if (route.Count == 0 && !PlanRoute()) return true;
            if (route.Count == 0) return true;

            var next = route.Dequeue();
            trail.Add(next);
            var marching = MarchOrder.Where(role => party[(int)role].Hp > 0).ToList();
            for (int rank = 0; rank < marching.Count; rank++)
                heroTiles[(int)marching[rank]] = trail[Math.Max(0, trail.Count - 1 - rank)];
            StepsTaken++;
            if (StepsSinceFork != int.MaxValue) StepsSinceFork++;
            bool bossWasSeen = IsSeen(Map.BossTile);
            Reveal();

            // Re-plan once the edge we were heading for has been seen, or as soon as the boss room shows up.
            if (!routeToBoss && (!IsFrontier(routeTarget) || (!bossWasSeen && IsSeen(Map.BossTile))))
                route.Clear();
            TryStartEncounter();
            return true;
        }

        private HeroState Leader()
        {
            foreach (var role in MarchOrder)
                if (party[(int)role].Hp > 0) return party[(int)role];
            return party[(int)HeroRole.Tank];
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
            for (int i = 0; i < placements.Count; i++)
                if (!defeated[i] && !spotted[i] && IsVisible(placements[i].Tile)) spotted[i] = true;
        }

        private bool TryStartEncounter()
        {
            if (Phase != RunPhase.Travel) return false;
            var near = Map.Distances(LeaderTile, Map.IsFloor, EncounterReach);
            for (int i = 0; i < placements.Count; i++)
            {
                if (defeated[i] || !IsVisible(placements[i].Tile) || !near.ContainsKey(placements[i].Tile)) continue;
                StartFight(i);
                return true;
            }
            if (IsVisible(Map.BossTile) && near.ContainsKey(Map.BossTile))
            {
                StartFight(BossIndex);
                return true;
            }
            return false;
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
                int monsters = VisibleMonstersAlong(ways[i]);
                fromEntrance.TryGetValue(ways[i].Path[ways[i].Path.Count - 1], out int depth);
                options.Add(new ForkOption(i, label, ways[i].Path, ways[i].Tiles, monsters, depth));
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

        private int VisibleMonstersAlong(Frontier way)
        {
            int count = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                if (defeated[i] || !IsVisible(placements[i].Tile)) continue;
                var tile = placements[i].Tile;
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
            string line;
            switch (hero.Tags[0])
            {
                case PersonalityTag.Reckless:
                    line = choice.VisibleMonsters > 0 ? $"{choice.Label}에 적이 있어. 정면 돌파다!" : $"{choice.Label}로 깊이 들어가자!";
                    break;
                case PersonalityTag.Cautious:
                    line = hurt ? $"다들 지쳤어요. {choice.Label}로 조심히 가요." :
                        choice.VisibleMonsters == 0 ? $"{choice.Label}은 조용해 보여요." : $"적을 살피며 {choice.Label}로 가죠.";
                    break;
                default:
                    line = $"{choice.Label}이 더 넓게 트였어. 가 보자.";
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
                var proposal = proposals?.FirstOrDefault(p => p != null && p.Role == local.Role);
                bool valid = proposal != null && proposal.Option >= 0 && proposal.Option < fork.Options.Count &&
                             !string.IsNullOrWhiteSpace(proposal.Line);
                var opinion = valid ? proposal : local;
                opinions.Add(opinion);
                var hero = party[(int)opinion.Role];
                hero.LastLine = opinion.Line;
                log.Add($"{hero.Name} → {fork.Options[opinion.Option].Label} “{opinion.Line}”");
            }

            int chosen;
            var summary = new StringBuilder();
            if (opinions.Select(o => o.Option).Distinct().Count() == 1)
            {
                chosen = opinions[0].Option;
                summary.Append($"의견 일치 → {fork.Options[chosen].Label}");
            }
            else
            {
                var rolls = new List<(ForkOpinion Opinion, int Roll, int Total)>();
                foreach (var opinion in opinions)
                {
                    int roll = dice.Next(1, 21);
                    rolls.Add((opinion, roll, roll + party[(int)opinion.Role].Modifier(Ability.Charisma)));
                }
                int top = rolls.Max(r => r.Total);
                var leaders = rolls.Where(r => r.Total == top).ToList();
                int Supporters(int option) => opinions.Count(o => o.Option == option);
                var winner = leaders.OrderByDescending(r => Supporters(r.Opinion.Option))
                    .ThenBy(r => Array.IndexOf(MarchOrder, r.Opinion.Role)).First();
                chosen = winner.Opinion.Option;
                summary.Append("설득 d20 + 매력  ");
                summary.Append(string.Join("  ", rolls.Select(r =>
                {
                    int mod = party[(int)r.Opinion.Role].Modifier(Ability.Charisma);
                    return $"{party[(int)r.Opinion.Role].Name} [{r.Roll}]{(mod >= 0 ? "+" : "")}{mod}={r.Total}";
                })));
                summary.Append($"\n→ {party[(int)winner.Opinion.Role].Name}의 의견대로 {fork.Options[chosen].Label}");
            }
            LastFork = summary.ToString();
            log.Add("결정 • " + LastFork.Replace('\n', ' '));
            ForkCount++;
            StepsSinceFork = 0;
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

        // ── Combat ───────────────────────────────────────────────────────

        private void StartFight(int foe)
        {
            bool later = Stage == 2;
            if (foe == BossIndex)
            {
                Enemy = later
                    ? new EnemyState("마왕 벨자크", 158, 19, 10, 0)
                    : new EnemyState("성문 수호자", 64, 10, 1, 2);
                ReachedBoss = true;
            }
            else if (placements[foe].Kind == MonsterKind.Ogre)
                Enemy = new EnemyState("오우거", later ? 106 : 69, later ? 15 : 10, 2, 8);
            else
                Enemy = new EnemyState("고블린", later ? 73 : 47, later ? 10 : 6, 0, 5);

            FoeIndex = foe;
            route.Clear();
            encounterRound = 0;
            RoundToken++;
            Phase = RunPhase.Combat;
            log.Add($"조우 • {Enemy.Name} 등장!  체력 {Enemy.Hp} / 방어 {Enemy.Armor}");
        }

        public bool ResolveRound(int token, IReadOnlyList<HeroDecision> decisions = null)
        {
            if (Phase != RunPhase.Combat || token != RoundToken || PendingAdversity) return false;
            RoundToken++;
            encounterRound++;
            bool guarded = false;
            foreach (var hero in party)
            {
                if (hero.Hp == 0) continue;
                var intent = ChooseAction(hero, decisions);
                hero.LastAction = intent.Action;
                hero.LastLine = intent.Line ?? "";
                switch (intent.Action)
                {
                    case HeroAction.Guard:
                        guarded = true;
                        log.Add($"{hero.Name} • 방패를 들어 파티를 엄호합니다.");
                        break;
                    case HeroAction.Heal:
                        var ally = party[(int)intent.Target];
                        int before = ally.Hp;
                        ally.Hp = Math.Min(ally.MaxHp, ally.Hp + 7 + Level);
                        log.Add($"{hero.Name} → {ally.Name} • 체력 {ally.Hp - before} 회복");
                        break;
                    default:
                        int damage = Math.Max(0, hero.BaseAttack + 2 * (Level - 1) - Enemy.Armor);
                        Enemy.Hp = Math.Max(0, Enemy.Hp - damage);
                        log.Add($"{hero.Name} → {Enemy.Name} • {(damage == 0 ? "장갑에 막힘" : damage + " 피해")}");
                        break;
                }
                if (Enemy.Hp == 0)
                {
                    FinishEncounter();
                    return true;
                }
            }

            var target = guarded ? party[(int)HeroRole.Tank] : EnemyTarget();
            int strike = Enemy.Attack + (encounterRound - 1) / 3;
            Hurt(target, guarded ? (strike + 1) / 2 : strike, Enemy.Name);
            return true;
        }

        private HeroDecision ChooseAction(HeroState hero, IReadOnlyList<HeroDecision> decisions)
        {
            if (decisions != null)
            {
                foreach (var choice in decisions)
                {
                    if (choice == null || choice.Role != hero.Role) continue;
                    if (choice.Action == HeroAction.Attack ||
                        (choice.Action == HeroAction.Guard && hero.Role == HeroRole.Tank) ||
                        (choice.Action == HeroAction.Heal && hero.Role == HeroRole.Healer && CanHeal(choice.Target)))
                        return choice;
                }
            }

            var wounded = WoundedHero();
            if (hero.Role == HeroRole.Healer && wounded != null && wounded.MaxHp - wounded.Hp >= 6)
                return new HeroDecision(hero.Role, HeroAction.Heal, wounded.Role, "제가 상처를 돌볼게요.");
            if (hero.Role == HeroRole.Tank && wounded != null && wounded.Role != HeroRole.Tank &&
                wounded.Hp * 2 < wounded.MaxHp)
                return new HeroDecision(hero.Role, HeroAction.Guard, hero.Role, "내 뒤로 물러서!");
            return new HeroDecision(hero.Role, HeroAction.Attack, hero.Role,
                hero.Role == HeroRole.RangedDamage ? "사선을 확보했어." : "빈틈을 노린다!");
        }

        private bool CanHeal(HeroRole role)
        {
            int index = (int)role;
            return index >= 0 && index < party.Length && party[index].Hp > 0 &&
                party[index].Hp < party[index].MaxHp;
        }

        private HeroState WoundedHero()
        {
            HeroState lowest = null;
            foreach (var hero in party)
            {
                if (!CanHeal(hero.Role)) continue;
                if (lowest == null || hero.Hp * lowest.MaxHp < lowest.Hp * hero.MaxHp)
                    lowest = hero;
            }
            return lowest;
        }

        private HeroState EnemyTarget()
        {
            // Rotate from the frontline; skip fallen heroes without consuming an enemy turn.
            for (int offset = 0; offset < party.Length; offset++)
            {
                var hero = party[(encounterRound - 1 + offset) % party.Length];
                if (hero.Hp > 0) return hero;
            }
            throw new InvalidOperationException("No living target during combat.");
        }

        private void Hurt(HeroState target, int damage, string source)
        {
            target.Hp = Math.Max(0, target.Hp - damage);
            log.Add($"{source} → {target.Name} • {damage} 피해{(target.Hp == 0 ? " / 쓰러짐" : "")}");
            if (Array.TrueForAll(party, hero => hero.Hp == 0))
            {
                Phase = RunPhase.Defeat;
                Enemy = null;
                pendingTrial = 0;
                PendingFork = null;
                log.Add("파티 전멸 • 원정이 끝났습니다.");
            }
        }

        private void FinishEncounter()
        {
            log.Add($"{Enemy.Name} 격파 • 경험치 +{Enemy.Experience}");
            GainExperience(Enemy.Experience);
            Enemy = null;
            if (FoeIndex != BossIndex)
            {
                defeated[FoeIndex] = true;
                FoeIndex = -1;
                Phase = RunPhase.Travel;
                return;
            }
            FoeIndex = -1;
            if (Stage == 2)
            {
                Phase = RunPhase.Victory;
                log.Add("마왕 토벌 • 살아남은 용사들이 왕국으로 귀환합니다!");
                return;
            }
            Stage++;
            placements.Clear();
            defeated.Clear();
            spotted.Clear();
            LoadMap(DungeonMap.ForStage(Stage));
            Budget = 7;
            TrialsLeft = 2;
            foreach (var hero in party) hero.Hp = hero.MaxHp;
            Phase = RunPhase.Planning;
            log.Add("야영 완료 • 용사들이 회복하고 쓰러진 동료도 복귀했습니다. 마왕성을 준비하세요.");
        }

        private void GainExperience(int amount)
        {
            int previous = Level;
            Experience += amount;
            int gain = Level - previous;
            if (gain == 0) return;
            foreach (var hero in party)
            {
                hero.MaxHp += 3 * gain;
                if (hero.Hp > 0) hero.Hp += 3 * gain;
            }
            log.Add($"파티 레벨 {Level} • 공격력 +{2 * gain} / 최대 체력 +{3 * gain}");
        }

        // ── Interventions ────────────────────────────────────────────────

        public bool Bless()
        {
            if (!CanBless) return false;
            var target = WoundedHero();
            int restored = Math.Min(target.MaxHp - target.Hp, 12 + Level);
            Faith -= BlessCost;
            target.Hp += restored;
            log.Add($"신의 축복 • {target.Name} 체력 +{restored} / 신앙 -{BlessCost}");
            return true;
        }

        public int BeginAdversity()
        {
            if (!InDungeon || PendingAdversity || TrialsLeft == 0) return 0;
            TrialsLeft--;
            pendingTrial = ++nextTrialToken;
            log.Add("낙석! • 용사들이 대응을 선언합니다.");
            return pendingTrial;
        }

        public int TrialModifier(HeroRole role) =>
            party[(int)role].Modifier(HeroTraits.TrialAbility(role)) + (Level - 1) / 2;

        public bool ResolveAdversity(int token, HeroRole role, int roll, string declaration = null)
        {
            if (token == 0 || token != pendingTrial || !InDungeon) return false;
            int index = (int)role;
            var actor = index >= 0 && index < party.Length && party[index].Hp > 0
                ? party[index] : Array.Find(party, hero => hero.Hp > 0);
            int modifier = TrialModifier(actor.Role);
            bool success = D20Check.Succeeds(roll, modifier, TrialDifficulty);
            pendingTrial = 0;
            string ability = HeroTraits.Name(HeroTraits.TrialAbility(actor.Role));
            string fallback;
            switch (actor.Role)
            {
                case HeroRole.Tank: fallback = "방패로 낙석을 막겠습니다."; break;
                case HeroRole.Healer: fallback = "안전한 틈을 찾아 안내할게요."; break;
                case HeroRole.MeleeDamage: fallback = "바위를 쳐내겠다!"; break;
                default: fallback = "바위가 떨어지기 전에 피하자!"; break;
            }
            actor.LastLine = string.IsNullOrWhiteSpace(declaration) ? fallback : declaration;
            string outcome = success ? "성공 • 경험치 +1 / 신앙 +1" : "실패 • 체력 -8";
            LastTrial = $"{actor.Name} “{actor.LastLine}”\nd20 [{roll}] + {ability} {modifier} = {roll + modifier} / 난이도 {TrialDifficulty} → {outcome}";
            log.Add(LastTrial);
            if (success)
            {
                Faith++;
                GainExperience(1);
            }
            else Hurt(actor, 8, "낙석");
            return true;
        }
    }
}
