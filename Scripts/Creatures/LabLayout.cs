using System.Collections.Generic;
using UnityEngine;

// Where the creature lab puts its enclosures (CreatureLab.Place, the creature kit's lab check): slots on a gentle
// arc (or ring) round the player, each drawn to the flattest of a few nearby spots, never within kEnclosureGap of a
// neighbour. Pure geometry over an IGroundProbe (no GameObjects), so the offline harness runs it too
// (CreatureKitLabTests.Lab_Placement_NeighboursNeverOverlap with every roster entry).
public static class LabLayout
{
    public const int kCandidates = 16, kRimProbes = 8;
    public const float kProbeWindow = 6f;
    const float kGoldenAngle = 2.39996323f;
    public const float kViewGap = 4f;          // the player starts this far outside the nearest curb, facing it
    public const float kEnclosureGap = 4f;     // free ground between neighbouring curbs
    // pushes of a crowded slot out of its neighbours (a full roster wraps the arc: one push can land in the next)
    const int kPushPasses = 16;

    // The arc for enclosures of these radii: neighbours a slot apart never closer than two of the largest radii
    // plus the gap, and the arc outside the player's spot.
    public static void Arc(float[] radius, float spacing, float arcRadius, float searchRadius, out float slotSpacing, out float slotRadius)
    {
        float maxR = 0f;
        for (int k = 0; k < radius.Length; k++) if (radius[k] > 0f) maxR = Mathf.Max(maxR, radius[k]);
        slotSpacing = Mathf.Max(spacing, 2f * maxR + kEnclosureGap);
        slotRadius = Mathf.Max(arcRadius, maxR + kViewGap + searchRadius);
    }

