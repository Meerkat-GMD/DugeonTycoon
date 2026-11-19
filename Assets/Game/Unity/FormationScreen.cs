using System;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DungeonTycoon.Unity
{
    /// <summary>
    /// The formation screen before the first departure (Resources/Formation/FormationScreen.uxml + .uss): four
    /// portraits with each hero's stats and traits, Start at the bottom right. Clicking a hero opens the editor,
    /// one step at a time: stat points, then the combat trait, then the personality trait. The change applies only
    /// when the last step is confirmed; Core computes every number shown.
    /// </summary>
    public sealed class FormationScreen : MonoBehaviour
    {
        private const string Teal = "#7BD5BF", Ember = "#EC9179";
        private static readonly string[] StepTitles = { "스탯 포인트", "전투 성향", "성격 성향" };
        private static readonly BuildStat[] Stats = (BuildStat[])Enum.GetValues(typeof(BuildStat));
        private static readonly HeroRole[] Order =
            { HeroRole.Tank, HeroRole.Healer, HeroRole.MeleeDamage, HeroRole.RangedDamage };

        private DungeonGame game;
        private VisualElement root, party, editor, editorStats, steps, body, partyGuide, editorGuide;
        private Label sub, hint, warn, editorName, partyGuideText, editorGuideText;
        private Button partyGuideSkip;
        private Button prev, next;
        private HeroRole editing;
        private HeroBuild draft;
        private int step;
        private DungeonRun guideRun;
        private bool guideFinished, addedPoint;
        private HeroRole? confirmedRole;

        public bool IsOpen { get; private set; }
        private bool Editing => editor != null && editor.resolvedStyle.display == DisplayStyle.Flex;
        private bool GuideActive => game.TutorialActive && !guideFinished;

        // Added by DungeonGame after the HUD, so by Start the HUD document and its `hud` element exist.
        private void Start()
        {
            game = GetComponent<DungeonGame>();
            var document = GetComponent<UIDocument>();
            var hud = document ? document.rootVisualElement.Q("hud") : null;
            var tree = Resources.Load<VisualTreeAsset>("Formation/FormationScreen");
            if (game == null || hud == null || tree == null)
            {
                Debug.LogError("FormationScreen: the game, the HUD document or Resources/Formation/FormationScreen is missing.");
                enabled = false;
                return;
            }
            tree.CloneTree(hud);
            root = hud.Q("formation-screen");
            party = root.Q("formation-party");
            sub = root.Q<Label>("formation-sub");
            editor = root.Q("formation-editor");
            editorStats = root.Q("editor-stats");
            editorName = root.Q<Label>("editor-name");
            steps = root.Q("editor-steps");
            hint = root.Q<Label>("editor-hint");
            body = root.Q("editor-body");
            warn = root.Q<Label>("editor-warn");
            prev = root.Q<Button>("editor-prev");
            next = root.Q<Button>("editor-next");
            partyGuide = root.Q("formation-guide");
            partyGuideText = root.Q<Label>("formation-guide-text");
            partyGuideSkip = root.Q<Button>("formation-guide-skip");
            editorGuide = root.Q("editor-guide");
            editorGuideText = root.Q<Label>("editor-guide-text");
            partyGuideSkip.clicked += SkipGuide;
            root.Q<Button>("editor-guide-skip").clicked += SkipGuide;

            root.Q<Button>("formation-back").clicked += Close;
            root.Q<Button>("formation-start").clicked += () =>
            {
                Close();
                game.Depart();
            };
            root.Q<Button>("editor-cancel").clicked += CloseEditor;
            root.Q<Button>("editor-restore").clicked += () =>
            {
                draft = HeroBuild.Default(editing);
                addedPoint = false;
                warn.text = "완료하면 기본값이 반영됩니다.";
                RenderEditor();
            };
            prev.clicked += () =>
            {
                step = Math.Max(0, step - 1);
                warn.text = "";
                RenderEditor();
            };
            next.clicked += () =>
            {
                if (step < StepTitles.Length - 1)
                {
                    step++;
                    warn.text = "";
                    RenderEditor();
                    return;
                }
                game.Run.SetBuild(editing, draft);
                if (GuideActive)
                {
                    guideFinished = true;
                    confirmedRole = editing;
                }
                CloseEditor();
                RenderParty();
            };
            Close();
        }

        private void Update()
        {
            if (IsOpen && Editing && Input.GetKeyDown(KeyCode.Escape)) CloseEditor();
            else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        /// <summary>Shows the formation before the first departure. Does nothing once the party has set off.</summary>
        public void Open()
        {
            if (root == null || !game.Run.CanEditFormation) return;
            if (guideRun != game.Run)
            {
                guideRun = game.Run;
                guideFinished = false;
            }
            confirmedRole = null;
            IsOpen = true;
            sub.text = game.TutorialActive
                ? "탱커와 근딜이 앞에, 힐러와 원딜이 뒤에 섭니다. 기본 편성으로 바로 출발할 수 있습니다."
                : "출발하면 편성이 확정됩니다.";
            root.style.display = DisplayStyle.Flex;
            CloseEditor();
            RenderParty();
        }

        public void Close()
        {
            IsOpen = false;
            if (root == null) return;
            root.style.display = DisplayStyle.None;
            CloseEditor();
        }

        // ── The four cards ───────────────────────────────────────────────

        private void RenderParty()
        {
            bool showGuide = GuideActive || confirmedRole.HasValue;
            partyGuide.style.display = showGuide ? DisplayStyle.Flex : DisplayStyle.None;
            partyGuideSkip.style.display = GuideActive ? DisplayStyle.Flex : DisplayStyle.None;
            HeroRole focusRole = confirmedRole ?? HeroRole.MeleeDamage;
            string focusName = game.Run.Party[(int)focusRole].Name;
            partyGuideText.text = confirmedRole.HasValue
                ? $"{focusName} 카드에 편성이 반영됐습니다. 출발 전까지 다시 바꿀 수 있습니다."
                : $"근딜 {focusName} 카드를 눌러 능력치와 성향을 바꿔 보세요.";
            party.Clear();
            foreach (var role in Order)
            {
                var hero = game.Run.Party[(int)role];
                var build = hero.Build;
                var card = new Button(() => OpenEditor(role)) { focusable = false };
                card.AddToClassList("formation-card");
                card.AddToClassList("role-" + (int)role);
                card.EnableInClassList("formation-guide-focus", showGuide && role == focusRole);

                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("card-art");
                art.Add(Text("수정", "card-edit"));
                card.Add(art);

                var info = new VisualElement { pickingMode = PickingMode.Ignore };
                info.AddToClassList("card-info");
                info.Add(NameRow(hero, "card-name"));
                AddStatRows(info, role, build, false);
                info.Add(Chip(TraitCatalog.Of(build.Combat), "전투", role, true));
                info.Add(Chip(TraitCatalog.Of(build.Personality), "성격", role, false));
                var status = Text((build.Equals(HeroBuild.Default(role)) ? "기본 편성" : "수정됨") + " · " +
                                  (build.Left > 0 ? $"남은 포인트 {build.Left}" : "포인트 배분 완료"), "card-status");
                status.EnableInClassList("left", build.Left > 0);
                info.Add(status);
                card.Add(info);
                if (GuideActive && role == focusRole) TutorialHighlight.SetPulse(card, true);
                party.Add(card);
            }
        }

        private static VisualElement NameRow(HeroState hero, string className)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList(className);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.Add(Text(hero.Name, null));
            var role = Text("  " + RoleName(hero.Role), "role");
            row.Add(role);
            return row;
        }

        /// <summary>One trait on a card: its name, what it changes in the stats and, in one line, what the hero does.</summary>
        private static VisualElement Chip(TraitInfo trait, string kind, HeroRole role, bool combat)
        {
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("card-chip");
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("chip-head");
            head.Add(Text(trait.Name, "chip-name"));
            head.Add(Text(kind, "kind"));
            chip.Add(head);
            chip.Add(Text(Describe(trait.Bonus), "chip-bonus"));
            chip.Add(Text(Action(trait, role, combat), "chip-action"));
            return chip;
        }

        /// <summary>The one-line action, marked when it does not happen in play yet.</summary>
        private static string Action(TraitInfo trait, HeroRole role, bool combat)
        {
            if (trait.OnlyFor is HeroRole only && only != role)
                return $"{trait.Summary} <color=#EC9179>({RoleName(only)} 전용)</color>";
            if (!trait.Works) return $"{trait.Summary} <color=#7E8A8E>(미구현)</color>";
            return trait.Summary;
        }

        /// <summary>The four starting stats; with <paramref name="change"/> each shows how far it moved from the role base.</summary>
        private static void AddStatRows(VisualElement parent, HeroRole role, HeroBuild build, bool change)
        {
            var now = HeroStats.Starting(role, build);
            var bare = HeroStats.Base(role);
            foreach (var stat in Stats)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("stat-row");
                row.Add(Text(HeroTraits.Name(stat), "stat-name"));
                string value = stat == BuildStat.Speed ? Percent(now.SpeedHundredths) : Value(now, stat).ToString();
                if (change)
                {
                    int delta = stat == BuildStat.Speed ? now.SpeedHundredths - bare.SpeedHundredths : Value(now, stat) - Value(bare, stat);
                    if (delta != 0)
                    {
                        string amount = stat == BuildStat.Speed ? Signed(delta / 100.0, "0.#") + "%" : Signed(delta, "0");
                        value += $" <color={(delta > 0 ? Teal : Ember)}>({amount})</color>";
                    }
                }
                row.Add(Text(value, null));
                parent.Add(row);
            }
        }

        private static int Value(HeroStatLine line, BuildStat stat)
        {
            switch (stat)
            {
                case BuildStat.MaxHp: return line.MaxHp;
                case BuildStat.Attack: return line.Attack;
                case BuildStat.Armor: return line.Armor;
                default: return line.SpeedHundredths;
            }
        }

        // ── The editor ───────────────────────────────────────────────────

        private void OpenEditor(HeroRole role)
        {
            if (confirmedRole.HasValue)
            {
                confirmedRole = null;
                RenderParty();
            }
            editing = role;
            draft = game.Run.BuildOf(role);
            step = 0;
            addedPoint = false;
            warn.text = "";
            for (int i = 0; i < 4; i++) editor.EnableInClassList("role-" + i, i == (int)role);
            partyGuide.style.visibility = Visibility.Hidden;
            editor.style.display = DisplayStyle.Flex;
            RenderEditor();
        }

        private void CloseEditor()
        {
            editor.style.display = DisplayStyle.None;
            partyGuide.style.visibility = Visibility.Visible;
            TutorialHighlight.SetPulse(next, false);
            draft = null;
        }

        private void SkipGuide()
        {
            guideFinished = true;
            RenderParty();
            if (draft != null) RenderEditor();
        }

        private void RenderEditor()
        {
            var hero = game.Run.Party[(int)editing];
            editorName.text = $"{hero.Name}  <size=11><color=#B3BEBB>{RoleName(editing)}</color></size>";
            editorStats.Clear();
            AddStatRows(editorStats, editing, draft, true);

            steps.Clear();
            for (int i = 0; i < StepTitles.Length; i++)
            {
                var chip = Text($"{i + 1}. {StepTitles[i]}", "editor-step");
                chip.EnableInClassList("now", i == step);
                chip.EnableInClassList("past", i < step);
                steps.Add(chip);
            }
            hint.style.display = step == 0 && !GuideActive ? DisplayStyle.Flex : DisplayStyle.None;
            prev.style.display = step > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            next.text = step == StepTitles.Length - 1 ? "완료" : "확인";
            editorStats.EnableInClassList("formation-guide-focus", GuideActive && step == 0 && (draft.Left > 0 || addedPoint));
            TutorialHighlight.SetPulse(next, GuideActive && step == 0 && addedPoint && draft.Left == 0);
            editorGuide.style.display = GuideActive ? DisplayStyle.Flex : DisplayStyle.None;
            if (GuideActive) editorGuideText.text = GuideMessage();

            body.Clear();
            if (step == 0) RenderPoints();
            else if (step == 1) RenderTraits(true);
            else RenderTraits(false);
        }

        private string GuideMessage()
        {
            if (step == 1)
                return "전투 성향은 예고 공격에 대응하는 법이나 우선 공격할 대상을 바꿉니다. 카드를 눌러 발동 조건과 행동을 확인해 보세요.";
            if (step == 2)
                return "성격 성향은 갈림길 의견, 장비 입찰이나 기도에 영향을 줍니다. 승부광은 적이 보이는 길을, 신중한 길잡이는 안전한 길을 선호합니다.";
            if (draft.Left > 0)
                return "+를 눌러 원하는 능력치에 포인트를 써 보세요. 왼쪽 수치는 즉시 바뀌며, 포인트를 남겨도 됩니다.";
            if (addedPoint)
                return "왼쪽에서 바뀐 능력치를 확인해 보세요. 확인을 누르면 전투 성향을 고를 수 있습니다.";
            return "−를 눌러 아무 능력치에서 1점을 빼 보세요. 돌려받은 포인트는 원하는 능력치에 쓸 수 있습니다.";
        }

        private void RenderPoints()
        {
            string valueColor = draft.Left == 0 ? "#FF7777" : "#F0EAE0";
            var pointsLeft = Text($"남은 포인트 <size=20><b><color={valueColor}>{draft.Left}</color></b></size> / {HeroBuild.Budget}", "points-left");
            pointsLeft.EnableInClassList("formation-guide-focus", GuideActive && (!addedPoint || draft.Left > 0));
            body.Add(pointsLeft);

            bool highlightAdd = GuideActive && draft.Left > 0;
            bool highlightRefund = GuideActive && draft.Left == 0 && !addedPoint;
            foreach (var stat in Stats)
            {
                int points = draft.Points(stat);
                int gain = HeroStats.Gain(stat);
                var row = new VisualElement();
                row.AddToClassList("point-row");
                row.Add(Text(HeroTraits.Name(stat), "point-name"));
                row.Add(Text($"점당 +{gain}{(stat == BuildStat.Speed ? "%" : "")}", "point-gain"));
                var refund = StepButton("−", () => ChangePoints(stat, -1));
                refund.EnableInClassList("formation-guide-focus", highlightRefund && points > 0);
                if (highlightRefund && points > 0) TutorialHighlight.SetPulse(refund, true);
                row.Add(refund);
                row.Add(Text(points.ToString(), "point-value"));
                var add = StepButton("+", () => ChangePoints(stat, 1));
                add.EnableInClassList("formation-guide-focus", highlightAdd);
                if (highlightAdd) TutorialHighlight.SetPulse(add, true);
                row.Add(add);
                body.Add(row);
            }
        }

        private void ChangePoints(BuildStat stat, int delta)
        {
            int value = draft.Points(stat) + delta;
            if (value < 0) return;
            if (delta > 0 && draft.Left == 0)
            {
                warn.text = "남은 포인트가 없습니다.";
                return;
            }
            draft = draft.WithPoints(stat, value);
            if (delta > 0) addedPoint = true;
            warn.text = "";
            RenderEditor();
        }

        private static Button StepButton(string text, Action action)
        {
            var button = new Button(action) { text = text, focusable = false };
            button.AddToClassList("point-step");
            return button;
        }

        private void RenderTraits(bool combat)
        {
            var list = new VisualElement();
            list.AddToClassList("trait-list");
            if (combat)
                foreach (var trait in TraitCatalog.CombatTraits)
                    list.Add(TraitButton(TraitCatalog.Of(trait), trait == draft.Combat, true, () => Choose(draft.With(trait))));
            else
                foreach (var trait in TraitCatalog.PersonalityTraits)
                    list.Add(TraitButton(TraitCatalog.Of(trait), trait == draft.Personality, false, () => Choose(draft.With(trait))));
            body.Add(list);

            var chosen = combat ? TraitCatalog.Of(draft.Combat) : TraitCatalog.Of(draft.Personality);
            var about = new VisualElement { pickingMode = PickingMode.Ignore };
            about.AddToClassList("trait-about");
            about.EnableInClassList("formation-guide-focus", GuideActive);
            about.Add(Text($"<b>{chosen.Name}</b> · {chosen.Trigger}", null));
            if (GuideActive) about.Add(Text(chosen.Summary, null));
            about.Add(Text($"“{chosen.Line}”", "line"));
            if (!chosen.Works || (chosen.OnlyFor is HeroRole only && only != editing)) about.Add(Text(Status(chosen, combat), "note"));
            body.Add(about);
        }

        private void Choose(HeroBuild build)
        {
            draft = build;
            RenderEditor();
        }

        private Button TraitButton(TraitInfo trait, bool chosen, bool combat, Action pick)
        {
            var button = new Button(pick) { focusable = false };
            button.AddToClassList("trait");
            button.EnableInClassList("chosen", chosen);
            bool blocked = trait.OnlyFor is HeroRole only && only != editing;
            button.EnableInClassList("blocked", blocked);
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("trait-head");
            head.Add(Text(trait.Name, "trait-name"));
            var badge = Text(blocked ? RoleName(trait.OnlyFor.Value) + " 전용" : trait.Works ? "동작" : "행동 미구현", "trait-badge");
            badge.EnableInClassList("only", blocked);
            badge.EnableInClassList("works", !blocked && trait.Works);
            head.Add(badge);
            button.Add(head);
            button.Add(Text(Describe(trait.Bonus), "trait-mods"));
            button.Add(Text(trait.Summary, "trait-summary"));
            return button;
        }

        private string Status(TraitInfo trait, bool combat)
        {
            if (trait.OnlyFor is HeroRole only && only != editing)
                return $"{RoleName(only)}가 아니라 발동하지 않는다. 스탯 보정만 적용된다.";
            if (trait.Works) return "동작한다.";
            return combat
                ? "대응할 상황이 아직 없어 스탯 보정만 적용된다. 그 밖의 상황은 역할 기본 전술을 따른다."
                : "아직 행동이 없어 스탯 보정만 적용된다.";
        }

        /// <summary>Trait bonuses as coloured text, e.g. "체력 +20 · 공격력 -2".</summary>
        private static string Describe(StatBonus bonus)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var stat in Stats)
            {
                int amount = bonus[stat];
                if (amount == 0) continue;
                parts.Add($"<color={(amount > 0 ? Teal : Ember)}>{HeroTraits.Name(stat)} {Signed(amount, "0")}{(stat == BuildStat.Speed ? "%" : "")}</color>");
            }
            return string.Join(" · ", parts);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = true };
            if (className != null) label.AddToClassList(className);
            return label;
        }

        private static string Percent(int hundredths) => (hundredths / 100.0).ToString("0.#") + "%";
        private static string Signed(double value, string format) => (value > 0 ? "+" : "") + value.ToString(format);

        private static string RoleName(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank: return "탱커";
                case HeroRole.Healer: return "힐러";
                case HeroRole.MeleeDamage: return "근거리 딜러";
                default: return "원거리 딜러";
            }
        }
    }
}
