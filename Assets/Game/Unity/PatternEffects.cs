using System;
using System.Collections.Generic;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DungeonTycoon.Unity
{
    /// <summary>
    /// The visual side of telegraphed monster attacks, on top of the warning tiles the board paints: a charge ring
    /// under the caster while it winds up, then one effect per pattern. Slam: the caster rears up, then a shockwave
    /// and flying debris. Soul bolt: a ghost orb gathers, then streaks down the line. Tracking beam: a thin aiming
    /// line that turns red when it locks, then a thick blast that fades. Meteor: a burning rock falls onto its shadow,
    /// then a flash, a wide shockwave and debris. The rules never read any of this.
    /// </summary>
    internal sealed class PatternEffects
    {
        private const float SlamSeconds = .5f, BoltFlightSeconds = .22f, BoltSeconds = .5f;
        private const float BeamSeconds = .5f, MeteorSeconds = .65f;
        /// <summary>The meteor shows in the sky once this share of its warning has passed.</summary>
        private const float MeteorAppears = .3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color SlamColor = new Color(1f, .58f, .24f);
        private static readonly Color BoltColor = new Color(.55f, 1f, .9f);
        private static readonly Color AimColor = new Color(1f, .64f, .25f);
        private static readonly Color LockColor = new Color(1f, .2f, .16f);
        private static readonly Color BlastCore = new Color(1f, .97f, .82f);
        private static readonly Color FireColor = new Color(1f, .45f, .15f);

        private sealed class Fx
        {
            public PatternCast Cast;
            public float Progress, FiredAt = -1;
            public Vector3 Origin, End, Target;
            public readonly List<Transform> Parts = new List<Transform>();
            public Transform Charge, Line, Orb, Trail, Rock, Shadow, Wave, Flash;
            public Vector3[] Debris;
        }

        private readonly Transform root;
        private readonly float tile;
        private readonly Func<TilePoint, Vector3> centre;
        private readonly Func<int, Transform> foeBody;
        private readonly Action<int, Vector3, float> poseFoe;
        private readonly Material glass;
        private readonly Material[] palette;
        private readonly Mesh cube, ring, disk;
        private readonly Transform orbStock, rockStock;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly Dictionary<PatternCast, Fx> live = new Dictionary<PatternCast, Fx>();
        private readonly List<Fx> landed = new List<Fx>();
        private readonly List<Mesh> owned = new List<Mesh>();

        internal PatternEffects(Transform parent, float tile, Material[] palette, Func<TilePoint, Vector3> centre,
            Func<int, Transform> foeBody, Action<int, Vector3, float> poseFoe)
        {
            root = new GameObject("예고 공격 효과").transform;
            root.SetParent(parent, false);
            this.tile = tile;
            this.palette = palette;
            this.centre = centre;
            this.foeBody = foeBody;
            this.poseFoe = poseFoe;
            glass = MakeGlass();
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            ring = Owned(Annulus(.78f, 1f, 48));
            disk = Owned(Annulus(0f, 1f, 48));

            var stock = new GameObject("효과 원형").transform;
            stock.SetParent(root, false);
            stock.gameObject.SetActive(false);
            var orb = new DungeonMesh();
            DungeonFigures.Jewel(orb, 0, 0, 0, .5f, 1f, Finish.Mint);
            orbStock = Attach("영혼 구슬", orb, stock);
            var rock = new DungeonMesh();
            rock.Ring(0, 0, 0, .55f, .35f, .5f, Finish.Basalt, 7);
            rock.Ring(0, -.32f, 0, .2f, .55f, .26f, Finish.Basalt, 7);
            rock.Ring(.05f, .3f, 0, .38f, .12f, .24f, Finish.Flame, 7);
            rock.Box(.3f, .05f, .2f, .22f, .18f, .2f, Finish.Ember, 20, 35, 10);
            rockStock = Attach("메테오 바위", rock, stock);
        }

        // ── Following the rules ──────────────────────────────────────────

        /// <summary>Starts effects for new casts, follows their progress, sets off the landing ones and drops cancelled ones.</summary>
        internal void Sync(IReadOnlyList<PatternCast> casts, int ticks)
        {
            var current = new HashSet<PatternCast>();
            if (casts != null)
            {
                foreach (var cast in casts)
                {
                    current.Add(cast);
                    if (!live.TryGetValue(cast, out var fx))
                    {
                        if (cast.Phase == CastPhase.Fired) continue;
                        fx = Begin(cast);
                        live.Add(cast, fx);
                    }
                    fx.Progress = Mathf.Clamp01((float)(ticks - cast.StartTick) / Mathf.Max(1, cast.FireTick - cast.StartTick));
                    Aim(fx);
                    if (cast.Phase == CastPhase.Fired && fx.FiredAt < 0) Land(fx);
                }
            }
            var gone = new List<PatternCast>();
            foreach (var pair in live)
                if (!current.Contains(pair.Key) || pair.Value.FiredAt >= 0) gone.Add(pair.Key);
            foreach (var cast in gone)
            {
                var fx = live[cast];
                live.Remove(cast);
                // A landed effect plays out on its own; one whose caster fell mid-warning just vanishes.
                if (fx.FiredAt >= 0) landed.Add(fx);
                else Clear(fx);
            }
        }

        internal void Animate()
        {
            float now = Time.time;
            foreach (var fx in live.Values) Windup(fx, now);
            for (int i = landed.Count - 1; i >= 0; i--)
            {
                var fx = landed[i];
                if (Aftermath(fx, now - fx.FiredAt)) continue;
                Clear(fx);
                landed.RemoveAt(i);
            }
        }

        internal void Dispose()
        {
            foreach (var fx in live.Values) Clear(fx);
            foreach (var fx in landed) Clear(fx);
            live.Clear();
            landed.Clear();
            foreach (var mesh in owned) if (mesh) Object.Destroy(mesh);
            if (glass) Object.Destroy(glass);
            if (root) Object.Destroy(root.gameObject);
        }

        private Fx Begin(PatternCast cast)
        {
            var fx = new Fx { Cast = cast };
            fx.Charge = Part(fx, "충전 고리", ring, glass);
            switch (cast.Pattern)
            {
                case MonsterPattern.SoulBolt:
                    fx.Orb = Clone(fx, orbStock);
                    break;
                case MonsterPattern.TrackingBeam:
                    fx.Line = Part(fx, "조준선", cube, glass);
                    fx.Orb = Clone(fx, orbStock);
                    break;
                case MonsterPattern.Meteor:
                    fx.Rock = Clone(fx, rockStock);
                    fx.Trail = Part(fx, "불꼬리", cube, glass);
                    fx.Shadow = Part(fx, "그림자", disk, glass);
                    break;
            }
            return fx;
        }

        /// <summary>Where the effect starts and ends: the caster, the far end of a line, the meteor's centre.</summary>
        private void Aim(Fx fx)
        {
            var cast = fx.Cast;
            var body = foeBody(cast.Caster.Id);
            var caster = body ? Flat(body.localPosition) : centre(cast.Caster.Tile);
            fx.Origin = caster;
            fx.Target = centre(cast.Aim);
            fx.End = fx.Target;
            if (cast.Pattern != MonsterPattern.SoulBolt && cast.Pattern != MonsterPattern.TrackingBeam) return;
            var direction = Flat(fx.Target - caster);
            if (direction.sqrMagnitude < .0001f) return;
            direction.Normalize();
            float reach = tile * .5f;
            foreach (var spot in cast.Area) reach = Mathf.Max(reach, Vector3.Dot(centre(spot) - caster, direction));
            fx.End = caster + direction * (reach + tile * .45f);
        }

        // ── Before it lands ──────────────────────────────────────────────

        private void Windup(Fx fx, float now)
        {
            float p = fx.Progress;
            var cast = fx.Cast;
            var colour = cast.Pattern == MonsterPattern.SoulBolt ? BoltColor
                : cast.Pattern == MonsterPattern.TrackingBeam ? (cast.Phase == CastPhase.Aiming ? AimColor : LockColor)
                : cast.Pattern == MonsterPattern.Meteor ? FireColor : SlamColor;
            float beat = Mathf.Sin(now * (10 + 14 * p));
            Place(fx.Charge, fx.Origin + Vector3.up * .07f, tile * (.48f + .06f * beat + .12f * p));
            Tint(fx.Charge, colour, .3f + .45f * p);

            var direction = Flat(fx.End - fx.Origin).normalized;
            switch (cast.Pattern)
            {
                case MonsterPattern.Slam:
                    // The caster rears up and swells before it brings its weight down.
                    poseFoe(cast.Caster.Id, Vector3.up * (.38f * p), 1 + .14f * p);
                    break;
                case MonsterPattern.SoulBolt:
                    poseFoe(cast.Caster.Id, Vector3.up * (.08f * beat), 1);
                    Place(fx.Orb, fx.Origin + direction * (tile * .45f) + Vector3.up * .95f, .14f + .22f * p,
                        Quaternion.Euler(0, now * 360, 0));
                    break;
                case MonsterPattern.TrackingBeam:
                {
                    var from = fx.Origin + direction * (tile * .35f) + Vector3.up * .95f;
                    var to = fx.End + Vector3.up * .45f;
                    bool locked = cast.Phase != CastPhase.Aiming;
                    float width = locked ? .13f + .03f * beat : .05f + .015f * beat;
                    Stretch(fx.Line, from, to, width, width * .6f);
                    Tint(fx.Line, locked ? LockColor : AimColor, locked ? .85f : .5f + .2f * Mathf.Abs(beat));
                    Place(fx.Orb, from, .12f + .2f * p, Quaternion.Euler(0, now * 540, 0));
                    poseFoe(cast.Caster.Id, Vector3.up * (.1f * p), 1 + .06f * p);
                    break;
                }
                case MonsterPattern.Meteor:
                {
                    float s = Mathf.Clamp01((p - MeteorAppears) / (1 - MeteorAppears));
                    bool shown = s > 0;
                    fx.Rock.gameObject.SetActive(shown);
                    fx.Trail.gameObject.SetActive(shown);
                    Place(fx.Shadow, fx.Target + Vector3.up * .05f, tile * (.35f + 1.05f * s));
                    Tint(fx.Shadow, Color.black, .5f * s);
                    poseFoe(cast.Caster.Id, Vector3.up * (.22f * p), 1 + .05f * p);
                    if (!shown) break;
                    // It falls steeply from the upper left, faster as it nears the ground.
                    float fall = 1 - s * s;
                    var slant = new Vector3(-.45f, 0, .3f);
                    var rock = fx.Target + slant * (fall * 4.5f) + Vector3.up * (.35f + fall * 9f);
                    var behind = rock + (slant + Vector3.up * 2f).normalized * (1.1f + 1.4f * fall);
                    Place(fx.Rock, rock, .9f, Quaternion.Euler(now * 210, now * 140, 0));
                    Stretch(fx.Trail, rock, behind, .5f, .5f);
                    Tint(fx.Trail, FireColor, .65f);
                    break;
                }
            }
        }

        // ── Landing ──────────────────────────────────────────────────────

        private void Land(Fx fx)
        {
            fx.FiredAt = Time.time;
            Hide(fx.Charge);
            Hide(fx.Line);
            Hide(fx.Shadow);
            var cast = fx.Cast;
            switch (cast.Pattern)
            {
                case MonsterPattern.Slam:
                    fx.Wave = Part(fx, "충격파", ring, glass);
                    fx.Flash = Part(fx, "먼지 빛", disk, glass);
                    Scatter(fx, fx.Origin, 8, 1.2f);
                    break;
                case MonsterPattern.SoulBolt:
                    fx.Trail = Part(fx, "영혼 꼬리", cube, glass);
                    fx.Wave = Part(fx, "영혼 파문", ring, glass);
                    break;
                case MonsterPattern.TrackingBeam:
                    Hide(fx.Orb);
                    fx.Line = Part(fx, "레이저 중심", cube, glass);
                    fx.Trail = Part(fx, "레이저 불빛", cube, glass);
                    fx.Flash = Part(fx, "발사 섬광", disk, glass);
                    break;
                case MonsterPattern.Meteor:
                    Hide(fx.Rock);
                    Hide(fx.Trail);
                    fx.Wave = Part(fx, "충격파", ring, glass);
                    fx.Flash = Part(fx, "폭발 섬광", disk, glass);
                    Scatter(fx, fx.Target, 11, 1.6f);
                    break;
            }
        }

        /// <summary>Plays the landing for <paramref name="age"/> seconds; false once it is over.</summary>
        private bool Aftermath(Fx fx, float age)
        {
            var cast = fx.Cast;
            var direction = Flat(fx.End - fx.Origin).normalized;
            switch (cast.Pattern)
            {
                case MonsterPattern.Slam:
                {
                    float t = age / SlamSeconds;
                    if (t >= 1) return false;
                    // The caster comes down hard, then springs back.
                    float squash = Mathf.Clamp01(1 - t * 4);
                    poseFoe(cast.Caster.Id, Vector3.down * (.12f * squash), 1 - .16f * squash);
                    Place(fx.Wave, fx.Origin + Vector3.up * .08f, tile * (.4f + 1.35f * EaseOut(t)));
                    Tint(fx.Wave, SlamColor, .9f * (1 - t));
                    Place(fx.Flash, fx.Origin + Vector3.up * .06f, tile * (.6f + .9f * t));
                    Tint(fx.Flash, SlamColor, .45f * (1 - t) * (1 - t));
                    Fly(fx, fx.Origin, t, 1.2f);
                    return true;
                }
                case MonsterPattern.SoulBolt:
                {
                    float t = age / BoltSeconds;
                    if (t >= 1) return false;
                    float flight = Mathf.Clamp01(age / BoltFlightSeconds);
                    var start = fx.Origin + direction * (tile * .45f) + Vector3.up * .95f;
                    var finish = fx.End + Vector3.up * .55f;
                    var orb = Vector3.Lerp(start, finish, flight);
                    if (flight < 1)
                    {
                        Place(fx.Orb, orb, .3f, Quaternion.Euler(0, age * 900, 0));
                        var tail = Vector3.Lerp(start, orb, Mathf.Clamp01(1 - 1.8f * tile / Mathf.Max(.01f, (orb - start).magnitude)));
                        Stretch(fx.Trail, tail, orb, .22f, .16f);
                        Tint(fx.Trail, BoltColor, .6f);
                        return true;
                    }
                    Hide(fx.Orb);
                    float fade = (age - BoltFlightSeconds) / (BoltSeconds - BoltFlightSeconds);
                    Stretch(fx.Trail, Vector3.Lerp(start, finish, .55f + .45f * fade), finish, .22f * (1 - fade), .16f);
                    Tint(fx.Trail, BoltColor, .5f * (1 - fade));
                    Place(fx.Wave, fx.End + Vector3.up * .08f, tile * (.2f + .7f * EaseOut(fade)));
                    Tint(fx.Wave, BoltColor, .85f * (1 - fade));
                    return true;
                }
                case MonsterPattern.TrackingBeam:
                {
                    float t = age / BeamSeconds;
                    if (t >= 1) return false;
                    var from = fx.Origin + direction * (tile * .35f) + Vector3.up * .95f;
                    var to = fx.End + Vector3.up * .45f;
                    float core = Mathf.Sqrt(1 - t);
                    Stretch(fx.Line, from, to, .55f * core, .4f * core);
                    Tint(fx.Line, BlastCore, 1 - t * t);
                    Stretch(fx.Trail, from, to, 1.3f * (1 - .5f * t), .8f * (1 - .5f * t));
                    Tint(fx.Trail, LockColor, .5f * (1 - t));
                    // The blast also scorches the 3×3 around the caster, so the flash spreads that far.
                    Place(fx.Flash, fx.Origin + Vector3.up * .07f, tile * (1.3f + .4f * t));
                    Tint(fx.Flash, BlastCore, .7f * (1 - t));
                    poseFoe(cast.Caster.Id, -direction * (.18f * (1 - t)), 1);
                    return true;
                }
                case MonsterPattern.Meteor:
                {
                    float t = age / MeteorSeconds;
                    if (t >= 1) return false;
                    Place(fx.Wave, fx.Target + Vector3.up * .08f, tile * (.5f + 2.1f * EaseOut(t)));
                    Tint(fx.Wave, FireColor, .95f * (1 - t));
                    float flash = Mathf.Clamp01(1 - t * 3);
                    Place(fx.Flash, fx.Target + Vector3.up * .1f, tile * (1.2f + .6f * t));
                    Tint(fx.Flash, BlastCore, .9f * flash);
                    Fly(fx, fx.Target, t, 1.6f);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Gives the landing a handful of debris chunks, each with its own throw.</summary>
        private void Scatter(Fx fx, Vector3 at, int count, float strength)
        {
            fx.Debris = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = (i + .37f * (i % 3)) * Mathf.PI * 2 / count;
                fx.Debris[i] = new Vector3(Mathf.Cos(angle), 1.4f + .5f * (i % 3), Mathf.Sin(angle)) * strength;
                var chunk = Part(fx, "파편", cube, palette[(int)(i % 3 == 0 ? Finish.Ember : Finish.Basalt)]);
                chunk.localPosition = at;
            }
        }

        private void Fly(Fx fx, Vector3 at, float t, float strength)
        {
            if (fx.Debris == null) return;
            int first = fx.Parts.Count - fx.Debris.Length;
            for (int i = 0; i < fx.Debris.Length; i++)
            {
                var throwBy = fx.Debris[i];
                var flat = new Vector3(throwBy.x, 0, throwBy.z) * (t * 1.1f);
                float height = Mathf.Max(0, throwBy.y * t * 1.2f - 4.5f * t * t) + .1f;
                Place(fx.Parts[first + i], at + flat + Vector3.up * height, .16f * strength * (1 - .6f * t),
                    Quaternion.Euler(t * 500 + i * 40, t * 300 + i * 70, 0));
            }
        }

        // ── Pieces ───────────────────────────────────────────────────────

        private Transform Part(Fx fx, string name, Mesh mesh, Material material)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(root, false);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            fx.Parts.Add(piece.transform);
            return piece.transform;
        }

        private Transform Clone(Fx fx, Transform stock)
        {
            var piece = Object.Instantiate(stock, root, false);
            piece.gameObject.SetActive(true);
            foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            fx.Parts.Add(piece);
            return piece;
        }

        private Transform Attach(string name, DungeonMesh geometry, Transform parent)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            owned.Add(geometry.Attach(piece, palette));
            return piece.transform;
        }

        private static void Clear(Fx fx)
        {
            foreach (var part in fx.Parts) if (part) Object.Destroy(part.gameObject);
            fx.Parts.Clear();
        }

        private static void Hide(Transform part)
        {
            if (part) part.gameObject.SetActive(false);
        }

        private static void Place(Transform part, Vector3 at, float size, Quaternion? turn = null)
        {
            if (!part) return;
            if (!part.gameObject.activeSelf) part.gameObject.SetActive(true);
            part.localPosition = at;
            part.localRotation = turn ?? Quaternion.identity;
            part.localScale = Vector3.one * Mathf.Max(.0001f, size);
        }

        /// <summary>Lays a box from one point to the other, <paramref name="width"/> across and <paramref name="height"/> tall.</summary>
        private static void Stretch(Transform part, Vector3 from, Vector3 to, float width, float height)
        {
            if (!part) return;
            if (!part.gameObject.activeSelf) part.gameObject.SetActive(true);
            var along = to - from;
            float length = along.magnitude;
            part.localPosition = (from + to) * .5f;
            part.localRotation = length > .0001f ? Quaternion.LookRotation(along / length, Vector3.up) : Quaternion.identity;
            part.localScale = new Vector3(Mathf.Max(.0001f, width), Mathf.Max(.0001f, height), Mathf.Max(.0001f, length));
        }

        private void Tint(Transform part, Color colour, float alpha)
        {
            if (!part) return;
            colour.a = Mathf.Clamp01(alpha);
            block.SetColor(BaseColorId, colour);
            part.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v;
        }

        private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

        private Mesh Owned(Mesh mesh)
        {
            owned.Add(mesh);
            return mesh;
        }

        /// <summary>A flat ring on the floor plane, unit outer radius; an inner radius of 0 makes a disc.</summary>
        private static Mesh Annulus(float inner, float outer, int segments)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices.Add(dir * inner);
                vertices.Add(dir * outer);
            }
            for (int i = 0; i < segments; i++)
            {
                int k = i * 2;
                triangles.AddRange(new[] { k, k + 3, k + 1, k, k + 2, k + 3 });
            }
            var mesh = new Mesh { name = inner > 0 ? "효과 고리" : "효과 원판" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>An unlit see-through material, tinted per piece; drawn over the warning tiles.</summary>
        private static Material MakeGlass()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "Pattern effect" };
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_Cull", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent + 5;
            return mat;
        }
    }
}