    // The ground centres of enclosures k = 0..n-1 (radius[k] <= 0: none, its slot stays empty), each the flattest
    // free spot near its arc slot; `placed` collects (centre, radius) of every one placed (pass the lab's list).
    public static Vector3[] Centres(IGroundProbe ground, Vector3 origin, Vector3 fwd, Vector3 up, float[] radius,
                                    float spacing, float arcRadius, float searchRadius, float[] flatness, List<Vector4> placed)
    {
        ground = LabGround.Wrap(ground);
        int n = radius.Length;
        Arc(radius, spacing, arcRadius, searchRadius, out float slotSpacing, out float slotRadius);
        float[] ring = Wraps(n, slotSpacing, slotRadius) ? RingAngles(radius, spacing, slotRadius, out slotRadius) : null;
        var c = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            if (!(radius[k] > 0f)) continue;
            Vector3 slot = ring == null ? ArcSlot(origin, fwd, up, k, n, slotSpacing, slotRadius)
                : origin + Quaternion.AngleAxis(ring[k] * Mathf.Rad2Deg, up) * Vector3.ProjectOnPlane(fwd, up).normalized * slotRadius;
            c[k] = FlattestSpot(ground, slot, up, radius[k], searchRadius, placed, out float flat);
            if (flatness != null) flatness[k] = flat;
            placed?.Add(new Vector4(c[k].x, c[k].y, c[k].z, radius[k]));
        }
        return c;
    }

    // A full roster with a few big enclosures (aviaries, the big lizards) spaces every slot by two of the LARGEST
    // radii, and the evenly spaced arc then wraps past a full turn: later slots land on earlier ones and the pushes
    // can leave an enclosure on the player's spot (2026-10-19, the crocodile at the end of 12 enclosures: 772 deg).
    // Such a roster is laid on a ring instead: each pair of neighbours spaced by their OWN radii (plus the gap, the
    // lab's spacing at least), the ring's radius grown until the whole roster fits in kMaxSweepDeg. A roster whose
    // arc does not wrap keeps the arc exactly.
    public const float kMaxSweepDeg = 300f;

    public static bool Wraps(int n, float slotSpacing, float slotRadius)
    {
        float step = 2f * Mathf.Asin(Mathf.Clamp(slotSpacing / (2f * slotRadius), -1f, 1f));
        return (n - 1) * step > kMaxSweepDeg * Mathf.Deg2Rad;
    }

    // Each enclosure's angle (radians, centred on fwd) on the ring and the ring's radius (>= minRadius).
    public static float[] RingAngles(float[] radius, float spacing, float minRadius, out float ringRadius)
    {
        int n = radius.Length;
        var arc = new float[n];
        float s = 0f; int prev = -1;
        for (int k = 0; k < n; k++)
        {
            if (!(radius[k] > 0f)) continue;
            // a chord is a little shorter than its arc: 6 % spare keeps the curbs kEnclosureGap apart at any radius
            if (prev >= 0) s += 1.06f * Mathf.Max(spacing, radius[prev] + radius[k] + kEnclosureGap);
            arc[k] = s; prev = k;
        }
        ringRadius = Mathf.Max(minRadius, s / (kMaxSweepDeg * Mathf.Deg2Rad));
        var angle = new float[n];
        for (int k = 0; k < n; k++) angle[k] = (arc[k] - 0.5f * s) / ringRadius;
        return angle;
    }

    // The k-th of n slots on an arc of radius arcRadius around origin, centred on fwd; adjacent slots are
    // `spacing` apart (chord length).
    public static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, Vector3 up, int k, int n, float spacing, float arcRadius)
    {
        float step = 2f * Mathf.Asin(Mathf.Clamp(spacing / (2f * arcRadius), -1f, 1f));
        float angle = (k - 0.5f * (n - 1)) * step;
        Vector3 dir = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, up) * Vector3.ProjectOnPlane(fwd, up).normalized;
        return origin + dir * arcRadius;
    }

    // The flattest of kCandidates spots (sunflower pattern) within searchRadius of slot: flatness is the
    // lowest dot(normal, up) over kRimProbes probes on the enclosure rim. Candidates that would bring the curb
    // within kEnclosureGap of an enclosure already placed (xyz centre, w radius) are skipped: neighbouring
    // slots are closer than two search radii, so two enclosures drawn to the same flat patch overlapped. If
    // every candidate is too close, the slot is pushed out of its neighbours. Returns the spot's ground point.
    public static Vector3 FlattestSpot(IGroundProbe ground, Vector3 slot, Vector3 up, float radius, float searchRadius,
                                       List<Vector4> placed, out float best)
    {
        ground = LabGround.Wrap(ground);
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        best = -2f;
        Vector3 bestP = slot;
        bool found = false;
        for (int i = 0; i < kCandidates; i++)
        {
            float r = searchRadius * Mathf.Sqrt(i / (float)kCandidates), a = i * kGoldenAngle;
            Vector3 c = slot + t0 * (r * Mathf.Cos(a)) + t1 * (r * Mathf.Sin(a));
            if (Crowds(c, up, radius, placed) >= 0) continue;
            if (!ground.Probe(c, up, kProbeWindow, out Vector3 cg, out _)) continue;
            float flat = 2f;
            for (int j = 0; j < kRimProbes; j++)
            {
                float b = j * 2f * Mathf.PI / kRimProbes;
                Vector3 q = cg + (t0 * Mathf.Cos(b) + t1 * Mathf.Sin(b)) * radius;
                flat = ground.Probe(q, up, kProbeWindow, out _, out Vector3 nrm) ? Mathf.Min(flat, Vector3.Dot(nrm, up)) : -1f;
                if (flat < best) break;
            }
            if (flat > best) { best = flat; bestP = cg; found = true; }
        }
        if (found) return bestP;
        // no free candidate answered: push the slot clear of every enclosure it crowds, then ground it
        Vector3 p = slot;
        for (int pass = 0; pass < kPushPasses; pass++)
        {
            int k = Crowds(p, up, radius, placed);
            if (k < 0) break;
            Vector3 c = placed[k], d = Vector3.ProjectOnPlane(p - c, up);
            if (d.sqrMagnitude < 1e-6f) d = t0;
            p = c + Vector3.Project(p - c, up) + d.normalized * (placed[k].w + radius + kEnclosureGap + 0.01f);
        }
        if (ground.Probe(p, up, kProbeWindow, out Vector3 pg, out Vector3 pn)) { p = pg; best = Vector3.Dot(pn, up); }
        return p;
    }

    // Index of the first placed enclosure a curb of the given radius centred at c would come within
    // kEnclosureGap of (planar), or -1.
    public static int Crowds(Vector3 c, Vector3 up, float radius, List<Vector4> placed)
    {
        if (placed == null) return -1;
        for (int k = 0; k < placed.Count; k++)
        {
            Vector4 e = placed[k];
            Vector3 d = Vector3.ProjectOnPlane(c - (Vector3)e, up);
            float need = e.w + radius + kEnclosureGap;
            if (d.sqrMagnitude < need * need) return k;
        }
        return -1;
    }

    // A free spot for an enclosure placed after the others (a grove): the flattest spot near slot (FlattestSpot,
    // searching searchRadius), and while that still crowds an enclosure already placed (every candidate was taken
    // and the push could not clear them all: a grove slot that lands on the lab's ring), the same search a step
    // further out along slot - origin, up to kFreeTries steps. Returns the ground point.
    public const int kFreeTries = 24;
    public static Vector3 FreeSpot(IGroundProbe ground, Vector3 origin, Vector3 slot, Vector3 up, float radius, float searchRadius,
                                   List<Vector4> placed, out float flatness)
    {
        Vector3 dir = Vector3.ProjectOnPlane(slot - origin, up);
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        Vector3 c = slot;
        flatness = -2f;
        for (int i = 0; i < kFreeTries; i++)
        {
            c = FlattestSpot(ground, slot + dir * (i * (radius + kEnclosureGap)), up, radius, searchRadius, placed, out flatness);
            if (Crowds(c, up, radius, placed) < 0) return c;
        }
        return c;
    }

    // True when p is at least `clearance` outside every placed curb (planar): a spot for the player.
    public static bool Clear(Vector3 p, Vector3 up, float clearance, List<Vector4> placed)
    {
        if (placed == null) return true;
        for (int k = 0; k < placed.Count; k++)
        {
            Vector4 e = placed[k];
            if (Vector3.ProjectOnPlane(p - (Vector3)e, up).magnitude < e.w + clearance) return false;
        }
        return true;
    }
}

