using System;
using UnityEngine;

// Surfaces from colliders (a CreatureSurfaces registry: a scene + mask). A probe is a ray along -up from near,
// with back faces on: a front face first = near is in air, that is the surface; a back face first = near is
// inside solid, and the answer is the exit above it (the up ray's first back face) - R25's nearest-surface
// rule, so a foot never drops through a branch and the body is never lifted onto one from below. The normal is
// the mesh's own vertex normals interpolated at the hit (the smooth normal the surface is shaded with); a
// collider without readable mesh data answers with the hit's face normal.
// Per query slot (a joint, a foot) the last triangle is kept in world space and tested analytically first: a
// line hit within kCacheBand x window of near is the answer without a raycast (re-checked by a raycast every
// kRevalidate uses and whenever the registry changes). A miss is remembered within kMissRadius x window.
// Physics.queriesHitBackfaces is a global: Backfaces() holds it on for a whole tick (CreatureWorld); a query
// outside such a scope sets and restores it itself.
public sealed class PhysicsSurfaceProbe : ISurfaceProbe, ISurfaceSlots
{
    const float kCacheBand = 0.25f;
    const float kBaryEps = 1e-4f;
    const int kRevalidate = 16;
    const float kMissRadius = 0.05f;
    const float kStartLift = 0.005f;   // rays start this fraction of the window behind near (a ray starting on a face may miss it)
    const int kInitialSlots = 64;

    struct Tri
    {
        public int id, version, uses;
        public Vector3 a, b, c, na, nb, nc, face;
    }
    struct Miss { public Vector3 at, up; public int version; }

    readonly CreatureSurfaces surfaces;
    Tri[] tris = new Tri[kInitialSlots];
    Miss[] misses = new Miss[kInitialSlots];

    public int Queries, Raycasts, CacheHits, MissHits; // cumulative stats

    public PhysicsSurfaceProbe(CreatureSurfaces surfaces)
    {
        this.surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
    }

    public CreatureSurfaces Registry => surfaces;

    public void EnsureSlots(int count)
    {
        if (count <= tris.Length) return;
        Array.Resize(ref tris, count);
        Array.Resize(ref misses, count);
    }

    // ---- the global back-face switch ----

    static int backfaceDepth;
    static bool savedBackfaces;

    public struct BackfaceScope : IDisposable
    {
        bool active;
        internal static BackfaceScope Begin()
        {
            if (backfaceDepth++ == 0) { savedBackfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true; }
            return new BackfaceScope { active = true };
        }
        public void Dispose()
        {
            if (!active) return;
            active = false;
            if (--backfaceDepth == 0) Physics.queriesHitBackfaces = savedBackfaces;
        }
    }

    // using (PhysicsSurfaceProbe.Backfaces()) { ...queries... } - nests; restores the previous setting.
    public static BackfaceScope Backfaces() => BackfaceScope.Begin();

