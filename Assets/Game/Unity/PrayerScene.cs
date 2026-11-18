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
    /// Plays a prayer (Resources/Prayer/PrayerScene.uxml + .uss): the screen turns into the sanctum, the praying
    /// hero kneels on the dais at the left, and the GM answers with one of three buttons at the right. A miracle
    /// turns the light gold and the hero bright; looking away puts the light out and the hero sinks into the dark.
    /// Core settles the answer; this only shows it. The scene always waits for the GM, whatever auto-advance says.
    /// </summary>
    public sealed class PrayerScene : MonoBehaviour
    {
        private const float CharsPerSecond = 28, NarrationHold = 1.1f, FlashSeconds = .7f;
        private const float TrembleSeconds = .18f, TremblePixels = 1.2f, SparkSeconds = 1.8f;
        private static readonly PrayerAnswer[] Answers =
            { PrayerAnswer.HealingMiracle, PrayerAnswer.RestMiracle, PrayerAnswer.Ignore };

        private VisualElement root, hero, bubble, choices, flash, sparkles, beam, vignette;
        private VisualElement pray, radiant, despair;
        private Label narration, who, line, stamp, next;
        private readonly Button[] buttons = new Button[3];
        private readonly List<VisualElement> sparks = new List<VisualElement>();
        private int picked = -1;
        private bool pressed;
        private string mood = "";
        private float flashStartedAt = -10;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// For automated play tests only: under <see cref="DialogueStage.FastForward"/> the scene answers at once with
        /// this (0 healing, 1 rest, 2 look away; an answer the GM cannot afford falls back to looking away).
        /// </summary>
        public static int FastAnswer { get; set; } = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFastAnswer() => FastAnswer = 2;

        // Added by DungeonGame after the HUD, so by Start the HUD document and its `hud` element exist.
        private void Start()
        {
            var document = GetComponent<UIDocument>();
            var hud = document ? document.rootVisualElement.Q("hud") : null;
            var tree = Resources.Load<VisualTreeAsset>("Prayer/PrayerScene");
            if (hud == null || tree == null)
            {
                Debug.LogError("PrayerScene: the HUD document or Resources/Prayer/PrayerScene is missing.");
                enabled = false;
                return;
            }
            tree.CloneTree(hud);
            root = hud.Q("prayer-scene");
            hero = root.Q("prayer-hero");
            pray = root.Q("art-pray");
            radiant = root.Q("art-radiant");
            despair = root.Q("art-despair");
            sparkles = root.Q("sparkles");
            beam = root.Q("prayer-beam");
            vignette = root.Q("prayer-vignette");
            bubble = root.Q("prayer-bubble");
            choices = root.Q("prayer-choices");
            flash = root.Q("prayer-flash");
            narration = root.Q<Label>("prayer-narration");
            who = root.Q<Label>("prayer-who");
            line = root.Q<Label>("prayer-line");
            stamp = root.Q<Label>("prayer-stamp");
            next = root.Q<Label>("prayer-next");
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i] = root.Q<Button>("choice-"+i);
                buttons[i].clicked += () => picked = index;
            }
            beam.style.backgroundImage = new StyleBackground(BeamTexture());
            vignette.style.backgroundImage = new StyleBackground(VignetteTexture());
            for (int i = 0; i < 7; i++)
            {
                var spark = new VisualElement { pickingMode = PickingMode.Ignore };
                spark.AddToClassList("spark");
                sparkles.Add(spark);
                sparks.Add(spark);
            }
            Close();
        }

        /// <summary>
        /// Plays the whole prayer: the narrator, the hero's words, the GM's choice, then the miracle or the silence
        /// and the hero's reaction. <paramref name="answer"/> applies the choice in Core and returns the outcome.
        /// </summary>
        public async UniTask PlayAsync(DungeonRun run, PrayerRequest prayer, Func<PrayerAnswer, PrayerOutcome> answer,
            CancellationToken ct)
        {
            if (root == null) return;
            var praying = run.Party[(int)prayer.Role];
            Open(prayer.Role, praying.Name);
            try
            {
                await TypeAsync(narration, DialogueLines.PrayerOpening(praying.Name), ct);
                await HoldAsync(NarrationHold, ct);
                bubble.AddToClassList("show");
                await TypeAsync(line, prayer.Line, ct);

                // The answers: the GM must choose; nothing passes on its own.
                for (int i = 0; i < buttons.Length; i++)
                {
                    var choice = Answers[i];
                    buttons[i].Q<Label>("title").text = DialogueLines.PrayerAnswerName(choice);
                    buttons[i].Q<Label>("desc").text = choice switch
                    {
                        PrayerAnswer.HealingMiracle => $"{praying.Name}의 체력을 모두 회복한다",
                        PrayerAnswer.RestMiracle => "파티의 피로를 크게 줄인다",
                        _ => "신앙을 아낀다. 믿음이 흔들린다"
                    };
                    buttons[i].Q<Label>("cost").text = choice == PrayerAnswer.Ignore ? "" : $"신앙 {DungeonRun.MiracleCost}";
                    buttons[i].SetEnabled(run.CanAnswerPrayer(choice));
                    buttons[i].RemoveFromClassList("picked");
                }
                choices.RemoveFromClassList("done");
                choices.AddToClassList("show");
                choices.pickingMode = PickingMode.Position;
                int index = await PickAsync(run, ct);
                buttons[index].AddToClassList("picked");
                choices.AddToClassList("done");
                choices.pickingMode = PickingMode.Ignore;

                var outcome = answer(Answers[index]);
                if (outcome == null) return;
                narration.text = "";
                if (outcome.Answered) flashStartedAt = Time.unscaledTime;
                SetMood(outcome.Answered ? "radiant" : "despair");
                stamp.text = "믿음 · " + DungeonRun.BeliefName(outcome.Belief);
                stamp.EnableInClassList("firm", outcome.Answered);
                stamp.EnableInClassList("shaken", !outcome.Answered);
                stamp.AddToClassList("show");
                await TypeAsync(narration, outcome.Narration, ct);
                await TypeAsync(line, outcome.Reaction, ct);
                await WaitAsync(ct);
            }
            finally
            {
                Close();
            }
        }

        private void Open(HeroRole role, string name)
        {
            for (int i = 0; i < 4; i++) root.EnableInClassList("role-"+i, i == (int)role);
            who.text = name;
            line.text = narration.text = stamp.text = "";
            bubble.RemoveFromClassList("show");
            choices.RemoveFromClassList("show");
            choices.RemoveFromClassList("done");
            choices.pickingMode = PickingMode.Ignore;
            stamp.RemoveFromClassList("show");
            SetMood("pray");
            picked = -1;
            pressed = false;
            IsOpen = true;
            root.style.display = DisplayStyle.Flex;
        }

        private void Close()
        {
            IsOpen = false;
            if (root == null) return;
            root.style.display = DisplayStyle.None;
            next.style.opacity = 0;
            flash.style.opacity = 0;
        }

        private void SetMood(string value)
        {
            mood = value;
            foreach (var name in new[] { "pray", "radiant", "despair" }) root.EnableInClassList("mood-"+name, name == value);
            pray.EnableInClassList("show", value == "pray");
            radiant.EnableInClassList("show", value == "radiant");
            despair.EnableInClassList("show", value == "despair");
        }

        private async UniTask<int> PickAsync(DungeonRun run, CancellationToken ct)
        {
            if (DialogueStage.FastForward)
            {
                int fast = Mathf.Clamp(FastAnswer, 0, Answers.Length-1);
                return run.CanAnswerPrayer(Answers[fast]) ? fast : Answers.Length-1;
            }
            picked = -1;
            while (picked < 0 || !buttons[picked].enabledSelf)
            {
                for (int i = 0; i < buttons.Length; i++)
                    if ((Input.GetKeyDown(KeyCode.Alpha1+i) || Input.GetKeyDown(KeyCode.Keypad1+i)) && buttons[i].enabledSelf)
                        picked = i;
                if (picked >= 0 && !buttons[picked].enabledSelf) picked = -1;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            // The click that chose an answer must not also skip the next line.
            pressed = false;
            return picked;
        }

        private async UniTask TypeAsync(Label label, string text, CancellationToken ct)
        {
            text ??= "";
            pressed = false;
            if (DialogueStage.FastForward)
            {
                label.text = text;
                return;
            }
            float start = Time.unscaledTime;
            int shown = 0;
            while (shown < text.Length)
            {
                if (Consume()) break;
                shown = Mathf.Min(text.Length, 1+Mathf.FloorToInt((Time.unscaledTime-start)*CharsPerSecond));
                label.text = text.Substring(0, shown);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            label.text = text;
        }

        private async UniTask HoldAsync(float seconds, CancellationToken ct)
        {
            if (DialogueStage.FastForward) return;
            float until = Time.unscaledTime+seconds;
            while (Time.unscaledTime < until && !Consume()) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        /// <summary>Blinks the hint until a click or Space closes the scene.</summary>
        private async UniTask WaitAsync(CancellationToken ct)
        {
            if (DialogueStage.FastForward) return;
            pressed = false;
            while (!Consume())
            {
                next.style.opacity = Mathf.Repeat(Time.unscaledTime, 1f) < .5f ? 1 : .35f;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            next.style.opacity = 0;
        }

        private bool Consume()
        {
            if (!pressed) return false;
            pressed = false;
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) pressed = true;
            float now = Time.unscaledTime;

            // The praying hero trembles; the answered one is lifted by rising sparks.
            if (mood == "pray")
            {
                int step = (int)(now/TrembleSeconds*4)%4;
                float x = step == 1 || step == 3 ? TremblePixels : step == 2 ? -TremblePixels : 0;
                float y = step == 1 ? -TremblePixels : step == 3 ? TremblePixels : 0;
                pray.style.translate = new Translate(x, y);
            }
            else pray.style.translate = new Translate(0, 0);

            for (int i = 0; i < sparks.Count; i++)
            {
                var spark = sparks[i];
                if (mood != "radiant")
                {
                    spark.style.opacity = 0;
                    continue;
                }
                float t = Mathf.Repeat(now/SparkSeconds+i*.37f, 1f);
                spark.style.left = Length.Percent(22+(i*53)%56);
                spark.style.top = Length.Percent(72-t*45);
                spark.style.opacity = t < .3f ? t/.3f : 1-(t-.3f)/.7f;
            }

            float since = now-flashStartedAt;
            flash.style.opacity = since < FlashSeconds ? .85f*(1-since/FlashSeconds) : 0;
        }

        // ── Light made in code, so the effect needs no extra art ──────────

        /// <summary>A soft vertical shaft of gold light: bright in the middle, fading at the edges and the top.</summary>
        private static Texture2D BeamTexture()
        {
            const int width = 64, height = 256;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width*height];
            for (int y = 0; y < height; y++)
            {
                // y = 0 is the bottom row. The light is strongest a little above the floor and fades toward the sky.
                float v = y/(float)(height-1);
                float along = Mathf.Clamp01(Mathf.Sin(Mathf.PI*Mathf.Clamp01(.15f+v*.95f)))*(.55f+.45f*v);
                for (int x = 0; x < width; x++)
                {
                    float across = Mathf.Sin(Mathf.PI*x/(width-1));
                    pixels[y*width+x] = new Color(1f, .86f, .5f, .75f*across*across*along);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>A dark red frame that closes in on the hero when the god looks away.</summary>
        private static Texture2D VignetteTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size*size];
            var centre = new Vector2(.3f, .45f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x/(float)(size-1), y/(float)(size-1));
                float d = Vector2.Distance(p, centre)/.75f;
                float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, 1f, d));
                pixels[y*size+x] = new Color(.17f, .02f, .02f, .92f*a);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
