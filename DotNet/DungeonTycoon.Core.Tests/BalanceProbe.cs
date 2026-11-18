using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>
    /// Prints how expeditions go on the numbers in balance.json, for tuning. Not a rule check: run it on purpose, e.g.
    /// <c>dotnet test DotNet/DungeonTycoon.Core.Tests --filter Name=Summary --logger "console;verbosity=detailed"</c>.
    /// Set the BALANCE_SEEDS environment variable to change how many seeds each line uses.
    /// </summary>
    [Explicit("Balance diagnostics; prints, never fails.")]
    public class BalanceProbe
    {
        /// <summary>A sensible GM plan per stage: the whole budget on the roads the party has to take.</summary>
        internal static readonly (int X, int Y, MonsterKind Kind)[][] SteadyPlan =
        {
            new[] { (21, 23, MonsterKind.Skeleton), (6, 11, MonsterKind.Skeleton), (22, 7, MonsterKind.Ghost), (25, 9, MonsterKind.Ghost) },
            // The crypt's corridors every way to the boss passes through.
            new[] { (27, 25, MonsterKind.Zombie), (38, 18, MonsterKind.Ghost), (16, 30, MonsterKind.Skeleton), (7, 24, MonsterKind.Skeleton), (36, 5, MonsterKind.Skeleton) },
            // Both roads into the castle's great hall and the hall's way up to the throne.
            new[] { (26, 12, MonsterKind.Zombie), (21, 14, MonsterKind.Archer), (7, 20, MonsterKind.Ghost), (17, 30, MonsterKind.Skeleton),
                    (26, 24, MonsterKind.Skeleton), (8, 12, MonsterKind.Skeleton) }
        };

        /// <summary>Whole-budget plans per stage: clumped on the main road, or spread like the steady plan.</summary>
        internal static readonly (int Stage, string Name, (int X, int Y, MonsterKind Kind)[] Plan)[] FullBudgets =
        {
            (1, "해골 6 한곳", Enumerable.Repeat((22, 7, MonsterKind.Skeleton), 6).ToArray()),
            (1, "유령 3 한곳", Enumerable.Repeat((22, 7, MonsterKind.Ghost), 3).ToArray()),
            (1, "길목에 흩어", SteadyPlan[0]),
            (2, "좀비 2+해골 2 한곳", new[] { (26, 18, MonsterKind.Zombie), (26, 18, MonsterKind.Zombie), (26, 18, MonsterKind.Skeleton), (26, 18, MonsterKind.Skeleton) }),
            (2, "유령 4 한곳", Enumerable.Repeat((26, 18, MonsterKind.Ghost), 4).ToArray()),
            (2, "길목에 흩어", SteadyPlan[1]),
            (3, "좀비 2+궁수 2 한곳", new[] { (26, 17, MonsterKind.Zombie), (26, 17, MonsterKind.Zombie), (26, 17, MonsterKind.Archer), (26, 17, MonsterKind.Archer) }),
            (3, "궁수 5 한곳", Enumerable.Repeat((26, 17, MonsterKind.Archer), 5).ToArray()),
            (3, "길목에 흩어", SteadyPlan[2])
        };

        internal sealed class StageReport
        {
            public int Stage, LevelAtBoss = -1, BossTicks, Steps, Gear, Fallen, Chests, Springs, Placed, PlacedKilled, PatrolKilled;
            public string HpAtBoss = "", Fatigue = "";
        }

        /// <summary>The default rule-check seed first, then 1, 2, …; BALANCE_SEEDS overrides how many.</summary>
        private static int[] Seeds(int fallback)
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("BALANCE_SEEDS"), out int n) && n > 0 ? n : fallback;
            return new[] { 20261118 }.Concat(Enumerable.Range(1, count - 1)).ToArray();
        }

        /// <summary>Runs each seed on its own thread; runs share nothing but the read-only balance table.</summary>
        private static T[] PerSeed<T>(int[] seeds, Func<int, T> play)
        {
            var results = new T[seeds.Length];
            Parallel.For(0, seeds.Length, i => results[i] = play(seeds[i]));
            return results;
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
                if (run.Phase == RunPhase.StageCleared) run.AdvanceToNextStage();
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

        internal sealed class FightReport
        {
            public bool Arrived;
            public int Lowest = 100, Down;
            public bool Wiped;
            public int CombatTicks;
        }

        /// <summary>
        /// Arrives at the stage the way the steady plan would (trials won, no blessings), lays out a plan and measures the
        /// ordinary fights up to the boss without any help from the GM.
        /// </summary>
        internal static FightReport Fight(int seed, int stage, (int X, int Y, MonsterKind Kind)[] plan)
        {
            var report = new FightReport();
            var run = new DungeonRun(seed);
            while (run.Stage < stage)
            {
                int next = run.Stage + 1;
                foreach (var (x, y, kind) in SteadyPlan[run.Stage - 1]) run.Place(new TilePoint(x, y), kind);
                run.Start();
                for (int step = 0; step < 60000 && !(run.Stage == next && run.PendingLoot == null) && run.Phase != RunPhase.Defeat; step++)
                {
                    if (DungeonRunTests.SettlePrayer(run)) continue;
                    if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                    if (run.AdvanceToNextStage()) continue;
                    if (run.TrialsLeft > 0 && DungeonRunTests.InDungeon(run)) { run.ResolveAdversity(run.BeginAdversity(), HeroRole.Tank, 20); continue; }
                    if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                    else run.Tick();
                }
                if (run.Stage != next || run.Phase != RunPhase.Planning) return report;
            }
            report.Arrived = true;
            foreach (var (x, y, kind) in plan) run.Place(new TilePoint(x, y), kind);
            run.Start();
            var down = new HashSet<HeroRole>();
            for (int step = 0; step < 40000 && DungeonRunTests.InDungeon(run) && !run.FightingBoss && run.Stage == stage; step++)
            {
                if (DungeonRunTests.SettlePrayer(run)) continue;
                if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                if (run.PendingFork != null) { run.ResolveFork(run.PendingFork.Token); continue; }
                run.Tick();
                if (run.Phase == RunPhase.Combat) report.CombatTicks++;
                foreach (var hero in run.Party)
                {
                    report.Lowest = Math.Min(report.Lowest, hero.Hp * 100 / hero.MaxHp);
                    if (hero.Hp == 0) down.Add(hero.Role);
                }
            }
            report.Down = down.Count;
            report.Wiped = run.Phase == RunPhase.Defeat;
            return report;
        }

        private static string Range(IEnumerable<int> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? "-" : list.Min() == list.Max() ? $"{list.Min()}" : $"{list.Min()}~{list.Max()}";
        }

        /// <summary>The short table to read after changing balance.json: who wins, and how hard ordinary fights hit.</summary>
        [Test]
        public void Summary()
        {
            void Say(string line) => TestContext.Out.WriteLine("| " + line);
            var seeds = Seeds(6);
            var problems = BalanceTable.Current.Problems();
            if (problems.Count > 0) Say("balance.json 문제: " + string.Join(" / ", problems));
            Say($"== 원정 {seeds.Length}개 시드 (역경 모두 성공, 신앙 전부 축복) — 목표: 빈 던전은 늘 지고, 길목 배치는 대부분 이긴다");
            foreach (bool plan in new[] { false, true })
            {
                var runs = PerSeed(seeds, seed => Play(seed, plan));
                int wins = runs.Count(r => r.Phase == RunPhase.Victory);
                var finals = runs.Select(r => r.Stages.LastOrDefault()).Where(s => s != null && s.Stage == DungeonRun.FinalStage && s.LevelAtBoss >= 0).ToList();
                string died = string.Join(" ", runs.Where(r => r.Phase == RunPhase.Defeat).Select(r => $"{r.Stages.Last().Stage}스테이지"));
                Say($"{(plan ? "길목 배치" : "빈 던전  ")}: 승리 {wins}/{seeds.Length}  최종 보스 앞 레벨 {Range(finals.Select(s => s.LevelAtBoss))}" +
                                          $"  최종 보스전 {Range(finals.Select(s => s.BossTicks / 10))}초" + (died.Length > 0 ? $"  패배한 곳: {died}" : ""));
            }
            var fightSeeds = seeds.Skip(1).Take(Math.Max(1, seeds.Length - 1)).ToArray();
            Say($"== 일반 전투, 예산 전부 ({fightSeeds.Length}개 시드, 사회자 도움 없음) — 가장 약해진 용사의 체력 %");
            foreach (var (stage, name, plan) in FullBudgets)
            {
                var fights = PerSeed(fightSeeds, seed => Fight(seed, stage, plan)).Where(f => f.Arrived).ToList();
                var lows = fights.Select(f => f.Lowest).OrderBy(v => v).ToList();
                string median = lows.Count == 0 ? "-" : lows[lows.Count / 2].ToString();
                Say($"{stage}스테이지 {name,-12}: 중간값 {median,3}%  범위 {Range(lows),-7}  쓰러짐 {fights.Sum(f => f.Down)}/{fights.Count * 4}  전멸 {fights.Count(f => f.Wiped)}/{fights.Count}");
            }
        }

        [Test]
        public void PrintExpeditions()
        {
            var seeds = Seeds(12);
            foreach (bool plan in new[] { false, true })
            {
                var runs = PerSeed(seeds, seed => Play(seed, plan));
                for (int i = 0; i < seeds.Length; i++)
                {
                    var (phase, stages, run) = runs[i];
                    TestContext.Out.WriteLine($"{(plan ? "plan " : "empty")} seed {seeds[i]}: {phase}, level {run.Level}, faith {run.Faith}");
                    foreach (var s in stages)
                        TestContext.Out.WriteLine($"   stage {s.Stage}: level@boss {s.LevelAtBoss} hp@boss {s.HpAtBoss} gear {s.Gear} " +
                                                  $"boss {s.BossTicks / 10.0:0.#}s steps {s.Steps} fatigue {s.Fatigue} chests {s.Chests} killed {s.PlacedKilled}/{s.Placed}+patrol {s.PatrolKilled} fallen {s.Fallen}");
                    if (phase == RunPhase.Defeat)
                        foreach (var line in run.Log.Reverse().Take(6).Reverse()) TestContext.Out.WriteLine("      " + line);
                }
            }
        }

        [Test]
        public void PrintFullBudgetFights()
        {
            var seeds = Seeds(9).Skip(1).ToArray();
            foreach (var (stage, name, plan) in FullBudgets)
            {
                var fights = PerSeed(seeds, seed => Fight(seed, stage, plan)).Where(f => f.Arrived).ToList();
                TestContext.Out.WriteLine($"stage {stage} {name}: lowest hp% [{string.Join(",", fights.Select(f => f.Lowest))}] " +
                                          $"down {fights.Sum(f => f.Down)}/{fights.Count * 4} wipes {fights.Count(f => f.Wiped)}/{fights.Count} " +
                                          $"combat {fights.Sum(f => f.CombatTicks) / Math.Max(1, fights.Count) / 10.0:0.#}s");
            }
        }
    }
}
