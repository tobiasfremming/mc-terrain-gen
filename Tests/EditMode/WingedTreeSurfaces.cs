using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Analytic trees for the winged serpent's tree tests (avoidance, branch perches, trunk climbs): each tree is
    // one bark "collider" (one source id, as EnclosurePlant registers one bark MeshCollider per plant) made of
    // capsules - a vertical trunk and a few rising limbs - over flat ground at y = 0 (source 0, terrain). The
    // probe semantics are PhysicsSurfaceProbe's (as OctopusTestSurfaces' AnalyticSurfaces): Probe answers the
    // nearest surface along any up (the first one below near in air, the exit above it inside a solid), Cast the
    // first front face along a ray (none from inside). Distance() is exact, so "nothing inside a tree" is
    // checkable on every skinned vertex. Crowns are data only (CrownCentre/CrownRadius: the foliage, not a
    // collider - the lab's leaves have none either).
    public sealed class WingedTreeSurfaces : ISurfaceProbe
    {
        public struct Capsule { public Vector3 a, b; public float r; public int id; }
        public sealed class Tree
        {
            public Vector3 basePoint; public float height, trunkRadius; public int id;
            public Vector3 crownCentre; public float crownRadius;
            public readonly List<int> limbs = new List<int>(); // capsule indices of the limbs
        }

        public readonly List<Capsule> Capsules = new List<Capsule>();
        public readonly List<Tree> Trees = new List<Tree>();
        public int Probes, Casts;

        // A tree at basePoint (on the ground): a trunk of trunkRadius up to 0.8 x height and `limbs` limbs
        // (radius limbRadius, 2.2-3 m long, rising 4-30 deg) from 0.5-0.75 x height, spread round the trunk.
        public Tree AddTree(Vector3 basePoint, float height, float trunkRadius, int id, int limbs = 4, float limbRadius = 0.11f, float azimuth0 = 0f)
        {
            var t = new Tree { basePoint = basePoint, height = height, trunkRadius = trunkRadius, id = id };
            Capsules.Add(new Capsule { a = basePoint - Vector3.up * 0.3f, b = basePoint + Vector3.up * (0.8f * height), r = trunkRadius, id = id });
            for (int k = 0; k < limbs; k++)
            {
                float a = (azimuth0 + 360f * k / limbs + 23f * (k % 2)) * Mathf.Deg2Rad;
                float h = Mathf.Lerp(0.5f, 0.75f, (k * 0.37f) % 1f) * height;
                float len = Mathf.Lerp(2.2f, 3f, (k * 0.61f) % 1f);
                float rise = Mathf.Lerp(4f, 30f, (k * 0.29f) % 1f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(rise), Mathf.Sin(rise), Mathf.Sin(a) * Mathf.Cos(rise));
                Vector3 s = basePoint + Vector3.up * h;
                t.limbs.Add(Capsules.Count);
                Capsules.Add(new Capsule { a = s, b = s + dir * len, r = limbRadius, id = id });
            }
            t.crownCentre = basePoint + Vector3.up * (0.75f * height);
            t.crownRadius = 0.35f * height;
            Trees.Add(t);
            return t;
        }

        // A straight horizontal limb on its own (a perch test), as its own collider.
        public WingedTreeSurfaces AddLimb(Vector3 a, Vector3 b, float r, int id)
        {
            Capsules.Add(new Capsule { a = a, b = b, r = r, id = id });
            return this;
        }

        // The lab's perch spot for a tree: its crown as a region (CreatureLab.DressAviary).
        public PerchSpot CrownSpot(Tree t) => new PerchSpot { point = t.basePoint + Vector3.up * (0.95f * t.height), radius = Mathf.Max(1f, 0.35f * t.height) };

        // Signed distance to the nearest tree capsule (negative inside), its outward normal and source.
        public float TreeDistance(Vector3 p, out Vector3 normal, out int source)
        {
            float best = float.MaxValue; normal = Vector3.up; source = 0;
            for (int i = 0; i < Capsules.Count; i++)
            {
                Capsule c = Capsules[i];
                Vector3 ab = c.b - c.a;
                float t = Mathf.Clamp01(Vector3.Dot(p - c.a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                Vector3 d = p - (c.a + ab * t);
                float m = d.magnitude, dist = m - c.r;
                if (dist < best) { best = dist; normal = m > 1e-9f ? d / m : Vector3.up; source = c.id; }
            }
            return best;
        }

        // Trees and the ground.
        public float Distance(Vector3 p, out Vector3 normal, out int source)
        {
            float best = TreeDistance(p, out normal, out source);
            if (p.y < best) { best = p.y; normal = Vector3.up; source = 0; }
            return best;
        }

        public bool Inside(Vector3 p) => Distance(p, out _, out _) < 0f;

        bool March(Vector3 o, Vector3 d, float maxDist, bool exit, out float t, out Vector3 n, out int src)
        {
            t = 0f; n = Vector3.up; src = 0;
            for (int it = 0; it < 512 && t <= maxDist; it++)
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
                if (Vector3.Dot(n, u) < 0f) n = -n;
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
            if (Vector3.Dot(n, d) > 0f) n = -n;
            hit = new SurfaceHit { point = origin + d * t, normal = n, source = src, distance = t };
            return true;
        }

        public bool AnySurfaceNear(Bounds b) => true;
    }
}
