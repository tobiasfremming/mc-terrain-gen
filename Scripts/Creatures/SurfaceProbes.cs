using System;
using System.Runtime.CompilerServices;
using UnityEngine;

// A surface point a creature can stand on, as found by a probe or a cast.
public struct SurfaceHit
{
    public Vector3 point;
    public Vector3 normal;   // the surface's outward unit normal (interpolated vertex normals on a mesh)
    public int source;       // 0 = terrain, else the instance id of the collider that was hit
    public float distance;   // Probe: offset of the point from `near` along up (+ = above near); Cast: distance along the ray

    public const int NoSource = int.MinValue; // "no preference" (collider instance ids may be negative)
}

// Ground for a climbing creature: any surface, along any up. A probe answers the surface NEAREST to `near`
// along `up` (the first one below near when near is in air, the exit above it when near is inside solid -
// FieldGroundProbe's rule, R25), so a body probing from where it stands never jumps to another surface just
// because one is within the window. Generic: terrain (density field or flat), and real collider geometry
// (PhysicsSurfaceProbe) - plants' bark meshes, or anything with a collider in another game.
public interface ISurfaceProbe : IGroundProbe
{
    // preferSource: the source the query's body part stood on last; it wins a near tie (no flip-flop in a
    // corner). slot: a stable per-query index (a joint, a foot) that lets a probe reuse its last answer's
    // triangle or seed; -1 = none.
    bool Probe(Vector3 near, Vector3 up, float window, int preferSource, int slot, out SurfaceHit hit);
    // First front-facing surface along the ray (none if the origin is inside solid).
    bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit);
    // Could a non-terrain surface lie within b? (A cheap gate: false = only terrain there.)
    bool AnySurfaceNear(Bounds b);
}

// A probe with per-slot caches: slots below `count` must exist before a query uses them (grown up front, so
// the per-frame queries never allocate).
public interface ISurfaceSlots { void EnsureSlots(int count); }

// Terrain plus surfaces, for one creature. The terrain part answers exactly as the terrain probe does when up
// is gravity's up (bit-identical: a walking creature is unchanged); for a tilted up it answers along that up:
// within ~20 deg of gravity the terrain's tangent plane at its vertical answer is met along up (exact on
// planes, a sub-millimetre error on dunes, and as cheap as the cache), beyond that the density field itself is
// probed along up - seeded per slot from the slot's last answer (one or two field samples) and found exactly
// (nearest-surface scan within the window) whenever the seed does not hold. The surface part (colliders) is
// only asked while the creature's bounds are near a registered surface (SetActiveBounds), so a creature far
// from any costs one AABB test per sub-step. Of the two answers the nearer to `near` wins; the source the
// query stood on keeps winning within kPrefer x window.
public sealed class CompositeSurfaceProbe : ISurfaceProbe, ISurfaceSlots
{
    const float kVertical = 0.99999f;  // up within ~0.26 deg of gravity: the terrain's own vertical query
    const float kPlaneTilt = 0.94f;    // up within ~20 deg: the tangent plane at the vertical answer
    const float kMinFacing = 0.2f;     // a surface facing the up less than this is not an answer along it
    const float kInsideBand = 0.25f;   // fraction of the window: near this little inside a solid stands on its exit
    const float kPrefer = 0.02f;
    const float kTrustSurface = 0.05f; // fraction of the window: the stood-on surface this close answers alone       // fraction of the window within which the preferred source wins (~10-20 % of a clearance)
    const float kNewtonTol = 1e-3f;    // metres: a seeded root within this of the surface is the answer
    const int kNewtonIter = 4;
    const float kSeedJump = 0.25f;     // fraction of the window a slot's answer may move before it is re-found exactly
    const float kRegrad = 0.05f;       // fraction of the window a slot's answer moves before its normal is re-measured
    const int kRevalidate = 32;        // seeded answers before the exact scan re-checks a slot (staggered by slot)
    const int kScanSteps = 8;          // exact scan: steps over the window
    const float kGradEps = 0.05f;      // central differences, as FieldGroundProbe
    const int kInitialSlots = 64;

