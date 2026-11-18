using System;

namespace DungeonTycoon.Core
{
    public enum Ability { Strength, Dexterity, Wisdom, Charisma }

    /// <summary>Personality shapes what a hero argues for at a fork. It never changes combat numbers or checks.</summary>
    public enum PersonalityTag { Reckless, Cautious, Curious }

    public static class HeroTraits
    {
        public static string Name(Ability ability)
        {
            switch (ability)
            {
                case Ability.Strength: return "힘";
                case Ability.Dexterity: return "민첩";
                case Ability.Wisdom: return "지혜";
                case Ability.Charisma: return "매력";
                default: throw new ArgumentOutOfRangeException(nameof(ability));
            }
        }

        public static string Name(PersonalityTag tag)
        {
            switch (tag)
            {
                case PersonalityTag.Reckless: return "무모함";
                case PersonalityTag.Cautious: return "조심스러움";
                case PersonalityTag.Curious: return "호기심";
                default: throw new ArgumentOutOfRangeException(nameof(tag));
            }
        }

        /// <summary>Appends 이 or 가 depending on whether the word ends in a final consonant.</summary>
        public static string Subject(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            char last = word[word.Length - 1];
            bool batchim = last >= '가' && last <= '힣' && (last - '가') % 28 != 0;
            return word + (batchim ? "이" : "가");
        }

        /// <summary>The ability each role leans on when answering an adversity.</summary>
        public static Ability TrialAbility(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank:
                case HeroRole.MeleeDamage: return Ability.Strength;
                case HeroRole.Healer: return Ability.Wisdom;
                default: return Ability.Dexterity;
            }
        }
    }
}
