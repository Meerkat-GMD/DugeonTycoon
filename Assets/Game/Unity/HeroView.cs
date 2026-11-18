using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    [DisallowMultipleComponent]
    public sealed class HeroView : MonoBehaviour
    {
        [SerializeField] private HeroRole role = HeroRole.Tank;

        public HeroRole Role => role;

        internal void SetRole(HeroRole value) => role = value;
    }
}