    readonly IGroundProbe terrain, terrainSmooth;
    readonly ISurfaceProbe surfaces;   // may be null: terrain only
    readonly DensityField fixedField;  // may be null: tilted terrain probes use the tangent plane
    readonly FieldGroundProbe fieldSource; // when set, the field is read from it (it follows terrain rebuilds)
    DensityField field => fieldSource != null ? fieldSource.Field : fixedField;
    readonly Func<Vector3, Vector3> gravityUp;
    bool surfacesNear = true;

    // Set by the caster (CreatureClimb): its head stands on a collider, so the terrain is only the ground below.
    public bool CasterOnCollider;

    // Off: colliders are ignored (a creature told not to climb walks through plants as before; terrain stays).
    public bool SurfacesEnabled = true;

    // per-slot seeds of the tilted field probe
    Vector3[] seedP = new Vector3[kInitialSlots], seedN = new Vector3[kInitialSlots], seedGradAt = new Vector3[kInitialSlots];
    float[] seedG = new float[kInitialSlots];
    int[] seedUses = new int[kInitialSlots];
    bool[] seedValid = new bool[kInitialSlots];

    // cumulative stats
    public int Queries, TiltedQueries, SeededAnswers, ExactScans, FieldSamples, SurfaceQueries, SurfaceWins;

    public CompositeSurfaceProbe(IGroundProbe terrain, ISurfaceProbe surfaces, DensityField field = null, Func<Vector3, Vector3> gravityUp = null)
        : this(terrain, surfaces, field, null, gravityUp) { }

    // fieldSource: the terrain's shared field probe; its Field (rebound when the terrain rebuilds) is the field.
    public CompositeSurfaceProbe(IGroundProbe terrain, ISurfaceProbe surfaces, FieldGroundProbe fieldSource, Func<Vector3, Vector3> gravityUp = null)
        : this(terrain, surfaces, null, fieldSource, gravityUp) { }

    CompositeSurfaceProbe(IGroundProbe terrain, ISurfaceProbe surfaces, DensityField field, FieldGroundProbe fieldSource, Func<Vector3, Vector3> gravityUp)
    {
        this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        terrainSmooth = terrain is CachedGroundProbe cache ? cache.Smooth : terrain;
        this.surfaces = surfaces;
        fixedField = field; this.fieldSource = fieldSource;
        this.gravityUp = gravityUp;
        Smooth = new SmoothView(this);
        for (int s = 0; s < kInitialSlots; s++) seedUses[s] = (s * 7) % kRevalidate;
    }

    public IGroundProbe Terrain => terrain;
    public ISurfaceProbe Surfaces => surfaces;
    public DensityField Field => field;

    // The same probe with the terrain's smooth view (CachedGroundProbe.Smooth): for the spine.
    public readonly ISurfaceProbe Smooth;

