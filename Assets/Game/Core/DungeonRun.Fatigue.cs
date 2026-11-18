using System;

namespace DungeonTycoon.Core
{
    /// <summary>How worn out the party is from wandering. Shared by the whole party.</summary>
    public enum FatigueStage { Rested, Tired, Exhausted }

    /// <summary>
    /// Travel fatigue: the price of getting lost. Only the leader's steps while exploring count, so fights, fork
    /// debates, adversities, loot sharing and waiting for a revive never tire the party. The thresholds come from
    /// the stage's shortest entrance-to-boss walk, so a party that heads straight for the boss stays rested and
    /// only one that wanders into dead ends pays for it. Nothing here rolls dice.
    /// </summary>
    public sealed partial class DungeonRun
    {
        /// <summary>The party tires past this many times the shortest walk to the boss. Balance value.</summary>
        public const double FatigueFirstMultiplier = 2.2;
        /// <summary>The party is exhausted past this many times the shortest walk to the boss. Balance value.</summary>
        public const double FatigueSecondMultiplier = 3.0;
        /// <summary>Attack every hero loses while tired or exhausted, combat included. Balance value.</summary>
        public const int TiredAttackPenalty = 2;
        /// <summary>An exhausted party loses health once every this many steps. Balance value.</summary>
        public const int ExhaustedDrainSteps = 5;
        /// <summary>Health each living hero loses per exhaustion drain; it never drops a hero below 1. Balance value.</summary>
        public const int ExhaustedDrain = 2;
        /// <summary>Steps of fatigue the GM's help takes away. Balance value.</summary>
        public const int FatigueRelief = 30;

        /// <summary>Steps the party leader has walked while exploring this stage.</summary>
        public int FatigueSteps { get; private set; }
        /// <summary>The stage's shortest walk from the entrance to the boss tile, in steps.</summary>
        public int ShortestRouteSteps => Map.WalkDistance(Map.BossTile);
        /// <summary>Above this many steps the party is tired.</summary>
        public int FatigueFirstThreshold => Threshold(FatigueFirstMultiplier);
        /// <summary>Above this many steps the party is exhausted.</summary>
        public int FatigueSecondThreshold => Threshold(FatigueSecondMultiplier);
        public FatigueStage Fatigue => StageAt(FatigueSteps);
        /// <summary>The GM can ease the party's fatigue now: on an expedition, with faith to spend, and the party tired at all.</summary>
        public bool CanRelieveFatigue => InDungeon && Faith >= BlessCost && FatigueSteps > 0;

        public static string FatigueName(FatigueStage stage)
        {
            switch (stage)
            {
                case FatigueStage.Rested: return "여유";
                case FatigueStage.Tired: return "지침";
                case FatigueStage.Exhausted: return "탈진";
                default: throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        /// <summary>Takes fatigue away for faith right away, even while paused or during a boss fight.</summary>
        public bool RelieveFatigue()
        {
            if (!CanRelieveFatigue) return false;
            var before = Fatigue;
            int relieved = Math.Min(FatigueRelief, FatigueSteps);
            Faith -= BlessCost;
            FatigueSteps -= relieved;
            log.Add($"신의 휴식 • 파티 피로 -{relieved} / 신앙 -{BlessCost}");
            LogFatigueChange(before);
            return true;
        }

        /// <summary>Takes fatigue away without spending faith. The rest miracle pays for itself.</summary>
        internal void ReduceFatigue(int steps)
        {
            if (steps <= 0) return;
            var before = Fatigue;
            FatigueSteps = Math.Max(0, FatigueSteps - steps);
            LogFatigueChange(before);
        }

        /// <summary>Attack the current fatigue takes off every hero's blow.</summary>
        private int FatigueAttackPenalty => Fatigue == FatigueStage.Rested ? 0 : TiredAttackPenalty;

        private int Threshold(double multiplier) =>
            (int)Math.Round(ShortestRouteSteps * multiplier, MidpointRounding.AwayFromZero);

        private FatigueStage StageAt(int steps) =>
            steps > FatigueSecondThreshold ? FatigueStage.Exhausted :
            steps > FatigueFirstThreshold ? FatigueStage.Tired : FatigueStage.Rested;

        private void ResetFatigue() => FatigueSteps = 0;

        /// <summary>Tests start the party at a chosen fatigue without walking there.</summary>
        internal void SetFatigueSteps(int steps) => FatigueSteps = Math.Max(0, steps);

        /// <summary>The leader walked one tile while exploring; an exhausted party pays for every few of them.</summary>
        private void TireFromStep()
        {
            var before = Fatigue;
            FatigueSteps++;
            LogFatigueChange(before);
            if (Fatigue != FatigueStage.Exhausted || (FatigueSteps - FatigueSecondThreshold) % ExhaustedDrainSteps != 0) return;
            bool drained = false;
            foreach (var hero in party)
            {
                int loss = Math.Min(ExhaustedDrain, hero.Hp - 1);
                if (hero.Hp == 0 || loss <= 0) continue;
                drained = true;
                Hurt(hero, loss, "탈진", false);
            }
            if (drained) log.Add($"탈진 • 지친 용사들의 체력이 {ExhaustedDrain}씩 깎였습니다.");
        }

        private void LogFatigueChange(FatigueStage before)
        {
            var now = Fatigue;
            if (now == before) return;
            switch (now)
            {
                case FatigueStage.Exhausted:
                    log.Add($"피로 • 탈진! 공격력 -{TiredAttackPenalty}, 걸을 때마다 체력이 깎입니다.");
                    break;
                case FatigueStage.Tired:
                    log.Add(before == FatigueStage.Rested
                        ? $"피로 • 파티가 지쳤다 — 공격력 -{TiredAttackPenalty}"
                        : $"피로 • 숨을 돌렸다 — 지침 단계로 내려왔습니다.");
                    break;
                default:
                    log.Add("피로 • 파티가 기운을 되찾았다 — 여유");
                    break;
            }
        }
    }
}