    // ---- ISurfaceProbe ----

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        bool ok = Probe(near, up, window, SurfaceHit.NoSource, -1, out SurfaceHit h);
        point = h.point; normal = h.normal;
        return ok;
    }

    public bool Probe(Vector3 near, Vector3 up, float window, int preferSource, int slot, out SurfaceHit hit)
    {
        Queries++;
        Vector3 u = SurfaceMath.Norm(up, Vector3.up);
        if (slot >= 0)
        {
            EnsureSlots(slot + 1);
            if (TryTriangle(slot, near, u, window, out hit)) { CacheHits++; return true; }
            if (TryMiss(slot, near, u, window)) { MissHits++; hit = MissHit(near, u); return false; }
        }
        BackfaceScope scope = Backfaces();
        try
        {
            if (Nearest(near, u, window, slot, out hit)) return true;
            if (slot >= 0) misses[slot] = new Miss { at = near, up = u, version = surfaces.Version };
            return false;
        }
        finally { scope.Dispose(); }
    }

    public bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit)
    {
        Vector3 d = SurfaceMath.Norm(dir, Vector3.forward);
        BackfaceScope scope = Backfaces();
        try
        {
            if (Ray(origin, d, maxDist, out RaycastHit h) && Front(h, d, out CreatureSurfaces.Surface s, out Vector3 face))
            {
                // the answer's normal faces against the ray (Answer orients normals toward the "up" it is given)
                hit = Answer(h, s, face, origin, -d, -1);
                hit.distance = h.distance;
                return true;
            }
        }
        finally { scope.Dispose(); }
        hit = new SurfaceHit { point = origin, normal = -d, source = 0, distance = maxDist };
        return false;
    }

    public bool AnySurfaceNear(Bounds b) => surfaces.AnySurfaceNear(b);

    static SurfaceHit MissHit(Vector3 near, Vector3 u) => new SurfaceHit { point = near, normal = u, source = 0, distance = 0f };

    // ---- the nearest surface by rays ----

    bool Nearest(Vector3 near, Vector3 u, float window, int slot, out SurfaceHit hit)
    {
        float lift = kStartLift * window;
        Vector3 above = near + u * lift, below = near - u * lift;
        if (Ray(above, -u, window + lift, out RaycastHit h))
        {
            if (Front(h, -u, out CreatureSurfaces.Surface s, out Vector3 face))
            {
                // a front face below is the ground only if near is not inside another, overlapping solid (a
                // branch's tube around a trunk's cap at a junction): the first face above must not be a back face
                if (Ray(below, u, window + lift, out RaycastHit ha) && !Front(ha, u, out CreatureSurfaces.Surface sa, out Vector3 fa))
                { hit = Answer(ha, sa, fa, near, u, slot); return true; }
                hit = Answer(h, s, face, near, u, slot);
                return true;
            }
            // inside solid: the exit above near
            if (Ray(below, u, window + lift, out h) && !Front(h, u, out s, out face)) { hit = Answer(h, s, face, near, u, slot); return true; }
            hit = MissHit(near, u);
            return false;
        }
        // nothing below within the window: inside a solid whose far side is further away, or in air
        if (Ray(below, u, window + lift, out h) && !Front(h, u, out CreatureSurfaces.Surface s2, out Vector3 face2))
        {
            hit = Answer(h, s2, face2, near, u, slot);
            return true;
        }
        hit = MissHit(near, u);
        return false;
    }

    bool Ray(Vector3 o, Vector3 d, float len, out RaycastHit h)
    {
        Raycasts++;
        return surfaces.Scene.Raycast(o, d, out h, len, surfaces.Mask, QueryTriggerInteraction.Ignore);
    }

    // Is the hit triangle's front facing the ray? Front = its outward side (the side the vertex normals point
    // to; the winding's normal when the mesh has none).
    bool Front(in RaycastHit h, Vector3 dir, out CreatureSurfaces.Surface s, out Vector3 face)
    {
        s = surfaces.Resolve(h);
        face = h.normal;
        SurfaceMeshCache.Entry m = s != null ? s.mesh : null;
        int ti = h.triangleIndex;
        if (m != null && m.vertices != null && ti >= 0 && 3 * ti + 2 < m.triangles.Length)
        {
            int i0 = m.triangles[3 * ti], i1 = m.triangles[3 * ti + 1], i2 = m.triangles[3 * ti + 2];
            Vector3 a = m.vertices[i0], b = m.vertices[i1], c = m.vertices[i2];
            Vector3 f = SurfaceMath.Cross(b - a, c - a);
            if (m.normals != null && SurfaceMath.Dot(f, m.normals[i0] + m.normals[i1] + m.normals[i2]) < 0f) f = -f;
            face = SurfaceMath.Norm(s.normalToWorld.MultiplyVector(f), h.normal);
        }
        return SurfaceMath.Dot(face, dir) < 0f;
    }

    SurfaceHit Answer(in RaycastHit h, CreatureSurfaces.Surface s, Vector3 face, Vector3 near, Vector3 u, int slot)
    {
        Vector3 n = face;
        SurfaceMeshCache.Entry m = s != null ? s.mesh : null;
        int ti = h.triangleIndex;
        bool tri = m != null && m.vertices != null && ti >= 0 && 3 * ti + 2 < m.triangles.Length;
        // the outward normal faces the probe's up: flip a back face's (an exit seen from inside)
        Vector3 outward = SurfaceMath.Dot(face, u) >= 0f ? face : -face;
        if (tri)
        {
            int i0 = m.triangles[3 * ti], i1 = m.triangles[3 * ti + 1], i2 = m.triangles[3 * ti + 2];
            if (m.normals != null)
            {
                Vector3 bc = h.barycentricCoordinate;
                Vector3 ln = m.normals[i0] * bc.x + m.normals[i1] * bc.y + m.normals[i2] * bc.z;
                n = SurfaceMath.Norm(s.normalToWorld.MultiplyVector(ln), face);
                if (SurfaceMath.Dot(n, face) < 0.1f) n = face;
            }
            if (slot >= 0)
            {
                ref Tri t = ref tris[slot];
                t.id = s.id; t.version = surfaces.Version; t.uses = 0;
                t.a = s.toWorld.MultiplyPoint3x4(m.vertices[i0]);
                t.b = s.toWorld.MultiplyPoint3x4(m.vertices[i1]);
                t.c = s.toWorld.MultiplyPoint3x4(m.vertices[i2]);
                t.face = face;
                if (m.normals != null)
                {
                    t.na = SurfaceMath.Norm(s.normalToWorld.MultiplyVector(m.normals[i0]), face);
                    t.nb = SurfaceMath.Norm(s.normalToWorld.MultiplyVector(m.normals[i1]), face);
                    t.nc = SurfaceMath.Norm(s.normalToWorld.MultiplyVector(m.normals[i2]), face);
                }
                else t.na = t.nb = t.nc = face;
            }
        }
        else if (slot >= 0) tris[slot].id = 0;
        if (SurfaceMath.Dot(n, u) < 0f && SurfaceMath.Dot(outward, u) >= 0f) n = outward;
        return new SurfaceHit
        {
            point = h.point,
            normal = n,
            source = s != null ? s.id : h.colliderInstanceID,
            distance = SurfaceMath.Dot(h.point - near, u),
        };
    }

    // ---- slot caches ----

    bool TryTriangle(int slot, Vector3 near, Vector3 u, float window, out SurfaceHit hit)
    {
        ref Tri t = ref tris[slot];
        hit = default;
        if (t.id == 0 || t.version != surfaces.Version || t.uses >= kRevalidate) return false;
        if (SurfaceMath.Dot(t.face, u) <= 0f) return false;
        if (!SurfaceMath.LineTriangle(near, u, t.a, t.b, t.c, out float tt, out float bu, out float bv)) return false;
        if (bu < -kBaryEps || bv < -kBaryEps || bu + bv > 1f + kBaryEps) return false;
        if (Mathf.Abs(tt) > kCacheBand * window) return false;
        t.uses++;
        float w0 = 1f - bu - bv;
        Vector3 n = SurfaceMath.Norm(t.na * w0 + t.nb * bu + t.nc * bv, t.face);
        hit = new SurfaceHit { point = near + u * tt, normal = n, source = t.id, distance = tt };
        return true;
    }

    bool TryMiss(int slot, Vector3 near, Vector3 u, float window)
    {
        ref Miss m = ref misses[slot];
        if (m.version != surfaces.Version) return false;
        float r = kMissRadius * window;
        return (near - m.at).sqrMagnitude < r * r && SurfaceMath.Dot(u, m.up) > 0.999f;
    }
}