    sealed class SmoothView : ISurfaceProbe
    {
        readonly CompositeSurfaceProbe c;
        public SmoothView(CompositeSurfaceProbe c) { this.c = c; }
        public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
        {
            bool ok = c.ProbeCore(near, up, window, SurfaceHit.NoSource, -1, true, out SurfaceHit h);
            point = h.point; normal = h.normal;
            return ok;
        }
        public bool Probe(Vector3 near, Vector3 up, float window, int preferSource, int slot, out SurfaceHit hit)
            => c.ProbeCore(near, up, window, preferSource, slot, true, out hit);
        public bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit) => c.Cast(origin, dir, maxDist, out hit);
        public bool AnySurfaceNear(Bounds b) => c.AnySurfaceNear(b);
    }

    // The per-creature gate: the surface part is asked only while b (the creature's joints grown by its reach)
    // is near a surface. Until the first call it is asked always.
    public void SetActiveBounds(Bounds b) => surfacesNear = surfaces != null && surfaces.AnySurfaceNear(b);
    public bool SurfacesNear => surfacesNear && SurfacesEnabled && surfaces != null;

    public bool AnySurfaceNear(Bounds b) => surfaces != null && surfaces.AnySurfaceNear(b);

    // Slots must exist before use; grows (allocates) only when a creature with more slots arrives.
    public void EnsureSlots(int count)
    {
        if (surfaces is ISurfaceSlots ss) ss.EnsureSlots(count);
        if (count <= seedP.Length) return;
        int old = seedP.Length;
        Array.Resize(ref seedP, count); Array.Resize(ref seedN, count); Array.Resize(ref seedGradAt, count);
        Array.Resize(ref seedG, count); Array.Resize(ref seedUses, count); Array.Resize(ref seedValid, count);
        for (int s = old; s < count; s++) seedUses[s] = (s * 7) % kRevalidate;
    }

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        bool ok = ProbeCore(near, up, window, SurfaceHit.NoSource, -1, false, out SurfaceHit h);
        point = h.point; normal = h.normal;
        return ok;
    }

    public bool Probe(Vector3 near, Vector3 up, float window, int preferSource, int slot, out SurfaceHit hit)
        => ProbeCore(near, up, window, preferSource, slot, false, out hit);

    bool ProbeCore(Vector3 near, Vector3 up, float window, int prefer, int slot, bool smooth, out SurfaceHit hit)
    {
        Queries++;
        // the walking case, kept as cheap as the plain terrain probe: no collider near, flat gravity, a vertical
        // (unit) up - the terrain's own vertical answer, unchanged
        if (!SurfacesNear && gravityUp == null && up.y >= kVertical)
        {
            bool ok = (smooth ? terrainSmooth : terrain).Probe(near, up, window, out Vector3 fp, out Vector3 fn);
            hit = new SurfaceHit { point = fp, normal = fn, source = 0, distance = fp.y - near.y };
            return ok;
        }
        Vector3 u = SurfaceMath.Norm(up, Vector3.up);
        SurfaceHit sh = default;
        bool sHit = false;
        if (SurfacesNear)
        {
            SurfaceQueries++;
            sHit = surfaces.Probe(near, u, window, prefer, slot, out sh);
            // a collider's face under the terrain (a trunk's bark below the sand it stands in) is no ground: a body
            // that walked down the bark into the sand kept standing on it there, under the ground
            if (sHit && Buried(sh.point, window, smooth)) sHit = false;
            // still on the surface it stood on, right at near: the terrain cannot be meaningfully nearer, and probing
            // it along a climbing joint's tilted up is the costly part (the density field along up)
            if (sHit && prefer == sh.source && Mathf.Abs(sh.distance) < kTrustSurface * window) { SurfaceWins++; hit = sh; return true; }
        }
        bool tHit = TerrainProbe(near, up, u, window, slot, smooth, prefer == 0 || prefer == SurfaceHit.NoSource, out Vector3 tp, out Vector3 tn);
        if (!sHit)
        {
            hit = new SurfaceHit { point = tp, normal = tn, source = 0, distance = SurfaceMath.Dot(tp - near, u) };
            return tHit;
        }
        if (tHit)
        {
            float to = SurfaceMath.Dot(tp - near, u);
            // An answer above near means near is inside that solid (the exit above it): it cannot rest on the other
            // surface below - inside a tree's root shelf the ground under it is no ground. Only when both or neither
            // say so does the nearer one win (the preferred source within the tie band).
            // (just inside: an exit far above - a vertical ray up a trunk's inside - is not what the joint stands on)
            float inBand = kInsideBand * window;
            bool tIn = to > 0f && to < inBand, sIn = sh.distance > 0f && sh.distance < inBand;
            float at = Mathf.Abs(to), asf = Mathf.Abs(sh.distance), band = kPrefer * window;
            if (prefer == 0) at -= band; else if (prefer == sh.source) asf -= band;
            if (tIn != sIn ? tIn : at <= asf)
            {
                hit = new SurfaceHit { point = tp, normal = tn, source = 0, distance = to };
                return true;
            }
        }
        SurfaceWins++;
        hit = sh;
        return true;
    }

    // ---- terrain ----

    const float kBuriedTol = 0.05f;    // fraction of the window: a collider point this far under the terrain is buried

    // p lies under the terrain along gravity (the terrain's own vertical answer above it).
    bool Buried(Vector3 p, float window, bool smooth)
    {
        IGroundProbe t = smooth ? terrainSmooth : terrain;
        Vector3 g = Gravity(p);
        if (!t.Probe(p, g, window, out Vector3 tp, out _)) return false;
        return SurfaceMath.Dot(tp - p, g) > kBuriedTol * window;
    }

    Vector3 Gravity(Vector3 p) => gravityUp != null ? SurfaceMath.Norm(gravityUp(p), Vector3.up) : Vector3.up;

    // `up` is passed on unchanged on the vertical path (bit-identical to the terrain probe); u is its unit copy.
    // onTerrain: the query's body part stood on the terrain (or no preference): only then is a steep tilted up
    // answered by the density field itself (climbing a terrain wall); a part standing on a collider (a trunk) needs
    // the terrain only as the ground near its foot, which the tangent plane answers (the field there cost ~10
    // samples a query for the joints swapping between bark and sand).
    bool TerrainProbe(Vector3 near, Vector3 up, Vector3 u, float window, int slot, bool smooth, bool onTerrain, out Vector3 p, out Vector3 n)
    {
        IGroundProbe t = smooth ? terrainSmooth : terrain;
        Vector3 g = Gravity(near);
        float c = SurfaceMath.Dot(u, g);
        if (c >= kVertical) return t.Probe(near, up, window, out p, out n);
        TiltedQueries++;
        if (field != null && c < kPlaneTilt && onTerrain) return FieldAlong(near, u, window, slot, out p, out n);
        // the terrain's tangent plane under near, met along up - from the exact terrain: the smooth view rounds a
        // step (a cliff's or a dug hole's edge) into an S that is not the ground, and a climbing body following
        // its tangent plane over the edge was pulled into it
        if (terrain.Probe(near, g, window, out Vector3 q, out Vector3 m))
        {
            float facing = SurfaceMath.Dot(m, u);
            if (facing >= kMinFacing)
            {
                float tt = SurfaceMath.Dot(q - near, m) / facing;
                if (Mathf.Abs(tt) <= window) { p = near + u * tt; n = m; return true; }
            }
        }
        if (field != null && onTerrain) return FieldAlong(near, u, window, slot, out p, out n);
        p = near; n = u;
        return false;
    }

    // The density field along a tilted up: the slot's last answer seeds a Newton iteration along up (the line
    // meets the seed's tangent plane, then f / |grad f . up| steps); the exact nearest-surface scan answers when
    // there is no seed, it moved too far, it did not converge, or the slot is due for a re-check.
    bool FieldAlong(Vector3 near, Vector3 u, float window, int slot, out Vector3 p, out Vector3 n)
    {
        if (slot >= 0 && slot < seedValid.Length && seedValid[slot] && ++seedUses[slot] < kRevalidate
            && Seeded(slot, near, u, window, out p, out n))
        {
            SeededAnswers++;
            return true;
        }
        ExactScans++;
        if (!FieldScan(near, u, window, out p))
        {
            if (slot >= 0 && slot < seedValid.Length) seedValid[slot] = false;
            n = u;
            return false;
        }
        Vector3 grad = field.Gradient(p, kGradEps, 0f);
        FieldSamples += 6;
        float gm = grad.magnitude;
        n = gm > 1e-12f ? -grad / gm : u;
        if (slot >= 0 && slot < seedValid.Length)
        {
            seedValid[slot] = gm > 1e-6f; seedP[slot] = p; seedN[slot] = n; seedG[slot] = gm; seedGradAt[slot] = p; seedUses[slot] = 0;
        }
        return true;
    }

    bool Seeded(int slot, Vector3 near, Vector3 u, float window, out Vector3 p, out Vector3 n)
    {
        Vector3 s = seedP[slot], sn = seedN[slot];
        float gm = seedG[slot];
        float denom = SurfaceMath.Dot(sn, u);
        p = near; n = u;
        if (denom < kMinFacing) return false;
        float t = SurfaceMath.Dot(s - near, sn) / denom;
        if (Mathf.Abs(t) > window) return false;
        Vector3 q = near + u * t;
        if ((q - s).sqrMagnitude > kSeedJump * kSeedJump * window * window) return false;
        float slope = gm * denom; // |d f / d t| along up
        for (int it = 0; it < kNewtonIter; it++)
        {
            float f = field.Sample(q);
            FieldSamples++;
            if (Mathf.Abs(f) <= kNewtonTol * gm)
            {
                p = q;
                if ((q - seedGradAt[slot]).sqrMagnitude > kRegrad * kRegrad * window * window)
                {
                    Vector3 grad = field.Gradient(q, kGradEps, 0f);
                    FieldSamples += 6;
                    float m = grad.magnitude;
                    if (m > 1e-6f) { sn = -grad / m; seedG[slot] = m; }
                    seedGradAt[slot] = q;
                }
                n = sn;
                seedP[slot] = q; seedN[slot] = sn;
                return true;
            }
            t += f / slope; // positive density = solid: inside, move out along up
            if (Mathf.Abs(t) > window) return false;
            q = near + u * t;
        }
        return false;
    }

    // Nearest surface along u within the window: down through air to the first solid, or up through solid to
    // the first air; the bracketing step refined by Illinois false position.
    bool FieldScan(Vector3 near, Vector3 u, float window, out Vector3 p)
    {
        float f0 = field.Sample(near);
        FieldSamples++;
        bool air = f0 <= 0f;
        float dir = air ? -1f : 1f, step = window / kScanSteps;
        float prevT = 0f, fPrev = f0;
        for (int k = 1; k <= kScanSteps; k++)
        {
            float t = k * step;
            float f = field.Sample(near + u * (dir * t));
            FieldSamples++;
            if ((f > 0f) == air)
            {
                // bracket [prevT, t] along dir: air at one end, solid at the other
                float ta = prevT, tb = t, fa = fPrev, fb = f;
                float tt = 0.5f * (ta + tb);
                int side = 0;
                for (int i = 0; i < 8; i++)
                {
                    tt = (ta * fb - tb * fa) / (fb - fa);
                    if (!(tt > Mathf.Min(ta, tb) && tt < Mathf.Max(ta, tb))) tt = 0.5f * (ta + tb);
                    float ft = field.Sample(near + u * (dir * tt));
                    FieldSamples++;
                    if (Mathf.Abs(ft) < 1e-5f || Mathf.Abs(tb - ta) < 1e-4f) break;
                    if ((ft > 0f) == (fa > 0f)) { ta = tt; fa = ft; if (side < 0) fb *= 0.5f; side = -1; }
                    else { tb = tt; fb = ft; if (side > 0) fa *= 0.5f; side = 1; }
                }
                p = near + u * (dir * tt);
                return true;
            }
            prevT = t; fPrev = f;
        }
        p = near;
        return false;
    }

    // ---- casts ----

    public bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit)
    {
        Vector3 d = SurfaceMath.Norm(dir, Vector3.forward);
        bool th = TerrainCast(origin, d, maxDist, out Vector3 tp, out Vector3 tn, out float tt);
        SurfaceHit sh = default;
        bool sHit = SurfacesNear && surfaces.Cast(origin, d, maxDist, out sh);
        if (sHit && (!th || sh.distance < tt)) { hit = sh; return true; }
        hit = new SurfaceHit { point = th ? tp : origin, normal = th ? tn : -d, source = 0, distance = th ? tt : maxDist };
        return th;
    }

    bool TerrainCast(Vector3 o, Vector3 d, float maxDist, out Vector3 p, out Vector3 n, out float t)
    {
        p = o; n = -d; t = maxDist;
        // a ray that cannot reach down to the terrain under its origin (high up a trunk) is not marched through the
        // field: a read-only look at the ground cache's height there
        // (only while the caster stands on a collider: when the terrain itself is the wall - a cliff - its height under
        // the origin says nothing about the wall beside it)
        if (CasterOnCollider && gravityUp == null && terrain is CachedGroundProbe cache && cache.Peek(o.x, o.z, out float gy) && o.y - gy > maxDist + 0.5f)
            return false;
        if (field != null) return FieldCast(o, d, maxDist, out p, out n, out t);
        // the terrain's tangent plane under the origin (exact for a flat ground)
        Vector3 g = Gravity(o);
        if (!terrain.Probe(o, g, Mathf.Max(maxDist, 1f), out Vector3 q, out Vector3 m)) return false;
        float denom = SurfaceMath.Dot(d, m);
        if (denom > -1e-4f) return false;
        float tt = SurfaceMath.Dot(q - o, m) / denom;
        if (tt < 0f || tt > maxDist) return false;
        p = o + d * tt; n = m; t = tt;
        return true;
    }

    bool FieldCast(Vector3 o, Vector3 d, float maxDist, out Vector3 p, out Vector3 n, out float t)
    {
        p = o; n = -d; t = maxDist;
        float f0 = field.Sample(o);
        FieldSamples++;
        if (f0 > 0f) return false; // origin inside the ground
        float step = Mathf.Clamp(maxDist / 8f, 0.05f, 0.5f);
        float ta = 0f, fa = f0;
        while (ta < maxDist)
        {
            float tb = Mathf.Min(ta + step, maxDist);
            float fb = field.Sample(o + d * tb);
            FieldSamples++;
            if (fb > 0f)
            {
                float tt = tb;
                for (int i = 0; i < 6; i++)
                {
                    tt = (ta * fb - tb * fa) / (fb - fa);
                    if (!(tt > ta && tt < tb)) tt = 0.5f * (ta + tb);
                    float ft = field.Sample(o + d * tt);
                    FieldSamples++;
                    if (Mathf.Abs(ft) < 1e-5f || tb - ta < 1e-4f) break;
                    if (ft > 0f) { tb = tt; fb = ft; } else { ta = tt; fa = ft; }
                }
                p = o + d * tt; t = tt;
                Vector3 grad = field.Gradient(p, kGradEps, 0f);
                FieldSamples += 6;
                n = SurfaceMath.Norm(-grad, -d);
                return true;
            }
            ta = tb; fa = fb;
        }
        return false;
    }
}

