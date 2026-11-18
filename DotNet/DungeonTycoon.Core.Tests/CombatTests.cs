using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class CombatTests
    {
        private static int Chebyshev(TilePoint a, TilePoint b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        /// <param name="fragile">
        /// Builds that strip armour and points, so the skeletons draw blood whatever the current balance and the healer
        /// has someone to heal.
        /// </param>
        private static DungeonRun Crowded(int seed = 11, bool fragile = false, bool pack = false)
        {
            var run = DungeonRun.Sandbox(seed);
            if (fragile)
                foreach (HeroRole role in Enum.GetValues(typeof(HeroRole)))
                    run.SetBuild(role, new HeroBuild(CombatTrait.Cornered, PersonalityTrait.Underdog));
            // Every first route walks the north corridor, so a pack there is always met together.
            var skeletons = pack
                ? new[] { new TilePoint(6, 11), new TilePoint(6, 11), new TilePoint(6, 11) }
                : new[] { new TilePoint(21, 23), new TilePoint(6, 11), new TilePoint(22, 7) };
            foreach (var tile in skeletons)
                Assert.That(run.Place(tile, MonsterKind.Skeleton), Is.True);
            run.Place(new TilePoint(25, 9), MonsterKind.Zombie);
            run.Start();
            return run;
        }

        [Test]
        public void AttackTracksLivingAlliesInTheMonstersCurrentRoom()
        {
            var run = DungeonRun.Sandbox();
            var roomTile = run.Map.BossTile;
            var tiles = DungeonRunTests.PlaceableTiles(run);
            int room = run.Map.RoomOf(roomTile);
            var sameRoom = tiles.First(t => t != roomTile && run.Map.RoomOf(t) == room);
            var otherRoom = tiles.First(t => run.Map.RoomOf(t) >= 0 && run.Map.RoomOf(t) != room);
            var corridor = tiles.First(t => run.Map.RoomOf(t) < 0);
            Assert.That(run.Place(sameRoom, MonsterKind.Skeleton), Is.True);
            Assert.That(run.Place(roomTile, MonsterKind.Ghost), Is.True);
            run.Start();
            var skeleton = run.Monsters[0];
            var ghost = run.Monsters[1];
            int bonus = BalanceTable.Current.attackPerRoomAlly;
            Assert.That(skeleton.Attack, Is.EqualTo(skeleton.BaseAttack + 2 * bonus));
            Assert.That(run.Boss.Attack, Is.EqualTo(run.Boss.BaseAttack + 2 * bonus));
            ghost.Tile = otherRoom;
            Assert.That(ghost.Attack, Is.EqualTo(ghost.BaseAttack));
            Assert.That(skeleton.Attack, Is.EqualTo(skeleton.BaseAttack + bonus));
            run.Boss.Mood = MonsterMood.Defeated;
            Assert.That(skeleton.Attack, Is.EqualTo(skeleton.BaseAttack));
            skeleton.Tile = ghost.Tile = corridor;
            Assert.That(skeleton.Attack, Is.EqualTo(skeleton.BaseAttack));
        }

        [Test]
        public void IdleMonstersWanderNearTheirPostAndAnIdleBossStaysInItsRoom()
        {
            var run = Crowded();
            bool roamed = false;
            DungeonRunTests.Drive(run, () =>
            {
                foreach (var monster in run.Monsters)
                {
                    if (!monster.Alive) continue;
                    if (!monster.IsBoss && monster.Mood == MonsterMood.Wandering && monster.Tile != monster.Home) roamed = true;
                    if (monster.IsBoss && monster.Mood == MonsterMood.Wandering)
                        Assert.That(run.Map.IsBossRoom(monster.Tile), Is.True, "an idle boss left its room");
                    if (monster.Mood == MonsterMood.Wandering)
                    {
                        var within = run.Map.Distances(monster.Home, run.Map.IsFloor, DungeonRun.WanderRadius);
                        Assert.That(within.ContainsKey(monster.Tile), Is.True, $"{monster.Name} strayed to {monster.Tile}");
                    }
                }
                return !DungeonRunTests.InDungeon(run);
            });
            Assert.That(roamed, Is.True, "idle monsters should move around their post");
        }

        [Test]
        public void MonstersNeverGiveUpTheHuntAndTheBossChasesOutOfItsRoom()
        {
            var run = DungeonRun.Sandbox();
            var hunting = new HashSet<int>();
            bool bossLeftRoom = false;
            DungeonRunTests.Drive(run, () =>
            {
                if (run.Phase == RunPhase.Planning) run.Start();
                foreach (var monster in run.Monsters)
                {
                    int key = run.Stage * 1000 + monster.Id;
                    if (monster.Mood == MonsterMood.Chasing) hunting.Add(key);
                    else if (hunting.Contains(key))
                        Assert.That(monster.Mood, Is.EqualTo(MonsterMood.Defeated), $"{monster.Name} stopped chasing");
                    if (monster.IsBoss && monster.Mood == MonsterMood.Chasing && !run.Map.IsBossRoom(monster.Tile)) bossLeftRoom = true;
                }
                return run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat;
            }, 60000);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Defeat), "an empty dungeon still ends, in defeat at the latest");
            Assert.That(bossLeftRoom, Is.True, "the boss should follow heroes who stand outside its room");
        }

        [Test]
        public void SpottingAHeroStartsAFightThatStopsExplorationUntilEveryChaserIsDown()
        {
            var run = DungeonRunTests.RunInCombat();
            Assert.That(run.Engaged.Count(), Is.Positive);
            Assert.That(run.PendingFork, Is.Null);
            Assert.That(run.Log.Any(l => l.StartsWith("교전")), Is.True);
            int steps = run.StepsTaken;
            DungeonRunTests.Drive(run, () => run.Phase != RunPhase.Combat);
            Assert.That(run.StepsTaken, Is.EqualTo(steps), "the party does not explore while fighting");
            Assert.That(run.Engaged, Is.Empty);
            Assert.That(run.Log.Any(l => l.StartsWith("전투 종료") || l.Contains("스테이지 클리어") || l.StartsWith("파티 전멸")), Is.True);
        }

        [Test]
        public void RolesFightFromTheirOwnPositionsAndTheHealerHeals()
        {
            var run = Crowded(fragile: true);
            var gaps = new Dictionary<HeroRole, List<int>>();
            foreach (HeroRole role in Enum.GetValues(typeof(HeroRole))) gaps[role] = new List<int>();
            var seen = new List<CombatEvent>();
            DungeonRunTests.Drive(run, () =>
            {
                seen.AddRange(run.TakeEvents());
                if (run.Phase == RunPhase.Combat)
                    foreach (var hero in run.Party.Where(h => h.Hp > 0))
                        gaps[hero.Role].Add(run.Engaged.Min(m => Chebyshev(m.Tile, run.HeroTile(hero.Role))));
                return run.Stage == 2 || !DungeonRunTests.InDungeon(run);
            });
            Assert.That(gaps[HeroRole.RangedDamage].Average(), Is.GreaterThan(gaps[HeroRole.MeleeDamage].Average()));
            Assert.That(gaps[HeroRole.Healer].Average(), Is.GreaterThan(gaps[HeroRole.Tank].Average()));
            Assert.That(seen.Any(e => e.Kind == CombatEventKind.Heal && e.FromHero && e.Source == (int)HeroRole.Healer), Is.True);
            Assert.That(seen.Any(e => e.Kind == CombatEventKind.Attack && e.FromHero && e.Source == (int)HeroRole.RangedDamage), Is.True);
        }

        [Test]
        public void TauntedMonstersHitOnlyTheTankUntilTheTauntWearsOff()
        {
            int tauntCount = 0, violations = 0, checkedHits = 0;
            for (int seed = 1; seed <= 4; seed++)
            {
                var run = Crowded(seed, pack: true);
                var taunts = new Dictionary<int, int>();
                DungeonRunTests.Drive(run, () =>
                {
                    foreach (var e in run.TakeEvents())
                    {
                        if (e.Kind == CombatEventKind.Taunt)
                        {
                            taunts[e.Target] = run.Ticks + DungeonRun.TauntTicks;
                            tauntCount++;
                        }
                        // Telegraphed patterns hit everyone in their area; the taunt rule is about plain blows, so only
                        // direct hits are checked.
                        if (e.Kind == CombatEventKind.Attack && !e.FromHero && !e.IsPatternDamage &&
                            taunts.TryGetValue(e.Source, out int until) &&
                            run.Ticks < until && run.Party[(int)HeroRole.Tank].Hp > 0)
                        {
                            checkedHits++;
                            if (e.Target != (int)HeroRole.Tank) violations++;
                        }
                    }
                    return run.Stage == 2 || !DungeonRunTests.InDungeon(run);
                });
            }
            Assert.That(tauntCount, Is.Positive, "the tank should taunt when monsters gather");
            Assert.That(checkedHits, Is.Positive);
            Assert.That(violations, Is.Zero);
        }

        [Test]
        public void TacticsAnswerOnlyTheirSituationAndInvalidEntriesAreIgnored()
        {
            var run = DungeonRunTests.RunInCombat(MonsterKind.Zombie);
            int situation = run.Situation;
            var foe = run.Engaged.First();
            Assert.That(run.ApplyTactics(situation - 1, new[] { new HeroTactic(HeroRole.RangedDamage, foe.Id, -1, "늦은 작전") }), Is.False);

            string before = run.Party[(int)HeroRole.MeleeDamage].LastLine;
            Assert.That(run.ApplyTactics(situation, new[]
            {
                new HeroTactic(HeroRole.RangedDamage, foe.Id, -1, "좀비부터 노린다!"),
                new HeroTactic(HeroRole.MeleeDamage, 999, -1, "없는 적"),
                new HeroTactic(HeroRole.Healer, -1, (int)HeroRole.Tank, "가론은 내가 지킬게요.")
            }), Is.True);
            Assert.That(run.TacticOf(HeroRole.RangedDamage).Focus, Is.EqualTo(foe.Id));
            Assert.That(run.Party[(int)HeroRole.RangedDamage].LastLine, Is.EqualTo("좀비부터 노린다!"));
            Assert.That(run.TacticOf(HeroRole.MeleeDamage), Is.Null);
            Assert.That(run.Party[(int)HeroRole.MeleeDamage].LastLine, Is.EqualTo(before));
            Assert.That(run.TacticOf(HeroRole.Healer).Protect, Is.EqualTo((int)HeroRole.Tank));

            // While the focus target is engaged, the ranged hero's shots go to it.
            run.TakeEvents();
            for (int i = 0; i < 60 && run.Situation == situation && foe.Alive; i++) run.Tick();
            var shots = run.TakeEvents().Where(e => e.Kind == CombatEventKind.Attack && e.FromHero && e.Source == (int)HeroRole.RangedDamage).ToList();
            Assert.That(shots.All(e => e.Target == foe.Id), Is.True);
        }

        [Test]
        public void DecidedTacticsDropUnsureOrMissingTargetsAndSpeakLocalLines()
        {
            var run = DungeonRunTests.RunInCombat(MonsterKind.Zombie);
            var foe = run.Engaged.First();
            double sure = DungeonRun.TacticConfidenceFloor, unsure = DungeonRun.TacticConfidenceFloor - 0.01;

            var strike = run.DecidedTactic(HeroRole.MeleeDamage, foe.Id, sure, -1, 1);
            Assert.That(strike.Focus, Is.EqualTo(foe.Id));
            Assert.That(strike.Line, Does.Contain(foe.Name));

            Assert.That(run.DecidedTactic(HeroRole.MeleeDamage, foe.Id, unsure, -1, 1), Is.Null, "an unsure target is left to the local rules");
            Assert.That(run.DecidedTactic(HeroRole.MeleeDamage, 999, 1, -1, 1), Is.Null, "a target that is not in the fight is dropped");

            // The healer keeps a sure ward even when the focus is unsure; the line names the ward.
            var tank = run.Party[(int)HeroRole.Tank];
            var heal = run.DecidedTactic(HeroRole.Healer, foe.Id, unsure, (int)HeroRole.Tank, sure);
            Assert.That(heal.Focus, Is.EqualTo(-1));
            Assert.That(heal.Protect, Is.EqualTo((int)HeroRole.Tank));
            Assert.That(heal.Line, Does.Contain(tank.Name));

            Assert.That(run.ApplyTactics(run.Situation, new[] { strike, heal }), Is.True);
            Assert.That(run.TacticOf(HeroRole.MeleeDamage), Is.SameAs(strike));
            Assert.That(run.Party[(int)HeroRole.Healer].LastLine, Is.EqualTo(heal.Line));
        }

        [Test]
        public void ADebateFreezesTheWholeDungeonUntilTheForkIsSettled()
        {
            var blind = DungeonRun.Sandbox();
            blind.Start();
            Assert.That(ExplorationTests.TickToFirstFork(blind), Is.True);
            var leader = blind.LeaderTile;
            // Skeletons just outside notice range would wander into sight if the dungeon kept moving.
            var spots = DungeonRunTests.PlaceableTiles(blind)
                .Where(t => Chebyshev(t, leader) > DungeonRun.DetectRadius && Chebyshev(t, leader) <= DungeonRun.DetectRadius + DungeonRun.WanderRadius)
                .Take(6).ToList();
            Assert.That(spots, Is.Not.Empty);
            var run = DungeonRun.Sandbox();
            foreach (var spot in spots) run.Place(spot, MonsterKind.Skeleton);
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);

            int ticks = run.Ticks;
            var tiles = run.Monsters.Select(m => m.Tile).ToList();
            for (int i = 0; i < 400; i++) Assert.That(run.Tick(), Is.False);
            Assert.That(run.Ticks, Is.EqualTo(ticks), "no time passes during the debate");
            Assert.That(run.Monsters.Select(m => m.Tile), Is.EqualTo(tiles), "monsters wait too");
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Travel));
            Assert.That(run.PendingFork, Is.Not.Null);

            Assert.That(run.ResolveFork(run.PendingFork.Token), Is.True);
            Assert.That(run.Tick(), Is.True, "time flows again once the party has chosen");
        }
    }
}
