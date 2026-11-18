using System;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>How much a hero trusts the god. Starts at <see cref="Steady"/> and lasts the whole expedition.</summary>
    public enum Belief { Shaken, Steady, Firm }

    /// <summary>The GM's answer to a prayer.</summary>
    public enum PrayerAnswer { HealingMiracle, RestMiracle, Ignore }

    /// <summary>What a hero (or the AI for them) says when asked whether to pray. Also the shape of an AI proposal.</summary>
    public sealed class PrayerProposal
    {
        public PrayerProposal(bool prays, string line)
        {
            Prays = prays;
            Line = line;
        }

        public bool Prays { get; }
        /// <summary>The prayer itself. Ignored on a refusal, which always uses the fixed line.</summary>
        public string Line { get; }
    }

    /// <summary>A desperate hero the party is waiting on. The dungeon is frozen until it is settled.</summary>
    public sealed class PrayerRequest
    {
        internal PrayerRequest(int token, HeroRole role, PrayerProposal local, bool grumbling)
        {
            Token = token;
            Role = role;
            Local = local;
            Grumbling = grumbling;
        }

        public int Token { get; }
        public HeroRole Role { get; }
        /// <summary>The hero is too worn out to pray: they refuse whatever the AI says.</summary>
        public bool Grumbling { get; }
        /// <summary>What the hero does without an AI response.</summary>
        public PrayerProposal Local { get; }
        /// <summary>The hero chose to pray and now waits for the GM's answer.</summary>
        public bool Praying { get; internal set; }
        /// <summary>The prayer once the hero prays, or the refusal once they decline.</summary>
        public string Line { get; internal set; } = "";
    }

    /// <summary>How the latest prayer ended, for the prayer scene.</summary>
    public sealed class PrayerOutcome
    {
        internal PrayerOutcome(HeroRole role, PrayerAnswer answer, string narration, string reaction, Belief belief)
        {
            Role = role;
            Answer = answer;
            Narration = narration;
            Reaction = reaction;
            Belief = belief;
        }

        public HeroRole Role { get; }
        public PrayerAnswer Answer { get; }
        public bool Answered => Answer != PrayerAnswer.Ignore;
        public string Narration { get; }
        public string Reaction { get; }
        public Belief Belief { get; }
    }

    /// <summary>
    /// Prayer: a hero close to death asks the god for help, at most once a stage. Core spots the despair at the
    /// end of a tick and freezes the dungeon; the hero (the AI, else a local rule) prays or refuses; the GM answers
    /// with a miracle or looks away, which leaves the hero's belief firm or shaken for the rest of the expedition.
    /// </summary>
    public sealed partial class DungeonRun
    {
        /// <summary>Faith a miracle costs, the same as the matching help. Balance value.</summary>
        public const int MiracleCost = BlessCost;
        /// <summary>A hero at or below this percent of their maximum health may despair. Balance value.</summary>
        public const int DesperateHpPercent = 25;
        /// <summary>The party is crumbling at or below this percent of its total maximum health. Balance value.</summary>
        public const int CrumblingPartyPercent = 40;
        /// <summary>Fatigue steps the rest miracle takes away. Balance value.</summary>
        public const int RestMiracleRelief = 3 * FatigueRelief;
        /// <summary>What a shaken hero adds to their persuasion roll against an oracle. Balance value.</summary>
        public const int ShakenBonus = 3;

        private enum PrayerHold { None, UntilCombatEnds, UntilNextFork }

        private readonly PrayerHold[] prayerHolds = new PrayerHold[4];
        private int nextPrayerToken;

        /// <summary>A hero waiting to pray or for the GM's answer, or null. The dungeon is frozen meanwhile.</summary>
        public PrayerRequest PendingPrayer { get; private set; }
        /// <summary>A prayer reached the GM this stage; there is no other until the next one.</summary>
        public bool PrayedThisStage { get; private set; }
        /// <summary>The latest answered or ignored prayer, or null.</summary>
        public PrayerOutcome LastPrayer { get; private set; }

        public static string BeliefName(Belief belief)
        {
            switch (belief)
            {
                case Belief.Firm: return "굳건";
                case Belief.Steady: return "보통";
                case Belief.Shaken: return "흔들림";
                default: throw new ArgumentOutOfRangeException(nameof(belief));
            }
        }

        /// <summary>
        /// The hero is close to death and the party is crumbling: an ally is down, the party's health is low, the
        /// party is exhausted or the boss is fighting. Ignores the once-a-stage limit and faith.
        /// </summary>
        public bool IsDesperate(HeroRole role)
        {
            if (!InDungeon) return false;
            var hero = party[(int)role];
            if (hero.Hp == 0 || hero.Hp * 100 > hero.MaxHp * DesperateHpPercent) return false;
            bool allyDown = party.Any(h => h.Hp == 0);
            bool partyLow = party.Sum(h => h.Hp) * 100 <= party.Sum(h => h.MaxHp) * CrumblingPartyPercent;
            return allyDown || partyLow || Fatigue == FatigueStage.Exhausted || FightingBoss;
        }

        /// <summary>The GM can give this answer to the prayer waiting on them now.</summary>
        public bool CanAnswerPrayer(PrayerAnswer answer)
        {
            var prayer = PendingPrayer;
            if (prayer == null || !prayer.Praying) return false;
            switch (answer)
            {
                case PrayerAnswer.HealingMiracle: return Faith >= MiracleCost && party[(int)prayer.Role].Hp > 0;
                case PrayerAnswer.RestMiracle: return Faith >= MiracleCost && FatigueSteps > 0;
                case PrayerAnswer.Ignore: return true;
                default: return false;
            }
        }

        /// <summary>
        /// Settles whether the desperate hero prays. A valid proposal (from the AI) replaces the local choice; a
        /// prayer without words falls back to it. A refusal costs nothing, lets the dungeon move on and spares that
        /// hero further asking until the fight ends (or, outside a fight, until the next fork). A prayer uses up the
        /// stage's prayer and waits for <see cref="AnswerPrayer"/>. Null for a stale token.
        /// </summary>
        public PrayerRequest HearPrayer(int token, PrayerProposal proposal = null)
        {
            var prayer = PendingPrayer;
            if (prayer == null || token != prayer.Token || prayer.Praying) return null;
            var hero = party[(int)prayer.Role];
            var choice = proposal != null && (!proposal.Prays || !string.IsNullOrWhiteSpace(proposal.Line)) ? proposal : prayer.Local;
            // The trait and the fatigue decide, not the AI: a skeptic or a grumbling hero never prays.
            if (hero.Personality == PersonalityTrait.Skeptic || prayer.Grumbling) choice = prayer.Local;
            if (!choice.Prays)
            {
                prayer.Line = prayer.Grumbling && hero.Personality != PersonalityTrait.Skeptic
                    ? DialogueLines.GrumbleRefusal(hero.Voice)
                    : DialogueLines.PrayerRefusal(hero.Personality);
                hero.LastLine = prayer.Line;
                prayerHolds[(int)hero.Role] = Phase == RunPhase.Combat ? PrayerHold.UntilCombatEnds : PrayerHold.UntilNextFork;
                log.Add($"기도 거절 • {hero.Name} “{prayer.Line}”");
                PendingPrayer = null;
                return prayer;
            }
            prayer.Praying = true;
            prayer.Line = choice.Line;
            hero.LastLine = choice.Line;
            PrayedThisStage = true;
            log.Add($"기도 • {hero.Name} “{choice.Line}”");
            return prayer;
        }

        /// <summary>
        /// The GM answers the prayer. A miracle heals the hero to full or rests the party and makes the hero's belief
        /// firm; looking away costs nothing and shakes it. Either way the hero reacts and the dungeon moves on.
        /// </summary>
        public bool AnswerPrayer(int token, PrayerAnswer answer)
        {
            var prayer = PendingPrayer;
            if (prayer == null || token != prayer.Token || !CanAnswerPrayer(answer)) return false;
            var hero = party[(int)prayer.Role];
            string narration;
            switch (answer)
            {
                case PrayerAnswer.HealingMiracle:
                    Faith -= MiracleCost;
                    hero.Hp = hero.MaxHp;
                    hero.Critical = false;
                    narration = $"치유의 기적 — {hero.Name}의 상처가 모두 아물었다.";
                    log.Add($"치유의 기적 • {hero.Name} 체력 {hero.Hp} / {hero.MaxHp} / 신앙 -{MiracleCost}");
                    break;
                case PrayerAnswer.RestMiracle:
                    Faith -= MiracleCost;
                    int before = FatigueSteps;
                    ReduceFatigue(RestMiracleRelief);
                    narration = "안식의 기적 — 파티의 피로가 씻겨 나갔다.";
                    log.Add($"안식의 기적 • 파티 피로 -{before - FatigueSteps} / 신앙 -{MiracleCost}");
                    break;
                default:
                    narration = "신은 대답하지 않았다.";
                    log.Add($"외면 • 신이 {hero.Name}의 기도에 대답하지 않았다.");
                    break;
            }
            bool answered = answer != PrayerAnswer.Ignore;
            hero.Belief = answered ? Belief.Firm : Belief.Shaken;
            string reaction = answered ? DialogueLines.PrayerAnswered(hero.Voice) : DialogueLines.PrayerIgnored(hero.Voice);
            hero.LastLine = reaction;
            log.Add($"믿음 • {hero.Name} {BeliefName(hero.Belief)} “{reaction}”");
            LastPrayer = new PrayerOutcome(hero.Role, answer, narration, reaction, hero.Belief);
            PendingPrayer = null;
            return true;
        }

        /// <summary>
        /// Run at the end of a tick: the most hurt desperate hero who has not just refused is asked to pray, if the
        /// stage's prayer is unused and the GM could pay for a miracle at all.
        /// </summary>
        private void CheckDesperation()
        {
            if (!InDungeon || PendingPrayer != null || PrayedThisStage || Faith < MiracleCost ||
                PendingAdversity || PendingFork != null || PendingLoot != null) return;
            HeroState chosen = null;
            foreach (var role in MarchOrder)
            {
                var hero = party[(int)role];
                if (prayerHolds[(int)role] != PrayerHold.None || !IsDesperate(role)) continue;
                if (chosen == null || hero.Hp * chosen.MaxHp < chosen.Hp * hero.MaxHp) chosen = hero;
            }
            if (chosen == null) return;
            log.Add($"절망 • {chosen.Name} 체력 {chosen.Hp} / {chosen.MaxHp} — 신에게 기도할지 망설인다.");
            bool grumbling = RollGrumblers(new[] { chosen.Role }).Count > 0;
            bool prays = chosen.Personality != PersonalityTrait.Skeptic && !grumbling;
            var local = new PrayerProposal(prays, DialogueLines.Prayer(chosen.Voice));
            PendingPrayer = new PrayerRequest(++nextPrayerToken, chosen.Role, local, grumbling);
        }

        /// <summary>Heroes who refused to pray may be asked again once their hold is over.</summary>
        private void ReleasePrayerHolds(bool combatEnded)
        {
            var released = combatEnded ? PrayerHold.UntilCombatEnds : PrayerHold.UntilNextFork;
            for (int i = 0; i < prayerHolds.Length; i++)
                if (prayerHolds[i] == released) prayerHolds[i] = PrayerHold.None;
        }

        /// <summary>A new stage brings a new prayer and forgets every refusal.</summary>
        private void ResetPrayer()
        {
            PendingPrayer = null;
            PrayedThisStage = false;
            Array.Clear(prayerHolds, 0, prayerHolds.Length);
        }
    }
}
