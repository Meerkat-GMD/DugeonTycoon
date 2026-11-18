using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DungeonTycoon.Unity
{
    /// <summary>
    /// Plays a fork debate or an adversity on the bottom stage (Resources/Dialogue/DialogueStage.uxml + .uss):
    /// four portraits stand on a dialogue box, the speaker steps forward, lines type out and dice roll above
    /// the heroes who roll. Core has already settled every outcome; this only shows it, so skipping or
    /// auto-advancing never changes a result. Presentation speed ignores the game speed.
    /// </summary>
    public sealed class DialogueStage : MonoBehaviour
    {
        // Balance values: typing speed and how long a finished line stays before auto-advance moves on.
        private const float CharsPerSecond = 28, AutoAdvanceBase = .6f, AutoAdvancePerChar = .04f;
        private const float DotSeconds = .3f, BlinkSeconds = 1f;
        private const float SpinSeconds = .95f, FlipSeconds = .055f, WobbleSeconds = .12f;
        private const float CalcDelay = .35f, VerdictDelay = .45f, PopScale = 1.45f;
        private const int RollAdvanceLength = 20;
        private const string AutoAdvanceKey = "DialogueAutoAdvance";

        private static readonly Color[] RoleColors = { Hex(0x74C5C4), Hex(0xE2C77D), Hex(0xDB9AA8), Hex(0xA2CD88) };
        private static readonly int[] DotSteps = { 1, 2, 3, 2 };

        /// <summary>How long the oracle cards wait before passing when auto-advance is on. Balance value.</summary>
        private const float OracleSeconds = 5;
        private const int Unpicked = -2;
        /// <summary>The oracle card chosen so far: an option index, -1 to pass, or <see cref="Unpicked"/>.</summary>
        private int picked = Unpicked;
        private VisualElement godPanel, oraclePanel, oracleCards, oracleTimer;
        private Label oracleQuestion;
        private VisualElement root, box, dcPanel, itemCard;
        private Label speaker, text, next, dcValue, stamp, itemGrade, itemName, itemStats, itemPower;
        private Button autoToggle;
        private readonly Slot[] slots = new Slot[4];
        private readonly string[] names = new string[4];
        private bool autoAdvance = true, pressed;
        private float waitStartedAt;

        private sealed class Slot
        {
            public VisualElement Root, Over;
            public Label Status, Name, Mark;
            public SpeechBubble Bubble;
            public bool Down, Thinking;
        }

        /// <summary>One hero's die while a roll beat is on screen.</summary>
        private sealed class RollView
        {
            public DiceRoll Roll;
            public Slot Slot;
            public Die Die;
            public Label Calc, Crown;
        }

        public bool IsOpen { get; private set; }

        /// <summary>
        /// For automated play tests only: lines appear whole, nothing waits, dice land at once and the oracle takes
        /// <see cref="FastOraclePick"/> straight away. Core settles every outcome first, so results do not change.
        /// Set it from the Unity CLI (see tools.md); it is never saved and resets when play mode restarts.
        /// </summary>
        public static bool FastForward { get; set; }
        /// <summary>The oracle answer under <see cref="FastForward"/>: an option index, or -1 to pass.</summary>
        public static int FastOraclePick { get; set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFastForward()
        {
            FastForward = false;
            FastOraclePick = -1;
        }

        // DungeonHud adds the UIDocument in its Awake and DungeonGame adds this component after the HUD, so by
        // Start the document and its `hud` element exist.
        private void Start()
        {
            var document = GetComponent<UIDocument>();
            var hud = document ? document.rootVisualElement.Q("hud") : null;
            var tree = Resources.Load<VisualTreeAsset>("Dialogue/DialogueStage");
            if (hud == null || tree == null)
            {
                Debug.LogError("DialogueStage: the HUD document or Resources/Dialogue/DialogueStage is missing.");
                enabled = false;
                return;
            }
            tree.CloneTree(hud);
            Bind(hud);
            autoAdvance = PlayerPrefs.GetInt(AutoAdvanceKey, 1) == 1;
            PaintAutoToggle();
            Close();
        }

        private void Bind(VisualElement hud)
        {
            root = hud.Q("dialogue-stage");
            box = root.Q("stage-box");
            speaker = root.Q<Label>("stage-speaker");
            text = root.Q<Label>("stage-text");
            next = root.Q<Label>("stage-next");
            autoToggle = root.Q<Button>("stage-auto");
            dcPanel = root.Q("stage-dc");
            dcValue = root.Q<Label>("dc-value");
            stamp = root.Q<Label>("dc-stamp");
            godPanel = root.Q("stage-god");
            oraclePanel = root.Q("stage-oracle");
            oracleQuestion = root.Q<Label>("oracle-question");
            oracleCards = root.Q("oracle-cards");
            oracleTimer = root.Q("oracle-timer");
            itemCard = root.Q("stage-item");
            itemGrade = root.Q<Label>("item-grade");
            itemName = root.Q<Label>("item-name");
            itemStats = root.Q<Label>("item-stats");
            itemPower = root.Q<Label>("item-power");
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = root.Q("slot-"+i);
                var bubble = new SpeechBubble();
                bubble.AddToClassList("stage-bubble");
                slot.Add(bubble);
                slots[i] = new Slot {
                    Bubble = bubble,
                    Root = slot, Over = slot.Q("over"), Status = slot.Q<Label>("status"),
                    Name = slot.Q<Label>("name"), Mark = slot.Q<Label>("mark")
                };
            }
            autoToggle.clicked += ToggleAutoAdvance;
        }

        // ── Opening and closing ──────────────────────────────────────────

        /// <summary>Shows the stage with every hero in place; downed heroes stay greyed out in their slot.</summary>
        public void Open(DungeonRun run)
        {
            if (root == null) return;
            ClearDice();
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                var hero = run.Party[i];
                names[i] = hero.Name;
                slot.Name.text = hero.Name;
                slot.Down = hero.Hp == 0;
                slot.Thinking = false;
                slot.Root.EnableInClassList("down", slot.Down);
                slot.Root.RemoveFromClassList("thinking");
                slot.Root.RemoveFromClassList("talk");
                slot.Root.RemoveFromClassList("rolling");
                SetStatus(slot, slot.Down ? "쓰러짐" : "");
            }
            ClearMarks();
            ShowItem(null);
            ShowNarrator("");
            Show(next, false);
            pressed = false;
            IsOpen = true;
            root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            IsOpen = false;
            pressed = false;
            if (root == null) return;
            ClearDice();
            foreach (var slot in slots)
            {
                slot.Thinking = false;
                slot.Root.RemoveFromClassList("thinking");
                slot.Root.RemoveFromClassList("talk");
                SetStatus(slot, slot.Down ? "쓰러짐" : "");
            }
            text.text = "";
            Show(next, false);
            ClearMarks();
            ShowItem(null);
            root.style.display = DisplayStyle.None;
        }

        /// <summary>The listed heroes show "생각 중" while their answer is on its way; an empty list clears it.</summary>
        public void SetThinking(IEnumerable<HeroRole> roles)
        {
            if (root == null) return;
            foreach (var slot in slots) slot.Thinking = false;
            if (roles != null)
                foreach (var role in roles)
                    if (!slots[(int)role].Down) slots[(int)role].Thinking = true;
            foreach (var slot in slots) RefreshThinking(slot);
        }

        // ── Lines ────────────────────────────────────────────────────────

        /// <summary>Types out one line (a null speaker is the narrator), then waits for the player or auto-advance.</summary>
        public async UniTask SayAsync(HeroRole? speakerRole, string line, CancellationToken ct)
        {
            if (root == null) return;
            line ??= "";
            if (speakerRole is HeroRole role)
            {
                var slot = slots[(int)role];
                slot.Thinking = false;
                RefreshThinking(slot);
                slot.Bubble.SetLine(line);
                foreach (var other in slots) other.Root.EnableInClassList("talk", other == slot);
                box.RemoveFromClassList("narration");
                Show(speaker, true);
                speaker.text = names[(int)role];
                speaker.style.color = RoleColors[(int)role];
            }
            else
            {
                foreach (var slot in slots) slot.Root.RemoveFromClassList("talk");
                ShowNarrator("");
            }
            await TypeAsync(line, ct);
            await WaitAdvanceAsync(line.Length, ct);
        }

        /// <summary>Plays beats in order. A roll calls <paramref name="onRoll"/> as its dice start to spin.</summary>
        public async UniTask PlayAsync(IReadOnlyList<DialogueBeat> beats, Action<DialogueBeat> onRoll, CancellationToken ct)
        {
            if (beats == null) return;
            foreach (var beat in beats)
            {
                ct.ThrowIfCancellationRequested();
                // A new item on the table starts a fresh round of wishes.
                if (beat.Item != null)
                {
                    ClearMarks();
                    ShowItem(beat.Item);
                }
                if (beat.MarkRole is HeroRole marked) SetMark(marked, beat.Mark);
                switch (beat.Kind)
                {
                    case BeatKind.Narration: await SayAsync(null, beat.Text, ct); break;
                    case BeatKind.Line: await SayAsync(beat.Speaker, beat.Text, ct); break;
                    case BeatKind.Roll: await RollAsync(beat, onRoll, ct); break;
                }
            }
        }

        private async UniTask TypeAsync(string line, CancellationToken ct)
        {
            pressed = false;
            if (FastForward)
            {
                text.text = line;
                return;
            }
            float start = Time.unscaledTime;
            int shown = 0;
            while (shown < line.Length)
            {
                if (Consume()) break;
                shown = Mathf.Min(line.Length, 1+Mathf.FloorToInt((Time.unscaledTime-start)*CharsPerSecond));
                text.text = line.Substring(0, shown);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            text.text = line;
        }

        /// <summary>Blinks ▼ until the player advances, or until a line of this length has been readable long enough.</summary>
        private async UniTask WaitAdvanceAsync(int length, CancellationToken ct)
        {
            if (FastForward) return;
            waitStartedAt = Time.unscaledTime;
            Show(next, true);
            try
            {
                while (true)
                {
                    if (Consume()) return;
                    if (autoAdvance && Time.unscaledTime-waitStartedAt >= AutoAdvanceBase+AutoAdvancePerChar*length) return;
                    next.style.opacity = Mathf.Repeat(Time.unscaledTime, BlinkSeconds) < BlinkSeconds*.5f ? 1 : 0;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally
            {
                if (next != null) Show(next, false);
            }
        }

        // ── Dice ─────────────────────────────────────────────────────────

        private async UniTask RollAsync(DialogueBeat beat, Action<DialogueBeat> onRoll, CancellationToken ct)
        {
            pressed = false;
            foreach (var slot in slots) slot.Root.RemoveFromClassList("talk");
            ShowNarrator(beat.Text);

            var views = new List<RollView>();
            foreach (var roll in beat.Rolls)
            {
                // The god's die stands at the middle of the stage; a hero's die over that hero's portrait.
                var slot = roll.IsGod ? null : slots[(int)roll.Role];
                if (slot != null)
                {
                    slot.Thinking = false;
                    RefreshThinking(slot);
                    slot.Root.AddToClassList("rolling");
                }
                // Loot dice have no ability behind them: show the face alone.
                bool plain = string.IsNullOrEmpty(roll.Ability) && roll.Modifier == 0;
                var view = new RollView {
                    Roll = roll, Slot = slot, Die = new Die(roll.IsGod),
                    Calc = new Label(plain ? roll.Face.ToString() : $"{roll.Face} {Signed(roll.Modifier)} = {roll.Total}"),
                    Crown = new Label(roll.IsGod ? "신탁" : plain ? "획득" : "설득 성공")
                };
                view.Calc.AddToClassList("stage-calc");
                view.Crown.AddToClassList("stage-crown");
                view.Calc.pickingMode = view.Crown.pickingMode = PickingMode.Ignore;
                var holder = slot != null ? slot.Over : godPanel;
                holder.Add(view.Crown);
                if (roll.IsGod)
                {
                    var name = new Label("신") { pickingMode = PickingMode.Ignore };
                    name.AddToClassList("stage-god-name");
                    holder.Add(name);
                }
                holder.Add(view.Die);
                holder.Add(view.Calc);
                if (roll.IsGod) Show(godPanel, true);
                views.Add(view);
            }
            bool check = beat.Difficulty > 0;
            if (check)
            {
                dcValue.text = beat.Difficulty.ToString();
                stamp.text = beat.Success ? "성공" : "실패";
                stamp.EnableInClassList("ok", beat.Success);
                stamp.EnableInClassList("fail", !beat.Success);
                stamp.RemoveFromClassList("show");
                Show(dcPanel, true);
            }
            onRoll?.Invoke(beat);

            // Spin: numbers flicker while the dice wobble. A click skips straight to the final state.
            float flipAt = 0;
            bool playing = await HoldAsync(SpinSeconds, elapsed =>
            {
                bool flip = elapsed >= flipAt;
                if (flip) flipAt = elapsed+FlipSeconds;
                float wobble = Mathf.Sin(elapsed*2*Mathf.PI/WobbleSeconds);
                foreach (var view in views)
                {
                    if (flip) view.Die.Show(UnityEngine.Random.Range(1, 21));
                    view.Die.style.rotate = new Rotate(8*wobble);
                    SetScale(view.Die, 1+.04f*wobble);
                }
            }, ct);

            foreach (var view in views)
            {
                view.Die.style.rotate = new Rotate(0);
                view.Die.Land(view.Roll.Face);
            }
            if (playing)
                playing = await HoldAsync(CalcDelay, elapsed =>
                {
                    float t = 1-Mathf.Clamp01(elapsed/CalcDelay);
                    foreach (var view in views) SetScale(view.Die, 1+(PopScale-1)*t*t);
                }, ct);
            foreach (var view in views)
            {
                SetScale(view.Die, 1);
                view.Calc.AddToClassList("show");
            }
            if (playing) await HoldAsync(VerdictDelay, null, ct);

            if (check) stamp.AddToClassList("show");
            else if (beat.Winner == null && !beat.GodWon)
            {
                // A tied round: the heroes sharing the top face are marked to roll again.
                int top = 0;
                foreach (var view in views) top = Mathf.Max(top, view.Roll.Total);
                foreach (var view in views)
                {
                    bool tied = view.Roll.Total == top;
                    view.Crown.text = "동점";
                    view.Crown.EnableInClassList("show", tied);
                    view.Die.EnableInClassList("dim", !tied);
                }
            }
            else
                foreach (var view in views)
                {
                    bool won = view.Roll.IsGod ? beat.GodWon : !beat.GodWon && beat.Winner == view.Roll.Role;
                    view.Crown.EnableInClassList("show", won);
                    view.Slot?.Root.EnableInClassList("talk", won);
                    view.Die.EnableInClassList("dim", !won);
                }

            await WaitAdvanceAsync(RollAdvanceLength, ct);
            ClearDice();
        }

        /// <summary>Runs <paramref name="tick"/> every frame for a while; false as soon as the player skips.</summary>
        private async UniTask<bool> HoldAsync(float seconds, Action<float> tick, CancellationToken ct)
        {
            if (FastForward) return true;
            float start = Time.unscaledTime;
            while (Time.unscaledTime-start < seconds)
            {
                if (Consume()) return false;
                tick?.Invoke(Time.unscaledTime-start);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            return true;
        }

        private void ClearDice()
        {
            if (root == null) return;
            foreach (var slot in slots)
            {
                slot.Over.Clear();
                slot.Root.RemoveFromClassList("rolling");
                slot.Root.RemoveFromClassList("talk");
            }
            stamp.RemoveFromClassList("show");
            Show(dcPanel, false);
            godPanel.Clear();
            Show(godPanel, false);
        }

        // ── Oracle ───────────────────────────────────────────────────────

        /// <summary>One way the GM can reveal: its name and the heroes who argued for it.</summary>
        public readonly struct OracleChoice
        {
            public OracleChoice(string label, IReadOnlyList<HeroRole> backers)
            {
                Label = label;
                Backers = backers;
            }

            public string Label { get; }
            public IReadOnlyList<HeroRole> Backers { get; }
        }

        /// <summary>
        /// Lays numbered path cards above the portraits plus a last "넘어간다" card and waits for a click or the
        /// number key. With auto-advance on, a bar runs down and the choice passes when it empties. Each portrait
        /// carries the number of the way its hero argued for. Returns the chosen index, or -1 to pass.
        /// <paramref name="hover"/> gets the index under the mouse, or -1, so the board can show that way.
        /// </summary>
        public async UniTask<int> ChooseOracleAsync(IReadOnlyList<OracleChoice> choices, int cost, Action<int> hover,
            CancellationToken ct)
        {
            if (root == null || choices == null || choices.Count == 0) return -1;
            if (FastForward) return FastOraclePick >= 0 && FastOraclePick < choices.Count ? FastOraclePick : -1;
            foreach (var slot in slots) slot.Root.RemoveFromClassList("talk");
            ShowNarrator("");
            oracleQuestion.text = $"{DialogueLines.OracleQuestion}  (신앙 {cost})";
            oracleCards.Clear();
            picked = Unpicked;
            for (int i = 0; i <= choices.Count; i++)
            {
                int index = i < choices.Count ? i : -1;
                var card = new VisualElement();
                card.AddToClassList("oracle-card");
                card.EnableInClassList("skip", index < 0);
                var number = new Label((i+1).ToString()) { pickingMode = PickingMode.Ignore };
                number.AddToClassList("oracle-number");
                var label = new Label(index < 0 ? DialogueLines.OracleSkip : choices[i].Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("oracle-label");
                card.Add(number);
                card.Add(label);
                if (index >= 0)
                {
                    var backers = new VisualElement { pickingMode = PickingMode.Ignore };
                    backers.AddToClassList("oracle-backers");
                    foreach (var role in choices[i].Backers)
                    {
                        var face = new VisualElement { pickingMode = PickingMode.Ignore };
                        face.AddToClassList("oracle-face");
                        face.AddToClassList("role-"+(int)role);
                        backers.Add(face);
                        SetMark(role, $"{i+1}. {choices[i].Label}");
                        slots[(int)role].Mark.AddToClassList("opt");
                    }
                    card.Add(backers);
                }
                card.RegisterCallback<ClickEvent>(_ => picked = index);
                card.RegisterCallback<PointerEnterEvent>(_ => hover?.Invoke(index));
                card.RegisterCallback<PointerLeaveEvent>(_ => hover?.Invoke(-1));
                oracleCards.Add(card);
            }
            Show(oraclePanel, true);
            float start = Time.unscaledTime;
            try
            {
                while (picked == Unpicked)
                {
                    for (int i = 0; i <= choices.Count && i < 9; i++)
                        if (Input.GetKeyDown(KeyCode.Alpha1+i) || Input.GetKeyDown(KeyCode.Keypad1+i))
                            picked = i < choices.Count ? i : -1;
                    float left = autoAdvance ? 1-(Time.unscaledTime-start)/OracleSeconds : 1;
                    oracleTimer.style.width = Length.Percent(100*Mathf.Clamp01(left));
                    Show(oracleTimer.parent, autoAdvance);
                    if (autoAdvance && left <= 0) picked = -1;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally
            {
                Show(oraclePanel, false);
                oracleCards.Clear();
                foreach (var slot in slots) slot.Mark.RemoveFromClassList("opt");
                ClearMarks();
                hover?.Invoke(-1);
                // The click that picked a card must not also skip the next line.
                pressed = false;
            }
            return picked;
        }

        // ── Input and per-frame animation ────────────────────────────────

        private void Update()
        {
            if (!IsOpen) return;
            int step = DotSteps[(int)(Time.unscaledTime/DotSeconds)%DotSteps.Length];
            foreach (var slot in slots)
                if (slot.Thinking) slot.Status.text = "생각 중"+new string('.', step);
            if (Input.GetKeyDown(KeyCode.Space) || (Input.GetMouseButtonDown(0) && !OverAutoToggle(Input.mousePosition)))
                pressed = true;
        }

        /// <summary>Takes the pending click or Space press, if any, so one press never counts twice.</summary>
        private bool Consume()
        {
            if (!pressed) return false;
            pressed = false;
            return true;
        }

        private bool OverAutoToggle(Vector2 screenPoint)
        {
            var panel = autoToggle?.panel;
            if (panel == null) return false;
            var point = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPoint.x, Screen.height-screenPoint.y));
            var picked = panel.Pick(point);
            return picked != null && (picked == autoToggle || autoToggle.Contains(picked));
        }

        private void ToggleAutoAdvance()
        {
            autoAdvance = !autoAdvance;
            PlayerPrefs.SetInt(AutoAdvanceKey, autoAdvance ? 1 : 0);
            PlayerPrefs.Save();
            // Turning auto-advance on while a line waits starts its reading time from now.
            waitStartedAt = Time.unscaledTime;
            PaintAutoToggle();
        }

        private void PaintAutoToggle()
        {
            autoToggle.text = autoAdvance ? "자동 진행 ON" : "자동 진행 OFF";
            autoToggle.EnableInClassList("on", autoAdvance);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private void ShowNarrator(string line)
        {
            box.AddToClassList("narration");
            Show(speaker, false);
            text.text = line;
        }

        private void SetMark(HeroRole role, string mark)
        {
            var label = slots[(int)role].Mark;
            label.text = mark ?? "";
            label.EnableInClassList("want", mark == DialogueLines.Wants);
            label.EnableInClassList("pass", mark == DialogueLines.Passes);
            label.EnableInClassList("worse", mark == DialogueLines.Worse);
            Show(label, !string.IsNullOrEmpty(mark));
        }

        private void ClearMarks()
        {
            if (root == null) return;
            foreach (var slot in slots) Show(slot.Mark, false);
        }

        private void ShowItem(Equipment item)
        {
            if (itemCard == null) return;
            Show(itemCard, item != null);
            if (item == null) return;
            itemCard.EnableInClassList("rare", item.Grade == EquipmentGrade.Rare);
            itemGrade.text = item.GradeName + (item.Slot == EquipmentSlot.Armor ? " 방어구" : " 무기");
            itemName.text = item.Name;
            itemStats.text = item.Stats;
            itemPower.text = "위력 " + item.Power;
        }

        private static void RefreshThinking(Slot slot)
        {
            slot.Root.EnableInClassList("thinking", slot.Thinking);
            SetStatus(slot, slot.Down ? "쓰러짐" : slot.Thinking ? "생각 중." : "");
        }

        private static void SetStatus(Slot slot, string value)
        {
            slot.Status.text = value;
            Show(slot.Status, value.Length > 0);
        }

        private static void SetScale(VisualElement element, float scale) =>
            element.style.scale = new Scale(new Vector3(scale, scale, 1));

        private static string Signed(int value) => value >= 0 ? "+"+value : value.ToString();

        private static void Show(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private static Color Hex(int rgb) =>
            new Color32((byte)(rgb >> 16), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF), 255);

        /// <summary>
        /// A d20 drawn as a hexagon with its face number. A natural 20 glows gold and a natural 1 turns dark red
        /// with a crack; both are only for show, the check itself treats them like any other number.
        /// </summary>
        private sealed class Die : VisualElement
        {
            private static readonly Color Paper = Hex(0xF0EAE0), Ink = Hex(0x151B20);
            private static readonly Color Gold = Hex(0xFFD766), Glow = new Color(1f, .84f, .4f, .45f);
            private static readonly Color Cracked = Hex(0x8A2F22), Crack = Hex(0xFFD9CF);
            private static readonly Color Divine = Hex(0xF3D27A);
            private readonly Label number;
            private readonly bool god;
            private bool natural20, natural1;

            public Die(bool god = false)
            {
                this.god = god;
                AddToClassList("stage-die");
                EnableInClassList("god", god);
                pickingMode = PickingMode.Ignore;
                number = new Label("?") { pickingMode = PickingMode.Ignore };
                number.AddToClassList("stage-die-number");
                Add(number);
                generateVisualContent += Draw;
            }

            public void Show(int face) => number.text = face.ToString();

            public void Land(int face)
            {
                Show(face);
                natural20 = face == 20;
                natural1 = face == 1;
                EnableInClassList("nat1", natural1);
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                var rect = contentRect;
                if (rect.width <= 0 || rect.height <= 0) return;
                var painter = context.painter2D;
                var center = rect.center;
                float radius = Mathf.Min(rect.width, rect.height)*.5f-3;
                painter.lineJoin = LineJoin.Round;

                if (natural20)
                {
                    Hexagon(painter, center, radius+5);
                    painter.fillColor = Glow;
                    painter.Fill();
                }
                Hexagon(painter, center, radius);
                painter.fillColor = natural20 ? Gold : natural1 ? Cracked : god ? Divine : Paper;
                painter.Fill();
                painter.strokeColor = Ink;
                painter.lineWidth = 3;
                painter.Stroke();

                // The faint inner triangle that makes the hexagon read as an icosahedron.
                painter.BeginPath();
                painter.MoveTo(center+new Vector2(0, -radius*.62f));
                painter.LineTo(center+new Vector2(radius*.56f, radius*.36f));
                painter.LineTo(center+new Vector2(-radius*.56f, radius*.36f));
                painter.ClosePath();
                painter.strokeColor = new Color(Ink.r, Ink.g, Ink.b, .3f);
                painter.lineWidth = 1.5f;
                painter.Stroke();

                if (!natural1) return;
                painter.BeginPath();
                painter.MoveTo(center+new Vector2(-radius*.4f, -radius*.8f));
                painter.LineTo(center+new Vector2(-radius*.12f, -radius*.24f));
                painter.LineTo(center+new Vector2(-radius*.28f, radius*.04f));
                painter.LineTo(center+new Vector2(radius*.04f, radius*.4f));
                painter.LineTo(center+new Vector2(-radius*.08f, radius*.84f));
                painter.strokeColor = Crack;
                painter.lineWidth = 2.5f;
                painter.Stroke();
            }

            private static void Hexagon(Painter2D painter, Vector2 center, float radius)
            {
                painter.BeginPath();
                for (int corner = 0; corner < 6; corner++)
                {
                    float angle = (-90+60*corner)*Mathf.Deg2Rad;
                    var point = center+new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))*radius;
                    if (corner == 0) painter.MoveTo(point);
                    else painter.LineTo(point);
                }
                painter.ClosePath();
            }
        }
    }
}
