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

        /// <summary>Runs the expedition with local decisions until the condition holds or nothing can advance.</summary>
        internal static void Drive(DungeonRun run, Func<bool> until, int limit = 5000)
        {
            for (int tick = 0; tick < limit && !until(); tick++)
            {
                if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                else if (run.Phase == RunPhase.Travel) run.Step();
                else if (run.Phase == RunPhase.Combat) run.ResolveRound(run.RoundToken);
                else return;
            }
        }

        /// <summary>A goblin on the boss tile is always met right before the boss, whatever way the party takes.</summary>
        private static DungeonRun RunInCombat()
        {
            var run = new DungeonRun();
            run.Place(run.Map.BossTile, MonsterKind.Goblin);
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
            Assert.That(run.LeaderTile, Is.EqualTo(run.Map.Entrance));
            Assert.That(run.IsSeen(run.Map.BossTile), Is.False);
            Assert.That(run.ReachedBoss, Is.False);
            Assert.That(run.Budget, Is.EqualTo(7));
        }

        [Test]
        public void LateAndDuplicateDecisionsCannotApplyAnExtraRound()
        {
            var run = RunInCombat();
            int before = run.RoundToken;
            Assert.That(run.ResolveRound(before), Is.True);
            int enemyHp = run.Enemy.Hp;
            int partyHp = run.Party.Sum(hero => hero.Hp);
            Assert.That(run.ResolveRound(before), Is.False);
            Assert.That(run.Enemy.Hp, Is.EqualTo(enemyHp));
            Assert.That(run.Party.Sum(hero => hero.Hp), Is.EqualTo(partyHp));
        }

        [Test]
        public void HealingIntentIsRevalidatedAfterAnImmediateBlessing()
        {
            var run = RunInCombat();
            run.ResolveRound(run.RoundToken);
            Assert.That(run.Bless(), Is.True);
            Assert.That(run.Party[0].Hp, Is.EqualTo(run.Party[0].MaxHp));
            Assert.That(run.Bless(), Is.False);
            var heal = new HeroDecision(HeroRole.Healer, HeroAction.Heal, HeroRole.Tank, "치유");
            run.ResolveRound(run.RoundToken, new[] { heal });
            Assert.That(run.Party[1].LastAction, Is.EqualTo(HeroAction.Attack));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AdversityWorksInTravelAndCombatAndConsumesOnlyOneAttempt(bool inCombat)
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
            Assert.That(run.Step(), Is.False);
            Assert.That(run.ResolveRound(run.RoundToken), Is.False);
            Assert.That(run.ResolveAdversity(ticket + 1, HeroRole.Tank, 20), Is.False);
            int hp = run.Party[0].Hp;
            Assert.That(run.ResolveAdversity(ticket, HeroRole.Tank, 1, "방패로 막겠다"), Is.True);
            Assert.That(run.Party[0].Hp, Is.EqualTo(hp - 8));
            Assert.That(run.LastTrial, Does.Contain("방패로 막겠다").And.Contain("실패").And.Contain("힘 3"));
            Assert.That(run.ResolveAdversity(ticket, HeroRole.Tank, 20), Is.False);
            int second = run.BeginAdversity();
            Assert.That(run.ResolveAdversity(second, HeroRole.Tank, 10), Is.True);
            Assert.That(run.LastTrial, Does.Contain("성공"));
            Assert.That(run.Experience, Is.EqualTo(1));
            Assert.That(run.Faith, Is.EqualTo(2));
            Assert.That(run.BeginAdversity(), Is.Zero);
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
            for (int step = 0; step < 5000; step++)
            {
                if (run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat) break;
                if (run.Phase == RunPhase.Planning)
                {
                    if (run.Stage == 2)
                    {
                        stageOneLevel = run.Level;
                        Assert.That(run.Party.All(hero => hero.Hp == hero.MaxHp), Is.True);
                    }
                    // Monsters on the boss tile are met on every route, just before the boss.
                    if (placeMonsters)
                        for (int i = 0; i < 4; i++) run.Place(run.Map.BossTile, MonsterKind.Goblin);
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
                else if (run.Phase == RunPhase.Travel) run.Step();
                else run.ResolveRound(run.RoundToken);
            }
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
                Drive(run, () => run.Phase != RunPhase.Travel && run.Phase != RunPhase.Combat);
                return run.Log.ToList();
            }
            Assert.That(Play(), Is.EqualTo(Play()));
        }
    }
}
