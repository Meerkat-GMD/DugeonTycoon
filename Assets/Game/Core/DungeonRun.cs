using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>
    /// One expedition. Time advances in fixed ticks of <see cref="TickSeconds"/>; the game speed only
    /// changes how many ticks run per real second, never what a tick does.
    /// </summary>
    public sealed partial class DungeonRun
    {
        public const double TickSeconds = .1;
        public const int BlessCost = 1;
        /// <summary>Tiles this many steps or fewer from the entrance cannot hold monsters. Balance value.</summary>
        public const int EntranceClearance = 4;
        private static readonly HeroRole[] MarchOrder =
            { HeroRole.Tank, HeroRole.MeleeDamage, HeroRole.Healer, HeroRole.RangedDamage };

        private readonly List<MonsterPlacement> placements = new List<MonsterPlacement>();
        private readonly List<MonsterState> monsters = new List<MonsterState>();
        private readonly List<string> log = new List<string>();
        private readonly HeroState[] party =
        {
            new HeroState(HeroRole.Tank, "가론", 46, 5, 2, 1, new[] { 3, 0, 1, 1 }, PersonalityTag.Cautious),
            new HeroState(HeroRole.Healer, "세레나", 28, 3, 0, 4, new[] { 0, 1, 2, 2 }, PersonalityTag.Cautious, PersonalityTag.Curious),
            new HeroState(HeroRole.MeleeDamage, "카이", 36, 8, 0, 1, new[] { 3, 2, -1, 0 }, PersonalityTag.Reckless),
            new HeroState(HeroRole.RangedDamage, "리아", 32, 7, 0, 4, new[] { 0, 2, 1, 1 }, PersonalityTag.Curious)
        };
        private readonly Random dice;
        /// <summary>
        /// Drops and loot rolls have their own seeded dice, so adding loot never shifts the rolls that decide
        /// wandering, forks and adversities.
        /// </summary>
        private readonly Random lootDice;
        private int nextTrialToken;
        private int pendingTrial;

        public DungeonRun(int seed = 20261118)
        {
            dice = new Random(seed);
            lootDice = new Random(unchecked(seed * 31 + 7));
            Placements = placements.AsReadOnly();
            Monsters = monsters.AsReadOnly();
            Party = Array.AsReadOnly(party);
            Log = log.AsReadOnly();
            LoadMap(DungeonMap.ForStage(1));
            log.Add("첫 원정 • 성문으로 가는 던전에 몬스터를 배치하세요.");
        }

        public RunPhase Phase { get; private set; } = RunPhase.Planning;
        public int Stage { get; private set; } = 1;
        public DungeonMap Map { get; private set; }
        /// <summary>Monsters in the order the GM placed them.</summary>
        public IReadOnlyList<MonsterPlacement> Placements { get; }
        /// <summary>Live monsters after departure: one per placement in the same order, then the boss.</summary>
        public IReadOnlyList<MonsterState> Monsters { get; }
        public int BossIndex => placements.Count;
        public int Ticks { get; private set; }
        public int Budget { get; private set; } = 6;
        public int Faith { get; private set; } = 1;
        public int TrialsLeft { get; private set; } = 2;
        public int Experience { get; private set; }
        /// <summary>Experience per party level. Balance value.</summary>
        public const int ExperiencePerLevel = 5;
        public int Level => 1 + Experience / ExperiencePerLevel;
        /// <summary>Experience gathered toward the next level, from 0 up to ExperiencePerLevel - 1.</summary>
        public int ExperienceIntoLevel => Experience % ExperiencePerLevel;
        /// <summary>This stage's boss as it waits in its room. Before departure there is no live Boss yet.</summary>
        public string BossName => Boss?.Name ?? CreateBoss().Name;
        public int BossMaxHp => Boss?.MaxHp ?? CreateBoss().MaxHp;
        public bool PendingAdversity => pendingTrial != 0;
        public int TrialDifficulty => Stage == 1 ? 13 : 15;
        public string LastTrial { get; private set; } = "";
        public IReadOnlyList<HeroState> Party { get; }
        public IReadOnlyList<string> Log { get; }
        private bool InDungeon => Phase == RunPhase.Travel || Phase == RunPhase.Combat;
        public bool CanBless => InDungeon && Faith >= BlessCost && WoundedHero() != null;

        public static int Cost(MonsterKind kind)
        {
            switch (kind)
            {
                case MonsterKind.None: return 0;
                case MonsterKind.Goblin: return 1;
                case MonsterKind.Ogre: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static string MonsterName(MonsterKind kind)
        {
            switch (kind)
            {
                case MonsterKind.None: return "빈 길";
                case MonsterKind.Goblin: return "고블린";
                case MonsterKind.Ogre: return "오우거";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        // ── Planning ─────────────────────────────────────────────────────

        public int CountAt(TilePoint tile)
        {
            int count = 0;
            foreach (var placement in placements)
                if (placement.Tile == tile) count++;
            return count;
        }

        /// <summary>Floor tile far enough from the entrance; ignores phase and budget.</summary>
        public bool CanHoldMonster(TilePoint tile) =>
            Map.IsFloor(tile) && Map.WalkDistance(tile) > EntranceClearance;

        public PlacementCheck CheckPlace(TilePoint tile, MonsterKind kind)
        {
            if (Phase != RunPhase.Planning) return PlacementCheck.Locked;
            if (kind != MonsterKind.Goblin && kind != MonsterKind.Ogre) return PlacementCheck.InvalidKind;
            if (!Map.IsFloor(tile)) return PlacementCheck.NotFloor;
            if (!CanHoldMonster(tile)) return PlacementCheck.NearEntrance;
            if (Cost(kind) > Budget) return PlacementCheck.OverBudget;
            return PlacementCheck.Allowed;
        }

        public bool Place(TilePoint tile, MonsterKind kind)
        {
            if (CheckPlace(tile, kind) != PlacementCheck.Allowed) return false;
            placements.Add(new MonsterPlacement(tile, kind));
            Budget -= Cost(kind);
            return true;
        }

        public PlacementCheck CheckRemove(TilePoint tile)
        {
            if (Phase != RunPhase.Planning) return PlacementCheck.Locked;
            return CountAt(tile) == 0 ? PlacementCheck.NothingToRemove : PlacementCheck.Allowed;
        }

        /// <summary>Takes back the most recently placed monster on the tile and refunds its cost.</summary>
        public bool Remove(TilePoint tile)
        {
            if (CheckRemove(tile) != PlacementCheck.Allowed) return false;
            int index = placements.FindLastIndex(placement => placement.Tile == tile);
            Budget += Cost(placements[index].Kind);
            placements.RemoveAt(index);
            return true;
        }

        public bool Start()
        {
            if (Phase != RunPhase.Planning || PendingLoot != null) return false;
            monsters.Clear();
            for (int i = 0; i < placements.Count; i++) monsters.Add(CreateMonster(i, placements[i].Kind, placements[i].Tile));
            monsters.Add(CreateBoss());
            foreach (var monster in monsters) monster.WanderWait = 5 + dice.Next(0, 20);
            foreach (var hero in party) hero.MoveReady = hero.ActReady = 0;
            ResetSkills();
            ResetFatigue();
            ResetPrayer();
            Phase = RunPhase.Travel;
            log.Add($"{Stage} 스테이지 출발 • 용사들이 던전에 들어섰습니다.");
            Reveal();
            return true;
        }

        private void LoadMap(DungeonMap map)
        {
            Map = map;
            ResetExploration();
            ResetFatigue();
        }

        /// <summary>
        /// Advances the expedition by one tick: monsters notice heroes, heroes explore or fight, monsters act.
        /// Nothing moves while an adversity is being resolved, the party is debating at a fork, sharing out armour
        /// or waiting on a prayer. A hero who despairs during the tick is asked to pray at its end.
        /// </summary>
        public bool Tick()
        {
            if (!InDungeon || PendingAdversity || PendingFork != null || PendingLoot != null || PendingPrayer != null) return false;
            Ticks++;
            foreach (var monster in monsters) Perceive(monster);
            UpdateEngagement();
            if (Phase == RunPhase.Combat)
            {
                foreach (var role in MarchOrder)
                    if (Phase == RunPhase.Combat && party[(int)role].Hp > 0) HeroCombatTick(party[(int)role]);
            }
            else if (Phase == RunPhase.Travel && !ReviveOnTheMarch()) ExploreTick();
            if (!InDungeon) return true;
            Reveal();
            foreach (var monster in monsters)
            {
                if (!InDungeon) return true;
                MonsterTick(monster);
            }
            if (InDungeon) UpdateEngagement();
            CheckDesperation();
            return true;
        }

        // ── Interventions ────────────────────────────────────────────────

        /// <summary>The GM can bless this hero now: on an expedition, with faith to spend, and the hero alive and hurt.</summary>
        public bool CanBlessHero(HeroRole role)
        {
            int index = (int)role;
            if (!InDungeon || Faith < BlessCost || index < 0 || index >= party.Length) return false;
            var hero = party[index];
            return hero.Hp > 0 && hero.Hp < hero.MaxHp;
        }

        /// <summary>Blesses the most hurt hero. A convenience for simulations; the GM picks a hero with <see cref="Bless(HeroRole)"/>.</summary>
        public bool Bless()
        {
            var target = WoundedHero();
            return target != null && Bless(target.Role);
        }

        /// <summary>Restores the chosen hero right away, even while paused or during a boss fight.</summary>
        public bool Bless(HeroRole role)
        {
            if (!CanBlessHero(role)) return false;
            var target = party[(int)role];
            int restored = Math.Min(target.MaxHp - target.Hp, 12 + Level);
            Faith -= BlessCost;
            target.Hp += restored;
            if (target.Hp * 10 >= target.MaxHp * 3) target.Critical = false;
            log.Add($"신의 축복 • {target.Name} 체력 +{restored} / 신앙 -{BlessCost}");
            return true;
        }

        public int BeginAdversity()
        {
            if (!InDungeon || PendingAdversity || PendingLoot != null || PendingPrayer != null || TrialsLeft == 0) return 0;
            TrialsLeft--;
            pendingTrial = ++nextTrialToken;
            log.Add("낙석! • 용사들이 대응을 선언합니다.");
            return pendingTrial;
        }

        public int TrialModifier(HeroRole role) =>
            party[(int)role].Modifier(HeroTraits.TrialAbility(role)) + (Level - 1) / 2;

        /// <summary>
        /// Rolls the d20 for the pending adversity with the run's seeded dice and settles it, so the same seed and
        /// the same intervention timing always give the same face.
        /// </summary>
        public bool RollAdversity(int token, HeroRole role, string declaration = null)
        {
            if (token == 0 || token != pendingTrial || !InDungeon) return false;
            return ResolveAdversity(token, role, dice.Next(1, 21), declaration);
        }

        /// <summary>Settles the pending adversity with a given face. Tests use it to force a roll.</summary>
        public bool ResolveAdversity(int token, HeroRole role, int roll, string declaration = null)
        {
            if (token == 0 || token != pendingTrial || !InDungeon) return false;
            int index = (int)role;
            var actor = index >= 0 && index < party.Length && party[index].Hp > 0
                ? party[index] : Array.Find(party, hero => hero.Hp > 0);
            int modifier = TrialModifier(actor.Role);
            bool success = D20Check.Succeeds(roll, modifier, TrialDifficulty);
            pendingTrial = 0;
            string ability = HeroTraits.Name(HeroTraits.TrialAbility(actor.Role));
            string fallback;
            switch (actor.Role)
            {
                case HeroRole.Tank: fallback = "방패로 낙석을 막겠습니다."; break;
                case HeroRole.Healer: fallback = "안전한 틈을 찾아 안내할게요."; break;
                case HeroRole.MeleeDamage: fallback = "바위를 쳐내겠다!"; break;
                default: fallback = "바위가 떨어지기 전에 피하자!"; break;
            }
            actor.LastLine = string.IsNullOrWhiteSpace(declaration) ? fallback : declaration;
            string outcome = success ? "성공 • 경험치 +1 / 신앙 +1" : $"실패 • 체력 -{TrialDamage}";
            LastTrial = $"{actor.Name} “{actor.LastLine}”\nd20 [{roll}] + {ability} {modifier} = {roll + modifier} / 난이도 {TrialDifficulty} → {outcome}";
            log.Add(LastTrial);
            if (success)
            {
                Faith++;
                GainExperience(1);
            }
            else Hurt(actor, TrialDamage, "낙석");

            var scene = new List<DialogueBeat>
            {
                DialogueBeat.Line(actor.Role, actor.LastLine),
                DialogueBeat.Check(DialogueLines.TrialTitle(ability), new DiceRoll(actor.Role, roll, modifier, ability),
                    TrialDifficulty, success),
                DialogueBeat.Narration(success ? "성공 — 경험치 +1, 신앙 +1" :
                    actor.Hp == 0 ? $"실패 — {HeroTraits.Subject(actor.Name)} 피해 {TrialDamage}을 입고 쓰러졌다." :
                    $"실패 — {HeroTraits.Subject(actor.Name)} 피해 {TrialDamage}을 입었다.")
            };
            if (actor.Hp > 0)
            {
                string reaction = DialogueLines.Reaction(actor.Role, success);
                scene.Add(DialogueBeat.Line(actor.Role, reaction));
                log.Add($"{actor.Name} “{reaction}”");
            }
            LastScene = scene.AsReadOnly();
            return true;
        }

        /// <summary>Damage a failed adversity deals to the hero who answered it. Balance value.</summary>
        public const int TrialDamage = 8;

        /// <summary>
        /// The beats of the latest fork debate, adversity or loot sharing, in the order the stage shows them. The outcome they
        /// show is already applied to the run.
        /// </summary>
        public IReadOnlyList<DialogueBeat> LastScene { get; private set; } = new DialogueBeat[0];

        private HeroState WoundedHero()
        {
            HeroState lowest = null;
            foreach (var hero in party)
            {
                if (hero.Hp == 0 || hero.Hp == hero.MaxHp) continue;
                if (lowest == null || hero.Hp * lowest.MaxHp < lowest.Hp * hero.MaxHp)
                    lowest = hero;
            }
            return lowest;
        }

        private void GainExperience(int amount)
        {
            int previous = Level;
            Experience += amount;
            int gain = Level - previous;
            if (gain == 0) return;
            foreach (var hero in party)
            {
                hero.MaxHp += 3 * gain;
                if (hero.Hp > 0) hero.Hp += 3 * gain;
            }
            log.Add($"파티 레벨 {Level} • 공격력 +{2 * gain} / 최대 체력 +{3 * gain}");
        }

        private void ClearStage()
        {
            if (Stage == 2)
            {
                Phase = RunPhase.Victory;
                log.Add("마왕 토벌 • 살아남은 용사들이 왕국으로 귀환합니다!");
                return;
            }
            Stage++;
            ReviveUsed = false;
            ResetPrayer();
            placements.Clear();
            monsters.Clear();
            LoadMap(DungeonMap.ForStage(Stage));
            Budget = 7;
            TrialsLeft = 2;
            foreach (var hero in party)
            {
                hero.Hp = hero.MaxHp;
                hero.Critical = false;
            }
            Phase = RunPhase.Planning;
            log.Add("야영 완료 • 용사들이 회복하고 쓰러진 동료도 복귀했습니다. 마왕성을 준비하세요.");
        }
    }
}
