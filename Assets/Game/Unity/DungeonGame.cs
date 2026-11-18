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
        /// <summary>Seconds per exploration step; the party crosses one tile per step.</summary>
        private const float StepSeconds = .32f;
        private const float SpotlightSeconds = 7f;
        private readonly bool[] living = new bool[4];
        private readonly TilePoint[] heroTiles = new TilePoint[4];
        private PartyDirector director;
        private CancellationToken lifetime;
        private int fogVersion = -1;

        public DungeonRun Run { get; private set; }
        public DungeonBoard Board { get; private set; }
        public string AiMode => director.ModeLabel;
        public bool Thinking { get; private set; }
        public int LastRoll { get; private set; }
        public string TrialAnnouncement { get; private set; } = "";
        /// <summary>The latest fork result or adversity, shown for a few seconds in the intervention panel.</summary>
        public string SpotlightTitle { get; private set; } = "";
        public string SpotlightText { get; private set; } = "";
        public float SpotlightUntil { get; private set; }

        private void Awake()
        {
            lifetime = destroyCancellationToken;
            director = new PartyDirector();
            Run = new DungeonRun(Environment.TickCount);
            Board = gameObject.AddComponent<DungeonBoard>();
            Board.Initialize();
            Board.WalkSpeed = DungeonBoard.Tile/StepSeconds;
            RefreshLayout();
            gameObject.AddComponent<DungeonHud>();
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
            RefreshLayout();
            Board.SetFollowParty(true);
            ExploreAsync(Run, lifetime).Forget();
        }

        private static bool InDungeon(DungeonRun run) => run.Phase == RunPhase.Travel || run.Phase == RunPhase.Combat;

        private async UniTask ExploreAsync(DungeonRun expedition, CancellationToken token)
        {
            int stage = expedition.Stage;
            while (Run == expedition && expedition.Stage == stage && InDungeon(expedition))
            {
                await WaitForTrialAsync(expedition, token);
                if (Run != expedition || !InDungeon(expedition)) return;

                if (expedition.PendingFork != null)
                {
                    await DiscussAsync(expedition, token);
                    continue;
                }

                if (expedition.Phase == RunPhase.Travel)
                {
                    expedition.Step();
                    RefreshParty();
                    if (expedition.Phase == RunPhase.Combat)
                    {
                        Board.SetEnemyHealth(expedition.FoeIndex, 1f);
                        await UniTask.Delay(TimeSpan.FromSeconds(.9), cancellationToken: token);
                    }
                    else await UniTask.Delay(TimeSpan.FromSeconds(StepSeconds), cancellationToken: token);
                    continue;
                }

                // A pace between decisions gives the GM time to read and intervene, even offline.
                await UniTask.Delay(TimeSpan.FromSeconds(1.0), cancellationToken: token);
                await WaitForTrialAsync(expedition, token);
                if (Run != expedition || expedition.Phase != RunPhase.Combat) continue;
                int round = expedition.RoundToken;
                int foe = expedition.FoeIndex;
                var enemy = expedition.Enemy;
                Thinking = true;
                var decisions = await director.DecideAsync(expedition, token);
                if (Run != expedition) return;
                Thinking = false;
                await WaitForTrialAsync(expedition, token);
                if (Run != expedition || expedition.Phase != RunPhase.Combat) continue;
                Board.PlayRound(foe);
                await UniTask.Delay(TimeSpan.FromSeconds(.8), cancellationToken: token);
                await WaitForTrialAsync(expedition, token);
                if (Run != expedition) return;
                if (expedition.ResolveRound(round, decisions))
                {
                    Board.SetEnemyHealth(foe, (float)enemy.Hp / enemy.MaxHp);
                    if (expedition.Stage != stage)
                    {
                        ClearTrialDisplay();
                        RefreshLayout();
                    }
                    else RefreshParty();
                }
            }
        }

        private async UniTask DiscussAsync(DungeonRun expedition, CancellationToken token)
        {
            var fork = expedition.PendingFork;
            Thinking = true;
            // Give the GM a moment to read the options even when the answer is local and instant.
            var discussion = director.DiscussAsync(expedition, fork, token);
            await UniTask.Delay(TimeSpan.FromSeconds(1.2), cancellationToken: token);
            var proposals = await discussion;
            Thinking = false;
            if (Run != expedition) return;
            await WaitForTrialAsync(expedition, token);
            if (Run != expedition || !expedition.ResolveFork(fork.Token, proposals)) return;
            Spotlight(fork.Backtracking ? "막다른 길 · 결정" : "갈림길 · 결정", expedition.LastFork);
            RefreshParty();
            await UniTask.Delay(TimeSpan.FromSeconds(1.6), cancellationToken: token);
        }

        private static async UniTask WaitForTrialAsync(DungeonRun expedition, CancellationToken token)
        {
            while (expedition.PendingAdversity) await UniTask.Yield(PlayerLoopTiming.Update, token);
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

        public void Bless()
        {
            if (Run.Bless()) RefreshParty();
        }

        public void Restart()
        {
            if (Run.Phase != RunPhase.Victory && Run.Phase != RunPhase.Defeat) return;
            Run = new DungeonRun(Environment.TickCount);
            Thinking = false;
            ClearTrialDisplay();
            RefreshLayout();
        }

        private void ClearTrialDisplay()
        {
            LastRoll = 0;
            TrialAnnouncement = "";
            SpotlightTitle = SpotlightText = "";
            SpotlightUntil = 0;
        }

        private void RefreshLayout()
        {
            bool planning = Run.Phase == RunPhase.Planning;
            Board.SetStage(Run.Stage, Run.Map, Run.Placements, Run.CanHoldMonster, Run.IsDefeated);
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
