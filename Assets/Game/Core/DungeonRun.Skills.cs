using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// Hero skills. Each fires on its own when its condition holds and its cooldown is over; tactics never order
    /// one, but a skill with a target keeps to the hero's focus. Nothing here rolls dice.
    /// </summary>
    public sealed partial class DungeonRun
    {
        /// <summary>Monsters this many tiles (Chebyshev) from the tank are taunted. Balance value.</summary>
        public const int TauntRadius = 3;
        /// <summary>Ticks a taunted monster keeps attacking the tank. Balance value.</summary>
        public const int TauntTicks = 30;
        /// <summary>Balance value.</summary>
        public const int TauntCooldownTicks = 60;

        /// <summary>Heroes this many tiles (Chebyshev) from the tank, the tank included, are covered by the shield wall. Balance value.</summary>
        public const int ShieldWallRadius = 2;
        /// <summary>Ticks the shield wall stands. Balance value.</summary>
        public const int ShieldWallTicks = 40;
        /// <summary>Balance value.</summary>
        public const int ShieldWallCooldownTicks = 120;
        /// <summary>Share of a monster's blow the shield wall takes away, in percent, rounded in the heroes' favour. Balance value.</summary>
        public const int ShieldWallReductionPercent = 50;

        /// <summary>A power strike deals this percentage of the normal hit. Balance value.</summary>
        public const int PowerStrikeDamagePercent = 200;
        /// <summary>Balance value.</summary>
        public const int PowerStrikeCooldownTicks = 60;

        /// <summary>Balance value.</summary>
        public const int WhirlwindCooldownTicks = 60;

        /// <summary>Balance value.</summary>
        public const int PiercingShotCooldownTicks = 45;

        /// <summary>Ticks rapid fire halves the attack cycle. Balance value.</summary>
        public const int RapidFireTicks = 45;
        /// <summary>Counted from the moment rapid fire starts. Balance value.</summary>
        public const int RapidFireCooldownTicks = 150;

        /// <summary>Allies this many tiles (Chebyshev) from the healer, the healer included, are healed. Balance value.</summary>
        public const int MassHealRadius = 3;
        /// <summary>Each ally gets this percentage of a single heal. Balance value.</summary>
        public const int MassHealPercent = 75;
        /// <summary>Balance value.</summary>
        public const int MassHealCooldownTicks = 90;

        /// <summary>A revived hero stands up with this percentage of their maximum health. Balance value.</summary>
        public const int ReviveHpPercent = 40;

        /// <summary>The healer has raised a fallen hero this stage; revive opens again when the stage is cleared.</summary>
        public bool ReviveUsed { get; private set; }

        /// <summary>Skill <paramref name="index"/> (0 starting, 1 unlocked) of the role can be used at the party's level.</summary>
        public bool SkillUnlocked(HeroRole role, int index)
        {
            if (index < 0 || index > 1) throw new ArgumentOutOfRangeException(nameof(index));
            return index == 0 || Level >= HeroSkills.UnlockLevel;
        }

        /// <summary>
        /// Ticks until the skill can fire again, 0 when ready. Revive has no cooldown; it is spent for the stage
        /// once <see cref="ReviveUsed"/> is true.
        /// </summary>
        public int SkillCooldownLeft(HeroRole role, int index)
        {
            if (index < 0 || index > 1) throw new ArgumentOutOfRangeException(nameof(index));
            return Math.Max(0, party[(int)role].SkillReady[index] - Ticks);
        }

        /// <summary>The full cooldown of a skill in ticks. Revive is limited per stage instead and returns 0.</summary>
        public static int SkillCooldownTicks(HeroSkill skill)
        {
            switch (skill)
            {
                case HeroSkill.Taunt: return TauntCooldownTicks;
                case HeroSkill.ShieldWall: return ShieldWallCooldownTicks;
                case HeroSkill.PowerStrike: return PowerStrikeCooldownTicks;
                case HeroSkill.Whirlwind: return WhirlwindCooldownTicks;
                case HeroSkill.PiercingShot: return PiercingShotCooldownTicks;
                case HeroSkill.RapidFire: return RapidFireCooldownTicks;
                case HeroSkill.MassHeal: return MassHealCooldownTicks;
                case HeroSkill.Revive: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(skill));
            }
        }

        private void ResetSkills()
        {
            foreach (var hero in party)
            {
                hero.SkillReady[0] = hero.SkillReady[1] = 0;
                hero.RapidFireUntil = hero.ShieldWallUntil = 0;
            }
        }

        private bool SkillReady(HeroState hero, int index) =>
            SkillUnlocked(hero.Role, index) && hero.SkillReady[index] <= Ticks;

        /// <summary>Starts the cooldown and announces the skill on the board, the hero and the log.</summary>
        private void UseSkill(HeroState hero, int index, bool toHero, int target, string shout, string detail)
        {
            var skill = HeroSkills.Of(hero.Role, index);
            hero.SkillReady[index] = Ticks + SkillCooldownTicks(skill);
            hero.LastLine = shout;
            events.Add(new CombatEvent(CombatEventKind.Skill, true, (int)hero.Role, toHero, target, (int)skill));
            log.Add($"{hero.Name} • {HeroSkills.Name(skill)}! {detail}");
        }

        /// <summary>Ticks until this hero acts again after an attack or heal now.</summary>
        private int AttackCycle(HeroState hero) =>
            hero.RapidFireUntil > Ticks ? Math.Max(1, hero.AttackTicks / 2) : hero.AttackTicks;

        // ── Tank ─────────────────────────────────────────────────────────

        /// <summary>The tank's skills take no turn: it raises the wall, taunts, and still fights.</summary>
        private void TankSkills(HeroState tank, List<MonsterState> engaged)
        {
            TryShieldWall(tank, engaged);
            TryTaunt(tank, engaged);
        }

        private void TryTaunt(HeroState tank, List<MonsterState> engaged)
        {
            if (!SkillReady(tank, 0)) return;
            var here = heroTiles[(int)tank.Role];
            var near = engaged.Where(m => Chebyshev(m.Tile, here) <= TauntRadius).ToList();
            var loose = near.Where(m => m.TauntedUntil <= Ticks).ToList();
            // Taunt when a monster goes after someone else, or before a pack can spread its blows.
            if (!loose.Any(m => MonsterTarget(m) != tank) && loose.Count < 2) return;
            UseSkill(tank, 0, false, near[0].Id, "이쪽이다! 덤벼라!",
                $"{string.Join(", ", near.Select(m => m.Name))}의 시선을 끕니다.");
            foreach (var monster in near)
            {
                monster.TauntedUntil = Ticks + TauntTicks;
                events.Add(new CombatEvent(CombatEventKind.Taunt, true, (int)tank.Role, false, monster.Id, 0));
            }
        }

        private void TryShieldWall(HeroState tank, List<MonsterState> engaged)
        {
            if (!SkillReady(tank, 1)) return;
            var here = heroTiles[(int)tank.Role];
            var targeted = new HashSet<HeroRole>();
            foreach (var monster in engaged)
            {
                var target = MonsterTarget(monster);
                if (target != null && Chebyshev(heroTiles[(int)target.Role], here) <= ShieldWallRadius) targeted.Add(target.Role);
            }
            if (targeted.Count < 2) return;
            tank.ShieldWallUntil = Ticks + ShieldWallTicks;
            UseSkill(tank, 1, false, -1, "방패 뒤로 모여!", "주변 동료들이 받는 피해가 줄어듭니다.");
        }

        /// <summary>A monster's blow after the shield wall, if the target stands behind it.</summary>
        private int Shielded(HeroState target, int damage)
        {
            var tank = party[(int)HeroRole.Tank];
            if (tank.Hp == 0 || tank.ShieldWallUntil <= Ticks) return damage;
            if (Chebyshev(heroTiles[(int)target.Role], heroTiles[(int)tank.Role]) > ShieldWallRadius) return damage;
            return Math.Max(0, damage * (100 - ShieldWallReductionPercent) / 100);
        }

        // ── Damage dealers ───────────────────────────────────────────────

        /// <summary>The hero attacks the target it is in reach of, with a skill when one fits.</summary>
        private void Attack(HeroState hero, MonsterState target, List<MonsterState> engaged)
        {
            if (hero.Role == HeroRole.RangedDamage)
            {
                TryRapidFire(hero, target);
                if (TryPiercingShot(hero, target, engaged)) return;
            }
            if (hero.Role == HeroRole.MeleeDamage && TryPowerStrike(hero, target)) return;
            Strike(hero, target);
        }

        private bool TryPowerStrike(HeroState hero, MonsterState target)
        {
            if (!SkillReady(hero, 0)) return false;
            int damage = HitDamage(hero, target) * PowerStrikeDamagePercent / 100;
            hero.ActReady = Ticks + AttackCycle(hero);
            UseSkill(hero, 0, false, target.Id, "이걸로 끝이다!", $"{target.Name}에게 {damage} 피해");
            Hit(hero, target, damage);
            return true;
        }

        /// <summary>With two or more monsters next to the melee hero, one swing hits them all. Takes the hero's turn when it fires.</summary>
        private bool TryWhirlwind(HeroState hero, List<MonsterState> engaged)
        {
            if (hero.ActReady > Ticks || !SkillReady(hero, 1)) return false;
            var here = heroTiles[(int)hero.Role];
            var adjacent = engaged.Where(m => InReach(here, m.Tile, 1)).OrderBy(m => m.Id).ToList();
            if (adjacent.Count < 2) return false;
            var tactic = tactics[(int)hero.Role];
            var main = adjacent.FirstOrDefault(m => tactic != null && m.Id == tactic.Focus) ?? adjacent[0];
            hero.ActReady = Ticks + AttackCycle(hero);
            UseSkill(hero, 1, false, main.Id, "다 덤벼라!", HeroTraits.Particle(string.Join(", ", adjacent.Select(m => m.Name)), "을", "를") + " 한꺼번에 벱니다.");
            foreach (var monster in adjacent)
            {
                if (Phase != RunPhase.Combat) break;
                Hit(hero, monster, HitDamage(hero, monster));
            }
            return true;
        }

        private void TryRapidFire(HeroState hero, MonsterState target)
        {
            if (!SkillReady(hero, 1)) return;
            hero.RapidFireUntil = Ticks + RapidFireTicks;
            UseSkill(hero, 1, false, target.Id, "연사로 간다!", "잠시 공격 주기가 절반이 됩니다.");
        }

        /// <summary>
        /// The arrow goes through the target into the monsters behind it. The hero's own target comes first; only
        /// a hero without a focus looks for another line to shoot down.
        /// </summary>
        private bool TryPiercingShot(HeroState hero, MonsterState target, List<MonsterState> engaged)
        {
            if (!SkillReady(hero, 0)) return false;
            var here = heroTiles[(int)hero.Role];
            var tactic = tactics[(int)hero.Role];
            var candidates = new List<MonsterState> { target };
            if (tactic == null || tactic.Focus != target.Id)
                candidates.AddRange(engaged.Where(m => m != target && InReach(here, m.Tile, hero.Range)).OrderBy(m => m.Id));
            foreach (var main in candidates)
            {
                var line = PierceLine(here, main, engaged, hero.Range);
                if (line.Count < 2) continue;
                hero.ActReady = Ticks + AttackCycle(hero);
                UseSkill(hero, 0, false, main.Id, "한 줄로 꿰뚫는다!", HeroTraits.Particle(string.Join(", ", line.Select(m => m.Name)), "을", "를") + " 꿰뚫습니다.");
                foreach (var monster in line)
                {
                    if (Phase != RunPhase.Combat) break;
                    Hit(hero, monster, HitDamage(hero, monster));
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// The target, then engaged monsters in reach past it on the same line (within half a tile of the line
        /// from the shooter through the target), nearest first.
        /// </summary>
        private List<MonsterState> PierceLine(TilePoint from, MonsterState target, List<MonsterState> engaged, int range)
        {
            var line = new List<MonsterState> { target };
            int tx = target.Tile.X - from.X, ty = target.Tile.Y - from.Y;
            int length = tx * tx + ty * ty;
            if (length == 0) return line;
            var behind = new List<(MonsterState monster, int distance)>();
            foreach (var monster in engaged)
            {
                if (monster == target) continue;
                int mx = monster.Tile.X - from.X, my = monster.Tile.Y - from.Y;
                if (tx * mx + ty * my <= length) continue;
                int cross = tx * my - ty * mx;
                if (4 * cross * cross > length || !InReach(from, monster.Tile, range)) continue;
                behind.Add((monster, mx * mx + my * my));
            }
            line.AddRange(behind.OrderBy(b => b.distance).ThenBy(b => b.monster.Id).Select(b => b.monster));
            return line;
        }

        // ── Healer ───────────────────────────────────────────────────────

        private int HealAmount => 7 + Level;

        private static bool Wounded(HeroState hero) => hero.Hp > 0 && hero.MaxHp - hero.Hp >= HealThreshold;

        /// <summary>Heals everyone close by when two or more of them are hurt. Takes the healer's turn when it fires.</summary>
        private bool TryMassHeal(HeroState healer)
        {
            if (healer.ActReady > Ticks || !SkillReady(healer, 0)) return false;
            var here = heroTiles[(int)healer.Role];
            var near = MarchOrder.Select(role => party[(int)role])
                .Where(hero => hero.Hp > 0 && Chebyshev(heroTiles[(int)hero.Role], here) <= MassHealRadius).ToList();
            if (near.Count(Wounded) < 2) return false;
            healer.ActReady = Ticks + AttackCycle(healer);
            UseSkill(healer, 0, false, -1, "모두 버텨요!", $"{string.Join(", ", near.Select(h => h.Name))}의 상처를 함께 돌봅니다.");
            int amount = HealAmount * MassHealPercent / 100;
            foreach (var hero in near)
            {
                int before = hero.Hp;
                hero.Hp = Math.Min(hero.MaxHp, hero.Hp + amount);
                if (hero.Hp * 10 >= hero.MaxHp * 3) hero.Critical = false;
                if (hero.Hp > before)
                    events.Add(new CombatEvent(CombatEventKind.Heal, true, (int)healer.Role, true, (int)hero.Role, hero.Hp - before));
            }
            return true;
        }

        /// <summary>
        /// Goes to the first fallen hero in marching order and raises them once per stage. Returns true while the
        /// revive takes the healer's turn: walking over, waiting for the next action, or raising.
        /// </summary>
        private bool TryRevive(HeroState healer)
        {
            if (healer.Hp == 0 || ReviveUsed || !SkillUnlocked(healer.Role, 1)) return false;
            var fallen = MarchOrder.Select(role => party[(int)role]).FirstOrDefault(hero => hero.Hp == 0);
            if (fallen == null) return false;
            var here = heroTiles[(int)healer.Role];
            var there = heroTiles[(int)fallen.Role];
            if (!InReach(here, there, healer.Range))
            {
                HeroStep(healer, there, healer.Range);
                return true;
            }
            if (healer.ActReady > Ticks) return true;
            healer.ActReady = Ticks + AttackCycle(healer);
            ReviveUsed = true;
            fallen.Hp = Math.Max(1, fallen.MaxHp * ReviveHpPercent / 100);
            fallen.Critical = fallen.Hp * 10 < fallen.MaxHp * 3;
            fallen.MoveReady = fallen.ActReady = Ticks;
            UseSkill(healer, 1, true, (int)fallen.Role, "일어나요, 아직 끝나지 않았어요!",
                $"{HeroTraits.Subject(fallen.Name)} 체력 {fallen.Hp} / {fallen.MaxHp}로 다시 일어섭니다.");
            events.Add(new CombatEvent(CombatEventKind.Heal, true, (int)healer.Role, true, (int)fallen.Role, fallen.Hp));
            fallen.LastLine = "고마워, 다시 싸울 수 있어!";
            if (Phase == RunPhase.Combat) BumpSituation();
            else Regroup();
            return true;
        }

        /// <summary>Between fights the party waits while the healer walks back to raise a fallen hero.</summary>
        private bool ReviveOnTheMarch() => TryRevive(party[(int)HeroRole.Healer]);

        // ── Test seams ───────────────────────────────────────────────────

        /// <summary>Tests grow the party without a fight.</summary>
        internal void GrantExperience(int amount) => GainExperience(amount);

        /// <summary>Tests stand a hero on a chosen tile.</summary>
        internal void PlaceHero(HeroRole role, TilePoint tile) => heroTiles[(int)role] = tile;
    }
}