// The lab's ground: the terrain surface in the column through a point, wherever that point is. The creature probes
// (FieldGroundProbe) search a window and then 16 m down through air or 8 m up through solid from the point they are
// given - right for a body that stands on the ground, wrong for the lab, which asks at its slots on the player's
// height: on the 2026-10-19 ring (enclosures ~70 m out) the DesertWorld dunes stand 8-11 m above the player's
// spot, every probe there missed, and those enclosures, their trees, rocks and animals were left at the player's
// height, under the sand. Here a probe that misses scans the column top-down from kColumnSpan above the point, each
// step inside the inner probe's 16 m fall: the first surface found is the column's top (a heightfield's only one).
// Every placement the lab makes (enclosure centres and rims, curbs, posts, trees, rocks, the player's spot, the
// groves) goes through it; the creatures' own probes are untouched.
public sealed class LabGround : IGroundProbe
{
    public const float kColumnSpan = 96f, kColumnStep = 12f, kScanWindow = 1f;
    readonly IGroundProbe inner;
    LabGround(IGroundProbe inner) { this.inner = inner; }

    public static IGroundProbe Wrap(IGroundProbe g) => g == null || g is LabGround ? g : new LabGround(g);

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        if (inner.Probe(near, up, window, out point, out normal)) return true;
        up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        for (float t = kColumnSpan; t >= -kColumnSpan; t -= kColumnStep)
            if (inner.Probe(near + up * t, up, kScanWindow, out point, out normal)) return true;
        point = near; normal = up;
        return false;
    }
}
