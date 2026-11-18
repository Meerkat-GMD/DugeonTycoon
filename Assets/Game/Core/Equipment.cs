using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    public enum EquipmentSlot { Weapon, Armor }
    public enum EquipmentGrade { Common, Rare }

    /// <summary>
    /// A weapon or a piece of armour. It only adjusts numbers: a weapon the attack and the attack cycle, armour
    /// the defence and the maximum health. <see cref="Power"/> is what heroes compare when deciding to swap.
    /// </summary>
    public sealed class Equipment
    {
        internal Equipment(string name, EquipmentSlot slot, EquipmentGrade grade, HeroRole? role,
            int attack = 0, int attackTicks = 0, int armor = 0, int maxHp = 0)
        {
            if (slot == EquipmentSlot.Weapon && role == null) throw new ArgumentException("A weapon belongs to one role.", nameof(role));
            Name = name;
            Slot = slot;
            Grade = grade;
            Role = slot == EquipmentSlot.Weapon ? role : null;
            AttackBonus = attack;
            AttackTicksBonus = attackTicks;
            ArmorBonus = armor;
            MaxHpBonus = maxHp;
        }

        public string Name { get; }
        public EquipmentSlot Slot { get; }
        public EquipmentGrade Grade { get; }
        /// <summary>The only role that can wield this weapon; null for armour, which anyone can wear.</summary>
        public HeroRole? Role { get; }
        public int AttackBonus { get; }
        /// <summary>Change to the ticks between attacks; negative is faster.</summary>
        public int AttackTicksBonus { get; }
        public int ArmorBonus { get; }
        public int MaxHpBonus { get; }

        /// <summary>Comparison score for "is this better than what I have". Balance value.</summary>
        public int Power => Slot == EquipmentSlot.Weapon
            ? 2 * AttackBonus - AttackTicksBonus
            : 3 * ArmorBonus + MaxHpBonus / 2;

        public bool Fits(HeroRole role) => Slot == EquipmentSlot.Armor || Role == role;

        /// <summary>The bonuses in words, e.g. "방어 +2 · 최대 체력 +8".</summary>
        public string Stats
        {
            get
            {
                var parts = new List<string>();
                if (AttackBonus != 0) parts.Add($"공격력 {Signed(AttackBonus)}");
                if (AttackTicksBonus != 0) parts.Add($"공격 주기 {Signed(AttackTicksBonus * DungeonRun.TickSeconds)}초");
                if (ArmorBonus != 0) parts.Add($"방어 {Signed(ArmorBonus)}");
                if (MaxHpBonus != 0) parts.Add($"최대 체력 {Signed(MaxHpBonus)}");
                return string.Join(" · ", parts);
            }
        }

        public string GradeName => Grade == EquipmentGrade.Rare ? "희귀" : "일반";

        public static int PowerOf(Equipment item) => item?.Power ?? 0;

        private static string Signed(int value) => (value >= 0 ? "+" : "") + value;
        private static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("0.0");

        public override string ToString() => $"{Name}({GradeName} · {Stats})";
    }

    /// <summary>Every item that can drop, and the drop tables. All numbers are balance values.</summary>
    public static class EquipmentCatalog
    {
        public static readonly IReadOnlyList<Equipment> Common = new[]
        {
            new Equipment("철 검과 방패", EquipmentSlot.Weapon, EquipmentGrade.Common, HeroRole.Tank, attack: 2),
            new Equipment("참나무 지팡이", EquipmentSlot.Weapon, EquipmentGrade.Common, HeroRole.Healer, attack: 1, attackTicks: -1),
            new Equipment("강철 대검", EquipmentSlot.Weapon, EquipmentGrade.Common, HeroRole.MeleeDamage, attack: 3),
            new Equipment("사냥꾼의 활", EquipmentSlot.Weapon, EquipmentGrade.Common, HeroRole.RangedDamage, attack: 2, attackTicks: -1),
            new Equipment("가죽 갑옷", EquipmentSlot.Armor, EquipmentGrade.Common, null, armor: 1, maxHp: 4),
            new Equipment("누빈 조끼", EquipmentSlot.Armor, EquipmentGrade.Common, null, maxHp: 8),
        };

        public static readonly IReadOnlyList<Equipment> Rare = new[]
        {
            new Equipment("수호자의 검과 방패", EquipmentSlot.Weapon, EquipmentGrade.Rare, HeroRole.Tank, attack: 3, attackTicks: -2),
            new Equipment("성자의 지팡이", EquipmentSlot.Weapon, EquipmentGrade.Rare, HeroRole.Healer, attack: 2, attackTicks: -3),
            new Equipment("용살 대검", EquipmentSlot.Weapon, EquipmentGrade.Rare, HeroRole.MeleeDamage, attack: 5),
            new Equipment("바람의 장궁", EquipmentSlot.Weapon, EquipmentGrade.Rare, HeroRole.RangedDamage, attack: 4, attackTicks: -2),
            new Equipment("사슬 갑옷", EquipmentSlot.Armor, EquipmentGrade.Rare, null, armor: 2, maxHp: 8),
            new Equipment("축복받은 로브", EquipmentSlot.Armor, EquipmentGrade.Rare, null, armor: 1, maxHp: 14),
        };

        /// <summary>Percent chance that a skeleton or ghost drops something; always common. Balance value.</summary>
        public const int SkeletonDropPercent = 20;
        /// <summary>Percent chance that a zombie drops something. Balance value.</summary>
        public const int ZombieDropPercent = 45;
        /// <summary>Percent of a zombie's drops that are rare. Balance value.</summary>
        public const int ZombieRarePercent = 25;

        public static Equipment Find(string name) => Common.Concat(Rare).FirstOrDefault(item => item.Name == name);

        /// <summary>
        /// What a fallen monster leaves, decided by the run's loot dice: skeletons and ghosts sometimes a common item, zombies more
        /// often and sometimes a rare one, a boss always a rare one. Null for nothing.
        /// </summary>
        internal static Equipment Roll(MonsterState monster, Random dice)
        {
            EquipmentGrade grade;
            if (monster.IsBoss) grade = EquipmentGrade.Rare;
            else if (monster.Kind == MonsterKind.Zombie)
            {
                if (dice.Next(100) >= ZombieDropPercent) return null;
                grade = dice.Next(100) < ZombieRarePercent ? EquipmentGrade.Rare : EquipmentGrade.Common;
            }
            else
            {
                if (dice.Next(100) >= SkeletonDropPercent) return null;
                grade = EquipmentGrade.Common;
            }
            var pool = grade == EquipmentGrade.Rare ? Rare : Common;
            return pool[dice.Next(pool.Count)];
        }
    }
}
