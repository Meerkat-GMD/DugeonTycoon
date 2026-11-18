using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    public enum EncounterKind { Merchant, Statue }

    public sealed class MapEncounter
    {
        internal MapEncounter(EncounterKind kind, TilePoint tile)
        {
            Kind = kind;
            Tile = tile;
        }

        public EncounterKind Kind { get; }
        public TilePoint Tile { get; }
        public bool Visited { get; internal set; }
        public bool Accepted { get; internal set; }
    }

    public sealed partial class DungeonRun
    {
        private string UseMerchandise(bool tonic, bool spoiled)
        {
            if (tonic && spoiled)
            {
                var before = Fatigue;
                FatigueSteps += 8;
                LogFatigueChange(before);
                return "쓴 강장차를 마시자 팔다리가 무거워졌다. 파티 피로 +8";
            }
            if (!tonic && !spoiled)
            {
                int relief = Math.Min(FatigueSteps, 12);
                ReduceFatigue(relief);
                return $"갓 구운 빵을 나눠 먹고 기운을 차렸다. 파티 피로 -{relief}";
            }
            var changes = new List<string>();
            foreach (var hero in party.Where(h => h.Hp > 0))
            {
                int before = hero.Hp;
                if (spoiled)
                {
                    int loss = Math.Min(5, before - 1);
                    if (loss > 0) Hurt(hero, loss, "상한 빵", false);
                }
                else
                {
                    hero.Hp = Math.Min(hero.MaxHp, before + 8);
                    if (hero.Hp * 10 >= hero.MaxHp * 3) hero.Critical = false;
                }
                changes.Add($"{hero.Name} {hero.Hp - before:+0;-0;0}");
            }
            return (spoiled ? "빵 속이 상했다! 체력 " : "따뜻한 약초차가 상처를 달랜다. 체력 ") + string.Join(", ", changes);
        }

        private readonly List<MapEncounter> encounters = new List<MapEncounter>();
        public IReadOnlyList<MapEncounter> Encounters => encounters;
        public int Coins { get; private set; } = 2;
        public int StatueAttackModifier => party[0].StatueBonus.Attack;
        public IReadOnlyList<DialogueBeat> EncounterScene { get; private set; }

        private void PlaceEncounters()
        {
            encounters.Clear();
            foreach (var hero in party) hero.StatueBonus = default;
            EncounterScene = null;
            if (sandbox && !sandboxFeatures) return;
            var candidates = Map.FindPath(Map.Entrance, Map.BossTile)
                .Where(t => Map.WalkDistance(t) > EntranceClearance && !Map.IsBossRoom(t)
                    && !Map.Chests.Contains(t) && !Map.Springs.Contains(t)).ToList();
            var random = new Random(unchecked(Seed * 43 + Stage));
            foreach (var kind in new[] { EncounterKind.Merchant, EncounterKind.Statue })
            {
                if (candidates.Count == 0) break;
                var tile = candidates[random.Next(candidates.Count)];
                encounters.Add(new MapEncounter(kind, tile));
                candidates.RemoveAll(t => Math.Abs(t.X - tile.X) + Math.Abs(t.Y - tile.Y) <= 2);
            }
        }

        internal void VisitEncounter(TilePoint tile)
        {
            foreach (var encounter in encounters)
            {
                if (encounter.Visited || Math.Abs(tile.X - encounter.Tile.X) + Math.Abs(tile.Y - encounter.Tile.Y) > 1)
                    continue;
                DiscussEncounter(encounter);
                return;
            }
        }

        private void DiscussEncounter(MapEncounter encounter)
        {
            bool merchant = encounter.Kind == EncounterKind.Merchant;
            var stock = new Random(unchecked(Seed * 53 + Stage * 101));
            bool tonic = stock.Next(2) == 0;
            bool spoiled = stock.Next(4) == 0;
            string product = tonic ? "강장차" : "여행빵";
            var scene = new List<DialogueBeat> { DialogueBeat.Narration(merchant
                ? $"상인 “여행자 양반들, 금화 한 닢에 {product}, 어떠신가?” (소지금 {Coins}닢)"
                : "금이 간 조각상 안에서 동전 소리가 난다. 받침돌에는 ‘봉인을 깨면 축복이나 저주가 따르리라’고 새겨져 있다.") };
            var random = new Random(unchecked(Seed * 47 + Stage * 2 + (int)encounter.Kind));
            int votes = 0;
            foreach (var role in MarchOrder)
            {
                var hero = party[(int)role];
                if (hero.Hp <= 0) continue;
                bool yes;
                string line;
                if (merchant)
                {
                    int threshold = hero.Voice == Voice.Careful ? 12 : 24;
                    bool needSupplies = FatigueSteps >= threshold + random.Next(13) ||
                        (tonic && party.Any(h => h.Hp > 0 && h.MaxHp - h.Hp >= 8));
                    yes = Coins > 0 && needSupplies;
                    line = Coins == 0 ? "주머니가 비었어. 다음에 사자." : yes
                        ? $"여기서 {product} 좀 나눠 먹고 가자." : "아직 걸을 만해. 금화는 아껴 두자.";
                }
                else
                {
                    yes = hero.Voice == Voice.Bold || hero.Voice == Voice.Curious ||
                        (hero.Voice != Voice.Careful && random.Next(2) == 0);
                    line = yes ? "속이 빈 것 같아. 깨서 안을 살펴보자." : "누가 세운 건지도 모르잖아. 그대로 두자.";
                }
                votes += yes ? 1 : -1;
                scene.Add(DialogueBeat.Line(role, line, yes ? "찬성" : "반대"));
            }
            encounter.Visited = true;
            encounter.Accepted = votes > 0;
            string result;
            if (merchant && encounter.Accepted)
            {
                Coins--;
                result = $"{UseMerchandise(tonic, spoiled)} · 남은 금화 {Coins}닢";
            }
            else if (encounter.Accepted)
            {
                Coins++;
                var statueDice = new Random(unchecked(Seed * 59 + Stage * 107));
                bool blessed = statueDice.Next(2) == 0;
                int sign = blessed ? 1 : -1;
                StatBonus bonus;
                string change;
                switch (statueDice.Next(3))
                {
                    case 0:
                        bonus = new StatBonus(attack: sign * 2);
                        change = $"공격력 {bonus.Attack:+0;-0}";
                        break;
                    case 1:
                        bonus = new StatBonus(armor: sign * 2);
                        change = $"방어력 {bonus.Armor:+0;-0}";
                        break;
                    default:
                        bonus = new StatBonus(speedPercent: sign * 20);
                        change = $"전투 이동속도 {bonus.SpeedPercent:+0;-0}%";
                        break;
                }
                foreach (var hero in party)
                {
                    hero.StatueBonus = bonus;
                    if (bonus.SpeedPercent != 0) hero.MoveCarry = 0;
                }
                string effect = blessed ? "조각상의 축복" : "조각상의 저주";
                result = $"조각상에서 금화 한 닢을 얻었다. 소지금 {Coins}닢. {effect}로 이번 스테이지가 끝날 때까지 파티 {change}.";
            }
            else result = merchant ? "파티는 물건을 사지 않고 상인에게 작별을 고했다." : "파티는 조각상을 그대로 두고 길을 나섰다.";
            scene.Add(DialogueBeat.Narration(result));
            foreach (var beat in scene)
                log.Add(beat.Speaker.HasValue ? $"{party[(int)beat.Speaker.Value].Name} • {beat.Text}" : beat.Text);
            EncounterScene = scene.AsReadOnly();
        }
    }
}
