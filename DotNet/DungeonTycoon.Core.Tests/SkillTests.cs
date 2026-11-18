using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class SkillTests
    {
        private const int Never = int.MaxValue / 2;

        /// <summary>
        /// A fight in the open stage-one boss room (x 22-27, y 2-7) against the given number of goblins. The boss
        /// is taken out of play, every hero is at full health and frozen, and every goblin is engaged but frozen,
        /// so a test lets only the actors it cares about move.
        /// </summary>
        private static DungeonRun Arena(int goblins, int level = 1)
        {
            var run = new DungeonRun();
            for (int i = 0; i < goblins; i++) Assert.That(run.Place(run.Map.BossTile, MonsterKind.Goblin), Is.True);
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Combat));
            int needed = (level - 1) * DungeonRun.ExperiencePerLevel - run.Experience;
            if (needed > 0) run.GrantExperience(needed);
            Assert.That(run.Level, Is.EqualTo(level));
            foreach (var monster in run.Monsters)
            {
                monster.Mood = monster.IsBoss ? MonsterMood.Defeated : MonsterMood.Chasing;
                Freeze(monster);
            }
            foreach (var hero in run.Party)
            {
                hero.Hp = hero.MaxHp;
                hero.Critical = false;
                Freeze(hero);
            }
            run.TakeEvents();
            return run;
        }

        private static void Freeze(HeroState hero)
        {
            hero.MoveReady = hero.ActReady = Never;
            hero.SkillReady[0] = hero.SkillReady[1] = Never;
        }

        private static void Freeze(MonsterState monster) => monster.MoveReady = monster.AttackReady = Never;

        /// <summary>Lets the hero act now with only the listed skills ready.</summary>
        private static void Ready(HeroState hero, params int[] skills)
        {
            hero.MoveReady = hero.ActReady = 0;
            foreach (int index in skills) hero.SkillReady[index] = 0;
        }

        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];

        private static MonsterState Goblin(DungeonRun run, int id) => run.Monsters.Single(m => m.Id == id);

        private static void Put(DungeonRun run, HeroRole role, int x, int y)
        {
            var tile = new TilePoint(x, y);
            Assert.That(run.Map.IsFloor(tile), Is.True, tile.ToString());
            run.PlaceHero(role, tile);
        }

        private static void Put(MonsterState monster, int x, int y) => monster.Tile = new TilePoint(x, y);

        private static List<CombatEvent> Skills(IEnumerable<CombatEvent> events, HeroSkill skill) =>
            events.Where(e => e.Kind == CombatEventKind.Skill && e.FromHero && e.Amount == (int)skill).ToList();

        private static List<CombatEvent> HitsBy(IEnumerable<CombatEvent> events, HeroRole role) =>
            events.Where(e => e.Kind == CombatEventKind.Attack && e.FromHero && e.Source == (int)role).ToList();

        private static int NormalHit(DungeonRun run, HeroRole role, MonsterState monster) =>
            Math.Max(0, Hero(run, role).Attack + 2 * (run.Level - 1) - monster.Armor);

        [Test]
        public void TheSecondSkillOpensAtTheUnlockLevel()
        {
            var run = new DungeonRun();
            foreach (HeroRole role in Enum.GetValues(typeof(HeroRole)))
            {
                Assert.That(run.SkillUnlocked(role, 0), Is.True);
                Assert.That(run.SkillUnlocked(role, 1), Is.False);
            }
            run.GrantExperience(DungeonRun.ExperiencePerLevel);
            Assert.That(run.Level, Is.EqualTo(HeroSkills.UnlockLevel - 1));
            Assert.That(run.SkillUnlocked(HeroRole.Healer, 1), Is.False);
            run.GrantExperience(DungeonRun.ExperiencePerLevel);
            Assert.That(run.Level, Is.EqualTo(HeroSkills.UnlockLevel));
            foreach (HeroRole role in Enum.GetValues(typeof(HeroRole)))
                Assert.That(run.SkillUnlocked(role, 1), Is.True);
            Assert.That(DungeonRun.SkillCooldownTicks(HeroSkill.Taunt), Is.EqualTo(DungeonRun.TauntCooldownTicks));
            Assert.That(DungeonRun.SkillCooldownTicks(HeroSkill.Revive), Is.Zero);
        }

        [Test]
        public void ALockedSkillNeverFires()
        {
            var run = Arena(2);
            Put(run, HeroRole.MeleeDamage, 24, 4);
            Put(Goblin(run, 0), 25, 4);
            Put(Goblin(run, 1), 23, 4);
            Ready(Hero(run, HeroRole.MeleeDamage), 1);
            run.Tick();
            var events = run.TakeEvents();
            Assert.That(Skills(events, HeroSkill.Whirlwind), Is.Empty);
            Assert.That(HitsBy(events, HeroRole.MeleeDamage).Count, Is.EqualTo(1), "a plain attack instead");
        }

        [Test]
        public void TheTankTauntsThroughTheSkillFramework()
        {
            var run = Arena(2);
            var tank = Hero(run, HeroRole.Tank);
            Put(run, HeroRole.Tank, 24, 4);
            Put(Goblin(run, 0), 26, 4);
            Put(Goblin(run, 1), 24, 6);
            tank.SkillReady[0] = 0;
            run.Tick();
            var events = run.TakeEvents();
            var skill = Skills(events, HeroSkill.Taunt).Single();
            Assert.That(skill.Source, Is.EqualTo((int)HeroRole.Tank));
            Assert.That(events.Where(e => e.Kind == CombatEventKind.Taunt).Select(e => e.Target), Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(run.SkillCooldownLeft(HeroRole.Tank, 0), Is.EqualTo(DungeonRun.TauntCooldownTicks));
            Assert.That(run.Log.Last(l => l.Contains("도발")), Does.StartWith($"{tank.Name} • 도발!"));
            Assert.That(tank.LastLine, Is.Not.Empty);

            run.Tick();
            Assert.That(run.SkillCooldownLeft(HeroRole.Tank, 0), Is.EqualTo(DungeonRun.TauntCooldownTicks - 1));
            Assert.That(Skills(run.TakeEvents(), HeroSkill.Taunt), Is.Empty, "not again while cooling down");
        }

        [Test]
        public void APowerStrikeHitsHarderThanTheNextPlainAttack()
        {
            var run = Arena(1);
            var melee = Hero(run, HeroRole.MeleeDamage);
            var goblin = Goblin(run, 0);
            goblin.Hp = 10000;
            Put(run, HeroRole.MeleeDamage, 24, 4);
            Put(goblin, 25, 4);
            Ready(melee, 0);
            run.Tick();
            var first = run.TakeEvents();
            var skill = Skills(first, HeroSkill.PowerStrike).Single();
            Assert.That(skill.Target, Is.EqualTo(goblin.Id));
            int normal = NormalHit(run, HeroRole.MeleeDamage, goblin);
            Assert.That(HitsBy(first, HeroRole.MeleeDamage).Single().Amount, Is.EqualTo(normal * DungeonRun.PowerStrikeDamagePercent / 100));
            Assert.That(run.Log.Any(l => l.StartsWith($"{melee.Name} • 강타!")), Is.True);

            melee.ActReady = 0;
            run.Tick();
            int plain = HitsBy(run.TakeEvents(), HeroRole.MeleeDamage).Single().Amount;
            Assert.That(plain, Is.EqualTo(normal));
            Assert.That(normal * DungeonRun.PowerStrikeDamagePercent / 100, Is.GreaterThan(plain));
        }

        [Test]
        public void WhirlwindHitsEveryAdjacentMonsterAndNoOneFurther()
        {
            var run = Arena(3, HeroSkills.UnlockLevel);
            Put(run, HeroRole.MeleeDamage, 24, 4);
            Put(Goblin(run, 0), 25, 4);
            Put(Goblin(run, 1), 23, 5);
            Put(Goblin(run, 2), 24, 6);
            Ready(Hero(run, HeroRole.MeleeDamage), 1);
            run.Tick();
            var events = run.TakeEvents();
            Assert.That(Skills(events, HeroSkill.Whirlwind).Count, Is.EqualTo(1));
            Assert.That(HitsBy(events, HeroRole.MeleeDamage).Select(e => e.Target), Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(run.SkillCooldownLeft(HeroRole.MeleeDamage, 1), Is.EqualTo(DungeonRun.WhirlwindCooldownTicks));
        }

        [Test]
        public void APiercingShotAlsoHitsTheMonsterBehindTheTarget()
        {
            var run = Arena(3);
            Put(run, HeroRole.RangedDamage, 22, 4);
            Put(Goblin(run, 0), 24, 4);
            Put(Goblin(run, 1), 26, 4);
            Put(Goblin(run, 2), 23, 6);
            Ready(Hero(run, HeroRole.RangedDamage), 0);
            run.Tick();
            var events = run.TakeEvents();
            var skill = Skills(events, HeroSkill.PiercingShot).Single();
            Assert.That(skill.Target, Is.EqualTo(0));
            Assert.That(HitsBy(events, HeroRole.RangedDamage).Select(e => e.Target), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void APiercingShotKeepsToTheFocusEvenWhenAnotherLineWouldPierce()
        {
            var run = Arena(3);
            var ranged = Hero(run, HeroRole.RangedDamage);
            Put(run, HeroRole.RangedDamage, 22, 4);
            Put(Goblin(run, 0), 24, 4);
            Put(Goblin(run, 1), 26, 4);
            Put(Goblin(run, 2), 23, 6);
            Assert.That(run.ApplyTactics(run.Situation, new[] { new HeroTactic(HeroRole.RangedDamage, 2, -1, "저놈부터!") }), Is.True);
            Ready(ranged, 0);
            run.Tick();
            var events = run.TakeEvents();
            Assert.That(Skills(events, HeroSkill.PiercingShot), Is.Empty);
            Assert.That(HitsBy(events, HeroRole.RangedDamage).Select(e => e.Target), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void RapidFireHalvesTheAttackCycleWhileItLasts()
        {
            var run = Arena(1, HeroSkills.UnlockLevel);
            var ranged = Hero(run, HeroRole.RangedDamage);
            var goblin = Goblin(run, 0);
            goblin.Hp = 10000;
            Put(run, HeroRole.RangedDamage, 22, 4);
            Put(goblin, 25, 4);
            Ready(ranged, 1);
            var shots = new List<int>();
            for (int i = 0; i < DungeonRun.RapidFireTicks + 3 * ranged.AttackTicks; i++)
            {
                run.Tick();
                var events = run.TakeEvents();
                if (i == 0) Assert.That(Skills(events, HeroSkill.RapidFire).Single().Target, Is.EqualTo(goblin.Id));
                else Assert.That(Skills(events, HeroSkill.RapidFire), Is.Empty);
                if (HitsBy(events, HeroRole.RangedDamage).Any()) shots.Add(run.Ticks);
            }
            var gaps = shots.Zip(shots.Skip(1), (a, b) => b - a).ToList();
            Assert.That(gaps.First(), Is.EqualTo(ranged.AttackTicks / 2));
            Assert.That(gaps.Last(), Is.EqualTo(ranged.AttackTicks));
            Assert.That(run.SkillCooldownLeft(HeroRole.RangedDamage, 1), Is.Positive);
        }

        [Test]
        public void MassHealMendsSeveralAlliesInOneAction()
        {
            var run = Arena(1);
            var healer = Hero(run, HeroRole.Healer);
            Put(run, HeroRole.Healer, 23, 3);
            Put(run, HeroRole.Tank, 24, 3);
            Put(run, HeroRole.MeleeDamage, 23, 4);
            Put(run, HeroRole.RangedDamage, 27, 7);
            Put(Goblin(run, 0), 27, 2);
            foreach (var role in new[] { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.RangedDamage })
                Hero(run, role).Hp -= 10;
            Ready(healer, 0);
            run.Tick();
            var events = run.TakeEvents();
            Assert.That(Skills(events, HeroSkill.MassHeal).Count, Is.EqualTo(1));
            var heals = events.Where(e => e.Kind == CombatEventKind.Heal && e.Source == (int)HeroRole.Healer).ToList();
            Assert.That(heals.Select(e => e.Target), Is.EquivalentTo(new[] { (int)HeroRole.Tank, (int)HeroRole.MeleeDamage }));
            int amount = (7 + run.Level) * DungeonRun.MassHealPercent / 100;
            Assert.That(heals.All(e => e.Amount == amount), Is.True);
            Assert.That(Hero(run, HeroRole.RangedDamage).Hp, Is.EqualTo(Hero(run, HeroRole.RangedDamage).MaxHp - 10), "out of the radius");
        }

        [Test]
        public void TheShieldWallHalvesTheBlowsOnAlliesBehindIt()
        {
            int[] Blows(int level)
            {
                var run = Arena(2, level);
                Put(run, HeroRole.Tank, 24, 4);
                Put(run, HeroRole.MeleeDamage, 25, 4);
                Put(run, HeroRole.Healer, 22, 7);
                Put(run, HeroRole.RangedDamage, 22, 2);
                // One goblin goes for the tank, the other for the melee hero next to it.
                Put(Goblin(run, 0), 23, 4);
                Put(Goblin(run, 1), 26, 4);
                foreach (var goblin in run.Monsters.Where(m => !m.IsBoss)) goblin.AttackReady = 0;
                Hero(run, HeroRole.Tank).SkillReady[1] = 0;
                run.Tick();
                var events = run.TakeEvents();
                Assert.That(Skills(events, HeroSkill.ShieldWall).Count, Is.EqualTo(level >= HeroSkills.UnlockLevel ? 1 : 0));
                var blows = events.Where(e => e.Kind == CombatEventKind.Attack && !e.FromHero).ToList();
                return new[]
                {
                    blows.Single(e => e.Target == (int)HeroRole.Tank).Amount,
                    blows.Single(e => e.Target == (int)HeroRole.MeleeDamage).Amount
                };
            }
            var bare = Blows(1);
            var walled = Blows(HeroSkills.UnlockLevel);
            Assert.That(bare, Is.EqualTo(new[] { 6 - 2, 6 }));
            Assert.That(walled, Is.EqualTo(bare.Select(d => d * (100 - DungeonRun.ShieldWallReductionPercent) / 100).ToArray()));
        }

        [Test]
        public void ReviveRaisesAFallenHeroOncePerStage()
        {
            var run = Arena(1, HeroSkills.UnlockLevel);
            var healer = Hero(run, HeroRole.Healer);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(run, HeroRole.MeleeDamage, 22, 2);
            Put(run, HeroRole.Healer, 27, 7);
            Put(run, HeroRole.Tank, 25, 3);
            Put(run, HeroRole.RangedDamage, 27, 6);
            Put(Goblin(run, 0), 27, 2);
            Goblin(run, 0).Hp = 10000;
            melee.Hp = 0;
            Ready(healer, 1);
            var events = new List<CombatEvent>();
            for (int i = 0; i < 100 && melee.Hp == 0; i++)
            {
                run.Tick();
                events.AddRange(run.TakeEvents());
            }
            Assert.That(melee.Hp, Is.EqualTo(melee.MaxHp * DungeonRun.ReviveHpPercent / 100));
            Assert.That(melee.Critical, Is.EqualTo(melee.Hp * 10 < melee.MaxHp * 3));
            Assert.That(run.ReviveUsed, Is.True);
            var skill = Skills(events, HeroSkill.Revive).Single();
            Assert.That(skill.ToHero, Is.True);
            Assert.That(skill.Target, Is.EqualTo((int)HeroRole.MeleeDamage));
            Assert.That(run.Log.Any(l => l.StartsWith($"{healer.Name} • 부활!")), Is.True);
            Assert.That(run.HeroTile(HeroRole.Healer), Is.Not.EqualTo(new TilePoint(27, 7)), "the healer walked over");

            // The revived hero fights again.
            melee.ActReady = 0;
            Put(Goblin(run, 0), 23, 2);
            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.MeleeDamage), Is.Not.Empty);

            // A second fall in the same stage stays down.
            melee.Hp = 0;
            healer.ActReady = healer.MoveReady = 0;
            for (int i = 0; i < 100; i++) run.Tick();
            Assert.That(melee.Hp, Is.Zero);
            Assert.That(Skills(run.TakeEvents(), HeroSkill.Revive), Is.Empty);

            // Clearing the stage opens revive again.
            var boss = run.Boss;
            boss.Mood = MonsterMood.Chasing;
            boss.Hp = 1;
            Put(boss, 24, 3);
            Hero(run, HeroRole.Tank).ActReady = 0;
            run.Tick();
            Assert.That(run.Stage, Is.EqualTo(2));
            Assert.That(run.ReviveUsed, Is.False);
            Assert.That(melee.Hp, Is.EqualTo(melee.MaxHp));
        }

        [Test]
        public void AHeroWhoFellInTheLastFightIsRaisedOnTheMarch()
        {
            var run = Arena(1, HeroSkills.UnlockLevel);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(run, HeroRole.MeleeDamage, 24, 4);
            Put(run, HeroRole.Healer, 25, 5);
            melee.Hp = 0;
            Goblin(run, 0).Mood = MonsterMood.Defeated;
            Ready(Hero(run, HeroRole.Healer), 1);
            int steps = run.StepsTaken;
            run.Tick();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Travel));
            Assert.That(melee.Hp, Is.Positive);
            Assert.That(run.ReviveUsed, Is.True);
            Assert.That(run.StepsTaken, Is.EqualTo(steps), "the party waited for the revive");
            foreach (var hero in run.Party) hero.MoveReady = hero.ActReady = 0;
            DungeonRunTests.Drive(run, () => run.StepsTaken > steps + 3);
            Assert.That(run.StepsTaken, Is.GreaterThan(steps + 3), "and then marches on with the raised hero");
        }
    }
}
