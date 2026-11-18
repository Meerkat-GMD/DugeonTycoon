using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// What combat traits do outside the telegraphed patterns (those live in DungeonRun.Patterns), and the
    /// personality traits that only talk during a fight. Every check is deterministic: no dice, same situation,
    /// same choice. A tactic's focus from the AI still comes first; traits come before the role's own tactics.
    /// </summary>
    public sealed partial class DungeonRun
    {
        /// <summary>At or below this share of maximum health, in percent, triage, the cornered and survival-first fire. Balance value.</summary>
        public const int LowHpTraitPercent = 25;
        /// <summary>A finisher goes back for an old foe at or below this share of its health, in percent. Balance value.</summary>
        public const int FinisherHpPercent = 15;
        /// <summary>Ticks the back line keeps gathering at a line keeper's rally point. Balance value.</summary>
        public const int RallyTicks = 30;
        /// <summary>How many steps from the line keeper a rally point may be. Balance value.</summary>
        public const int RallySearchSteps = 4;

        /// <summary>For each hero, the monster that brought them down this fight, or -1.</summary>
        private readonly int[] downedBy = { -1, -1, -1, -1 };
        /// <summary>For each hero, the monster they are guarding someone from, or -1. That monster turns on them on a tie.</summary>
        private readonly int[] guarding = { -1, -1, -1, -1 };
        /// <summary>For each hero, the ally they guard, or -1.</summary>
        private readonly int[] guardWard = { -1, -1, -1, -1 };
        /// <summary>For each hero, <see cref="Situation"/> + 1 when they last called out their combat trait.</summary>
        private readonly int[] traitShouts = new int[4];
        private int rallyUntil;

        /// <summary>Where a line keeper told the back line to gather after the front line broke, or null.</summary>
        public TilePoint? RallyPoint { get; private set; }

        private static bool Low(HeroState hero) => hero.Hp > 0 && hero.Hp * 100 <= hero.MaxHp * LowHpTraitPercent;

        private void ClearTraitState()
        {
            for (int i = 0; i < 4; i++) downedBy[i] = guarding[i] = guardWard[i] = -1;
            Array.Clear(traitShouts, 0, traitShouts.Length);
            RallyPoint = null;
        }

        /// <summary>The hero says their combat trait's line and the log notes what it made them do, once a situation.</summary>
        private void TraitShout(HeroState hero, string detail)
        {
            if (traitShouts[(int)hero.Role] == Situation + 1) return;
            traitShouts[(int)hero.Role] = Situation + 1;
            hero.LastLine = TraitCatalog.Of(hero.CombatTrait).Line;
            log.Add($"{hero.Name} • {HeroTraits.Name(hero.CombatTrait)} — {detail}");
        }

        // ── Whom to hit ──────────────────────────────────────────────────

        /// <summary>
        /// The monster the hero's combat trait picks, or null to leave it to the role. A finisher ends a nearly dead
        /// old foe once reinforcements are in; a giant hunter turns on a big reinforcement in reach; an avenger goes
        /// after whoever downed an ally who is still down.
        /// </summary>
        private MonsterState TraitTarget(HeroState hero, List<MonsterState> engaged)
        {
            var here = heroTiles[(int)hero.Role];
            switch (hero.CombatTrait)
            {
                case CombatTrait.Finisher:
                {
                    if (!engaged.Any(m => m.Reinforcement)) return null;
                    var prey = engaged.Where(m => !m.Reinforcement && m.Hp * 100 <= m.MaxHp * FinisherHpPercent)
                        .OrderBy(m => m.Hp).ThenBy(m => m.Id).FirstOrDefault();
                    if (prey != null) TraitShout(hero, $"증원보다 빈사의 {HeroTraits.Particle(prey.Name, "을", "를")} 먼저 마무리합니다.");
                    return prey;
                }
                case CombatTrait.GiantHunter:
                {
                    var giant = engaged.Where(m => m.Reinforcement && m.IsBig && InReach(here, m.Tile, hero.Range))
                        .OrderBy(m => m.Id).FirstOrDefault();
                    if (giant != null) TraitShout(hero, $"합류한 {HeroTraits.Particle(giant.Name, "을", "를")} 먼저 노립니다.");
                    return giant;
                }
                case CombatTrait.Avenger:
                    foreach (var role in MarchOrder)
                    {
                        var ally = party[(int)role];
                        if (ally == hero || ally.Hp > 0 || downedBy[(int)role] < 0) continue;
                        var culprit = engaged.FirstOrDefault(m => m.Id == downedBy[(int)role]);
                        if (culprit == null) continue;
                        TraitShout(hero, $"{HeroTraits.Particle(ally.Name, "을", "를")} 쓰러뜨린 {HeroTraits.Particle(culprit.Name, "을", "를")} 노립니다.");
                        return culprit;
                    }
                    return null;
                default:
                    return null;
            }
        }

        // ── Whom to stand in front of ────────────────────────────────────

        /// <summary>
        /// The monster an interceptor or a bodyguard steps up to, or null. An interceptor answers a reinforcement going
        /// for a critical ally, a bodyguard anything going for the critical healer. The guard holds while the ward is
        /// standing and still critical, even after the monster has turned on the guard.
        /// </summary>
        private MonsterState GuardTarget(HeroState hero, List<MonsterState> engaged)
        {
            int self = (int)hero.Role;
            if (hero.CombatTrait != CombatTrait.Interceptor && hero.CombatTrait != CombatTrait.Bodyguard) return null;
            var kept = engaged.FirstOrDefault(m => m.Id == guarding[self]);
            if (kept != null && guardWard[self] >= 0 && party[guardWard[self]].Hp > 0 && party[guardWard[self]].Critical) return kept;
            foreach (var monster in engaged.OrderBy(m => m.Id))
            {
                var prey = MonsterTarget(monster);
                if (prey == null || prey == hero || !prey.Critical) continue;
                if (hero.CombatTrait == CombatTrait.Interceptor && !monster.Reinforcement) continue;
                if (hero.CombatTrait == CombatTrait.Bodyguard && prey.Role != HeroRole.Healer) continue;
                guardWard[self] = (int)prey.Role;
                return monster;
            }
            guardWard[self] = -1;
            return null;
        }

        /// <summary>
        /// Steps up to the guarded monster until next to it; a tank taunts it as soon as it can. Returns true when the
        /// step took the hero's turn. <paramref name="target"/> becomes that monster while guarding.
        /// </summary>
        private bool TryGuard(HeroState hero, List<MonsterState> engaged, HeroTactic tactic, ref MonsterState target, out bool guardingNow)
        {
            int self = (int)hero.Role;
            guardingNow = false;
            bool focused = tactic != null && tactic.Focus >= 0 && engaged.Any(m => m.Id == tactic.Focus);
            var monster = focused ? null : GuardTarget(hero, engaged);
            guarding[self] = monster?.Id ?? -1;
            if (monster == null) return false;
            guardingNow = true;
            target = monster;
            var ward = party[guardWard[self]];
            TraitShout(hero, $"{HeroTraits.Particle(ward.Name, "을", "를")} 노리는 {HeroTraits.Particle(monster.Name, "을", "를")} 막아섭니다.");
            if (hero.Role == HeroRole.Tank) TryTaunt(hero, engaged, force: true);
            if (Chebyshev(heroTiles[self], monster.Tile) <= 1) return false;
            HeroStep(hero, monster.Tile, 1);
            return true;
        }

        /// <summary>The hero is guarding someone from this monster, which then picks the guard over an equally near hero.</summary>
        private bool Guards(HeroState hero, MonsterState monster) => guarding[(int)hero.Role] == monster.Id;

        // ── Where to stand ───────────────────────────────────────────────

        /// <summary>Survival-first: badly hurt, the hero walks back into the healer's reach instead of fighting.</summary>
        private bool TrySurvive(HeroState hero)
        {
            if (hero.CombatTrait != CombatTrait.SurvivalFirst || hero.Role == HeroRole.Healer || !Low(hero)) return false;
            var healer = party[(int)HeroRole.Healer];
            if (healer.Hp == 0) return false;
            var here = heroTiles[(int)hero.Role];
            var there = heroTiles[(int)HeroRole.Healer];
            if (InReach(here, there, healer.Range) || Map.FindPath(here, there).Count < 2) return false;
            TraitShout(hero, $"{healer.Name}의 치유 사거리로 물러납니다.");
            HeroStep(hero, there, healer.Range);
            return true;
        }

        /// <summary>
        /// A front-liner fell: the first standing line keeper picks the floor tile within
        /// <see cref="RallySearchSteps"/> of them that is farthest from the fight (then fewest steps, row, column) and
        /// calls the back line there for <see cref="RallyTicks"/>.
        /// </summary>
        private void CallRally()
        {
            var keeper = MarchOrder.Select(role => party[(int)role])
                .FirstOrDefault(hero => hero.Hp > 0 && hero.CombatTrait == CombatTrait.LineKeeper);
            var engaged = Engaged.ToList();
            if (keeper == null || engaged.Count == 0) return;
            var from = heroTiles[(int)keeper.Role];
            TilePoint? best = null;
            int bestRoom = -1, bestSteps = int.MaxValue;
            foreach (var pair in Map.Distances(from, Map.IsFloor, RallySearchSteps).OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
            {
                int room = engaged.Min(m => Chebyshev(m.Tile, pair.Key));
                if (room < bestRoom || (room == bestRoom && pair.Value >= bestSteps)) continue;
                best = pair.Key;
                bestRoom = room;
                bestSteps = pair.Value;
            }
            if (best == null) return;
            RallyPoint = best;
            rallyUntil = Ticks + RallyTicks;
            string way = best.Value == from ? "내" : Direction(from, best.Value);
            keeper.LastLine = $"대열 무너졌어! {way} 쪽으로 모여!";
            log.Add($"대열 정비 • {keeper.Name} “{keeper.LastLine}”");
        }

        /// <summary>While a rally lasts, back-liners walk to within a tile of the rally point before anything else.</summary>
        private bool TryRally(HeroState hero)
        {
            if (RallyPoint == null) return false;
            if (Ticks >= rallyUntil)
            {
                RallyPoint = null;
                return false;
            }
            if (hero.Range <= 1) return false;
            var here = heroTiles[(int)hero.Role];
            if (Chebyshev(here, RallyPoint.Value) <= 1 || Map.FindPath(here, RallyPoint.Value).Count < 2) return false;
            HeroStep(hero, RallyPoint.Value, 1);
            return true;
        }

        // ── Calling out ──────────────────────────────────────────────────

        /// <summary>A lookout names the reinforcements and the side they come from.</summary>
        private void SpotReinforcements(IReadOnlyList<MonsterState> arrivals)
        {
            foreach (var role in MarchOrder)
            {
                var lookout = party[(int)role];
                if (lookout.Hp == 0 || lookout.CombatTrait != CombatTrait.Lookout) continue;
                var lead = arrivals.OrderBy(m => m.Id).First();
                string names = string.Join(", ", arrivals.OrderBy(m => m.Id).Select(m => m.Name));
                lookout.LastLine = $"{Direction(heroTiles[(int)role], lead.Tile)}쪽! {HeroTraits.Subject(names)} 온다!";
                log.Add($"망보기 • {lookout.Name} “{lookout.LastLine}”");
            }
        }

        /// <summary>Records who downed a hero, and a line keeper answers a broken front line.</summary>
        private void Downed(HeroState hero, MonsterState attacker)
        {
            downedBy[(int)hero.Role] = attacker?.Id ?? -1;
            if (Phase == RunPhase.Combat && (hero.Role == HeroRole.Tank || hero.Role == HeroRole.MeleeDamage)) CallRally();
        }

        /// <summary>A heal pulled a critical ally out of danger: a cheerer praises whoever healed them.</summary>
        private void Cheer(HeroState healer, HeroState patient)
        {
            var cheerer = patient != healer && patient.Personality == PersonalityTrait.Cheerer
                ? patient
                : MarchOrder.Select(role => party[(int)role])
                    .FirstOrDefault(hero => hero.Hp > 0 && hero != healer && hero.Personality == PersonalityTrait.Cheerer);
            if (cheerer == null) return;
            string line = cheerer == patient ? DialogueLines.CheerOwnRescue(healer.Name) : DialogueLines.CheerRescue(healer.Name, patient.Name);
            cheerer.LastLine = line;
            log.Add($"칭찬 • {cheerer.Name} “{line}”");
        }

        /// <summary>A nagging hero points out what an ally the pattern caught should have watched.</summary>
        private void Nag(PatternCast cast)
        {
            bool ground = cast.Pattern == MonsterPattern.Slam || cast.Pattern == MonsterPattern.Meteor;
            foreach (var role in MarchOrder)
            {
                var nagger = party[(int)role];
                if (nagger.Hp == 0 || nagger.Personality != PersonalityTrait.Grumbler) continue;
                var victim = MarchOrder.Select(r => party[(int)r])
                    .FirstOrDefault(hero => hero != nagger && hero.Hp > 0 && cast.hits.Contains((int)hero.Role));
                if (victim == null) continue;
                nagger.LastLine = DialogueLines.Nag(victim.Name, ground);
                log.Add($"잔소리 • {nagger.Name} “{nagger.LastLine}”");
            }
        }
    }
}
