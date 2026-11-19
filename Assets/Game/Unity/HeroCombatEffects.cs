using System;
using System.Collections.Generic;
using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    internal sealed class HeroCombatEffects
    {
        private const float StateEntryTimeout = .75f;
        private const float CueTimeout = 2f;
        private readonly ActionEffect[] actions = new ActionEffect[4];
        private readonly Transform parent;

        private sealed class ActionEffect
        {
            public Animator Animator;
            public Transform Body, Source, Tip;
            public Renderer BakedArrow;
            public CombatVfx Prefab;
            public int State;
            public float Cue;
            public float RequestedAt, PreviousProgress;
            public int RequestedFrame;
            public bool WaitForRestart, Entered, ArrowHidden;
            public readonly List<Transform> Targets = new List<Transform>(4);
            public readonly List<CombatVfx> Instances = new List<CombatVfx>(4);
        }

        public HeroCombatEffects(Transform parent) => this.parent = parent;

        public void Bind(HeroRole role, Transform body, Transform model, Animator animator)
        {
            var (prefab, state, cue, anchor) = role switch
            {
                HeroRole.MeleeDamage => ("BearSlash", "Bear_Sword_Slash", 13f / 36f, "SwordGrip"),
                HeroRole.RangedDamage => ("CrowArrow", "Crow_Bow_Shot", 27f / 46f, "arrow_shot"),
                HeroRole.Healer => ("RabbitHeal", "Rabbit_Heal", .22f, "Rabbit_Staff"),
                _ => (null, null, 0f, null)
            };
            if (prefab == null) return;

            var action = new ActionEffect
            {
                Animator = animator,
                Body = body,
                Prefab = Resources.Load<GameObject>("HeroEffects/" + prefab).GetComponent<CombatVfx>(),
                State = Animator.StringToHash("Base Layer." + state),
                Cue = cue
            };
            var parts = model.GetComponentsInChildren<Transform>(true);
            action.Source = Array.Find(parts, part => part.name == anchor);
            if (role == HeroRole.MeleeDamage)
                action.Tip = Array.Find(parts, part => part.name == "SwordTip");
            if (role == HeroRole.RangedDamage)
                action.BakedArrow = Array.Find(parts, part => part.name == "SM_Archer_Arrow_Shot")?.GetComponent<Renderer>();
            if (role == HeroRole.Healer && action.Source)
            {
                var staffLight = new GameObject("지팡이 광원").transform;
                staffLight.SetParent(action.Source, false);
                staffLight.localPosition = Vector3.forward * .285f;
                action.Source = staffLight;
            }
            actions[(int)role] = action;
        }

        public void Queue(int source, Transform target)
        {
            var action = actions[source];
            if (action == null || !target) return;

            if (action.Targets.Count == 0 || action.RequestedFrame != Time.frameCount)
            {
                // 연타 중 애니메이션이 이전 타격을 건너뛰어도 이미 판정한 효과는 표시합니다.
                if (action.Targets.Count > 0) Release(action, false);
                action.RequestedAt = Time.time;
                action.RequestedFrame = Time.frameCount;
                action.Entered = false;
                action.WaitForRestart = TryGetProgress(action, out action.PreviousProgress);
            }
            action.Targets.Add(target);
        }

        public void Update(float attackSpeed)
        {
            foreach (var action in actions)
            {
                if (action == null) continue;
                bool inState = TryGetProgress(action, out float progress);
                action.Animator.speed = inState ? attackSpeed : 1f;
                if (action.ArrowHidden && (!inState || progress < action.Cue))
                    RestoreArrow(action);
                if (action.Targets.Count == 0) continue;

                action.WaitForRestart &= inState && progress >= action.PreviousProgress;
                action.PreviousProgress = progress;
                if (inState && !action.WaitForRestart)
                {
                    action.Entered = true;
                    if (progress >= action.Cue)
                    {
                        Release(action, true);
                        continue;
                    }
                }
                float timeout = action.Entered ? CueTimeout : StateEntryTimeout;
                if ((action.Entered && !inState) || Time.time - action.RequestedAt >= timeout)
                    Release(action, false);
            }
        }

        public void Clear()
        {
            foreach (var action in actions)
            {
                if (action == null) continue;
                action.Targets.Clear();
                action.Animator.speed = 1f;
                RestoreArrow(action);
                foreach (var instance in action.Instances) instance.Stop();
            }
        }

        private static bool TryGetProgress(ActionEffect action, out float progress)
        {
            var animator = action.Animator;
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                if (next.fullPathHash == action.State)
                {
                    progress = next.normalizedTime;
                    return true;
                }
            }
            var current = animator.GetCurrentAnimatorStateInfo(0);
            progress = current.normalizedTime;
            return current.fullPathHash == action.State;
        }

        private void Release(ActionEffect action, bool atCue)
        {
            if (atCue && action.BakedArrow && !action.BakedArrow.forceRenderingOff)
            {
                action.BakedArrow.forceRenderingOff = true;
                action.ArrowHidden = true;
            }
            var source = action.Source ? action.Source.position : action.Body.position + Vector3.up * .9f;
            foreach (var target in action.Targets)
            {
                if (!target) continue;
                var instance = action.Instances.Find(effect => !effect.IsPlaying);
                if (!instance)
                {
                    instance = UnityEngine.Object.Instantiate(action.Prefab, parent, false);
                    action.Instances.Add(instance);
                }
                var hit = target.position + Vector3.up * .86f;
                var ground = parent.TransformPoint(new Vector3(target.localPosition.x, 0, target.localPosition.z));
                instance.Play(source, hit, ground, action.Source, atCue ? action.Tip : null, .33f);
            }
            action.Targets.Clear();
        }

        private static void RestoreArrow(ActionEffect action)
        {
            if (!action.ArrowHidden) return;
            action.BakedArrow.forceRenderingOff = false;
            action.ArrowHidden = false;
        }
    }
}
