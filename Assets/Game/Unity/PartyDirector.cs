using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace DungeonTycoon.Unity
{
    public sealed class PartyDirector
    {
        private const string Endpoint = "https://api.openai.com/v1/responses";
        private const int TimeoutSeconds = 6;
        private const string CombatPrompt =
            "던전 TRPG 용사들을 연기한다. 실시간 전투의 상황이 바뀌었다. 살아 있는 용사마다 다음 상황 변화까지 따를 작전을 정해 JSON으로 답하라. " +
            "tactics 배열에 role(입력의 영문 역할 ID), focus(먼저 노릴 적의 번호 문자열, 없으면 \"-1\"), " +
            "protect(먼저 지킬 동료의 영문 역할 ID, 없으면 빈 문자열), line(24자 이내의 짧은 한국어 전투 대사)을 넣어라. " +
            "Tank는 주변 적을 도발해 공격을 받아내고, Healer는 protect 동료를 먼저 치료한다. 이동·공격·피해는 게임이 판정한다.";
        private const string TrialPrompt =
            "던전에서 낙석이 떨어진다. 살아 있는 용사 중 한 명을 골라 동료를 지킬 행동을 선언하라. " +
            "JSON 객체에 role(입력의 영문 역할 ID)과 line(40자 이내의 한국어 1인칭 대응 선언)을 넣어라. " +
            "주사위와 성공 여부는 게임이 판정한다.";

        private const string ForkPrompt =
            "던전 TRPG 용사들이 갈림길에서 어느 길로 갈지 논의한다. 각 용사는 자기 성격 태그와 능력치, 파티의 체력, " +
            "입력에 적힌 보이는 정보만 근거로 길 하나를 고른다. 입력에 없는 것은 용사들도 모른다. " +
            "opinions 배열에 살아 있는 용사마다 role(입력의 영문 역할 ID), option(입력의 길 번호 숫자 문자열), " +
            "line(30자 이내의 한국어 1인칭 주장)을 넣어 JSON으로 답하라. line에서는 길 번호를 말하지 말고 " +
            "'북쪽 길'처럼 입력에 적힌 길 이름으로 부르며, 용사마다 말투가 겹치지 않게 하라. 최종 결정은 게임이 판정한다.";

        private readonly string apiKey;
        private readonly string model = "gpt-5.4-mini";
        private DateTime retryAfter;

        public PartyDirector()
        {
#if UNITY_EDITOR
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
            var configuredModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
            if (!string.IsNullOrWhiteSpace(configuredModel))
                model = configuredModel.Trim();
#else
            apiKey = null;
#endif
        }

        private bool Online => !string.IsNullOrEmpty(apiKey) && DateTime.UtcNow >= retryAfter;
        public string ModeLabel => Online ? "OpenAI" : "로컬 전술 AI";

        /// <summary>
        /// Asks for each hero's plan for the current fight. Returns null offline or on failure; the rules then
        /// keep fighting with local tactics. Entries that do not parse are dropped here; the run re-checks the rest.
        /// </summary>
        public async UniTask<IReadOnlyList<HeroTactic>> TacticsAsync(DungeonRun run, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Online || run.Phase != RunPhase.Combat) return null;
            var plan = await RequestAsync<CombatPlan>(run, CombatPrompt, token, DescribeFight(run));
            if (plan?.tactics == null) return null;
            var tactics = new List<HeroTactic>();
            foreach (var entry in plan.tactics)
            {
                if (entry == null || !TryRole(entry.role, out var role) || !int.TryParse(entry.focus, out int focus) ||
                    string.IsNullOrWhiteSpace(entry.line)) continue;
                int protect = -1;
                if (!string.IsNullOrWhiteSpace(entry.protect))
                {
                    if (!TryRole(entry.protect, out var ward)) continue;
                    protect = (int)ward;
                }
                tactics.Add(new HeroTactic(role, focus, protect, CleanLine(entry.line)));
            }
            return tactics;
        }

        private static string DescribeFight(DungeonRun run)
        {
            var text = new StringBuilder();
            text.AppendLine($"스테이지 {run.Stage}, 파티 레벨 {run.Level}{(run.FightingBoss ? ", 보스전" : "")}");
            foreach (var monster in run.Engaged)
                text.AppendLine($"적 {monster.Id}: {monster.Name} HP {monster.Hp}/{monster.MaxHp}, 방어 {monster.Armor}, 공격 {monster.Attack}");
            foreach (var hero in run.Party)
            {
                if (hero.Hp == 0)
                {
                    text.AppendLine($"{hero.Role} ({hero.Name}) 쓰러짐");
                    continue;
                }
                var here = run.HeroTile(hero.Role);
                var nearest = run.Engaged.OrderBy(m => Math.Max(Math.Abs(m.Tile.X - here.X), Math.Abs(m.Tile.Y - here.Y))).FirstOrDefault();
                string near = nearest == null ? "" :
                    $", 가장 가까운 적 {nearest.Id}까지 {Math.Max(Math.Abs(nearest.Tile.X - here.X), Math.Abs(nearest.Tile.Y - here.Y))}칸";
                text.AppendLine($"{hero.Role} ({hero.Name}) HP {hero.Hp}/{hero.MaxHp}, 사거리 {hero.Range}{near}");
            }
            text.Append("각 용사의 작전을 JSON으로 작성해.");
            return text.ToString();
        }

        public async UniTask<(HeroRole Role, string Line)> RespondAsync(DungeonRun run, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Online)
            {
                var response = await RequestAsync<TrialResponse>(run, TrialPrompt, token);
                if (response != null && TryRole(response.role, out var role) && IsAlive(run, role) &&
                    !string.IsNullOrWhiteSpace(response.line)) return (role, CleanLine(response.line));
            }

            HeroRole responder = HeroRole.Tank;
            int mostHealth = 0;
            foreach (var hero in run.Party)
            {
                if (hero.Hp <= mostHealth) continue;
                mostHealth = hero.Hp;
                responder = hero.Role;
            }

            return responder switch
            {
                HeroRole.Tank => (responder, "방패를 내 머리 위로! 다들 내 뒤로 와!"),
                HeroRole.Healer => (responder, "보호막을 펼칠게요. 가까이 모여요!"),
                HeroRole.MeleeDamage => (responder, "앞길을 막는 돌은 내가 쳐낼게!"),
                _ => (responder, "오른쪽이 비었어. 내 발자국을 따라와!")
            };
        }

        /// <summary>
        /// Asks the AI for each hero's vote at the fork. Returns null offline or on failure; the run then uses
        /// local opinions. Invalid entries are dropped here and per hero again by <see cref="DungeonRun.ResolveFork"/>.
        /// </summary>
        public async UniTask<IReadOnlyList<ForkOpinion>> DiscussAsync(DungeonRun run, ForkDiscussion fork, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Online || fork == null) return null;
            var plan = await RequestAsync<ForkPlan>(run, ForkPrompt, token, DescribeFork(run, fork));
            if (plan?.opinions == null) return null;
            var opinions = new List<ForkOpinion>();
            var assigned = new HashSet<HeroRole>();
            foreach (var vote in plan.opinions)
            {
                if (vote == null || !TryRole(vote.role, out var role) || !IsAlive(run, role) || !assigned.Add(role) ||
                    !int.TryParse(vote.option, out int option) || option < 0 || option >= fork.Options.Count ||
                    string.IsNullOrWhiteSpace(vote.line)) continue;
                opinions.Add(new ForkOpinion(role, option, CleanLine(vote.line)));
            }
            return opinions;
        }

        private static string DescribeFork(DungeonRun run, ForkDiscussion fork)
        {
            var text = new StringBuilder();
            text.AppendLine(fork.Backtracking
                ? "막다른 길에 이르렀다. 지나쳐 온 길 중 어디로 되돌아갈지 정한다."
                : "갈림길에 섰다. 아직 가 보지 않은 길이 여러 개다.");
            text.AppendLine($"스테이지 {run.Stage}, 파티 레벨 {run.Level}. 보스방은 아직 보이지 않는다.");
            foreach (var option in fork.Options)
                text.AppendLine($"길 {option.Index}: {option.Label} — {option.Distance}걸음, 지금 보이는 적 {option.VisibleMonsters}, " +
                                $"입구에서 깊이 {option.Depth}, 트인 정도 {option.Openness}");
            foreach (var hero in run.Party)
            {
                if (hero.Hp == 0) continue;
                var tags = new List<string>();
                foreach (var tag in hero.Tags) tags.Add(HeroTraits.Name(tag));
                text.AppendLine($"{hero.Role} ({hero.Name}) HP {hero.Hp}/{hero.MaxHp}, 성격 {string.Join("·", tags)}, " +
                                $"힘 {hero.Modifier(Ability.Strength):+0;-0;0} 민첩 {hero.Modifier(Ability.Dexterity):+0;-0;0} " +
                                $"지혜 {hero.Modifier(Ability.Wisdom):+0;-0;0} 매력 {hero.Modifier(Ability.Charisma):+0;-0;0}");
            }
            text.Append("각 용사의 의견을 JSON으로 작성해.");
            return text.ToString();
        }

        private async UniTask<T> RequestAsync<T>(DungeonRun run, string instructions, CancellationToken token, string input = null)
            where T : class
        {
            // GPT-5.x models reason before answering; the heroes' short JSON lines need none of it, and skipping it
            // keeps answers fast and keeps hidden reasoning from eating the output budget. Older models reject the field.
            RequestBody payload = model.StartsWith("gpt-5.", StringComparison.Ordinal)
                ? new ReasoningRequestBody()
                : new RequestBody();
            payload.model = model;
            payload.instructions = instructions;
            payload.input = input ?? Describe(run);
            try
            {
                using var request = new UnityWebRequest(Endpoint, "POST")
                {
                    timeout = TimeoutSeconds,
                    redirectLimit = 0,
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                using var timer = deadline.CancelAfterSlim(TimeSpan.FromSeconds(TimeoutSeconds), DelayType.Realtime);
                await request.SendWebRequest().WithCancellation(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                var response = JsonUtility.FromJson<ResponseBody>(request.downloadHandler.text);
                if (response?.status != "completed" || response.output == null)
                {
                    string reason = response?.incomplete_details?.reason;
                    Debug.LogWarning($"OpenAI 응답이 완료되지 않았습니다 ({model}, 상태 {response?.status ?? "없음"}" +
                                     $"{(string.IsNullOrEmpty(reason) ? "" : ", 이유 " + reason)}). 이번 판단은 로컬 전술로 진행합니다.");
                    return null;
                }
                foreach (var item in response.output)
                {
                    if (item?.type != "message" || item.content == null) continue;
                    foreach (var part in item.content)
                    {
                        if (part?.type == "output_text" && !string.IsNullOrWhiteSpace(part.text))
                            return JsonUtility.FromJson<T>(part.text);
                    }
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException || exception is UnityWebRequestException ||
                                              exception is ArgumentException || exception is InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                retryAfter = DateTime.UtcNow.AddSeconds(30);
                // The message carries the HTTP status and OpenAI's error text (wrong key, unknown model, no credit…),
                // never the key itself.
                string why = exception is OperationCanceledException ? $"{TimeoutSeconds}초 안에 응답 없음" : exception.Message;
                Debug.LogWarning($"OpenAI 요청 실패 ({model}): {why}. 30초 동안 로컬 전술로 진행합니다.");
            }
            return null;
        }

        private static string Describe(DungeonRun run)
        {
            var text = new StringBuilder();
            text.AppendLine($"스테이지 {run.Stage}, 파티 레벨 {run.Level}{(run.FightingBoss ? ", 보스전" : "")}");
            foreach (var hero in run.Party)
                text.AppendLine($"{hero.Role} ({hero.Name}) HP {hero.Hp}/{hero.MaxHp}");
            foreach (var monster in run.Engaged)
                text.AppendLine($"적 {monster.Name} HP {monster.Hp}/{monster.MaxHp}");
            text.Append("행동 선언을 JSON으로 작성해.");
            return text.ToString();
        }

        private static bool TryRole(string id, out HeroRole role) =>
            Enum.TryParse(id, out role) && Enum.IsDefined(typeof(HeroRole), role) && role.ToString() == id;

        private static bool IsAlive(DungeonRun run, HeroRole role)
        {
            foreach (var hero in run.Party)
                if (hero.Role == role) return hero.Hp > 0;
            return false;
        }

        private static string CleanLine(string line)
        {
            var text = line.Trim().Replace('\r', ' ').Replace('\n', ' ').Replace('<', '〈').Replace('>', '〉');
            return text.Length > 60 ? text.Substring(0, 60) : text;
        }

        [Serializable] private class RequestBody
        {
            public string model;
            public string instructions;
            public string input;
            public bool store = false;
            // Answers are a few short lines; the headroom covers models that still spend tokens on reasoning.
            public int max_output_tokens = 1024;
            public TextOptions text = new TextOptions();
        }
        [Serializable] private sealed class ReasoningRequestBody : RequestBody { public Reasoning reasoning = new Reasoning(); }
        [Serializable] private sealed class Reasoning { public string effort = "none"; }
        [Serializable] private sealed class TextOptions { public OutputFormat format = new OutputFormat(); }
        [Serializable] private sealed class OutputFormat { public string type = "json_object"; }

        // JsonUtility populates these fields from the wire response.
#pragma warning disable CS0649
        [Serializable] private sealed class ResponseBody { public string status; public IncompleteDetails incomplete_details; public OutputItem[] output; }
        [Serializable] private sealed class IncompleteDetails { public string reason; }
        [Serializable] private sealed class OutputItem { public string type; public OutputPart[] content; }
        [Serializable] private sealed class OutputPart { public string type; public string text; }
        [Serializable] private sealed class CombatPlan { public Move[] tactics; }
        // Numbers stay strings so a missing field is not silently read as 0.
        [Serializable] private sealed class Move { public string role, focus, protect, line; }
        [Serializable] private sealed class TrialResponse { public string role, line; }
        [Serializable] private sealed class ForkPlan { public Vote[] opinions; }
        // option stays a string so a missing field is not silently read as path 0.
        [Serializable] private sealed class Vote { public string role, option, line; }
#pragma warning restore CS0649
    }
}
