using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class OracleTests
    {
        private static readonly HeroRole[] March =
            { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.Healer, HeroRole.RangedDamage };

        private static DungeonRun AtFirstFork(int faith = 3)
        {
            var run = new DungeonRun();
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            run.SetFaith(faith);
            return run;
        }

        private static List<ForkOpinion> Votes(DungeonRun run, Func<HeroRole, int> option) =>
            run.Party.Where(h => h.Hp > 0).Select(h => new ForkOpinion(h.Role, option(h.Role), h.Name + "의 주장")).ToList();

        /// <summary>Pins the persuasion dice: heroes in march order, then the god.</summary>
        private static void Faces(DungeonRun run, params int[] faces)
        {
            var queue = new Queue<int>(faces);
            run.ForkRollOverride = () => queue.Dequeue();
        }

        [Test]
        public void HearingFirstShowsOnlyTheOpinionsAndTheVerdictComesAfter()
        {
            var run = AtFirstFork();
            var fork = run.PendingFork;
            var heard = run.HearFork(fork.Token, Votes(run, _ => 0));
            Assert.That(heard.Select(o => o.Role), Is.EqualTo(March));
            Assert.That(run.LastScene.Select(b => b.Kind), Is.All.EqualTo(BeatKind.Line));
            Assert.That(run.PendingFork, Is.SameAs(fork), "hearing does not settle the fork");
            Assert.That(run.Tick(), Is.False, "the dungeon stays frozen until the verdict");
            Assert.That(run.HearFork(fork.Token, Votes(run, _ => 1)), Is.SameAs(heard), "a second hearing keeps the first answer");

            Assert.That(run.ResolveFork(fork.Token), Is.True);
            Assert.That(run.LastScene.Any(b => b.Kind == BeatKind.Line && b.Text.EndsWith("의 주장")), Is.False,
                "the opinions were already shown");
            Assert.That(run.LastScene.Last().Text, Does.Contain(fork.Options[0].Label));
        }

        [Test]
        public void AnOracleCostsFaithMakesEveryoneReactAndTheGodRollsWithItsBonus()
        {
            var run = AtFirstFork(faith: 2);
            var fork = run.PendingFork;
            run.HearFork(fork.Token, Votes(run, _ => 0));
            Faces(run, 3, 3, 3, 3, 10);
            Assert.That(run.ResolveFork(fork.Token, null, oracle: 1), Is.True);
            Assert.That(run.Faith, Is.EqualTo(2 - DungeonRun.OracleCost));

            var scene = run.LastScene;
            Assert.That(scene[0].Kind, Is.EqualTo(BeatKind.Narration));
            Assert.That(scene[0].Text, Does.Contain(fork.Options[1].Label));
            var reactions = scene.Skip(1).Take(4).ToList();
            Assert.That(reactions.Select(b => b.Speaker), Is.EqualTo(March.Cast<HeroRole?>()));
            foreach (var beat in reactions)
                Assert.That(beat.Text, Is.EqualTo(DialogueLines.OracleReaction(run.Party[(int)beat.Speaker.Value].Tags[0], false)));

            var roll = scene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(roll.Text, Is.EqualTo(DialogueLines.OracleTitle(DungeonRun.OracleBonus)));
            var god = roll.Rolls.Single(r => r.IsGod);
            Assert.That(god.Total, Is.EqualTo(10 + DungeonRun.OracleBonus));
            Assert.That(roll.Rolls.Where(r => !r.IsGod).Select(r => r.Role), Is.EqualTo(March));
            Assert.That(roll.GodWon, Is.True);
            Assert.That(roll.Winner, Is.Null);
            Assert.That(run.LastFork, Does.Contain("신탁대로").And.Contain(fork.Options[1].Label));
        }

        [Test]
        public void TheGodWinsATieButAHighRollingHeroCanDefyTheOracle()
        {
            // Tie: the healer (charisma +2) rolls 13 for 15, the god rolls 10 + 5 = 15.
            var tied = AtFirstFork();
            var fork = tied.PendingFork;
            Faces(tied, 1, 1, 13, 1, 10);
            Assert.That(tied.ResolveFork(fork.Token, Votes(tied, role => role == HeroRole.Healer ? 0 : 1), oracle: 1), Is.True);
            var roll = tied.LastScene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(roll.GodWon, Is.True);
            Assert.That(tied.LastScene.Any(b => b.Text == "신과 같은 합계 — 신탁을 따른다."), Is.True);

            // Defiance: the healer rolls 18 for 20 against the god's 15 and wins for the other way.
            var defied = AtFirstFork();
            fork = defied.PendingFork;
            Faces(defied, 1, 1, 18, 1, 10);
            Assert.That(defied.ResolveFork(fork.Token, Votes(defied, role => role == HeroRole.Healer ? 0 : 1), oracle: 1), Is.True);
            roll = defied.LastScene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(roll.GodWon, Is.False);
            Assert.That(roll.Winner, Is.EqualTo(HeroRole.Healer));
            Assert.That(defied.LastScene.Any(b => b.Text.EndsWith("반발이 계시를 이겼다.")), Is.True);
            Assert.That(defied.LastFork, Does.Contain(fork.Options[0].Label));
        }

        [Test]
        public void AUnanimousPartyGoesAtOnceWhenTheOracleAgreesButRollsWhenItDisagrees()
        {
            var agree = AtFirstFork();
            Assert.That(agree.ResolveFork(agree.PendingFork.Token, Votes(agree, _ => 0), oracle: 0), Is.True);
            Assert.That(agree.LastScene.Any(b => b.Kind == BeatKind.Roll), Is.False);
            Assert.That(agree.Faith, Is.EqualTo(3 - DungeonRun.OracleCost), "an agreeing oracle still costs faith");

            var disagree = AtFirstFork();
            Faces(disagree, 20, 20, 20, 20, 1);
            Assert.That(disagree.ResolveFork(disagree.PendingFork.Token, Votes(disagree, _ => 0), oracle: 1), Is.True);
            var roll = disagree.LastScene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(roll.Rolls.Count(r => r.IsGod), Is.EqualTo(1));
            Assert.That(roll.GodWon, Is.False, "four 20s beat the god's 6");
        }

        [Test]
        public void AnOracleWithoutFaithOrForANonexistentWayIsIgnoredAndCostsNothing()
        {
            var broke = AtFirstFork(faith: 0);
            Assert.That(broke.CanOracle, Is.False);
            Assert.That(broke.ResolveFork(broke.PendingFork.Token, Votes(broke, _ => 0), oracle: 1), Is.True);
            Assert.That(broke.LastScene.Any(b => b.Kind == BeatKind.Roll), Is.False, "unanimous and no oracle");
            Assert.That(broke.Faith, Is.Zero);

            var wrong = AtFirstFork(faith: 2);
            int ways = wrong.PendingFork.Options.Count;
            Assert.That(wrong.ResolveFork(wrong.PendingFork.Token, Votes(wrong, _ => 0), oracle: ways), Is.True);
            Assert.That(wrong.Faith, Is.EqualTo(2));
        }

        [Test]
        public void WithoutAnOracleTheOldPersuasionRulesStillHold()
        {
            var run = AtFirstFork();
            Faces(run, 5, 9, 2, 4);
            Assert.That(run.ResolveFork(run.PendingFork.Token, Votes(run, role => role == HeroRole.MeleeDamage ? 1 : 0)), Is.True);
            var roll = run.LastScene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(roll.Text, Is.EqualTo(DialogueLines.PersuasionTitle));
            Assert.That(roll.Rolls.Any(r => r.IsGod), Is.False);
            Assert.That(roll.Winner, Is.EqualTo(HeroRole.MeleeDamage), "9 + 0 beats 5 + 1, 2 + 2 and 4 + 1");
            Assert.That(run.Faith, Is.EqualTo(3));
        }
    }
}
