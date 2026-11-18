using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class LootTests
    {
        private static readonly HeroRole[] March =
            { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.Healer, HeroRole.RangedDamage };

        private static Equipment Item(string name) => EquipmentCatalog.Find(name) ?? throw new ArgumentException(name);

        /// <summary>
        /// One goblin per drop, all just past the entrance clearance so they are met first whatever the route.
        /// Each one drops the next item. The fight runs to its end without settling the loot;
        /// <paramref name="midFight"/> runs once the fight has started.
        /// </summary>
        private static DungeonRun FightOneGoblin(params string[] drops) => FightOneGoblin(null, drops);

        private static DungeonRun FightOneGoblin(Action<DungeonRun> midFight, params string[] drops)
        {
            var run = new DungeonRun();
            var tile = DungeonRunTests.PlaceableTiles(run).OrderBy(t => run.Map.WalkDistance(t)).First();
            for (int i = 0; i < Math.Max(1, drops.Length); i++) Assert.That(run.Place(tile, MonsterKind.Goblin), Is.True);
            var queue = new Queue<string>(drops);
            run.DropOverride = _ => queue.Count > 0 ? Item(queue.Dequeue()) : null;
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Combat));
            midFight?.Invoke(run);
            for (int i = 0; i < 5000 && run.Phase == RunPhase.Combat; i++) run.Tick();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Travel), "the goblin falls and the party walks on");
            return run;
        }

        private static List<LootWish> Wishes(DungeonRun run, params HeroRole[] wanting) =>
            run.PendingLoot.Heroes.Select(role => new LootWish(role, 0, wanting.Contains(role), $"{run.Party[(int)role].Name}의 말")).ToList();

        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];

        [Test]
        public void AWeaponGoesStraightToItsOwnerWhenItIsAnUpgrade()
        {
            var run = FightOneGoblin("강철 대검");
            var melee = Hero(run, HeroRole.MeleeDamage);
            Assert.That(run.PendingLoot, Is.Null, "weapons are never debated");
            Assert.That(melee.Weapon.Name, Is.EqualTo("강철 대검"));
            Assert.That(melee.Attack, Is.EqualTo(8 + 3));
            Assert.That(run.LastSpoils.Items.Single().Wearer, Is.EqualTo(HeroRole.MeleeDamage));
            Assert.That(run.LastSpoils.Experience, Is.GreaterThan(0));
            Assert.That(run.Tick(), Is.True, "the dungeon keeps running");
        }

        [Test]
        public void AFasterWeaponShortensTheAttackCycleAndAWorseOneIsThrownAway()
        {
            var run = FightOneGoblin("바람의 장궁");
            var ranged = Hero(run, HeroRole.RangedDamage);
            Assert.That(ranged.Weapon.Name, Is.EqualTo("바람의 장궁"));
            Assert.That(ranged.AttackTicks, Is.EqualTo(DungeonRun.HeroAttackTicks - 2));

            // A common bow after a rare one: thrown away with the reason on the banner.
            var again = FightOneGoblin(r => Hero(r, HeroRole.RangedDamage).Equip(Item("바람의 장궁")), "사냥꾼의 활");
            var result = again.LastSpoils.Items.Single();
            Assert.That(result.Wearer, Is.Null);
            Assert.That(result.Reason, Is.EqualTo("지금 무기가 더 낫다"));
            Assert.That(Hero(again, HeroRole.RangedDamage).Weapon.Name, Is.EqualTo("바람의 장궁"));
        }

        [Test]
        public void AFallenHeroGetsNoWeaponAndHasNoSayInTheArmour()
        {
            var run = FightOneGoblin(r => Hero(r, HeroRole.Healer).Hp = 0, "참나무 지팡이", "사슬 갑옷");
            Assert.That(Hero(run, HeroRole.Healer).Weapon, Is.Null);
            Assert.That(run.LastSpoils, Is.Null, "the banner waits until the armour is shared out");
            var loot = run.PendingLoot;
            Assert.That(loot, Is.Not.Null);
            Assert.That(loot.Heroes, Is.EqualTo(new[] { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.RangedDamage }));

            // Even an AI answer that has the fallen healer asking cannot hand it the armour.
            var wishes = new List<LootWish> { new LootWish(HeroRole.Healer, 0, true, "저요!") };
            Assert.That(run.ResolveLoot(loot.Token, wishes), Is.True);
            Assert.That(Hero(run, HeroRole.Healer).BodyArmor, Is.Null);
            var staff = run.LastSpoils.Items.First(i => i.Item.Name == "참나무 지팡이");
            Assert.That(staff.Wearer, Is.Null);
            Assert.That(staff.Reason, Does.Contain("쓰러져"));
        }

        [Test]
        public void ArmourFreezesTheDungeonUntilTheHeroesShareItOut()
        {
            var run = FightOneGoblin("사슬 갑옷");
            var loot = run.PendingLoot;
            Assert.That(loot, Is.Not.Null);
            Assert.That(loot.Heroes, Is.EqualTo(March));
            Assert.That(loot.LocalWishes.Count, Is.EqualTo(4));
            for (int i = 0; i < 50; i++) Assert.That(run.Tick(), Is.False, "nothing moves during the debate");
            Assert.That(run.BeginAdversity(), Is.Zero, "no adversity on top of the loot debate");
            Assert.That(run.ResolveLoot(loot.Token + 1), Is.False, "a stale answer is refused");
            Assert.That(run.ResolveLoot(loot.Token), Is.True);
            Assert.That(run.PendingLoot, Is.Null);
            Assert.That(run.ResolveLoot(loot.Token), Is.False, "the same answer cannot apply twice");
            Assert.That(run.Tick(), Is.True);
        }

        [Test]
        public void TwoWantersRollAPlainD20AndATieRollsAgainBetweenTheTiedOnly()
        {
            var run = FightOneGoblin("사슬 갑옷");
            var faces = new Queue<int>(new[] { 12, 12, 7, 15 });
            run.LootRollOverride = () => faces.Dequeue();
            var melee = Hero(run, HeroRole.MeleeDamage);
            int maxBefore = melee.MaxHp, hpBefore = melee.Hp;
            Assert.That(run.ResolveLoot(run.PendingLoot.Token, Wishes(run, HeroRole.Tank, HeroRole.MeleeDamage)), Is.True);

            var scene = run.LastScene;
            Assert.That(scene[0].Kind, Is.EqualTo(BeatKind.Narration));
            Assert.That(scene[0].Item.Name, Is.EqualTo("사슬 갑옷"));
            var lines = scene.Skip(1).Take(4).ToList();
            Assert.That(lines.Select(b => b.Speaker), Is.EqualTo(March.Cast<HeroRole?>()));
            Assert.That(lines.Select(b => b.Mark), Is.EqualTo(new[] { "원함", "원함", "패스", "패스" }));

            var rolls = scene.Where(b => b.Kind == BeatKind.Roll).ToList();
            Assert.That(rolls.Count, Is.EqualTo(2));
            Assert.That(rolls[0].Winner, Is.Null, "a tied round has no winner");
            Assert.That(rolls[0].Text, Is.EqualTo(DialogueLines.LootRollTitle));
            Assert.That(rolls[1].Text, Is.EqualTo(DialogueLines.LootRerollTitle));
            Assert.That(rolls[1].Rolls.Select(r => r.Face), Is.EqualTo(new[] { 7, 15 }));
            Assert.That(rolls.SelectMany(r => r.Rolls).All(r => r.Modifier == 0), Is.True, "no modifier on loot dice");
            Assert.That(rolls[1].Winner, Is.EqualTo(HeroRole.MeleeDamage));
            Assert.That(scene.Last().Speaker, Is.EqualTo(HeroRole.MeleeDamage), "the winner closes the scene");

            Assert.That(melee.BodyArmor.Name, Is.EqualTo("사슬 갑옷"));
            Assert.That(melee.Armor, Is.EqualTo(2));
            Assert.That(melee.MaxHp, Is.EqualTo(maxBefore + 8));
            Assert.That(melee.Hp, Is.EqualTo(hpBefore + 8), "a higher maximum heals by the difference");
            Assert.That(run.LastSpoils.Items.Single().Wearer, Is.EqualTo(HeroRole.MeleeDamage));
        }

        [Test]
        public void AHeroWhoseArmourWouldGetWorseIsLeftOutAndALoneWanterTakesItWithoutDice()
        {
            var run = FightOneGoblin(r => Hero(r, HeroRole.Tank).Equip(Item("사슬 갑옷")), "가죽 갑옷");
            Assert.That(run.ResolveLoot(run.PendingLoot.Token, Wishes(run, HeroRole.Tank, HeroRole.RangedDamage)), Is.True);

            var scene = run.LastScene;
            var worse = scene.Single(b => b.Mark == DialogueLines.Worse);
            Assert.That(worse.MarkRole, Is.EqualTo(HeroRole.Tank));
            Assert.That(worse.Kind, Is.EqualTo(BeatKind.Narration));
            Assert.That(scene.Any(b => b.Kind == BeatKind.Roll), Is.False);
            Assert.That(Hero(run, HeroRole.Tank).BodyArmor.Name, Is.EqualTo("사슬 갑옷"), "the tank keeps the better armour");
            Assert.That(Hero(run, HeroRole.RangedDamage).BodyArmor.Name, Is.EqualTo("가죽 갑옷"));
        }

        [Test]
        public void ArmourNobodyWantsIsThrownAwayAndBadAnswersFallBackToTheLocalWish()
        {
            var run = FightOneGoblin("누빈 조끼", "축복받은 로브");
            var loot = run.PendingLoot;
            Assert.That(loot.Items.Select(i => i.Name), Is.EqualTo(new[] { "누빈 조끼", "축복받은 로브" }));
            // Nobody wants the vest; for the robe only the tank answers, and with an empty line.
            var wishes = loot.Heroes.Select(role => new LootWish(role, 0, false, "필요 없어")).ToList();
            wishes.Add(new LootWish(HeroRole.Tank, 1, true, " "));
            int calls = 0;
            run.LootRollOverride = () => ++calls == 1 ? 20 : 1;
            Assert.That(run.ResolveLoot(loot.Token, wishes), Is.True);

            var vest = run.LastSpoils.Items[0];
            Assert.That(vest.Wearer, Is.Null);
            Assert.That(vest.Reason, Is.EqualTo("원하는 용사가 없다"));
            // Without valid answers for the robe every hero falls back to "I want it if it beats what I wear".
            var robeLines = run.LastScene.SkipWhile(b => b.Item == null || b.Item.Name != "축복받은 로브").Skip(1).Take(4);
            Assert.That(robeLines.Select(b => b.Text), Is.EqualTo(March.Select(r => DialogueLines.LootWish(r, true))));
            Assert.That(run.LastSpoils.Items[1].Wearer, Is.EqualTo(HeroRole.Tank), "the first die shows 20");
        }

        [Test]
        public void TheSameSeedDropsTheSameItemsFromTheTables()
        {
            List<string> Play()
            {
                var run = new DungeonRun(seed: 11);
                foreach (var tile in DungeonRunTests.PlaceableTiles(run).Where((_, i) => i % 9 == 0).Take(5))
                    run.Place(tile, MonsterKind.Ogre);
                run.Start();
                DungeonRunTests.Drive(run, () => !DungeonRunTests.InDungeon(run));
                return run.Log.Where(line => line.StartsWith("전리품") || line.StartsWith("장착") || line.StartsWith("버림")).ToList();
            }
            var first = Play();
            Assert.That(first, Is.Not.Empty, "five ogres drop something");
            Assert.That(Play(), Is.EqualTo(first));
        }

        [Test]
        public void PowerRanksRareAboveCommonInEverySlot()
        {
            foreach (var role in March)
            {
                int common = EquipmentCatalog.Common.Single(i => i.Role == role).Power;
                int rare = EquipmentCatalog.Rare.Single(i => i.Role == role).Power;
                Assert.That(rare, Is.GreaterThan(common), role.ToString());
            }
            int bestCommonArmour = EquipmentCatalog.Common.Where(i => i.Slot == EquipmentSlot.Armor).Max(i => i.Power);
            Assert.That(EquipmentCatalog.Rare.Where(i => i.Slot == EquipmentSlot.Armor).All(i => i.Power > bestCommonArmour), Is.True);
        }
    }
}
