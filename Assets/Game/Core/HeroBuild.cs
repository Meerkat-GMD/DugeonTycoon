using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// How a hero fights when a situation matches: dodging, whom to hit, whom to guard, where to stand. Outside its
    /// trigger the hero follows the role's own tactics.
    /// </summary>
    public enum CombatTrait
    {
        DamageMad, Protector, QuickFeet, Stoic, BeamPilot, Lookout, Interceptor, Finisher,
        GiantHunter, Triage, Bodyguard, Cornered, SurvivalFirst, Avenger, RescueLeader, LineKeeper
    }

    /// <summary>What a hero says and wants away from the blows: fork opinions, armour bids, prayers, remarks.</summary>
    public enum PersonalityTrait
    {
        Greedy, Generous, Appraiser, Collector, Underdog, Challenger, CautiousGuide, Curious,
        Navigator, Devout, Skeptic, Cheerer, Grumbler, Peacemaker
    }

    /// <summary>The four stats a formation spends points on.</summary>
    public enum BuildStat { MaxHp, Attack, Armor, Speed }

    /// <summary>
    /// A flat change to the starting stats. Speed is in percent of the role's own base speed and multiplies it;
    /// the others add.
    /// </summary>
    public readonly struct StatBonus
    {
        public StatBonus(int maxHp = 0, int attack = 0, int armor = 0, int speedPercent = 0)
        {
            MaxHp = maxHp;
            Attack = attack;
            Armor = armor;
            SpeedPercent = speedPercent;
        }

        public int MaxHp { get; }
        public int Attack { get; }
        public int Armor { get; }
        public int SpeedPercent { get; }

        public int this[BuildStat stat]
        {
            get
            {
                switch (stat)
                {
                    case BuildStat.MaxHp: return MaxHp;
                    case BuildStat.Attack: return Attack;
                    case BuildStat.Armor: return Armor;
                    case BuildStat.Speed: return SpeedPercent;
                    default: throw new ArgumentOutOfRangeException(nameof(stat));
                }
            }
        }

        public static StatBonus operator +(StatBonus a, StatBonus b) =>
            new StatBonus(a.MaxHp + b.MaxHp, a.Attack + b.Attack, a.Armor + b.Armor, a.SpeedPercent + b.SpeedPercent);
    }

    /// <summary>One trait as the formation screen and the AI see it.</summary>
    public sealed class TraitInfo
    {
        internal TraitInfo(string name, StatBonus bonus, string trigger, string line, HeroRole? onlyFor = null)
        {
            Name = name;
            Bonus = bonus;
            Trigger = trigger;
            Line = line;
            OnlyFor = onlyFor;
        }

        public string Name { get; }
        public StatBonus Bonus { get; }
        /// <summary>When it fires and what the hero does then.</summary>
        public string Trigger { get; }
        /// <summary>What the hero does, in one short line for cards and lists.</summary>
        public string Summary { get; internal set; } = "";
        /// <summary>A sample line the hero says when it fires.</summary>
        public string Line { get; }
        /// <summary>The behaviour is in the game. Every trait acts now; screens still read it to mark unfinished ones.</summary>
        public bool Works => true;
        /// <summary>The role the behaviour needs (a skill it uses), or null for anyone.</summary>
        public HeroRole? OnlyFor { get; }
    }

    /// <summary>The thirty traits. Bonuses and triggers are playtest drafts from hero-traits-design.md.</summary>
    public static class TraitCatalog
    {
        private static readonly Dictionary<CombatTrait, TraitInfo> combat = new Dictionary<CombatTrait, TraitInfo>
        {
            [CombatTrait.DamageMad] = new TraitInfo("딜에 미친 자", new StatBonus(maxHp: -8, attack: 5),
                "자신을 겨눈 빔이나 발밑 장판이 예고되면 공격을 계속한다.", "이 정도는 버텨. 계속 친다!"),
            [CombatTrait.Protector] = new TraitInfo("동료를 지키는 자", new StatBonus(maxHp: 20, attack: -2),
                "빔이 자신을 겨누면 빈 벽 쪽으로 유도하고, 발밑에 장판이 생기면 범위를 벗어난다.", "내가 이쪽으로 뺄게!"),
            [CombatTrait.QuickFeet] = new TraitInfo("재빠른 발", new StatBonus(maxHp: -10, speedPercent: 15),
                "발밑 장판이 예고되면 가장 빨리 닿는 안전한 자리로 이동한다.", "먼저 장판에서 빠질게!"),
            [CombatTrait.Stoic] = new TraitInfo("버티는 자", new StatBonus(armor: 4, speedPercent: -10),
                "장판이 생기고 체력 60% 이상이면 현재 역할 행동을 이어간다.", "이 정도 체력이면 버틸 수 있어."),
            [CombatTrait.BeamPilot] = new TraitInfo("사선 유도자", new StatBonus(attack: -2, speedPercent: 10),
                "추적형 빔이 자신을 겨누면 조준이 잠기기 전에 빈 벽 쪽으로 돌린다.", "사선을 벽으로 돌릴게."),
            [CombatTrait.Lookout] = new TraitInfo("망보기", new StatBonus(maxHp: -6, speedPercent: 8),
                "교전 중 증원이 합류하면 적의 종류와 접근 방향을 알린다.", "오른쪽! 큰 놈이 온다!"),
            [CombatTrait.Interceptor] = new TraitInfo("가로막는 자", new StatBonus(attack: -2, armor: 4),
                "증원이 위독한 아군을 노리면 그 적에게 붙어 시선을 대신 받는다. 탱커면 도발도 쓴다.", "내가 저 녀석을 막을게!"),
            [CombatTrait.Finisher] = new TraitInfo("마무리 집착", new StatBonus(maxHp: -8, attack: 4),
                "증원이 접근하고 기존 적의 체력이 15% 이하면 기존 적부터 마무리한다.", "이 녀석 한 대면 끝이야!"),
            [CombatTrait.GiantHunter] = new TraitInfo("거인 사냥꾼", new StatBonus(attack: 3, speedPercent: -5),
                "큰 몬스터(좀비·보스)가 증원으로 합류해 사거리에 들어오면 그 몬스터를 공격한다.", "큰 놈부터 쓰러뜨리자."),
            [CombatTrait.Triage] = new TraitInfo("응급 처치광", new StatBonus(attack: -1, speedPercent: 5),
                "체력 25% 이하 아군이 있으면 부활·광역 치유·작전보다 먼저 그 아군을 치료한다.", "움직이지 마, 지금 치료할게!",
                onlyFor: HeroRole.Healer),
            [CombatTrait.Bodyguard] = new TraitInfo("호위 본능", new StatBonus(maxHp: 16, speedPercent: -6),
                "위독한 힐러를 적이 노리면 그 적에게 붙어 힐러 대신 맞는다.", "힐러 뒤로! 나부터 상대해."),
            [CombatTrait.Cornered] = new TraitInfo("궁지의 전사", new StatBonus(attack: 5, armor: -3),
                "자기 체력 25% 이하이고 적이 사거리에 있으면 붙은 적에게서 물러나지 않고 공격한다.", "나 아직 안 쓰러졌어!"),
            [CombatTrait.SurvivalFirst] = new TraitInfo("생존 우선", new StatBonus(maxHp: 12, attack: -2),
                "자기 체력 25% 이하면 힐러의 회복 사거리로 물러난다.", "잠깐 빠질게. 치료 좀 부탁해!"),
            [CombatTrait.Avenger] = new TraitInfo("복수심", new StatBonus(attack: 4, armor: -2),
                "동료가 쓰러지고 가해자가 살아 있으면 그 적을 노린다.", "네가 쓰러뜨렸지. 이제 내 차례야."),
            [CombatTrait.RescueLeader] = new TraitInfo("구조대장", new StatBonus(maxHp: 12, speedPercent: -5),
                "동료가 쓰러지고 부활을 쓸 수 있으면 예고 공격을 피하지 않고 부활 사거리로 달려간다.", "아직 끝난 거 아니야. 일어나!",
                onlyFor: HeroRole.Healer),
            [CombatTrait.LineKeeper] = new TraitInfo("대열 정비자", new StatBonus(attack: -2, armor: 3),
                "전열(탱커·근거리 딜러)이 쓰러지면 적에게서 먼 후퇴 지점을 알리고, 후열이 잠시 그곳에 모인다.", "대열 무너졌어! 내 쪽으로 모여.")
        };

        private static readonly Dictionary<PersonalityTrait, TraitInfo> personality = new Dictionary<PersonalityTrait, TraitInfo>
        {
            [PersonalityTrait.Greedy] = new TraitInfo("탐욕광", new StatBonus(maxHp: 8, speedPercent: -5),
                "장비가 분배되면 항상 입찰을 선언한다. 위력이 내려가면 판정 후보에서 빠진다.", "일단 나도 입찰!"),
            [PersonalityTrait.Generous] = new TraitInfo("양보하는 자", new StatBonus(maxHp: 12, attack: -2),
                "장비로 더 큰 이득을 보는 살아 있는 동료가 입찰하면 양보한다.", "이번 건 네가 입어."),
            [PersonalityTrait.Appraiser] = new TraitInfo("감정가", new StatBonus(maxHp: -6, armor: 2),
                "방어구 교체로 위력이 5 이상 오를 때만 입찰한다.", "입어볼 가치가 있는 것만 고를게."),
            [PersonalityTrait.Collector] = new TraitInfo("희귀품 수집가", new StatBonus(attack: 2, speedPercent: -5),
                "희귀 방어구가 나오면 언제나 입찰한다.", "희귀품이면 내가 빠질 수 없지."),
            [PersonalityTrait.Underdog] = new TraitInfo("약자 챙김", new StatBonus(maxHp: 10, armor: -2),
                "방어가 더 낮은 동료가 같은 장비에 입찰하면 양보한다.", "방어구부터 필요한 사람이 가져."),
            [PersonalityTrait.Challenger] = new TraitInfo("승부광", new StatBonus(attack: 4, armor: -2),
                "갈림길에서 강한 적이 보이는 쪽을 제안한다.", "저 녀석을 잡고 가자."),
            [PersonalityTrait.CautiousGuide] = new TraitInfo("신중한 길잡이", new StatBonus(armor: 3, speedPercent: -5),
                "갈림길에서 적이 보이지 않는 길을 제안한다. 파티가 다쳤으면 더 강하게 고집한다.", "지금 체력으론 저 길이 낫겠어."),
            [PersonalityTrait.Curious] = new TraitInfo("호기심쟁이", new StatBonus(armor: -2, speedPercent: 10),
                "갈림길에서 더 트이고 깊은 미탐색 길을 제안한다.", "저 모퉁이 너머만 보고 오자."),
            [PersonalityTrait.Navigator] = new TraitInfo("길눈 밝은 자", new StatBonus(attack: -2, speedPercent: 8),
                "갈림길에서 본 적 있는 보스방 표식에 가장 가까워지는 길을 제안한다.", "아까 본 표식, 이쪽으로 이어져."),
            [PersonalityTrait.Devout] = new TraitInfo("독실한 신자", new StatBonus(maxHp: 8, armor: -1),
                "동료가 절망하면 그 동료 대신 기도하고, 자신이 절망하면 언제나 기도한다.", "제발 저 사람만은 다시 일어서게 해 주세요."),
            [PersonalityTrait.Skeptic] = new TraitInfo("불신자", new StatBonus(maxHp: -6, armor: 2),
                "기도할 기회가 생기면 거절하고 하던 행동을 이어간다.", "지금은 내가 할 일을 할게."),
            [PersonalityTrait.Cheerer] = new TraitInfo("칭찬쟁이", new StatBonus(attack: -1, speedPercent: 5),
                "치료로 위독한 동료가 위기를 벗어나면 치료한 동료를 칭찬한다.", "그 회복 덕에 버텼어!"),
            [PersonalityTrait.Grumbler] = new TraitInfo("잔소리꾼", new StatBonus(armor: 2, speedPercent: -5),
                "동료가 예고 공격에 맞으면 무엇을 놓쳤는지 짚어 준다.", "젠장, 다음엔 바닥부터 봐!"),
            [PersonalityTrait.Peacemaker] = new TraitInfo("중재자", new StatBonus(maxHp: 8, attack: -1),
                "같은 방어구에 후보가 둘 이상이면 각자의 이득을 짚고 판정으로 정하자고 말한다.", "둘 다 필요한 건 알겠어. 판정대로 하자.")
        };

        static TraitCatalog()
        {
            var combatSummaries = new Dictionary<CombatTrait, string>
            {
                [CombatTrait.DamageMad] = "예고 공격에도 피하지 않고 계속 공격",
                [CombatTrait.Protector] = "빔은 빈 벽 쪽으로 끌고, 장판은 피함",
                [CombatTrait.QuickFeet] = "장판 예고 때 가장 가까운 안전지대로",
                [CombatTrait.Stoic] = "체력 60% 이상이면 장판을 버팀",
                [CombatTrait.BeamPilot] = "추적 빔을 잠기기 전에 벽 쪽으로 돌림",
                [CombatTrait.Lookout] = "증원의 종류와 접근 방향을 알림",
                [CombatTrait.Interceptor] = "위독한 아군을 쫓는 증원을 막아섬",
                [CombatTrait.Finisher] = "증원이 와도 빈사의 적부터 마무리",
                [CombatTrait.GiantHunter] = "큰 몬스터가 오면 그쪽부터 공격",
                [CombatTrait.Triage] = "체력 25% 이하 아군을 최우선 치료",
                [CombatTrait.Bodyguard] = "쫓기는 위독한 힐러 앞을 막아섬",
                [CombatTrait.Cornered] = "위독해도 물러나지 않고 공격",
                [CombatTrait.SurvivalFirst] = "위독하면 힐러 곁으로 물러남",
                [CombatTrait.Avenger] = "동료를 쓰러뜨린 적을 노림",
                [CombatTrait.RescueLeader] = "쓰러진 동료를 부활하러 달려감",
                [CombatTrait.LineKeeper] = "전열이 무너지면 후퇴 지점을 알림"
            };
            var personalitySummaries = new Dictionary<PersonalityTrait, string>
            {
                [PersonalityTrait.Greedy] = "방어구가 나오면 무조건 입찰",
                [PersonalityTrait.Generous] = "더 이득인 동료가 원하면 양보",
                [PersonalityTrait.Appraiser] = "위력이 5 이상 오를 때만 입찰",
                [PersonalityTrait.Collector] = "희귀 장비가 나오면 입찰",
                [PersonalityTrait.Underdog] = "방어가 낮은 동료에게 양보",
                [PersonalityTrait.Challenger] = "갈림길에서 적이 보이는 길을 고집",
                [PersonalityTrait.CautiousGuide] = "갈림길에서 안전한 길을 고집",
                [PersonalityTrait.Curious] = "갈림길에서 트인 미탐색 길을 고집",
                [PersonalityTrait.Navigator] = "보스방 표식이 있던 쪽을 제안",
                [PersonalityTrait.Devout] = "위독한 동료를 위해 기도",
                [PersonalityTrait.Skeptic] = "기도할 기회가 와도 늘 거절",
                [PersonalityTrait.Cheerer] = "위기를 넘긴 치료를 칭찬",
                [PersonalityTrait.Grumbler] = "피할 수 있던 피격을 지적",
                [PersonalityTrait.Peacemaker] = "장비 다툼을 판정으로 정리"
            };
            foreach (var pair in combatSummaries) combat[pair.Key].Summary = pair.Value;
            foreach (var pair in personalitySummaries) personality[pair.Key].Summary = pair.Value;
        }

        public static TraitInfo Of(CombatTrait trait) => combat[trait];
        public static TraitInfo Of(PersonalityTrait trait) => personality[trait];
        public static IEnumerable<CombatTrait> CombatTraits => (CombatTrait[])Enum.GetValues(typeof(CombatTrait));
        public static IEnumerable<PersonalityTrait> PersonalityTraits => (PersonalityTrait[])Enum.GetValues(typeof(PersonalityTrait));
    }

    /// <summary>
    /// A hero's formation: one combat trait, one personality trait and up to <see cref="Budget"/> points spread over
    /// the four stats. Immutable; the <c>With</c> methods return a changed copy.
    /// </summary>
    public sealed class HeroBuild : IEquatable<HeroBuild>
    {
        public const int Budget = 10;
        private readonly int[] points;

        public HeroBuild(CombatTrait combat, PersonalityTrait personality, int maxHp = 0, int attack = 0, int armor = 0, int speed = 0)
            : this(combat, personality, new[] { maxHp, attack, armor, speed }) { }

        private HeroBuild(CombatTrait combat, PersonalityTrait personality, int[] points)
        {
            if (!Enum.IsDefined(typeof(CombatTrait), combat)) throw new ArgumentOutOfRangeException(nameof(combat));
            if (!Enum.IsDefined(typeof(PersonalityTrait), personality)) throw new ArgumentOutOfRangeException(nameof(personality));
            int spent = 0;
            foreach (int p in points)
            {
                if (p < 0) throw new ArgumentOutOfRangeException(nameof(points), "Points cannot be negative.");
                spent += p;
            }
            if (spent > Budget) throw new ArgumentOutOfRangeException(nameof(points), $"At most {Budget} points.");
            Combat = combat;
            Personality = personality;
            this.points = points;
            Spent = spent;
        }

        public CombatTrait Combat { get; }
        public PersonalityTrait Personality { get; }
        public int Spent { get; }
        public int Left => Budget - Spent;
        public int Points(BuildStat stat) => points[(int)stat];

        public HeroBuild With(CombatTrait trait) => new HeroBuild(trait, Personality, (int[])points.Clone());
        public HeroBuild With(PersonalityTrait trait) => new HeroBuild(Combat, trait, (int[])points.Clone());

        /// <summary>The same build with <paramref name="stat"/> at <paramref name="value"/> points; throws past the budget.</summary>
        public HeroBuild WithPoints(BuildStat stat, int value)
        {
            var copy = (int[])points.Clone();
            copy[(int)stat] = value;
            return new HeroBuild(Combat, Personality, copy);
        }

        /// <summary>
        /// What a hero starts with when the GM changes nothing: role-fitting traits and all ten points spent. A
        /// draft for playtests (hero-traits-design.md, 기본 편성).
        /// </summary>
        public static HeroBuild Default(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return new HeroBuild(CombatTrait.Protector, PersonalityTrait.CautiousGuide, maxHp: 6, armor: 4);
                case HeroRole.Healer: return new HeroBuild(CombatTrait.Triage, PersonalityTrait.CautiousGuide, maxHp: 5, armor: 3, speed: 2);
                case HeroRole.MeleeDamage: return new HeroBuild(CombatTrait.DamageMad, PersonalityTrait.Challenger, maxHp: 3, attack: 5, armor: 2);
                case HeroRole.RangedDamage: return new HeroBuild(CombatTrait.QuickFeet, PersonalityTrait.Curious, maxHp: 2, attack: 6, speed: 2);
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        public bool Equals(HeroBuild other) =>
            other != null && Combat == other.Combat && Personality == other.Personality &&
            points[0] == other.points[0] && points[1] == other.points[1] && points[2] == other.points[2] && points[3] == other.points[3];

        public override bool Equals(object obj) => Equals(obj as HeroBuild);

        public override int GetHashCode()
        {
            int hash = (int)Combat * 31 + (int)Personality;
            foreach (int p in points) hash = hash * 31 + p;
            return hash;
        }
    }

    /// <summary>Starting stats before equipment and party level.</summary>
    public readonly struct HeroStatLine
    {
        public HeroStatLine(int maxHp, int attack, int armor, int speedHundredths)
        {
            MaxHp = maxHp;
            Attack = attack;
            Armor = armor;
            SpeedHundredths = speedHundredths;
        }

        public int MaxHp { get; }
        public int Attack { get; }
        public int Armor { get; }
        /// <summary>Movement speed in hundredths of a percent of the standard speed: 9630 is 96.3%.</summary>
        public int SpeedHundredths { get; }
        public double SpeedPercent => SpeedHundredths / 100.0;
    }

    /// <summary>Role base stats, what one point buys, and how a build turns into starting stats.</summary>
    public static class HeroStats
    {
        /// <summary>Before traits and points. Speed 100 is the standard speed. Balance values.</summary>
        public static HeroStatLine Base(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return new HeroStatLine(104, 8, 4, 9000);
                case HeroRole.Healer: return new HeroStatLine(44, 4, 0, 10000);
                case HeroRole.MeleeDamage: return new HeroStatLine(54, 13, 1, 11000);
                case HeroRole.RangedDamage: return new HeroStatLine(48, 10, 0, 10500);
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        /// <summary>What one point adds. Speed is percent of the role's base speed.</summary>
        public static int Gain(BuildStat stat)
        {
            switch (stat)
            {
                case BuildStat.MaxHp: return 6;
                case BuildStat.Attack: return 1;
                case BuildStat.Armor: return 1;
                case BuildStat.Speed: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        /// <summary>The trait bonuses and points of a build, summed as one bonus.</summary>
        public static StatBonus Bonus(HeroBuild build) =>
            TraitCatalog.Of(build.Combat).Bonus + TraitCatalog.Of(build.Personality).Bonus +
            new StatBonus(build.Points(BuildStat.MaxHp) * Gain(BuildStat.MaxHp), build.Points(BuildStat.Attack) * Gain(BuildStat.Attack),
                build.Points(BuildStat.Armor) * Gain(BuildStat.Armor), build.Points(BuildStat.Speed) * Gain(BuildStat.Speed));

        /// <summary>
        /// Base plus both traits plus points. Health is at least 1, attack and armour at least 0. Speed percentages
        /// add up and multiply the role's base speed, at least 1% of the standard speed.
        /// </summary>
        public static HeroStatLine Starting(HeroRole role, HeroBuild build)
        {
            var b = Base(role);
            var bonus = Bonus(build);
            return new HeroStatLine(
                Math.Max(1, b.MaxHp + bonus.MaxHp),
                Math.Max(0, b.Attack + bonus.Attack),
                Math.Max(0, b.Armor + bonus.Armor),
                Math.Max(100, b.SpeedHundredths * (100 + bonus.SpeedPercent) / 100));
        }
    }
}
