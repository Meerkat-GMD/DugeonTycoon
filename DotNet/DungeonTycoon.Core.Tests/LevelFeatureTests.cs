using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>Stage rosters, fixed patrols, treasure chests and springs.</summary>
    public class LevelFeatureTests
    {
        private static bool Adjacent(TilePoint a, TilePoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

        /// <summary>Clears stages with local decisions until the given stage is open for planning.</summary>
        private static DungeonRun AtStage(DungeonRun run, int stage)
        {
            while (run.Stage < stage)
            {
                int next = run.Stage + 1;
                run.Start();
                DungeonRunTests.Drive(run, () => (run.Stage == next && run.PendingLoot == null) || run.Phase == RunPhase.Defeat);
                Assert.That(run.Stage, Is.EqualTo(next));
            }
            return run;
        }

        [Test]
        public void EachStageRosterAddsOneKindToThePreviousOne()
        {
            Assert.That(DungeonRun.RosterFor(1), Is.EqualTo(new[] { MonsterKind.Skeleton, MonsterKind.Ghost }));
            for (int stage = 2; stage <= DungeonRun.FinalStage; stage++)
            {
                var before = DungeonRun.RosterFor(stage - 1);
                var now = DungeonRun.RosterFor(stage);
                Assert.That(now.Count, Is.EqualTo(before.Count + 1));
                Assert.That(now.Take(before.Count), Is.EqualTo(before));
            }
            Assert.That(DungeonRun.RosterFor(2).Last(), Is.EqualTo(MonsterKind.Zombie));
            Assert.That(DungeonRun.RosterFor(3).Last(), Is.EqualTo(MonsterKind.Archer));
        }

        [Test]
        public void OnlyRosterMonstersCanBePlaced()
        {
            var run = new DungeonRun();
            var tile = DungeonRunTests.PlaceableTiles(run)[0];
            Assert.That(run.CheckPlace(tile, MonsterKind.Zombie), Is.EqualTo(PlacementCheck.NotInRoster));
            Assert.That(run.CheckPlace(tile, MonsterKind.Archer), Is.EqualTo(PlacementCheck.NotInRoster));
            Assert.That(run.Place(tile, MonsterKind.Ghost), Is.True);
            Assert.That(run.Budget, Is.EqualTo(DungeonRun.BudgetFor(1) - DungeonRun.Cost(MonsterKind.Ghost)));
        }

        [Test]
        public void SameMonsterGrowsStrongerOnLaterStages()
        {
            foreach (var kind in new[] { MonsterKind.Skeleton, MonsterKind.Ghost, MonsterKind.Zombie, MonsterKind.Archer })
                for (int stage = 2; stage <= DungeonRun.FinalStage; stage++)
                {
                    var before = DungeonRun.MonsterStats(kind, stage - 1);
                    var now = DungeonRun.MonsterStats(kind, stage);
                    Assert.That(now.Hp, Is.GreaterThan(before.Hp), kind.ToString());
                    Assert.That(now.Attack, Is.GreaterThan(before.Attack), kind.ToString());
                }
        }

        [Test]
        public void ThePartyClearsThreeStagesBeforeTheFinalBoss()
        {
            var run = AtStage(DungeonRun.Sandbox(features: true), DungeonRun.FinalStage);
            Assert.That(run.BossName, Is.EqualTo("네크로맨서"));
            Assert.That(run.Map.Width, Is.EqualTo(DungeonMap.ForStage(3).Width));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PatrolRoutesAreClosedWalksClearOfTheEntranceAndTheBossRoom(int stage)
        {
            var run = AtStage(DungeonRun.Sandbox(features: true), stage);
            // A sandbox has no patrols; build the real ones from the same map.
            var real = new DungeonRun();
            var plans = DungeonLayouts.PatrolsForStage(stage);
            Assert.That(plans, Is.Not.Empty);
            foreach (var plan in plans)
            {
                Assert.That(plan.Members.All(kind => DungeonRun.RosterFor(stage).Contains(kind)), Is.True, "patrol members come from the roster");
                Assert.That(plan.Members.Count, Is.InRange(2, 3));
            }
            if (stage == 1)
                foreach (var squad in real.Patrols)
                {
                    for (int i = 0; i < squad.Route.Count; i++)
                    {
                        var tile = squad.Route[i];
                        Assert.That(real.PatrolWalkable(tile), Is.True, tile.ToString());
                        Assert.That(Adjacent(tile, squad.Route[(i + 1) % squad.Route.Count]), Is.True, $"route breaks after {tile}");
                    }
                }
            Assert.That(run.Map.Chests.Count, Is.InRange(2, 3));
            Assert.That(run.Map.Springs.Count, Is.EqualTo(stage == 1 ? 0 : 1));
        }

        [Test]
        public void PatrolsJoinTheMonsterListBetweenPlacementsAndTheBossAndWalkTheirRoute()
        {
            var run = new DungeonRun();
            var tile = DungeonRunTests.PlaceableTiles(run).Last();
            run.Place(tile, MonsterKind.Skeleton);
            var squad = run.Patrols.Single();
            var start = squad.Members.Select((_, k) => squad.StartOf(k)).ToList();
            Assert.That(run.BossIndex, Is.EqualTo(1 + squad.Members.Count));
            run.Start();
            Assert.That(run.Monsters.Count, Is.EqualTo(2 + squad.Members.Count));
            Assert.That(run.Monsters[0].IsPatrol, Is.False);
            for (int k = 0; k < squad.Members.Count; k++)
            {
                var member = run.Monsters[1 + k];
                Assert.That(member.Squad, Is.EqualTo(squad.Index));
                Assert.That(member.Tile, Is.EqualTo(start[k]));
                Assert.That(member.Mood, Is.EqualTo(MonsterMood.Patrolling));
            }
            Assert.That(run.Monsters.Last().IsBoss, Is.True);
            Assert.That(run.Monsters.Last().Id, Is.EqualTo(run.BossIndex));

            var visited = new HashSet<TilePoint>();
            for (int tick = 0; tick < 400 && run.Monsters.Where(m => m.IsPatrol).All(m => m.Mood == MonsterMood.Patrolling); tick++)
            {
                if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                else run.Tick();
                foreach (var member in run.Monsters.Where(m => m.IsPatrol && m.Mood == MonsterMood.Patrolling))
                {
                    Assert.That(squad.Route.Contains(member.Tile), Is.True, $"{member.Name} left its route at {member.Tile}");
                    visited.Add(member.Tile);
                }
            }
            Assert.That(visited.Count, Is.GreaterThan(squad.Members.Count), "the squad walks on");
        }

        [Test]
        public void WhenOnePatrolMemberSpotsTheHeroesTheWholeSquadHunts()
        {
            var run = new DungeonRun();
            run.Start();
            var members = run.Monsters.Where(m => m.IsPatrol).ToList();
            DungeonRunTests.Drive(run, () => members.Any(m => m.Mood == MonsterMood.Chasing) || !DungeonRunTests.InDungeon(run), 40000);
            Assert.That(members.Any(m => m.Mood == MonsterMood.Chasing), Is.True, "the party meets the patrol on its way");
            Assert.That(members.All(m => m.Mood != MonsterMood.Patrolling), Is.True, "nobody stays on its rounds");
        }

        [Test]
        public void ASkeletonArcherShootsFromRange()
        {
            var run = AtStage(DungeonRun.Sandbox(features: true), 3);
            Assert.That(run.CheckPlace(run.Map.BossTile, MonsterKind.Archer), Is.EqualTo(PlacementCheck.Allowed));
            run.Place(run.Map.BossTile, MonsterKind.Archer);
            run.Start();
            var archer = run.Monsters[0];
            Assert.That(archer.Range, Is.EqualTo(DungeonRun.ArcherRange));
            var shots = new List<int>();
            DungeonRunTests.Drive(run, () =>
            {
                foreach (var e in run.TakeEvents())
                    if (e.Kind == CombatEventKind.Attack && !e.FromHero && e.Source == archer.Id)
                    {
                        var target = run.HeroTile((HeroRole)e.Target);
                        shots.Add(Math.Max(Math.Abs(target.X - archer.Tile.X), Math.Abs(target.Y - archer.Tile.Y)));
                    }
                return !archer.Alive || !DungeonRunTests.InDungeon(run);
            });
            Assert.That(shots, Is.Not.Empty);
            Assert.That(shots.Max(), Is.GreaterThan(1), "the archer hits from beyond melee reach");
        }

        [Test]
        public void AChestOpenedOnTheMarchGivesItsItemThroughTheLootRules()
        {
            var run = DungeonRun.Sandbox(features: true);
            var weapon = EquipmentCatalog.Common.First(item => item.Slot == EquipmentSlot.Weapon && item.Role == HeroRole.MeleeDamage);
            run.ChestOverride = chest => weapon;
            run.Start();
            var events = new List<CombatEvent>();
            DungeonRunTests.Drive(run, () =>
            {
                events.AddRange(run.TakeEvents());
                return run.Chests.Any(c => c.Opened) || run.FightingBoss || !DungeonRunTests.InDungeon(run);
            });
            var opened = run.Chests.FirstOrDefault(c => c.Opened);
            Assert.That(opened, Is.Not.Null, "the party opens a chest on stage 1");
            Assert.That(opened.Item, Is.SameAs(weapon));
            var cue = events.Single(e => e.Kind == CombatEventKind.Chest);
            Assert.That(cue.Source, Is.EqualTo(opened.Index));
            Assert.That(run.Drops[cue.Amount], Is.SameAs(weapon));
            Assert.That(run.Party[(int)HeroRole.MeleeDamage].Weapon, Is.SameAs(weapon), "a weapon goes straight to its one owner");
            Assert.That(run.Log.Any(l => l.StartsWith("보물 상자")), Is.True);
        }

        [Test]
        public void ChestsHoldTheSameItemsForTheSameSeed()
        {
            List<string> Items()
            {
                var run = DungeonRun.Sandbox(seed: 3, features: true);
                run.Start();
                DungeonRunTests.Drive(run, () => run.FightingBoss || !DungeonRunTests.InDungeon(run));
                return run.Chests.Select(c => c.Item?.Name ?? "-").ToList();
            }
            Assert.That(Items(), Is.EqualTo(Items()));
        }

        [Test]
        public void ASeenChestBecomesAWayToArgueOverAndTheGreedyWantIt()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var run = DungeonRun.Sandbox(seed, features: true);
                run.SetBuild(HeroRole.MeleeDamage, run.BuildOf(HeroRole.MeleeDamage).With(PersonalityTrait.Greedy));
                run.Start();
                ForkDiscussion fork = null;
                DungeonRunTests.Drive(run, () =>
                {
                    if (run.PendingFork != null && run.PendingFork.Options.Any(o => o.Feature == ForkFeature.Chest)) fork = run.PendingFork;
                    return fork != null || run.FightingBoss || !DungeonRunTests.InDungeon(run);
                });
                if (fork == null) continue;
                var chest = fork.Options.First(o => o.Feature == ForkFeature.Chest);
                Assert.That(chest.Label, Does.EndWith("상자 길"));
                Assert.That(run.Chests[chest.FeatureIndex].Opened, Is.False);
                var greedy = fork.LocalOpinions.Single(o => o.Role == HeroRole.MeleeDamage);
                Assert.That(fork.Options[greedy.Option].Feature, Is.EqualTo(ForkFeature.Chest), "the greedy hero argues for the chest");
                return;
            }
            Assert.Fail("no seed brought a chest into a fork discussion");
        }

        [Test]
        public void ATiredPartyDrinksFromASpringButARestedOneLeavesIt()
        {
            foreach (bool tired in new[] { true, false })
            {
                var run = AtStage(DungeonRun.Sandbox(features: true), 2);
                run.Start();
                var spring = run.Springs.Single();
                var beside = new[] { new TilePoint(1, 0), new TilePoint(-1, 0), new TilePoint(0, 1), new TilePoint(0, -1) }
                    .Select(d => new TilePoint(spring.Tile.X + d.X, spring.Tile.Y + d.Y)).First(run.Map.IsFloor);
                foreach (HeroRole role in Enum.GetValues(typeof(HeroRole))) run.PlaceHero(role, beside);
                int before = tired ? run.FatigueFirstThreshold + 20 : 0;
                run.SetFatigueSteps(before);
                var events = new List<CombatEvent>();
                for (int tick = 0; tick < 300 && !spring.Used && run.Phase == RunPhase.Travel; tick++)
                {
                    if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
                    else run.Tick();
                    events.AddRange(run.TakeEvents());
                }
                if (tired)
                {
                    Assert.That(spring.Used, Is.True, "a tired party goes for the spring it sees");
                    var cue = events.Single(e => e.Kind == CombatEventKind.Spring);
                    Assert.That(cue.Amount, Is.EqualTo(DungeonRun.SpringRelief));
                    Assert.That(run.FatigueSteps, Is.LessThan(before));
                    Assert.That(run.Party.All(h => h.Hp == h.MaxHp), Is.True, "a spring eases fatigue, never health");
                }
                else Assert.That(spring.Used, Is.False, "a rested party saves the spring");
            }
        }
    }
}
