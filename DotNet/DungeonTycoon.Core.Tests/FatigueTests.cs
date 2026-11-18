using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class FatigueTests
    {
        /// <summary>
        /// A fresh run in the planning phase of the given stage, earlier stages cleared with local decisions. The maps
        /// keep their chests and springs; patrols stay out so only the walking is measured.
        /// </summary>
        private static DungeonRun AtStage(int stage)
        {
            var run = DungeonRun.Sandbox(features: true);
            while (run.Stage < stage)
            {
                int next = run.Stage + 1;
                run.Start();
                DungeonRunTests.Drive(run, () => (run.Stage == next && run.PendingLoot == null) || run.Phase == RunPhase.Defeat);
                Assert.That(run.Stage, Is.EqualTo(next));
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Planning));
            }
            return run;
        }

        private static int ToBoss(DungeonRun run, TilePoint tile) => run.Map.FindPath(tile, run.Map.BossTile).Count - 1;

        /// <summary>Uses full map knowledge to pick the way whose edge is nearest the boss.</summary>
        private static Func<IReadOnlyList<ForkOption>, int> Nearest(DungeonRun run) =>
            options => options.OrderBy(o => ToBoss(run, o.Target)).ThenBy(o => o.Index).First().Index;

        /// <summary>Uses full map knowledge to pick the way whose edge is farthest from the boss.</summary>
        private static Func<IReadOnlyList<ForkOption>, int> Farthest(DungeonRun run) =>
            options => options.OrderByDescending(o => ToBoss(run, o.Target)).ThenBy(o => o.Index).First().Index;

        /// <summary>Like <see cref="DungeonRunTests.Drive"/>, but every living hero votes for the way the policy picks.</summary>
        private static void DriveWith(DungeonRun run, Func<IReadOnlyList<ForkOption>, int> pick, Func<bool> until)
        {
            for (int tick = 0; tick < 40000 && !until(); tick++)
            {
                if (DungeonRunTests.SettlePrayer(run)) continue;
                if (run.PendingLoot != null) run.ResolveLoot(run.PendingLoot.Token);
                else if (run.PendingFork != null)
                {
                    int option = pick(run.PendingFork.Options);
                    var votes = run.Party.Where(h => h.Hp > 0).Select(h => new ForkOpinion(h.Role, option, "이쪽이다.")).ToList();
                    run.ResolveFork(run.PendingFork.Token, votes);
                }
                else if (DungeonRunTests.InDungeon(run)) run.Tick();
                else return;
            }
        }

        private static int DeadEnds(DungeonRun run, int fromLog) => run.Log.Skip(fromLog).Count(l => l.StartsWith("막다른 길"));

        [TestCase(1, 52, 114, 156)]
        [TestCase(2, 67, 147, 201)]
        [TestCase(3, 50, 110, 150)]
        public void ThresholdsComeFromTheShortestWalkToTheBossAndResetEachStage(int stage, int shortest, int first, int second)
        {
            var run = AtStage(stage);
            Assert.That(run.FatigueSteps, Is.Zero, "a new stage starts rested");
            Assert.That(run.ShortestRouteSteps, Is.EqualTo(run.Map.FindPath(run.Map.Entrance, run.Map.BossTile).Count - 1));
            Assert.That(run.ShortestRouteSteps, Is.EqualTo(shortest));
            Assert.That(run.FatigueFirstThreshold, Is.EqualTo((int)Math.Round(shortest * DungeonRun.FatigueFirstMultiplier, MidpointRounding.AwayFromZero)));
            Assert.That(run.FatigueSecondThreshold, Is.EqualTo((int)Math.Round(shortest * DungeonRun.FatigueSecondMultiplier, MidpointRounding.AwayFromZero)));
            Assert.That(run.FatigueFirstThreshold, Is.EqualTo(first));
            Assert.That(run.FatigueSecondThreshold, Is.EqualTo(second));

            run.SetFatigueSteps(17);
            Assert.That(run.Start(), Is.True);
            Assert.That(run.FatigueSteps, Is.Zero, "departure resets fatigue");
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Rested));
        }

        [Test]
        public void ClearingAStageLeavesTheWearinessBehind()
        {
            var run = new DungeonRun();
            run.Start();
            DungeonRunTests.Drive(run, () => run.FightingBoss);
            Assert.That(run.FatigueSteps, Is.Positive);
            DungeonRunTests.Drive(run, () => run.Stage == 2);
            Assert.That(run.Stage, Is.EqualTo(2));
            Assert.That(run.FatigueSteps, Is.Zero);
        }

        [Test]
        public void OnlyTheLeadersExploringStepsTire()
        {
            // Isolate walking fatigue from springs and merchant consumables.
            var run = DungeonRun.Sandbox();
            foreach (var tile in DungeonRunTests.PlaceableTiles(run).Where((_, i) => i % 40 == 0).Take(3)) run.Place(tile, MonsterKind.Skeleton);
            run.Start();
            int steps = run.StepsTaken, fatigue = run.FatigueSteps;
            bool fought = false;
            DungeonRunTests.Drive(run, () =>
            {
                if (run.Stage != 1 || !DungeonRunTests.InDungeon(run)) return true;
                if (run.Phase == RunPhase.Combat)
                {
                    fought = true;
                    Assert.That(run.FatigueSteps, Is.EqualTo(fatigue), "no fatigue while fighting");
                }
                Assert.That(run.FatigueSteps - fatigue, Is.EqualTo(run.StepsTaken - steps));
                steps = run.StepsTaken;
                fatigue = run.FatigueSteps;
                return false;
            });
            Assert.That(fought, Is.True);
            Assert.That(fatigue, Is.Positive);
        }

        [Test]
        public void ForkDebatesAndAdversitiesDoNotTire()
        {
            var run = new DungeonRun();
            run.Start();
            // The first fork is right at the entrance; walk on to the next one.
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            run.ResolveFork(run.PendingFork.Token);
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            int steps = run.FatigueSteps;
            Assert.That(steps, Is.EqualTo(run.StepsTaken));
            Assert.That(steps, Is.Positive);
            for (int i = 0; i < 30; i++) Assert.That(run.Tick(), Is.False);
            Assert.That(run.FatigueSteps, Is.EqualTo(steps), "the party stands still while it argues");
            run.ResolveFork(run.PendingFork.Token);

            int ticket = run.BeginAdversity();
            for (int i = 0; i < 30; i++) Assert.That(run.Tick(), Is.False);
            Assert.That(run.FatigueSteps, Is.EqualTo(steps), "nor while the dice are out");
            run.ResolveAdversity(ticket, HeroRole.Tank, 20);
            DungeonRunTests.Drive(run, () => run.FatigueSteps > steps);
            Assert.That(run.FatigueSteps, Is.EqualTo(steps + 1));
        }

        [Test]
        public void TheStageRisesPastEachThresholdAndIsAnnounced()
        {
            var run = new DungeonRun();
            run.Start();
            run.SetFatigueSteps(run.FatigueFirstThreshold);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Rested), "the first threshold itself is still rested");
            DungeonRunTests.Drive(run, () => run.FatigueSteps > run.FatigueFirstThreshold);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Tired));
            Assert.That(run.Log.Count(l => l.StartsWith("피로 • 파티가 지쳤다")), Is.EqualTo(1));

            run.SetFatigueSteps(run.FatigueSecondThreshold);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Tired));
            DungeonRunTests.Drive(run, () => run.FatigueSteps > run.FatigueSecondThreshold);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Exhausted));
            Assert.That(run.Log.Count(l => l.StartsWith("피로 • 탈진!")), Is.EqualTo(1));
            Assert.That(DungeonRun.FatigueName(run.Fatigue), Is.EqualTo("탈진"));
            Assert.That(DungeonRun.FatigueName(FatigueStage.Tired), Is.EqualTo("지침"));
            Assert.That(DungeonRun.FatigueName(FatigueStage.Rested), Is.EqualTo("여유"));
        }

        [Test]
        public void TiredHeroesHitForLessEvenInCombat()
        {
            var run = DungeonRunTests.RunInCombat();
            foreach (var monster in run.Monsters) monster.Hp = 10000;
            var tank = run.Party[(int)HeroRole.Tank];
            // The blow plus the target's armour, so blows on the skeleton and on the boss compare alike.
            int NextTankBlow()
            {
                int amount = -1, armor = 0;
                DungeonRunTests.Drive(run, () =>
                {
                    var blow = run.TakeEvents().FirstOrDefault(e =>
                        e.Kind == CombatEventKind.Attack && e.FromHero && e.Source == (int)HeroRole.Tank);
                    if (blow == null) return run.Phase != RunPhase.Combat;
                    var target = run.Monsters.Single(m => m.Id == blow.Target);
                    amount = blow.Amount;
                    armor = target.Armor;
                    int penalty = run.Fatigue == FatigueStage.Rested ? 0 : DungeonRun.TiredAttackPenalty;
                    Assert.That(amount, Is.EqualTo(DungeonRun.Mitigate(tank.Attack + DungeonRun.LevelAttackGain * (run.Level - 1) - penalty, target.Armor)));
                    return true;
                });
                Assert.That(amount, Is.Positive, "the tank should have struck through the armour");
                return amount + armor;
            }

            run.SetFatigueSteps(0);
            run.TakeEvents();
            int rested = NextTankBlow();
            run.SetFatigueSteps(run.FatigueFirstThreshold + 1);
            int tired = NextTankBlow();
            Assert.That(rested, Is.GreaterThan(DungeonRun.TiredAttackPenalty));
            Assert.That(tired, Is.EqualTo(rested - DungeonRun.TiredAttackPenalty));
            run.SetFatigueSteps(run.FatigueSecondThreshold + 1);
            Assert.That(NextTankBlow(), Is.EqualTo(tired), "exhaustion keeps the same attack penalty");
        }

        [Test]
        public void ExhaustionWearsHeroesDownEveryFewStepsButNeverFellsThem()
        {
            var run = new DungeonRun();
            run.Start();
            int second = run.FatigueSecondThreshold;
            run.SetFatigueSteps(second);
            var tank = run.Party[(int)HeroRole.Tank];
            tank.Hp = 2;
            var before = run.Party.Select(h => h.Hp).ToArray();

            DungeonRunTests.Drive(run, () => run.FatigueSteps == second + DungeonRun.ExhaustedDrainSteps - 1);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Exhausted));
            Assert.That(run.Party.Select(h => h.Hp), Is.EqualTo(before), "no drain before the interval is walked");

            DungeonRunTests.Drive(run, () => run.FatigueSteps == second + DungeonRun.ExhaustedDrainSteps);
            Assert.That(tank.Hp, Is.EqualTo(1), "drained to 1, not below");
            for (int i = 1; i < run.Party.Count; i++)
                Assert.That(run.Party[i].Hp, Is.EqualTo(before[i] - DungeonRun.ExhaustedDrain), run.Party[i].Name);
            Assert.That(run.Log.Count(l => l.StartsWith("탈진 •")), Is.EqualTo(1));

            DungeonRunTests.Drive(run, () => run.FatigueSteps == second + 3 * DungeonRun.ExhaustedDrainSteps);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Travel));
            Assert.That(tank.Hp, Is.EqualTo(1), "exhaustion never fells a hero");
            for (int i = 1; i < run.Party.Count; i++)
                Assert.That(run.Party[i].Hp, Is.EqualTo(before[i] - 3 * DungeonRun.ExhaustedDrain), run.Party[i].Name);
        }

        [Test]
        public void TheGmEasesFatigueForFaith()
        {
            var run = new DungeonRun();
            Assert.That(run.CanRelieveFatigue, Is.False, "not before departure");
            run.SetFaith(DungeonRun.BlessCost);
            run.Start();
            Assert.That(run.CanRelieveFatigue, Is.False, "nothing to ease yet");
            Assert.That(run.RelieveFatigue(), Is.False);

            // Works while the party is stopped at a fork, the way a blessing does.
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            run.SetFatigueSteps(run.FatigueFirstThreshold + 10);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Tired));
            Assert.That(run.CanRelieveFatigue, Is.True);
            Assert.That(run.RelieveFatigue(), Is.True);
            Assert.That(run.Faith, Is.Zero);
            Assert.That(run.FatigueSteps, Is.EqualTo(run.FatigueFirstThreshold + 10 - DungeonRun.FatigueRelief));
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Rested), "below the threshold the stage drops");
            Assert.That(run.Log.Any(l => l.StartsWith("신의 휴식 • 파티 피로 -30 / 신앙 -1")), Is.True);

            int steps = run.FatigueSteps;
            Assert.That(run.CanRelieveFatigue, Is.False, "no faith left");
            Assert.That(run.RelieveFatigue(), Is.False);
            Assert.That(run.FatigueSteps, Is.EqualTo(steps));

            run.ResolveFork(run.PendingFork.Token);
            run.SetFaith(DungeonRun.BlessCost);
            run.SetFatigueSteps(12);
            Assert.That(run.RelieveFatigue(), Is.True);
            Assert.That(run.FatigueSteps, Is.Zero, "never below zero");
        }

        [Test]
        public void ARestingMiracleEasesFatigueWithoutFaith()
        {
            var run = new DungeonRun();
            run.Start();
            run.SetFatigueSteps(run.FatigueSecondThreshold + 1);
            run.ReduceFatigue(run.FatigueSecondThreshold + 1 - run.FatigueFirstThreshold);
            Assert.That(run.Fatigue, Is.EqualTo(FatigueStage.Rested));
            Assert.That(run.Faith, Is.EqualTo(3));
            run.ReduceFatigue(1000);
            Assert.That(run.FatigueSteps, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void APartyThatHeadsForTheBossStaysRestedButOneThatWandersTires(int stage)
        {
            var direct = AtStage(stage);
            direct.Start();
            int from = direct.Log.Count;
            DriveWith(direct, Nearest(direct), () => direct.FightingBoss || !DungeonRunTests.InDungeon(direct));
            TestContext.Out.WriteLine($"stage {stage} direct: {direct.FatigueSteps} steps / thresholds {direct.FatigueFirstThreshold}, {direct.FatigueSecondThreshold}");
            Assert.That(direct.FightingBoss, Is.True);
            Assert.That(DeadEnds(direct, from), Is.Zero);
            if (!direct.Chests.Any(chest => chest.Opened))
                Assert.That(direct.FatigueSteps, Is.LessThanOrEqualTo(direct.ShortestRouteSteps * 5 / 4), "the direct way, peeks included, stays near the shortest walk");
            Assert.That(direct.Fatigue, Is.EqualTo(FatigueStage.Rested));
            Assert.That(direct.Log.Skip(from).Any(l => l.StartsWith("피로")), Is.False);

            var lost = AtStage(stage);
            lost.Start();
            from = lost.Log.Count;
            DriveWith(lost, Farthest(lost), () => lost.FightingBoss || !DungeonRunTests.InDungeon(lost));
            TestContext.Out.WriteLine($"stage {stage} lost: {lost.FatigueSteps} steps, {DeadEnds(lost, from)} dead ends");
            Assert.That(lost.FightingBoss, Is.True);
            // Stage 1 has two dead ends; the crypt and the castle mostly make a lost party walk their long loops.
            Assert.That(DeadEnds(lost, from), Is.GreaterThanOrEqualTo(stage == 1 ? 2 : 1));
            Assert.That(lost.Fatigue, Is.Not.EqualTo(FatigueStage.Rested));
        }

        [Test]
        public void FatigueColoursTheForkLinesButNotTheVotes()
        {
            ForkDiscussion ForkAt(Func<DungeonRun, int> steps, FatigueStage expected)
            {
                var run = new DungeonRun();
                run.Start();
                run.SetFatigueSteps(steps(run));
                // Grumbling has its own lines; this test is about the calm ones.
                run.GrumbleOverride = _ => false;
                Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
                Assert.That(run.Fatigue, Is.EqualTo(expected));
                return run.PendingFork;
            }
            var rested = ForkAt(_ => 0, FatigueStage.Rested);
            var tired = ForkAt(run => run.FatigueFirstThreshold + 1, FatigueStage.Tired);
            var exhausted = ForkAt(run => run.FatigueSecondThreshold + 1, FatigueStage.Exhausted);
            foreach (var fork in new[] { tired, exhausted })
            {
                Assert.That(fork.Options.Select(o => o.Label), Is.EqualTo(rested.Options.Select(o => o.Label)));
                Assert.That(fork.LocalOpinions.Select(o => (o.Role, o.Option)), Is.EqualTo(rested.LocalOpinions.Select(o => (o.Role, o.Option))),
                    "fatigue does not change which way a hero argues for");
            }
            for (int i = 0; i < rested.LocalOpinions.Count; i++)
            {
                Assert.That(tired.LocalOpinions[i].Line, Is.Not.EqualTo(rested.LocalOpinions[i].Line));
                Assert.That(exhausted.LocalOpinions[i].Line, Is.Not.EqualTo(rested.LocalOpinions[i].Line));
                Assert.That(exhausted.LocalOpinions[i].Line, Is.Not.EqualTo(tired.LocalOpinions[i].Line));
            }
        }
    }
}
