using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Real generated meshes for the climbing tests (smooth vertex normals, Unity's clockwise front faces), put
    // on MeshColliders in the default physics scene on layer kLayer (unused by the project: an open scene's
    // own colliders - terrain chunks, the player - are on Default and never answer). Dispose destroys
    // everything it made.
    public sealed class ClimbTestMeshes : System.IDisposable
    {
        public const int kLayer = 29, kOtherLayer = 28;
        public readonly CreatureSurfaces Registry;
        readonly List<Object> made = new List<Object>();

        public ClimbTestMeshes()
        {
            Registry = CreatureSurfaces.ForScene(Physics.defaultPhysicsScene, 1 << kLayer, true, kLayer);
        }

        // A vertical cylinder (radius r, base y0, top y1) around (x, z), capped, with radial smooth normals on
        // the side; registered.
        public MeshCollider Cylinder(Vector3 baseCentre, float r, float height, int segments = 48, int rings = 24)
            => Add(CylinderMesh(r, height, segments, rings), baseCentre, Quaternion.identity, kLayer, register: true);

        // A horizontal branch: a cylinder along +x from `from`, radius r, length len.
        public MeshCollider Branch(Vector3 from, float r, float len)
            => Add(CylinderMesh(r, len, 32, 16), from, Quaternion.AngleAxis(-90f, Vector3.forward), kLayer, register: true);

        public MeshCollider Box(Vector3 centre, Vector3 size, int layer = kLayer, bool register = true)
            => Add(BoxMesh(size), centre, Quaternion.identity, layer, register);

        public MeshCollider Add(Mesh m, Vector3 pos, Quaternion rot, int layer, bool register)
        {
            made.Add(m);
            var go = new GameObject("ClimbTest " + m.name) { layer = layer, hideFlags = HideFlags.DontSave };
            go.transform.SetPositionAndRotation(pos, rot);
            made.Add(go);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = m;
            if (register) Registry.Register(mc);
            Physics.SyncTransforms();
            return mc;
        }

        public static Mesh CylinderMesh(float r, float h, int seg, int rings)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k <= rings; k++)
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * 2f * Mathf.PI / seg;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * r + Vector3.up * (h * k / rings)); n.Add(d);
                }
            for (int k = 0; k < rings; k++)
                for (int s = 0; s < seg; s++)
                {
                    int a = k * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    // outward-facing clockwise (seen from outside): a, c, b and b, c, d
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            // caps
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? 0f : h;
                Vector3 nn = cap == 0 ? Vector3.down : Vector3.up;
                int centre = v.Count; v.Add(new Vector3(0f, y, 0f)); n.Add(nn);
                int first = v.Count;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * 2f * Mathf.PI / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); n.Add(nn);
                }
                for (int s = 0; s < seg; s++)
                {
                    if (cap == 1) { t.Add(centre); t.Add(first + s + 1); t.Add(first + s); }
                    else { t.Add(centre); t.Add(first + s); t.Add(first + s + 1); }
                }
            }
            var m = new Mesh { name = "Cylinder" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            FixWinding(m);
            return m;
        }

        public MeshCollider Sphere(Vector3 centre, float r) => Add(SphereMesh(r, 48, 24), centre, Quaternion.identity, kLayer, register: true);

        public static Mesh SphereMesh(float r, int seg, int rings)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int k = 0; k <= rings; k++)
            {
                float th = Mathf.PI * k / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float ph = 2f * Mathf.PI * s / seg;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * r); n.Add(d);
                }
            }
            for (int k = 0; k < rings; k++)
                for (int s = 0; s < seg; s++)
                {
                    int a = k * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
                }
            var m = new Mesh { name = "Sphere" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            FixWinding(m);
            return m;
        }

        public static Mesh BoxMesh(Vector3 size)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Vector3 h = 0.5f * size;
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 f in axes)
            {
                Vector3 u = Mathf.Abs(f.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 r = Vector3.Cross(u, f);
                int b = v.Count;
                for (int c = 0; c < 4; c++)
                {
                    float sx = (c & 1) == 0 ? -1f : 1f, sy = (c & 2) == 0 ? -1f : 1f;
                    Vector3 p = f + r * sx + u * sy;
                    v.Add(Vector3.Scale(p, h)); n.Add(f);
                }
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
            }
            var m = new Mesh { name = "Box" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            FixWinding(m);
            return m;
        }

        // Makes every triangle's winding normal agree with its vertex normals (front = outward).
        static void FixWinding(Mesh m)
        {
            Vector3[] v = m.vertices, n = m.normals;
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 f = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (Vector3.Dot(f, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0f) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
            }
            m.triangles = t;
        }

        public void Dispose()
        {
            Registry.Dispose();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            Physics.SyncTransforms();
        }
    }
}
