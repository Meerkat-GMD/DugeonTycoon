using System;

namespace DungeonTycoon.Core
{
    public enum Ability { Strength, Dexterity, Wisdom, Charisma }

    /// <summary>
    /// How a hero sounds and leans at a fork, from the personality trait. Three traits carry over the old
    /// personality tags (무모함 → 승부광, 조심스러움 → 신중한 길잡이, 호기심 → 호기심쟁이); the rest speak plainly.
    /// </summary>
    public enum Voice { Neutral, Bold, Careful, Curious }

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

        public static string Name(CombatTrait trait) => TraitCatalog.Of(trait).Name;
        public static string Name(PersonalityTrait trait) => TraitCatalog.Of(trait).Name;

        public static string Name(BuildStat stat)
        {
            switch (stat)
            {
                case BuildStat.MaxHp: return "체력";
                case BuildStat.Attack: return "공격력";
                case BuildStat.Armor: return "방어";
                case BuildStat.Speed: return "이동 속도";
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        public static Voice VoiceOf(PersonalityTrait trait)
        {
            switch (trait)
            {
                case PersonalityTrait.Challenger: return Voice.Bold;
                case PersonalityTrait.CautiousGuide: return Voice.Careful;
                case PersonalityTrait.Curious: return Voice.Curious;
                default: return Voice.Neutral;
            }
        }

        /// <summary>Appends 이 or 가 depending on whether the word ends in a final consonant.</summary>
        public static string Subject(string word) => Particle(word, "이", "가");

        /// <summary>Appends the particle that fits the last syllable: <paramref name="afterConsonant"/> after a final consonant.</summary>
        public static string Particle(string word, string afterConsonant, string afterVowel)
        {
            if (string.IsNullOrEmpty(word)) return word;
            char last = word[word.Length - 1];
            bool batchim = last >= '가' && last <= '힣' && (last - '가') % 28 != 0;
            return word + (batchim ? afterConsonant : afterVowel);
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
