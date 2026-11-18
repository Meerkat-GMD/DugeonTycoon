using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    [DisallowMultipleComponent]
    public sealed class DungeonGame : MonoBehaviour
    {
        private const float TickSeconds = (float)DungeonRun.TickSeconds;
        private const int MaxTicksPerFrame = 5;
        private const float SpotlightSeconds = 7f;
        private readonly bool[] living = new bool[4];
        private readonly TilePoint[] heroTiles = new TilePoint[4];
        private PartyDirector director;
        private CancellationToken lifetime;
        private int fogVersion = -1;
        private float accumulator;
        private int discussedFork;
        private int plannedSituation;
        private int thinkers;

        public DungeonRun Run { get; private set; }
        public DungeonBoard Board { get; private set; }
        public string AiMode => director.ModeLabel;
        public bool Thinking => thinkers > 0;
        public int LastRoll { get; private set; }
        public string TrialAnnouncement { get; private set; } = "";
        /// <summary>The latest fork result or adversity, shown for a few seconds in the intervention panel.</summary>
        public string SpotlightTitle { get; private set; } = "";
        public string SpotlightText { get; private set; } = "";
        public float SpotlightUntil { get; private set; }
        /// <summary>0 when paused, otherwise 1 or 2. Speed only changes how many ticks run per second.</summary>
        public int Speed { get; private set; } = 1;
        /// <summary>The hero the GM picked as the target of help, or -1.</summary>
        public int SelectedHero { get; private set; } = -1;
        private int resumeSpeed = 1;

        private void Awake()
        {
            lifetime = destroyCancellationToken;
            director = new PartyDirector();
            Run = new DungeonRun(Environment.TickCount);
            Board = gameObject.AddComponent<DungeonBoard>();
            Board.Initialize();
            SetSpeed(1);
            RefreshLayout();
            gameObject.AddComponent<DungeonHud>();
        }

        public void SetSpeed(int speed)
        {
            Speed = Mathf.Clamp(speed, 0, 2);
            if (Speed > 0) resumeSpeed = Speed;
            // Figures walk faster at 2x so they keep up with the ticks; while paused they only finish their last step.
            Board.WalkSpeed = DungeonBoard.Tile/(TickSeconds*DungeonRun.HeroMoveTicks)*Mathf.Max(1, Speed);
        }

        public void TogglePause() => SetSpeed(Speed == 0 ? resumeSpeed : 0);

        public void SelectHero(int hero)
        {
            SelectedHero = hero >= 0 && hero < Run.Party.Count && Run.Party[hero].Hp > 0 ? hero : -1;
            Board.SetSelectedHero(SelectedHero);
        }

        public PlacementCheck Place(TilePoint tile, MonsterKind kind)
        {
            var check = kind == MonsterKind.None ? Run.CheckRemove(tile) : Run.CheckPlace(tile, kind);
            if (check != PlacementCheck.Allowed) return check;
            if (kind == MonsterKind.None) Run.Remove(tile);
            else Run.Place(tile, kind);
            RefreshLayout();
            return check;
        }

        public void Depart()
        {
            if (!Run.Start()) return;
            accumulator = 0;
            SetSpeed(1);
            discussedFork = 0;
            plannedSituation = Run.Situation;
            RefreshLayout();
            Board.SetFollowParty(true);
        }

        private static bool InDungeon(DungeonRun run) => run.Phase == RunPhase.Travel || run.Phase == RunPhase.Combat;

        /// <summary>Feeds fixed ticks to the run at the chosen speed; pausing or an adversity stops the clock.</summary>
        private void Update()
        {
            var run = Run;
            if (run == null || !InDungeon(run)) return;
            int stage = run.Stage;
            if (run.PendingAdversity || Speed == 0) accumulator = 0;
            else
            {
                accumulator += Time.deltaTime*Speed;
                int budget = MaxTicksPerFrame*Speed;
                for (int i = 0; i < budget && accumulator >= TickSeconds; i++)
                {
                    accumulator -= TickSeconds;
                    run.Tick();
                    if (!InDungeon(run) || run.Stage != stage || run.PendingAdversity) break;
                }
                if (accumulator > TickSeconds*budget) accumulator = 0;
            }
            if (SelectedHero >= 0 && run.Party[SelectedHero].Hp == 0) SelectHero(-1);

            if (run.Stage != stage)
            {
                ClearTrialDisplay();
                RefreshLayout();
                return;
            }
            RefreshParty();
            Board.SetMonsters(run.Monsters);
            Board.PlayEvents(run.TakeEvents());

            var fork = run.PendingFork;
            if (fork != null && fork.Token != discussedFork)
            {
                discussedFork = fork.Token;
                DiscussAsync(run, fork, lifetime).Forget();
            }
            if (run.Phase == RunPhase.Combat && run.Situation != plannedSituation)
            {
                plannedSituation = run.Situation;
                PlanAsync(run, run.Situation, lifetime).Forget();
            }
        }

        private async UniTask DiscussAsync(DungeonRun expedition, ForkDiscussion fork, CancellationToken token)
        {
            thinkers++;
            try
            {
                // Give the GM a moment to read the options even when the answer is local and instant.
                var discussion = director.DiscussAsync(expedition, fork, token);
                await UniTask.Delay(TimeSpan.FromSeconds(1.2), cancellationToken: token);
                var proposals = await discussion;
                while (expedition.PendingAdversity) await UniTask.Yield(PlayerLoopTiming.Update, token);
                if (Run != expedition || !expedition.ResolveFork(fork.Token, proposals)) return;
                Spotlight(fork.Backtracking ? "막다른 길 · 결정" : "갈림길 · 결정", expedition.LastFork);
            }
            finally
            {
                thinkers--;
            }
        }

        /// <summary>Asks for tactics for one situation; the fight keeps going on local rules meanwhile.</summary>
        private async UniTask PlanAsync(DungeonRun expedition, int situation, CancellationToken token)
        {
            thinkers++;
            try
            {
                var plan = await director.TacticsAsync(expedition, token);
                if (Run == expedition) expedition.ApplyTactics(situation, plan);
            }
            finally
            {
                thinkers--;
            }
        }

        private void Spotlight(string title, string text)
        {
            SpotlightTitle = title;
            SpotlightText = text;
            SpotlightUntil = Time.time + SpotlightSeconds;
        }

        public void Trial()
        {
            int ticket = Run.BeginAdversity();
            if (ticket == 0) return;
            LastRoll = 0;
            TrialAnnouncement = "낙석이 떨어진다!  용사가 대응을 선언하는 중…";
            RollAsync(Run, ticket, lifetime).Forget();
        }

        private async UniTask RollAsync(DungeonRun expedition, int ticket, CancellationToken token)
        {
            var response = await director.RespondAsync(expedition, token);
            if (Run != expedition) return;
            TrialAnnouncement = response.Line;
            await UniTask.Delay(TimeSpan.FromSeconds(.85), cancellationToken: token);
            if (Run != expedition) return;
            int roll = UnityEngine.Random.Range(1, 21);
            int modifier = expedition.TrialModifier(response.Role);
            int difficulty = expedition.TrialDifficulty;
            if (!expedition.ResolveAdversity(ticket, response.Role, roll, response.Line)) return;
            LastRoll = roll;
            TrialAnnouncement = expedition.LastTrial;
            Spotlight("낙석 역경 · 판정 결과", expedition.LastTrial);
            Board.PlayAdversity(D20Check.Succeeds(roll, modifier, difficulty));
            RefreshParty();
        }

        /// <summary>Blesses the hero the GM selected. Works while paused.</summary>
        public void Bless()
        {
            if (SelectedHero < 0 || !Run.Bless((HeroRole)SelectedHero)) return;
            Spotlight("축복", Run.Log[Run.Log.Count - 1]);
            RefreshParty();
        }

        public void Restart()
        {
            if (Run.Phase != RunPhase.Victory && Run.Phase != RunPhase.Defeat) return;
            Run = new DungeonRun(Environment.TickCount);
            ClearTrialDisplay();
            RefreshLayout();
        }

        private void ClearTrialDisplay()
        {
            SelectHero(-1);
            LastRoll = 0;
            TrialAnnouncement = "";
            SpotlightTitle = SpotlightText = "";
            SpotlightUntil = 0;
        }

        private void RefreshLayout()
        {
            bool planning = Run.Phase == RunPhase.Planning;
            Board.SetStage(Run.Stage, Run.Map, Run.Placements, Run.CanHoldMonster);
            if (!planning) Board.SetMonsters(Run.Monsters);
            fogVersion = -1;
            RefreshParty();
            Board.SetPlanning(planning);
            if (planning) Board.SetFollowParty(false);
        }

        private void RefreshParty()
        {
            for (int i = 0; i < living.Length; i++)
            {
                living[i] = Run.Party[i].Hp > 0;
                heroTiles[i] = Run.HeroTile((HeroRole)i);
            }
            Board.SetParty(heroTiles, living);
            if (Run.Phase != RunPhase.Planning && fogVersion != Run.SeenVersion)
            {
                fogVersion = Run.SeenVersion;
                Board.SetFog(Run.IsSeen);
            }
        }
    }
}
