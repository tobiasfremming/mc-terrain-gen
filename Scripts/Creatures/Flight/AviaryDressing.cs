using System.Collections.Generic;
using UnityEngine;

// Dressing for a flying species' enclosure in the creature lab (design decision 4: an aviary, radius 14 m x
// height 10 m with trees and rock perches): sandstone rock perches (render meshes made climbable/perchable by
// CreatureSurfaceMesh) and slim posts marking the aviary's edge up to its roof. Pure mesh generation plus a
// few GameObjects; built once when the lab places the enclosure.
public static class AviaryDressing
{
    // A boulder: a subdivided octahedron pushed out to an ellipsoid of `size`, lumpy (value noise), its top
    // flattened into a perch, its base sunk. Pivot at the base centre.
    public static Mesh RockMesh(Vector3 size, int seed)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        // octahedron
        verts.AddRange(new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back });
        int[] faces = { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
        tris.AddRange(faces);
        for (int level = 0; level < 3; level++)
        {
            var mid = new Dictionary<long, int>();
            var next = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                int ab = Mid(a, b, verts, mid), bc = Mid(b, c, verts, mid), ca = Mid(c, a, verts, mid);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = next;
        }
        uint h = (uint)seed * 2654435761u + 0x9E3779B9u;
        float ox = Hash(ref h) * 100f, oy = Hash(ref h) * 100f, oz = Hash(ref h) * 100f;
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 d = verts[i].normalized;
            float lump = 1f + 0.18f * (Noise(d * 2.1f + new Vector3(ox, oy, oz)) - 0.5f) * 2f + 0.07f * (Noise(d * 5.3f + new Vector3(oz, ox, oy)) - 0.5f) * 2f;
            Vector3 p = Vector3.Scale(d * lump, 0.5f * size);
            // a flat-ish top to perch on, a sunk base
            float top = 0.5f * size.y * 0.78f;
            if (p.y > top) p.y = top + (p.y - top) * 0.15f;
            p.y += 0.5f * size.y * 0.85f;
            verts[i] = p;
        }
        // Unity's front faces are clockwise seen from outside
        for (int t = 0; t < tris.Count; t += 3) { int tmp = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = tmp; }
        var m = new Mesh { name = "Aviary rock" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static int Mid(int a, int b, List<Vector3> v, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int i)) return i;
        v.Add(((v[a] + v[b]) * 0.5f).normalized);
        cache[key] = v.Count - 1;
        return v.Count - 1;
    }

    static float Hash(ref uint s)
    {
        s ^= s >> 16; s *= 0x7FEB352Du; s ^= s >> 15; s *= 0x846CA68Bu; s ^= s >> 16;
        return (s & 0xFFFFFFu) / 16777216f;
    }

    // smooth value noise in 0..1
    static float Noise(Vector3 p)
    {
        Vector3Int i = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
        Vector3 f = p - i;
        f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));
        float c000 = Lattice(i.x, i.y, i.z), c100 = Lattice(i.x + 1, i.y, i.z), c010 = Lattice(i.x, i.y + 1, i.z), c110 = Lattice(i.x + 1, i.y + 1, i.z);
        float c001 = Lattice(i.x, i.y, i.z + 1), c101 = Lattice(i.x + 1, i.y, i.z + 1), c011 = Lattice(i.x, i.y + 1, i.z + 1), c111 = Lattice(i.x + 1, i.y + 1, i.z + 1);
        float x00 = Mathf.Lerp(c000, c100, f.x), x10 = Mathf.Lerp(c010, c110, f.x), x01 = Mathf.Lerp(c001, c101, f.x), x11 = Mathf.Lerp(c011, c111, f.x);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, f.y), Mathf.Lerp(x01, x11, f.y), f.z);
    }

    static float Lattice(int x, int y, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
        return Hash(ref h);
    }

    // A rock perch at `at` (on the ground, up), registered as a perchable surface; its mesh is owned by the
    // returned object (destroy both with it).
    public static GameObject Rock(Transform parent, Vector3 at, Vector3 up, Vector3 size, int seed, Material material, out Mesh mesh)
    {
        mesh = RockMesh(size, seed);
        var go = new GameObject("Rock perch");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(at - up * (0.08f * size.y), Quaternion.AngleAxis(57f * seed, up) * Quaternion.FromToRotation(Vector3.up, up));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<CreatureSurfaceMesh>();
        return go;
    }

    // Slim posts round the aviary's edge, up to its roof (decoration: no collider).
    public static void Posts(Transform parent, Vector3 center, Vector3 up, float radius, float height, int count, Material material, IGroundProbe ground)
    {
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        for (int k = 0; k < count; k++)
        {
            float a = 2f * Mathf.PI * k / count;
            Vector3 p = center + (t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a)) * (radius + 0.2f);
            if (ground != null && ground.Probe(p + up * 2f, up, 8f, out Vector3 g, out _)) p = g;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "Aviary post";
            var col = post.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col); }
            post.transform.SetParent(parent, true);
            post.transform.SetPositionAndRotation(p + up * (0.5f * height), Quaternion.FromToRotation(Vector3.up, up));
            post.transform.localScale = new Vector3(0.1f, 0.5f * height, 0.1f);
            if (material != null) post.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
