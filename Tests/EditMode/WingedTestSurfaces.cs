using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Analytic ground for flight tests: flat ground at y = 0 plus axis-aligned boxes (rocks) and horizontal
    // cylinders along x (branches), as one ISurfaceProbe with the probe rule of the real ones (the surface
    // nearest to `near` along up, facing up; Cast: the first front face along a ray). Up is +Y only.
    public sealed class WingedTestSurfaces : ISurfaceProbe
    {
        public struct Box { public Vector3 min, max; public int id; }
        public struct Branch { public float x0, x1, y, z, r; public int id; }

        public readonly List<Box> Boxes = new List<Box>();
        public readonly List<Branch> Branches = new List<Branch>();
        public int Queries;

        public WingedTestSurfaces AddRock(Vector3 centreBottom, Vector3 size, int id)
        {
            Boxes.Add(new Box { min = centreBottom - new Vector3(0.5f * size.x, 0f, 0.5f * size.z), max = centreBottom + new Vector3(0.5f * size.x, size.y, 0.5f * size.z), id = id });
            return this;
        }

        public WingedTestSurfaces AddBranch(float x0, float x1, float y, float z, float r, int id)
        {
            Branches.Add(new Branch { x0 = x0, x1 = x1, y = y, z = z, r = r, id = id });
            return this;
        }

        public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
        {
            bool ok = Probe(near, up, window, SurfaceHit.NoSource, -1, out SurfaceHit h);
            point = h.point; normal = h.normal;
            return ok;
        }

        // Up-facing surfaces in the column through near: the ground, box tops, branch tops.
        public bool Probe(Vector3 near, Vector3 up, float window, int preferSource, int slot, out SurfaceHit hit)
        {
            Queries++;
            SurfaceHit h = default;
            float best = float.MaxValue; bool found = false;
            // as the real probes: the surface nearest along up within the window, else the first one below
            // within the fallback range (FieldGroundProbe scans 16 m down through air)
            void Consider(float y, Vector3 nrm, int src)
            {
                float d = Mathf.Abs(y - near.y);
                if (d > window) { if (y > near.y || near.y - y > 16f) return; d = window + (near.y - y); }
                if (d >= best) return;
                best = d; found = true;
                h = new SurfaceHit { point = new Vector3(near.x, y, near.z), normal = nrm, source = src, distance = y - near.y };
            }
            Consider(0f, Vector3.up, 0);
            foreach (var b in Boxes)
                if (near.x >= b.min.x && near.x <= b.max.x && near.z >= b.min.z && near.z <= b.max.z) Consider(b.max.y, Vector3.up, b.id);
            foreach (var c in Branches)
            {
                if (near.x < c.x0 || near.x > c.x1) continue;
                float dz = near.z - c.z;
                if (Mathf.Abs(dz) >= c.r) continue;
                float dy = Mathf.Sqrt(c.r * c.r - dz * dz);
                Consider(c.y + dy, new Vector3(0f, dy, dz) / c.r, c.id);
            }
            hit = h;
            return found;
        }

        public bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit)
        {
            hit = default;
            dir = dir.normalized;
            float best = maxDist; bool found = false;
            // ground
            if (dir.y < -1e-6f && origin.y > 0f)
            {
                float t = -origin.y / dir.y;
                if (t < best) { best = t; found = true; hit = new SurfaceHit { point = origin + dir * t, normal = Vector3.up, source = 0, distance = t }; }
            }
            foreach (var b in Boxes)
                if (RayBox(origin, dir, b.min, b.max, out float t, out Vector3 nrm) && t < best)
                { best = t; found = true; hit = new SurfaceHit { point = origin + dir * t, normal = nrm, source = b.id, distance = t }; }
            foreach (var c in Branches)
            {
                // circle in (y, z), x within [x0, x1]
                float oy = origin.y - c.y, oz = origin.z - c.z;
                float a = dir.y * dir.y + dir.z * dir.z;
                if (a < 1e-9f) continue;
                float bq = 2f * (oy * dir.y + oz * dir.z), cq = oy * oy + oz * oz - c.r * c.r;
                float disc = bq * bq - 4f * a * cq;
                if (disc < 0f || cq < 0f) continue;
                float t = (-bq - Mathf.Sqrt(disc)) / (2f * a);
                if (t < 0f || t >= best) continue;
                Vector3 p = origin + dir * t;
                if (p.x < c.x0 || p.x > c.x1) continue;
                best = t; found = true;
                hit = new SurfaceHit { point = p, normal = new Vector3(0f, p.y - c.y, p.z - c.z) / c.r, source = c.id, distance = t };
            }
            return found;
        }

        public bool AnySurfaceNear(Bounds b) => Boxes.Count + Branches.Count > 0;

        static bool RayBox(Vector3 o, Vector3 d, Vector3 lo, Vector3 hi, out float tHit, out Vector3 nrm)
        {
            tHit = 0f; nrm = Vector3.up;
            float t0 = 0f, t1 = float.MaxValue; int axis = -1; float sign = 0f;
            for (int k = 0; k < 3; k++)
            {
                float ok = k == 0 ? o.x : k == 1 ? o.y : o.z, dk = k == 0 ? d.x : k == 1 ? d.y : d.z;
                float lk = k == 0 ? lo.x : k == 1 ? lo.y : lo.z, hk = k == 0 ? hi.x : k == 1 ? hi.y : hi.z;
                if (Mathf.Abs(dk) < 1e-9f) { if (ok < lk || ok > hk) return false; continue; }
                float a = (lk - ok) / dk, b = (hk - ok) / dk;
                float s = -1f;
                if (a > b) { float tmp = a; a = b; b = tmp; s = 1f; }
                if (a > t0) { t0 = a; axis = k; sign = s; }
                if (b < t1) t1 = b;
                if (t0 > t1) return false;
            }
            if (axis < 0) return false; // origin inside
            tHit = t0;
            nrm = axis == 0 ? new Vector3(sign, 0f, 0f) : axis == 1 ? new Vector3(0f, sign, 0f) : new Vector3(0f, 0f, sign);
            return true;
        }
    }
}
