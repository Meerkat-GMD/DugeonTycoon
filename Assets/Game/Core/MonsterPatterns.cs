using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// A telegraphed monster attack. Every pattern shows its area first and lands on <see cref="MonsterPatterns.FireTicks"/>;
    /// the tracking beam follows its target until <see cref="MonsterPatterns.LockTicks"/>.
    /// </summary>
    public enum MonsterPattern
    {
        /// <summary>The 3×3 tiles around the caster.</summary>
        Slam,
        /// <summary>A one-tile-wide line toward the target, fixed when cast.</summary>
        SoulBolt,
        /// <summary>A three-tile-wide line that follows the top-aggro hero, then locks.</summary>
        TrackingBeam,
        /// <summary>The 3×3 tiles around where the target stood when cast.</summary>
        Meteor
    }

    /// <summary>How a hero meets a pattern. Lure only means something for the tracking beam's own target before it locks.</summary>
    public enum PatternResponse { Dodge, Hold, Lure }

    /// <summary>Aiming: the area still follows the target. Locked: the area is final. Fired: it has landed and only lingers for the board.</summary>
    public enum CastPhase { Aiming, Locked, Fired }

    public static class MonsterPatterns
    {
        public static string Name(MonsterPattern pattern)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return "내려찍기";
                case MonsterPattern.SoulBolt: return "영혼 화살";
                case MonsterPattern.TrackingBeam: return "추적 레이저";
                case MonsterPattern.Meteor: return "메테오";
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        public static string Name(PatternResponse response)
        {
            switch (response)
            {
                case PatternResponse.Dodge: return "회피";
                case PatternResponse.Hold: return "버팀";
                case PatternResponse.Lure: return "유도";
                default: throw new ArgumentOutOfRangeException(nameof(response));
            }
        }

        /// <summary>Ticks from the cast until the area stops following the target. 0 for patterns fixed when cast. Balance value.</summary>
        public static int LockTicks(MonsterPattern pattern) => pattern == MonsterPattern.TrackingBeam ? 15 : 0;

        /// <summary>Ticks from the cast until it lands. Balance value.</summary>
        public static int FireTicks(MonsterPattern pattern)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return 12;
                case MonsterPattern.SoulBolt: return 10;
                case MonsterPattern.TrackingBeam: return 20;
                case MonsterPattern.Meteor: return 20;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        /// <summary>Ticks from the cast until the same monster can cast it again. Balance value.</summary>
        public static int CooldownTicks(MonsterPattern pattern)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return 60;
                case MonsterPattern.SoulBolt: return 50;
                case MonsterPattern.TrackingBeam: return 100;
                case MonsterPattern.Meteor: return 120;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        /// <summary>A hit deals this percentage of the caster's attack before armour. Balance value.</summary>
        public static int DamagePercent(MonsterPattern pattern)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return 150;
                case MonsterPattern.SoulBolt: return 150;
                case MonsterPattern.TrackingBeam: return 180;
                case MonsterPattern.Meteor: return 200;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        /// <summary>How far the pattern reaches from the caster in tiles. Balance value.</summary>
        public static int Reach(MonsterPattern pattern)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return 1;
                case MonsterPattern.SoulBolt: return 5;
                case MonsterPattern.TrackingBeam: return 10;
                case MonsterPattern.Meteor: return 10;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        /// <summary>
        /// What a role does with no AI answer and no trait for it. The tank holds under a slam to keep the monsters on
        /// itself; everyone lures a tracking beam aimed at them and dodges the rest.
        /// </summary>
        public static PatternResponse RoleDefault(HeroRole role, MonsterPattern pattern, bool beamTarget)
        {
            if (pattern == MonsterPattern.TrackingBeam && beamTarget) return PatternResponse.Lure;
            if (pattern == MonsterPattern.Slam && role == HeroRole.Tank) return PatternResponse.Hold;
            return PatternResponse.Dodge;
        }

        /// <summary>The short line a hero shouts when it starts answering a pattern.</summary>
        public static string Shout(PatternResponse response)
        {
            switch (response)
            {
                case PatternResponse.Dodge: return "피해!";
                case PatternResponse.Hold: return "계속 친다!";
                case PatternResponse.Lure: return "이쪽으로 뺄게!";
                default: throw new ArgumentOutOfRangeException(nameof(response));
            }
        }
    }

    /// <summary>One telegraphed attack under way. The board draws <see cref="Area"/>; the rules hit whoever stands in it on <see cref="FireTick"/>.</summary>
    public sealed class PatternCast
    {
        internal PatternCast(MonsterState caster, MonsterPattern pattern, int target, int start)
        {
            Caster = caster;
            Pattern = pattern;
            Target = target;
            StartTick = start;
            LockTick = start + MonsterPatterns.LockTicks(pattern);
            FireTick = start + MonsterPatterns.FireTicks(pattern);
            Phase = MonsterPatterns.LockTicks(pattern) > 0 ? CastPhase.Aiming : CastPhase.Locked;
        }

        public MonsterState Caster { get; }
        public MonsterPattern Pattern { get; }
        /// <summary>Hero role (as int) the pattern was aimed at, or -1 for the slam.</summary>
        public int Target { get; }
        public int StartTick { get; }
        public int LockTick { get; }
        public int FireTick { get; }
        public CastPhase Phase { get; internal set; }
        /// <summary>The tiles it will hit, or did hit once fired.</summary>
        public IReadOnlyCollection<TilePoint> Area => area;
        /// <summary>Heroes (as roles) it hit; empty until fired.</summary>
        public IReadOnlyList<int> Hits => hits;

        internal HashSet<TilePoint> area = new HashSet<TilePoint>();
        internal readonly List<int> hits = new List<int>();
        /// <summary>Heroes who have already said how they meet this cast, so each speaks once.</summary>
        internal readonly HashSet<int> answered = new HashSet<int>();
        /// <summary>The tile the area is aimed through.</summary>
        internal TilePoint Aim { get; set; }
    }
}
