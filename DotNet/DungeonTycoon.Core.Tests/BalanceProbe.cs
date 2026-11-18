using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>
    /// Prints how whole expeditions go with the default formation, for tuning monster numbers. Not a rule check:
    /// run it on purpose with <c>--filter BalanceProbe</c>.
    /// </summary>
    [Explicit("Balance diagnostics; prints, never fails.")]
    public class BalanceProbe
    {
        /// <summary>A sensible GM plan per stage: monsters along the main roads, spending the whole budget.</summary>
        internal static readonly (int X, int Y, MonsterKind Kind)[][] SteadyPlan =
        {
            new[] { (21, 23, MonsterKind.Skeleton), (6, 11, MonsterKind.Skeleton), (22, 7, MonsterKind.Ghost), (25, 9, MonsterKind.Ghost) },
            // The crypt's corridors every way to the boss passes through.
            new[] { (27, 25, MonsterKind.Zombie), (38, 18, MonsterKind.Ghost), (16, 30, MonsterKind.Skeleton), (7, 24, MonsterKind.Skeleton), (36, 5, MonsterKind.Skeleton) },
            // Both roads into the castle's great hall and the hall's way up to the throne.
            new[] { (26, 12, MonsterKind.Zombie), (21, 14, MonsterKind.Archer), (7, 20, MonsterKind.Ghost), (17, 30, MonsterKind.Skeleton),
                    (26, 24, MonsterKind.Skeleton), (8, 12, MonsterKind.Skeleton) }
        };

        internal sealed class StageReport
        {
            public int Stage, LevelAtBoss = -1, BossTicks, Steps, Gear, Fallen, Chests, Springs, Placed, PlacedKilled, PatrolKilled;
            public string HpAtBoss = "", Fatigue = "";
        }

        /// <summary>Plays a whole expedition on local decisions, spending trials and blessings like the rule check does.</summary>
        internal static (RunPhase Phase, List<StageReport> Stages, DungeonRun Run) Play(int seed, bool plan, bool bless = true)
        {
            var run = new DungeonRun(seed);
            var reports = new List<StageReport>();
            StageReport current = null;
            int bossStart = 0;
            for (int step = 0; step < 120000; step++)
            {
                if (run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat) break;
                if (DungeonRunTests.SettlePrayer(run)) continue;
                if (run.PendingLoot != null)
                {
                    run.ResolveLoot(run.PendingLoot.Token);
                    continue;
                }
                if (run.Phase == RunPhase.Planning)
                {
                    if (plan)
                        foreach (var (x, y, kind) in SteadyPlan[run.Stage - 1])
                            run.Place(new TilePoint(x, y), kind);
                    run.Start();
                    current = new StageReport { Stage = run.Stage };
                    reports.Add(current);
                }
                if (run.TrialsLeft == 2 || (run.TrialsLeft == 1 && run.FightingBoss))
                    run.ResolveAdversity(run.BeginAdversity(), HeroRole.Tank, 20);
                if (bless) run.Bless();
                if (run.FightingBoss && current.LevelAtBoss < 0)
                {
                    current.LevelAtBoss = run.Level;
                    current.HpAtBoss = string.Join("/", run.Party.Select(h => h.Hp + ":" + h.MaxHp));
                    current.Gear = run.Party.Count(h => h.Weapon != null) + run.Party.Count(h => h.BodyArmor != null);
                    current.Fatigue = DungeonRun.FatigueName(run.Fatigue);
                    bossStart = run.Ticks;
                }
                int stage = run.Stage;
                if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                else run.Tick();
                if (run.Stage != stage) continue;
                if (current.LevelAtBoss >= 0) current.BossTicks = run.Ticks - bossStart;
                current.Steps = run.StepsTaken;
                current.Fallen = run.Party.Count(h => h.Hp == 0);
                current.Chests = run.Chests.Count(c => c.Opened);
                current.Springs = run.Springs.Count(s => s.Used);
                current.Placed = run.Placements.Count;
                current.PlacedKilled = run.Monsters.Count(m => !m.IsBoss && !m.IsPatrol && !m.Alive);
                current.PatrolKilled = run.Monsters.Count(m => m.IsPatrol && !m.Alive);
            }
            return (run.Phase, reports, run);
        }

        [Test]
        public void PrintExpeditions()
        {
            foreach (bool plan in new[] { false, true })
            foreach (int seed in new[] { 20261118, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })
            {
                var (phase, stages, run) = Play(seed, plan);
                TestContext.Out.WriteLine($"{(plan ? "plan " : "empty")} seed {seed}: {phase}, level {run.Level}, faith {run.Faith}");
                foreach (var s in stages)
                    TestContext.Out.WriteLine($"   stage {s.Stage}: level@boss {s.LevelAtBoss} hp@boss {s.HpAtBoss} gear {s.Gear} " +
                                              $"boss {s.BossTicks / 10.0:0.#}s steps {s.Steps} fatigue {s.Fatigue} chests {s.Chests} killed {s.PlacedKilled}/{s.Placed}+patrol {s.PatrolKilled} fallen {s.Fallen}");
                if (phase == RunPhase.Defeat)
                    foreach (var line in run.Log.Reverse().Take(6).Reverse()) TestContext.Out.WriteLine("      " + line);
            }
        }
        /// <summary>Stage-1 plans that spend the whole budget, for judging how dangerous ordinary fights are.</summary>
        internal static readonly (string Name, (int X, int Y, MonsterKind Kind)[] Plan)[] StageOneBudgets =
        {
            ("skeleton x6 clumped", Enumerable.Repeat((22, 7, MonsterKind.Skeleton), 6).ToArray()),
            ("ghost x3 clumped", Enumerable.Repeat((22, 7, MonsterKind.Ghost), 3).ToArray()),
            ("2 ghost + 2 skel clumped", new[] { (22, 7, MonsterKind.Ghost), (22, 7, MonsterKind.Ghost), (22, 7, MonsterKind.Skeleton), (22, 7, MonsterKind.Skeleton) }),
            ("steady spread", SteadyPlan[0])
        };

        [Test]
        public void PrintStageOneFights()
        {
            foreach (var (name, plan) in StageOneBudgets)
            {
                var lows = new List<int>();
                int falls = 0, wipes = 0, combatTicks = 0;
                for (int seed = 1; seed <= 8; seed++)
                {
                    var run = new DungeonRun(seed);
                    foreach (var (x, y, kind) in plan) Assert.That(run.Place(new TilePoint(x, y), kind), Is.True);
                    run.Start();
                    int low = 100;
                    var down = new HashSet<HeroRole>();
                    for (int step = 0; step < 30000 && DungeonRunTests.InDungeon(run) && !run.FightingBoss && run.Stage == 1; step++)
                    {
                        if (DungeonRunTests.SettlePrayer(run)) continue;
                        if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                        if (run.PendingFork != null) { run.ResolveFork(run.PendingFork.Token); continue; }
                        run.Tick();
                        if (run.Phase == RunPhase.Combat) combatTicks++;
                        foreach (var hero in run.Party)
                        {
                            low = Math.Min(low, hero.Hp * 100 / hero.MaxHp);
                            if (hero.Hp == 0) down.Add(hero.Role);
                        }
                    }
                    lows.Add(low);
                    falls += down.Count;
                    if (run.Phase == RunPhase.Defeat) wipes++;
                }
                TestContext.Out.WriteLine($"{name}: lowest hero hp% per seed [{string.Join(",", lows)}], heroes down {falls}/32, wipes {wipes}/8, combat {combatTicks / 80.0:0.#}s per run");
            }
        }
        /// <summary>Whole-budget plans per stage: clumped on the main road, or spread like the steady plan.</summary>
        internal static readonly (int Stage, string Name, (int X, int Y, MonsterKind Kind)[] Plan)[] FullBudgets =
        {
            (1, "skeleton x6 clumped", Enumerable.Repeat((22, 7, MonsterKind.Skeleton), 6).ToArray()),
            (1, "ghost x3 clumped", Enumerable.Repeat((22, 7, MonsterKind.Ghost), 3).ToArray()),
            (1, "steady spread", SteadyPlan[0]),
            (2, "zombie x2 + skel x2 clumped", new[] { (26, 18, MonsterKind.Zombie), (26, 18, MonsterKind.Zombie), (26, 18, MonsterKind.Skeleton), (26, 18, MonsterKind.Skeleton) }),
            (2, "ghost x4 clumped", Enumerable.Repeat((26, 18, MonsterKind.Ghost), 4).ToArray()),
            (2, "steady spread", SteadyPlan[1]),
            (3, "zombie x2 + archer x2 clumped", new[] { (26, 17, MonsterKind.Zombie), (26, 17, MonsterKind.Zombie), (26, 17, MonsterKind.Archer), (26, 17, MonsterKind.Archer) }),
            (3, "archer x5 clumped", Enumerable.Repeat((26, 17, MonsterKind.Archer), 5).ToArray()),
            (3, "steady spread", SteadyPlan[2])
        };

        /// <summary>
        /// Arrives at each stage the way a steady plan would (trials won, no blessings), then lays out a whole-budget plan
        /// and measures the ordinary fights up to the boss without any help from the GM.
        /// </summary>
        [Test]
        public void PrintFullBudgetFights()
        {
            foreach (var (stage, name, plan) in FullBudgets)
            {
                var lows = new List<int>();
                int falls = 0, wipes = 0, combatTicks = 0, seeds = 0;
                for (int seed = 1; seed <= 8; seed++)
                {
                    var run = new DungeonRun(seed);
                    bool arrived = true;
                    while (run.Stage < stage && arrived)
                    {
                        int next = run.Stage + 1;
                        foreach (var (x, y, kind) in SteadyPlan[run.Stage - 1]) run.Place(new TilePoint(x, y), kind);
                        run.Start();
                        for (int step = 0; step < 60000 && !(run.Stage == next && run.PendingLoot == null) && run.Phase != RunPhase.Defeat; step++)
                        {
                            if (DungeonRunTests.SettlePrayer(run)) continue;
                            if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                            if (run.TrialsLeft > 0 && DungeonRunTests.InDungeon(run)) { run.ResolveAdversity(run.BeginAdversity(), HeroRole.Tank, 20); continue; }
                            if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                            else run.Tick();
                        }
                        arrived = run.Stage == next && run.Phase == RunPhase.Planning;
                    }
                    if (!arrived) continue;
                    seeds++;
                    foreach (var (x, y, kind) in plan) Assert.That(run.Place(new TilePoint(x, y), kind), Is.True, name);
                    run.Start();
                    int low = 100;
                    var down = new HashSet<HeroRole>();
                    for (int step = 0; step < 40000 && DungeonRunTests.InDungeon(run) && !run.FightingBoss && run.Stage == stage; step++)
                    {
                        if (DungeonRunTests.SettlePrayer(run)) continue;
                        if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                        if (run.PendingFork != null) { run.ResolveFork(run.PendingFork.Token); continue; }
                        run.Tick();
                        if (run.Phase == RunPhase.Combat) combatTicks++;
                        foreach (var hero in run.Party)
                        {
                            low = Math.Min(low, hero.Hp * 100 / hero.MaxHp);
                            if (hero.Hp == 0) down.Add(hero.Role);
                        }
                    }
                    lows.Add(low);
                    falls += down.Count;
                    if (run.Phase == RunPhase.Defeat) wipes++;
                }
                TestContext.Out.WriteLine($"stage {stage} {name}: lowest hp% [{string.Join(",", lows)}] down {falls}/{seeds * 4} wipes {wipes}/{seeds} combat {combatTicks / Math.Max(1, seeds) / 10.0:0.#}s");
            }
        }
    }
}