using System.Collections.Generic;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    internal enum Finish
    {
        Ink, Basalt, Slate, Flagstone, Ash, Ivory, Brass, Gold, Wood, Leather,
        Steel, Teal, Mint, Leaf, Plum, Crimson, Ember, Flame, Skin
    }

    // Each prop is one flat-shaded mesh. Faces with the same finish share a submesh.
    internal sealed class DungeonMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly Dictionary<Finish, List<int>> faces = new Dictionary<Finish, List<int>>();

        internal void Box(float x, float y, float z, float w, float h, float d, Finish paint,
            float rx = 0, float ry = 0, float rz = 0)
        {
            var rotation = Quaternion.Euler(rx, ry, rz);
            var center = new Vector3(x, y, z);
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var offset = new Vector3((i & 1) == 0 ? -w : w,
                    (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -d : d);
                corners[i] = center + rotation * (offset * .5f);
            }
            Quad(paint, corners[0], corners[2], corners[3], corners[1]);
            Quad(paint, corners[4], corners[5], corners[7], corners[6]);
            Quad(paint, corners[0], corners[4], corners[6], corners[2]);
            Quad(paint, corners[1], corners[3], corners[7], corners[5]);
            Quad(paint, corners[2], corners[6], corners[7], corners[3]);
            Quad(paint, corners[0], corners[1], corners[5], corners[4]);
        }

        internal void Ring(float x, float y, float z, float bottom, float top, float h,
            Finish paint, int sides = 8, float depth = 1f)
        {
            Lathe(new Vector3(x, y, z), Quaternion.identity, bottom, top, h, sides, depth, paint);
        }

        internal void Rod(Vector3 start, Vector3 end, float diameter, Finish paint,
            float tipDiameter = -1, int sides = 6)
        {
            Vector3 axis = end - start;
            Lathe((start + end) * .5f, Quaternion.FromToRotation(Vector3.up, axis),
                diameter, tipDiameter < 0 ? diameter : tipDiameter, axis.magnitude, sides, 1, paint);
        }

        private void Lathe(Vector3 center, Quaternion rotation, float bottom, float top,
            float height, int sides, float depth, Finish paint)
        {
            Vector3 lower = center + rotation * (Vector3.down * (height * .5f));
            Vector3 upper = center + rotation * (Vector3.up * (height * .5f));
            for (int i = 0; i < sides; i++)
            {
                float a = -Mathf.PI * (2 * i + 1) / sides;
                float b = -Mathf.PI * (2 * i + 3) / sides;
                Vector3 ra = rotation * new Vector3(.5f * Mathf.Cos(a), 0, .5f * Mathf.Sin(a) * depth);
                Vector3 rb = rotation * new Vector3(.5f * Mathf.Cos(b), 0, .5f * Mathf.Sin(b) * depth);
                Vector3 l0 = lower + bottom * ra, l1 = lower + bottom * rb;
                Vector3 u0 = upper + top * ra, u1 = upper + top * rb;
                if (bottom > 0 && top > 0) Quad(paint, l0, l1, u1, u0);
                else if (bottom > 0) Triangle(paint, l0, l1, upper);
                else Triangle(paint, lower, u1, u0);
                if (bottom > 0) Triangle(paint, lower, l1, l0);
                if (top > 0) Triangle(paint, upper, u0, u1);
            }
        }

        // Convex XY silhouette, extruded along Z. Used for banners, shields and wings.
        internal void Cutout(float x, float y, float z, float depth, Finish paint, params Vector2[] outline)
        {
            float area = 0;
            for (int i = 0; i < outline.Length; i++)
            {
                var p = outline[i];
                var q = outline[(i + 1) % outline.Length];
                area += p.x * q.y - q.x * p.y;
            }
            var front = new Vector3[outline.Length];
            var back = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++)
            {
                var p = outline[area > 0 ? i : outline.Length - i - 1];
                front[i] = new Vector3(x + p.x, y + p.y, z - depth * .5f);
                back[i] = front[i] + Vector3.forward * depth;
            }
            for (int i = 1; i < front.Length - 1; i++)
            {
                Triangle(paint, front[0], front[i + 1], front[i]);
                Triangle(paint, back[0], back[i], back[i + 1]);
            }
            for (int i = 0; i < front.Length; i++)
            {
                int j = (i + 1) % front.Length;
                Quad(paint, front[i], front[j], back[j], back[i]);
            }
        }

        internal void Triangle(Finish paint, Vector3 a, Vector3 b, Vector3 c)
        {
            List<int> indices = Indices(paint);
            int n = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            indices.Add(n); indices.Add(n + 1); indices.Add(n + 2);
        }

        internal void Face(Finish paint, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Quad(paint, a, b, c, d);

        private void Quad(Finish paint, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            List<int> indices = Indices(paint);
            int n = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            indices.Add(n); indices.Add(n + 1); indices.Add(n + 2);
            indices.Add(n); indices.Add(n + 2); indices.Add(n + 3);
        }

        private List<int> Indices(Finish paint)
        {
            if (!faces.TryGetValue(paint, out List<int> indices))
            {
                indices = new List<int>();
                faces.Add(paint, indices);
            }
            return indices;
        }

        internal Mesh Attach(GameObject target, Material[] palette)
        {
            var mesh = new Mesh { name = target.name };
            if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.subMeshCount = faces.Count;
            var materials = new Material[faces.Count];
            int i = 0;
            foreach (var group in faces)
            {
                mesh.SetTriangles(group.Value, i, false);
                materials[i++] = palette[(int)group.Key];
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            target.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return mesh;
        }
    }
}
