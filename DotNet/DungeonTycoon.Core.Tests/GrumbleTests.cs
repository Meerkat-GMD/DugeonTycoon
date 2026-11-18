using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>Worn-out heroes answer negatively: they complain, back no oracle, pass on armour and refuse to pray.</summary>
    public class GrumbleTests
    {
        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];

        /// <summary>Departs, wears the party down to the given stage and walks to the first fork with the grumblers pinned.</summary>
        private static DungeonRun AtFirstFork(FatigueStage stage, Func<HeroRole, bool> grumbles)
        {
            var run = DungeonRun.Sandbox();
            run.Start();
            Tire(run, stage);
            run.GrumbleOverride = grumbles;
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            Assert.That(run.Fatigue, Is.EqualTo(stage));
            return run;
        }

        private static void Tire(DungeonRun run, FatigueStage stage) =>
            run.SetFatigueSteps(stage == FatigueStage.Exhausted ? run.FatigueSecondThreshold + 1 :
                stage == FatigueStage.Tired ? run.FatigueFirstThreshold + 1 : 0);

        [TestCase(FatigueStage.Rested, 0)]
        [TestCase(FatigueStage.Tired, DungeonRun.TiredGrumblePercent)]
        [TestCase(FatigueStage.Exhausted, DungeonRun.ExhaustedGrumblePercent)]
        public void TheChanceToGrumbleRisesWithFatigue(FatigueStage stage, int percent)
        {
            var run = DungeonRun.Sandbox();
            run.Start();
            Tire(run, stage);
            Assert.That(run.GrumbleChance, Is.EqualTo(percent));
        }

        [Test]
        public void ARestedPartyNeverGrumbles()
        {
            var run = AtFirstFork(FatigueStage.Rested, _ => true);
            Assert.That(run.PendingFork.Grumbling, Is.Empty, "nothing is rolled while rested");
            Assert.That(run.Log.Any(line => line.Contains("투덜")), Is.False);
        }

        [Test]
        public void MoreHeroesGrumbleWhenExhaustedThanWhenTiredAndTheSameSeedGrumblesTheSame()
        {
            // Real dice: count grumblers over many seeds; the shares should sit near 35% and 70%.
            double Share(FatigueStage stage)
            {
                int grumblers = 0, heroes = 0;
                for (int seed = 1; seed <= 60; seed++)
                {
                    var run = DungeonRun.Sandbox(seed);
                    run.Start();
                    Tire(run, stage);
                    Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
                    grumblers += run.PendingFork.Grumbling.Count;
                    heroes += run.PendingFork.LocalOpinions.Count;
                }
                return (double)grumblers / heroes;
            }
            double tired = Share(FatigueStage.Tired), exhausted = Share(FatigueStage.Exhausted);
            TestContext.Out.WriteLine($"tired {tired:P0}, exhausted {exhausted:P0}");
            Assert.That(tired, Is.InRange(.25, .45));
            Assert.That(exhausted, Is.InRange(.6, .8));

            ForkDiscussion Fork()
            {
                var run = DungeonRun.Sandbox(7);
                run.Start();
                Tire(run, FatigueStage.Exhausted);
                Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
                return run.PendingFork;
            }
            Assert.That(Fork().Grumbling, Is.EqualTo(Fork().Grumbling), "grumbling comes from the seeded dice");
        }

        [Test]
        public void AGrumblingHeroArguesForTheSameWaySourlyAndIsMarked()
        {
            var calm = AtFirstFork(FatigueStage.Tired, _ => false).PendingFork;
            var run = AtFirstFork(FatigueStage.Tired, role => role == HeroRole.Tank);
            var fork = run.PendingFork;
            Assert.That(fork.Grumbling, Is.EqualTo(new[] { HeroRole.Tank }));
            Assert.That(fork.LocalOpinions.Select(o => (o.Role, o.Option)), Is.EqualTo(calm.LocalOpinions.Select(o => (o.Role, o.Option))),
                "grumbling changes the words, not the vote");
            var tank = fork.LocalOpinions.Single(o => o.Role == HeroRole.Tank);
            Assert.That(tank.Line, Is.EqualTo(DialogueLines.GrumbleOpinion(Hero(run, HeroRole.Tank).Voice, fork.Options[tank.Option].Label, false)));
            Assert.That(run.Log.Any(line => line.StartsWith("피로 •") && line.Contains(Hero(run, HeroRole.Tank).Name) && line.Contains("투덜")), Is.True);

            run.HearFork(fork.Token);
            var marked = run.LastScene.Where(b => b.Mark == DialogueLines.Grumbles).Select(b => b.Speaker).ToList();
            Assert.That(marked, Is.EqualTo(new HeroRole?[] { HeroRole.Tank }));
        }

        [Test]
        public void AGrumblerKeepsTheAisWayButComplainsInsteadOfTheAisCheerfulLine()
        {
            var run = AtFirstFork(FatigueStage.Exhausted, role => role == HeroRole.Healer);
            var fork = run.PendingFork;
            var votes = run.Party.Select(h => new ForkOpinion(h.Role, 1, "좋아, 신나게 가 보자!")).ToList();
            var heard = run.HearFork(fork.Token, votes);
            var healer = heard.Single(o => o.Role == HeroRole.Healer);
            Assert.That(healer.Option, Is.EqualTo(1), "the AI still picks the way");
            Assert.That(healer.Line, Is.EqualTo(DialogueLines.GrumbleOpinion(Hero(run, HeroRole.Healer).Voice, fork.Options[1].Label, true)));
            Assert.That(heard.Where(o => o.Role != HeroRole.Healer).Select(o => o.Line), Is.All.EqualTo("좋아, 신나게 가 보자!"));
        }

        [Test]
        public void AGrumblingHeroOfFirmBeliefRollsAgainstTheOracleInsteadOfYielding()
        {
            var run = AtFirstFork(FatigueStage.Exhausted, role => role == HeroRole.Tank);
            run.SetFaith(3);
            Hero(run, HeroRole.Tank).Belief = Belief.Firm;
            Hero(run, HeroRole.Healer).Belief = Belief.Firm;
            var fork = run.PendingFork;
            var votes = run.Party.Select(h => new ForkOpinion(h.Role, 0, h.Name + "의 주장")).ToList();
            Assert.That(run.ResolveFork(fork.Token, votes, oracle: 1), Is.True);

            var scene = run.LastScene;
            var tank = Hero(run, HeroRole.Tank);
            var healer = Hero(run, HeroRole.Healer);
            Assert.That(scene.Any(b => b.Speaker == HeroRole.Tank && b.Text == DialogueLines.OracleGrumble(tank.Voice, false)), Is.True);
            Assert.That(scene.Any(b => b.Speaker == HeroRole.Healer && b.Text == DialogueLines.OracleReaction(healer.Voice, true)), Is.True,
                "a firm hero who is not grumbling still welcomes the oracle");
            var contest = scene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(contest.Rolls.Where(r => !r.IsGod).Select(r => r.Role), Does.Contain(HeroRole.Tank), "the worn-out tank rolls");
            Assert.That(contest.Rolls.Where(r => !r.IsGod).Select(r => r.Role), Does.Not.Contain(HeroRole.Healer), "the calm firm healer yields");
            Assert.That(scene.Any(b => b.Kind == BeatKind.Narration && b.Text.Contains(tank.Name) && b.Text.Contains("지쳐서 계시를 따르지 않는다")), Is.True);
        }

        private static DungeonRun AfterArmourFight(FatigueStage stage, Func<HeroRole, bool> grumbles, params string[] drops)
        {
            var run = DungeonRun.Sandbox();
            var tile = DungeonRunTests.PlaceableTiles(run).OrderBy(t => run.Map.WalkDistance(t)).First();
            Assert.That(run.Place(tile, MonsterKind.Skeleton), Is.True);
            var queue = new Queue<string>(drops);
            run.DropOverride = _ => queue.Count > 0 ? EquipmentCatalog.Find(queue.Dequeue()) : null;
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Combat));
            Tire(run, stage);
            run.GrumbleOverride = grumbles;
            for (int i = 0; i < 5000 && run.Phase == RunPhase.Combat; i++) run.Tick();
            Assert.That(run.PendingLoot, Is.Not.Null);
            return run;
        }

        [Test]
        public void AGrumblingHeroPassesOnTheArmourEvenWhenGreedyOrWhenTheAiSaysYes()
        {
            var run = AfterArmourFight(FatigueStage.Tired, role => role != HeroRole.Healer, "사슬 갑옷");
            var loot = run.PendingLoot;
            Assert.That(loot.Grumbling, Is.EquivalentTo(new[] { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.RangedDamage }));
            foreach (var wish in loot.LocalWishes.Where(w => loot.Grumbles(w.Role)))
            {
                Assert.That(wish.Wants, Is.False);
                Assert.That(wish.Line, Is.EqualTo(DialogueLines.GrumblePass(Hero(run, wish.Role).Voice)));
            }

            var everyoneWants = loot.Heroes.Select(role => new LootWish(role, 0, true, "내가 입을래")).ToList();
            Assert.That(run.ResolveLoot(loot.Token, everyoneWants), Is.True);
            Assert.That(Hero(run, HeroRole.Healer).BodyArmor?.Name, Is.EqualTo("사슬 갑옷"), "only the calm healer was left to bid");
            var passes = run.LastScene.Where(b => b.Kind == BeatKind.Line && b.Mark == DialogueLines.Passes).Select(b => b.Speaker);
            Assert.That(passes, Is.EquivalentTo(new HeroRole?[] { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.RangedDamage }));
        }

        [Test]
        public void AGreedyGrumblerStillPasses()
        {
            var run = DungeonRun.Sandbox();
            run.SetBuild(HeroRole.Tank, run.BuildOf(HeroRole.Tank).With(PersonalityTrait.Greedy));
            var tile = DungeonRunTests.PlaceableTiles(run).OrderBy(t => run.Map.WalkDistance(t)).First();
            Assert.That(run.Place(tile, MonsterKind.Skeleton), Is.True);
            run.DropOverride = _ => EquipmentCatalog.Find("사슬 갑옷");
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Tire(run, FatigueStage.Exhausted);
            run.GrumbleOverride = role => role == HeroRole.Tank;
            for (int i = 0; i < 5000 && run.Phase == RunPhase.Combat; i++) run.Tick();
            var wish = run.PendingLoot.LocalWishes.First(w => w.Role == HeroRole.Tank);
            Assert.That(wish.Wants, Is.False);
            Assert.That(wish.Line, Is.EqualTo(DialogueLines.GrumblePass(Hero(run, HeroRole.Tank).Voice)));
        }

        [Test]
        public void AGrumblingDesperateHeroRefusesToPrayWhateverTheAiSays()
        {
            var run = DungeonRun.Sandbox();
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            Assert.That(run.ResolveFork(run.PendingFork.Token), Is.True);
            run.SetFaith(2);
            Tire(run, FatigueStage.Exhausted);
            run.GrumbleOverride = _ => true;
            Hero(run, HeroRole.Tank).Hp = 5;
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            run.Tick();
            var prayer = run.PendingPrayer;
            Assert.That(prayer?.Role, Is.EqualTo(HeroRole.Tank));
            Assert.That(prayer.Grumbling, Is.True);
            Assert.That(prayer.Local.Prays, Is.False);

            var heard = run.HearPrayer(prayer.Token, new PrayerProposal(true, "신이시여, 제발!"));
            Assert.That(heard.Praying, Is.False);
            Assert.That(heard.Line, Is.EqualTo(DialogueLines.GrumbleRefusal(Hero(run, HeroRole.Tank).Voice)));
            Assert.That(run.PrayedThisStage, Is.False, "a refusal does not use the stage's prayer");
            Assert.That(run.PendingPrayer, Is.Null);
        }
    }
}
