using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Analytic surfaces for the octopus agent tests: capsules (branches, bars, trunks) and horizontal planes
    // (ground), behind ISurfaceProbe with PhysicsSurfaceProbe's semantics - Probe answers the nearest surface
    // along up (the first surface below near when near is in air, the exit above it when near is inside a
    // solid); Cast answers the first front face along the ray (none from inside a solid). Every answer is a
    // point ON the analytic surface, so "contacts come from the probe" is checkable exactly. The real-mesh
    // versions of these tests use PhysicsSurfaceProbe on generated MeshColliders (Editor).
    internal sealed class AnalyticSurfaces : ISurfaceProbe
    {
        public struct Capsule { public Vector3 a, b; public float r; }
        public readonly List<Capsule> Capsules = new List<Capsule>();
        public readonly List<float> Planes = new List<float>(); // y heights, solid below
        public int Probes, Casts;

        public AnalyticSurfaces AddCapsule(Vector3 a, Vector3 b, float r) { Capsules.Add(new Capsule { a = a, b = b, r = r }); return this; }
        public AnalyticSurfaces AddGround(float y) { Planes.Add(y); return this; }

        // Signed distance (negative inside) and outward normal of the nearest solid.
        public float Distance(Vector3 p, out Vector3 normal, out int source)
        {
            float best = float.MaxValue; normal = Vector3.up; source = 0;
            for (int i = 0; i < Capsules.Count; i++)
            {
                Capsule c = Capsules[i];
                Vector3 ab = c.b - c.a;
                float t = Mathf.Clamp01(Vector3.Dot(p - c.a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                Vector3 q = c.a + ab * t, d = p - q;
                float dist = d.magnitude - c.r;
                if (dist < best) { best = dist; normal = d.magnitude > 1e-9f ? d / d.magnitude : Vector3.up; source = i + 1; }
            }
            for (int i = 0; i < Planes.Count; i++)
            {
                float dist = p.y - Planes[i];
                if (dist < best) { best = dist; normal = Vector3.up; source = 1000 + i; }
            }
            return best;
        }

        public bool Inside(Vector3 p) => Distance(p, out _, out _) < 0f;

        // Sphere-traced ray against the union (exact enough at a 1e-5 m tolerance).
        bool March(Vector3 o, Vector3 d, float maxDist, bool exit, out float t, out Vector3 n, out int src)
        {
            t = 0f; n = Vector3.up; src = 0;
            for (int it = 0; it < 256 && t <= maxDist; it++)
            {
                float s = Distance(o + d * t, out n, out src);
                if (exit) s = -s;
                if (s < 1e-5f)
                {
                    if (exit) Distance(o + d * t, out n, out src);
                    return true;
                }
                t += Mathf.Max(s, 1e-5f);
            }
            return false;
        }

        public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
        {
            bool ok = Probe(near, up, window, SurfaceHit.NoSource, -1, out SurfaceHit h);
            point = h.point; normal = h.normal;
            return ok;
        }

        public bool Probe(Vector3 near, Vector3 up, float window, int preferSource, int slot, out SurfaceHit hit)
        {
            Probes++;
            hit = default;
            Vector3 u = up.normalized;
            if (Inside(near))
            {
                if (!March(near, u, window, true, out float t, out Vector3 n, out int src)) return false;
                if (Vector3.Dot(n, u) < 0f) n = -n;   // as PhysicsSurfaceProbe: normals face the up given
                hit = new SurfaceHit { point = near + u * t, normal = n, source = src, distance = t };
                return true;
            }
            if (!March(near, -u, window, false, out float t2, out Vector3 n2, out int src2)) return false;
            if (Vector3.Dot(n2, u) < 0f) n2 = -n2;
            hit = new SurfaceHit { point = near - u * t2, normal = n2, source = src2, distance = -t2 };
            return true;
        }

        public bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit)
        {
            Casts++;
            hit = default;
            if (Inside(origin)) return false;
            Vector3 d = dir.normalized;
            if (!March(origin, d, maxDist, false, out float t, out Vector3 n, out int src)) return false;
            if (Vector3.Dot(n, d) > 0f) n = -n;   // as PhysicsSurfaceProbe: against the ray
            hit = new SurfaceHit { point = origin + d * t, normal = n, source = src, distance = t };
            return true;
        }

        public bool AnySurfaceNear(Bounds b) => true;
    }

    // Branch hints from the analytic capsules (as BranchHints answers a plant skeleton: the closest point on each
    // skeleton segment), each capsule cut into segments of about `segment` metres; planes are not branches.
    internal sealed class CapsuleHints : IBranchHints
    {
        readonly List<Vector3> a = new List<Vector3>(), b = new List<Vector3>();
        readonly List<float> r = new List<float>();
        public int Queries;

        public CapsuleHints(AnalyticSurfaces s, float segment)
        {
            foreach (var c in s.Capsules)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((c.b - c.a).magnitude / segment));
                for (int i = 0; i < n; i++) { a.Add(Vector3.Lerp(c.a, c.b, (float)i / n)); b.Add(Vector3.Lerp(c.a, c.b, (float)(i + 1) / n)); r.Add(c.r); }
            }
        }

        public int Query(Vector3 origin, Vector3 coneAxis, float coneCos, float minDist, float maxDist, float minRadius, float maxRadius, BranchHint[] results)
        {
            Queries++;
            int count = 0, cap = results.Length;
            for (int i = 0; i < a.Count; i++)
            {
                if (r[i] < minRadius || r[i] > maxRadius) continue;
                Vector3 ab = b[i] - a[i];
                float l2 = ab.sqrMagnitude, u = l2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(origin - a[i], ab) / l2) : 0f;
                Vector3 p = a[i] + ab * u, d = p - origin;
                float dist = d.magnitude;
                if (dist < minDist || dist > maxDist || dist < 1e-6f) continue;
                if (Vector3.Dot(d, coneAxis) < coneCos * dist) continue;
                float score = dist * (2f - Vector3.Dot(d / dist, coneAxis));
                if (count == cap && score >= results[cap - 1].score) continue;
                int at = count < cap ? count++ : cap - 1;
                while (at > 0 && results[at - 1].score > score) { results[at] = results[at - 1]; at--; }
                results[at] = new BranchHint { point = p, axis = l2 > 1e-12f ? ab / Mathf.Sqrt(l2) : Vector3.up, radius = r[i], score = score };
            }
            return count;
        }
    }

    internal static class OctopusGroves
    {
        // `trees` trees in a row along +x, `spacing` apart (x scale s): a trunk (r 0.12) and six limbs radiating
        // level at 2.3-2.8 m, 1.3-1.7 m long (r 0.05-0.07): neighbouring crowns meet, so the way from tree to
        // tree is from limb to limb. Far ground below. Tree i stands at x = i spacing.
        public static AnalyticSurfaces Row(int trees, float s, float spacing = 2.6f, int seed = 1)
        {
            var g = new AnalyticSurfaces();
            uint rng = (uint)(seed * 2654435761u + 12345u);
            float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
            for (int t = 0; t < trees; t++)
            {
                Vector3 c = new Vector3(t * spacing, 0f, 0f);
                g.AddCapsule((c + new Vector3(0f, -1f, 0f)) * s, (c + new Vector3(0f, 3.2f, 0f)) * s, 0.12f * s);
                for (int k = 0; k < 6; k++)
                {
                    float az = (k + 0.3f * Rand()) * Mathf.PI / 3f + t * 0.4f;
                    float y = 2.3f + 0.5f * Rand(), len = 1.3f + 0.4f * Rand();
                    Vector3 dir = new Vector3(Mathf.Cos(az), 0.05f * (Rand() - 0.5f), Mathf.Sin(az));
                    Vector3 root = c + new Vector3(0f, y, 0f);
                    g.AddCapsule(root * s, (root + dir * len) * s, (0.05f + 0.02f * Rand()) * s);
                }
            }
            g.AddGround(-5f * s);
            return g;
        }
    }
}
