using UnityEngine;

namespace DungeonTycoon.Unity
{
    // The two prop clips use the same 24 fps timeline as Astrologer_Meteor.
    internal sealed class AstrologerMeteor
    {
        internal const float Impact = 33f / 24f;
        internal const float Duration = 45f / 24f;
        private readonly Animator mark, stone;

        internal AstrologerMeteor(Transform parent, Vector3 target)
        {
            mark = Spawn("AstrologerTelegraph", parent, target);
            stone = Spawn("AstrologerTombstone", parent, target);
            Sample(0);
        }

        private static Animator Spawn(string name, Transform parent, Vector3 target)
        {
            var obj = Object.Instantiate(Resources.Load<GameObject>("EnemyEffects/" + name), parent, false);
            obj.transform.localPosition = target;
            var animator = obj.GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.speed = 0;
            return animator;
        }

        internal void Sample(float seconds)
        {
            Pose(mark, "Base Layer.MeteorTelegraph", seconds, 10f / 24f);
            Pose(stone, "Base Layer.TombstoneFall", seconds, 23f / 24f);
        }

        private static void Pose(Animator animator, string state, float seconds, float appears)
        {
            bool visible = seconds >= appears;
            if (animator.gameObject.activeSelf != visible) animator.gameObject.SetActive(visible);
            if (!visible) return;
            animator.Play(state, 0, Mathf.Clamp01(seconds / Duration));
            animator.Update(0);
        }

        internal void Clear()
        {
            Object.Destroy(mark.gameObject);
            Object.Destroy(stone.gameObject);
        }
    }
}
