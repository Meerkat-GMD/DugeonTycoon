using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    public enum RunPhase { Planning, Travel, Combat, Victory, Defeat }
    public enum MonsterKind { None, Goblin, Ogre }
    public enum HeroAction { Attack, Guard, Heal }
    public enum PlacementCheck { Allowed, Locked, InvalidKind, NotFloor, NearEntrance, OverBudget, NothingToRemove }

    public sealed class MonsterPlacement
    {
        internal MonsterPlacement(TilePoint tile, MonsterKind kind)
        {
            Tile = tile;
            Kind = kind;
        }

        public TilePoint Tile { get; }
        public MonsterKind Kind { get; }
    }

    public sealed class HeroDecision
    {
        public HeroDecision(HeroRole role, HeroAction action, HeroRole target, string line = null)
        {
            Role = role;
            Action = action;
            Target = target;
            Line = line;
        }

        public HeroRole Role { get; }
        public HeroAction Action { get; }
        public HeroRole Target { get; }
        public string Line { get; }
    }

    public sealed class HeroState
    {
        private readonly int[] abilities;

        internal HeroState(HeroRole role, string name, int maxHp, int attack, int[] abilities, params PersonalityTag[] tags)
        {
            if (abilities == null || abilities.Length != 4) throw new ArgumentException("Four ability modifiers are required.", nameof(abilities));
            if (tags == null || tags.Length == 0) throw new ArgumentException("At least one personality tag is required.", nameof(tags));
            Role = role;
            Name = name;
            Hp = MaxHp = maxHp;
            BaseAttack = attack;
            this.abilities = (int[])abilities.Clone();
            Tags = Array.AsReadOnly((PersonalityTag[])tags.Clone());
        }

        public HeroRole Role { get; }
        public string Name { get; }
        public IReadOnlyList<PersonalityTag> Tags { get; }
        public int Modifier(Ability ability) => abilities[(int)ability];
        public int Hp { get; internal set; }
        public int MaxHp { get; internal set; }
        public string LastLine { get; internal set; } = "";
        public HeroAction LastAction { get; internal set; }
        internal int BaseAttack { get; }
    }

    public sealed class EnemyState
    {
        internal EnemyState(string name, int maxHp, int attack, int armor, int reward)
        {
            Name = name;
            Hp = MaxHp = maxHp;
            Attack = attack;
            Armor = armor;
            Experience = reward;
        }

        public string Name { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; }
        public int Attack { get; }
        public int Armor { get; }
        internal int Experience { get; }
    }
}
