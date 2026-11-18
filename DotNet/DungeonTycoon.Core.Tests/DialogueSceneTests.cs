using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class DialogueSceneTests
    {
        private static readonly HeroRole[] March =
            { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.Healer, HeroRole.RangedDamage };

        private static DungeonRun AtFirstFork(int seed = 20261118)
        {
            var run = DungeonRun.Sandbox(seed);
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            return run;
        }

        private static List<ForkOpinion> Votes(DungeonRun run, Func<HeroRole, int> option) =>
            run.Party.Where(h => h.Hp > 0).Select(h => new ForkOpinion(h.Role, option(h.Role), h.Name + "의 주장")).ToList();

        [Test]
        public void AUnanimousDebateIsEveryOpinionInMarchOrderThenTheResultWithoutDice()
        {
            var run = AtFirstFork();
            var fork = run.PendingFork;
            Assert.That(run.ResolveFork(fork.Token, Votes(run, _ => 0)), Is.True);

            var scene = run.LastScene;
            Assert.That(scene.Take(4).Select(b => b.Kind), Is.All.EqualTo(BeatKind.Line));
            Assert.That(scene.Take(4).Select(b => b.Speaker), Is.EqualTo(March.Cast<HeroRole?>()));
            Assert.That(scene.Take(4).Select(b => b.Text), Is.EqualTo(March.Select(r => run.Party[(int)r].Name + "의 주장")));
            Assert.That(scene.Any(b => b.Kind == BeatKind.Roll), Is.False);
            Assert.That(scene.Last().Kind, Is.EqualTo(BeatKind.Narration));
            Assert.That(scene.Last().Text, Does.Contain(fork.Options[0].Label));
        }

        [Test]
        public void ASplitDebateRollsEveryoneOnceAndTheWinnerClosesIt()
        {
            var run = AtFirstFork();
            var fork = run.PendingFork;
            var votes = Votes(run, role => role == HeroRole.Tank || role == HeroRole.Healer ? 0 : 1);
            Assert.That(run.ResolveFork(fork.Token, votes), Is.True);

            var scene = run.LastScene;
            var roll = scene.Single(b => b.Kind == BeatKind.Roll);
            Assert.That(roll.Text, Is.EqualTo(DialogueLines.PersuasionTitle));
            Assert.That(roll.Difficulty, Is.Zero);
            Assert.That(roll.Rolls.Select(r => r.Role), Is.EqualTo(March), "dice roll in march order");
            foreach (var die in roll.Rolls)
            {
                Assert.That(die.Face, Is.InRange(1, 20));
                Assert.That(die.Modifier, Is.EqualTo(run.Party[(int)die.Role].Modifier(Ability.Charisma)));
                Assert.That(die.Ability, Is.EqualTo("매력"));
            }
            int best = roll.Rolls.Max(r => r.Total);
            var winner = roll.Winner.Value;
            Assert.That(roll.Rolls.Single(r => r.Role == winner).Total, Is.EqualTo(best));

            int beat = scene.ToList().IndexOf(roll);
            Assert.That(beat, Is.EqualTo(4), "the dice come right after the four opinions");
            var closing = scene[scene.Count - 2];
            Assert.That(closing.Kind, Is.EqualTo(BeatKind.Line));
            Assert.That(closing.Speaker, Is.EqualTo(winner));
            Assert.That(closing.Text, Is.EqualTo(DialogueLines.Closing(winner)));
            string way = fork.Options[votes.Single(v => v.Role == winner).Option].Label;
            Assert.That(scene.Last().Text, Does.Contain(run.Party[(int)winner].Name).And.Contain(way));
            Assert.That(run.LastFork, Does.EndWith(way), "the scene and the summary agree on the way taken");
            Assert.That(run.Log.Any(l => l.Contains(DialogueLines.Closing(winner))), Is.True, "the closing line is logged");
        }

        [Test]
        public void ATieIsExplainedOnlyWhenTiedHeroesWantedDifferentWays()
        {
            int explained = 0, silent = 0;
            for (int seed = 1; seed <= 300; seed++)
            {
                var run = AtFirstFork(seed);
                var fork = run.PendingFork;
                var votes = Votes(run, role => role == HeroRole.Tank || role == HeroRole.Healer ? 0 : 1);
                run.ResolveFork(fork.Token, votes);
                var scene = run.LastScene;
                var roll = scene.Single(b => b.Kind == BeatKind.Roll);
                int best = roll.Rolls.Max(r => r.Total);
                var ways = roll.Rolls.Where(r => r.Total == best).Select(r => votes.Single(v => v.Role == r.Role).Option).Distinct();
                bool tie = ways.Count() > 1;
                var after = scene[scene.ToList().IndexOf(roll) + 1];
                Assert.That(after.Kind == BeatKind.Narration, Is.EqualTo(tie), $"seed {seed}");
                if (!tie) continue;
                // Two supporters each way, so a tie between ways always falls to march order.
                Assert.That(after.Text, Does.Contain("대형 앞쪽"));
                var front = roll.Rolls.Where(r => r.Total == best).OrderBy(r => Array.IndexOf(March, r.Role)).First();
                Assert.That(roll.Winner, Is.EqualTo(front.Role), $"seed {seed}");
                explained++;
            }
            silent = 300 - explained;
            Assert.That(explained, Is.Positive, "some seed should produce a tie");
            Assert.That(silent, Is.Positive);
        }

        [Test]
        public void AnAdversitySceneIsDeclarationThenTheCheckThenTheOutcomeAndAReaction()
        {
            var run = DungeonRunTests.RunInCombat();
            int ticket = run.BeginAdversity();
            Assert.That(run.ResolveAdversity(ticket, HeroRole.Tank, 1, "방패를 들겠다"), Is.True);

            var scene = run.LastScene;
            Assert.That(scene.Select(b => b.Kind),
                Is.EqualTo(new[] { BeatKind.Line, BeatKind.Roll, BeatKind.Narration, BeatKind.Line }));
            Assert.That(scene[0].Speaker, Is.EqualTo(HeroRole.Tank));
            Assert.That(scene[0].Text, Is.EqualTo("방패를 들겠다"));
            var check = scene[1];
            Assert.That(check.Text, Is.EqualTo(DialogueLines.TrialTitle("힘")));
            Assert.That(check.Difficulty, Is.EqualTo(run.TrialDifficulty));
            Assert.That(check.Success, Is.False);
            Assert.That(check.Winner, Is.Null);
            var die = check.Rolls.Single();
            Assert.That(die.Role, Is.EqualTo(HeroRole.Tank));
            Assert.That(die.Face, Is.EqualTo(1));
            Assert.That(die.Modifier, Is.EqualTo(run.TrialModifier(HeroRole.Tank)));
            Assert.That(scene[2].Text, Does.Contain(DungeonRun.TrialDamage.ToString()).And.Not.Contain("실패"));
            Assert.That(scene[3].Text, Is.EqualTo(DialogueLines.Reaction(HeroRole.Tank, false)));

            int second = run.BeginAdversity();
            Assert.That(run.ResolveAdversity(second, HeroRole.Tank, 20), Is.True);
            Assert.That(run.LastScene[1].Success, Is.True);
            Assert.That(run.LastScene[2].Text, Does.Contain("경험치 +1, 신앙 +1").And.Not.Contain("성공"));
            Assert.That(run.LastScene[3].Text, Is.EqualTo(DialogueLines.Reaction(HeroRole.Tank, true)));
        }

        [Test]
        public void TheAdversityDieComesFromTheSeedSoTheSameRunRollsTheSameFace()
        {
            int Face(int seed)
            {
                var run = DungeonRun.Sandbox(seed);
                run.Start();
                for (int i = 0; i < 25; i++) run.Tick();
                int ticket = run.BeginAdversity();
                Assert.That(run.RollAdversity(ticket + 1, HeroRole.Tank), Is.False, "a stale ticket rolls nothing");
                Assert.That(run.RollAdversity(ticket, HeroRole.Tank, "막겠다"), Is.True);
                Assert.That(run.RollAdversity(ticket, HeroRole.Tank), Is.False, "the same adversity is not rolled twice");
                return run.LastScene.Single(b => b.Kind == BeatKind.Roll).Rolls.Single().Face;
            }
            var faces = Enumerable.Range(1, 40).Select(Face).ToList();
            Assert.That(faces, Is.All.InRange(1, 20));
            Assert.That(faces.Distinct().Count(), Is.GreaterThan(5), "different seeds roll different faces");
            Assert.That(Enumerable.Range(1, 40).Select(Face), Is.EqualTo(faces));
        }
    }
}
