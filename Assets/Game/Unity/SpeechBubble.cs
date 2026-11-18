using UnityEngine;
using UnityEngine.UIElements;

namespace DungeonTycoon.Unity
{
    internal sealed class SpeechBubble : VisualElement
    {
        private readonly Label expression = new Label { pickingMode = PickingMode.Ignore };

        public SpeechBubble()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("speech-bubble");
            var tail = new VisualElement { pickingMode = PickingMode.Ignore };
            tail.AddToClassList("stage-bubble-tail");
            Add(tail);
            expression.AddToClassList("stage-expression");
            Add(expression);
        }

        public void SetLine(string line)
        {
            var face = ExpressionFor(line);
            expression.text = face == null ? "…" : "";
            expression.style.backgroundImage = face == null ? new StyleBackground(StyleKeyword.None)
                : new StyleBackground(Resources.Load<Texture2D>("Dialogue/Emoji" + face));
        }

        private static string ExpressionFor(string line)
        {
            if (line.Contains("#@#$@#")) return "Angry";
            if (line.Contains("피해!") || line.Contains("온다!")) return "Alert";
            if (line.Contains("계속 친다") || line.Contains("내 뒤로") || line.Contains("이쪽으로 뺄게")) return "Cool";
            if (line.Contains("하하") || line.Contains("빵 터")) return "Laugh";
            if (line.Contains("메롱") || line.Contains("장난이야")) return "Playful";
            if (line.Contains("고마") || line.Contains("사랑")) return "Love";
            if (line.Contains("앞장") || line.Contains("따라와")) return "Cool";
            if (line.Contains("내 말 믿어") || line.Contains("양보할게")) return "Wink";
            if (line.Contains("좋다") || line.Contains("안심하고") || line.Contains("신난다")) return "Smile";
            return null;
        }
    }
}
