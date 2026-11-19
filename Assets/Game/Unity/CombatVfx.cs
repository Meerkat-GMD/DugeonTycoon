using System.Collections.Generic;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CombatVfx : MonoBehaviour
    {
        public enum EffectType { Slash, Arrow, Heal }

        [SerializeField] private EffectType effect;
        [SerializeField, Min(.1f)] private float impactScale = 1f;

        private static readonly Color Amber = new Color(1f, .44f, .12f);
        private static readonly Color Cyan = new Color(.23f, .77f, 1f);
        private static readonly Color Mint = new Color(.30f, 1f, .53f);
        private static readonly Color Core = new Color(1f, .96f, .80f);

        private readonly List<Vector3> vertices = new List<Vector3>(640);
        private readonly List<Color> colors = new List<Color>(640);
        private readonly List<int> indices = new List<int>(960);
        private Mesh mesh;
        private MeshRenderer surface;
        private readonly List<(Vector3 Edge, Vector3 Tip, float Age)> bladePath =
            new List<(Vector3, Vector3, float)>(32);
        private Transform movingOrigin;
        private Transform bladeTip;
        private Vector3 origin;
        private Vector3 target;
        private Vector3 ground;
        private Vector3 forward;
        private Vector3 right;
        private Vector3 flightOrigin;
        private float travel;
        private float charge;
        private float time;

        internal bool IsPlaying => surface.enabled;

        private void Awake()
        {
            surface = GetComponent<MeshRenderer>();
            surface.enabled = false;
            mesh = new Mesh { name = "전투 효과 메시" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (mesh) Destroy(mesh);
        }

        public void Play(Vector3 source, Vector3 hitPoint, Vector3 targetGround,
            Transform sourceAnchor = null, Transform slashTip = null, float healChargeSeconds = .3f)
        {
            movingOrigin = sourceAnchor;
            bladeTip = slashTip;
            bladePath.Clear();
            origin = source;
            flightOrigin = source;
            target = hitPoint;
            ground = targetGround;
            forward = Vector3.ProjectOnPlane(target - source, Vector3.up).normalized;
            if (forward == Vector3.zero) forward = transform.forward;
            right = Vector3.Cross(Vector3.up, forward);
            travel = Mathf.Clamp(Vector3.Distance(source, target) / 14f, .10f, .25f);
            charge = Mathf.Max(.01f, healChargeSeconds);
            time = 0f;
            surface.enabled = true;
            Rebuild();
        }

        public void Stop()
        {
            if (surface) surface.enabled = false;
        }

        private void LateUpdate()
        {
            if (!surface.enabled) return;
            time += Time.deltaTime;
            Rebuild();
        }

        private void Rebuild()
        {
            vertices.Clear();
            colors.Clear();
            indices.Clear();

            switch (effect)
            {
                case EffectType.Slash:
                    DrawSlash();
                    break;
                case EffectType.Arrow:
                    DrawArrow();
                    break;
                case EffectType.Heal:
                    DrawHeal();
                    break;
            }

            mesh.Clear();
            if (vertices.Count == 0)
            {
                Stop();
                return;
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
        }

        private void DrawSlash()
        {
            const float impact = .13f;
            const float arcLength = .20f;
            if (movingOrigin && bladeTip)
                DrawBladePath(arcLength);
            else if (time < impact + arcLength)
            {
                const int segments = 22;
                float progress = 1f - Mathf.Pow(1f - Mathf.Clamp01(time / .095f), 3f);
                float opacity = Falloff(time - impact, arcLength);
                float radius = Mathf.Clamp(Vector3.Distance(origin, target) * .82f, .63f, 1.07f);
                Vector3 center = origin + forward * .16f;
                Vector3 axis = (right + Vector3.up * .40f).normalized;

                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments;
                    float b = (i + 1) / (float)segments;
                    Vector3 outsideA = ArcPoint(center, axis, radius, progress, a);
                    Vector3 outsideB = ArcPoint(center, axis, radius, progress, b);
                    Vector3 insideA = ArcPoint(center, axis, radius - ArcWidth(a), progress, a);
                    Vector3 insideB = ArcPoint(center, axis, radius - ArcWidth(b), progress, b);
                    Quad(insideA, insideB, outsideB, outsideA,
                        WithAlpha(Amber, .07f * opacity), WithAlpha(Amber, .07f * opacity),
                        WithAlpha(Amber, .83f * opacity), WithAlpha(Amber, .83f * opacity));
                    Ribbon(outsideA, outsideB, .016f, WithAlpha(Core, opacity),
                        WithAlpha(Core, opacity));
                }
            }

            if (time >= impact)
                Burst(target, Amber, time - impact, .27f, 12);
        }

        private void DrawBladePath(float lifetime)
        {
            if (time <= .215f && (bladePath.Count == 0 || time - bladePath[^1].Age >= .012f))
            {
                Vector3 tip = bladeTip.position;
                Vector3 edge = Vector3.Lerp(tip, movingOrigin.position, .32f);
                bladePath.Add((edge, tip, time));
            }

            for (int i = 1; i < bladePath.Count; i++)
            {
                var a = bladePath[i - 1];
                var b = bladePath[i];
                float fadeA = Falloff(time - a.Age, lifetime);
                float fadeB = Falloff(time - b.Age, lifetime);
                if (fadeB <= 0f) continue;
                Quad(a.Edge, b.Edge, b.Tip, a.Tip, WithAlpha(Amber, 0f), WithAlpha(Amber, 0f),
                    WithAlpha(Amber, fadeB * .83f), WithAlpha(Amber, fadeA * .83f));
                Ribbon(a.Tip, b.Tip, .018f, WithAlpha(Core, fadeA), WithAlpha(Core, fadeB));
            }

            if (bladePath.Count == 1 && time <= .215f)
                Glint(bladeTip.position, .045f, Amber, .7f);
        }

        private Vector3 ArcPoint(Vector3 center, Vector3 axis, float radius, float sweep, float along)
        {
            float angle = Mathf.Lerp(-1.65f, -.92f, sweep) + along * Mathf.Lerp(.35f, 2.12f, sweep);
            return center + radius * (forward * Mathf.Cos(angle) + axis * Mathf.Sin(angle));
        }

        private static float ArcWidth(float along) => Mathf.Sin(along * Mathf.PI) * .24f;

        private void DrawArrow()
        {
            if (time < travel)
            {
                const int segments = 12;
                float head = time / travel;
                float tail = Mathf.Max(0f, head - .42f);
                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments;
                    float b = (i + 1) / (float)segments;
                    Ribbon(Projectile(Mathf.Lerp(tail, head, a)), Projectile(Mathf.Lerp(tail, head, b)),
                        Mathf.Lerp(.008f, .040f, b), WithAlpha(Cyan, a * a * .64f), WithAlpha(Cyan, b * b * .88f));
                }

                Vector3 arrow = Projectile(head);
                Vector3 velocity = (Projectile(head + .025f) - arrow).normalized;
                Ribbon(arrow - velocity * .23f, arrow + velocity * .035f, .018f, Cyan, Core);
                Vector3 headSide = Vector3.Cross(velocity, Vector3.up).normalized * .037f;
                Vector3 headBase = arrow - velocity * .05f;
                Triangle(arrow + velocity * .065f, headBase + headSide, headBase - headSide, Core, Cyan, Cyan);
                Glint(arrow, .125f, Cyan, .82f);
                if (time < .07f) Glint(origin, .13f, Cyan, Falloff(time, .07f));
                return;
            }

            Burst(target, Cyan, time - travel, .24f, 9);
        }

        private Vector3 Projectile(float progress) =>
            Vector3.LerpUnclamped(origin, target, progress) + Vector3.up * (.20f * Mathf.Sin(Mathf.PI * progress));

        private void DrawHeal()
        {
            const float flight = .09f;
            const float decay = .42f;
            if (time < charge)
            {
                float progress = time / charge;
                if (movingOrigin) flightOrigin = movingOrigin.position;
                Glint(flightOrigin, Mathf.Lerp(.045f, .14f, progress), Mint, Mathf.Lerp(.3f, .94f, progress));
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * Mathf.PI * .4f + progress * 4f;
                    Vector3 offset = right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
                    Glint(flightOrigin + offset * Mathf.Lerp(.20f, .07f, progress), .027f, Mint, .7f);
                }
                return;
            }

            if (time < charge + flight)
            {
                float progress = (time - charge) / flight;
                Vector3 bead = Vector3.Lerp(flightOrigin, target, progress) + Vector3.up * (.15f * Mathf.Sin(progress * Mathf.PI));
                Glint(bead, .15f, Mint, 1f);
                return;
            }

            float age = time - charge - flight;
            if (age >= decay) return;
            float progressOut = age / decay;
            float opacity = Mathf.Pow(1f - progressOut, 1.3f);
            float radius = impactScale * Mathf.Lerp(.35f, .66f, 1f - Mathf.Pow(1f - progressOut, 3f));
            GroundRing(ground + Vector3.up * .025f, radius, .034f, WithAlpha(Mint, opacity));
            Glint(target, .25f, Mint, opacity * .72f);

            for (int i = 0; i < 10; i++)
            {
                float angle = i * 2.4f + progressOut;
                Vector3 offset = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                Vector3 point = ground + offset * (.22f + (i % 4) * .046f) * impactScale
                    + Vector3.up * (.15f + progressOut * (.78f + (i % 3) * .22f));
                Glint(point, i % 3 == 0 ? .07f : .045f, Mint, opacity);
            }
        }

        private void Burst(Vector3 point, Color tint, float age, float duration, int count)
        {
            if (age >= duration) return;
            float progress = age / duration;
            float opacity = Mathf.Pow(1f - progress, 1.4f);
            if (age < .105f) Glint(point, .23f * impactScale, tint, Falloff(age, .105f));

            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.4f;
                Vector3 direction = (forward * .62f + right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
                float distance = (.30f + (i % 5) * .105f) * (1f - Mathf.Pow(1f - progress, 3f)) * impactScale;
                Vector3 end = point + direction * distance - Vector3.up * (.20f * progress * progress);
                Ribbon(end - direction * (.06f + i % 3 * .021f), end, .018f,
                    WithAlpha(tint, opacity * .25f), WithAlpha(i % 3 == 0 ? Core : tint, opacity));
            }
        }

        private void GroundRing(Vector3 center, float radius, float width, Color color)
        {
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = RingDirection(i * Mathf.PI * 2f / segments);
                Vector3 b = RingDirection((i + 1) * Mathf.PI * 2f / segments);
                Vector3 insideA = center + a * (radius - width);
                Vector3 insideB = center + b * (radius - width);
                Vector3 outsideA = center + a * radius;
                Vector3 outsideB = center + b * radius;
                Quad(insideA, insideB, outsideB, outsideA, color, color, color, color);
            }
        }

        private static Vector3 RingDirection(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

        private void Glint(Vector3 center, float size, Color tint, float opacity)
        {
            Vector3 x = right * size;
            Vector3 y = Vector3.up * size;
            Color inner = WithAlpha(Color.Lerp(tint, Core, .57f), opacity);
            Color outer = WithAlpha(tint, 0f);
            Triangle(center, center + x, center + y, inner, outer, outer);
            Triangle(center, center + y, center - x, inner, outer, outer);
            Triangle(center, center - x, center - y, inner, outer, outer);
            Triangle(center, center - y, center + x, inner, outer, outer);
        }

        private void Ribbon(Vector3 start, Vector3 end, float halfWidth, Color startColor, Color endColor)
        {
            Vector3 normal = Vector3.Cross(end - start, Vector3.up + forward * .60f).normalized;
            if (normal == Vector3.zero) normal = right;
            normal *= halfWidth;
            Quad(start - normal, start + normal, end + normal, end - normal, startColor, startColor, endColor, endColor);
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
        {
            int index = vertices.Count;
            AddVertex(a, ca);
            AddVertex(b, cb);
            AddVertex(c, cc);
            indices.Add(index);
            indices.Add(index + 1);
            indices.Add(index + 2);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
        {
            int index = vertices.Count;
            AddVertex(a, ca);
            AddVertex(b, cb);
            AddVertex(c, cc);
            AddVertex(d, cd);
            indices.Add(index);
            indices.Add(index + 1);
            indices.Add(index + 2);
            indices.Add(index);
            indices.Add(index + 2);
            indices.Add(index + 3);
        }

        private void AddVertex(Vector3 vertex, Color color)
        {
            vertices.Add(transform.InverseTransformPoint(vertex));
            colors.Add(color);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static float Falloff(float age, float duration) => 1f - Mathf.Clamp01(age / duration);
    }
}
