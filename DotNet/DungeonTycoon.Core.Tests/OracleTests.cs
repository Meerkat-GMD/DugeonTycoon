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
            var run = DungeonRun.Sandbox();
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            run.SetFaith(faith);
            return run;
        }

        private static List<ForkOpinion> Votes(DungeonRun run, Func<HeroRole, int> option) =>
            run.Party.Where(h => h.Hp > 0).Select(h => new ForkOpinion(h.Role, option(h.Role), h.Name + "의 주장")).ToList();

        /// <summary>Pins the persuasion dice: heroes in march order.</summary>
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

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        public void PaidOracleChoosesTheRouteWithoutRolling(int chosen, bool split)
        {
            var run = AtFirstFork(faith: 2);
            var fork = run.PendingFork;
            run.HearFork(fork.Token, Votes(run, role => split && role == HeroRole.Healer ? 1 : 0));
            Faces(run); // Any roll would exhaust the empty queue.
            Assert.That(run.ResolveFork(fork.Token, null, oracle: chosen), Is.True);
            Assert.That(run.Faith, Is.EqualTo(2 - DungeonRun.OracleCost));

            var scene = run.LastScene;
            Assert.That(scene[0].Kind, Is.EqualTo(BeatKind.Narration));
            Assert.That(scene[0].Text, Does.Contain("신탁이 내려왔다").And.Contain(fork.Options[chosen].Label));
            Assert.That(scene.Where(b => b.Kind == BeatKind.Line).Select(b => b.Speaker), Is.EqualTo(March.Cast<HeroRole?>()));
            Assert.That(scene.Any(b => b.Kind == BeatKind.Roll), Is.False);
            Assert.That(run.LastFork, Does.Contain("신탁대로").And.Contain(fork.Options[chosen].Label));

            var route = fork.Options[chosen].Route;
            for (int i = 0; i < 100 && run.LeaderTile == route[0]; i++) run.Tick();
            Assert.That(run.LeaderTile, Is.EqualTo(route[1]));
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
