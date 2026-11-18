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
        /// <summary>What the god adds to its d20 in the persuasion contest. Balance value.</summary>
        public const int OracleBonus = 5;

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
                opinions.Add(opinion);
                var hero = party[(int)opinion.Role];
                hero.LastLine = opinion.Line;
                log.Add($"{hero.Name} → {fork.Options[opinion.Option].Label} “{opinion.Line}”");
            }
            // The stage shows the debate front to back, the way the party marches.
            fork.Heard = opinions.OrderBy(o => Array.IndexOf(MarchOrder, o.Role)).ToList().AsReadOnly();
            LastScene = fork.Heard.Select(o => DialogueBeat.Line(o.Role, o.Line)).ToList().AsReadOnly();
            return fork.Heard;
        }

        /// <summary>
        /// Settles the pending fork. <paramref name="oracle"/> is the option the GM reveals, or -1 to pass; it is
        /// ignored without the faith for it. A unanimous party that the oracle agrees with (or that gets no
        /// oracle) goes at once. Otherwise every living hero rolls d20 + Charisma and, with an oracle, the god
        /// rolls d20 + <see cref="OracleBonus"/>: the highest total wins, the god wins any tie it is part of,
        /// other ties go to the way more heroes backed, then to the hero further forward.
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
            if (revealed)
            {
                Faith -= OracleCost;
                scene.Add(DialogueBeat.Narration($"신탁 — 하늘이 {HeroTraits.Particle(Way(oracle), "을", "를")} 비춘다."));
                log.Add($"신탁 • {Way(oracle)} / 신앙 -{OracleCost}");
                foreach (var opinion in opinions)
                {
                    var hero = party[(int)opinion.Role];
                    bool accepts = opinion.Option == oracle || hero.Belief == Belief.Firm;
                    string reaction = DialogueLines.OracleReaction(hero.Tags[0], accepts);
                    hero.LastLine = reaction;
                    scene.Add(DialogueBeat.Line(hero.Role, reaction));
                }
            }
            else oracle = -1;

            // A hero whose prayer the god answered does not argue with it: they drop their own way and back the
            // oracle without rolling. A hero the god ignored holds their ground harder against it.
            bool Yields(ForkOpinion o) => oracle >= 0 && o.Option != oracle && party[(int)o.Role].Belief == Belief.Firm;
            bool Defies(ForkOpinion o) => oracle >= 0 && o.Option != oracle && party[(int)o.Role].Belief == Belief.Shaken;
            int Backs(ForkOpinion o) => Yields(o) ? oracle : o.Option;
            var yielding = opinions.Where(Yields).ToList();
            if (yielding.Count > 0)
            {
                string names = string.Join(", ", yielding.Select(o => party[(int)o.Role].Name));
                scene.Add(DialogueBeat.Narration($"{HeroTraits.Subject(names)} 굳건한 믿음으로 계시를 따른다."));
                log.Add($"믿음 • {names} 굳건 — 신탁을 따른다");
            }

            int chosen;
            var summary = new StringBuilder();
            var ways = opinions.Select(Backs).Distinct().ToList();
            if (ways.Count == 1 && (oracle < 0 || ways[0] == oracle))
            {
                chosen = ways[0];
                summary.Append(oracle < 0 ? $"의견 일치 → {Way(chosen)}" : $"의견 일치, 신탁도 같음 → {Way(chosen)}");
                scene.Add(DialogueBeat.Narration(oracle < 0
                    ? $"의견이 모였다. {Toward(chosen)} 간다."
                    : $"의견도 계시도 {HeroTraits.Particle(Way(chosen), "이다", "다")}. {Toward(chosen)} 간다."));
            }
            else
            {
                string charisma = HeroTraits.Name(Ability.Charisma);
                var rolls = opinions.Where(o => !Yields(o)).Select(opinion => (Opinion: opinion,
                    Die: new DiceRoll(opinion.Role, ForkRoll(),
                        party[(int)opinion.Role].Modifier(Ability.Charisma) + (Defies(opinion) ? ShakenBonus : 0),
                        Defies(opinion) ? charisma + "·흔들림" : charisma))).ToList();
                var defiant = rolls.Where(r => Defies(r.Opinion)).Select(r => party[(int)r.Opinion.Role].Name).ToList();
                if (defiant.Count > 0)
                    scene.Add(DialogueBeat.Narration($"흔들리는 믿음 — {HeroTraits.Subject(string.Join(", ", defiant))} 계시에 맞선다. 설득 굴림 +{ShakenBonus}"));
                var god = oracle >= 0 ? DiceRoll.ForGod(ForkRoll(), OracleBonus) : null;
                int top = Math.Max(rolls.Max(r => r.Die.Total), god?.Total ?? int.MinValue);
                var leaders = rolls.Where(r => r.Die.Total == top).ToList();
                bool godWins = god != null && god.Total == top;
                var shown = rolls.Select(r => r.Die).ToList();
                if (god != null) shown.Add(god);

                summary.Append(god != null ? $"설득 d20 + 매력 · 신 d20 + {OracleBonus}  " : "설득 d20 + 매력  ");
                summary.Append(string.Join("  ", rolls.Select(r =>
                    $"{party[(int)r.Die.Role].Name} [{r.Die.Face}]{(r.Die.Modifier >= 0 ? "+" : "")}{r.Die.Modifier}={r.Die.Total}" +
                    (Defies(r.Opinion) ? $"(흔들림 +{ShakenBonus})" : ""))));
                if (yielding.Count > 0)
                    summary.Append("  " + string.Join(", ", yielding.Select(o => party[(int)o.Role].Name)) + " 굳건 — 신탁 지지");
                if (god != null) summary.Append($"  신 [{god.Face}]+{god.Modifier}={god.Total}");

                if (godWins)
                {
                    chosen = oracle;
                    summary.Append($"\n→ 신탁대로 {Way(chosen)}");
                    scene.Add(DialogueBeat.OracleContest(DialogueLines.OracleTitle(OracleBonus), shown, null));
                    if (leaders.Count > 0) scene.Add(DialogueBeat.Narration("신과 같은 합계 — 신탁을 따른다."));
                    scene.Add(DialogueBeat.Narration($"용사들이 계시를 받아들인다. {Toward(chosen)} 간다."));
                }
                else
                {
                    int Supporters(int option) => opinions.Count(o => Backs(o) == option);
                    var winner = leaders.OrderByDescending(r => Supporters(r.Opinion.Option))
                        .ThenBy(r => Array.IndexOf(MarchOrder, r.Opinion.Role)).First();
                    chosen = winner.Opinion.Option;
                    var speaker = party[(int)winner.Opinion.Role];
                    summary.Append($"\n→ {speaker.Name}의 의견대로 {Way(chosen)}");

                    scene.Add(god != null
                        ? DialogueBeat.OracleContest(DialogueLines.OracleTitle(OracleBonus), shown, speaker.Role)
                        : DialogueBeat.Contest(DialogueLines.PersuasionTitle, shown, speaker.Role));
                    if (leaders.Select(r => r.Opinion.Option).Distinct().Count() > 1)
                    {
                        bool bySupport = leaders.Count(r => Supporters(r.Opinion.Option) == Supporters(chosen)) < leaders.Count;
                        scene.Add(DialogueBeat.Narration(bySupport
                            ? $"합계가 같다. 지지한 용사가 더 많은 {Toward(chosen)} 정한다."
                            : $"합계도, 지지한 용사 수도 같다. 대형 앞쪽 {speaker.Name}의 의견을 따른다."));
                    }
                    if (oracle >= 0 && chosen != oracle)
                        scene.Add(DialogueBeat.Narration($"{speaker.Name}의 반발이 계시를 이겼다."));
                    string closing = DialogueLines.Closing(speaker.Role);
                    speaker.LastLine = closing;
                    scene.Add(DialogueBeat.Line(speaker.Role, closing));
                    scene.Add(DialogueBeat.Narration($"{speaker.Name}의 의견대로 {Toward(chosen)} 간다."));
                    log.Add($"{speaker.Name} “{closing}”");
                }
            }
            LastFork = summary.ToString();
            LastScene = scene.AsReadOnly();
            log.Add("결정 • " + LastFork.Replace('\n', ' '));
            ForkCount++;
            PendingFork = null;
            ReleasePrayerHolds(combatEnded: false);

            // Ways not taken are remembered so the party comes back to them only from a dead end.
            for (int i = 0; i < fork.Options.Count; i++)
                if (i != chosen && !MatchesSkipped(fork.Options[i].Frontier)) skipped.Add(new HashSet<TilePoint>(fork.Options[i].Frontier));
            ForgetSkipped(fork.Options[chosen].Frontier);
            route.Clear();
            var path = Map.FindPath(LeaderTile, fork.Options[chosen].Target, Known);
            if (path.Count >= 2) FollowPath(path, false);
            return true;
        }
    }
}
