using System.Linq;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DungeonTycoon.Unity
{
    public sealed partial class DungeonHud
    {
        private const string TutorialSeenKey = "StageOneTutorialSeen";
        private const float TutorialNoticeSeconds = 5f;
        private static readonly TilePoint TutorialTile = new TilePoint(10, 23);
        private static readonly TilePoint TutorialSecondTile = new TilePoint(28, 6);

        private VisualElement tutorialCard, tutorialConnector, tutorialFocus;
        private bool tutorialFocusPulses;
        private Label tutorialTitle, tutorialBody, tutorialCarryover;
        private Button tutorialSkip, tutorialContinue;
        private bool tutorialActive, tutorialSelectedSkeleton, tutorialPlaced, tutorialOracleSeen, tutorialPrayerSeen;
        private bool tutorialXpSeen, tutorialBlessSeen, tutorialChestSeen, tutorialFatigueSeen, tutorialDeparted;
        private int tutorialPausedSpeed;
        private float tutorialNoticeUntil;
        private string tutorialNoticeTitle, tutorialNoticeBody;
        private VisualElement tutorialNoticeAnchor;

        internal bool TutorialActive => tutorialActive && game.Run.Stage == 1;

        private void BindTutorial(VisualElement root)
        {
            tutorialCard = root.Q("tutorial-card");
            tutorialConnector = root.Q("tutorial-connector");
            tutorialTitle = tutorialCard.Q<Label>("tutorial-title");
            tutorialBody = tutorialCard.Q<Label>("tutorial-body");
            tutorialContinue = tutorialCard.Q<Button>("tutorial-continue");
            tutorialSkip = root.Q<Button>("tutorial-skip");
            tutorialCarryover = root.Q<Label>("tutorial-carryover");
            tutorialSkip.clicked += DismissTutorial;
            tutorialContinue.clicked += ResumeTutorialPause;
        }

        private void ResetTutorial(DungeonRun run)
        {
            tutorialActive = run.Stage == 1 && PlayerPrefs.GetInt(TutorialSeenKey) == 0;
            tutorialSelectedSkeleton = tutorialPlaced = tutorialOracleSeen = tutorialPrayerSeen = false;
            tutorialXpSeen = tutorialBlessSeen = tutorialChestSeen = tutorialFatigueSeen = tutorialDeparted = false;
            tutorialPausedSpeed = 0;
            tutorialNoticeUntil = 0;
        }

        private void DismissTutorial()
        {
            tutorialActive = false;
            PlayerPrefs.SetInt(TutorialSeenKey, 1);
            PlayerPrefs.Save();
            ResumeTutorialPause();
            HideTutorialCard();
            Show(tutorialSkip, false);
            Show(tutorialCarryover, false);
        }

        private void ResumeTutorialPause()
        {
            if (tutorialPausedSpeed == 0) return;
            int speed = tutorialPausedSpeed;
            tutorialPausedSpeed = 0;
            Show(blessPicker, false);
            if (game.Speed == 0) game.SetSpeed(speed);
            HideTutorialCard();
        }

        internal bool TryExplainOracle()
        {
            if (!TutorialActive || tutorialOracleSeen) return false;
            tutorialOracleSeen = true;
            return true;
        }

        internal bool TryExplainPrayer()
        {
            if (!TutorialActive || tutorialPrayerSeen) return false;
            tutorialPrayerSeen = true;
            return true;
        }

        private void RefreshTutorial(DungeonRun run)
        {
            bool fieldVisible = TutorialActive && !game.Presenting && !game.Formation.IsOpen &&
                !HistoryOpen && !audioPanel.ClassListContains("open");
            bool playingField = fieldVisible && (run.Phase == RunPhase.Planning || Exploring(run));
            Show(tutorialSkip, playingField);
            bool showCarryover = TutorialActive && stageClearPage.style.display == DisplayStyle.Flex;
            if (showCarryover && tutorialCarryover.style.display != DisplayStyle.Flex)
            {
                var gear = run.Party.SelectMany(hero => new[] { hero.Weapon, hero.BodyArmor })
                    .Where(item => item != null).Select(item => item.Name).ToArray();
                tutorialCarryover.text = $"파티 레벨 {run.Level} 유지 · 장착 장비 {(gear.Length == 0 ? "없음" : string.Join(" · ", gear))}";
            }
            Show(tutorialCarryover, showCarryover);
            if (!playingField)
            {
                HideTutorialCard();
                return;
            }

            if (run.Phase == RunPhase.Planning)
            {
                RefreshPlacementTutorial(run);
                return;
            }

            if (tutorialPausedSpeed > 0)
            {
                if (game.Speed == 0)
                {
                    ShowBlessingTutorial();
                    return;
                }
                ResumeTutorialPause();
            }

            if (!tutorialDeparted)
            {
                tutorialDeparted = true;
                SetTutorialNotice("용사들의 원정을 지켜보세요",
                    "용사들은 스스로 길을 찾고 싸웁니다. 자세히 보고 싶으면 일시정지를 누르세요.", speedButtons[0]);
            }
            if (!tutorialXpSeen && run.LastSpoils is { Experience: > 0 } && run.PendingLoot == null)
            {
                tutorialXpSeen = true;
                SetTutorialNotice("용사들이 경험치를 얻었습니다",
                    "경험치가 쌓이면 파티 레벨이 오르고 용사들의 공격력과 최대 체력이 오릅니다.", partyLevel);
            }
            if (tutorialXpSeen && !tutorialBlessSeen && game.Speed > 0 &&
                run.Phase == RunPhase.Travel && run.PendingFork == null && run.PendingPrayer == null &&
                run.PendingLoot == null && run.Party.Any(hero => run.CanBlessHero(hero.Role)))
            {
                tutorialBlessSeen = true;
                tutorialPausedSpeed = game.Speed;
                game.SetSpeed(0);
                ShowBlessingTutorial();
                return;
            }
            if (Time.unscaledTime < tutorialNoticeUntil)
            {
                ShowTutorialCard(tutorialNoticeTitle, tutorialNoticeBody, tutorialNoticeAnchor);
                return;
            }
            if (!tutorialChestSeen && run.Chests.Any(chest => chest.Opened))
            {
                tutorialChestSeen = true;
                SetTutorialNotice("보물 상자에서 장비를 얻었습니다",
                    "용사들은 발견한 상자를 찾아 열고, 장착한 장비는 다음 스테이지로 가져갑니다.", partyLevel);
            }
            else if (!tutorialFatigueSeen && run.Fatigue != FatigueStage.Rested)
            {
                tutorialFatigueSeen = true;
                SetTutorialNotice("용사들이 오래 걸어 지쳤습니다",
                    "지침 상태에서는 공격력이 내려갑니다. 신앙을 써서 피로를 줄일 수 있습니다.", fatigue);
            }

            if (Time.unscaledTime < tutorialNoticeUntil)
                ShowTutorialCard(tutorialNoticeTitle, tutorialNoticeBody, tutorialNoticeAnchor);
            else HideTutorialCard();
        }

        private void ShowBlessingTutorial()
        {
            bool picking = blessPicker.style.display == DisplayStyle.Flex;
            ShowTutorialCard("축복으로 용사를 도울 수 있습니다",
                $"전투로 경험치를 얻었습니다. 신앙 {DungeonRun.BlessCost}을 써서 다친 용사를 회복시키거나 계속 진행하세요.",
                picking ? blessPicker : blessCard, canContinue: true);
        }

        private void RefreshPlacementTutorial(DungeonRun run)
        {
            bool hasPlacement = tutorialPlaced || run.Placements.Count > 0;
            bool collapsed = bottom.ClassListContains("collapsed");
            bool showSecond = !collapsed && brush == MonsterKind.Skeleton && run.Placements.Count == 1 &&
                run.CountAt(TutorialTile) == 1 && run.Placements[0].Kind == MonsterKind.Skeleton &&
                run.CheckPlace(TutorialSecondTile, MonsterKind.Skeleton) == PlacementCheck.Allowed;
            if (hasPlacement)
            {
                game.Board.SetTutorialPreview(showSecond ? TutorialSecondTile : (TilePoint?)null);
                if (showSecond && game.Board.TryTutorialTileScreenPoint(TutorialSecondTile, out var nextPoint))
                {
                    ShowTutorialCard("두 번째 교전을 나눠 보세요",
                        "두 번째 해골 검사는 보스방으로 가는 길에 따로 둘 수 있습니다. 준비가 끝났다면 출발해도 됩니다.",
                        budget, nextPoint);
                    return;
                }
                ShowTutorialCard("전투가 몰리지 않게 배치하세요",
                    "금색으로 표시되는 칸에서는 순회조와 교전이 겹칠 수 있습니다. 두 길목에 적을 나눠 두면 따로 싸우기 쉽습니다.",
                    collapsed ? collapse : budget);
                return;
            }
            bool showTile = !collapsed && tutorialSelectedSkeleton && brush == MonsterKind.Skeleton &&
                run.CheckPlace(TutorialTile, MonsterKind.Skeleton) == PlacementCheck.Allowed;
            game.Board.SetTutorialPreview(showTile ? TutorialTile : (TilePoint?)null);
            if (showTile && game.Board.TryTutorialTileScreenPoint(TutorialTile, out var screenPoint))
            {
                ShowTutorialCard("첫 몬스터 배치", "빛나는 칸을 눌러 해골 검사를 놓으세요.",
                    brushCards[MonsterKind.Skeleton], screenPoint);
                return;
            }
            ShowTutorialCard("용사들이 싸울 길을 준비하세요",
                showTile ? "전체 보기에서 추천 타일을 확인하세요." :
                    "용사들은 입구에서 붉은 카펫이 있는 보스방을 찾아갑니다. 보라색 점선은 고정 순회로입니다. 해골 검사를 골라 보세요.",
                showTile ? whole : collapsed ? collapse : brushCards[MonsterKind.Skeleton], pulseAnchor: true);
        }

        private void SetTutorialNotice(string title, string body, VisualElement anchor)
        {
            tutorialNoticeTitle = title;
            tutorialNoticeBody = body;
            tutorialNoticeAnchor = anchor;
            tutorialNoticeUntil = Time.unscaledTime + TutorialNoticeSeconds;
        }

        private void ShowTutorialCard(string title, string body, VisualElement anchor, Vector2? screenPoint = null,
            bool canContinue = false, bool pulseAnchor = false)
        {
            if (tutorialTitle.text != title) tutorialTitle.text = title;
            if (tutorialBody.text != body) tutorialBody.text = body;
            Show(tutorialContinue, canContinue);
            Show(tutorialConnector, screenPoint.HasValue);
            Show(tutorialCard, true);
            SetTutorialFocus(anchor, pulseAnchor);

            var width = tutorialCard.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) width = 230;
            var height = tutorialCard.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0) height = 78;
            float maxX = Mathf.Max(8, hud.layout.width - width - 8);
            float maxY = Mathf.Max(8, hud.layout.height - height - 8);
            if (screenPoint.HasValue)
            {
                var point = screenPoint.Value;
                var panelPoint = RuntimePanelUtils.ScreenToPanel(hud.panel, new Vector2(point.x, Screen.height - point.y));
                float x = panelPoint.x + 24;
                if (x > maxX) x = panelPoint.x - width - 24;
                float cardX = Mathf.Clamp(x, 8, maxX);
                tutorialCard.style.left = cardX;
                tutorialCard.style.top = Mathf.Clamp(panelPoint.y - height * .5f, 8, maxY);
                float tileEdge = panelPoint.x + (cardX > panelPoint.x ? 6 : -6);
                float cardEdge = cardX > tileEdge ? cardX : cardX + width;
                tutorialConnector.style.left = Mathf.Min(tileEdge, cardEdge);
                tutorialConnector.style.top = panelPoint.y;
                tutorialConnector.style.width = Mathf.Abs(cardEdge - tileEdge);
                return;
            }
            var bounds = anchor.worldBound;
            float left = bounds.xMin;
            float top = bounds.yMin - height - 8;
            if (top < 8 || anchor == partyLevel || anchor == fatigue)
            {
                left = bounds.xMax + 10;
                top = bounds.yMin;
            }
            if (anchor == whole)
            {
                left = bounds.xMax - width;
                top = bounds.yMax + 8;
            }
            tutorialCard.style.left = Mathf.Clamp(left, 8, maxX);
            tutorialCard.style.top = Mathf.Clamp(top, 8, maxY);
        }

        private void SetTutorialFocus(VisualElement element, bool pulse = false)
        {
            if (tutorialFocus == element && tutorialFocusPulses == pulse) return;
            if (tutorialFocus != element)
            {
                if (tutorialFocus != null)
                {
                    TutorialHighlight.SetPulse(tutorialFocus, false);
                    tutorialFocus.RemoveFromClassList("tutorial-focus");
                }
                tutorialFocus = element;
                tutorialFocus?.AddToClassList("tutorial-focus");
            }
            tutorialFocusPulses = pulse;
            if (element != null) TutorialHighlight.SetPulse(element, pulse);
        }

        private void HideTutorialCard()
        {
            Show(tutorialCard, false);
            Show(tutorialConnector, false);
            SetTutorialFocus(null);
            game.Board.SetTutorialPreview(null);
        }
    }
}
