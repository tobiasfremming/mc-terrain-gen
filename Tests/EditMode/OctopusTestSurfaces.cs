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
}
