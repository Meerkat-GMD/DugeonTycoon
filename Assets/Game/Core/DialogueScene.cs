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

        private DiceRoll(int face, int modifier)
        {
            IsGod = true;
            Face = face;
            Modifier = modifier;
            Ability = "신탁";
        }

        /// <summary>The die the god rolls for an oracle; it stands at the middle of the stage, not over a hero.</summary>
        public static DiceRoll ForGod(int face, int modifier) => new DiceRoll(face, modifier);

        /// <summary>True for the god's die; <see cref="Role"/> means nothing then.</summary>
        public bool IsGod { get; }
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
            int difficulty, bool success, HeroRole? winner, HeroRole? markRole = null, string mark = null,
            Equipment item = null)
        {
            Kind = kind;
            Speaker = speaker;
            Text = text;
            Rolls = rolls ?? NoRolls;
            Difficulty = difficulty;
            Success = success;
            Winner = winner;
            MarkRole = markRole;
            Mark = mark;
            Item = item;
        }

        public static DialogueBeat Narration(string text) =>
            new DialogueBeat(BeatKind.Narration, null, text, null, 0, false, null);

        /// <summary>Narration that puts an item on show beside the stage, such as the armour being shared out.</summary>
        public static DialogueBeat Narration(string text, Equipment item) =>
            new DialogueBeat(BeatKind.Narration, null, text, null, 0, false, null, item: item);

        /// <summary>Narration that pins a short mark above one hero's portrait, such as "손해".</summary>
        public static DialogueBeat Narration(string text, HeroRole markRole, string mark) =>
            new DialogueBeat(BeatKind.Narration, null, text, null, 0, false, null, markRole, mark);

        public static DialogueBeat Line(HeroRole speaker, string text) =>
            new DialogueBeat(BeatKind.Line, speaker, text, null, 0, false, null);

        /// <summary>A line with a mark above the speaker's portrait, such as "원함" or "패스".</summary>
        public static DialogueBeat Line(HeroRole speaker, string text, string mark) =>
            new DialogueBeat(BeatKind.Line, speaker, text, null, 0, false, null, speaker, mark);

        /// <summary>A roll against a difficulty, such as an adversity.</summary>
        public static DialogueBeat Check(string title, DiceRoll roll, int difficulty, bool success) =>
            new DialogueBeat(BeatKind.Roll, null, title, new[] { roll }, difficulty, success, null);

        /// <summary>Several heroes roll and the highest total wins, such as persuasion at a fork.</summary>
        public static DialogueBeat Contest(string title, IReadOnlyList<DiceRoll> rolls, HeroRole winner) =>
            new DialogueBeat(BeatKind.Roll, null, title, rolls, 0, false, winner);

        /// <summary>
        /// Persuasion with the god taking part: <paramref name="rolls"/> holds the heroes' dice and the god's die
        /// (<see cref="DiceRoll.IsGod"/>). When the god wins, <see cref="Winner"/> is null and <see cref="GodWon"/> is true.
        /// </summary>
        public static DialogueBeat OracleContest(string title, IReadOnlyList<DiceRoll> rolls, HeroRole? winner) =>
            new DialogueBeat(BeatKind.Roll, null, title, rolls, 0, false, winner) { GodWon = winner == null };

        /// <summary>A contest whose highest face is shared, so the tied heroes roll again.</summary>
        public static DialogueBeat Tie(string title, IReadOnlyList<DiceRoll> rolls) =>
            new DialogueBeat(BeatKind.Roll, null, title, rolls, 0, false, null);

        public BeatKind Kind { get; }
        public HeroRole? Speaker { get; }
        /// <summary>The spoken or narrated text; for a roll, the name of the check.</summary>
        public string Text { get; }
        public IReadOnlyList<DiceRoll> Rolls { get; }
        /// <summary>The number to reach for a check; 0 for a contest.</summary>
        public int Difficulty { get; }
        /// <summary>Whether a check succeeded. Unused for a contest.</summary>
        public bool Success { get; }
        /// <summary>Whose opinion a contest settled on. Null for a check or a tied round.</summary>
        public HeroRole? Winner { get; }
        /// <summary>The hero whose portrait gets <see cref="Mark"/>, or null.</summary>
        public HeroRole? MarkRole { get; }
        /// <summary>A short tag above a portrait, such as "원함", "패스" or "손해".</summary>
        public string Mark { get; }
        /// <summary>An item to show beside the stage from this beat on, or null.</summary>
        public Equipment Item { get; }
        /// <summary>The god's die won an oracle contest.</summary>
        public bool GodWon { get; private set; }
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

        public const string OracleQuestion = "신탁 — 어느 길을 계시하시겠습니까?";
        public const string OracleSkip = "넘어간다";

        public static string OracleTitle(int bonus) => $"설득 판정 — 용사 d20 + 매력 · 신 d20 + {bonus}";

        /// <summary>A hero's answer to the oracle, by their first personality tag.</summary>
        public static string OracleReaction(PersonalityTag tag, bool agrees)
        {
            switch (tag)
            {
                case PersonalityTag.Reckless: return agrees ? "봐, 신도 내 편이라고!" : "신이라도 내 감은 못 이기지!";
                case PersonalityTag.Cautious: return agrees ? "계시예요… 안심하고 따를게요." : "정말… 그쪽이 맞을까요?";
                default: return agrees ? "신께서도 같은 생각이시군." : "흠, 계시라니. 그래도 내 생각은 달라.";
            }
        }

        public const string LootOpening = "전투가 끝났다. 용사들이 전리품을 나눈다.";
        public const string LootRollTitle = "전리품 주사위 — d20";
        public const string LootRerollTitle = "다시 굴림 — d20";
        public const string Wants = "원함";
        public const string Passes = "패스";
        public const string Worse = "손해";

        /// <summary>A hero's local answer when the party shares out armour.</summary>
        public static string LootWish(HeroRole role, bool wants)
        {
            switch (role)
            {
                case HeroRole.Tank: return wants ? "이 갑옷이면 앞에서 더 오래 버틴다. 내가 입겠다." : "난 지금 것으로 충분하다.";
                case HeroRole.Healer: return wants ? "저도 하나쯤은… 입어 보고 싶어요." : "무거워서 못 움직여요. 사양할게요.";
                case HeroRole.MeleeDamage: return wants ? "나도 맞으면서 싸우거든? 그거 내 거야." : "무거운 건 질색이야. 패스.";
                default: return wants ? "가벼워 보이는데, 나한테 줘." : "난 됐어. 활 쏘는 데 방해돼.";
            }
        }

        /// <summary>What the hero who gets the armour says.</summary>
        public static string LootTaken(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return "고맙다. 이걸로 모두를 지키지.";
                case HeroRole.Healer: return "소중히 입을게요.";
                case HeroRole.MeleeDamage: return "역시 내 운이지!";
                default: return "어때, 잘 어울리지?";
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
