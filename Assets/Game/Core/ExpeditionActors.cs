using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    public enum RunPhase { Planning, Travel, Combat, Victory, Defeat }
    public enum MonsterKind { None, Goblin, Ogre }
    public enum PlacementCheck { Allowed, Locked, InvalidKind, NotFloor, NearEntrance, OverBudget, NothingToRemove }
    public enum MonsterMood { Wandering, Chasing, Returning, Defeated }
    public enum CombatEventKind { Attack, Heal, Taunt }

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

    /// <summary>
    /// A combat plan for one hero until the situation changes: which monster to focus and which ally to
    /// look after. The rules decide the actual moves, attacks, heals and taunts each tick.
    /// </summary>
    public sealed class HeroTactic
    {
        public HeroTactic(HeroRole role, int focus, int protect, string line)
        {
            Role = role;
            Focus = focus;
            Protect = protect;
            Line = line;
        }

        public HeroRole Role { get; }
        /// <summary>Monster id to attack first, or -1 to let the rules pick.</summary>
        public int Focus { get; }
        /// <summary>Hero role (as int) to keep alive first, or -1.</summary>
        public int Protect { get; }
        public string Line { get; }
    }

    /// <summary>Something the board can animate. Ids are hero roles or monster ids.</summary>
    public sealed class CombatEvent
    {
        internal CombatEvent(CombatEventKind kind, bool fromHero, int source, bool toHero, int target, int amount)
        {
            Kind = kind;
            FromHero = fromHero;
            Source = source;
            ToHero = toHero;
            Target = target;
            Amount = amount;
        }

        public CombatEventKind Kind { get; }
        public bool FromHero { get; }
        public int Source { get; }
        public bool ToHero { get; }
        public int Target { get; }
        public int Amount { get; }
    }

    public sealed class HeroState
    {
        private readonly int[] abilities;

        internal HeroState(HeroRole role, string name, int maxHp, int attack, int armor, int range,
            int[] abilities, params PersonalityTag[] tags)
        {
            if (abilities == null || abilities.Length != 4) throw new ArgumentException("Four ability modifiers are required.", nameof(abilities));
            if (tags == null || tags.Length == 0) throw new ArgumentException("At least one personality tag is required.", nameof(tags));
            Role = role;
            Name = name;
            Hp = MaxHp = maxHp;
            BaseAttack = attack;
            Armor = armor;
            Range = range;
            this.abilities = (int[])abilities.Clone();
            Tags = Array.AsReadOnly((PersonalityTag[])tags.Clone());
        }

        public HeroRole Role { get; }
        public string Name { get; }
        public IReadOnlyList<PersonalityTag> Tags { get; }
        public int Modifier(Ability ability) => abilities[(int)ability];
        public int Hp { get; internal set; }
        public int MaxHp { get; internal set; }
        public int Armor { get; }
        /// <summary>Attack reach in tiles; 1 means an adjacent tile.</summary>
        public int Range { get; }
        public string LastLine { get; internal set; } = "";
        internal int BaseAttack { get; }
        internal int MoveReady { get; set; }
        internal int ActReady { get; set; }
        internal int TauntReady { get; set; }
        internal bool Critical { get; set; }
    }

    public sealed class MonsterState
    {
        internal MonsterState(int id, MonsterKind kind, bool boss, string name, int maxHp, int attack, int armor,
            int reward, int moveTicks, int attackTicks, TilePoint home)
        {
            Id = id;
            Kind = kind;
            IsBoss = boss;
            Name = name;
            Hp = MaxHp = maxHp;
            Attack = attack;
            Armor = armor;
            Experience = reward;
            MoveTicks = moveTicks;
            AttackTicks = attackTicks;
            Tile = Home = home;
        }

        /// <summary>The placement index, or the run's BossIndex for the stage boss.</summary>
        public int Id { get; }
        public MonsterKind Kind { get; }
        public bool IsBoss { get; }
        public string Name { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; }
        public int Attack { get; }
        public int Armor { get; }
        public TilePoint Tile { get; internal set; }
        public TilePoint Home { get; }
        public MonsterMood Mood { get; internal set; }
        public bool Alive => Mood != MonsterMood.Defeated;
        /// <summary>The party has seen this monster at least once.</summary>
        public bool Spotted { get; internal set; }
        internal int Experience { get; }
        internal int MoveTicks { get; }
        internal int AttackTicks { get; }
        internal int MoveReady { get; set; }
        internal int AttackReady { get; set; }
        internal int LastSawHero { get; set; }
        internal int TauntedUntil { get; set; }
        internal int WanderWait { get; set; }
        internal TilePoint? WanderGoal { get; set; }
    }
}
