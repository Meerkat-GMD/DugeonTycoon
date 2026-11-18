using System.Collections.Generic;
using System.Linq;
using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    public sealed class DungeonHud : MonoBehaviour
    {
        private static readonly Color Ink = Hex(0x151B20), Panel = Hex(0x202A30), Card = Hex(0x29343A);
        private static readonly Color Edge = Hex(0x435056), Muted = Hex(0xB3BEBB), Paper = Hex(0xF0EAE0);
        private static readonly Color Gold = Hex(0xE8BE76), Teal = Hex(0x7BD5BF), Ember = Hex(0xEC9179);
        private static readonly Color[] RoleColors = { Hex(0x74C5C4), Hex(0xE2C77D), Hex(0xDB9AA8), Hex(0xA2CD88) };
        private static readonly string[] RoleNames = { "탱커", "힐러", "근딜", "원딜" };
        private DungeonGame game;
        private Font face;
        private GUIStyle text;
        private MonsterKind brush = MonsterKind.Goblin;
        private bool history, blocked, panning;
        private Vector2 scroll;
        private string notice;
        private float noticeUntil, scale, width, height, railY;
        private int knownStage;
        private DungeonRun knownRun;
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
                knownRun = run;
                knownStage = run.Stage;
            }
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) {
                    font = face ? face : GUI.skin.font, clipping = TextClipping.Clip,
                    richText = false, padding = new RectOffset(), margin = new RectOffset()
                };
            }
            float readableScale = Mathf.Clamp(Screen.height/780f+.22f,.72f,1.65f);
            scale = Mathf.Min(Screen.width/760f,Screen.height/480f,readableScale);
            if (scale <= 0) return;
            width = Screen.width/scale;
            height = Screen.height/scale;
            const float header = 65, roster = 83, actions = 93, logHeight = 34, rail = 33;
            railY = height-roster-actions-logHeight-rail;
            game.Board.SetViewport(new Rect(0,(height-railY)/height,1,(railY-header)/height));
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUI.color = Color.white;
            blocked = history;
            Header(run);
            BoardInput(run,header);
            Route(run);
            Party(run,railY+rail);
            Controls(run,height-logHeight-actions);
            LogStrip(run,height-logHeight);
            if (history) History(run);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void Header(DungeonRun run)
        {
            Fill(new Rect(0,0,width,65),Ink);
            Fill(new Rect(0,63,width,2),Edge);
            Diamond(28,32,18,Gold);
            Diamond(28,32,12,Ink);
            Diamond(28,32,5,Gold);
            Write(new Rect(53,7,240,19),"던전 타이쿤    /    스테이지 "+run.Stage+"·2",13,Gold,true);
            Write(new Rect(52,25,305,32),run.Stage == 1 ? "성문으로 가는 길" : "마왕의 성채",25,Paper,true);

            float statWidth = 77, left = width-3*statWidth-18;
            HeaderStat(left,statWidth,"위협 예산",run.Budget.ToString(),Gold);
            HeaderStat(left+statWidth,statWidth,"신앙",run.Faith.ToString(),Teal);
            HeaderStat(left+statWidth*2,statWidth,"남은 역경",run.TrialsLeft+" / 2",Paper);
            string status = run.Phase == RunPhase.Planning ? "던전 준비" :
                run.Phase == RunPhase.Travel ? "이동 중" :
                run.Phase == RunPhase.Combat ? "전투 중" :
                run.Phase == RunPhase.Victory ? "원정 성공" : "원정 종료";
            float badgeX = left-94;
            Fill(new Rect(badgeX,21,77,24),Panel);
            Write(new Rect(badgeX,20,77,25),status,13,Teal,true,TextAnchor.MiddleCenter);
        }

        private void HeaderStat(float x, float w, string title, string value, Color accent)
        {
            Fill(new Rect(x,15,1,33),Edge);
            Write(new Rect(x+12,7,w-14,20),title,12,Muted);
            Write(new Rect(x+12,26,w-14,27),value,21,accent,true);
        }

        private Vector2 ToScreen(Vector2 gui) => new Vector2(gui.x*scale,Screen.height-gui.y*scale);

        private void BoardInput(DungeonRun run,float boardTop)
        {
            var e = Event.current;
            var mouse = e.mousePosition;
            bool overBoard = !history && mouse.y >= boardTop && mouse.y < railY &&
                game.Board.ContainsScreenPoint(ToScreen(mouse));
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

        private void Route(DungeonRun run)
        {
            Fill(new Rect(0,railY,width,33),Ink);
            float buttonWidth = 140;
            var toggle = new Rect(width-14-buttonWidth,railY+3,buttonWidth,26);
            if (run.Phase == RunPhase.Planning)
            {
                int goblins = 0, ogres = 0;
                foreach (var placement in run.Placements)
                    if (placement.Kind == MonsterKind.Ogre) ogres++; else goblins++;
                string summary = run.Placements.Count == 0 ? "배치한 몬스터 없음  ·  비워 두고 출발할 수도 있습니다" :
                    "배치 "+run.Placements.Count+"마리   ·   고블린 "+goblins+"   ·   오우거 "+ogres;
                Write(new Rect(16,railY+3,width-buttonWidth-46,26),summary,13,Paper,true);
                if (Button(toggle,"던전 전체 보기",Card,Paper)) game.Board.ShowWholeDungeon();
                return;
            }

            string where = run.FightingBoss ? "보스와 교전 중" :
                run.PendingFork != null ? "갈림길에서 논의 중" :
                run.Phase == RunPhase.Combat ? "교전 중  ·  적 "+run.Engaged.Count() :
                run.IsSeen(run.Map.BossTile) ? "보스방을 발견했다" : "보스방을 찾는 중";
            string progress = where+"   ·   "+run.StepsTaken+"걸음   ·   발견한 몬스터 "+run.SpottedCount+" / "+run.Placements.Count+
                "   ·   처치 "+run.DefeatedCount+"   ·   갈림길 "+run.ForkCount+"번";
            Write(new Rect(16,railY+3,width-buttonWidth-46,26),progress,13,Paper,true);
            bool following = game.Board.FollowParty;
            if (Button(toggle,following ? "파티 따라가기  켬" : "파티 따라가기  끔",following ? Hex(0x35534D) : Card,
                following ? Teal : Paper))
                game.Board.SetFollowParty(!following);
        }

        private void Party(DungeonRun run,float y)
        {
            Fill(new Rect(0,y,width,83),Ink);
            Write(new Rect(14,y+1,240,21),"용사 파티    Lv. "+run.Level+"    ·    경험치 "+run.Experience,13,Paper,true);
            Write(new Rect(width-330,y+1,316,21),game.AiMode+(game.Thinking ? "    ·    행동 판단 중" : ""),12,
                game.Thinking ? Gold : Muted,false,TextAnchor.MiddleRight);
            float w = (width-28-15)/4;
            for (int i=0;i<run.Party.Count;i++)
            {
                var h = run.Party[i];
                float x = 14+i*(w+5);
                var accent = h.Hp == 0 ? Muted : RoleColors[i];
                Fill(new Rect(x,y+24,w,54),Card);
                Fill(new Rect(x,y+24,3,54),accent);
                Write(new Rect(x+9,y+27,w*.53f,21),h.Name+"  "+RoleNames[i],14,Paper,true);
                Write(new Rect(x+w*.52f,y+27,w*.48f-8,21),h.Hp+" / "+h.MaxHp,13,accent,true,TextAnchor.MiddleRight);
                Fill(new Rect(x+10,y+50,w-20,3),Ink);
                Fill(new Rect(x+10,y+50,(w-20)*h.Hp/h.MaxHp,3),accent);
                string line = h.Hp == 0 ? "쓰러짐" : string.IsNullOrWhiteSpace(h.LastLine) ? "대기 중" : h.LastLine;
                Write(new Rect(x+9,y+55,w-17,19),line,12,Muted);
            }
        }

        private void Controls(DungeonRun run,float y)
        {
            Fill(new Rect(0,y,width,93),Panel);
            Fill(new Rect(0,y,width,1),Edge);
            if (run.Phase == RunPhase.Planning)
            {
                Write(new Rect(15,y+6,145,26),"몬스터 배치",20,Paper,true);
                string hint = !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil
                    ? notice : "바닥을 클릭해 배치   ·   우클릭·휠 드래그로 이동   ·   휠로 확대·축소";
                Write(new Rect(148,y+9,width-332,22),hint,13,
                    !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil ? Ember : Muted);
                BrushButton(new Rect(15,y+39,133,40),MonsterKind.Goblin,"고블린   ·   비용 1");
                BrushButton(new Rect(154,y+39,133,40),MonsterKind.Ogre,"오우거   ·   비용 2");
                BrushButton(new Rect(293,y+39,115,40),MonsterKind.None,"한 마리 되돌리기");
                if (Button(new Rect(width-164,y+36,149,45),"원정 출발  ›",Gold,Ink)) game.Depart();
                return;
            }
            bool finished = run.Phase == RunPhase.Victory || run.Phase == RunPhase.Defeat;
            if (finished)
            {
                bool won = run.Phase == RunPhase.Victory;
                Write(new Rect(15,y+10,295,33),won ? "마왕을 쓰러뜨렸습니다" : "파티가 전멸했습니다",22,won ? Gold : Ember,true);
                Write(new Rect(16,y+47,width-207,30),"최종 파티 레벨 "+run.Level+"    ·    누적 경험치 "+run.Experience+
                    (run.ReachedBoss ? "    ·    보스전" : "")+"    ·    "+run.StepsTaken+"걸음",15,Muted);
                if (Button(new Rect(width-164,y+28,149,44),"새 원정",Gold,Ink)) game.Restart();
                return;
            }
            Write(new Rect(15,y+4,261,26),"사회자의 개입",18,Paper,true);
            if (Button(new Rect(15,y+33,140,43),"d20  역경 주기",Gold,Ink,run.TrialsLeft>0 && !run.PendingAdversity))
                game.Trial();
            if (Button(new Rect(161,y+33,109,43),"축복  ·  신앙 1",Card,Teal,run.CanBless)) game.Bless();
            Fill(new Rect(283,y+12,1,68),Edge);
            float details = width-315;
            var fork = run.PendingFork;
            if (run.PendingAdversity)
            {
                Write(new Rect(299,y+5,details,21),"낙석 역경   ·   용사 대응 중",13,Gold,true);
                Write(new Rect(299,y+27,details,55),game.TrialAnnouncement,14,Paper,false,TextAnchor.UpperLeft,true);
            }
            else if (fork != null)
            {
                Write(new Rect(299,y+5,details,21),(fork.Backtracking ? "막다른 길" : "갈림길")+"   ·   용사들이 논의하는 중",13,Gold,true);
                var options = new System.Text.StringBuilder();
                foreach (var option in fork.Options)
                {
                    if (options.Length > 0) options.Append("     ");
                    options.Append(option.Label);
                    if (option.VisibleMonsters > 0) options.Append("  (적 "+option.VisibleMonsters+")");
                }
                Write(new Rect(299,y+27,details,55),options.ToString(),15,Paper,false,TextAnchor.UpperLeft,true);
            }
            else if (Time.time < game.SpotlightUntil && !string.IsNullOrWhiteSpace(game.SpotlightText))
            {
                Write(new Rect(299,y+5,details,21),game.SpotlightTitle,13,Gold,true);
                Write(new Rect(299,y+27,details,55),game.SpotlightText,14,Paper,false,TextAnchor.UpperLeft,true);
            }
            else
            {
                var engaged = run.Engaged.OrderByDescending(m => m.IsBoss).ThenBy(m => m.Hp).Take(3)
                    .Select(m => m.Name+" "+m.Hp+"/"+m.MaxHp).ToList();
                string title = engaged.Count > 0 ? string.Join("   ·   ",engaged) :
                    run.IsSeen(run.Map.BossTile) ? "보스방으로 향하는 중" : "던전을 탐색하는 중";
                Write(new Rect(299,y+12,details,28),title,17,Paper,true);
                Write(new Rect(299,y+44,details,28),"낙석 역경    d20 + 능력 보정 ≥ 난이도 "+run.TrialDifficulty,14,Muted);
            }
        }

        private void BrushButton(Rect r,MonsterKind kind,string caption)
        {
            bool chosen = brush == kind;
            if (Button(r,caption,chosen ? Hex(0x35534D) : Card,chosen ? Teal : Paper)) brush = kind;
            if (chosen) Fill(new Rect(r.x,r.yMax-3,r.width,3),Teal);
        }

        private void LogStrip(DungeonRun run,float y)
        {
            Fill(new Rect(0,y,width,34),Ink);
            Fill(new Rect(0,y,width,1),Edge);
            Write(new Rect(14,y+3,77,28),"원정 기록",13,Gold,true);
            string latest = run.Log.Count == 0 ? "" : run.Log[run.Log.Count-1].Replace('\n',' ');
            Write(new Rect(91,y+3,width-225,28),latest,13,Muted);
            if (Button(new Rect(width-114,y+4,100,26),"기록 보기",Card,Paper))
            {
                history = true;
                scroll = new Vector2(0,float.MaxValue);
            }
        }

        private void History(DungeonRun run)
        {
            blocked = false;
            Fill(new Rect(0,0,width,height),new Color(0,0,0,.72f));
            float w = Mathf.Min(730,width-42);
            var box = new Rect((width-w)/2,26,w,height-52);
            Fill(box,Panel);
            Fill(new Rect(box.x,box.y,w,3),Gold);
            Write(new Rect(box.x+17,box.y+11,220,33),"원정 기록",22,Paper,true);
            bool close = Button(new Rect(box.xMax-89,box.y+10,72,34),"닫기",Card,Paper);
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

        private bool Hit(Rect r) => !blocked && GUI.Button(r,GUIContent.none,GUIStyle.none);

        private bool Button(Rect r,string caption,Color background,Color ink,bool enabled = true)
        {
            bool hover = !blocked && enabled && r.Contains(Event.current.mousePosition);
            Fill(r,enabled ? (hover ? Color.Lerp(background,Paper,.12f) : background) : Card);
            Write(r,caption,14,enabled ? ink : Hex(0x91A09E),true,TextAnchor.MiddleCenter);
            return enabled && Hit(r);
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

        private static void Diamond(float x,float y,float size,Color color)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45,new Vector2(x,y));
            Fill(new Rect(x-size*.5f,y-size*.5f,size,size),color);
            GUI.matrix = matrix;
        }

        private static void Fill(Rect r,Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r,Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Color Hex(int rgb) => new Color32((byte)(rgb >> 16),(byte)(rgb >> 8),(byte)rgb,255);
    }
}
