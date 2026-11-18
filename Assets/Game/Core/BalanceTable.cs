using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    /// <summary>Numbers for one ordinary monster kind. Per-stage lists hold stages 1, 2 and 3 in order.</summary>
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
        public int[] hp;
        public int[] attack;
        public int[] armor;
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
        public int experiencePerLevel = 10;
        public int levelAttackGain = 3;
        public int levelHpGain = 5;
        public int[] budget = { 6, 8, 10 };
        public int[] trialDifficulty = { 13, 14, 15 };

        public MonsterBalance skeleton = new MonsterBalance
        {
            cost = 1, experience = 4, moveTicks = 3, attackTicks = 15,
            hp = new[] { 85, 120, 150 }, attack = new[] { 17, 21, 25 }, armor = new[] { 2, 4, 5 }
        };
        public MonsterBalance ghost = new MonsterBalance
        {
            cost = 2, experience = 5, moveTicks = 2, attackTicks = 15,
            hp = new[] { 80, 100, 120 }, attack = new[] { 18, 22, 26 }, armor = new[] { 0, 2, 3 }
        };
        public MonsterBalance zombie = new MonsterBalance
        {
            cost = 3, experience = 8, moveTicks = 5, attackTicks = 18,
            hp = new[] { 160, 200, 250 }, attack = new[] { 22, 25, 30 }, armor = new[] { 4, 6, 8 }
        };
        public MonsterBalance archer = new MonsterBalance
        {
            cost = 2, experience = 5, moveTicks = 3, attackTicks = 18, range = 4,
            hp = new[] { 70, 80, 90 }, attack = new[] { 18, 21, 24 }, armor = new[] { 0, 1, 2 }
        };

        public BossBalance graveKnight = new BossBalance { hp = 450, attack = 22, armor = 4, experience = 2, moveTicks = 4, attackTicks = 15 };
        public BossBalance cryptPriest = new BossBalance { hp = 900, attack = 28, armor = 6, experience = 3, moveTicks = 4, attackTicks = 15 };
        public BossBalance necromancer = new BossBalance { hp = 3300, attack = 42, armor = 8, experience = 0, moveTicks = 4, attackTicks = 15 };

        public int[] chestRarePercent = { 15, 30, 50 };
        public int springRelief = 40;

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
            Positive(nameof(experiencePerLevel), experiencePerLevel);
            Stages(nameof(budget), budget, 0);
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
                Stages(name + ".hp", monster.hp, 1);
                Stages(name + ".attack", monster.attack, 0);
                Stages(name + ".armor", monster.armor, 0);
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
