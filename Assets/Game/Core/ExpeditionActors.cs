using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    public enum RunPhase { Planning, Travel, Combat, Victory, Defeat }
    public enum MonsterKind { None, Skeleton, Ghost, Zombie }
    public enum PlacementCheck { Allowed, Locked, InvalidKind, NotFloor, NearEntrance, OverBudget, NothingToRemove }
    public enum MonsterMood { Wandering, Chasing, Defeated }
    /// <summary>
    /// Attack, Heal and Taunt go from a source to a target. Drop: monster <see cref="CombatEvent.Source"/> left
    /// <see cref="DungeonRun.Drops"/>[<see cref="CombatEvent.Amount"/>]. Skill: hero <see cref="CombatEvent.Source"/>
    /// used the <see cref="HeroSkill"/> in <see cref="CombatEvent.Amount"/>; Target is the main target if any.
    /// Pattern: monster <see cref="CombatEvent.Source"/> began the <see cref="MonsterPattern"/> in
    /// <see cref="CombatEvent.Amount"/> at hero <see cref="CombatEvent.Target"/> (-1 for none); its blows arrive
    /// later as Attack events.
    /// </summary>
    public enum CombatEventKind { Attack, Heal, Taunt, Drop, Skill, Pattern }

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
        public HeroTactic(HeroRole role, int focus, int protect, string line,
            IReadOnlyDictionary<MonsterPattern, PatternResponse> responses = null)
        {
            Role = role;
            Focus = focus;
            Protect = protect;
            Line = line;
            Responses = responses ?? new Dictionary<MonsterPattern, PatternResponse>();
        }

        /// <summary>How to meet each enemy pattern until the situation changes; missing ones follow trait and role.</summary>
        public IReadOnlyDictionary<MonsterPattern, PatternResponse> Responses { get; }

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
            BaseArmor = armor;
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
        /// <summary>Defence including the worn armour.</summary>
        public int Armor => BaseArmor + (BodyArmor?.ArmorBonus ?? 0);
        /// <summary>Attack before the party level bonus, including the weapon.</summary>
        public int Attack => BaseAttack + (Weapon?.AttackBonus ?? 0);
        /// <summary>Ticks between this hero's attacks or heals, including the weapon.</summary>
        public int AttackTicks => Math.Max(MinAttackTicks, DungeonRun.HeroAttackTicks + (Weapon?.AttackTicksBonus ?? 0));
        public Equipment Weapon { get; private set; }
        public Equipment BodyArmor { get; private set; }
        /// <summary>Attack reach in tiles; 1 means an adjacent tile.</summary>
        public int Range { get; }
        public string LastLine { get; internal set; } = "";
        /// <summary>Trust in the god, set by how the GM answered this hero's prayer. Lasts the whole expedition.</summary>
        public Belief Belief { get; internal set; } = Belief.Steady;
        private const int MinAttackTicks = 5;
        internal int BaseAttack { get; }
        internal int BaseArmor { get; }

        /// <summary>What the hero has on in the slot this item would take.</summary>
        public Equipment WornIn(EquipmentSlot slot) => slot == EquipmentSlot.Weapon ? Weapon : BodyArmor;

        /// <summary>
        /// Puts the item on; the old one in that slot is gone. A higher maximum health heals by the difference, a
        /// lower one only trims the current health.
        /// </summary>
        internal void Equip(Equipment item)
        {
            if (!item.Fits(Role)) throw new InvalidOperationException($"{Name} cannot use {item.Name}.");
            if (item.Slot == EquipmentSlot.Weapon)
            {
                Weapon = item;
                return;
            }
            int gain = item.MaxHpBonus - (BodyArmor?.MaxHpBonus ?? 0);
            BodyArmor = item;
            MaxHp += gain;
            if (gain > 0) Hp += gain;
            Hp = Math.Min(Hp, MaxHp);
        }
        internal int MoveReady { get; set; }
        internal int ActReady { get; set; }
        /// <summary>Tick at which each of the role's two skills (see <see cref="HeroSkills.Of"/>) can fire again.</summary>
        internal int[] SkillReady { get; } = new int[2];
        /// <summary>The ranged hero's rapid fire lasts until this tick.</summary>
        internal int RapidFireUntil { get; set; }
        /// <summary>The tank's shield wall lasts until this tick.</summary>
        internal int ShieldWallUntil { get; set; }
        internal bool Critical { get; set; }
        /// <summary>Where the hero's current step started; it still counts as standing there until <see cref="StepUntil"/>.</summary>
        internal TilePoint StepFrom { get; set; }
        internal int StepUntil { get; set; }
    }

    public sealed class MonsterState
    {
        internal MonsterState(int id, MonsterKind kind, bool boss, string name, int maxHp, int attack, int armor,
            int reward, int moveTicks, int attackTicks, TilePoint home, params MonsterPattern[] patterns)
        {
            Patterns = Array.AsReadOnly((MonsterPattern[])patterns.Clone());
            PatternReady = new int[patterns.Length];
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
        /// <summary>The monster's current step lands on this tick.</summary>
        internal int StepUntil { get; set; }
        internal int AttackReady { get; set; }
        internal int TauntedUntil { get; set; }
        internal int WanderWait { get; set; }
        internal TilePoint? WanderGoal { get; set; }
        /// <summary>Telegraphed attacks besides the plain blow, in the order the rules list them.</summary>
        public IReadOnlyList<MonsterPattern> Patterns { get; }
        /// <summary>Tick at which each of <see cref="Patterns"/> can be cast again.</summary>
        internal int[] PatternReady { get; }
    }
}
