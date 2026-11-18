using System;
using System.Collections.Generic;
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
            "던전 TRPG 용사들을 연기한다. 살아 있는 용사마다 한 행동을 골라 JSON으로 답하라. " +
            "모든 용사는 Attack으로 현재 적을 공격할 수 있다. Tank는 Guard로 파티를 보호하고, " +
            "Healer는 Heal로 다친 생존자 한 명을 치료할 수도 있다. " +
            "actions 배열에 각자의 role, action, target, line을 넣어라. role과 target은 입력의 영문 역할 ID다. " +
            "Attack이나 Guard의 target에는 자기 ID를 넣어라. line은 24자 이내의 짧은 한국어 전투 대사다.";
        private const string TrialPrompt =
            "던전에서 낙석이 떨어진다. 살아 있는 용사 중 한 명을 골라 동료를 지킬 행동을 선언하라. " +
            "JSON 객체에 role(입력의 영문 역할 ID)과 line(40자 이내의 한국어 1인칭 대응 선언)을 넣어라. " +
            "주사위와 성공 여부는 게임이 판정한다.";

        private const string ForkPrompt =
            "던전 TRPG 용사들이 갈림길에서 어느 길로 갈지 논의한다. 각 용사는 자기 성격 태그와 능력치, 파티의 체력, " +
            "입력에 적힌 보이는 정보만 근거로 길 하나를 고른다. 입력에 없는 것은 용사들도 모른다. " +
            "opinions 배열에 살아 있는 용사마다 role(입력의 영문 역할 ID), option(입력의 길 번호 숫자 문자열), " +
            "line(30자 이내의 한국어 1인칭 주장)을 넣어 JSON으로 답하라. 최종 결정은 게임이 판정한다.";

        private readonly string apiKey;
        private readonly string model = "gpt-4.1-mini";
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

        public async UniTask<IReadOnlyList<HeroDecision>> DecideAsync(DungeonRun run, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Online || run.Enemy == null) return null;

            var plan = await RequestAsync<CombatPlan>(run, CombatPrompt, token);
            if (plan?.actions == null || plan.actions.Length > run.Party.Count) return null;

            var decisions = new List<HeroDecision>(plan.actions.Length);
            var assigned = new HashSet<HeroRole>();
            foreach (var move in plan.actions)
            {
                if (move == null || !TryRole(move.role, out var actor) || !IsAlive(run, actor) ||
                    !assigned.Add(actor) || !TryRole(move.target, out var target) ||
                    !Enum.TryParse<HeroAction>(move.action, out var action) ||
                    !Enum.IsDefined(typeof(HeroAction), action) || action.ToString() != move.action ||
                    string.IsNullOrWhiteSpace(move.line)) return null;

                decisions.Add(new HeroDecision(actor, action, target, CleanLine(move.line)));
            }

            foreach (var hero in run.Party)
                if (hero.Hp > 0 && !assigned.Contains(hero.Role)) return null;
            return decisions;
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
            var payload = new RequestBody { model = model, instructions = instructions, input = input ?? Describe(run) };
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
                if (response?.status != "completed" || response.output == null) return null;
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
            }
            return null;
        }

        private static string Describe(DungeonRun run)
        {
            var text = new StringBuilder();
            text.AppendLine($"스테이지 {run.Stage}, 파티 레벨 {run.Level}{(run.FightingBoss ? ", 보스전" : "")}");
            foreach (var hero in run.Party)
                text.AppendLine($"{hero.Role} ({hero.Name}) HP {hero.Hp}/{hero.MaxHp}");
            if (run.Enemy != null)
                text.AppendLine($"적 {run.Enemy.Name} HP {run.Enemy.Hp}/{run.Enemy.MaxHp}");
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

        [Serializable] private sealed class RequestBody
        {
            public string model;
            public string instructions;
            public string input;
            public bool store = false;
            public int max_output_tokens = 512;
            public TextOptions text = new TextOptions();
        }
        [Serializable] private sealed class TextOptions { public OutputFormat format = new OutputFormat(); }
        [Serializable] private sealed class OutputFormat { public string type = "json_object"; }

        // JsonUtility populates these fields from the wire response.
#pragma warning disable CS0649
        [Serializable] private sealed class ResponseBody { public string status; public OutputItem[] output; }
        [Serializable] private sealed class OutputItem { public string type; public OutputPart[] content; }
        [Serializable] private sealed class OutputPart { public string type; public string text; }
        [Serializable] private sealed class CombatPlan { public Move[] actions; }
        [Serializable] private sealed class Move { public string role, action, target, line; }
        [Serializable] private sealed class TrialResponse { public string role, line; }
        [Serializable] private sealed class ForkPlan { public Vote[] opinions; }
        // option stays a string so a missing field is not silently read as path 0.
        [Serializable] private sealed class Vote { public string role, option, line; }
#pragma warning restore CS0649
    }
}
