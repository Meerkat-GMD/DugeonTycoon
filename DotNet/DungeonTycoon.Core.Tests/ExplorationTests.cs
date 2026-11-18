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

        private static void DriveToFirstFork(DungeonRun run)
        {
            for (int tick = 0; tick < 500 && run.PendingFork == null && run.Phase == RunPhase.Travel; tick++) run.Step();
            Assert.That(run.PendingFork, Is.Not.Null, "expected a fork");
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
            DungeonRunTests.Drive(run, () => run.FightingBoss || run.Phase != RunPhase.Travel && run.Phase != RunPhase.Combat);
            Assert.That(run.FightingBoss, Is.True);
            Assert.That(run.ForkCount, Is.Positive, "the maps branch, so the party should have argued at least once");
            Assert.That(run.StepsTaken, Is.LessThan(FloorCount(run.Map) * 2), "exploration should not loop");
            int distance = run.Map.Distances(run.LeaderTile, run.Map.IsFloor, DungeonRun.EncounterReach)
                .TryGetValue(run.Map.BossTile, out int d) ? d : -1;
            Assert.That(distance, Is.InRange(1, DungeonRun.EncounterReach));
        }

        [Test]
        public void ForksOnlyDescribeWhatThePartyCanSee()
        {
            var blind = new DungeonRun();
            blind.Start();
            DriveToFirstFork(blind);

            var hidden = new DungeonRun();
            // A goblin deep in the closet north-west is far outside the party's sight at the first fork.
            hidden.Place(new TilePoint(2, 1), MonsterKind.Goblin);
            hidden.Start();
            DriveToFirstFork(hidden);
            Assert.That(hidden.IsVisible(new TilePoint(2, 1)), Is.False);

            var a = blind.PendingFork;
            var b = hidden.PendingFork;
            Assert.That(b.Options.Select(o => (o.Label, o.Distance, o.VisibleMonsters, o.Depth, o.Openness)),
                Is.EqualTo(a.Options.Select(o => (o.Label, o.Distance, o.VisibleMonsters, o.Depth, o.Openness))));
            Assert.That(b.LocalOpinions.Select(o => (o.Role, o.Option, o.Line)),
                Is.EqualTo(a.LocalOpinions.Select(o => (o.Role, o.Option, o.Line))));
            Assert.That(b.Options.All(o => o.VisibleMonsters == 0), Is.True);
        }

        [Test]
        public void ForkOptionsCountMonstersInSightAndPersonalitiesDisagreeAboutThem()
        {
            var run = new DungeonRun();
            run.Start();
            DriveToFirstFork(run);
            var fork = run.PendingFork;
            Assert.That(fork.Options.Count, Is.GreaterThanOrEqualTo(2));

            // Find a tile the party sees at the fork that lies along exactly one option and is too far to start a fight.
            var near = run.Map.Distances(run.LeaderTile, run.Map.IsFloor, DungeonRun.EncounterReach + 2);
            bool Along(TilePoint tile, int option) =>
                DungeonRunTests.PlaceableTiles(run).Contains(tile) &&
                fork.Options[option].Target.X - 2 <= tile.X && tile.X <= fork.Options[option].Target.X + 2 &&
                fork.Options[option].Target.Y - 2 <= tile.Y && tile.Y <= fork.Options[option].Target.Y + 2;
            int chosen = -1;
            TilePoint spot = default;
            for (int option = 0; option < fork.Options.Count && chosen < 0; option++)
            {
                foreach (var tile in DungeonRunTests.PlaceableTiles(run))
                {
                    if (!run.IsVisible(tile) || near.ContainsKey(tile) || !Along(tile, option)) continue;
                    if (Enumerable.Range(0, fork.Options.Count).Any(other => other != option && Along(tile, other))) continue;
                    chosen = option;
                    spot = tile;
                    break;
                }
            }
            Assert.That(chosen, Is.GreaterThanOrEqualTo(0), "no visible, placeable tile next to a single option");

            var probe = new DungeonRun();
            probe.Place(spot, MonsterKind.Goblin);
            probe.Start();
            DriveToFirstFork(probe);
            var seen = probe.PendingFork;
            Assert.That(seen.Options[chosen].VisibleMonsters, Is.EqualTo(1));
            var reckless = seen.LocalOpinions.Single(o => o.Role == HeroRole.MeleeDamage);
            var cautious = seen.LocalOpinions.Single(o => o.Role == HeroRole.Tank);
            Assert.That(reckless.Option, Is.EqualTo(chosen), "카이 is reckless and heads for the visible goblin");
            Assert.That(cautious.Option, Is.Not.EqualTo(chosen), "가론 is cautious and avoids it");
        }

        [Test]
        public void ForkResolutionValidatesProposalsAndIgnoresStaleTokens()
        {
            var run = new DungeonRun();
            run.Start();
            DriveToFirstFork(run);
            var fork = run.PendingFork;
            Assert.That(run.Step(), Is.False, "the party waits at the fork");
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
                DriveToFirstFork(run);
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

        [Test]
        public void PartyFightsVisibleMonstersItPassesAndMarchesInOrder()
        {
            var run = new DungeonRun();
            run.Place(run.Map.BossTile, MonsterKind.Ogre);
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.FoeIndex, Is.EqualTo(0));
            Assert.That(run.Enemy.Name, Is.EqualTo("오우거"));
            Assert.That(run.IsSpotted(0), Is.True);

            // Marching order: the healer and the ranged hero walk behind the front line.
            var leader = run.HeroTile(HeroRole.Tank);
            Assert.That(run.LeaderTile, Is.EqualTo(leader));
            var path = run.Map.FindPath(leader, run.HeroTile(HeroRole.RangedDamage));
            Assert.That(path.Count - 1, Is.InRange(1, 3));

            DungeonRunTests.Drive(run, () => run.Phase != RunPhase.Combat);
            Assert.That(run.IsDefeated(0), Is.True);
            Assert.That(run.DefeatedCount, Is.EqualTo(1));
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat || run.Phase != RunPhase.Travel);
            Assert.That(run.FightingBoss, Is.True);
        }
    }
}