// Small allocation-free vector helpers for the climbing code (unit-vector slerp and turn-toward without the
// engine calls, smoothstep, ray-triangle).
public static class SurfaceMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Norm(Vector3 v, Vector3 fallback)
    {
        float m = v.x * v.x + v.y * v.y + v.z * v.z;
        if (!(m > 1e-12f)) return fallback;
        float inv = 1f / Mathf.Sqrt(m);
        return new Vector3(v.x * inv, v.y * inv, v.z * inv);
    }

    public static float SmoothStep(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // Angle between unit vectors, degrees.
    public static float AngleDeg(Vector3 a, Vector3 b) => Mathf.Acos(Mathf.Clamp(Dot(a, b), -1f, 1f)) * Mathf.Rad2Deg;

    // Spherical interpolation of unit vectors (t in 0..1). Antiparallel inputs turn about any perpendicular.
    public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        float c = Mathf.Clamp(Dot(a, b), -1f, 1f);
        if (c > 0.9995f) return Norm(a + (b - a) * t, b);
        float th = Mathf.Acos(c);
        Vector3 perp = b - a * c;
        float pm = perp.magnitude;
        if (pm < 1e-6f)
        {
            perp = Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right);
            pm = perp.magnitude;
        }
        perp /= pm;
        float ang = th * t;
        return Norm(a * Mathf.Cos(ang) + perp * Mathf.Sin(ang), b);
    }

    // a turned toward b by at most maxRad (unit vectors).
    public static Vector3 RotateTowards(Vector3 a, Vector3 b, float maxRad)
    {
        float c = Mathf.Clamp(Dot(a, b), -1f, 1f);
        float th = Mathf.Acos(c);
        if (th <= maxRad || th < 1e-7f) return b;
        return Slerp(a, b, maxRad / th);
    }

    // v turned by the minimal rotation that takes unit `from` onto unit `to` (parallel transport of a tangent).
    public static Vector3 Transport(Vector3 v, Vector3 from, Vector3 to)
    {
        Vector3 axis = Cross(from, to);
        float s = axis.magnitude, c = Dot(from, to);
        if (s < 1e-7f) return c > 0f ? v : v - from * (2f * Dot(v, from)); // antiparallel: mirror the normal part
        axis /= s;
        float ad = Dot(axis, v) * (1f - c);
        return new Vector3(v.x * c + (axis.y * v.z - axis.z * v.y) * s + axis.x * ad,
                           v.y * c + (axis.z * v.x - axis.x * v.z) * s + axis.y * ad,
                           v.z * c + (axis.x * v.y - axis.y * v.x) * s + axis.z * ad);
    }

    // The line o + t d against triangle (a, b, c) (Moller-Trumbore, both directions of the line). Returns
    // false for a line parallel to the plane. u, v are the barycentric weights of b and c.
    public static bool LineTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
    {
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 pv = Cross(d, e2);
        float det = Dot(e1, pv);
        t = u = v = 0f;
        if (Mathf.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        Vector3 tv = o - a;
        u = Dot(tv, pv) * inv;
        Vector3 qv = Cross(tv, e1);
        v = Dot(d, qv) * inv;
        t = Dot(e2, qv) * inv;
        return true;
    }
}
