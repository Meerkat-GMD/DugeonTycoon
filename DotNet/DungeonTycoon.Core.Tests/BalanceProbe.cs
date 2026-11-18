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
            new[] { (26, 18, MonsterKind.Zombie), (27, 30, MonsterKind.Ghost), (45, 18, MonsterKind.Skeleton), (26, 5, MonsterKind.Skeleton), (8, 18, MonsterKind.Skeleton) },
            new[] { (26, 17, MonsterKind.Zombie), (30, 14, MonsterKind.Archer), (8, 14, MonsterKind.Ghost), (26, 30, MonsterKind.Skeleton),
                    (7, 22, MonsterKind.Skeleton), (40, 18, MonsterKind.Skeleton) }
        };

        internal sealed class StageReport
        {
            public int Stage, LevelAtBoss = -1, BossTicks, Steps, Gear, Fallen, Chests, Springs;
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
                                              $"boss {s.BossTicks / 10.0:0.#}s steps {s.Steps} fatigue {s.Fatigue} chests {s.Chests} springs {s.Springs} fallen {s.Fallen}");
                if (phase == RunPhase.Defeat)
                    foreach (var line in run.Log.Reverse().Take(6).Reverse()) TestContext.Out.WriteLine("      " + line);
            }
        }
    }
}