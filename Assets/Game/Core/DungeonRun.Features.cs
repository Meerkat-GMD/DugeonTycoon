using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>A fixed patrol on the current stage. Members walk its route in single file; the first leads.</summary>
    public sealed class PatrolSquad
    {
        internal PatrolSquad(int index, IReadOnlyList<MonsterKind> members, IReadOnlyList<TilePoint> route)
        {
            Index = index;
            Members = members;
            Route = route;
            Position = members.Count - 1;
        }

        public int Index { get; }
        public IReadOnlyList<MonsterKind> Members { get; }
        /// <summary>Every tile of the loop in walking order; after the last tile the squad steps onto the first again.</summary>
        public IReadOnlyList<TilePoint> Route { get; }
        /// <summary>Where member <paramref name="member"/> stands before departure.</summary>
        public TilePoint StartOf(int member) => Route[Wrap(Members.Count - 1 - member)];
        internal int Position { get; set; }
        internal int MoveReady { get; set; }
        internal TilePoint TileOf(int member) => Route[Wrap(Position - member)];
        private int Wrap(int i) => ((i % Route.Count) + Route.Count) % Route.Count;
    }

    /// <summary>A treasure chest fixed on the current stage's map.</summary>
    public sealed class TreasureChest
    {
        internal TreasureChest(int index, TilePoint tile)
        {
            Index = index;
            Tile = tile;
        }

        public int Index { get; }
        public TilePoint Tile { get; }
        public bool Opened { get; internal set; }
        /// <summary>What it held once opened, else null.</summary>
        public Equipment Item { get; internal set; }
    }

    /// <summary>A spring fixed on the current stage's map. Drinking from it once eases the party's fatigue.</summary>
    public sealed class Spring
    {
        internal Spring(int index, TilePoint tile)
        {
            Index = index;
            Tile = tile;
        }

        public int Index { get; }
        public TilePoint Tile { get; }
        public bool Used { get; internal set; }
        internal bool Passed { get; set; }
    }

    /// <summary>
    /// Stage setup (roster, budget, patrols, chests, springs) and the rules of the fixed map features.
    /// </summary>
    public sealed partial class DungeonRun
    {
        public const int FinalStage = DungeonLayouts.StageCount;
        /// <summary>Patrol routes keep further than this many steps from the entrance. Balance value.</summary>
        public const int PatrolClearance = 8;
        /// <summary>Fatigue steps a spring takes away. From <see cref="BalanceTable"/>.</summary>
        public static int SpringRelief => BalanceTable.Current.springRelief;
        /// <summary>A wanted spring this many steps away or fewer is visited without a discussion. Balance value.</summary>
        public const int FeatureDetour = 4;

        private static readonly MonsterKind[] FullRoster = { MonsterKind.Skeleton, MonsterKind.Ghost, MonsterKind.Zombie, MonsterKind.Archer };
        private readonly List<PatrolSquad> patrols = new List<PatrolSquad>();
        private readonly List<TreasureChest> chests = new List<TreasureChest>();
        private readonly List<Spring> springs = new List<Spring>();
        private bool freshFeature;
        /// <summary>A test run: any monster kind on any stage and no patrols; chests and springs only when <see cref="sandboxFeatures"/>.</summary>
        private bool sandbox;
        private bool sandboxFeatures;

        /// <summary>
        /// For tests that set up their own monsters or only check the maps: the stage maps without patrols, every
        /// monster kind placeable from the first stage, and chests and springs only when <paramref name="features"/>.
        /// <paramref name="map"/> replaces the stage's map, so a draft layout can be checked before it joins the game.
        /// </summary>
        internal static DungeonRun Sandbox(int seed = 20261118, bool features = false, int stage = 1, DungeonMap map = null)
        {
            var run = new DungeonRun(seed) { sandbox = true, sandboxFeatures = features, Stage = stage };
            run.Budget = BudgetFor(stage);
            run.LoadMap(map ?? DungeonMap.ForStage(stage));
            return run;
        }

        /// <summary>The monsters the GM may place on a stage: two on the first, one more on each stage after.</summary>
        public static IReadOnlyList<MonsterKind> RosterFor(int stage)
        {
            if (stage < 1 || stage > FinalStage) throw new ArgumentOutOfRangeException(nameof(stage));
            return Array.AsReadOnly(FullRoster.Take(stage + 1).ToArray());
        }

        public IReadOnlyList<MonsterKind> Roster => RosterFor(Stage);
        public bool InRoster(MonsterKind kind) => Roster.Contains(kind) || (sandbox && Array.IndexOf(FullRoster, kind) >= 0);

        /// <summary>Threat budget a stage opens with. From <see cref="BalanceTable"/>.</summary>
        public static int BudgetFor(int stage) => BalanceTable.ForStage(BalanceTable.Current.budget, stage);

        public IReadOnlyList<PatrolSquad> Patrols => patrols;
        public int PatrolMemberCount => patrols.Sum(squad => squad.Members.Count);
        public IReadOnlyList<TreasureChest> Chests => chests;
        public IReadOnlyList<Spring> Springs => springs;

        /// <summary>Builds the current stage's patrols, chests and springs from the map.</summary>
        private void LoadFeatures()
        {
            patrols.Clear();
            chests.Clear();
            springs.Clear();
            freshFeature = false;
            if (!sandbox)
            {
                var plans = DungeonLayouts.PatrolsForStage(Stage);
                for (int i = 0; i < plans.Count; i++) patrols.Add(new PatrolSquad(i, plans[i].Members, PatrolRoute(plans[i])));
            }
            if (sandbox && !sandboxFeatures) return;
            for (int i = 0; i < Map.Chests.Count; i++) chests.Add(new TreasureChest(i, Map.Chests[i]));
            for (int i = 0; i < Map.Springs.Count; i++) springs.Add(new Spring(i, Map.Springs[i]));
        }

        /// <summary>Tiles a patrol may walk: floor outside the boss room and well clear of the entrance.</summary>
        public bool PatrolWalkable(TilePoint tile) =>
            Map.IsFloor(tile) && !Map.IsBossRoom(tile) && Map.WalkDistance(tile) > PatrolClearance;

        private IReadOnlyList<TilePoint> PatrolRoute(PatrolPlan plan)
        {
            var route = new List<TilePoint>();
            for (int i = 0; i < plan.Waypoints.Count; i++)
            {
                var from = plan.Waypoints[i];
                var to = plan.Waypoints[(i + 1) % plan.Waypoints.Count];
                if (!PatrolWalkable(from)) throw new InvalidOperationException($"Patrol waypoint {from} is off limits.");
                var leg = Map.FindPath(from, to, PatrolWalkable);
                if (leg.Count < 2) throw new InvalidOperationException($"No patrol path from {from} to {to}.");
                for (int j = 0; j < leg.Count - 1; j++) route.Add(leg[j]);
            }
            return route.AsReadOnly();
        }

        /// <summary>Patrol members join the monster list after the placements and before the boss.</summary>
        private void SpawnPatrols()
        {
            foreach (var squad in patrols)
            {
                squad.Position = squad.Members.Count - 1;
                squad.MoveReady = 0;
                for (int k = 0; k < squad.Members.Count; k++)
                {
                    var monster = CreateMonster(monsters.Count, squad.Members[k], squad.StartOf(k));
                    monster.Squad = squad.Index;
                    monster.Mood = MonsterMood.Patrolling;
                    monsters.Add(monster);
                }
            }
        }

        private IEnumerable<MonsterState> MembersOf(PatrolSquad squad) => monsters.Where(m => m.Squad == squad.Index);

        /// <summary>A squad that is still on its rounds steps forward together at its slowest member's pace.</summary>
        private void PatrolTick()
        {
            foreach (var squad in patrols)
            {
                var members = MembersOf(squad).ToList();
                if (members.Count == 0 || members.Any(m => m.Mood != MonsterMood.Patrolling)) continue;
                if (squad.MoveReady > Ticks) continue;
                int pace = members.Max(m => m.MoveTicks);
                squad.Position++;
                squad.MoveReady = Ticks + pace;
                for (int k = 0; k < members.Count; k++)
                {
                    members[k].Tile = squad.TileOf(k);
                    members[k].StepUntil = Ticks + pace;
                }
            }
        }

        /// <summary>When one member notices the party the whole squad joins the hunt.</summary>
        private void RaiseSquad(MonsterState spotter)
        {
            if (!spotter.IsPatrol) return;
            foreach (var mate in monsters)
            {
                if (mate == spotter || mate.Squad != spotter.Squad || !mate.Alive || mate.Mood == MonsterMood.Chasing) continue;
                StartHunt(mate);
            }
        }

        // ── Chests and springs ───────────────────────────────────────────

        /// <summary>Whether the party would go out of its way for a feature it remembers.</summary>
        private bool Wanted(TreasureChest chest) => !chest.Opened && IsSeen(chest.Tile);
        private bool Wanted(Spring spring) => !spring.Used && IsSeen(spring.Tile) && Fatigue != FatigueStage.Rested;

        /// <summary>Called by Reveal: a chest or spring the party sees for the first time makes it rethink its route.</summary>
        private void NoticeFeatures(HashSet<TilePoint> fresh)
        {
            foreach (var chest in chests)
                if (!chest.Opened && fresh.Contains(chest.Tile))
                {
                    route.Clear();
                    log.Add($"발견 • 용사들이 보물 상자를 보았습니다.");
                }
            foreach (var spring in springs)
                if (!spring.Used && fresh.Contains(spring.Tile))
                {
                    freshFeature = true;
                    log.Add($"발견 • 용사들이 샘물을 보았습니다.");
                }
        }

        /// <summary>The leader stepped onto a tile: open a chest there or drink from a spring if the party is tired.</summary>
        private void VisitFeatures(TilePoint tile)
        {
            foreach (var chest in chests)
                if (!chest.Opened && chest.Tile == tile) OpenChest(chest);
            foreach (var spring in springs)
                if (!spring.Used && spring.Tile == tile && Fatigue != FatigueStage.Rested) DrinkSpring(spring);
        }

        private void OpenChest(TreasureChest chest)
        {
            var item = ChestOverride != null ? ChestOverride(chest) : EquipmentCatalog.RollChest(Stage, lootDice);
            chest.Opened = true;
            chest.Item = item;
            BeginSpoils();
            spoils.Add(item);
            drops.Add(item);
            events.Add(new CombatEvent(CombatEventKind.Chest, false, chest.Index, false, -1, drops.Count - 1));
            log.Add($"보물 상자 • 용사들이 상자를 열어 {HeroTraits.Particle(item.Name, "을", "를")} 찾았다 ({item.GradeName} · {item.Stats})");
            route.Clear();
            SettleSpoils();
        }

        private void DrinkSpring(Spring spring)
        {
            int relieved = Math.Min(SpringRelief, FatigueSteps);
            spring.Used = true;
            events.Add(new CombatEvent(CombatEventKind.Spring, false, spring.Index, false, -1, relieved));
            log.Add($"샘물 • 용사들이 샘물을 마시고 숨을 돌렸습니다. 파티 피로 -{relieved}");
            ReduceFatigue(relieved);
        }

        /// <summary>Tests pin what a chest holds. Null uses the chest table.</summary>
        internal Func<TreasureChest, Equipment> ChestOverride { get; set; }

        /// <summary>
        /// Springs the party remembers and would go out of its way for, as ways to argue over.
        /// <paramref name="includePassed"/> also lists those the party argued past before.
        /// </summary>
        private List<(ForkFeature Feature, int Index, IReadOnlyList<TilePoint> Path)> FeatureWays(bool includePassed)
        {
            var ways = new List<(ForkFeature, int, IReadOnlyList<TilePoint>)>();
            var leader = LeaderTile;
            foreach (var spring in springs)
            {
                if (!Wanted(spring) || (spring.Passed && !includePassed)) continue;
                var path = Map.FindPath(leader, spring.Tile, Known);
                if (path.Count >= 2) ways.Add((ForkFeature.Spring, spring.Index, path));
            }
            return ways;
        }

        private void PassFeature(ForkOption option)
        {
            if (option.Feature == ForkFeature.Spring) springs[option.FeatureIndex].Passed = true;
        }
    }
}
