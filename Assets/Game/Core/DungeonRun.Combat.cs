using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    public sealed partial class DungeonRun
    {
        /// <summary>How far a monster notices a hero it can see. Balance value.</summary>
        public const int DetectRadius = 4;
        /// <summary>How far from its placement a monster wanders, in steps. Balance value.</summary>
        public const int WanderRadius = 3;
        /// <summary>Ticks between a hero's attacks or heals. Balance value.</summary>
        public const int HeroAttackTicks = 15;
        private const int HealThreshold = 6;

        private readonly HeroTactic[] tactics = new HeroTactic[4];
        private readonly List<MonsterState> joined = new List<MonsterState>();
        private readonly List<CombatEvent> events = new List<CombatEvent>();

        /// <summary>
        /// Changes when the fight changes shape: it starts, a monster joins, a hero drops below 30% or falls.
        /// Tactics answer one situation; an answer for an older one is ignored.
        /// </summary>
        public int Situation { get; private set; }
        public bool ReachedBoss { get; private set; }
        public MonsterState Boss => monsters.Count > 0 && monsters[monsters.Count - 1].IsBoss ? monsters[monsters.Count - 1] : null;
        public bool FightingBoss => Phase == RunPhase.Combat && Boss != null && Boss.Alive && Boss.Mood == MonsterMood.Chasing;
        /// <summary>Living monsters chasing or fighting the party.</summary>
        public IEnumerable<MonsterState> Engaged => monsters.Where(m => m.Alive && m.Mood == MonsterMood.Chasing);
        public HeroTactic TacticOf(HeroRole role) => tactics[(int)role];

        /// <summary>Animation cues since the last call. The board plays them; the rules never read them.</summary>
        public IReadOnlyList<CombatEvent> TakeEvents()
        {
            var taken = events.ToArray();
            events.Clear();
            return taken;
        }

        private MonsterState CreateMonster(int id, MonsterKind kind, TilePoint tile)
        {
            bool later = Stage == 2;
            return kind == MonsterKind.Ogre
                ? new MonsterState(id, kind, false, "오우거", later ? 106 : 69, later ? 15 : 10, 2, 8, 4, 18, tile)
                : new MonsterState(id, kind, false, "고블린", later ? 73 : 47, later ? 10 : 6, 0, 5, 3, 15, tile);
        }

        private MonsterState CreateBoss() => Stage == 2
            ? new MonsterState(placements.Count, MonsterKind.None, true, "마왕 벨자크", 158, 19, 10, 0, 4, 15, Map.BossTile)
            : new MonsterState(placements.Count, MonsterKind.None, true, "성문 수호자", 64, 10, 1, 2, 4, 15, Map.BossTile);

        private static int Chebyshev(TilePoint a, TilePoint b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private bool InReach(TilePoint from, TilePoint to, int range)
        {
            if (range <= 1) return Chebyshev(from, to) <= 1 && Map.HasLineOfSight(from, to);
            int dx = from.X - to.X, dy = from.Y - to.Y;
            return dx * dx + dy * dy <= range * range && Map.HasLineOfSight(from, to);
        }

        /// <summary>Where a monster may walk: an idle boss keeps to its room, anyone on the hunt goes anywhere.</summary>
        private Func<TilePoint, bool> Walkable(MonsterState monster) =>
            monster.IsBoss && monster.Mood != MonsterMood.Chasing ? (Func<TilePoint, bool>)Map.IsBossRoom : Map.IsFloor;

        // ── Monsters ─────────────────────────────────────────────────────

        /// <summary>A monster that notices a hero hunts the party until it or the party falls.</summary>
        private void Perceive(MonsterState monster)
        {
            if (!monster.Alive || monster.Mood == MonsterMood.Chasing) return;
            bool seesHero = party.Any(hero => hero.Hp > 0 &&
                InReach(monster.Tile, heroTiles[(int)hero.Role], DetectRadius));
            if (!seesHero) return;
            monster.Mood = MonsterMood.Chasing;
            monster.WanderGoal = null;
            joined.Add(monster);
        }

        private void UpdateEngagement()
        {
            bool engaged = monsters.Any(m => m.Alive && m.Mood == MonsterMood.Chasing);
            if (engaged && Phase == RunPhase.Travel)
            {
                Phase = RunPhase.Combat;
                CancelFork();
                route.Clear();
                BeginSpoils();
                log.Add("교전 • " + HeroTraits.Subject(string.Join(", ", joined.Select(m => m.Name))) + " 용사들을 발견했다!");
                SetBattleCries();
                BumpSituation();
            }
            else if (joined.Count > 0 && Phase == RunPhase.Combat)
            {
                log.Add("합류 • " + HeroTraits.Subject(string.Join(", ", joined.Select(m => m.Name))) + " 전투에 끼어듭니다.");
                BumpSituation();
            }
            if (joined.Any(m => m.IsBoss) && !ReachedBoss)
            {
                ReachedBoss = true;
                log.Add($"보스전 • {HeroTraits.Subject(Boss.Name)} 용사들 앞을 막아섭니다!  체력 {Boss.Hp} / 방어 {Boss.Armor}");
            }
            joined.Clear();

            if (!engaged && Phase == RunPhase.Combat)
            {
                Phase = RunPhase.Travel;
                Array.Clear(tactics, 0, tactics.Length);
                Regroup();
                int hp = party.Sum(h => h.Hp), max = party.Sum(h => h.MaxHp);
                log.Add($"전투 종료 • 파티 체력 {hp} / {max}");
                ReleasePrayerHolds(combatEnded: true);
                SettleSpoils();
            }
        }

        private void BumpSituation()
        {
            Situation++;
            Array.Clear(tactics, 0, tactics.Length);
        }

        private void SetBattleCries()
        {
            foreach (var hero in party)
            {
                if (hero.Hp == 0) continue;
                switch (hero.Role)
                {
                    case HeroRole.Tank: hero.LastLine = "내 뒤로 붙어!"; break;
                    case HeroRole.Healer: hero.LastLine = "다치면 바로 말해요!"; break;
                    case HeroRole.MeleeDamage: hero.LastLine = "한 놈씩 베어 주지!"; break;
                    default: hero.LastLine = "사선 확보했어."; break;
                }
            }
        }

        private HeroState MonsterTarget(MonsterState monster)
        {
            var tank = party[(int)HeroRole.Tank];
            if (monster.TauntedUntil > Ticks && tank.Hp > 0) return tank;
            // The nearest hero; among equally near ones, the most hurt. This is what the tank's taunt is for.
            HeroState best = null;
            int bestDistance = int.MaxValue;
            foreach (var hero in party)
            {
                if (hero.Hp == 0) continue;
                int distance = Chebyshev(monster.Tile, heroTiles[(int)hero.Role]);
                bool nearer = distance < bestDistance;
                bool weaker = distance == bestDistance && hero.Hp * best.MaxHp < best.Hp * hero.MaxHp;
                if (!nearer && !weaker) continue;
                best = hero;
                bestDistance = distance;
            }
            return best;
        }

        private void MonsterTick(MonsterState monster)
        {
            if (!monster.Alive) return;
            switch (monster.Mood)
            {
                case MonsterMood.Chasing:
                    var target = MonsterTarget(monster);
                    if (target == null) return;
                    var tile = heroTiles[(int)target.Role];
                    if (InReach(monster.Tile, tile, 1))
                    {
                        if (monster.AttackReady > Ticks) return;
                        monster.AttackReady = Ticks + monster.AttackTicks;
                        int damage = Shielded(target, Math.Max(0, monster.Attack - target.Armor));
                        events.Add(new CombatEvent(CombatEventKind.Attack, false, monster.Id, true, (int)target.Role, damage));
                        Hurt(target, damage, monster.Name, false);
                    }
                    else MonsterStep(monster, tile);
                    break;
                default:
                    Wander(monster);
                    break;
            }
        }

        private void MonsterStep(MonsterState monster, TilePoint goal)
        {
            if (monster.MoveReady > Ticks) return;
            var walkable = Walkable(monster);
            var path = Map.FindPath(monster.Tile, goal, tile => walkable(tile) || tile == goal);
            TilePoint? next = path.Count >= 2 ? path[1] : (TilePoint?)null;
            if (next.HasValue && !walkable(next.Value)) next = null;
            if (next == null)
            {
                // No open path (e.g. the goal is a wall): shuffle to the neighbouring tile nearest the goal instead.
                int best = Chebyshev(monster.Tile, goal);
                foreach (var step in new[] { new TilePoint(0, -1), new TilePoint(1, 0), new TilePoint(0, 1), new TilePoint(-1, 0) })
                {
                    var candidate = new TilePoint(monster.Tile.X + step.X, monster.Tile.Y + step.Y);
                    if (!walkable(candidate) || Chebyshev(candidate, goal) >= best) continue;
                    best = Chebyshev(candidate, goal);
                    next = candidate;
                }
            }
            if (next == null) return;
            monster.Tile = next.Value;
            monster.MoveReady = Ticks + monster.MoveTicks;
        }

        private void Wander(MonsterState monster)
        {
            if (monster.MoveReady > Ticks) return;
            if (monster.WanderGoal == null || monster.Tile == monster.WanderGoal.Value)
            {
                if (monster.WanderWait > 0)
                {
                    monster.WanderWait--;
                    return;
                }
                var spots = WanderArea(monster).OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
                monster.WanderGoal = spots[dice.Next(spots.Count)];
                monster.WanderWait = 10 + dice.Next(0, 20);
            }
            // Wandering stays inside the area: the shortest way between two spots could clip outside it.
            var area = WanderArea(monster);
            if (!area.Contains(monster.Tile)) area = null;
            if (monster.MoveReady > Ticks) return;
            var path = area == null
                ? Map.FindPath(monster.Tile, monster.WanderGoal.Value, Walkable(monster))
                : Map.FindPath(monster.Tile, monster.WanderGoal.Value, area.Contains);
            if (path.Count < 2) return;
            monster.Tile = path[1];
            monster.MoveReady = Ticks + monster.MoveTicks;
        }

        private readonly Dictionary<MonsterState, HashSet<TilePoint>> wanderAreas = new Dictionary<MonsterState, HashSet<TilePoint>>();

        private HashSet<TilePoint> WanderArea(MonsterState monster)
        {
            if (!wanderAreas.TryGetValue(monster, out var area))
            {
                area = new HashSet<TilePoint>(Map.Distances(monster.Home, Walkable(monster), WanderRadius).Keys);
                wanderAreas[monster] = area;
            }
            return area;
        }

        // ── Heroes ───────────────────────────────────────────────────────

        private void HeroCombatTick(HeroState hero)
        {
            var engaged = monsters.Where(m => m.Alive && m.Mood == MonsterMood.Chasing).ToList();
            if (engaged.Count == 0) return;
            var tactic = tactics[(int)hero.Role];
            var here = heroTiles[(int)hero.Role];
            if (hero.Role == HeroRole.Tank) TankSkills(hero, engaged);
            if (hero.Role == HeroRole.Healer && (TryRevive(hero) || TryHeal(hero, tactic))) return;
            if (hero.Role == HeroRole.MeleeDamage && TryWhirlwind(hero, engaged)) return;
            var target = PickTarget(hero, engaged, tactic);
            if (hero.Range > 1 && engaged.Any(m => Chebyshev(m.Tile, here) <= 1) && hero.MoveReady <= Ticks && Retreat(hero, engaged))
                return;
            if (InReach(here, target.Tile, hero.Range))
            {
                if (hero.ActReady <= Ticks) Attack(hero, target, engaged);
            }
            else HeroStep(hero, target.Tile, hero.Range);
        }

        private MonsterState PickTarget(HeroState hero, List<MonsterState> engaged, HeroTactic tactic)
        {
            if (tactic != null && tactic.Focus >= 0)
            {
                var focus = engaged.FirstOrDefault(m => m.Id == tactic.Focus);
                if (focus != null) return focus;
            }
            var here = heroTiles[(int)hero.Role];
            if (hero.Role == HeroRole.Tank)
                return engaged.OrderBy(m => Chebyshev(m.Tile, here)).ThenBy(m => m.Id).First();
            var reachable = engaged.Where(m => InReach(here, m.Tile, hero.Range)).ToList();
            if (reachable.Count > 0) return reachable.OrderBy(m => m.Hp).ThenBy(m => m.Id).First();
            return engaged.OrderBy(m => Chebyshev(m.Tile, here)).ThenBy(m => m.Id).First();
        }

        private void Strike(HeroState hero, MonsterState monster)
        {
            hero.ActReady = Ticks + AttackCycle(hero);
            Hit(hero, monster, HitDamage(hero, monster));
        }

        private int HitDamage(HeroState hero, MonsterState monster) =>
            Math.Max(0, hero.Attack + 2 * (Level - 1) - FatigueAttackPenalty - monster.Armor);

        /// <summary>Deals the damage; a monster brought down gives experience and loot, and the boss clears the stage.</summary>
        private void Hit(HeroState hero, MonsterState monster, int damage)
        {
            if (!monster.Alive) return;
            events.Add(new CombatEvent(CombatEventKind.Attack, true, (int)hero.Role, false, monster.Id, damage));
            monster.Hp = Math.Max(0, monster.Hp - damage);
            if (monster.Hp > 0) return;
            monster.Mood = MonsterMood.Defeated;
            log.Add($"{hero.Name} → {monster.Name} 격파 • 경험치 +{monster.Experience}");
            GainExperience(monster.Experience);
            DropLoot(monster);
            if (!monster.IsBoss) return;
            // The last boss ends the expedition; there is nothing left to share its loot for.
            if (Stage == 1) SettleSpoils();
            else spoils.Clear();
            ClearStage();
        }

        private void HeroStep(HeroState hero, TilePoint goal, int range)
        {
            if (hero.MoveReady > Ticks) return;
            var here = heroTiles[(int)hero.Role];
            var path = Map.FindPath(here, goal);
            if (path.Count < 2 || (path.Count == 2 && range <= 1)) return;
            heroTiles[(int)hero.Role] = path[1];
            hero.MoveReady = Ticks + HeroMoveTicks;
        }

        /// <summary>Back-liners step away from an adjacent monster when an open tile puts more room between them.</summary>
        private bool Retreat(HeroState hero, List<MonsterState> engaged)
        {
            var here = heroTiles[(int)hero.Role];
            int Room(TilePoint tile) => engaged.Min(m => Chebyshev(m.Tile, tile));
            int best = Room(here);
            TilePoint? choice = null;
            foreach (var step in new[] { new TilePoint(0, -1), new TilePoint(1, 0), new TilePoint(0, 1), new TilePoint(-1, 0) })
            {
                var candidate = new TilePoint(here.X + step.X, here.Y + step.Y);
                if (!Map.IsFloor(candidate) || Room(candidate) <= best) continue;
                best = Room(candidate);
                choice = candidate;
            }
            if (choice == null) return false;
            heroTiles[(int)hero.Role] = choice.Value;
            hero.MoveReady = Ticks + HeroMoveTicks;
            return true;
        }

        /// <summary>
        /// Heals everyone close by with mass heal when it fits, else the protected ally or the most hurt one.
        /// Returns true when healing took this hero's turn.
        /// </summary>
        private bool TryHeal(HeroState healer, HeroTactic tactic)
        {
            if (TryMassHeal(healer)) return true;
            HeroState patient = null;
            if (tactic != null && tactic.Protect >= 0 && tactic.Protect < party.Length)
            {
                var ward = party[tactic.Protect];
                if (ward.Hp > 0 && ward.MaxHp - ward.Hp >= HealThreshold) patient = ward;
            }
            if (patient == null)
            {
                foreach (var hero in party)
                {
                    if (hero.Hp == 0 || hero.MaxHp - hero.Hp < HealThreshold) continue;
                    if (patient == null || hero.Hp * patient.MaxHp < patient.Hp * hero.MaxHp) patient = hero;
                }
            }
            if (patient == null) return false;
            var here = heroTiles[(int)healer.Role];
            var there = heroTiles[(int)patient.Role];
            if (!InReach(here, there, healer.Range))
            {
                HeroStep(healer, there, healer.Range);
                return true;
            }
            if (healer.ActReady > Ticks) return true;
            healer.ActReady = Ticks + AttackCycle(healer);
            int before = patient.Hp;
            patient.Hp = Math.Min(patient.MaxHp, patient.Hp + HealAmount);
            if (patient.Hp * 10 >= patient.MaxHp * 3) patient.Critical = false;
            events.Add(new CombatEvent(CombatEventKind.Heal, true, (int)healer.Role, true, (int)patient.Role, patient.Hp - before));
            return true;
        }

        private void Hurt(HeroState target, int damage, string source, bool announce = true)
        {
            target.Hp = Math.Max(0, target.Hp - damage);
            if (announce) log.Add($"{source} → {target.Name} • {damage} 피해{(target.Hp == 0 ? " / 쓰러짐" : "")}");
            if (target.Hp == 0)
            {
                if (!announce) log.Add($"{source} → {target.Name} • 쓰러짐");
                target.LastLine = "";
                BumpSituation();
            }
            else if (!target.Critical && target.Hp * 10 < target.MaxHp * 3)
            {
                target.Critical = true;
                log.Add($"위기 • {target.Name}의 체력이 {target.Hp} / {target.MaxHp}까지 떨어졌습니다.");
                if (Phase == RunPhase.Combat) BumpSituation();
            }
            if (Array.TrueForAll(party, hero => hero.Hp == 0))
            {
                Phase = RunPhase.Defeat;
                pendingTrial = 0;
                PendingFork = null;
                PendingPrayer = null;
                spoils.Clear();
                log.Add("파티 전멸 • 원정이 끝났습니다.");
            }
        }

        /// <summary>
        /// Applies an AI plan for the current situation. Stale plans are refused; invalid entries are skipped so
        /// those heroes keep following the local rules.
        /// </summary>
        public bool ApplyTactics(int situation, IReadOnlyList<HeroTactic> plan)
        {
            if (situation != Situation || Phase != RunPhase.Combat || plan == null) return false;
            foreach (var tactic in plan)
            {
                if (tactic == null || (int)tactic.Role < 0 || (int)tactic.Role >= party.Length) continue;
                var hero = party[(int)tactic.Role];
                bool focusOk = tactic.Focus < 0 || monsters.Any(m => m.Id == tactic.Focus && m.Alive && m.Mood == MonsterMood.Chasing);
                bool protectOk = tactic.Protect < 0 || (tactic.Protect < party.Length && party[tactic.Protect].Hp > 0);
                if (hero.Hp == 0 || !focusOk || !protectOk || string.IsNullOrWhiteSpace(tactic.Line)) continue;
                tactics[(int)tactic.Role] = tactic;
                hero.LastLine = tactic.Line;
            }
            return true;
        }
    }
}
