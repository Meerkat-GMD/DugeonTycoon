using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class ExplorationTests
    {
        private static int FloorCount(DungeonMap map)
        {
            int count = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.IsFloor(new TilePoint(x, y))) count++;
            return count;
        }

        /// <summary>Ticks until a fork opens. Returns false if a fight or the end came first.</summary>
        internal static bool TickToFirstFork(DungeonRun run)
        {
            for (int tick = 0; tick < 3000 && run.PendingFork == null && run.Phase == RunPhase.Travel; tick++) run.Tick();
            return run.PendingFork != null;
        }

        [Test]
        public void HeroesSeeWithinTheirRadiusButNotThroughWalls()
        {
            var run = new DungeonRun();
            Assert.That(run.IsSeen(run.Map.Entrance), Is.False, "nothing is seen before departure");
            run.Start();
            var eye = run.Map.Entrance;
            Assert.That(run.IsVisible(eye), Is.True);
            Assert.That(run.IsSeen(run.Map.BossTile), Is.False);
            for (int y = 0; y < run.Map.Height; y++)
            for (int x = 0; x < run.Map.Width; x++)
            {
                var tile = new TilePoint(x, y);
                if (!run.IsVisible(tile)) continue;
                int dx = x - eye.X, dy = y - eye.Y;
                Assert.That(dx * dx + dy * dy, Is.LessThanOrEqualTo(DungeonRun.VisionRadius * DungeonRun.VisionRadius), tile.ToString());
                Assert.That(run.Map.HasLineOfSight(eye, tile), Is.True, tile.ToString());
            }
            // Stage 1: the corridor north of the entrance room lies behind the room's top wall.
            Assert.That(run.Map.HasLineOfSight(new TilePoint(2, 17), new TilePoint(4, 11)), Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void AnEmptyDungeonIsExploredUntilTheBossWithoutWalkingInCircles(int stage)
        {
            var run = new DungeonRun();
            if (stage == 2)
            {
                run.Start();
                DungeonRunTests.Drive(run, () => run.Stage == 2);
                Assert.That(run.Stage, Is.EqualTo(2));
            }
            run.Start();
            DungeonRunTests.Drive(run, () => run.FightingBoss || !DungeonRunTests.InDungeon(run));
            Assert.That(run.FightingBoss, Is.True);
            Assert.That(run.ForkCount, Is.Positive, "the maps branch, so the party should have argued at least once");
            Assert.That(run.StepsTaken, Is.LessThan(FloorCount(run.Map) * 2), "exploration should not loop");
        }

        [Test]
        public void ForksOnlyDescribeWhatThePartyCanSee()
        {
            var blind = new DungeonRun();
            blind.Start();
            Assert.That(TickToFirstFork(blind), Is.True);

            var hidden = new DungeonRun();
            // A goblin in the closet north-west is far outside the party's sight at the first fork.
            hidden.Place(new TilePoint(2, 1), MonsterKind.Goblin);
            hidden.Start();
            Assert.That(TickToFirstFork(hidden), Is.True);
            Assert.That(hidden.Monsters[0].Spotted, Is.False);

            var a = blind.PendingFork;
            var b = hidden.PendingFork;
            Assert.That(b.Options.Select(o => (o.Label, o.Distance, o.VisibleMonsters, o.Depth, o.Openness)),
                Is.EqualTo(a.Options.Select(o => (o.Label, o.Distance, o.VisibleMonsters, o.Depth, o.Openness))));
            Assert.That(b.LocalOpinions.Select(o => (o.Role, o.Option, o.Line)),
                Is.EqualTo(a.LocalOpinions.Select(o => (o.Role, o.Option, o.Line))));
            Assert.That(b.Options.All(o => o.VisibleMonsters == 0), Is.True);
        }

        [Test]
        public void PersonalitiesDisagreeAboutAMonsterInSightAtAFork()
        {
            var blind = new DungeonRun();
            blind.Start();
            Assert.That(TickToFirstFork(blind), Is.True);
            var candidates = DungeonRunTests.PlaceableTiles(blind).Where(blind.IsVisible).ToList();
            foreach (var spot in candidates)
            {
                // Monsters wander, so try spots until one is in sight along a single way when the party stops.
                var probe = new DungeonRun();
                probe.Place(spot, MonsterKind.Goblin);
                probe.Start();
                if (!TickToFirstFork(probe)) continue;
                var fork = probe.PendingFork;
                var marked = fork.Options.Where(o => o.VisibleMonsters == 1).ToList();
                if (marked.Count != 1 || fork.Options.Count < 2) continue;
                var reckless = fork.LocalOpinions.Single(o => o.Role == HeroRole.MeleeDamage);
                var cautious = fork.LocalOpinions.Single(o => o.Role == HeroRole.Tank);
                Assert.That(reckless.Option, Is.EqualTo(marked[0].Index), "카이 is reckless and heads for the goblin");
                Assert.That(cautious.Option, Is.Not.EqualTo(marked[0].Index), "가론 is cautious and avoids it");
                return;
            }
            Assert.Fail("no placement left a single goblin in sight at the first fork");
        }

        [Test]
        public void ForkResolutionValidatesProposalsAndIgnoresStaleTokens()
        {
            var run = new DungeonRun();
            run.Start();
            Assert.That(TickToFirstFork(run), Is.True);
            var fork = run.PendingFork;
            var leader = run.LeaderTile;
            for (int i = 0; i < 20; i++) run.Tick();
            Assert.That(run.LeaderTile, Is.EqualTo(leader), "the party waits at the fork");
            Assert.That(run.ResolveFork(fork.Token + 1), Is.False);

            int pick = fork.Options.Count - 1;
            var proposals = new List<ForkOpinion>
            {
                new ForkOpinion(HeroRole.Tank, pick, "저쪽으로!"),
                new ForkOpinion(HeroRole.Healer, pick, "저도 찬성이에요."),
                new ForkOpinion(HeroRole.MeleeDamage, pick, "좋아."),
                new ForkOpinion(HeroRole.RangedDamage, 99, "엉뚱한 길") // invalid: falls back to her local opinion
            };
            Assert.That(run.ResolveFork(fork.Token, proposals), Is.True);
            Assert.That(run.Party[(int)HeroRole.Tank].LastLine, Is.EqualTo("저쪽으로!"));
            var ranged = fork.LocalOpinions.Single(o => o.Role == HeroRole.RangedDamage);
            Assert.That(run.Party[(int)HeroRole.RangedDamage].LastLine, Is.EqualTo(ranged.Line));
            Assert.That(run.PendingFork, Is.Null);
            Assert.That(run.ResolveFork(fork.Token, proposals), Is.False, "a late duplicate answer is dropped");
            Assert.That(run.LastFork, Does.Contain(ranged.Option == pick ? "의견 일치" : "설득"));
        }

        [Test]
        public void SplitVotesFollowTheBestPersuasionRollAndReplayWithTheSameSeed()
        {
            string Settle(int seed, out string chosenLabel, out IReadOnlyList<ForkOption> options)
            {
                var run = new DungeonRun(seed);
                run.Start();
                Assert.That(TickToFirstFork(run), Is.True);
                var fork = run.PendingFork;
                options = fork.Options;
                var split = run.Party.Where(h => h.Hp > 0)
                    .Select((h, i) => new ForkOpinion(h.Role, i % 2, $"{h.Name}의 의견")).ToList();
                run.ResolveFork(fork.Token, split);
                string summary = run.LastFork;
                chosenLabel = fork.Options.Single(o => summary.EndsWith(o.Label)).Label;
                return summary;
            }
            string first = Settle(3, out var label, out var options);
            Assert.That(first, Does.Contain("설득 d20 + 매력"));
            Assert.That(options.Take(2).Select(o => o.Label), Does.Contain(label));
            Assert.That(Settle(3, out _, out _), Is.EqualTo(first));
        }
    }
}
