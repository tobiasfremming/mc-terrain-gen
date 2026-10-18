using System.Collections.Generic;
using UnityEngine;

// The aviary's rock perch geometry (AviaryDressing.RockMesh builds the mesh from it). Pure C#.
public static class AviaryRock
{
    // A boulder: a subdivided octahedron pushed out to an ellipsoid of `size`, lumpy (value noise), its top
    // flattened into a perch, its base sunk. Pivot at the base centre. Front faces outward: Cross(b - a, c - a)
    // points out of the rock (Unity's clockwise-from-outside winding; the octahedron's faces already are).
    public static void Geometry(Vector3 size, int seed, List<Vector3> verts, List<int> tris)
    {
        verts.Clear(); tris.Clear();
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
            tris.Clear(); tris.AddRange(next);
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
    }

    // The centre the rock's surface is star-shaped about (the ellipsoid's centre before the base sinks).
    public static Vector3 Center(Vector3 size) => new Vector3(0f, 0.5f * size.y * 0.85f, 0f);

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
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
        Vector3 f = p - new Vector3(ix, iy, iz);
        f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));
        float c000 = Lattice(ix, iy, iz), c100 = Lattice(ix + 1, iy, iz), c010 = Lattice(ix, iy + 1, iz), c110 = Lattice(ix + 1, iy + 1, iz);
        float c001 = Lattice(ix, iy, iz + 1), c101 = Lattice(ix + 1, iy, iz + 1), c011 = Lattice(ix, iy + 1, iz + 1), c111 = Lattice(ix + 1, iy + 1, iz + 1);
        float x00 = Mathf.Lerp(c000, c100, f.x), x10 = Mathf.Lerp(c010, c110, f.x), x01 = Mathf.Lerp(c001, c101, f.x), x11 = Mathf.Lerp(c011, c111, f.x);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, f.y), Mathf.Lerp(x01, x11, f.y), f.z);
    }

    static float Lattice(int x, int y, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
        return Hash(ref h);
    }

}
