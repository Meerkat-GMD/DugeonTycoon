using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class DungeonRunTests
    {
        internal static List<TilePoint> PlaceableTiles(DungeonRun run)
        {
            var tiles = new List<TilePoint>();
            for (int y = 0; y < run.Map.Height; y++)
            for (int x = 0; x < run.Map.Width; x++)
            {
                var tile = new TilePoint(x, y);
                if (run.CanHoldMonster(tile)) tiles.Add(tile);
            }
            return tiles;
        }

        internal static bool InDungeon(DungeonRun run) => run.Phase == RunPhase.Travel || run.Phase == RunPhase.Combat;

        /// <summary>Runs the expedition with local decisions until the condition holds or nothing can advance.</summary>
        internal static void Drive(DungeonRun run, Func<bool> until, int limit = 20000)
        {
            for (int tick = 0; tick < limit && !until(); tick++)
            {
                if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                else if (InDungeon(run)) run.Tick();
                else return;
            }
        }

        /// <summary>A goblin on the boss tile is always met near the boss, whatever way the party takes.</summary>
        internal static DungeonRun RunInCombat(MonsterKind kind = MonsterKind.Goblin)
        {
            var run = new DungeonRun();
            run.Place(run.Map.BossTile, kind);
            run.Start();
            Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Combat));
            return run;
        }

        [Test]
        public void PlacementSpendsBudgetStacksRefundsOneAtATimeAndLocksOnDeparture()
        {
            var run = new DungeonRun();
            var tiles = PlaceableTiles(run);
            Assert.That(run.Place(tiles[0], MonsterKind.Ogre), Is.True);
            Assert.That(run.Place(tiles[0], MonsterKind.Ogre), Is.True);
            Assert.That(run.Place(tiles[1], MonsterKind.Ogre), Is.True);
            Assert.That(run.Budget, Is.Zero);
            Assert.That(run.CountAt(tiles[0]), Is.EqualTo(2));
            Assert.That(run.CheckPlace(tiles[2], MonsterKind.Goblin), Is.EqualTo(PlacementCheck.OverBudget));
            Assert.That(run.Place(tiles[2], MonsterKind.Goblin), Is.False);

            Assert.That(run.Remove(tiles[0]), Is.True);
            Assert.That(run.Budget, Is.EqualTo(2));
            Assert.That(run.CountAt(tiles[0]), Is.EqualTo(1));
            Assert.That(run.Place(tiles[0], MonsterKind.Goblin), Is.True);
            // The most recent monster on a tile comes back first.
            Assert.That(run.Remove(tiles[0]), Is.True);
            Assert.That(run.Placements.Single(p => p.Tile == tiles[0]).Kind, Is.EqualTo(MonsterKind.Ogre));
            Assert.That(run.CheckRemove(tiles[2]), Is.EqualTo(PlacementCheck.NothingToRemove));

            Assert.That(run.Start(), Is.True);
            Assert.That(run.Start(), Is.False);
            Assert.That(run.CheckPlace(tiles[2], MonsterKind.Goblin), Is.EqualTo(PlacementCheck.Locked));
            Assert.That(run.Remove(tiles[0]), Is.False);
            Assert.That(run.Placements.Count, Is.EqualTo(2));
            Assert.That(run.Monsters.Count, Is.EqualTo(3), "two placed monsters and the boss");
            Assert.That(run.Monsters.Last().IsBoss, Is.True);
        }

        [Test]
        public void MonstersStayOffWallsAndAwayFromTheEntrance()
        {
            var run = new DungeonRun();
            var map = run.Map;
            Assert.That(run.CheckPlace(new TilePoint(0, 0), MonsterKind.Goblin), Is.EqualTo(PlacementCheck.NotFloor));
            Assert.That(run.CheckPlace(new TilePoint(-1, 3), MonsterKind.Goblin), Is.EqualTo(PlacementCheck.NotFloor));
            Assert.That(run.CheckPlace(map.Entrance, MonsterKind.Goblin), Is.EqualTo(PlacementCheck.NearEntrance));
            Assert.That(run.CheckPlace(PlaceableTiles(run)[0], MonsterKind.None), Is.EqualTo(PlacementCheck.InvalidKind));

            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var tile = new TilePoint(x, y);
                int distance = map.WalkDistance(tile);
                if (distance < 0) continue;
                var expected = distance <= DungeonRun.EntranceClearance ? PlacementCheck.NearEntrance : PlacementCheck.Allowed;
                Assert.That(run.CheckPlace(tile, MonsterKind.Goblin), Is.EqualTo(expected), tile.ToString());
            }
            Assert.That(run.CheckPlace(map.BossTile, MonsterKind.Ogre), Is.EqualTo(PlacementCheck.Allowed));
        }

        [Test]
        public void ClearingTheFirstStageLoadsTheSecondMapWithAnEmptyPlan()
        {
            var run = new DungeonRun();
            var firstMap = run.Map;
            run.Start();
            Drive(run, () => run.Stage == 2 || run.Phase == RunPhase.Defeat);
            Assert.That(run.Stage, Is.EqualTo(2));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Planning));
            Assert.That(run.Map, Is.Not.SameAs(firstMap));
            Assert.That(run.Map.Width, Is.Not.EqualTo(firstMap.Width));
            Assert.That(run.Placements, Is.Empty);
            Assert.That(run.Monsters, Is.Empty);
            Assert.That(run.LeaderTile, Is.EqualTo(run.Map.Entrance));
            Assert.That(run.IsSeen(run.Map.BossTile), Is.False);
            Assert.That(run.ReachedBoss, Is.False);
            Assert.That(run.Budget, Is.EqualTo(7));
            Assert.That(run.Party.All(h => h.Hp == h.MaxHp), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AdversityFreezesTimeAndConsumesOnlyOneAttempt(bool inCombat)
        {
            DungeonRun run;
            if (inCombat) run = RunInCombat();
            else
            {
                run = new DungeonRun();
                Assert.That(run.BeginAdversity(), Is.Zero);
                run.Start();
            }
            Assert.That(run.Phase, Is.EqualTo(inCombat ? RunPhase.Combat : RunPhase.Travel));
            int ticket = run.BeginAdversity();
            Assert.That(ticket, Is.Positive);
            Assert.That(run.BeginAdversity(), Is.Zero);
            Assert.That(run.TrialsLeft, Is.EqualTo(1));
            int ticks = run.Ticks;
            Assert.That(run.Tick(), Is.False, "nothing moves while the dice are out");
            Assert.That(run.Ticks, Is.EqualTo(ticks));
            Assert.That(run.ResolveAdversity(ticket + 1, HeroRole.Tank, 20), Is.False);
            int hp = run.Party[0].Hp;
            Assert.That(run.ResolveAdversity(ticket, HeroRole.Tank, 1, "방패로 막겠다"), Is.True);
            Assert.That(run.Party[0].Hp, Is.EqualTo(hp - 8));
            Assert.That(run.LastTrial, Does.Contain("방패로 막겠다").And.Contain("실패").And.Contain("힘 3"));
            Assert.That(run.ResolveAdversity(ticket, HeroRole.Tank, 20), Is.False);
            int second = run.BeginAdversity();
            Assert.That(run.ResolveAdversity(second, HeroRole.Tank, 10), Is.True);
            Assert.That(run.LastTrial, Does.Contain("성공"));
            Assert.That(run.Faith, Is.EqualTo(2));
            Assert.That(run.BeginAdversity(), Is.Zero);
            Assert.That(run.Tick(), Is.True);
        }

        [Test]
        public void BlessingRestoresTheMostHurtHeroForFaith()
        {
            var run = RunInCombat();
            Assert.That(run.CanBless, Is.False, "nobody is hurt yet");
            Drive(run, () => run.CanBless || run.Phase != RunPhase.Combat);
            Assert.That(run.CanBless, Is.True);
            var hurt = run.Party.Where(h => h.Hp > 0 && h.Hp < h.MaxHp).OrderBy(h => (double)h.Hp / h.MaxHp).First();
            int before = hurt.Hp;
            Assert.That(run.Bless(), Is.True);
            Assert.That(hurt.Hp, Is.GreaterThan(before));
            Assert.That(run.Faith, Is.Zero);
            Assert.That(run.Bless(), Is.False);
        }

        // Monsters on the first goblin-friendly tiles of each main room; the party meets most of them.
        private static readonly TilePoint[][] TrainingGrounds =
        {
            new[] { new TilePoint(16, 15), new TilePoint(5, 7), new TilePoint(17, 6), new TilePoint(19, 8) },
            new[] { new TilePoint(17, 11), new TilePoint(5, 9), new TilePoint(30, 19), new TilePoint(29, 5), new TilePoint(15, 12) }
        };

        [Test]
        public void BossPreviewMatchesTheBossTheStageFightsAndExperienceSplitsIntoLevels()
        {
            var run = new DungeonRun();
            Assert.That(run.Boss, Is.Null);
            string previewName = run.BossName;
            int previewHp = run.BossMaxHp;
            Assert.That(run.ExperienceIntoLevel, Is.Zero);
            run.Start();
            Assert.That(run.Boss.Name, Is.EqualTo(previewName));
            Assert.That(run.Boss.MaxHp, Is.EqualTo(previewHp));
            Drive(run, () => run.FightingBoss);
            Assert.That(run.FightingBoss, Is.True);
            Assert.That(run.BossName, Is.EqualTo(previewName));
            Assert.That(run.Level, Is.EqualTo(1 + run.Experience / DungeonRun.ExperiencePerLevel));
            Assert.That(run.ExperienceIntoLevel, Is.EqualTo(run.Experience % DungeonRun.ExperiencePerLevel));
        }

        [TestCase(true, RunPhase.Victory)]
        [TestCase(false, RunPhase.Defeat)]
        public void PlannedExpeditionCanWinButEmptyExpeditionLosesEvenWithPerfectTrials(
            bool placeMonsters, RunPhase expected)
        {
            var run = new DungeonRun();
            int trials = 0;
            int stageOneLevel = 0;
            int blessings = 0;
            for (int step = 0; step < 40000; step++)
            {
                if (run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat) break;
                if (run.Phase == RunPhase.Planning)
                {
                    if (run.Stage == 2)
                    {
                        stageOneLevel = run.Level;
                        Assert.That(run.Party.All(hero => hero.Hp == hero.MaxHp), Is.True);
                    }
                    if (placeMonsters)
                        foreach (var tile in TrainingGrounds[run.Stage - 1])
                            Assert.That(run.Place(tile, MonsterKind.Goblin), Is.True, tile.ToString());
                    run.Start();
                }
                // Spend one trial while exploring and save one for the stage boss.
                if (run.TrialsLeft == 2 || (run.TrialsLeft == 1 && run.FightingBoss))
                {
                    int ticket = run.BeginAdversity();
                    Assert.That(run.ResolveAdversity(ticket, HeroRole.Tank, 20), Is.True);
                    trials++;
                }
                if (run.Bless()) blessings++;
                if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                else run.Tick();
            }
            TestContext.Out.WriteLine($"level {run.Level}, stage-one level {stageOneLevel}, faith {run.Faith}, blessings {blessings}");
            foreach (var line in run.Log.Where(l => l.Contains("격파") || l.Contains("레벨") || l.Contains("전투 종료") || l.Contains("전멸") || l.Contains("쓰러짐")))
                TestContext.Out.WriteLine("  " + line);
            Assert.That(run.Stage, Is.EqualTo(2));
            Assert.That(trials, Is.EqualTo(4));
            Assert.That(run.Phase, Is.EqualTo(expected));
            if (placeMonsters)
            {
                Assert.That(run.Level, Is.GreaterThan(stageOneLevel));
                Assert.That(run.Party.Any(hero => hero.Hp > 0), Is.True);
            }
            else
            {
                Assert.That(run.Faith, Is.Zero);
                Assert.That(blessings, Is.EqualTo(5));
            }
        }

        [Test]
        public void SameSeedPlacementsAndChoicesReplayTheSameExpedition()
        {
            List<string> Play()
            {
                var run = new DungeonRun(seed: 7);
                var tiles = PlaceableTiles(run);
                run.Place(tiles[tiles.Count / 3], MonsterKind.Goblin);
                run.Place(tiles[tiles.Count / 2], MonsterKind.Ogre);
                run.Start();
                Drive(run, () => !InDungeon(run));
                return run.Log.ToList();
            }
            Assert.That(Play(), Is.EqualTo(Play()));
        }
    }
}
