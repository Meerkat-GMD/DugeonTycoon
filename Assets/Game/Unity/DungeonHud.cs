using System.Collections.Generic;
using System.Linq;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DungeonTycoon.Unity
{
    /// <summary>
    /// Binds the UI Toolkit HUD (Resources/Hud/DungeonHud.uxml + .uss) to the run, and routes mouse input that
    /// is not over the HUD to the board. The dungeon fills the whole screen; the HUD floats on top of it.
    /// Layout and look live in the UXML/USS so they can be edited in UI Builder; this script only fills in
    /// values, toggles classes and reacts to clicks.
    /// </summary>
    public sealed class DungeonHud : MonoBehaviour
    {
        // The panel is scaled so the screen is about ReferenceHeight units tall. Below that it keeps at least
        // ReadableScale, unless the layout would no longer fit in MinWidth x MinHeight units.
        private const float ReferenceHeight = 640, ReadableScale = .75f;
        private const float MinWidth = 1000, MinHeight = 500;
        private const float LineSeconds = 6, NoticeSeconds = 2.5f;

        private static readonly string[] RoleNames = { "탱커", "힐러", "근딜", "원딜" };

        private DungeonGame game;
        private UIDocument document;
        private PanelSettings panel;
        private VisualElement hud, bossBar, bossFill, party, bottom, history, titleScreen;
        private VisualElement interventionPage, resultPage, speedGroup, dialogueStage;
        private Label stageNumber, stageName, bossTag, bossName, bossHp, pauseBanner, aiMode;
        private Label hint, budget, faith, expeditionFaith, partyLevel, summaryTitle, summaryDetail;
        private VisualElement stageClearPage;
        private Label stageClearTitle;
        private Label resultTitle, resultText;
        private Button follow, primary, collapse, removeCard, trialCard, blessCard;
        private Label summaryNote;
        private VisualElement planningTip;
        private Label tipKind, tipName, tipDescription, tipFact;
        private DungeonBoard.PlanningTarget shownTarget;
        // One placement card per monster kind; only the kinds on the stage's roster are shown.
        private static readonly (MonsterKind Kind, string Name)[] Brushes =
        {
            (MonsterKind.Skeleton, "brush-skeleton"), (MonsterKind.Ghost, "brush-ghost"),
            (MonsterKind.Zombie, "brush-zombie"), (MonsterKind.Archer, "brush-archer")
        };
        private readonly Dictionary<MonsterKind, Button> brushCards = new Dictionary<MonsterKind, Button>();
        // Shown under the stage number, indexed by stage.
        private static readonly string[] StageNames = { "", "성문으로 가는 길", "지하 납골당", "마왕의 성채" };
        private VisualElement faithCells;
        private Label trialDetail, blessDetail, restDetail, fatigueStage, fatigueEffect;
        private Button restCard;
        private VisualElement fatigue, fatigueFill, fatigueTick1, fatigueTick2;
        private readonly Button[] speedButtons = new Button[3];
        private ScrollView historyList;
        private Label historyStats;
        private readonly HeroCard[] heroes = new HeroCard[4];

        private MonsterKind brush = MonsterKind.Skeleton;
        private bool panning, fitPending, cameraUsed;
        private Vector2 lastMouse;
        private string notice = "";
        private float noticeUntil;
        private int knownStage, loggedRows;
        private DungeonRun knownRun;
        private int bossMaxHp;

        // Settlement banner and board pop-ups. Balance values for how long they stay.
        private const float SpoilsSeconds = 4.5f, PopSeconds = 1.3f, LevelPopSeconds = 1.9f;
        private const float ItemFlySeconds = .55f, ItemHoldSeconds = .6f, PopRise = 26;
        private VisualElement spoilsBanner, spoilsLines, pops;
        private int shownSpoils;
        private float spoilsUntil;
        private readonly List<Pop> livePops = new List<Pop>();
        private readonly HashSet<int> deadFoes = new HashSet<int>();
        private int seenExperience, seenLevel;

        private sealed class Pop
        {
            public Label Label;
            public Vector2 From;
            public float Born, Life;
            public bool FliesToParty;
        }

        private sealed class HeroCard
        {
            public VisualElement Root, Bubble, Health;
            public Label Portrait, Name, Hp, Line, Weapon, Armor;
            public readonly Label[] Skills = new Label[2];
            public readonly VisualElement[] Cooldowns = new VisualElement[2];
            public string Heard = "";
            public float HeardAt = -1000;
        }

        private void Awake()
        {
            game = GetComponent<DungeonGame>();
            // A runtime copy, so changing the scale every frame never edits the asset on disk.
            panel = Instantiate(Resources.Load<PanelSettings>("Hud/HudPanel"));
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("Hud/DungeonHud");
            Bind(document.rootVisualElement);
            game.EventsPlayed += OnCombatEvents;
        }

        private void OnDestroy()
        {
            if (game) game.EventsPlayed -= OnCombatEvents;
            if (panel) Destroy(panel);
        }

        // ── Binding ──────────────────────────────────────────────────────

        private void Bind(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            hud = root.Q("hud");
            planningTip = hud.Q("planning-tip");
            tipKind = planningTip.Q<Label>("kind");
            tipName = planningTip.Q<Label>("name");
            tipDescription = planningTip.Q<Label>("description");
            tipFact = planningTip.Q<Label>("fact");
            hud.style.display = DisplayStyle.None;
            titleScreen = root.Q("title-screen");
            var newGame = titleScreen.Q<Button>("title-new");
            BindPrologue(newGame);
            newGame.schedule.Execute(() => newGame.Focus());
            titleScreen.Q<Button>("title-continue").SetEnabled(false);
            titleScreen.Q<Button>("title-quit").clicked += QuitGame;
            stageNumber = root.Q<Label>("stage-number");
            stageName = root.Q<Label>("stage-name");
            bossBar = root.Q("boss-bar");
            bossTag = root.Q<Label>("boss-tag");
            bossName = root.Q<Label>("boss-name");
            bossHp = root.Q<Label>("boss-hp");
            bossFill = root.Q("boss-fill");
            pauseBanner = root.Q<Label>("pause-banner");
            follow = root.Q<Button>("follow");
            party = root.Q("party");
            aiMode = root.Q<Label>("ai-mode");
            bottom = root.Q("bottom");
            faithCells = root.Q("faith-cells");
            expeditionFaith = root.Q<Label>("expedition-faith");
            hint = root.Q<Label>("hint");
            budget = root.Q<Label>("budget");
            faith = root.Q<Label>("faith");
            partyLevel = root.Q<Label>("party-level");
            speedGroup = root.Q("speed");
            primary = root.Q<Button>("primary");
            collapse = root.Q<Button>("collapse");
            interventionPage = root.Q("intervention");
            resultPage = root.Q("result");
            stageClearPage = root.Q("stage-clear");
            stageClearTitle = root.Q<Label>("stage-clear-title");
            root.Q<Button>("stage-continue").clicked += game.AdvanceToNextStage;
            foreach (var (kind, cardName) in Brushes)
            {
                var card = root.Q<Button>(cardName);
                card.Q<Label>("detail").text = $"위협 {DungeonRun.Cost(kind)}";
                card.clicked += () => brush = kind;
                brushCards.Add(kind, card);
            }
            removeCard = root.Q<Button>("brush-remove");
            summaryTitle = root.Q<Label>("summary-title");
            summaryDetail = root.Q<Label>("summary-detail");
            summaryNote = root.Q<Label>("summary-note");
            trialCard = root.Q<Button>("trial");
            root.Q<Label>("trial-risk").text = $"실패 체력 -{DungeonRun.TrialDamage}";
            blessCard = root.Q<Button>("bless");
            trialDetail = root.Q<Label>("trial-detail");
            blessDetail = root.Q<Label>("bless-detail");
            restCard = root.Q<Button>("rest");
            restDetail = root.Q<Label>("rest-detail");
            blessCard.Q<Label>("cost").text = DungeonRun.BlessCost.ToString();
            restCard.Q<Label>("cost").text = DungeonRun.BlessCost.ToString();
            fatigue = root.Q("fatigue");
            fatigueStage = root.Q<Label>("fatigue-stage");
            fatigueEffect = root.Q<Label>("fatigue-effect");
            fatigueFill = root.Q("fatigue-fill");
            fatigueTick1 = root.Q("fatigue-tick-1");
            fatigueTick2 = root.Q("fatigue-tick-2");
            resultTitle = root.Q<Label>("result-title");
            resultText = root.Q<Label>("result-text");
            history = root.Q("history");
            historyStats = root.Q<Label>("history-stats");
            historyList = root.Q<ScrollView>("history-list");
            spoilsBanner = root.Q("spoils");
            spoilsLines = root.Q("spoils-lines");
            pops = root.Q("pops");

            for (int i = 0; i < heroes.Length; i++)
            {
                var card = root.Q("hero-"+i);
                heroes[i] = new HeroCard {
                    Root = card, Bubble = card.Q("bubble"), Health = card.Q("health"),
                    Portrait = card.Q<Label>("portrait"), Name = card.Q<Label>("name"), Hp = card.Q<Label>("hp"),
                    Line = card.Q<Label>("line"),
                    Weapon = card.Q<Label>("weapon"), Armor = card.Q<Label>("armor")
                };
                for (int s = 0; s < 2; s++)
                {
                    heroes[i].Skills[s] = card.Q<Label>("skill-"+s);
                    heroes[i].Cooldowns[s] = heroes[i].Skills[s].Q("cooldown");
                }
                int index = i;
                // Picking a hero chooses who the GM's help goes to; clicking again clears the choice.
                card.RegisterCallback<ClickEvent>(_ => PickHero(index));
            }
            for (int speed = 0; speed < speedButtons.Length; speed++)
            {
                int chosen = speed;
                speedButtons[speed] = root.Q<Button>("speed-"+speed);
                speedButtons[speed].clicked += () => game.SetSpeed(chosen);
            }

            follow.clicked += () => game.Board.SetFollowParty(!game.Board.FollowParty);
            root.Q<Button>("whole").clicked += () => game.Board.ShowWholeDungeon();
            root.Q<Button>("history-open").clicked += OpenHistory;
            root.Q<Button>("history-close").clicked += () => history.RemoveFromClassList("open");
            collapse.clicked += () => bottom.ToggleInClassList("collapsed");
            primary.clicked += game.RequestDeparture;
            root.Q<Button>("result-new").clicked += () => game.Restart();
            removeCard.clicked += () => brush = MonsterKind.None;
            // A new expedition from the same formation and monster plan, with new dice or the same ones.
            root.Q<Button>("result-copy").clicked += () => game.Restart(keepPlan: true);
            root.Q<Button>("result-replay").clicked += () => game.Restart(keepPlan: true, sameSeed: true);
            trialCard.clicked += () => game.Trial();
            blessCard.clicked += () => game.Bless();
            restCard.clicked += () => game.RelieveFatigue();
        }

        private void PickHero(int index)
        {
            var run = game.Run;
            if (!Exploring(run) || run.Party[index].Hp == 0) return;
            game.SelectHero(game.SelectedHero == index ? -1 : index);
        }

        // ── Frame ────────────────────────────────────────────────────────

        private void BindPrologue(Button newGame)
        {
            var pages = new (string Art, string Caption)[] {
                ("Necromancer", "어느날 강력한 네크로맨서가 나타나 세상을 위협하기 시작했어요."),
                ("EmptyHall", "하지만 이 세상에는 네크로맨서를 막을만한 용사가 없었어요."),
                ("ChosenParty", "가엾게 여긴 신이 직접 용사들을 육성하기로 했어요."),
                ("DungeonMap", "용사 파티 수준에 맞는 적을 적절히 배치하여 용사를 성장시키고 네크로 맨서를 무찌르세요!")
            };
            var prologue = titleScreen.Q("prologue");
            var art = prologue.Q<Image>("prologue-art");
            var caption = prologue.Q<Label>("prologue-caption");
            var skip = prologue.Q<Button>("prologue-skip");
            int page = 0;

            void ShowPage()
            {
                art.image = Resources.Load<Texture2D>("Hud/Prologue" + pages[page].Art);
                caption.text = pages[page].Caption;
            }

            void Finish()
            {
                titleScreen.RemoveFromHierarchy();
                titleScreen = null;
                hud.style.display = DisplayStyle.Flex;
            }

            newGame.clicked += () => {
                titleScreen.Q(className: "title-menu").style.display = DisplayStyle.None;
                prologue.style.display = DisplayStyle.Flex;
                ShowPage();
                prologue.Focus();
            };

            void Advance()
            {
                if (++page == pages.Length) Finish();
                else ShowPage();
            }

            prologue.RegisterCallback<ClickEvent>(evt => {
                if (evt.target == prologue && evt.button == 0) Advance();
            });
            prologue.RegisterCallback<NavigationSubmitEvent>(evt => {
                if (evt.target != prologue) return;
                evt.StopPropagation();
                Advance();
            });
            skip.clicked += Finish;
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Update()
        {
            if (!game || game.Run == null || !game.Board || hud == null) return;
            float scale = Mathf.Min(Screen.width/MinWidth,Screen.height/MinHeight,
                Mathf.Max(Screen.height/ReferenceHeight,ReadableScale));
            if (scale > 0 && !Mathf.Approximately(panel.scale,scale)) panel.scale = scale;

            if (titleScreen != null) return;
            var run = game.Run;
            if (!ReferenceEquals(knownRun,run) || knownStage != run.Stage) ResetForStage(run);
            Keys(run);
            Refresh(run);
            RefreshSpoils(run);
            RefreshPops(run);
            UpdateClearArea(run);
            BoardInput(run);
        }

        private void ResetForStage(DungeonRun run)
        {
            if (!ReferenceEquals(knownRun,run))
            {
                historyList.Clear();
                loggedRows = 0;
                // A new expedition: forget the previous one's banner and pop-up bookkeeping.
                seenExperience = run.Experience;
                seenLevel = run.Level;
            }
            shownSpoils = run.LastSpoils?.Number ?? 0;
            spoilsUntil = 0;
            spoilsBanner.RemoveFromClassList("show");
            spoilsBanner.style.display = DisplayStyle.None;
            // Monster ids restart with every stage.
            deadFoes.Clear();
            foreach (var pop in livePops) pop.Label.RemoveFromHierarchy();
            livePops.Clear();
            brush = MonsterKind.Skeleton;
            panning = false;
            notice = "";
            history.RemoveFromClassList("open");
            foreach (var hero in heroes)
            {
                hero.Heard = "";
                hero.HeardAt = -1000;
            }
            bossMaxHp = run.BossMaxHp;
            knownRun = run;
            knownStage = run.Stage;
            fitPending = true;
        }

        private static bool Exploring(DungeonRun run) => run.Phase == RunPhase.Travel || run.Phase == RunPhase.Combat;

        private bool HistoryOpen => history.ClassListContains("open");

        /// <summary>Space pauses or resumes, 1 and 2 pick the speed, Escape closes the record.</summary>
        private void Keys(DungeonRun run)
        {
            if (HistoryOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) history.RemoveFromClassList("open");
                return;
            }
            // While the dialogue stage is open, Space and clicks advance its lines instead.
            if (!Exploring(run) || game.Presenting) return;
            if (Input.GetKeyDown(KeyCode.Space)) game.TogglePause();
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) game.SetSpeed(1);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) game.SetSpeed(2);
        }

        private void Refresh(DungeonRun run)
        {
            bool planning = run.Phase == RunPhase.Planning;
            bool finished = run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat;
            bool exploring = Exploring(run);

            stageNumber.text = "스테이지 "+run.Stage+" / "+DungeonRun.FinalStage;
            stageName.text = run.Stage < StageNames.Length ? StageNames[run.Stage] : "";

            // The live boss only exists once the party departs; until then the bar shows the stage's boss at full health.
            int hp = run.Boss != null ? run.Boss.Hp : bossMaxHp;
            bool engaged = run.FightingBoss;
            bossBar.EnableInClassList("engaged",engaged);
            bossTag.text = engaged ? "보스전" : "BOSS";
            bossName.text = run.BossName;
            bossHp.text = hp+" / "+bossMaxHp;
            bossFill.style.width = Length.Percent(bossMaxHp == 0 ? 0 : 100f*hp/bossMaxHp);

            string banner = !exploring ? "" : run.PendingAdversity ? "역경 판정 중" :
                game.Speed == 0 ? "일시정지" : "";
            Show(pauseBanner,banner.Length > 0);
            pauseBanner.text = banner;

            Show(follow,exploring);
            follow.text = game.Board.FollowParty ? "따라가기  켬" : "따라가기  끔";
            follow.EnableInClassList("on",game.Board.FollowParty);

            if (!game.Presenting)
                partyLevel.text = $"파티 Lv.{run.Level}  ·  경험치 {run.ExperienceIntoLevel}/{DungeonRun.ExperiencePerLevel}";
            // The stage shows a result before the cards do, so they hold still until it closes.
            for (int i = 0; i < heroes.Length; i++)
                if (game.Presenting) MuteHero(run.Party[i],i);
                else RefreshHero(run,run.Party[i],i);
            RefreshFatigue(run,exploring);
            aiMode.text = "판단 중";
            Show(aiMode,game.Thinking && !game.Presenting);
            aiMode.EnableInClassList("thinking",game.Thinking);

            bool warning = notice.Length > 0 && Time.unscaledTime < noticeUntil;
            hint.text = warning ? notice : planning && !cameraUsed ? "우클릭 드래그 이동 · 휠 확대·축소" : "";
            hint.EnableInClassList("warning",warning);
            budget.text = run.Budget.ToString();
            if (!game.Presenting) faith.text = run.Faith.ToString();

            Show(speedGroup,exploring && !game.Presenting);
            for (int speed = 0; speed < speedButtons.Length; speed++)
                speedButtons[speed].EnableInClassList("on",game.Speed == speed);
            primary.text = run.CanEditFormation ? "편성 · 출발  ›" : "원정 출발  ›";
            collapse.text = bottom.ClassListContains("collapsed") ? "▲" : "▼";

            Show(bottom,planning && !game.Presenting);
            Show(interventionPage,exploring && !game.Presenting);
            bool showClear = run.Phase == RunPhase.StageCleared && run.PendingLoot == null && !game.Presenting;
            Show(stageClearPage,showClear);
            if (showClear) stageClearTitle.text = $"{run.Stage}스테이지 클리어";
            Show(resultPage,finished && !game.Presenting);
            if (planning) RefreshPlanning(run);
            else if (finished) RefreshResult(run);
            else if (exploring && !game.Presenting) RefreshIntervention(run);

            if (HistoryOpen)
            {
                // Every monster but the boss counts: the GM's placements and the stage's fixed patrols.
                historyStats.text = $"{run.StepsTaken}걸음 · 발견 {run.SpottedCount}/{run.Placements.Count+run.PatrolMemberCount} · 처치 {run.DefeatedCount} · 갈림길 {run.ForkCount}회";
                AppendHistory(run);
            }
        }

        private void RefreshHero(DungeonRun run,HeroState hero,int index)
        {
            var card = heroes[index];
            bool down = hero.Hp == 0;
            card.Root.EnableInClassList("down",down);
            card.Root.EnableInClassList("pickable",Exploring(run) && !down);
            // The choice only matters while help can be given, so the frame shows only during an expedition.
            card.Root.EnableInClassList("picked",Exploring(run) && game.SelectedHero == index);
            card.Name.text = RoleNames[index]+"  "+hero.Name;
            card.Hp.text = down ? "쓰러짐" : "HP "+hero.Hp+" / "+hero.MaxHp;
            card.Health.style.width = Length.Percent(hero.MaxHp == 0 ? 0 : 100f*hero.Hp/hero.MaxHp);

            // Speech floats beside the card for a few seconds after a hero says something new.
            string line = hero.LastLine ?? "";
            if (line != card.Heard)
            {
                card.Heard = line;
                card.HeardAt = Time.unscaledTime;
            }
            // While a desperate hero decides whether to pray, the dungeon is frozen and "…" hangs beside their card.
            bool asking = !down && game.AskingPrayer == index;
            bool speaking = asking || (!down && !string.IsNullOrWhiteSpace(line) && Time.unscaledTime-card.HeardAt <= LineSeconds);
            Show(card.Bubble,speaking);
            if (speaking) card.Line.text = asking ? "…" : line;
            card.Root.EnableInClassList("belief-firm",hero.Belief == Belief.Firm);
            card.Root.EnableInClassList("belief-shaken",hero.Belief == Belief.Shaken);
            RefreshKit(run,hero,card);
        }

        /// <summary>One bar for the party: it fills toward 탈진, with marks at both thresholds, and changes colour by stage.</summary>
        private void RefreshFatigue(DungeonRun run,bool exploring)
        {
            Show(fatigue,exploring);
            if (!exploring) return;
            var stage = run.Fatigue;
            float full = Mathf.Max(1,run.FatigueSecondThreshold*1.3f);
            fatigue.EnableInClassList("tired",stage == FatigueStage.Tired);
            fatigue.EnableInClassList("exhausted",stage == FatigueStage.Exhausted);
            fatigueStage.text = "피로 · "+DungeonRun.FatigueName(stage);
            fatigueEffect.text = stage == FatigueStage.Rested ? "" :
                stage == FatigueStage.Tired ? "공격력 -"+DungeonRun.TiredAttackPenalty : "공격력 -"+DungeonRun.TiredAttackPenalty+" · 체력 감소";
            fatigueFill.style.width = Length.Percent(100f*Mathf.Clamp01(run.FatigueSteps/full));
            fatigueTick1.style.left = Length.Percent(100f*run.FatigueFirstThreshold/full);
            fatigueTick2.style.left = Length.Percent(100f*run.FatigueSecondThreshold/full);
        }

        /// <summary>Weapon and armour names, and the two skills: the second greyed until the party level opens it.</summary>
        private void RefreshKit(DungeonRun run,HeroState hero,HeroCard card)
        {
            Gear(card.Weapon,hero.Weapon);
            Gear(card.Armor,hero.BodyArmor);
            for (int s = 0; s < 2; s++)
            {
                var skill = HeroSkills.Of(hero.Role,s);
                bool open = run.SkillUnlocked(hero.Role,s);
                // Revive has no cooldown; once used it stays spent for the stage.
                bool spent = skill == HeroSkill.Revive && run.ReviveUsed;
                card.Skills[s].text = open ? HeroSkills.Name(skill) : "Lv"+HeroSkills.UnlockLevel;
                card.Skills[s].EnableInClassList("locked",!open || spent);
                card.Cooldowns[s].style.height = Length.Percent(open && !spent ? 100f*SkillCooling(run,hero.Role,s) : 0);
            }
        }

        /// <summary>How much of a skill's cooldown is left, from 1 just used to 0 ready.</summary>
        private static float SkillCooling(DungeonRun run,HeroRole role,int index)
        {
            int total = DungeonRun.SkillCooldownTicks(HeroSkills.Of(role,index));
            return total <= 0 ? 0 : Mathf.Clamp01((float)run.SkillCooldownLeft(role,index)/total);
        }

        private static void Gear(Label slot,Equipment item)
        {
            Show(slot,item != null);
            slot.text = item?.Name ?? "";
            slot.EnableInClassList("empty",item == null);
            slot.EnableInClassList("rare",item != null && item.Grade == EquipmentGrade.Rare);
        }

        // ── Settlement banner and board pop-ups ──────────────────────────

        /// <summary>Shows each settled fight once, after the stage (if any) has closed, then fades it out.</summary>
        private void RefreshSpoils(DungeonRun run)
        {
            var spoils = run.LastSpoils;
            if (spoils != null && spoils.Number != shownSpoils && !game.Presenting && run.PendingLoot == null)
            {
                shownSpoils = spoils.Number;
                if (!spoils.Items.Any(result => result.Item.Slot == EquipmentSlot.Weapon)) return;
                FillSpoils(run,spoils);
                spoilsUntil = Time.unscaledTime+SpoilsSeconds;
                spoilsBanner.style.display = DisplayStyle.Flex;
                spoilsBanner.schedule.Execute(() => spoilsBanner.AddToClassList("show"));
            }
            if (spoilsUntil > 0 && Time.unscaledTime >= spoilsUntil)
            {
                spoilsUntil = 0;
                spoilsBanner.RemoveFromClassList("show");
                spoilsBanner.schedule.Execute(() =>
                {
                    if (spoilsUntil == 0) spoilsBanner.style.display = DisplayStyle.None;
                }).ExecuteLater(350);
            }
        }

        private void FillSpoils(DungeonRun run,CombatSpoils spoils)
        {
            spoilsLines.Clear();
            foreach (var result in spoils.Items)
            {
                var item = result.Item;
                if (item.Slot != EquipmentSlot.Weapon) continue;
                if (result.Wearer is HeroRole wearer)
                    SpoilsLine($"{run.Party[(int)wearer].Name} ▸ {item.Name} 장착 ({item.Stats})",
                        item.Grade == EquipmentGrade.Rare ? "rare" : null);
                else SpoilsLine($"{item.Name} ▸ 버림 ({result.Reason})","discarded");
            }
        }

        private void SpoilsLine(string text,string style)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("spoils-line");
            if (style != null) label.AddToClassList(style);
            spoilsLines.Add(label);
        }

        /// <summary>A dropped item pops over the monster, then flies to the party; a skill's name pops over its hero.</summary>
        private void OnCombatEvents(IReadOnlyList<CombatEvent> events)
        {
            var run = game.Run;
            foreach (var e in events)
            {
                // A skill's name flashes over the hero who used it.
                if (e.Kind == CombatEventKind.Skill && e.FromHero && game.Board.TryHeroScreenPoint(e.Source,out var hero))
                {
                    AddPop(HeroSkills.Name((HeroSkill)e.Amount)+"!","skill",hero,PopSeconds);
                    continue;
                }
                // A pattern's name flashes over the monster that started it; the tiles show where it lands.
                if (e.Kind == CombatEventKind.Pattern && game.Board.TryFoeScreenPoint(e.Source,out var caster))
                {
                    AddPop(MonsterPatterns.Name((MonsterPattern)e.Amount)+"!","skill foe",caster,PopSeconds);
                    continue;
                }
                // A spring shows the fatigue it took away over the basin.
                if (e.Kind == CombatEventKind.Spring)
                {
                    if (game.Board.TryFeatureScreenPoint(true,e.Source,out var basin))
                        AddPop("샘물 · 피로 -"+e.Amount,"spring",basin,PopSeconds);
                    continue;
                }
                // An item from an opened chest pops over it and flies to the party, like a monster's drop.
                bool chest = e.Kind == CombatEventKind.Chest;
                if ((e.Kind != CombatEventKind.Drop && !chest) || e.Amount < 0 || e.Amount >= run.Drops.Count) continue;
                Vector2 at;
                if (chest ? !game.Board.TryFeatureScreenPoint(false,e.Source,out at) : !game.Board.TryFoeScreenPoint(e.Source,out at))
                    continue;
                var item = run.Drops[e.Amount];
                var pop = AddPop(item.Name,item.Grade == EquipmentGrade.Rare ? "item rare" : "item",at,ItemHoldSeconds+ItemFlySeconds);
                pop.From += new Vector2(0,-18);
                pop.FliesToParty = true;
            }
        }

        /// <summary>Experience rises from each monster as it falls; a level-up rises from the party, bigger.</summary>
        private void RefreshPops(DungeonRun run)
        {
            int gained = run.Experience-seenExperience;
            int fallen = 0;
            foreach (var monster in run.Monsters)
            {
                if (monster.Alive || !deadFoes.Add(monster.Id)) continue;
                fallen++;
                if (gained > 0 && game.Board.TryFoeScreenPoint(monster.Id,out var at))
                    AddPop("+"+gained+" 경험치","xp",at,PopSeconds);
            }
            // Experience from an adversity or a boss that cleared the stage has no monster to rise from.
            if (gained > 0 && fallen == 0 && game.Board.TryPartyScreenPoint(out var party0))
                AddPop("+"+gained+" 경험치","xp",party0,PopSeconds);
            seenExperience = run.Experience;
            if (run.Level > seenLevel && game.Board.TryPartyScreenPoint(out var party1))
                AddPop("레벨 업!  Lv "+run.Level,"level",party1+new Vector2(0,30),LevelPopSeconds);
            seenLevel = run.Level;

            for (int i = livePops.Count-1; i >= 0; i--)
            {
                var pop = livePops[i];
                float age = Time.unscaledTime-pop.Born;
                if (age >= pop.Life)
                {
                    pop.Label.RemoveFromHierarchy();
                    livePops.RemoveAt(i);
                    continue;
                }
                Vector2 at = pop.From;
                float fade = 1;
                if (pop.FliesToParty)
                {
                    float t = Mathf.Clamp01((age-ItemHoldSeconds)/ItemFlySeconds);
                    if (t > 0 && game.Board.TryPartyScreenPoint(out var target))
                        at = Vector2.Lerp(pop.From,target,t*t);
                    fade = 1-t*t;
                    pop.Label.style.scale = new Scale(Vector3.one*(1-.5f*t));
                }
                else
                {
                    float t = age/pop.Life;
                    at.y += PopRise*t;
                    fade = t < .75f ? 1 : 1-(t-.75f)/.25f;
                }
                Place(pop.Label,at);
                pop.Label.style.opacity = fade;
            }
        }

        private Pop AddPop(string text,string classes,Vector2 screenPoint,float life)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("pop");
            foreach (var name in classes.Split(' ')) label.AddToClassList(name);
            pops.Add(label);
            var pop = new Pop { Label = label, From = screenPoint, Born = Time.unscaledTime, Life = life };
            livePops.Add(pop);
            Place(label,screenPoint);
            return pop;
        }

        /// <summary>Screen point (origin bottom-left) to the HUD panel's coordinates.</summary>
        private void Place(VisualElement element,Vector2 screenPoint)
        {
            if (hud.panel == null) return;
            var point = RuntimePanelUtils.ScreenToPanel(hud.panel,new Vector2(screenPoint.x,Screen.height-screenPoint.y));
            element.style.left = point.x;
            element.style.top = point.y;
        }

        /// <summary>
        /// Keeps a card's bubble hidden while the stage speaks, and marks the line as already heard so it does not
        /// pop up beside the card once the stage closes.
        /// </summary>
        private void MuteHero(HeroState hero,int index)
        {
            var card = heroes[index];
            card.Heard = hero.LastLine ?? "";
            card.HeardAt = -1000;
            Show(card.Bubble,false);
        }

        private void RefreshPlanning(DungeonRun run)
        {
            // A stage only offers the monsters on its roster; a brush that left the roster falls back to the skeleton.
            if (brush != MonsterKind.None && !run.InRoster(brush)) brush = MonsterKind.Skeleton;
            foreach (var (kind, _) in Brushes)
            {
                var card = brushCards[kind];
                Show(card,run.InRoster(kind));
                card.EnableInClassList("chosen",brush == kind);
            }
            removeCard.EnableInClassList("chosen",brush == MonsterKind.None);
            int Placed(MonsterKind kind) => run.Placements.Count(p => p.Kind == kind);
            bool empty = run.Placements.Count == 0;
            summaryTitle.text = "배치 "+run.Placements.Count+"마리";
            summaryDetail.text = empty ? ""
                : string.Join("  ·  ",run.Roster.Select(kind => DungeonRun.MonsterName(kind)+" "+Placed(kind)));
            summaryNote.text = FeatureNote(run);
        }

        /// <summary>The stage's fixed features, which the GM plans around but cannot move.</summary>
        private static string FeatureNote(DungeonRun run)
        {
            var features = new List<string>();
            if (run.Patrols.Count > 0)
                features.Add($"고정 순회조 {run.Patrols.Count}조 {run.PatrolMemberCount}마리 · 바닥의 색 선을 따라 돎");
            var props = new List<string>();
            if (run.Chests.Count > 0) props.Add($"보물 상자 {run.Chests.Count}");
            if (run.Springs.Count > 0) props.Add($"샘물 {run.Springs.Count}");
            if (props.Count > 0) features.Add(string.Join("  ·  ",props));
            return string.Join("\n",features);
        }

        private void RefreshIntervention(DungeonRun run)
        {
            expeditionFaith.text = run.Faith.ToString();
            int slots = Mathf.Max(5, run.Faith);
            while (faithCells.childCount < slots)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("faith-cell");
                faithCells.Add(cell);
            }
            for (int i = 0; i < faithCells.childCount; i++)
                faithCells[i].EnableInClassList("filled", i < run.Faith);

            trialCard.SetEnabled(run.TrialsLeft > 0 && !run.PendingAdversity);
            trialDetail.text = $"d20+보정 ≥ {run.TrialDifficulty} · {run.TrialsLeft}회";
            int target = game.SelectedHero;
            blessDetail.text = target < 0 ? "용사를 먼저 고르세요" :
                run.Party[target].Hp == run.Party[target].MaxHp ? run.Party[target].Name+" · 다치지 않음" :
                run.Faith < DungeonRun.BlessCost ? "신앙 부족" : $"체력 +{run.BlessRecovery(run.Party[target])} · 신앙 {DungeonRun.BlessCost}";
            blessCard.SetEnabled(target >= 0 && run.CanBlessHero((HeroRole)target));
            restDetail.text = run.FatigueSteps == 0 ? "피로 없음" : run.Faith < DungeonRun.BlessCost ? "신앙 부족" :
                "피로 -"+System.Math.Min(run.FatigueSteps,DungeonRun.FatigueRelief)+" · 신앙 "+DungeonRun.BlessCost;
            restCard.SetEnabled(run.CanRelieveFatigue);

        }

        private void RefreshResult(DungeonRun run)
        {
            bool won = run.Phase == RunPhase.Victory;
            resultTitle.text = won ? "마왕을 쓰러뜨렸습니다" : "파티가 전멸했습니다";
            resultTitle.EnableInClassList("lost",!won);
            resultText.text = "최종 파티 레벨 "+run.Level+"    ·    누적 경험치 "+run.Experience+
                (!won && run.ReachedBoss ? "    ·    보스전" : "")+"    ·    "+run.StepsTaken+"걸음";
        }

        // ── History ──────────────────────────────────────────────────────

        private void OpenHistory()
        {
            history.AddToClassList("open");
            AppendHistory(game.Run);
            // Jump to the newest entry once the rows have been laid out.
            historyList.schedule.Execute(() => historyList.scrollOffset = new Vector2(0,float.MaxValue));
        }

        private void AppendHistory(DungeonRun run)
        {
            for (; loggedRows < run.Log.Count; loggedRows++)
            {
                var row = new Label(run.Log[loggedRows]);
                row.AddToClassList("log-row");
                row.EnableInClassList("shaded",loggedRows%2 == 0);
                historyList.Add(row);
            }
        }

        // ── Camera area and board input ──────────────────────────────────

        /// <summary>Tells the camera which part of the screen the party column, boss bar and bottom panel leave clear.</summary>
        private void UpdateClearArea(DungeonRun run)
        {
            var screen = hud.worldBound;
            float columnRight = party.worldBound.xMax, bossBottom = bossBar.worldBound.yMax, panelTop = bottom.resolvedStyle.display == DisplayStyle.None ? screen.yMax : bottom.worldBound.yMin;
            if (screen.width <= 0 || screen.height <= 0 || float.IsNaN(columnRight) || float.IsNaN(panelTop) ||
                float.IsNaN(bossBottom))
                return;
            // The open dialogue stage stands over the bottom panel, so the party is framed above it.
            dialogueStage ??= hud.Q("dialogue-stage");
            if (game.Presenting && dialogueStage != null && dialogueStage.resolvedStyle.display == DisplayStyle.Flex)
            {
                float stageTop = dialogueStage.worldBound.yMin;
                if (!float.IsNaN(stageTop) && stageTop > 0) panelTop = Mathf.Min(panelTop,stageTop);
            }
            float w = screen.width, h = screen.height;
            float left = (columnRight+8)/w, lower = (h-panelTop)/h, upper = (bossBottom+4)/h;
            var area = Rect.MinMaxRect(left,lower,1,1-upper);
            if (run.Phase == RunPhase.Planning)
            {
                // Mirror the HUD margins so the whole map fits around the actual screen centre.
                float verticalMargin = Mathf.Max(lower,upper);
                area = Rect.MinMaxRect(left,verticalMargin,1-left,1-verticalMargin);
            }
            game.Board.SetClearArea(area);
            if (!fitPending) return;
            // A new stage opens on the whole dungeon, framed inside the area the HUD leaves clear.
            fitPending = false;
            if (run.Phase == RunPhase.Planning) game.Board.ShowWholeDungeon();
        }

        /// <summary>True when the screen point (origin bottom-left) is over a HUD element that takes the mouse.</summary>
        private bool OverHud(Vector2 screenPoint)
        {
            var target = hud.panel;
            if (target == null) return false;
            var point = RuntimePanelUtils.ScreenToPanel(target,new Vector2(screenPoint.x,Screen.height-screenPoint.y));
            var picked = target.Pick(point);
            return picked != null && hud.Contains(picked);
        }

        private void BoardInput(DungeonRun run)
        {
            Vector2 mouse = Input.mousePosition;
            bool overBoard = !HistoryOpen && !game.Formation.IsOpen && !OverHud(mouse) && game.Board.ContainsScreenPoint(mouse);
            bool editing = overBoard && run.Phase == RunPhase.Planning;
            TilePoint tile = default;
            bool onTile = editing && game.Board.TryPickTile(mouse,out tile);

            if ((Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && overBoard) panning = true;
            if (panning && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) panning = false;
            // A drag that started on the board keeps panning even when the cursor crosses the HUD.
            if (panning && mouse != lastMouse)
            {
                cameraUsed = true;
                game.Board.Pan(lastMouse,mouse);
            }
            lastMouse = mouse;

            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0 && overBoard)
            {
                cameraUsed = true;
                game.Board.ZoomAt(mouse,Mathf.Clamp(-wheel,-1,1));
            }

            // A left click advances the dialogue stage while it is open; it must not also pick a hero.
            if (Input.GetMouseButtonDown(0) && overBoard && !game.Presenting)
            {
                if (onTile) Paint(tile);
                else if (Exploring(run) && game.Board.TryPickHero(mouse,out int hero))
                    game.SelectHero(game.SelectedHero == hero ? -1 : hero);
            }

            if (onTile && !panning)
            {
                var check = brush == MonsterKind.None ? run.CheckRemove(tile) : run.CheckPlace(tile,brush);
                game.Board.SetHover(run.Map.IsFloor(tile) ? tile : (TilePoint?)null,check == PlacementCheck.Allowed);
            }
            else game.Board.SetHover(null,false);
            UpdatePlanningTip(run, mouse, editing && !panning && !game.Presenting);
        }

        private void UpdatePlanningTip(DungeonRun run, Vector2 mouse, bool enabled)
        {
            Vector2 anchor = default;
            var target = enabled ? game.Board.PickPlanningTarget(mouse, out anchor) : null;
            if (target != shownTarget)
            {
                shownTarget = target;
                Show(planningTip, target != null);
                if (target != null) DescribePlanningTarget(run, target);
            }
            if (target == null) return;
            var point = RuntimePanelUtils.ScreenToPanel(hud.panel, new Vector2(anchor.x, Screen.height-anchor.y));
            point = hud.WorldToLocal(point);
            var area = hud.contentRect;
            float width = planningTip.layout.width, height = planningTip.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height)) return;
            float x = point.x + 18;
            if (x + width > area.xMax - 4) x = point.x - width - 18;
            planningTip.style.left = Mathf.Clamp(x, 4, area.xMax - width - 4);
            planningTip.style.top = Mathf.Clamp(point.y - height/2, 4, area.yMax - height - 4);
        }

        private void DescribePlanningTarget(DungeonRun run, DungeonBoard.PlanningTarget target)
        {
            var kind = target.Kind;
            bool enemy = kind == DungeonBoard.PlanningKind.Monster || kind == DungeonBoard.PlanningKind.Patrol || kind == DungeonBoard.PlanningKind.Boss;
            planningTip.EnableInClassList("tip-enemy", enemy);
            planningTip.EnableInClassList("tip-encounter", !enemy && kind != DungeonBoard.PlanningKind.Chest);
            (tipKind.text, tipName.text, tipDescription.text, tipFact.text) = kind switch
            {
                DungeonBoard.PlanningKind.Chest => ("보물 · 고정 배치", "보물 상자", "전투가 끝난 뒤 파티가 상자 칸에 서면 장비를 얻습니다.", "보상　장비 1개"),
                DungeonBoard.PlanningKind.Spring => ("시설 · 고정 배치", "샘물", "지친 파티가 들러 물을 마시면 피로가 줄어듭니다.", "사용　스테이지당 1회"),
                DungeonBoard.PlanningKind.Merchant => ("NPC · 거래", "떠돌이 상인", "파티가 다가가면 강장차나 여행빵을 살지 의논합니다.", "가격　금화 1닢"),
                DungeonBoard.PlanningKind.Statue => ("조우 · 고정 배치", "금이 간 조각상", "봉인을 깨면 금화를 얻고 파티에 축복이나 저주가 깃듭니다.", "효과 지속　이번 스테이지"),
                DungeonBoard.PlanningKind.Boss => ("적 · 스테이지 보스", run.BossName, "보스방을 지키다가 용사를 발견하면 방 밖까지 추격합니다.", $"최대 체력　{run.BossMaxHp}"),
                _ => (kind == DungeonBoard.PlanningKind.Patrol ? "적 · 고정 순회조" : "적 · 배치 몬스터",
                    DungeonRun.MonsterName(target.Monster), brushCards[target.Monster].Q<Label>(className:"placement-description").text,
                    kind == DungeonBoard.PlanningKind.Patrol ? "위협 예산 소모 없음" : $"위협 비용　{DungeonRun.Cost(target.Monster)}")
            };
        }

        private void Paint(TilePoint tile)
        {
            switch (game.Place(tile,brush))
            {
                case PlacementCheck.OverBudget: notice = "위협 예산이 부족합니다."; break;
                case PlacementCheck.NearEntrance: notice = "입구 근처에는 몬스터를 둘 수 없습니다."; break;
                case PlacementCheck.NotFloor: notice = "벽에는 몬스터를 둘 수 없습니다."; break;
                case PlacementCheck.NothingToRemove: notice = "이 칸에는 되돌릴 몬스터가 없습니다."; break;
                case PlacementCheck.NotInRoster:
                    notice = $"{HeroTraits.Particle(DungeonRun.MonsterName(brush),"은","는")} 이 스테이지에 배치할 수 없습니다.";
                    break;
                default: notice = ""; break;
            }
            noticeUntil = Time.unscaledTime+NoticeSeconds;
        }

        private static void Show(VisualElement element,bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
