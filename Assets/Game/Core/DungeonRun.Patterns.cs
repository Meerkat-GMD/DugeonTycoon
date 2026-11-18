using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// Telegraphed monster attacks and how heroes meet them. A cast shows its area, may follow its target until it
    /// locks, and hits whoever stands in the area when it fires. Heroes decide each tick, from the AI tactic, then
    /// the trait hook, then their role, whether to dodge, hold or lure. Nothing here rolls dice.
    /// </summary>
    public sealed partial class DungeonRun
    {
        /// <summary>Ticks a landed pattern stays on the board for its flash. The rules ignore it by then.</summary>
        public const int PatternFlashTicks = 4;
        /// <summary>A monster that starts hunting waits this long before its first pattern. Balance value.</summary>
        public const int FirstPatternDelayTicks = 20;
        /// <summary>Each further pattern of the same monster waits this much longer at the start. Balance value.</summary>
        public const int PatternStaggerTicks = 15;
        /// <summary>The meteor falls on the hero with the most allies this close (Chebyshev). Balance value.</summary>
        public const int MeteorCrowdRadius = 2;
        /// <summary>Half the width of the tracking beam in tiles: 1 makes it three tiles wide. Balance value.</summary>
        private const double BeamHalfWidth = 1.0;
        private const double BoltHalfWidth = .5;

        private readonly List<PatternCast> casts = new List<PatternCast>();
        private readonly HashSet<string> seenPatterns = new HashSet<string>();

        /// <summary>Patterns being telegraphed or just landed, oldest first. The board draws these.</summary>
        public IReadOnlyList<PatternCast> Casts => casts;

        /// <summary>The party has watched a monster like this one use the pattern. Tactic requests only carry these.</summary>
        public bool PartyKnows(MonsterState monster, MonsterPattern pattern) => seenPatterns.Contains(PatternKey(monster, pattern));

        private static string PatternKey(MonsterState monster, MonsterPattern pattern) => monster.Name + "/" + (int)pattern;

        private IEnumerable<PatternCast> ActiveCasts => casts.Where(c => c.Phase != CastPhase.Fired && c.Caster.Alive);

        /// <summary>The hero is partway through a step and cannot attack, heal or use a skill until it lands.</summary>
        private bool Moving(HeroState hero) => hero.StepUntil > Ticks;
        /// <summary>The monster is partway through a step and cannot attack or cast until it lands.</summary>
        private bool Moving(MonsterState monster) => monster.StepUntil > Ticks;

        /// <summary>Where a pattern would find the hero now: a hero still in the middle of a step is hit where it started.</summary>
        public TilePoint StandingTile(HeroRole role)
        {
            var hero = party[(int)role];
            return hero.StepUntil > Ticks ? hero.StepFrom : heroTiles[(int)role];
        }

        private void StepHero(HeroState hero, TilePoint to)
        {
            hero.StepFrom = heroTiles[(int)hero.Role];
            heroTiles[(int)hero.Role] = to;
            hero.MoveReady = hero.StepUntil = Ticks + CombatMoveTicks(hero);
        }

        /// <summary>How many whole steps the hero can finish in <paramref name="ticks"/>, at the slowest its speed rounds to.</summary>
        private static int StepsWithin(HeroState hero, int ticks)
        {
            int speed = Math.Max(1, hero.SpeedHundredths);
            int perStep = Math.Max(1, (HeroMoveTicks * 10000 + speed - 1) / speed);
            return ticks / perStep;
        }

        private void ArmPatterns(MonsterState monster)
        {
            for (int i = 0; i < monster.Patterns.Count; i++)
                monster.PatternReady[i] = Ticks + FirstPatternDelayTicks + i * PatternStaggerTicks;
        }

        private void ClearCasts() => casts.Clear();

        // ── Casting ──────────────────────────────────────────────────────

        private PatternCast CastOf(MonsterState monster) =>
            casts.FirstOrDefault(c => c.Caster == monster && c.Phase != CastPhase.Fired);

        /// <summary>
        /// Starts the last listed pattern that is ready and can reach someone. Returns true when the monster spends
        /// this tick casting.
        /// </summary>
        private bool TryCast(MonsterState monster, HeroState target)
        {
            for (int i = monster.Patterns.Count - 1; i >= 0; i--)
            {
                if (monster.PatternReady[i] > Ticks) continue;
                var pattern = monster.Patterns[i];
                var aimed = PatternTarget(monster, pattern, target);
                if (!CanCast(monster, pattern, aimed)) continue;
                Begin(monster, pattern, aimed, i);
                return true;
            }
            return false;
        }

        private HeroState PatternTarget(MonsterState monster, MonsterPattern pattern, HeroState target)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return null;
                case MonsterPattern.Meteor: return CrowdedHero();
                default: return target;
            }
        }

        /// <summary>The hero with the most living allies close by; then the most hurt; then the front of the march.</summary>
        internal HeroState CrowdedHero()
        {
            HeroState best = null;
            int bestCrowd = -1;
            foreach (var role in MarchOrder)
            {
                var hero = party[(int)role];
                if (hero.Hp == 0) continue;
                var here = StandingTile(role);
                int crowd = party.Count(other => other != hero && other.Hp > 0 &&
                    Chebyshev(StandingTile(other.Role), here) <= MeteorCrowdRadius);
                bool better = crowd > bestCrowd ||
                    (crowd == bestCrowd && hero.Hp * best.MaxHp < best.Hp * hero.MaxHp);
                if (!better) continue;
                best = hero;
                bestCrowd = crowd;
            }
            return best;
        }

        private bool CanCast(MonsterState monster, MonsterPattern pattern, HeroState aimed)
        {
            int reach = MonsterPatterns.Reach(pattern);
            switch (pattern)
            {
                case MonsterPattern.Slam:
                    var area = AreaOf(monster, pattern, monster.Tile);
                    return party.Any(hero => hero.Hp > 0 && area.Contains(StandingTile(hero.Role)));
                case MonsterPattern.Meteor:
                    return aimed != null && Chebyshev(monster.Tile, StandingTile(aimed.Role)) <= reach;
                default:
                    if (aimed == null) return false;
                    var there = StandingTile(aimed.Role);
                    return there != monster.Tile && InReach(monster.Tile, there, reach);
            }
        }

        private void Begin(MonsterState monster, MonsterPattern pattern, HeroState aimed, int index)
        {
            var cast = new PatternCast(monster, pattern, aimed == null ? -1 : (int)aimed.Role, Ticks);
            cast.Aim = aimed == null ? monster.Tile : StandingTile(aimed.Role);
            cast.area = AreaOf(monster, pattern, cast.Aim);
            if (pattern == MonsterPattern.Tombstones)
            {
                var second = party.Where(h => h != aimed && h.Hp > 0 &&
                    InReach(monster.Tile, StandingTile(h.Role), monster.Range))
                    .OrderBy(h => StandingTile(h.Role) == cast.Aim)
                    .ThenBy(h => Chebyshev(monster.Tile, StandingTile(h.Role))).FirstOrDefault();
                if (second != null)
                {
                    cast.SecondAim = StandingTile(second.Role);
                    cast.area.Add(cast.SecondAim.Value);
                }
            }
            monster.PatternReady[index] = Ticks + MonsterPatterns.CooldownTicks(pattern);
            casts.Add(cast);
            seenPatterns.Add(PatternKey(monster, pattern));
            events.Add(new CombatEvent(CombatEventKind.Pattern, false, monster.Id, true, cast.Target, (int)pattern));
            string name = MonsterPatterns.Name(pattern);
            log.Add(aimed == null
                ? $"{monster.Name} • {name}! 주변을 내리찍으려 합니다."
                : $"{monster.Name} • {name}! → {aimed.Name} 조준");
        }

        /// <summary>Beams follow their target, lock and land; landed ones fade; a fallen caster's pattern is called off.</summary>
        private void AdvanceCasts()
        {
            foreach (var cast in casts.ToList())
            {
                if (cast.Phase == CastPhase.Fired)
                {
                    if (Ticks >= cast.FireTick + PatternFlashTicks) casts.Remove(cast);
                    continue;
                }
                if (!cast.Caster.Alive)
                {
                    casts.Remove(cast);
                    continue;
                }
                if (cast.Phase == CastPhase.Aiming)
                {
                    var target = cast.Target >= 0 ? party[cast.Target] : null;
                    if (target != null && target.Hp > 0) cast.Aim = StandingTile(target.Role);
                    cast.area = AreaOf(cast.Caster, cast.Pattern, cast.Aim);
                    if (Ticks >= cast.LockTick) cast.Phase = CastPhase.Locked;
                }
                if (Ticks >= cast.FireTick) Fire(cast);
                if (!InDungeon) return;
            }
        }

        private void Fire(PatternCast cast)
        {
            cast.Phase = CastPhase.Fired;
            var caster = cast.Caster;
            caster.AttackReady = Math.Max(caster.AttackReady, Ticks + caster.AttackTicks / 2);
            var struck = new List<string>();
            foreach (var role in MarchOrder)
            {
                var hero = party[(int)role];
                if (hero.Hp == 0 || !cast.area.Contains(StandingTile(role))) continue;
                int damage = Shielded(hero, Mitigate(caster.Attack * MonsterPatterns.DamagePercent(cast.Pattern) / 100, hero.Armor));
                cast.hits.Add((int)role);
                struck.Add($"{hero.Name} {damage}");
                var hit = new CombatEvent(CombatEventKind.Attack, false, caster.Id, true, (int)role, damage,
                    isPatternDamage: true);
                events.Add(hit);
                Hurt(hero, damage, caster.Name, false, caster);
                if (cast.Pattern == MonsterPattern.Shove && hero.Hp > 0)
                    hit.PushedHero = PushHero(hero, caster.Tile, cast.Aim);
                if (!InDungeon) break;
            }
            string name = MonsterPatterns.Name(cast.Pattern);
            log.Add(struck.Count == 0 ? $"{name} 발동 • 아무도 맞지 않았습니다." : $"{name} 발동 • {string.Join(", ", struck)} 피해");
            if (struck.Count > 0 && InDungeon) Nag(cast);
        }

        /// <summary>
        /// The tiles a pattern covers when aimed through <paramref name="aim"/>. Walls stop it. The tracking beam also
        /// scorches the 3×3 around its caster, so a target cannot slip out of it by hugging the caster.
        /// </summary>
        private HashSet<TilePoint> AreaOf(MonsterState caster, MonsterPattern pattern, TilePoint aim)
        {
            switch (pattern)
            {
                case MonsterPattern.Slam: return Square(caster.Tile);
                case MonsterPattern.Tombstones:
                case MonsterPattern.Shove: return new HashSet<TilePoint> { aim };
                case MonsterPattern.Cleave:
                    int dx = Math.Sign(aim.X - caster.Tile.X), dy = Math.Sign(aim.Y - caster.Tile.Y);
                    var fan = Square(caster.Tile);
                    fan.RemoveWhere(p => {
                        int x = p.X - caster.Tile.X, y = p.Y - caster.Tile.Y;
                        return x * dx + y * dy < Math.Max(1, Math.Abs(x * dy - y * dx));
                    });
                    return fan;
                case MonsterPattern.Bomb:
                case MonsterPattern.Meteor: return Square(aim);
                case MonsterPattern.SoulBolt: return Line(caster.Tile, aim, MonsterPatterns.Reach(pattern), BoltHalfWidth);
                case MonsterPattern.TrackingBeam:
                    var beam = Line(caster.Tile, aim, MonsterPatterns.Reach(pattern), BeamHalfWidth);
                    beam.UnionWith(Square(caster.Tile));
                    return beam;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        private bool PushHero(HeroState hero, TilePoint from, TilePoint aim)
        {
            var here = StandingTile(hero.Role);
            var dest = new TilePoint(here.X + Math.Sign(aim.X - from.X), here.Y + Math.Sign(aim.Y - from.Y));
            if (!Map.IsFloor(dest) || !Map.HasLineOfSight(here, dest) ||
                party.Any(h => h != hero && h.Hp > 0 && (StandingTile(h.Role) == dest || HeroTile(h.Role) == dest)) ||
                monsters.Any(m => m.Alive && m.Tile == dest)) return false;
            heroTiles[(int)hero.Role] = dest;
            hero.StepFrom = dest;
            hero.StepUntil = Ticks;
            return dest != here;
        }

        private HashSet<TilePoint> Square(TilePoint centre)
        {
            var area = new HashSet<TilePoint>();
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var tile = new TilePoint(centre.X + dx, centre.Y + dy);
                if (Map.IsFloor(tile) && Map.HasLineOfSight(centre, tile)) area.Add(tile);
            }
            return area;
        }

        /// <summary>
        /// Floor tiles ahead of <paramref name="from"/> toward <paramref name="aim"/> whose centres lie within
        /// <paramref name="halfWidth"/> of the line, up to <paramref name="reach"/> tiles or the first wall on the line.
        /// </summary>
        private HashSet<TilePoint> Line(TilePoint from, TilePoint aim, int reach, double halfWidth)
        {
            var area = new HashSet<TilePoint>();
            double dx = aim.X - from.X, dy = aim.Y - from.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0) return area;
            double ux = dx / length, uy = dy / length;
            double stop = reach;
            for (double t = .25; t <= reach; t += .25)
            {
                var sample = new TilePoint((int)Math.Floor(from.X + ux * t + .5), (int)Math.Floor(from.Y + uy * t + .5));
                if (Map.IsFloor(sample)) continue;
                stop = t;
                break;
            }
            for (int y = from.Y - reach; y <= from.Y + reach; y++)
            for (int x = from.X - reach; x <= from.X + reach; x++)
            {
                var tile = new TilePoint(x, y);
                if (tile == from || !Map.IsFloor(tile)) continue;
                double vx = x - from.X, vy = y - from.Y;
                double along = vx * ux + vy * uy;
                double across = Math.Abs(vx * uy - vy * ux);
                if (along <= 0 || along > stop || across > halfWidth + 1e-9) continue;
                if (Map.HasLineOfSight(from, tile)) area.Add(tile);
            }
            return area;
        }

        // ── Heroes answering ─────────────────────────────────────────────

        /// <summary>
        /// The tactic's answer, else the hero's combat trait, else the role default. Lure only stands for the beam's
        /// own target before it locks; anywhere else it means dodge.
        /// </summary>
        public PatternResponse ResponseTo(HeroRole role, MonsterPattern pattern, bool beamTarget)
        {
            var hero = party[(int)role];
            var tactic = tactics[(int)role];
            PatternResponse response;
            if (tactic != null && tactic.Responses.TryGetValue(pattern, out var planned)) response = planned;
            else response = TraitResponse(hero, pattern, beamTarget) ?? MonsterPatterns.RoleDefault(role, pattern, beamTarget);
            if (response == PatternResponse.Lure && !(pattern == MonsterPattern.TrackingBeam && beamTarget)) return PatternResponse.Dodge;
            return response;
        }

        /// <summary>Below this share of maximum health, in percent, the stoic stops holding through ground blasts. Balance value.</summary>
        public const int StoicHoldPercent = 60;

        /// <summary>
        /// How the hero's combat trait meets the pattern, or null to fall back on the role. A slam or a meteor is a
        /// blast at the hero's feet; a soul bolt or a beam the hero is not the target of is a line to step off.
        /// </summary>
        private PatternResponse? TraitResponse(HeroState hero, MonsterPattern pattern, bool beamTarget)
        {
            bool ground = MonsterPatterns.IsGround(pattern);
            switch (hero.CombatTrait)
            {
                case CombatTrait.DamageMad:
                    return PatternResponse.Hold;
                case CombatTrait.Protector:
                    if (beamTarget) return PatternResponse.Lure;
                    return ground ? PatternResponse.Dodge : (PatternResponse?)null;
                case CombatTrait.QuickFeet:
                    return beamTarget ? (PatternResponse?)null : PatternResponse.Dodge;
                case CombatTrait.Stoic:
                    return ground && hero.Hp * 100 >= hero.MaxHp * StoicHoldPercent ? PatternResponse.Hold : (PatternResponse?)null;
                case CombatTrait.BeamPilot:
                    return beamTarget ? PatternResponse.Lure : (PatternResponse?)null;
                default:
                    return null;
            }
        }

        private static bool IsBeamTarget(PatternCast cast, HeroState hero) =>
            cast.Pattern == MonsterPattern.TrackingBeam && cast.Phase == CastPhase.Aiming && cast.Target == (int)hero.Role;

        private PatternResponse ResponseTo(HeroState hero, PatternCast cast) =>
            ResponseTo(hero.Role, cast.Pattern, IsBeamTarget(cast, hero));

        /// <summary>
        /// A tile this hero stays off: inside a pattern it means to dodge, or, while it lures a beam, a spot from which
        /// the beam would catch an ally.
        /// </summary>
        private bool Dangerous(HeroState hero, TilePoint tile) =>
            ActiveCasts.Any(cast =>
            {
                var response = ResponseTo(hero, cast);
                if (response == PatternResponse.Lure) return Caught(cast, hero, tile) > 0;
                return response == PatternResponse.Dodge && cast.area.Contains(tile);
            });

        /// <summary>Allies the cast would hit if it were aimed through <paramref name="aim"/>.</summary>
        private int Caught(PatternCast cast, HeroState hero, TilePoint aim)
        {
            var area = AreaOf(cast.Caster, cast.Pattern, aim);
            return party.Count(ally => ally != hero && ally.Hp > 0 && area.Contains(StandingTile(ally.Role)));
        }

        /// <summary>Lures a beam aimed at the hero or steps out of an area it dodges. Returns true when that took the hero's turn.</summary>
        private bool AnswerPatterns(HeroState hero)
        {
            var here = heroTiles[(int)hero.Role];
            PatternCast lure = null;
            int deadline = int.MaxValue;
            foreach (var cast in ActiveCasts)
            {
                var response = ResponseTo(hero, cast);
                bool target = IsBeamTarget(cast, hero);
                if (!target && !cast.area.Contains(here)) continue;
                Announce(hero, cast, response);
                if (response == PatternResponse.Lure) lure = cast;
                else if (response == PatternResponse.Dodge) deadline = Math.Min(deadline, cast.FireTick);
            }
            if (lure != null && TryLure(hero, lure)) return true;
            return deadline != int.MaxValue && TryDodge(hero, deadline);
        }

        private void Announce(HeroState hero, PatternCast cast, PatternResponse response)
        {
            if (!cast.answered.Add((int)hero.Role)) return;
            hero.LastLine = MonsterPatterns.Shout(response);
            log.Add($"{hero.Name} • {MonsterPatterns.Name(cast.Pattern)} {MonsterPatterns.Name(response)}");
        }

        /// <summary>
        /// Walks toward the nearest tile out of every area the hero dodges that it can reach before
        /// <paramref name="deadline"/>. Stays and keeps fighting when there is none.
        /// </summary>
        private bool TryDodge(HeroState hero, int deadline)
        {
            int steps = StepsWithin(hero, deadline - Ticks);
            if (hero.MoveReady > Ticks || steps < 1) return false;
            var here = heroTiles[(int)hero.Role];
            var near = NearestFoe(here);
            TilePoint? best = null;
            int bestSteps = int.MaxValue, bestGap = 0;
            foreach (var pair in Map.Distances(here, Map.IsFloor, steps).OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
            {
                if (Dangerous(hero, pair.Key)) continue;
                // Melee heroes stay close to the fight; everyone else keeps their distance.
                int gap = near == null ? 0 : Chebyshev(pair.Key, near.Tile) * (hero.Range > 1 ? -1 : 1);
                if (pair.Value > bestSteps || (pair.Value == bestSteps && gap >= bestGap)) continue;
                best = pair.Key;
                bestSteps = pair.Value;
                bestGap = gap;
            }
            if (best == null || best.Value == here) return false;
            var path = Map.FindPath(here, best.Value);
            if (path.Count < 2) return false;
            StepHero(hero, path[1]);
            return true;
        }

        /// <summary>
        /// Before the beam locks, its target walks to the nearest tile it can reach in time from which the beam would
        /// catch the fewest allies. A hero whose line is already clear stays and fights.
        /// </summary>
        private bool TryLure(HeroState hero, PatternCast cast)
        {
            var here = heroTiles[(int)hero.Role];
            int now = Caught(cast, hero, here);
            if (now == 0 || hero.MoveReady > Ticks) return false;
            int steps = StepsWithin(hero, cast.LockTick - Ticks);
            if (steps < 1) return false;
            TilePoint? best = null;
            int bestCaught = now, bestSteps = int.MaxValue;
            foreach (var pair in Map.Distances(here, Map.IsFloor, steps).OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
            {
                if (pair.Key == here) continue;
                // Other patterns still count; this beam's own spots are judged by how many allies they catch.
                if (ActiveCasts.Any(other => other != cast && other.area.Contains(pair.Key) &&
                    ResponseTo(hero, other) == PatternResponse.Dodge)) continue;
                int caught = Caught(cast, hero, pair.Key);
                if (caught >= now || caught > bestCaught || (caught == bestCaught && pair.Value >= bestSteps)) continue;
                best = pair.Key;
                bestCaught = caught;
                bestSteps = pair.Value;
            }
            if (best == null) return false;
            var path = Map.FindPath(here, best.Value);
            if (path.Count < 2) return false;
            StepHero(hero, path[1]);
            return true;
        }

        private MonsterState NearestFoe(TilePoint tile) =>
            monsters.Where(m => m.Alive && m.Mood == MonsterMood.Chasing)
                .OrderBy(m => Chebyshev(m.Tile, tile)).ThenBy(m => m.Id).FirstOrDefault();

        // ── Test seams ───────────────────────────────────────────────────

        /// <summary>Tests make a monster cast now, aimed at a chosen hero (ignored by the slam).</summary>
        internal PatternCast ForceCast(MonsterState monster, MonsterPattern pattern, HeroRole? target = null)
        {
            int index = -1;
            for (int i = 0; i < monster.Patterns.Count; i++)
                if (monster.Patterns[i] == pattern) index = i;
            if (index < 0) throw new ArgumentException($"{monster.Name} has no {pattern}.", nameof(pattern));
            Begin(monster, pattern, target.HasValue ? party[(int)target.Value] : null, index);
            return casts[casts.Count - 1];
        }
    }
}
