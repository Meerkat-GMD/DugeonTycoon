using System;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// Each role has a starting skill and one that opens at <see cref="HeroSkills.UnlockLevel"/>. The rules fire
    /// them on a condition and a cooldown; nothing else spends a resource.
    /// </summary>
    public enum HeroSkill
    {
        Taunt,
        ShieldWall,
        PowerStrike,
        Whirlwind,
        PiercingShot,
        RapidFire,
        MassHeal,
        Revive
    }

    public static class HeroSkills
    {
        /// <summary>Party level that opens every role's second skill. Balance value.</summary>
        public const int UnlockLevel = 3;

        public static string Name(HeroSkill skill)
        {
            switch (skill)
            {
                case HeroSkill.Taunt: return "도발";
                case HeroSkill.ShieldWall: return "방패 벽";
                case HeroSkill.PowerStrike: return "강타";
                case HeroSkill.Whirlwind: return "회전 베기";
                case HeroSkill.PiercingShot: return "관통 사격";
                case HeroSkill.RapidFire: return "속사";
                case HeroSkill.MassHeal: return "광역 치유";
                case HeroSkill.Revive: return "부활";
                default: throw new ArgumentOutOfRangeException(nameof(skill));
            }
        }

        /// <summary>The skill a role has from the start.</summary>
        public static HeroSkill Starting(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return HeroSkill.Taunt;
                case HeroRole.MeleeDamage: return HeroSkill.PowerStrike;
                case HeroRole.RangedDamage: return HeroSkill.PiercingShot;
                case HeroRole.Healer: return HeroSkill.MassHeal;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        /// <summary>The skill a role gains at <see cref="UnlockLevel"/>.</summary>
        public static HeroSkill Unlocked(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return HeroSkill.ShieldWall;
                case HeroRole.MeleeDamage: return HeroSkill.Whirlwind;
                case HeroRole.RangedDamage: return HeroSkill.RapidFire;
                case HeroRole.Healer: return HeroSkill.Revive;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        /// <summary>The role's two skills: index 0 starts open, index 1 opens at <see cref="UnlockLevel"/>.</summary>
        public static HeroSkill Of(HeroRole role, int index) => index == 0 ? Starting(role) : Unlocked(role);
    }
}
