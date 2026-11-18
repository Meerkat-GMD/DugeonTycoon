using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    public enum RunPhase { Planning, Travel, Combat, Victory, Defeat }
    public enum MonsterKind { None, Skeleton, Ghost, Zombie, Archer }
    /// <summary>NotInRoster: this stage's roster does not have that monster yet.</summary>
    public enum PlacementCheck { Allowed, Locked, InvalidKind, NotFloor, NearEntrance, OverBudget, NothingToRemove, NotInRoster }
    /// <summary>Patrolling: a fixed patrol member walking its route before it notices the party.</summary>
    public enum MonsterMood { Wandering, Chasing, Defeated, Patrolling }
    /// <summary>
    /// Attack, Heal and Taunt go from a source to a target. Drop: monster <see cref="CombatEvent.Source"/> left
    /// <see cref="DungeonRun.Drops"/>[<see cref="CombatEvent.Amount"/>]. Skill: hero <see cref="CombatEvent.Source"/>
    /// used the <see cref="HeroSkill"/> in <see cref="CombatEvent.Amount"/>; Target is the main target if any.
    /// Pattern: monster <see cref="CombatEvent.Source"/> began the <see cref="MonsterPattern"/> in
    /// <see cref="CombatEvent.Amount"/> at hero <see cref="CombatEvent.Target"/> (-1 for none); its blows arrive
    /// later as Attack events. Chest: the party opened <see cref="DungeonRun.Chests"/>[<see cref="CombatEvent.Source"/>]
    /// and found <see cref="DungeonRun.Drops"/>[<see cref="CombatEvent.Amount"/>]. Spring: the party drank from
    /// <see cref="DungeonRun.Springs"/>[<see cref="CombatEvent.Source"/>] and shed <see cref="CombatEvent.Amount"/> steps of
    /// fatigue. Both have Target -1.
    /// </summary>
    public enum CombatEventKind { Attack, Heal, Taunt, Drop, Skill, Pattern, Chest, Spring }

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

        internal HeroState(HeroRole role, string name, int range, int[] abilities, HeroBuild build)
        {
            if (abilities == null || abilities.Length != 4) throw new ArgumentException("Four ability modifiers are required.", nameof(abilities));
            Role = role;
            Name = name;
            Range = range;
            this.abilities = (int[])abilities.Clone();
            ApplyBuild(build);
        }

        public HeroRole Role { get; }
        public string Name { get; }
        /// <summary>The formation chosen before the first departure: two traits and the stat points.</summary>
        public HeroBuild Build { get; private set; }
        public CombatTrait CombatTrait => Build.Combat;
        public PersonalityTrait Personality => Build.Personality;
        public Voice Voice => HeroTraits.VoiceOf(Build.Personality);
        /// <summary>Movement speed in hundredths of a percent of the standard speed. Only combat moves use it.</summary>
        public int SpeedHundredths { get; private set; }

        /// <summary>
        /// Sets the starting stats from the build: health full, no equipment yet. Only before the first departure,
        /// when there is no level or gear to keep.
        /// </summary>
        internal void ApplyBuild(HeroBuild build)
        {
            var stats = HeroStats.Starting(Role, build ?? throw new ArgumentNullException(nameof(build)));
            Build = build;
            Hp = MaxHp = stats.MaxHp;
            BaseAttack = stats.Attack;
            BaseArmor = stats.Armor;
            SpeedHundredths = stats.SpeedHundredths;
        }
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
        internal int BaseAttack { get; private set; }
        internal int BaseArmor { get; private set; }
        /// <summary>Ticks of movement owed, in 1/<see cref="SpeedHundredths"/> units, so fractional speeds stay exact.</summary>
        internal int MoveCarry { get; set; }

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

        /// <summary>
        /// The index in <see cref="DungeonRun.Monsters"/>: placements first, then patrol members squad by squad, and the
        /// run's BossIndex for the stage boss.
        /// </summary>
        public int Id { get; }
        /// <summary>Tiles from which its plain blow lands: 1 for melee, more for a monster that shoots.</summary>
        public int Range { get; internal set; } = 1;
        /// <summary>The index of its fixed patrol in <see cref="DungeonRun.Patrols"/>, or -1 for a placed monster or the boss.</summary>
        public int Squad { get; internal set; } = -1;
        public bool IsPatrol => Squad >= 0;
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
        /// <summary>Joined a fight that was already going on: what lookouts, interceptors, finishers and giant hunters answer.</summary>
        public bool Reinforcement { get; internal set; }
        /// <summary>A zombie or a boss: the big monsters a giant hunter goes for.</summary>
        public bool IsBig => IsBoss || Kind == MonsterKind.Zombie;
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
