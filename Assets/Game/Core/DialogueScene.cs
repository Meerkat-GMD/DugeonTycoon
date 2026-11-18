using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    public enum BeatKind
    {
        /// <summary>The narrator speaks; <see cref="DialogueBeat.Speaker"/> is null.</summary>
        Narration,
        /// <summary>A hero speaks a line.</summary>
        Line,
        /// <summary>One or more heroes roll d20 at once. <see cref="DialogueBeat.Text"/> names the check.</summary>
        Roll
    }

    /// <summary>One die already rolled by Core. The presentation only shows it.</summary>
    public sealed class DiceRoll
    {
        public DiceRoll(HeroRole role, int face, int modifier, string ability)
        {
            Role = role;
            Face = face;
            Modifier = modifier;
            Ability = ability;
        }

        public HeroRole Role { get; }
        public int Face { get; }
        public int Modifier { get; }
        /// <summary>The ability the modifier comes from, e.g. "매력".</summary>
        public string Ability { get; }
        public int Total => Face + Modifier;
    }

    /// <summary>
    /// One step of a fork debate or an adversity, in the order the screen shows them. The outcome is settled
    /// before the first beat is built, so replaying, skipping or speeding up the beats never changes it.
    /// </summary>
    public sealed class DialogueBeat
    {
        private static readonly IReadOnlyList<DiceRoll> NoRolls = new DiceRoll[0];

        private DialogueBeat(BeatKind kind, HeroRole? speaker, string text, IReadOnlyList<DiceRoll> rolls,
            int difficulty, bool success, HeroRole? winner)
        {
            Kind = kind;
            Speaker = speaker;
            Text = text;
            Rolls = rolls ?? NoRolls;
            Difficulty = difficulty;
            Success = success;
            Winner = winner;
        }

        public static DialogueBeat Narration(string text) =>
            new DialogueBeat(BeatKind.Narration, null, text, null, 0, false, null);

        public static DialogueBeat Line(HeroRole speaker, string text) =>
            new DialogueBeat(BeatKind.Line, speaker, text, null, 0, false, null);

        /// <summary>A roll against a difficulty, such as an adversity.</summary>
        public static DialogueBeat Check(string title, DiceRoll roll, int difficulty, bool success) =>
            new DialogueBeat(BeatKind.Roll, null, title, new[] { roll }, difficulty, success, null);

        /// <summary>Several heroes roll and the highest total wins, such as persuasion at a fork.</summary>
        public static DialogueBeat Contest(string title, IReadOnlyList<DiceRoll> rolls, HeroRole winner) =>
            new DialogueBeat(BeatKind.Roll, null, title, rolls, 0, false, winner);

        public BeatKind Kind { get; }
        public HeroRole? Speaker { get; }
        /// <summary>The spoken or narrated text; for a roll, the name of the check.</summary>
        public string Text { get; }
        public IReadOnlyList<DiceRoll> Rolls { get; }
        /// <summary>The number to reach for a check; 0 for a contest.</summary>
        public int Difficulty { get; }
        /// <summary>Whether a check succeeded. Unused for a contest.</summary>
        public bool Success { get; }
        /// <summary>Whose opinion a contest settled on. Null for a check.</summary>
        public HeroRole? Winner { get; }
    }

    /// <summary>Fixed narration and the local lines heroes say around a debate or an adversity.</summary>
    public static class DialogueLines
    {
        public const string ForkOpening = "갈림길이다. 용사들이 의견을 나눈다.";
        public const string BacktrackOpening = "막다른 길이다. 어디로 되돌아갈지 의논한다.";
        public const string RockfallOpening = "낙석 역경! 천장이 무너져 내린다.";
        public const string PersuasionTitle = "설득 판정 — d20 + 매력";

        public static string Opening(ForkDiscussion fork) => fork.Backtracking ? BacktrackOpening : ForkOpening;

        public static string TrialTitle(string ability) => "역경 판정 — d20 + " + ability;

        /// <summary>What the hero whose opinion won says before the party sets off.</summary>
        public static string Closing(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return "좋다, 내가 앞장서지.";
                case HeroRole.Healer: return "고마워요. 조심해서 가요.";
                case HeroRole.MeleeDamage: return "결정 났다. 따라와!";
                default: return "내 말 믿어. 후회 안 할 거야.";
            }
        }

        /// <summary>What the hero who answered an adversity says once it is over.</summary>
        public static string Reaction(HeroRole role, bool success)
        {
            switch (role)
            {
                case HeroRole.Tank: return success ? "다들 무사하지?" : "크윽… 괜찮다, 계속 간다.";
                case HeroRole.Healer: return success ? "휴, 다친 사람 없죠?" : "아야… 금방 치료할게요.";
                case HeroRole.MeleeDamage: return success ? "하! 이 정도쯤이야." : "큭, 방심했다…";
                default: return success ? "봤지? 타이밍이 전부야." : "아, 늦었어…!";
            }
        }
    }
}
