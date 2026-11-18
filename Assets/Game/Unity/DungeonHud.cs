using System.Collections.Generic;
using System.Linq;
using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    /// <summary>
    /// The dungeon fills the whole screen and the HUD floats on top of it: boss bar at the top centre,
    /// party cards down the left, and a collapsible category panel along the bottom. Only the pieces
    /// actually drawn take mouse input; everywhere else the board pans, zooms and takes placements.
    /// </summary>
    public sealed class DungeonHud : MonoBehaviour
    {
        private static readonly Color Ink = Hex(0x151B20), Panel = Hex(0x202A30), Card = Hex(0x29343A);
        private static readonly Color Edge = Hex(0x435056), Muted = Hex(0xB3BEBB), Paper = Hex(0xF0EAE0);
        private static readonly Color Gold = Hex(0xE8BE76), Teal = Hex(0x7BD5BF), Ember = Hex(0xEC9179);
        private static readonly Color Health = Hex(0xD9534F), Growth = Hex(0xF2C14E);
        private static readonly Color[] RoleColors = { Hex(0x74C5C4), Hex(0xE2C77D), Hex(0xDB9AA8), Hex(0xA2CD88) };
        private static readonly string[] RoleNames = { "탱커", "힐러", "근딜", "원딜" };
        private static readonly string[] RoleGlyphs = { "탱", "힐", "근", "원" };

        // Layout, in GUI units. The GUI is scaled so the screen is at least 1000 x 640 units.
        private const float Margin = 10;
        private const float CardWidth = 170, CardHeight = 50, CardGap = 10, Portrait = 46;
        private const float BossTop = 8, BossHeight = 38;
        private const float TabHeight = 34, ContentHeight = 116;
        private const float ElementWidth = 104, ElementHeight = 90, ElementGap = 8;
        private const float LineSeconds = 6;

        private DungeonGame game;
        private Font face;
        private GUIStyle text;
        private MonsterKind brush = MonsterKind.Goblin;
        private bool history, blocked, panning, collapsed, fitPending;
        private Vector2 scroll;
        private string notice;
        private float noticeUntil, scale, width, height;
        private int knownStage;
        private DungeonRun knownRun;
        private string bossName = "";
        private int bossHp, bossMaxHp;
        private readonly string[] heardLines = new string[4];
        private readonly float[] heardAt = new float[4];
        private readonly List<Rect> solid = new List<Rect>();
        private readonly List<float> rowOffsets = new List<float>();
        private float historyWidth;

        private void Awake()
        {
            game = GetComponent<DungeonGame>();
            face = Font.CreateDynamicFontFromOSFont(new[] {
                "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial Unicode MS", "Arial"
            }, 19);
        }

        private void OnDestroy()
        {
            if (face) Destroy(face);
        }

        private void OnGUI()
        {
            if (!game || game.Run == null || !game.Board) return;
            var run = game.Run;
            if (!ReferenceEquals(knownRun,run) || knownStage != run.Stage)
            {
                brush = MonsterKind.Goblin;
                panning = false;
                notice = "";
                history = false;
                rowOffsets.Clear();
                for (int i=0;i<heardLines.Length;i++) heardLines[i] = "";
                bossName = run.BossName;
                bossHp = bossMaxHp = run.BossMaxHp;
                knownRun = run;
                knownStage = run.Stage;
                fitPending = true;
            }
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) {
                    font = face ? face : GUI.skin.font, clipping = TextClipping.Clip,
                    richText = false, padding = new RectOffset(), margin = new RectOffset()
                };
            }
            scale = Mathf.Min(Screen.width/1000f,Screen.height/640f);
            if (scale <= 0) return;
            width = Screen.width/scale;
            height = Screen.height/scale;

            float panelTop = height-Margin-TabHeight-(collapsed ? 0 : ContentHeight);
            float columnRight = Margin+CardWidth+8;
            float bossBottom = BossTop+BossHeight+4;
            // The camera draws the full screen; it only needs to know which part the HUD leaves clear.
            game.Board.SetClearArea(Rect.MinMaxRect(columnRight/width,(height-panelTop)/height,1,1-bossBottom/height));
            if (fitPending)
            {
                // A new stage opens on the whole dungeon, framed inside the area the HUD leaves clear.
                fitPending = false;
                if (run.Phase == RunPhase.Planning) game.Board.ShowWholeDungeon();
            }

            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUI.color = Color.white;
            blocked = history;
            solid.Clear();

            TimeKeys(run);
            StageTitle(run);
            BossBar(run);
            PauseBanner(run);
            ViewButtons(run);
            Party(run);
            BottomPanel(run,panelTop);
            BoardInput(run);
            if (history) History(run);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        // ── Top ──────────────────────────────────────────────────────────

        private void StageTitle(DungeonRun run)
        {
            Shadowed(new Rect(Margin+2,6,280,16),"스테이지 "+run.Stage+" / 2",11,Gold,true);
            Shadowed(new Rect(Margin+2,21,280,24),run.Stage == 1 ? "성문으로 가는 길" : "마왕의 성채",17,Paper,true);
        }

        private void BossBar(DungeonRun run)
        {
            // The live boss only exists once the party departs; until then the bar shows the stage's boss at full health.
            if (run.Boss != null) bossHp = run.Boss.Hp;
            float w = Mathf.Clamp(width*.42f,300,560);
            var box = new Rect((width-w)/2,BossTop,w,BossHeight);
            Solid(box);
            Fill(box,Fade(Ink,.78f));
            bool engaged = run.FightingBoss;
            Write(new Rect(box.x+10,box.y+3,w-20,20),bossName,14,engaged ? Ember : Paper,true,TextAnchor.MiddleCenter);
            Write(new Rect(box.xMax-110,box.y+4,100,18),bossHp+" / "+bossMaxHp,11,Muted,false,TextAnchor.MiddleRight);
            Write(new Rect(box.x+10,box.y+4,110,18),engaged ? "보스전" : "BOSS",11,engaged ? Ember : Muted,true);
            Meter(new Rect(box.x+10,box.y+26,w-20,6),bossMaxHp == 0 ? 0 : (float)bossHp/bossMaxHp,Health);
        }

        private static bool Exploring(DungeonRun run) => run.Phase == RunPhase.Travel || run.Phase == RunPhase.Combat;

        /// <summary>Space pauses or resumes, 1 and 2 pick the speed.</summary>
        private void TimeKeys(DungeonRun run)
        {
            var e = Event.current;
            if (history || e.type != EventType.KeyDown || !Exploring(run)) return;
            switch (e.keyCode)
            {
                case KeyCode.Space: game.TogglePause(); break;
                case KeyCode.Alpha1: case KeyCode.Keypad1: game.SetSpeed(1); break;
                case KeyCode.Alpha2: case KeyCode.Keypad2: game.SetSpeed(2); break;
                default: return;
            }
            e.Use();
        }

        private void PauseBanner(DungeonRun run)
        {
            if (!Exploring(run)) return;
            string banner = run.PendingAdversity ? "역경 판정 중  ·  시간이 멈췄습니다" :
                game.Speed == 0 ? "일시정지  ·  스페이스로 다시 진행" : "";
            if (banner.Length == 0) return;
            var r = new Rect((width-300)/2,BossTop+BossHeight+8,300,26);
            Fill(r,Fade(Ink,.85f));
            Fill(new Rect(r.x,r.yMax-2,r.width,2),Gold);
            Write(r,banner,13,Gold,true,TextAnchor.MiddleCenter);
        }

        private void ViewButtons(DungeonRun run)
        {
            float w = 78, gap = 6, x = width-Margin-w;
            if (SmallButton(new Rect(x,BossTop+6,w,26),"기록",Card,Paper))
            {
                history = true;
                scroll = new Vector2(0,float.MaxValue);
            }
            x -= w+gap;
            if (SmallButton(new Rect(x,BossTop+6,w,26),"전체 보기",Card,Paper)) game.Board.ShowWholeDungeon();
            if (run.Phase != RunPhase.Travel && run.Phase != RunPhase.Combat) return;
            x -= w+16+gap;
            bool following = game.Board.FollowParty;
            if (SmallButton(new Rect(x,BossTop+6,w+16,26),following ? "따라가기  켬" : "따라가기  끔",
                    following ? Hex(0x35534D) : Card,following ? Teal : Paper))
                game.Board.SetFollowParty(!following);
        }

        // ── Party column ─────────────────────────────────────────────────

        private void Party(DungeonRun run)
        {
            float y = BossTop+BossHeight+14;
            float growth = (float)run.ExperienceIntoLevel/DungeonRun.ExperiencePerLevel;
            for (int i=0;i<run.Party.Count;i++)
            {
                HeroCard(run,run.Party[i],i,new Rect(Margin,y,CardWidth,CardHeight),growth);
                y += CardHeight+CardGap;
            }
            Shadowed(new Rect(Margin+2,y-4,CardWidth+120,18),game.AiMode+(game.Thinking ? "  ·  판단 중" : ""),11,
                game.Thinking ? Gold : Muted);
        }

        private void HeroCard(DungeonRun run,HeroState hero,int index,Rect card,float growth)
        {
            bool down = hero.Hp == 0;
            Solid(card);
            bool picked = game.SelectedHero == index;
            bool pickable = Exploring(run) && !down;
            bool hover = pickable && !blocked && card.Contains(Event.current.mousePosition);
            Fill(card,Fade(hover ? Card : Ink,.78f));
            if (picked) Frame(card,2,Gold);
            // Picking a hero chooses who the GM's help goes to; clicking again clears the choice.
            if (pickable && Hit(card)) game.SelectHero(picked ? -1 : index);
            var portrait = new Rect(card.x+2,card.y+2,Portrait,Portrait);
            var accent = down ? Edge : RoleColors[index];
            Fill(portrait,accent);
            Fill(new Rect(portrait.x+3,portrait.y+3,portrait.width-6,portrait.height-6),down ? Card : Color.Lerp(accent,Ink,.25f));
            Write(portrait,RoleGlyphs[index],20,down ? Muted : Ink,true,TextAnchor.MiddleCenter);

            float x = portrait.xMax+8, w = card.xMax-x-6;
            Write(new Rect(x,card.y+3,w,17),RoleNames[index]+"  "+hero.Name,13,down ? Muted : Paper,true);
            Write(new Rect(x,card.y+19,w,13),down ? "쓰러짐" : "HP "+hero.Hp+" / "+hero.MaxHp,10,down ? Ember : Muted);
            Write(new Rect(x,card.y+19,w,13),"Lv."+run.Level,10,Growth,false,TextAnchor.MiddleRight);
            Meter(new Rect(x,card.y+34,w,5),hero.MaxHp == 0 ? 0 : (float)hero.Hp/hero.MaxHp,Health);
            Meter(new Rect(x,card.y+42,w,4),growth,Growth);

            // Speech floats beside the card for a few seconds after a hero says something new.
            string line = hero.LastLine ?? "";
            if (line != heardLines[index])
            {
                heardLines[index] = line;
                heardAt[index] = Time.unscaledTime;
            }
            if (down || string.IsNullOrWhiteSpace(line) || Time.unscaledTime-heardAt[index] > LineSeconds) return;
            ConfigureText(12,Ink,false,TextAnchor.UpperLeft,true);
            float bubbleWidth = Mathf.Min(240,text.CalcSize(new GUIContent(line)).x+16);
            float bubbleHeight = text.CalcHeight(new GUIContent(line),bubbleWidth-16)+10;
            var bubble = new Rect(card.xMax+8,card.y+(card.height-bubbleHeight)*.5f,bubbleWidth,bubbleHeight);
            Fill(bubble,Fade(Paper,.92f));
            Fill(new Rect(bubble.x-4,bubble.center.y-3,4,6),Fade(Paper,.92f));
            Write(new Rect(bubble.x+8,bubble.y+5,bubbleWidth-16,bubbleHeight-10),line,12,Ink,false,TextAnchor.UpperLeft,true);
        }

        // ── Bottom panel ─────────────────────────────────────────────────

        private void BottomPanel(DungeonRun run,float top)
        {
            float x = Margin, w = width-2*Margin;
            var tabs = new Rect(x,top,w,TabHeight);
            var content = new Rect(x,top+TabHeight,w,ContentHeight);
            Solid(collapsed ? tabs : new Rect(x,top,w,TabHeight+ContentHeight));
            Fill(tabs,Fade(Ink,.94f));

            bool planning = run.Phase == RunPhase.Planning;
            bool finished = run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat;
            string category = planning ? "몬스터 배치" : finished ? "원정 결과" : "사회자 개입";
            var tab = new Rect(x,top,118,TabHeight);
            Fill(tab,Fade(Panel,.96f));
            Fill(new Rect(tab.x,tab.y,tab.width,2),Gold);
            Write(tab,category,14,Paper,true,TextAnchor.MiddleCenter);

            // Right side of the tab bar: collapse toggle, the phase's main action, then the run's resources.
            float right = tabs.xMax-6;
            var toggle = new Rect(right-30,top+5,30,TabHeight-10);
            if (SmallButton(toggle,collapsed ? "▲" : "▼",Card,Paper)) collapsed = !collapsed;
            right = toggle.x-8;
            if (planning || finished)
            {
                var action = new Rect(right-124,top+4,124,TabHeight-8);
                if (planning && SmallButton(action,"원정 출발  ›",Gold,Ink)) game.Depart();
                if (finished && SmallButton(action,"새 원정",Gold,Ink)) game.Restart();
                right = action.x-14;
            }
            else
            {
                // Speed: pause, 1x, 2x. An adversity freezes time on its own and resumes at the chosen speed.
                string[] labels = { "일시정지", "1배속", "2배속" };
                for (int speed = 2; speed >= 0; speed--)
                {
                    float buttonWidth = speed == 0 ? 66 : 52;
                    var button = new Rect(right-buttonWidth,top+5,buttonWidth,TabHeight-10);
                    bool current = game.Speed == speed;
                    if (SmallButton(button,labels[speed],current ? Hex(0x35534D) : Card,current ? Teal : Paper))
                        game.SetSpeed(speed);
                    right = button.x-4;
                }
                right -= 12;
            }
            right = Stat(right,"역경",run.TrialsLeft+" / 2",Paper,top);
            right = Stat(right,"신앙",run.Faith.ToString(),Teal,top);
            right = Stat(right,"위협 예산",run.Budget.ToString(),Gold,top);

            bool warning = !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil;
            string hint = warning ? notice : planning ? "바닥 클릭 배치  ·  우클릭·휠 드래그 이동  ·  휠 확대·축소" :
                run.Log.Count == 0 ? "" : run.Log[run.Log.Count-1].Replace('\n',' ');
            Write(new Rect(tab.xMax+14,top,right-tab.xMax-24,TabHeight),hint,12,warning ? Ember : Muted);

            if (collapsed) return;
            Fill(content,Fade(Panel,.94f));
            if (planning) PlanningContent(run,content);
            else if (finished) ResultContent(run,content);
            else InterventionContent(run,content);
        }

        private float Stat(float right,string title,string value,Color accent,float top)
        {
            ConfigureText(15,accent,true,TextAnchor.MiddleLeft,false);
            float valueWidth = text.CalcSize(new GUIContent(value)).x;
            ConfigureText(11,Muted,false,TextAnchor.MiddleLeft,false);
            float titleWidth = text.CalcSize(new GUIContent(title)).x;
            float x = right-valueWidth-titleWidth-6;
            Write(new Rect(x,top,titleWidth+2,TabHeight),title,11,Muted);
            Write(new Rect(x+titleWidth+6,top,valueWidth+2,TabHeight),value,15,accent,true);
            return x-16;
        }

        private void PlanningContent(DungeonRun run,Rect content)
        {
            float x = content.x+12, y = content.y+(content.height-ElementHeight)*.5f;
            BrushCard(new Rect(x,y,ElementWidth,ElementHeight),MonsterKind.Goblin,"고",RoleColors[3],"고블린",
                "비용 "+DungeonRun.Cost(MonsterKind.Goblin));
            x += ElementWidth+ElementGap;
            BrushCard(new Rect(x,y,ElementWidth,ElementHeight),MonsterKind.Ogre,"오",Ember,"오우거",
                "비용 "+DungeonRun.Cost(MonsterKind.Ogre));
            x += ElementWidth+ElementGap;
            BrushCard(new Rect(x,y,ElementWidth,ElementHeight),MonsterKind.None,"×",Muted,"되돌리기","한 마리씩");

            int goblins = 0, ogres = 0;
            foreach (var placement in run.Placements)
                if (placement.Kind == MonsterKind.Ogre) ogres++; else goblins++;
            float summaryWidth = 300;
            var summary = new Rect(content.xMax-12-summaryWidth,content.y+14,summaryWidth,content.height-28);
            Write(new Rect(summary.x,summary.y,summaryWidth,24),
                run.Placements.Count == 0 ? "배치한 몬스터 없음" : "배치 "+run.Placements.Count+"마리",17,Paper,true,
                TextAnchor.MiddleRight);
            Write(new Rect(summary.x,summary.y+28,summaryWidth,20),
                run.Placements.Count == 0 ? "비워 두고 출발할 수도 있습니다" : "고블린 "+goblins+"   ·   오우거 "+ogres,13,
                Muted,false,TextAnchor.MiddleRight);
            Write(new Rect(summary.x,summary.y+52,summaryWidth,20),"입구 근처와 벽에는 둘 수 없습니다",12,Muted,false,
                TextAnchor.MiddleRight);
        }

        private void BrushCard(Rect r,MonsterKind kind,string glyph,Color accent,string title,string detail)
        {
            bool chosen = brush == kind;
            if (ElementCard(r,glyph,accent,title,detail,chosen,true)) brush = kind;
        }

        private void InterventionContent(DungeonRun run,Rect content)
        {
            float x = content.x+12, y = content.y+(content.height-ElementHeight)*.5f;
            bool canTrial = run.TrialsLeft > 0 && !run.PendingAdversity;
            if (ElementCard(new Rect(x,y,ElementWidth,ElementHeight),"d20",Gold,"낙석 역경","남은 "+run.TrialsLeft+"회",
                    false,canTrial))
                game.Trial();
            x += ElementWidth+ElementGap;
            int target = game.SelectedHero;
            string blessDetail = target < 0 ? "용사를 먼저 고르세요" :
                run.Party[target].Hp == run.Party[target].MaxHp ? run.Party[target].Name+" · 다치지 않음" :
                run.Faith < DungeonRun.BlessCost ? "신앙 부족" : run.Party[target].Name+" · 신앙 "+DungeonRun.BlessCost;
            if (ElementCard(new Rect(x,y,ElementWidth,ElementHeight),"+",Teal,"축복",blessDetail,
                    false,target >= 0 && run.CanBlessHero((HeroRole)target)))
                game.Bless();
            x += ElementWidth+16;
            Fill(new Rect(x,content.y+14,1,content.height-28),Edge);
            var info = new Rect(x+16,content.y+12,content.xMax-x-30,content.height-24);

            var fork = run.PendingFork;
            if (run.PendingAdversity)
                Detail(info,"낙석 역경   ·   용사 대응 중",game.TrialAnnouncement);
            else if (fork != null)
            {
                var options = new System.Text.StringBuilder();
                foreach (var option in fork.Options)
                {
                    if (options.Length > 0) options.Append("     ");
                    options.Append(option.Label);
                    if (option.VisibleMonsters > 0) options.Append("  (적 "+option.VisibleMonsters+")");
                }
                Detail(info,(fork.Backtracking ? "막다른 길" : "갈림길")+"   ·   용사들이 논의하는 중",options.ToString());
            }
            else if (Time.time < game.SpotlightUntil && !string.IsNullOrWhiteSpace(game.SpotlightText))
                Detail(info,game.SpotlightTitle,game.SpotlightText);
            else
            {
                string where = run.FightingBoss ? "보스와 교전 중" :
                    run.Phase == RunPhase.Combat ? "교전 중  ·  적 "+run.Engaged.Count() :
                    run.IsSeen(run.Map.BossTile) ? "보스방으로 향하는 중" : "보스방을 찾는 중";
                var engaged = run.Engaged.OrderByDescending(m => m.IsBoss).ThenBy(m => m.Hp).Take(3)
                    .Select(m => m.Name+" "+m.Hp+"/"+m.MaxHp).ToList();
                string title = engaged.Count > 0 ? string.Join("   ·   ",engaged) : where;
                Write(new Rect(info.x,info.y,info.width,26),title,17,Paper,true);
                Write(new Rect(info.x,info.y+30,info.width,20),
                    run.StepsTaken+"걸음   ·   발견한 몬스터 "+run.SpottedCount+" / "+run.Placements.Count+
                    "   ·   처치 "+run.DefeatedCount+"   ·   갈림길 "+run.ForkCount+"번",13,Muted);
                Write(new Rect(info.x,info.y+54,info.width,20),"낙석 역경    d20 + 능력 보정 ≥ 난이도 "+run.TrialDifficulty,13,Muted);
            }
        }

        private void Detail(Rect info,string title,string body)
        {
            Write(new Rect(info.x,info.y,info.width,20),title,13,Gold,true);
            Write(new Rect(info.x,info.y+24,info.width,info.height-24),body,14,Paper,false,TextAnchor.UpperLeft,true);
        }

        private void ResultContent(DungeonRun run,Rect content)
        {
            bool won = run.Phase == RunPhase.Victory;
            Write(new Rect(content.x+16,content.y+18,content.width-32,34),
                won ? "마왕을 쓰러뜨렸습니다" : "파티가 전멸했습니다",22,won ? Gold : Ember,true);
            Write(new Rect(content.x+16,content.y+58,content.width-32,26),"최종 파티 레벨 "+run.Level+"    ·    누적 경험치 "+
                run.Experience+(run.ReachedBoss ? "    ·    보스전" : "")+"    ·    "+run.StepsTaken+"걸음",15,Muted);
        }

        /// <summary>A square element card: glyph tile, title and one detail line. Returns true when clicked.</summary>
        private bool ElementCard(Rect r,string glyph,Color accent,string title,string detail,bool chosen,bool enabled)
        {
            bool hover = !blocked && enabled && r.Contains(Event.current.mousePosition);
            Fill(r,chosen ? Hex(0x35534D) : hover ? Color.Lerp(Card,Paper,.1f) : Card);
            if (chosen) Frame(r,2,Teal);
            var icon = new Rect(r.x+(r.width-34)*.5f,r.y+9,34,34);
            Fill(icon,enabled ? accent : Edge);
            Write(icon,glyph,glyph.Length > 1 ? 13 : 17,Ink,true,TextAnchor.MiddleCenter);
            Write(new Rect(r.x+4,r.y+47,r.width-8,20),title,14,enabled ? (chosen ? Teal : Paper) : Muted,true,TextAnchor.MiddleCenter);
            Write(new Rect(r.x+4,r.y+67,r.width-8,16),detail,11,enabled ? Muted : Edge,false,TextAnchor.MiddleCenter);
            return enabled && Hit(r);
        }

        // ── Board input ──────────────────────────────────────────────────

        private Vector2 ToScreen(Vector2 gui) => new Vector2(gui.x*scale,Screen.height-gui.y*scale);

        private bool OverHud(Vector2 mouse)
        {
            foreach (var r in solid)
                if (r.Contains(mouse)) return true;
            return false;
        }

        private void BoardInput(DungeonRun run)
        {
            var e = Event.current;
            var mouse = e.mousePosition;
            bool overBoard = !history && !OverHud(mouse) && game.Board.ContainsScreenPoint(ToScreen(mouse));
            bool editing = overBoard && run.Phase == RunPhase.Planning;
            TilePoint tile = default;
            bool onTile = editing && game.Board.TryPickTile(ToScreen(mouse),out tile);

            if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2) && overBoard)
            {
                panning = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && panning)
            {
                // A drag that started on the board keeps panning even when the cursor crosses the HUD.
                game.Board.Pan(ToScreen(mouse-e.delta),ToScreen(mouse));
                e.Use();
            }
            else if (panning && e.rawType == EventType.MouseUp && (e.button == 1 || e.button == 2))
                panning = false;
            else if (e.type == EventType.ScrollWheel && overBoard)
            {
                game.Board.ZoomAt(ToScreen(mouse),Mathf.Clamp(e.delta.y,-3,3)/3f);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && onTile)
            {
                Paint(tile);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && overBoard && Exploring(run) &&
                     game.Board.TryPickHero(ToScreen(mouse),out int hero))
            {
                game.SelectHero(game.SelectedHero == hero ? -1 : hero);
                e.Use();
            }

            if (e.type == EventType.Repaint)
            {
                if (onTile && !panning)
                {
                    var check = brush == MonsterKind.None ? run.CheckRemove(tile) : run.CheckPlace(tile,brush);
                    bool floor = run.Map.IsFloor(tile);
                    game.Board.SetHover(floor ? tile : (TilePoint?)null,check == PlacementCheck.Allowed);
                }
                else game.Board.SetHover(null,false);
            }
        }

        private void Paint(TilePoint tile)
        {
            string message;
            switch (game.Place(tile,brush))
            {
                case PlacementCheck.Allowed: message = ""; break;
                case PlacementCheck.OverBudget: message = "위협 예산이 부족합니다."; break;
                case PlacementCheck.NearEntrance: message = "입구 근처에는 몬스터를 둘 수 없습니다."; break;
                case PlacementCheck.NotFloor: message = "벽에는 몬스터를 둘 수 없습니다."; break;
                case PlacementCheck.NothingToRemove: message = "이 칸에는 되돌릴 몬스터가 없습니다."; break;
                default: message = ""; break;
            }
            notice = message;
            noticeUntil = Time.unscaledTime+2.5f;
        }

        // ── History overlay ──────────────────────────────────────────────

        private void History(DungeonRun run)
        {
            blocked = false;
            Fill(new Rect(0,0,width,height),new Color(0,0,0,.72f));
            float w = Mathf.Min(730,width-42);
            var box = new Rect((width-w)/2,26,w,height-52);
            Fill(box,Panel);
            Fill(new Rect(box.x,box.y,w,3),Gold);
            Write(new Rect(box.x+17,box.y+11,220,33),"원정 기록",22,Paper,true);
            bool close = SmallButton(new Rect(box.xMax-89,box.y+10,72,34),"닫기",Card,Paper);
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                close = true;
                Event.current.Use();
            }
            var area = new Rect(box.x+14,box.y+52,w-28,box.height-66);
            float lineWidth = area.width-23;
            ConfigureText(14,Paper,false,TextAnchor.UpperLeft,true);
            if (rowOffsets.Count == 0 || !Mathf.Approximately(historyWidth,lineWidth))
            {
                historyWidth = lineWidth;
                rowOffsets.Clear();
                rowOffsets.Add(0);
            }
            for (int i=rowOffsets.Count-1;i<run.Log.Count;i++)
            {
                float row = Mathf.Max(31,text.CalcHeight(new GUIContent(run.Log[i]),lineWidth-18)+15);
                rowOffsets.Add(rowOffsets[i]+row);
            }
            float contentHeight = rowOffsets[rowOffsets.Count-1];
            scroll.y = Mathf.Clamp(scroll.y,0,Mathf.Max(0,contentHeight-area.height));
            scroll = GUI.BeginScrollView(area,scroll,new Rect(0,0,lineWidth,contentHeight),false,true);
            int firstRow = rowOffsets.BinarySearch(scroll.y);
            if (firstRow < 0) firstRow = Mathf.Max(0,~firstRow-1);
            for (int i=firstRow;i<run.Log.Count && rowOffsets[i]<scroll.y+area.height;i++)
            {
                string entry = run.Log[i];
                float y = rowOffsets[i], row = rowOffsets[i+1]-y;
                if (i%2 == 0) Fill(new Rect(0,y,lineWidth,row),Card);
                Write(new Rect(9,y+6,lineWidth-18,row-10),entry,14,Paper,false,TextAnchor.UpperLeft,true);
            }
            GUI.EndScrollView();
            if (close) history = false;
        }

        // ── Drawing helpers ──────────────────────────────────────────────

        /// <summary>Marks a drawn rectangle as HUD, so the board ignores clicks, drags and wheel over it.</summary>
        private void Solid(Rect r) => solid.Add(r);

        private bool Hit(Rect r) => !blocked && GUI.Button(r,GUIContent.none,GUIStyle.none);

        private bool SmallButton(Rect r,string caption,Color background,Color ink,bool enabled = true)
        {
            Solid(r);
            bool hover = !blocked && enabled && r.Contains(Event.current.mousePosition);
            Fill(r,enabled ? (hover ? Color.Lerp(background,Paper,.12f) : background) : Card);
            Write(r,caption,13,enabled ? ink : Hex(0x91A09E),true,TextAnchor.MiddleCenter);
            return enabled && Hit(r);
        }

        private static void Meter(Rect r,float fraction,Color color)
        {
            Fill(r,Fade(Ink,.9f));
            Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01(fraction),r.height),color);
        }

        private void Shadowed(Rect r,string value,int size,Color color,bool bold = false)
        {
            Write(new Rect(r.x+1,r.y+1,r.width,r.height),value,size,new Color(0,0,0,.85f),bold);
            Write(r,value,size,color,bold);
        }

        private void Write(Rect r,string value,int size,Color color,bool bold = false,
            TextAnchor alignment = TextAnchor.MiddleLeft,bool wrap = false)
        {
            ConfigureText(size,color,bold,alignment,wrap);
            GUI.Label(r,value,text);
        }

        private void ConfigureText(int size,Color color,bool bold,TextAnchor alignment,bool wrap)
        {
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.normal.textColor = color;
            text.alignment = alignment;
            text.wordWrap = wrap;
        }

        private static void Frame(Rect r,float thickness,Color color)
        {
            Fill(new Rect(r.x,r.y,r.width,thickness),color);
            Fill(new Rect(r.x,r.yMax-thickness,r.width,thickness),color);
            Fill(new Rect(r.x,r.y,thickness,r.height),color);
            Fill(new Rect(r.xMax-thickness,r.y,thickness,r.height),color);
        }

        private static void Fill(Rect r,Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r,Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Color Fade(Color color,float alpha) => new Color(color.r,color.g,color.b,alpha);

        private static Color Hex(int rgb) => new Color32((byte)(rgb >> 16),(byte)(rgb >> 8),(byte)rgb,255);
    }
}
