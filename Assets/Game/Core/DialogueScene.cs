using System.Collections.Generic;
using System.Linq;

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
        public const string ForkOpening = "갈림길";
        public const string BacktrackOpening = "막다른 길";
        public const string RockfallOpening = "낙석 역경! 천장이 무너져 내린다.";
        public const string PersuasionTitle = "설득 판정 — d20 + 매력";

        public static string Opening(ForkDiscussion fork) => fork.Backtracking ? BacktrackOpening : ForkOpening;

        /// <summary>
        /// A hero's short battle line for a decided tactic: the ally to keep alive comes first, then the monster to
        /// focus. <paramref name="foe"/> and <paramref name="ward"/> are names, or null when the tactic has none.
        /// </summary>
        public static string Tactic(HeroRole role, string foe, bool foeIsBoss, string ward, bool wardIsSelf)
        {
            if (ward != null)
            {
                switch (role)
                {
                    case HeroRole.Healer: return wardIsSelf ? "제 상처부터 돌볼게요!" : $"{ward}부터 치료할게요!";
                    case HeroRole.Tank: return wardIsSelf ? "아직 버틸 수 있다!" : $"{ward}, 내 뒤로 와!";
                    default: return wardIsSelf ? "나부터 추슬러야겠어." : $"{ward}, 내가 엄호할게!";
                }
            }
            switch (role)
            {
                case HeroRole.Tank: return foeIsBoss ? $"{foe}, 내가 상대다!" : $"{foe}, 이쪽을 봐라!";
                case HeroRole.Healer: return $"{foe}, 물러나요!";
                case HeroRole.MeleeDamage: return foeIsBoss ? $"{foe}의 목은 내 거다!" : $"{foe}부터 베어 주지!";
                default: return foeIsBoss ? $"{foe}의 급소를 노린다." : $"{foe}, 조준했어.";
            }
        }

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

        public const string OracleQuestion = "신탁";
        public const string OracleSkip = "넘어간다";

        /// <summary>A hero's answer to the oracle, by the voice of their personality trait.</summary>
        public static string OracleReaction(Voice voice, bool agrees)
        {
            switch (voice)
            {
                case Voice.Bold: return agrees ? "신탁이다! 앞장은 내가 서지." : "그쪽이라고? 좋아, 내가 앞장서지!";
                case Voice.Careful: return agrees ? "계시예요… 안심하고 따를게요." : "정말… 그쪽이 맞을까요?";
                default: return agrees ? "계시가 가리키는 길로 가세." : "뜻밖이군. 그 길로 가 보세.";
            }
        }

        /// <summary>The mark above a hero who grumbles through a fork debate.</summary>
        public const string Grumbles = "투덜";

        /// <summary>A worn-out hero's sour argument for the way they still pick as usual. Every label ends in "길".</summary>
        public static string GrumbleOpinion(Voice voice, string way, bool exhausted)
        {
            switch (voice)
            {
                case Voice.Bold: return exhausted ? $"아 진짜, 언제까지 걸을 건데? {way}로 가든가." : $"또 갈림길이야? 짜증 나네. {way}로 가.";
                case Voice.Careful: return exhausted ? $"…더는 못 걷겠어요. {way}… 아무 데나요." : $"이렇게 헤매다 다 쓰러지겠어요. 그냥 {way}로 가요.";
                case Voice.Curious: return exhausted ? $"구경이고 뭐고 다 귀찮아. {way}로 가." : $"길 고르는 것도 지겹다. {way}이면 되겠지.";
                default: return exhausted ? $"발이 안 떨어져. {way}든 어디든 빨리 정해." : $"또 헤매는 거야? {way}로 가자, 이제.";
            }
        }

        /// <summary>A grumbling hero's sour answer to the oracle, whether or not it points their way.</summary>
        public static string OracleGrumble(Voice voice, bool agrees)
        {
            switch (voice)
            {
                case Voice.Bold: return agrees ? "그래, 그쪽으로 가지 뭐. 빨리 끝내자." : "이제야 알려 주냐? 가자고.";
                case Voice.Careful: return agrees ? "계시든 뭐든… 쉴 수만 있으면 좋겠어요." : "그쪽이요? …천천히 좀 가요.";
                default: return agrees ? "이제야 알려 주시나. 진작 좀 해 주지." : "헤매는 동안엔 조용하더니, 이제 와서?";
            }
        }

        /// <summary>A grumbling hero's cry as a fight starts.</summary>
        public static string GrumbleBattleCry(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "하필 지금이야? 귀찮게 하네!";
                case Voice.Careful: return "또 싸워요…? 다리가 후들거려요.";
                case Voice.Curious: return "적이라니, 하나도 안 반갑다.";
                default: return "쉬지도 못하고 또 싸움이냐.";
            }
        }

        /// <summary>What a grumbling hero says when a new battle plan comes. They still follow it.</summary>
        public static string GrumbleTactic(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "알았어, 한다고. 빨리 끝내자.";
                case Voice.Careful: return "하라는 대로 할게요… 힘들지만요.";
                case Voice.Curious: return "작전이고 뭐고, 그냥 끝내자.";
                default: return "으휴, 이번엔 또 뭐야.";
            }
        }

        /// <summary>A grumbling hero's declaration against an adversity.</summary>
        public static string GrumbleDeclaration(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "돌까지 떨어지냐? 내가 막는다, 막아!";
                case Voice.Careful: return "왜 하필 지금… 제가 할게요, 해 볼게요.";
                case Voice.Curious: return "천장까지 무너지네. 오늘 운 최악이다.";
                default: return "정말 끝이 없구나. 내가 해 볼게.";
            }
        }

        /// <summary>What a grumbling hero says once the adversity is over.</summary>
        public static string GrumbleReaction(Voice voice, bool success)
        {
            switch (voice)
            {
                case Voice.Bold: return success ? "됐지? 이제 좀 쉬자고." : "젠장… 오늘은 되는 게 없어.";
                case Voice.Careful: return success ? "겨우 넘겼어요… 더는 무리예요." : "아파요… 이럴 줄 알았어요.";
                default: return success ? "넘기긴 했는데, 하나도 안 기뻐." : "역시나. 지친 날엔 다 꼬여.";
            }
        }

        /// <summary>A grumbling hero turns down every piece of armour in the scene.</summary>
        public static string GrumblePass(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "갑옷 갈아입을 기운도 없어. 패스.";
                case Voice.Careful: return "무거운 건 더는 못 들어요… 다른 분 주세요.";
                case Voice.Curious: return "지금은 뭘 봐도 감흥이 없어. 아무나 가져가.";
                default: return "귀찮아. 아무나 가져가.";
            }
        }

        /// <summary>A grumbling hero turns down the prayer, blaming the god for the wandering.</summary>
        public static string GrumbleRefusal(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "신이고 뭐고, 이렇게 헤매게 둔 게 누군데.";
                case Voice.Careful: return "…이렇게 지쳤는데, 기도할 힘도 없어요.";
                default: return "기도해 봤자 길이 줄어들진 않잖아.";
            }
        }

        public const string LootRollTitle = "전리품 주사위 — d20";
        public const string LootRerollTitle = "다시 굴림 — d20";
        public const string Wants = "원함";
        public const string Passes = "패스";
        public const string Worse = "손해";

        /// <summary>A greedy hero bids whatever the armour is worth to them.</summary>
        public static string GreedyBid(bool better) =>
            better ? "좋은 건 내 거야. 나도 입찰!" : "일단 나도 입찰! 나쁠 게 뭐 있어?";

        /// <summary>A generous hero steps back for an ally who gains more from the armour.</summary>
        public static string GenerousPass(string ally) => $"이번 건 {HeroTraits.Subject(ally)} 입는 게 더 낫겠어. 양보할게.";

        /// <summary>An appraiser bids only when the armour is clearly worth it.</summary>
        public static string AppraiserWish(int gain, bool wants) => wants
            ? $"위력이 {gain}이나 오르네. 이건 입을 가치가 있어."
            : gain > 0 ? $"고작 {gain} 오르는 건 갈아입을 가치가 없어." : "지금 것보다 못해. 감정 결과, 패스.";

        /// <summary>A collector cannot let rare armour go.</summary>
        public static string CollectorBid(string item) => $"희귀한 {HeroTraits.Particle(item, "을", "를")} 그냥 보낼 순 없지. 내가 입찰!";

        /// <summary>A hero who looks out for the weak steps back for an ally with less armour.</summary>
        public static string UnderdogPass(string ally) => $"{HeroTraits.Subject(ally)} 나보다 방어가 약해. 이건 {ally} 몫이야.";

        /// <summary>
        /// A peacemaker sums up what each candidate gains, then calls for the roll. <paramref name="gains"/> are names
        /// with their gain in power.
        /// </summary>
        public static string PeacemakerCall(IReadOnlyList<(string Name, int Gain)> gains) =>
            string.Join(", ", gains.Select(g => $"{g.Name} +{g.Gain}")) + ". 다들 필요한 건 알겠어. 판정대로 하자.";

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

        /// <summary>The narrator as a desperate hero kneels to pray.</summary>
        public static string PrayerOpening(string name) => $"{HeroTraits.Subject(name)} 무릎을 꿇고 하늘을 올려다본다.";

        /// <summary>A hero's local prayer, by the voice of their personality trait.</summary>
        public static string Prayer(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "…젠장. 신이든 뭐든 좋아. 듣고 있다면 대답해!";
                case Voice.Careful: return "신이시여… 더는 버틸 수 없습니다. 동료들만은 지켜 주소서!";
                default: return "숨이 다 바닥났어요… 신이시여, 저희를 잊지 않으셨죠?";
            }
        }

        /// <summary>A cheerer the healer just pulled out of danger.</summary>
        public static string CheerOwnRescue(string healer) => $"{healer}, 그 회복 덕에 버텼어! 고마워!";

        /// <summary>A cheerer watching the healer pull an ally out of danger.</summary>
        public static string CheerRescue(string healer, string patient) => $"나이스 {healer}! {HeroTraits.Subject(patient)} 살았어!";

        /// <summary>A nagging hero after a telegraphed attack caught an ally: the floor for blasts, the line for beams and bolts.</summary>
        public static string Nag(string ally, bool ground) => ground
            ? $"{ally}, 바닥에 장판 뜬 거 못 봤어? 다음엔 바닥부터 봐!"
            : $"{ally}, 사선 위에 서 있으면 어떡해! 조준선부터 봐!";

        /// <summary>A devout hero praying for a desperate ally.</summary>
        public static string DevoutPrayer(string ally) =>
            $"신이시여, 저 말고 {HeroTraits.Particle(ally, "을", "를")} 굽어살펴 주소서. 제발 다시 일어서게 해 주세요.";

        /// <summary>A devout hero praying for themselves.</summary>
        public const string DevoutOwnPrayer = "신이시여, 끝까지 당신을 믿습니다. 부디 이 손을 잡아 주소서.";

        /// <summary>The narrator as a devout hero kneels for someone else.</summary>
        public static string PrayerOpeningFor(string name, string ally) =>
            $"{HeroTraits.Subject(name)} {ally} 곁에 무릎을 꿇고 하늘을 올려다본다.";

        /// <summary>What a desperate hero says instead of praying. A skeptic has their own words.</summary>
        public static string PrayerRefusal(PersonalityTrait personality)
        {
            if (personality == PersonalityTrait.Skeptic) return TraitCatalog.Of(PersonalityTrait.Skeptic).Line;
            switch (HeroTraits.VoiceOf(personality))
            {
                case Voice.Bold: return "기도는 무슨. 이 정도로 쓰러질 내가 아니야!";
                case Voice.Careful: return "아직… 아직 버틸 수 있어.";
                default: return "아니, 아직 해 볼 게 남았어!";
            }
        }

        /// <summary>What a hero says when the god answers their prayer.</summary>
        public static string PrayerAnswered(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "하, 진짜 대답하잖아? 좋아, 믿어 주지!";
                case Voice.Careful: return "느껴져요… 신께서 우리를 보고 계세요!";
                default: return "응답하셨어…! 이제 아무것도 두렵지 않아!";
            }
        }

        /// <summary>What a hero says when the god looks away.</summary>
        public static string PrayerIgnored(Voice voice)
        {
            switch (voice)
            {
                case Voice.Bold: return "역시 그렇지. 믿은 내가 바보였어.";
                case Voice.Careful: return "…대답이 없어. 신께서도 우리를 버리신 걸까.";
                default: return "아무도… 듣고 있지 않았구나.";
            }
        }

        /// <summary>The GM's three answers on the prayer scene, in button order.</summary>
        public static string PrayerAnswerName(PrayerAnswer answer)
        {
            switch (answer)
            {
                case PrayerAnswer.HealingMiracle: return "치유의 기적";
                case PrayerAnswer.RestMiracle: return "안식의 기적";
                default: return "외면한다";
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
