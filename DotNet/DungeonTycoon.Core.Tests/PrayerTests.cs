using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class PrayerTests
    {
        /// <summary>On the march past the first fork, which opens right at the entrance.</summary>
        private static DungeonRun Departed(int faith = 2)
        {
            var run = new DungeonRun();
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            Assert.That(run.ResolveFork(run.PendingFork.Token), Is.True);
            run.SetFaith(faith);
            return run;
        }

        [Test]
        public void DespairDuringAForkDebateWaitsUntilTheForkIsSettled()
        {
            var run = new DungeonRun();
            run.Start();
            run.SetFaith(2);
            BreakTheTank(run);
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            Assert.That(run.PendingPrayer, Is.Null, "the debate comes first");
            run.ResolveFork(run.PendingFork.Token);
            run.Tick();
            Assert.That(run.PendingPrayer?.Role, Is.EqualTo(HeroRole.Tank));
        }

        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];

        /// <summary>The tank is close to death and the ranged hero is down: a desperate tank.</summary>
        private static void BreakTheTank(DungeonRun run)
        {
            Hero(run, HeroRole.Tank).Hp = 5;
            Hero(run, HeroRole.RangedDamage).Hp = 0;
        }

        [Test]
        public void ALowHeroInAStrongPartyDoesNotDespair()
        {
            var run = Departed();
            Hero(run, HeroRole.Tank).Hp = 5;
            Assert.That(run.IsDesperate(HeroRole.Tank), Is.False);
            run.Tick();
            Assert.That(run.PendingPrayer, Is.Null);
        }

        [Test]
        public void EachSignOfACrumblingPartyMakesALowHeroDesperate()
        {
            var run = Departed();
            Hero(run, HeroRole.Tank).Hp = 5;
            run.SetFatigueSteps(run.FatigueSecondThreshold + 1);
            Assert.That(run.IsDesperate(HeroRole.Tank), Is.True, "exhaustion");

            run = Departed();
            Hero(run, HeroRole.Tank).Hp = 5;
            Hero(run, HeroRole.Healer).Hp = 8;
            Hero(run, HeroRole.MeleeDamage).Hp = 10;
            Hero(run, HeroRole.RangedDamage).Hp = 10;
            Assert.That(run.IsDesperate(HeroRole.Tank), Is.True, "party at 33/142 health");
            Assert.That(run.IsDesperate(HeroRole.MeleeDamage), Is.False, "the hero must be close to death too");

            run = Departed();
            BreakTheTank(run);
            Assert.That(run.IsDesperate(HeroRole.Tank), Is.True, "an ally is down");
        }

        [Test]
        public void ADesperateHeroIsAskedAtTheEndOfATickAndTheDungeonFreezes()
        {
            var run = Departed();
            BreakTheTank(run);
            Assert.That(run.Tick(), Is.True);
            var prayer = run.PendingPrayer;
            Assert.That(prayer, Is.Not.Null);
            Assert.That(prayer.Role, Is.EqualTo(HeroRole.Tank));
            Assert.That(prayer.Local.Prays, Is.True, "a cautious hero prays without the AI");
            Assert.That(prayer.Local.Line, Is.EqualTo(DialogueLines.Prayer(PersonalityTag.Cautious)));
            int ticks = run.Ticks;
            Assert.That(run.Tick(), Is.False);
            Assert.That(run.Ticks, Is.EqualTo(ticks));
            Assert.That(run.BeginAdversity(), Is.EqualTo(0), "no adversity while a hero waits to pray");
        }

        [Test]
        public void TheMostHurtDesperateHeroIsAsked()
        {
            var run = Departed();
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            Hero(run, HeroRole.Tank).Hp = 10;   // 10/46
            Hero(run, HeroRole.Healer).Hp = 3;  // 3/28
            run.Tick();
            Assert.That(run.PendingPrayer.Role, Is.EqualTo(HeroRole.Healer));
        }

        [Test]
        public void WithoutFaithForAMiracleNoPrayerIsAsked()
        {
            var run = Departed(faith: 0);
            BreakTheTank(run);
            run.Tick();
            Assert.That(run.PendingPrayer, Is.Null);
            Assert.That(run.PrayedThisStage, Is.False);
        }

        [Test]
        public void ARecklessHeroRefusesOnLocalRulesAndARefusalCostsNothing()
        {
            var run = Departed();
            Hero(run, HeroRole.MeleeDamage).Hp = 4;
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            run.Tick();
            var prayer = run.PendingPrayer;
            Assert.That(prayer.Role, Is.EqualTo(HeroRole.MeleeDamage));
            Assert.That(prayer.Local.Prays, Is.False);

            var heard = run.HearPrayer(prayer.Token);
            Assert.That(heard.Praying, Is.False);
            Assert.That(heard.Line, Is.EqualTo(DialogueLines.PrayerRefusal(PersonalityTag.Reckless)));
            Assert.That(run.PendingPrayer, Is.Null, "the dungeon moves on");
            Assert.That(run.PrayedThisStage, Is.False, "a refusal does not use the stage's prayer");
            Assert.That(run.Tick(), Is.True);
            Assert.That(run.PendingPrayer, Is.Null, "the refusing hero is not asked again at once");
        }

        [Test]
        public void AHeroWhoRefusedOnTheMarchIsAskedAgainAfterTheNextFork()
        {
            var run = Departed();
            BreakTheTank(run);
            run.Tick();
            run.HearPrayer(run.PendingPrayer.Token, new PrayerProposal(false, ""));
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True, "the refusal never blocks the march");
            Assert.That(run.ResolveFork(run.PendingFork.Token), Is.True);
            run.Tick();
            Assert.That(run.PendingPrayer?.Role, Is.EqualTo(HeroRole.Tank));
        }

        [Test]
        public void AnAiPrayerReplacesTheLocalOneButAWordlessPrayerFallsBack()
        {
            var run = Departed();
            BreakTheTank(run);
            run.Tick();
            int token = run.PendingPrayer.Token;
            var heard = run.HearPrayer(token, new PrayerProposal(true, "  "));
            Assert.That(heard.Line, Is.EqualTo(DialogueLines.Prayer(PersonalityTag.Cautious)));

            run = Departed();
            Hero(run, HeroRole.MeleeDamage).Hp = 4;
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            run.Tick();
            heard = run.HearPrayer(run.PendingPrayer.Token, new PrayerProposal(true, "이번만은 빌어 보지."));
            Assert.That(heard.Praying, Is.True, "the AI can make even a reckless hero pray");
            Assert.That(heard.Line, Is.EqualTo("이번만은 빌어 보지."));
            Assert.That(run.HearPrayer(heard.Token, new PrayerProposal(false, "")), Is.Null, "a prayer is heard once");
        }

        [Test]
        public void TheHealingMiracleRestoresTheHeroAndMakesTheirBeliefFirm()
        {
            var run = Departed(faith: 2);
            BreakTheTank(run);
            run.Tick();
            var prayer = run.HearPrayer(run.PendingPrayer.Token);
            Assert.That(prayer.Praying, Is.True);
            Assert.That(run.PrayedThisStage, Is.True);
            Assert.That(run.Tick(), Is.False, "the dungeon waits for the GM");
            run.SetFatigueSteps(0);
            Assert.That(run.CanAnswerPrayer(PrayerAnswer.RestMiracle), Is.False, "a rested party cannot be rested");

            Assert.That(run.AnswerPrayer(prayer.Token, PrayerAnswer.HealingMiracle), Is.True);
            var tank = Hero(run, HeroRole.Tank);
            Assert.That(tank.Hp, Is.EqualTo(tank.MaxHp));
            Assert.That(run.Faith, Is.EqualTo(2 - DungeonRun.MiracleCost));
            Assert.That(tank.Belief, Is.EqualTo(Belief.Firm));
            Assert.That(run.LastPrayer.Answered, Is.True);
            Assert.That(run.LastPrayer.Reaction, Is.EqualTo(DialogueLines.PrayerAnswered(PersonalityTag.Cautious)));
            Assert.That(run.PendingPrayer, Is.Null);
            Assert.That(run.Tick(), Is.True);
        }

        [Test]
        public void TheRestMiracleTakesAwayFatigue()
        {
            var run = Departed();
            BreakTheTank(run);
            run.Tick();
            run.SetFatigueSteps(100);
            var prayer = run.HearPrayer(run.PendingPrayer.Token);
            Assert.That(run.AnswerPrayer(prayer.Token, PrayerAnswer.RestMiracle), Is.True);
            Assert.That(run.FatigueSteps, Is.EqualTo(100 - DungeonRun.RestMiracleRelief));
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.EqualTo(5), "resting does not heal");
            Assert.That(Hero(run, HeroRole.Tank).Belief, Is.EqualTo(Belief.Firm));
        }

        [Test]
        public void LookingAwayCostsNothingButShakesTheHero()
        {
            var run = Departed(faith: 2);
            BreakTheTank(run);
            run.Tick();
            var prayer = run.HearPrayer(run.PendingPrayer.Token);
            Assert.That(run.AnswerPrayer(prayer.Token, PrayerAnswer.Ignore), Is.True);
            Assert.That(run.Faith, Is.EqualTo(2));
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.EqualTo(5));
            Assert.That(Hero(run, HeroRole.Tank).Belief, Is.EqualTo(Belief.Shaken));
            Assert.That(run.LastPrayer.Reaction, Is.EqualTo(DialogueLines.PrayerIgnored(PersonalityTag.Cautious)));
        }

        [Test]
        public void OnlyOnePrayerReachesTheGodEachStage()
        {
            var run = Departed(faith: 3);
            BreakTheTank(run);
            run.Tick();
            run.AnswerPrayer(run.HearPrayer(run.PendingPrayer.Token).Token, PrayerAnswer.Ignore);
            Hero(run, HeroRole.Healer).Hp = 2;
            run.Tick();
            Assert.That(run.IsDesperate(HeroRole.Healer), Is.True);
            Assert.That(run.PendingPrayer, Is.Null);
        }

        [Test]
        public void AnAnswerNeedsAPrayingHeroAndTheFaithForIt()
        {
            var run = Departed(faith: 1);
            BreakTheTank(run);
            run.Tick();
            int token = run.PendingPrayer.Token;
            Assert.That(run.AnswerPrayer(token, PrayerAnswer.HealingMiracle), Is.False, "the hero has not prayed yet");
            run.HearPrayer(token);
            run.SetFaith(0);
            Assert.That(run.AnswerPrayer(token, PrayerAnswer.HealingMiracle), Is.False);
            Assert.That(run.AnswerPrayer(token + 1, PrayerAnswer.Ignore), Is.False, "stale token");
            Assert.That(run.AnswerPrayer(token, PrayerAnswer.Ignore), Is.True);
        }

        // ── Belief at the fork ──────────────────────────────────────────

        private static DungeonRun AtFork(params Belief[] beliefs)
        {
            var run = new DungeonRun();
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            run.SetFaith(3);
            for (int i = 0; i < beliefs.Length; i++) run.Party[i].Belief = beliefs[i];
            return run;
        }

        private static List<ForkOpinion> Votes(DungeonRun run, int option) =>
            run.Party.Select(h => new ForkOpinion(h.Role, option, h.Name + "의 주장")).ToList();

        [Test]
        public void AFirmHeroBacksTheOracleWithoutRollingAndAShakenOneRollsWithABonus()
        {
            // Party order is Tank, Healer, MeleeDamage, RangedDamage.
            var run = AtFork(Belief.Firm, Belief.Steady, Belief.Steady, Belief.Shaken);
            var fork = run.PendingFork;
            var votes = Votes(run, 0);
            run.HearFork(fork.Token, votes);
            var faces = new Queue<int>(new[] { 2, 2, 2, 2 });
            run.ForkRollOverride = () => faces.Dequeue();
            Assert.That(run.ResolveFork(fork.Token, null, oracle: 1), Is.True);

            var roll = run.LastScene.Single(b => b.Kind == BeatKind.Roll);
            var heroes = roll.Rolls.Where(r => !r.IsGod).ToList();
            Assert.That(heroes.Select(r => r.Role), Does.Not.Contain(HeroRole.Tank), "the firm tank does not roll");
            Assert.That(heroes.Count, Is.EqualTo(3));
            var ranged = heroes.Single(r => r.Role == HeroRole.RangedDamage);
            Assert.That(ranged.Modifier, Is.EqualTo(run.Party[(int)HeroRole.RangedDamage].Modifier(Ability.Charisma) + DungeonRun.ShakenBonus));
            Assert.That(ranged.Ability, Does.Contain("흔들림"));
            Assert.That(run.LastScene.Any(b => b.Kind == BeatKind.Narration && b.Text.Contains("굳건한 믿음")), Is.True);
            Assert.That(run.LastFork, Does.Contain("흔들림 +" + DungeonRun.ShakenBonus));
        }

        [Test]
        public void WhenEveryDissenterIsFirmThePartyFollowsTheOracleWithoutDice()
        {
            var run = AtFork(Belief.Firm, Belief.Firm, Belief.Firm, Belief.Firm);
            var fork = run.PendingFork;
            run.HearFork(fork.Token, Votes(run, 0));
            Assert.That(run.ResolveFork(fork.Token, null, oracle: 1), Is.True);
            Assert.That(run.LastScene.Any(b => b.Kind == BeatKind.Roll), Is.False);
            Assert.That(run.LastFork, Does.Contain(fork.Options[1].Label));
            foreach (var beat in run.LastScene.Where(b => b.Kind == BeatKind.Line))
                Assert.That(beat.Text, Is.EqualTo(DialogueLines.OracleReaction(run.Party[(int)beat.Speaker.Value].Tags[0], true)));
        }

        [Test]
        public void BeliefLastsIntoTheNextStage()
        {
            var run = Departed();
            run.Party[(int)HeroRole.Healer].Belief = Belief.Shaken;
            DungeonRunTests.Drive(run, () => run.Stage == 2 && run.PendingLoot == null || run.Phase == RunPhase.Defeat);
            Assert.That(run.Stage, Is.EqualTo(2));
            Assert.That(run.Party[(int)HeroRole.Healer].Belief, Is.EqualTo(Belief.Shaken));
            Assert.That(run.PrayedThisStage, Is.False);
        }
    }
}
