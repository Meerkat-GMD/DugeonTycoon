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
        private const int DetailFontSize = 11, SmallestDetailFontSize = 8;

        private static readonly string[] RoleNames = { "탱커", "힐러", "근딜", "원딜" };

        private DungeonGame game;
        private UIDocument document;
        private PanelSettings panel;
        private VisualElement hud, bossBar, bossFill, party, bottom, history;
        private VisualElement planningPage, interventionPage, resultPage, speedGroup, info;
        private Label stageNumber, stageName, bossTag, bossName, bossHp, pauseBanner, aiMode;
        private Label category, hint, budget, faith, trials, summaryTitle, summaryDetail;
        private Label infoHeading, infoText, infoNote, resultTitle, resultText;
        private Button follow, primary, collapse, goblinCard, ogreCard, removeCard, trialCard, blessCard;
        private Label trialDetail, blessDetail;
        private readonly Button[] speedButtons = new Button[3];
        private ScrollView historyList;
        private readonly HeroCard[] heroes = new HeroCard[4];

        private MonsterKind brush = MonsterKind.Goblin;
        private bool panning, fitPending;
        private Vector2 lastMouse;
        private string notice = "";
        private float noticeUntil;
        private int knownStage, loggedRows;
        private DungeonRun knownRun;
        private int bossMaxHp;

        private sealed class HeroCard
        {
            public VisualElement Root, Bubble, Health, Growth;
            public Label Portrait, Name, Hp, Level, Line;
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
        }

        private void OnDestroy()
        {
            if (panel) Destroy(panel);
        }

        // ── Binding ──────────────────────────────────────────────────────

        private void Bind(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            hud = root.Q("hud");
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
            category = root.Q<Label>("category");
            hint = root.Q<Label>("hint");
            budget = root.Q<Label>("budget");
            faith = root.Q<Label>("faith");
            trials = root.Q<Label>("trials");
            speedGroup = root.Q("speed");
            primary = root.Q<Button>("primary");
            collapse = root.Q<Button>("collapse");
            planningPage = root.Q("planning");
            interventionPage = root.Q("intervention");
            resultPage = root.Q("result");
            goblinCard = root.Q<Button>("brush-goblin");
            ogreCard = root.Q<Button>("brush-ogre");
            removeCard = root.Q<Button>("brush-remove");
            summaryTitle = root.Q<Label>("summary-title");
            summaryDetail = root.Q<Label>("summary-detail");
            trialCard = root.Q<Button>("trial");
            blessCard = root.Q<Button>("bless");
            trialDetail = trialCard.Q<Label>("detail");
            blessDetail = blessCard.Q<Label>("detail");
            info = root.Q("info");
            infoHeading = root.Q<Label>("info-heading");
            infoText = root.Q<Label>("info-text");
            infoNote = root.Q<Label>("info-note");
            resultTitle = root.Q<Label>("result-title");
            resultText = root.Q<Label>("result-text");
            history = root.Q("history");
            historyList = root.Q<ScrollView>("history-list");

            for (int i = 0; i < heroes.Length; i++)
            {
                var card = root.Q("hero-"+i);
                heroes[i] = new HeroCard {
                    Root = card, Bubble = card.Q("bubble"), Health = card.Q("health"), Growth = card.Q("growth"),
                    Portrait = card.Q<Label>("portrait"), Name = card.Q<Label>("name"), Hp = card.Q<Label>("hp"),
                    Level = card.Q<Label>("level"), Line = card.Q<Label>("line")
                };
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
            primary.clicked += () =>
            {
                if (game.Run.Phase == RunPhase.Planning) game.Depart();
                else game.Restart();
            };
            goblinCard.clicked += () => brush = MonsterKind.Goblin;
            ogreCard.clicked += () => brush = MonsterKind.Ogre;
            removeCard.clicked += () => brush = MonsterKind.None;
            trialCard.clicked += () => game.Trial();
            blessCard.clicked += () => game.Bless();
        }

        private void PickHero(int index)
        {
            var run = game.Run;
            if (!Exploring(run) || run.Party[index].Hp == 0) return;
            game.SelectHero(game.SelectedHero == index ? -1 : index);
        }

        // ── Frame ────────────────────────────────────────────────────────

        private void Update()
        {
            if (!game || game.Run == null || !game.Board || hud == null) return;
            var run = game.Run;
            if (!ReferenceEquals(knownRun,run) || knownStage != run.Stage) ResetForStage(run);

            float scale = Mathf.Min(Screen.width/MinWidth,Screen.height/MinHeight,
                Mathf.Max(Screen.height/ReferenceHeight,ReadableScale));
            if (scale > 0 && !Mathf.Approximately(panel.scale,scale)) panel.scale = scale;

            Keys(run);
            Refresh(run);
            UpdateClearArea(run);
            BoardInput(run);
        }

        private void ResetForStage(DungeonRun run)
        {
            if (!ReferenceEquals(knownRun,run))
            {
                historyList.Clear();
                loggedRows = 0;
            }
            brush = MonsterKind.Goblin;
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
            if (!Exploring(run)) return;
            if (Input.GetKeyDown(KeyCode.Space)) game.TogglePause();
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) game.SetSpeed(1);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) game.SetSpeed(2);
        }

        private void Refresh(DungeonRun run)
        {
            bool planning = run.Phase == RunPhase.Planning;
            bool finished = run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat;
            bool exploring = Exploring(run);

            stageNumber.text = "스테이지 "+run.Stage+" / 2";
            stageName.text = run.Stage == 1 ? "성문으로 가는 길" : "마왕의 성채";

            // The live boss only exists once the party departs; until then the bar shows the stage's boss at full health.
            int hp = run.Boss != null ? run.Boss.Hp : bossMaxHp;
            bool engaged = run.FightingBoss;
            bossBar.EnableInClassList("engaged",engaged);
            bossTag.text = engaged ? "보스전" : "BOSS";
            bossName.text = run.BossName;
            bossHp.text = hp+" / "+bossMaxHp;
            bossFill.style.width = Length.Percent(bossMaxHp == 0 ? 0 : 100f*hp/bossMaxHp);

            string banner = !exploring ? "" : run.PendingAdversity ? "역경 판정 중  ·  시간이 멈췄습니다" :
                game.Speed == 0 ? "일시정지  ·  스페이스로 다시 진행" : "";
            Show(pauseBanner,banner.Length > 0);
            pauseBanner.text = banner;

            Show(follow,exploring);
            follow.text = game.Board.FollowParty ? "따라가기  켬" : "따라가기  끔";
            follow.EnableInClassList("on",game.Board.FollowParty);

            float growth = (float)run.ExperienceIntoLevel/DungeonRun.ExperiencePerLevel;
            for (int i = 0; i < heroes.Length; i++) RefreshHero(run,run.Party[i],i,growth);
            aiMode.text = game.AiMode+(game.Thinking ? "  ·  판단 중" : "");
            aiMode.EnableInClassList("thinking",game.Thinking);

            category.text = planning ? "몬스터 배치" : finished ? "원정 결과" : "사회자 개입";
            bool warning = notice.Length > 0 && Time.unscaledTime < noticeUntil;
            hint.text = warning ? notice : planning ? "바닥 클릭 배치  ·  우클릭·휠 드래그 이동  ·  휠 확대·축소" :
                run.Log.Count == 0 ? "" : run.Log[run.Log.Count-1].Replace('\n',' ');
            hint.EnableInClassList("warning",warning);
            budget.text = run.Budget.ToString();
            faith.text = run.Faith.ToString();
            trials.text = run.TrialsLeft+" / 2";

            Show(speedGroup,exploring);
            for (int speed = 0; speed < speedButtons.Length; speed++)
                speedButtons[speed].EnableInClassList("on",game.Speed == speed);
            Show(primary,planning || finished);
            primary.text = planning ? "원정 출발  ›" : "새 원정";
            collapse.text = bottom.ClassListContains("collapsed") ? "▲" : "▼";

            Show(planningPage,planning);
            Show(interventionPage,exploring);
            Show(resultPage,finished);
            if (planning) RefreshPlanning(run);
            else if (finished) RefreshResult(run);
            else RefreshIntervention(run);

            if (HistoryOpen) AppendHistory(run);
        }

        private void RefreshHero(DungeonRun run,HeroState hero,int index,float growth)
        {
            var card = heroes[index];
            bool down = hero.Hp == 0;
            card.Root.EnableInClassList("down",down);
            card.Root.EnableInClassList("pickable",Exploring(run) && !down);
            // The choice only matters while help can be given, so the frame shows only during an expedition.
            card.Root.EnableInClassList("picked",Exploring(run) && game.SelectedHero == index);
            card.Name.text = RoleNames[index]+"  "+hero.Name;
            card.Hp.text = down ? "쓰러짐" : "HP "+hero.Hp+" / "+hero.MaxHp;
            card.Level.text = "Lv."+run.Level;
            card.Health.style.width = Length.Percent(hero.MaxHp == 0 ? 0 : 100f*hero.Hp/hero.MaxHp);
            card.Growth.style.width = Length.Percent(100f*growth);

            // Speech floats beside the card for a few seconds after a hero says something new.
            string line = hero.LastLine ?? "";
            if (line != card.Heard)
            {
                card.Heard = line;
                card.HeardAt = Time.unscaledTime;
            }
            bool speaking = !down && !string.IsNullOrWhiteSpace(line) && Time.unscaledTime-card.HeardAt <= LineSeconds;
            Show(card.Bubble,speaking);
            if (speaking) card.Line.text = line;
        }

        private void RefreshPlanning(DungeonRun run)
        {
            goblinCard.EnableInClassList("chosen",brush == MonsterKind.Goblin);
            ogreCard.EnableInClassList("chosen",brush == MonsterKind.Ogre);
            removeCard.EnableInClassList("chosen",brush == MonsterKind.None);
            int goblins = 0, ogres = 0;
            foreach (var placement in run.Placements)
                if (placement.Kind == MonsterKind.Ogre) ogres++; else goblins++;
            bool empty = run.Placements.Count == 0;
            summaryTitle.text = empty ? "배치한 몬스터 없음" : "배치 "+run.Placements.Count+"마리";
            summaryDetail.text = empty ? "비워 두고 출발할 수도 있습니다" : "고블린 "+goblins+"   ·   오우거 "+ogres;
        }

        private void RefreshIntervention(DungeonRun run)
        {
            trialCard.SetEnabled(run.TrialsLeft > 0 && !run.PendingAdversity);
            SetDetail(trialDetail,"남은 "+run.TrialsLeft+"회");
            int target = game.SelectedHero;
            SetDetail(blessDetail,target < 0 ? "용사를 먼저 고르세요" :
                run.Party[target].Hp == run.Party[target].MaxHp ? run.Party[target].Name+" · 다치지 않음" :
                run.Faith < DungeonRun.BlessCost ? "신앙 부족" : run.Party[target].Name+" · 신앙 "+DungeonRun.BlessCost);
            blessCard.SetEnabled(target >= 0 && run.CanBlessHero((HeroRole)target));

            var fork = run.PendingFork;
            if (run.PendingAdversity)
                Detail("낙석 역경   ·   용사 대응 중",game.TrialAnnouncement);
            else if (fork != null)
            {
                var options = new System.Text.StringBuilder();
                foreach (var option in fork.Options)
                {
                    if (options.Length > 0) options.Append("     ");
                    options.Append(option.Label);
                    if (option.VisibleMonsters > 0) options.Append("  (적 "+option.VisibleMonsters+")");
                }
                Detail((fork.Backtracking ? "막다른 길" : "갈림길")+"   ·   용사들이 논의하는 중",options.ToString());
            }
            else if (Time.time < game.SpotlightUntil && !string.IsNullOrWhiteSpace(game.SpotlightText))
                Detail(game.SpotlightTitle,game.SpotlightText);
            else
            {
                string where = run.FightingBoss ? "보스와 교전 중" :
                    run.Phase == RunPhase.Combat ? "교전 중  ·  적 "+run.Engaged.Count() :
                    run.IsSeen(run.Map.BossTile) ? "보스방으로 향하는 중" : "보스방을 찾는 중";
                var engaged = run.Engaged.OrderByDescending(m => m.IsBoss).ThenBy(m => m.Hp).Take(3)
                    .Select(m => m.Name+" "+m.Hp+"/"+m.MaxHp).ToList();
                info.RemoveFromClassList("detail");
                infoHeading.text = engaged.Count > 0 ? string.Join("   ·   ",engaged) : where;
                infoText.text = run.StepsTaken+"걸음   ·   발견한 몬스터 "+run.SpottedCount+" / "+run.Placements.Count+
                    "   ·   처치 "+run.DefeatedCount+"   ·   갈림길 "+run.ForkCount+"번";
                infoNote.text = "낙석 역경    d20 + 능력 보정 ≥ 난이도 "+run.TrialDifficulty;
            }
        }

        private void Detail(string heading,string body)
        {
            info.AddToClassList("detail");
            infoHeading.text = heading;
            infoText.text = body;
        }

        /// <summary>Card details step their font down rather than being cut off at the card's edge.</summary>
        private static void SetDetail(Label label,string value)
        {
            if (label.text == value && label.userData is string fitted && fitted == value) return;
            label.text = value;
            float room = label.parent.resolvedStyle.width-8;
            float size = label.resolvedStyle.fontSize;
            if (float.IsNaN(room) || room <= 0 || float.IsNaN(size) || size <= 0) return;
            float width = label.MeasureTextSize(value,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;
            // Text width scales with font size, so one measurement at the current size picks the size that fits.
            int fit = width <= 0 ? DetailFontSize : Mathf.FloorToInt(size*room/width);
            label.style.fontSize = Mathf.Clamp(fit,SmallestDetailFontSize,DetailFontSize);
            label.userData = value;
        }

        private void RefreshResult(DungeonRun run)
        {
            bool won = run.Phase == RunPhase.Victory;
            resultTitle.text = won ? "마왕을 쓰러뜨렸습니다" : "파티가 전멸했습니다";
            resultTitle.EnableInClassList("lost",!won);
            resultText.text = "최종 파티 레벨 "+run.Level+"    ·    누적 경험치 "+run.Experience+
                (run.ReachedBoss ? "    ·    보스전" : "")+"    ·    "+run.StepsTaken+"걸음";
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
            float columnRight = party.worldBound.xMax, bossBottom = bossBar.worldBound.yMax, panelTop = bottom.worldBound.yMin;
            if (screen.width <= 0 || screen.height <= 0 || float.IsNaN(columnRight) || float.IsNaN(panelTop) ||
                float.IsNaN(bossBottom))
                return;
            float w = screen.width, h = screen.height;
            game.Board.SetClearArea(Rect.MinMaxRect((columnRight+8)/w,(h-panelTop)/h,1,1-(bossBottom+4)/h));
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
            bool overBoard = !HistoryOpen && !OverHud(mouse) && game.Board.ContainsScreenPoint(mouse);
            bool editing = overBoard && run.Phase == RunPhase.Planning;
            TilePoint tile = default;
            bool onTile = editing && game.Board.TryPickTile(mouse,out tile);

            if ((Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && overBoard) panning = true;
            if (panning && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) panning = false;
            // A drag that started on the board keeps panning even when the cursor crosses the HUD.
            if (panning && mouse != lastMouse) game.Board.Pan(lastMouse,mouse);
            lastMouse = mouse;

            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0 && overBoard) game.Board.ZoomAt(mouse,Mathf.Clamp(-wheel,-1,1));

            if (Input.GetMouseButtonDown(0) && overBoard)
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
        }

        private void Paint(TilePoint tile)
        {
            switch (game.Place(tile,brush))
            {
                case PlacementCheck.OverBudget: notice = "위협 예산이 부족합니다."; break;
                case PlacementCheck.NearEntrance: notice = "입구 근처에는 몬스터를 둘 수 없습니다."; break;
                case PlacementCheck.NotFloor: notice = "벽에는 몬스터를 둘 수 없습니다."; break;
                case PlacementCheck.NothingToRemove: notice = "이 칸에는 되돌릴 몬스터가 없습니다."; break;
                default: notice = ""; break;
            }
            noticeUntil = Time.unscaledTime+NoticeSeconds;
        }

        private static void Show(VisualElement element,bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
