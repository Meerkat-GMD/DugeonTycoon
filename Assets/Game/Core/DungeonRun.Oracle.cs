using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// Settling a fork in two steps so the GM can step in between: the party first states its opinions
    /// (<see cref="HearFork"/>), then the GM may reveal a way (the oracle) and the fork is settled
    /// (<see cref="ResolveFork"/>). Calling ResolveFork alone does both with no oracle.
    /// </summary>
    public sealed partial class DungeonRun
    {
        /// <summary>Faith the oracle costs. Balance value.</summary>
        public const int OracleCost = 1;

        /// <summary>Tests pin the persuasion d20 faces. Null uses the run's dice.</summary>
        internal Func<int> ForkRollOverride { get; set; }
        /// <summary>Tests set the faith directly.</summary>
        internal void SetFaith(int faith) => Faith = faith;

        private int ForkRoll() => ForkRollOverride?.Invoke() ?? dice.Next(1, 21);

        /// <summary>The GM has the faith to reveal a way at the pending fork.</summary>
        public bool CanOracle => PendingFork != null && Faith >= OracleCost;

        /// <summary>
        /// Settles what each living hero argues for. Valid proposals (from the AI) replace a hero's local opinion;
        /// anything missing or invalid falls back to it. Sets <see cref="LastScene"/> to the opinions in march
        /// order. Hearing the same fork again returns the first answer. Null for a stale token.
        /// </summary>
        public IReadOnlyList<ForkOpinion> HearFork(int token, IReadOnlyList<ForkOpinion> proposals = null)
        {
            var fork = PendingFork;
            if (fork == null || token != fork.Token || Phase != RunPhase.Travel) return null;
            if (fork.Heard != null) return fork.Heard;
            var opinions = new List<ForkOpinion>();
            foreach (var local in fork.LocalOpinions)
            {
                if (party[(int)local.Role].Hp == 0) continue;
                var proposal = proposals?.FirstOrDefault(p => p != null && p.Role == local.Role);
                bool valid = proposal != null && proposal.Option >= 0 && proposal.Option < fork.Options.Count &&
                             !string.IsNullOrWhiteSpace(proposal.Line);
                var opinion = valid ? proposal : local;
                // Fatigue decides the tone, not the AI: a grumbler keeps the AI's way but always complains.
                if (valid && fork.Grumbles(local.Role))
                    opinion = new ForkOpinion(local.Role, proposal.Option, DialogueLines.GrumbleOpinion(
                        party[(int)local.Role].Voice, fork.Options[proposal.Option].Label, Fatigue == FatigueStage.Exhausted));
                opinions.Add(opinion);
                var hero = party[(int)opinion.Role];
                hero.LastLine = opinion.Line;
                log.Add($"{hero.Name} → {fork.Options[opinion.Option].Label} “{opinion.Line}”");
            }
            // The stage shows the debate front to back, the way the party marches.
            fork.Heard = opinions.OrderBy(o => Array.IndexOf(MarchOrder, o.Role)).ToList().AsReadOnly();
            LastScene = fork.Heard.Select(o => fork.Grumbles(o.Role)
                ? DialogueBeat.Line(o.Role, o.Line, DialogueLines.Grumbles)
                : DialogueBeat.Line(o.Role, o.Line)).ToList().AsReadOnly();
            return fork.Heard;
        }

        /// <summary>
        /// Settles the pending fork. <paramref name="oracle"/> is the option the GM reveals, or -1 to pass; it is
        /// ignored without the faith for it. A valid oracle settles the route immediately. Without one,
        /// only a divided party rolls for persuasion.
        /// <see cref="LastScene"/> holds the opinions too unless <see cref="HearFork"/> already showed them.
        /// </summary>
        public bool ResolveFork(int token, IReadOnlyList<ForkOpinion> proposals = null, int oracle = -1)
        {
            var fork = PendingFork;
            if (fork == null || token != fork.Token || Phase != RunPhase.Travel) return false;
            bool heardBefore = fork.Heard != null;
            var opinions = HearFork(token, proposals);
            if (opinions == null || opinions.Count == 0) return false;
            var scene = heardBefore ? new List<DialogueBeat>() : LastScene.ToList();

            string Way(int option) => fork.Options[option].Label;
            // Every label ends in "길", which takes 로.
            string Toward(int option) => Way(option) + "로";
            bool revealed = oracle >= 0 && oracle < fork.Options.Count && Faith >= OracleCost;
            int chosen;
            var summary = new StringBuilder();
            if (revealed)
            {
                chosen = oracle;
                Faith -= OracleCost;
                scene.Add(DialogueBeat.Narration($"신탁이 내려왔다! 하늘이 {HeroTraits.Particle(Way(oracle), "을", "를")} 비춘다."));
                log.Add($"신탁 • {Way(oracle)} / 신앙 -{OracleCost}");
                foreach (var opinion in opinions)
                {
                    var hero = party[(int)opinion.Role];
                    bool grumbles = fork.Grumbles(hero.Role);
                    bool agrees = opinion.Option == oracle;
                    string reaction = grumbles
                        ? DialogueLines.OracleGrumble(hero.Voice, agrees)
                        : DialogueLines.OracleReaction(hero.Voice, agrees || hero.Belief == Belief.Firm);
                    hero.LastLine = reaction;
                    scene.Add(DialogueBeat.Line(hero.Role, reaction));
                }
                summary.Append($"신탁대로 {Way(chosen)}");
                scene.Add(DialogueBeat.Narration($"파티가 신탁을 따라 {Toward(chosen)} 간다."));
            }
            else if (opinions.All(o => o.Option == opinions[0].Option))
            {
                chosen = opinions[0].Option;
                summary.Append($"의견 일치 → {Way(chosen)}");
                scene.Add(DialogueBeat.Narration($"의견이 모였다. {Toward(chosen)} 간다."));
            }
            else
            {
                string charisma = HeroTraits.Name(Ability.Charisma);
                var rolls = opinions.Select(opinion => (Opinion: opinion,
                    Die: new DiceRoll(opinion.Role, ForkRoll(),
                        party[(int)opinion.Role].Modifier(Ability.Charisma), charisma))).ToList();
                int top = rolls.Max(r => r.Die.Total);
                var leaders = rolls.Where(r => r.Die.Total == top).ToList();
                var shown = rolls.Select(r => r.Die).ToList();
                summary.Append("설득 d20 + 매력  ");
                summary.Append(string.Join("  ", rolls.Select(r =>
                    $"{party[(int)r.Die.Role].Name} [{r.Die.Face}]{(r.Die.Modifier >= 0 ? "+" : "")}{r.Die.Modifier}={r.Die.Total}")));

                int Supporters(int option) => opinions.Count(o => o.Option == option);
                var winner = leaders.OrderByDescending(r => Supporters(r.Opinion.Option))
                    .ThenBy(r => Array.IndexOf(MarchOrder, r.Opinion.Role)).First();
                chosen = winner.Opinion.Option;
                var speaker = party[(int)winner.Opinion.Role];
                summary.Append($"\n→ {speaker.Name}의 의견대로 {Way(chosen)}");

                scene.Add(DialogueBeat.Contest(DialogueLines.PersuasionTitle, shown, speaker.Role));
                if (leaders.Select(r => r.Opinion.Option).Distinct().Count() > 1)
                {
                    bool bySupport = leaders.Count(r => Supporters(r.Opinion.Option) == Supporters(chosen)) < leaders.Count;
                    scene.Add(DialogueBeat.Narration(bySupport
                        ? $"합계가 같다. 지지한 용사가 더 많은 {Toward(chosen)} 정한다."
                        : $"합계도, 지지한 용사 수도 같다. 대형 앞쪽 {speaker.Name}의 의견을 따른다."));
                }
                string closing = DialogueLines.Closing(speaker.Role);
                speaker.LastLine = closing;
                scene.Add(DialogueBeat.Line(speaker.Role, closing));
                scene.Add(DialogueBeat.Narration($"{speaker.Name}의 의견대로 {Toward(chosen)} 간다."));
                log.Add($"{speaker.Name} “{closing}”");
            }
            LastFork = summary.ToString();
            LastScene = scene.AsReadOnly();
            log.Add("결정 • " + LastFork.Replace('\n', ' '));
            ForkCount++;
            PendingFork = null;
            ReleasePrayerHolds(combatEnded: false);

            // Ways not taken are remembered so the party comes back to them only from a dead end; chests and springs
            // not taken are not raised again from afar.
            var taken = fork.Options[chosen];
            lastPassed.Clear();
            for (int i = 0; i < fork.Options.Count; i++)
            {
                if (i == chosen) continue;
                var option = fork.Options[i];
                if (option.Feature == ForkFeature.Way)
                {
                    var memory = skipped.FirstOrDefault(set => Touches(set, option.Frontier));
                    if (memory == null) skipped.Add(memory = new HashSet<TilePoint>(option.Frontier));
                    lastPassed.Add(memory);
                }
                else PassFeature(option);
            }
            if (taken.Feature == ForkFeature.Way)
            {
                ForgetSkipped(taken.Frontier);
                Head(new HashSet<TilePoint>(taken.Frontier));
            }
            else
            {
                // No way was chosen, so the ways passed over keep growing as the party walks to the spring or the boss
                // room and stay the same ways when it comes back to them.
                heading.Clear();
            }
            resumePassed = taken.Feature == ForkFeature.Spring && lastPassed.Count > 0;
            route.Clear();
            var path = Map.FindPath(LeaderTile, taken.Target, Known);
            if (path.Count >= 2)
            {
                if (taken.Feature == ForkFeature.Chest || taken.Feature == ForkFeature.Spring) FollowFeature(path);
                else FollowPath(path, taken.Feature == ForkFeature.Boss);
            }
            return true;
        }
    }
}
