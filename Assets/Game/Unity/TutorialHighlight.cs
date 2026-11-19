using UnityEngine;
using UnityEngine.UIElements;

namespace DungeonTycoon.Unity
{
    internal static class TutorialHighlight
    {
        private const string PulseClass = "tutorial-pulse";
        private const float Period = 1.4f;

        internal static float Brightness(float startedAt)
        {
            float phase = Mathf.Repeat((Time.unscaledTime - startedAt) / Period, 1f);
            float crest = (1f + Mathf.Cos(phase * 2f * Mathf.PI)) * .5f;
            return crest * crest;
        }

        internal static void SetPulse(VisualElement target, bool active)
        {
            var pulse = target.Q(className: PulseClass);
            if (!active)
            {
                pulse?.RemoveFromHierarchy();
                return;
            }
            if (pulse != null) return;

            pulse = new VisualElement { pickingMode = PickingMode.Ignore };
            pulse.AddToClassList(PulseClass);
            target.Add(pulse);
            float startedAt = Time.unscaledTime;
            pulse.schedule.Execute(() => pulse.style.opacity = Brightness(startedAt)).Every(32);
        }
    }
}
