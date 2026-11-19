using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    /// <summary>Numbers for one ordinary monster kind. The same kind has the same numbers on every stage.</summary>
    [Serializable]
    public sealed class MonsterBalance
    {
        public int cost;
        public int experience;
        /// <summary>Ticks per tile moved.</summary>
        public int moveTicks;
        /// <summary>Ticks between plain blows.</summary>
        public int attackTicks;
        /// <summary>Tiles from which its plain blow lands; 1 is melee.</summary>
        public int range = 1;
        public int hp;
        public int attack;
        public int armor;
    }

    /// <summary>Numbers for one stage boss.</summary>
    [Serializable]
    public sealed class BossBalance
    {
        public int hp;
        public int attack;
        public int armor;
        public int experience;
        public int moveTicks;
        public int attackTicks;
    }

    /// <summary>
    /// The balance numbers that playtests tune, in one serialisable table. The game reads them from
    /// <c>Assets/Game/Unity/Resources/Balance/balance.json</c> (Unity) and the Core tests read the same file, so a change
    /// there reaches both without touching code. The values written here are the fallback when no file is loaded and
    /// match that file. Field names are the JSON keys. Per-stage lists hold stages 1, 2 and 3 in order.
    /// </summary>
    [Serializable]
    public sealed class BalanceTable
    {
        /// <summary>
        /// How armour turns into damage taken: damage = attack × armorScale ÷ (armorScale + armour). Armour equal to the
        /// scale halves a blow; it never takes a blow below 1.
        /// </summary>
        public int armorScale = 20;
        public int attackPerRoomAlly = 1;
        public int experiencePerLevel = 12;
        public int levelAttackGain = 2;
        public int levelHpGain = 5;
        public int reducedGrowthLevel = 8;
        public int lateLevelAttackGain = 1;
        public int lateLevelHpGain = 3;
        public int[] budget = { 6, 9, 12 };
        public int[] placementLimit = { 6, 7, 8 };
        public int[] nearbyPlacementLimit = { 3, 3, 4 };
        public int[] trialDifficulty = { 13, 14, 15 };

        public MonsterBalance skeleton = new MonsterBalance
        {
            cost = 1, experience = 4, moveTicks = 3, attackTicks = 15,
            hp = 100, attack = 22, armor = 3
        };
        public MonsterBalance ghost = new MonsterBalance
        {
            cost = 2, experience = 8, moveTicks = 2, attackTicks = 15, range = 5,
            hp = 90, attack = 22, armor = 1
        };
        public MonsterBalance zombie = new MonsterBalance
        {
            cost = 3, experience = 8, moveTicks = 5, attackTicks = 18,
            hp = 240, attack = 28, armor = 6
        };
        public MonsterBalance archer = new MonsterBalance
        {
            cost = 2, experience = 8, moveTicks = 3, attackTicks = 18, range = 4,
            hp = 100, attack = 24, armor = 2
        };

        public BossBalance graveKnight = new BossBalance { hp = 520, attack = 22, armor = 4, experience = 5, moveTicks = 4, attackTicks = 15 };
        public BossBalance cryptPriest = new BossBalance { hp = 1050, attack = 28, armor = 6, experience = 6, moveTicks = 4, attackTicks = 15 };
        public BossBalance necromancer = new BossBalance { hp = 3800, attack = 48, armor = 8, experience = 0, moveTicks = 4, attackTicks = 15 };

        public int[] chestRarePercent = { 15, 30, 50 };
        public int springRelief = 40;
        public int faithRecoveryTicks = 600;
        public int faithRecoveryCap = 5;

        private static BalanceTable current = new BalanceTable();

        /// <summary>
        /// The table new expeditions read. Set it before creating a run; a run reads the numbers as it goes, so swap
        /// tables only between expeditions. Setting null restores the built-in values.
        /// </summary>
        public static BalanceTable Current
        {
            get => current;
            set => current = value ?? new BalanceTable();
        }

        public MonsterBalance Of(MonsterKind kind)
        {
            switch (kind)
            {
                case MonsterKind.Skeleton: return skeleton;
                case MonsterKind.Ghost: return ghost;
                case MonsterKind.Zombie: return zombie;
                case MonsterKind.Archer: return archer;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public BossBalance BossOf(int stage)
        {
            switch (stage)
            {
                case 1: return graveKnight;
                case 2: return cryptPriest;
                default: return necromancer;
            }
        }

        internal int AttackGrowth(int level) => Growth(level, levelAttackGain, lateLevelAttackGain);

        internal int HealthGrowth(int level) => Growth(level, levelHpGain, lateLevelHpGain);

        private int Growth(int level, int earlyGain, int lateGain)
        {
            int earlyLevels = Math.Min(level, reducedGrowthLevel - 1) - 1;
            int lateLevels = level - 1 - earlyLevels;
            return earlyLevels * earlyGain + lateLevels * lateGain;
        }

        /// <summary>A blow of <paramref name="attack"/> against <paramref name="armor"/>, rounded; at least 1 for any attack above 0.</summary>
        public int Mitigate(int attack, int armor)
        {
            if (attack <= 0) return 0;
            int divisor = armorScale + Math.Max(0, armor);
            return Math.Max(1, (attack * armorScale + divisor / 2) / divisor);
        }

        /// <summary>The value for a stage from a per-stage list.</summary>
        public static int ForStage(int[] values, int stage) => values[Math.Max(1, Math.Min(values.Length, stage)) - 1];

        /// <summary>What is wrong with the table, in words; empty when it can be played.</summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            void Positive(string name, int value)
            {
                if (value <= 0) problems.Add($"{name} must be above 0 (is {value}).");
            }
            void Stages(string name, int[] values, int min)
            {
                if (values == null || values.Length != DungeonLayouts.StageCount)
                    problems.Add($"{name} needs {DungeonLayouts.StageCount} values, one per stage.");
                else
                    foreach (int value in values)
                        if (value < min) problems.Add($"{name} has {value}; values must be at least {min}.");
            }
            Positive(nameof(armorScale), armorScale);
            Positive(nameof(attackPerRoomAlly), attackPerRoomAlly);
            Positive(nameof(experiencePerLevel), experiencePerLevel);
            if (reducedGrowthLevel < 2) problems.Add("성장 감소 시작 레벨은 2 이상이어야 합니다.");
            if (levelAttackGain < 0 || levelHpGain < 0 || lateLevelAttackGain < 0 || lateLevelHpGain < 0)
                problems.Add("레벨당 공격력과 최대 체력 증가량은 0 이상이어야 합니다.");
            Positive(nameof(faithRecoveryTicks), faithRecoveryTicks);
            Positive(nameof(faithRecoveryCap), faithRecoveryCap);
            Stages(nameof(budget), budget, 0);
            Stages(nameof(placementLimit), placementLimit, 1);
            Stages(nameof(nearbyPlacementLimit), nearbyPlacementLimit, 1);
            Stages(nameof(trialDifficulty), trialDifficulty, 1);
            Stages(nameof(chestRarePercent), chestRarePercent, 0);
            foreach (var (name, monster) in new[] { (nameof(skeleton), skeleton), (nameof(ghost), ghost), (nameof(zombie), zombie), (nameof(archer), archer) })
            {
                if (monster == null)
                {
                    problems.Add($"{name} is missing.");
                    continue;
                }
                Positive(name + ".cost", monster.cost);
                Positive(name + ".moveTicks", monster.moveTicks);
                Positive(name + ".attackTicks", monster.attackTicks);
                Positive(name + ".range", monster.range);
                Positive(name + ".hp", monster.hp);
                if (monster.attack < 0) problems.Add($"{name}.attack must not be negative.");
                if (monster.armor < 0) problems.Add($"{name}.armor must not be negative.");
            }
            foreach (var (name, boss) in new[] { (nameof(graveKnight), graveKnight), (nameof(cryptPriest), cryptPriest), (nameof(necromancer), necromancer) })
            {
                if (boss == null)
                {
                    problems.Add($"{name} is missing.");
                    continue;
                }
                Positive(name + ".hp", boss.hp);
                Positive(name + ".moveTicks", boss.moveTicks);
                Positive(name + ".attackTicks", boss.attackTicks);
            }
            return problems;
        }
    }
}
