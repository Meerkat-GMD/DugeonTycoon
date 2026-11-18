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
        private readonly TilePoint[] heroTiles = new TilePoint[4];
        private PartyDirector director;
        private DialogueStage stage;
        private PrayerScene prayerScene;
        private CancellationToken lifetime;
        private int heardPrayer;
        private int fogVersion = -1;
        private float accumulator;
        private int discussedFork;
        private int sharedLoot;
        private int plannedSituation;
        private int thinkers;

        public DungeonRun Run { get; private set; }
        /// <summary>Combat cues as the board plays them, for the HUD's pop-ups.</summary>
        public event Action<System.Collections.Generic.IReadOnlyList<CombatEvent>> EventsPlayed;
        public DungeonBoard Board { get; private set; }
        public string AiMode => director.ModeLabel;
        public bool Thinking => thinkers > 0;
        /// <summary>
        /// A fork debate or an adversity is playing on the dialogue stage. The dungeon stays still and the HUD keeps
        /// showing the party as it was until the stage closes, so the cards never give the result away early.
        /// </summary>
        public bool Presenting => (stage != null && stage.IsOpen) || (prayerScene != null && prayerScene.IsOpen);
        /// <summary>The hero (as int) whose answer to "will you pray?" is on its way, or -1. The HUD shows "…" over them.</summary>
        public int AskingPrayer { get; private set; } = -1;
        public int LastRoll { get; private set; }
        public string TrialAnnouncement { get; private set; } = "";
        /// <summary>Feedback from the most recent blessing or fatigue relief.</summary>
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
            BalanceFile.Load();
            Run = new DungeonRun(Environment.TickCount);
            Board = gameObject.AddComponent<DungeonBoard>();
            Board.Initialize();
            SetSpeed(1);
            RefreshLayout();
            gameObject.AddComponent<DungeonHud>();
            // Mounts itself into the HUD document in Start, after the HUD has bound its own elements.
            stage = gameObject.AddComponent<DialogueStage>();
            prayerScene = gameObject.AddComponent<PrayerScene>();
            Formation = gameObject.AddComponent<FormationScreen>();
        }

        /// <summary>The formation screen shown before the first departure.</summary>
        public FormationScreen Formation { get; private set; }

        /// <summary>
        /// The departure button: before the first departure it opens the formation screen, whose Start departs.
        /// Later stages keep the formation and set off at once.
        /// </summary>
        public void RequestDeparture()
        {
            if (Run.Phase != RunPhase.Planning) return;
            if (Run.CanEditFormation) Formation.Open();
            else Depart();
        }

        public void AdvanceToNextStage()
        {
            if (Presenting || !Run.AdvanceToNextStage()) return;
            ClearTrialDisplay();
            RefreshLayout();
        }

        public void SetSpeed(int speed)
        {
            Speed = Mathf.Clamp(speed, 0, 2);
            Board.AttackSpeed = Mathf.Max(1, Speed);
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
            // Cues left over from the last stage's final blow point at monsters that no longer exist.
            Run.TakeEvents();
            if (!Run.Start()) return;
            accumulator = 0;
            SetSpeed(1);
            discussedFork = 0;
            plannedSituation = Run.Situation;
            RefreshLayout();
            Board.SetFollowParty(true);
        }

        private static bool InDungeon(DungeonRun run) => run.Phase == RunPhase.Travel || run.Phase == RunPhase.Combat;

        /// <summary>
        /// Feeds fixed ticks to the run at the chosen speed. Pausing, an adversity, a fork debate or the dialogue
        /// stage stops the clock.
        /// </summary>
        private void Update()
        {
            var run = Run;
            // Checked before the dungeon test: armour from the first boss is shared out after the stage has cleared.
            var loot = run?.PendingLoot;
            if (loot != null && loot.Token != sharedLoot && !Presenting)
            {
                sharedLoot = loot.Token;
                ShareLootAsync(run, loot, lifetime).Forget();
            }
            if (run == null || !InDungeon(run)) return;

            if (run.PendingAdversity || run.PendingPrayer != null || Speed == 0 || Presenting) accumulator = 0;
            else
            {
                accumulator += Time.deltaTime*Speed;
                int budget = MaxTicksPerFrame*Speed;
                for (int i = 0; i < budget && accumulator >= TickSeconds; i++)
                {
                    accumulator -= TickSeconds;
                    run.Tick();
                    if (!InDungeon(run) || run.PendingAdversity || run.PendingPrayer != null) break;
                }
                if (accumulator > TickSeconds*budget) accumulator = 0;
            }
            if (SelectedHero >= 0 && run.Party[SelectedHero].Hp == 0) SelectHero(-1);

            // The result is already applied in Core; the board catches up once the stage has shown it.
            if (Presenting) return;
            RefreshParty();
            Board.SetMonsters(run.Monsters);
            Board.SetCasts(run.Casts, run.Ticks + Mathf.Clamp01(accumulator / TickSeconds));
            var events = run.TakeEvents();
            Board.PlayEvents(events);
            Board.SetFeatures(run.Chests, run.Springs);
            EventsPlayed?.Invoke(events);

            var fork = run.PendingFork;
            if (fork != null && fork.Token != discussedFork)
            {
                discussedFork = fork.Token;
                DiscussAsync(run, fork, lifetime).Forget();
            }
            var prayer = run.PendingPrayer;
            if (prayer != null && prayer.Token != heardPrayer)
            {
                heardPrayer = prayer.Token;
                PrayAsync(run, prayer, lifetime).Forget();
            }
            if (run.Phase == RunPhase.Combat && run.Situation != plannedSituation)
            {
                plannedSituation = run.Situation;
                PlanAsync(run, run.Situation, lifetime).Forget();
            }
        }

        /// <summary>
        /// Plays a fork debate on the stage: the narrator opens while the heroes think, then Core settles the fork
        /// and the stage shows the opinions, the persuasion dice and the result. The dungeon is frozen throughout.
        /// </summary>
        private async UniTask DiscussAsync(DungeonRun expedition, ForkDiscussion fork, CancellationToken token)
        {
            stage.Open(expedition);
            try
            {
                stage.SetThinking(LivingRoles(expedition));
                var discussion = Think(director.DiscussAsync(expedition, fork, token));
                await stage.SayAsync(null, DialogueLines.Opening(fork), token);
                var proposals = await discussion;
                stage.SetThinking(Array.Empty<HeroRole>());
                if (Run != expedition) return;
                var heard = expedition.HearFork(fork.Token, proposals);
                if (heard == null) return;
                await stage.PlayAsync(expedition.LastScene, PlayRoll, token);

                // With the faith for it, the GM may reveal a way before the dice.
                int oracle = -1;
                if (Run == expedition && expedition.CanOracle)
                {
                    var choices = new System.Collections.Generic.List<DialogueStage.OracleChoice>();
                    foreach (var option in fork.Options)
                    {
                        var backers = new System.Collections.Generic.List<HeroRole>();
                        foreach (var opinion in heard)
                            if (opinion.Option == option.Index) backers.Add(opinion.Role);
                        choices.Add(new DialogueStage.OracleChoice(option.Label, backers));
                    }
                    oracle = await stage.ChooseOracleAsync(choices, DungeonRun.OracleCost,
                        index => Board.ShowRoute(index >= 0 ? fork.Options[index].Route : null), token);
                }
                if (Run != expedition || !expedition.ResolveFork(fork.Token, null, oracle)) return;
                await stage.PlayAsync(expedition.LastScene, PlayRoll, token);
            }
            finally
            {
                if (Board) Board.ShowRoute(null);
                if (stage) stage.Close();
            }
        }

        /// <summary>
        /// Plays the armour sharing on the stage: the standing heroes think, Core settles it
        /// from their wishes (local ones if the AI is offline or wrong) and the stage shows who wants it, the loot
        /// dice and who wears it. The dungeon is frozen throughout.
        /// </summary>
        private async UniTask ShareLootAsync(DungeonRun expedition, LootDistribution loot, CancellationToken token)
        {
            stage.Open(expedition);
            try
            {
                stage.SetThinking(System.Linq.Enumerable.ToArray(loot.Heroes));
                var wishes = await Think(director.ShareLootAsync(expedition, loot, token));
                stage.SetThinking(Array.Empty<HeroRole>());
                if (Run != expedition || !expedition.ResolveLoot(loot.Token, wishes)) return;
                await stage.PlayAsync(expedition.LastScene, PlayRoll, token);
            }
            finally
            {
                if (stage) stage.Close();
            }
        }

        /// <summary>
        /// A desperate hero: the dungeon is frozen and "…" hangs over the hero while they decide. A refusal shows in
        /// the hero's card bubble and the dungeon moves on; a prayer opens the sanctum and waits for the GM's answer.
        /// </summary>
        private async UniTask PrayAsync(DungeonRun expedition, PrayerRequest prayer, CancellationToken token)
        {
            PrayerProposal proposal;
            AskingPrayer = (int)prayer.Role;
            try
            {
                proposal = await Think(director.PrayAsync(expedition, prayer, token));
            }
            finally
            {
                AskingPrayer = -1;
            }
            if (Run != expedition) return;
            var heard = expedition.HearPrayer(prayer.Token, proposal);
            if (heard == null) return;
            if (!heard.Praying) return;
            await prayerScene.PlayAsync(expedition, heard,
                answer => expedition.AnswerPrayer(heard.Token, answer) ? expedition.LastPrayer : null, token);
            if (Run == expedition) RefreshParty();
        }

        /// <summary>Counts an AI request as "판단 중" for the HUD until it answers.</summary>
        private async UniTask<T> Think<T>(UniTask<T> request)
        {
            thinkers++;
            try
            {
                return await request;
            }
            finally
            {
                thinkers--;
            }
        }

        private static HeroRole[] LivingRoles(DungeonRun run)
        {
            var roles = new System.Collections.Generic.List<HeroRole>();
            foreach (var hero in run.Party)
                if (hero.Hp > 0) roles.Add(hero.Role);
            return roles.ToArray();
        }

        /// <summary>The 3D die over the party spins while the stage rolls its dice.</summary>
        private void PlayRoll(DialogueBeat roll) => Board.PlayAdversity(roll.Difficulty == 0 || roll.Success);

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
            if (Presenting) return;
            int ticket = Run.BeginAdversity();
            if (ticket == 0) return;
            LastRoll = 0;
            TrialAnnouncement = "낙석이 떨어진다!  용사가 대응을 선언하는 중…";
            // Open before the first await so the clock stops this very frame.
            stage.Open(Run);
            RollAsync(Run, ticket, lifetime).Forget();
        }

        /// <summary>
        /// Plays an adversity on the stage: the narrator opens while every living hero thinks, the party names a
        /// representative, Core rolls the seeded d20 and the stage shows the declaration, the check and the outcome.
        /// </summary>
        private async UniTask RollAsync(DungeonRun expedition, int ticket, CancellationToken token)
        {
            try
            {
                stage.SetThinking(LivingRoles(expedition));
                var answer = Think(director.RespondAsync(expedition, token));
                await stage.SayAsync(null, DialogueLines.RockfallOpening, token);
                var response = await answer;
                stage.SetThinking(Array.Empty<HeroRole>());
                if (Run != expedition) return;
                TrialAnnouncement = response.Line;
                if (!expedition.RollAdversity(ticket, response.Role, response.Line)) return;
                foreach (var beat in expedition.LastScene)
                    if (beat.Kind == BeatKind.Roll) LastRoll = beat.Rolls[0].Face;
                TrialAnnouncement = expedition.LastTrial;
                await stage.PlayAsync(expedition.LastScene, PlayRoll, token);
            }
            finally
            {
                if (stage) stage.Close();
            }
        }

        /// <summary>Blesses the hero the GM selected. Works while paused.</summary>
        public void Bless()
        {
            if (SelectedHero < 0 || !Run.Bless((HeroRole)SelectedHero)) return;
            Spotlight("축복", Run.Log[Run.Log.Count - 1]);
            RefreshParty();
        }

        /// <summary>Eases the party's fatigue for faith. Works while paused.</summary>
        public void RelieveFatigue()
        {
            if (!Run.RelieveFatigue()) return;
            Spotlight("신의 휴식", Run.Log[Run.Log.Count - 1]);
        }

        /// <summary>
        /// Starts a new expedition after the last one ended. <paramref name="keepPlan"/> copies the formation and the
        /// monsters placed on each stage (still editable); <paramref name="sameSeed"/> also replays the same dice,
        /// so the GM can compare one change at a time.
        /// </summary>
        public void Restart(bool keepPlan = false, bool sameSeed = false)
        {
            if (Run.Phase != RunPhase.Victory && Run.Phase != RunPhase.Defeat) return;
            // A balance file edited since the last expedition applies from this one.
            BalanceFile.Load();
            Run = keepPlan ? Run.Rematch(sameSeed ? Run.Seed : Environment.TickCount) : new DungeonRun(Environment.TickCount);
            sharedLoot = 0;
            heardPrayer = 0;
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
            Board.SetStage(Run.Stage, Run.Map, Run.Placements, Run.Patrols, Run.CanHoldMonster, Run.Encounters);
            Board.SetFeatures(Run.Chests, Run.Springs);
            if (!planning) Board.SetMonsters(Run.Monsters);
            Board.SetCasts(planning ? null : Run.Casts, Run.Ticks);
            fogVersion = -1;
            RefreshParty();
            Board.SetPlanning(planning);
            if (planning) Board.SetFollowParty(false);
        }

        private void RefreshParty()
        {
            for (int i = 0; i < heroTiles.Length; i++)
                heroTiles[i] = Run.HeroTile((HeroRole)i);
            Board.SetParty(heroTiles, Run.Party);
            if (Run.Phase != RunPhase.Planning && fogVersion != Run.SeenVersion)
            {
                fogVersion = Run.SeenVersion;
                Board.SetFog(Run.IsSeen);
            }
        }
    }
}
