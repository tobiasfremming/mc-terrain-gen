using System;
using UnityEngine;

// Wrapping an arm around a branch on the REAL surface (tree octopus design 2.4). Every pin comes from an
// ISurfaceProbe answer on real geometry; the local cylinder fit (axis, radius) only steers the march and
// decides wrap vs spread.
//
//   EstimateAxis   two extra probes at small tangent offsets give the normal's change along two tangents;
//                  on a tube the change is along the circumference c: axis = n x c, radius = h / |dn|. A third
//                  probe along the axis checks it (a bent or non-tubular surface lowers the confidence).
//   Plan           wrap when one turn fits in WrapReach x the remaining arm: 2 pi (r + armRadius) <= 0.6 x
//                  remaining; else spread (pins along the arm's own line, pressed onto the surface).
//   Wrap march     from the contact (the tip) backwards along a helix (pitch WrapPitchDeg) around the
//                  estimated axis, one arm spacing per step; each step's helix point is snapped to the real
//                  surface by Probe along the local radial direction and becomes a pin at hit + n x armRadius;
//                  the axis is re-estimated every ReestimateEvery pins at the latest hit; a miss ends the wrap.
// Output: Count pins (particle index, point, normal, source), tip first. Scratch arrays, no allocation per call.
public sealed class WrapMarch
{
    public const int ReestimateEvery = 6;
    public const float AxisOffsetRadii = 1.5f;   // tangent probe offset, x arm radius
    public const float MinConfidence = 0.5f;

    public readonly int[] Particle;
    public readonly Vector3[] Point, Normal;
    public readonly int[] Source;
    public int Count { get; private set; }
    public bool Spread { get; private set; }
    public Vector3 Axis { get; private set; }
    public float Radius { get; private set; }
    public float Confidence { get; private set; }
    public float WrapTurns { get; private set; }
    public int Probes;   // cumulative

    readonly int capacity;
    readonly float[] turnsAt;   // cumulative turns at each wrap pin

    public WrapMarch(int capacity)
    {
        this.capacity = capacity;
        Particle = new int[capacity]; Point = new Vector3[capacity]; Normal = new Vector3[capacity]; Source = new int[capacity];
        turnsAt = new float[capacity];
    }

    // Local tube fit at a surface hit. False when the probes miss (no surface around the hit).
    public bool EstimateAxis(ISurfaceProbe probe, SurfaceHit hit, float armRadius, int slot,
                             out Vector3 axis, out float radius, out float confidence)
    {
        Vector3 n = OctoMath.Norm(hit.normal, Vector3.up);
        Vector3 t1 = OctoMath.Norm(OctoMath.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.right);
        Vector3 t2 = OctoMath.Cross(n, t1);
        float h = AxisOffsetRadii * armRadius, win = 4f * armRadius;
        axis = t1; radius = float.PositiveInfinity; confidence = 0f;
        if (!ProbeAt(probe, hit.point + t1 * h, n, win, hit.source, slot, out SurfaceHit a)) return false;
        if (!ProbeAt(probe, hit.point + t2 * h, n, win, hit.source, slot, out SurfaceHit b)) return false;
        Vector3 d1 = a.normal - n, d2 = b.normal - n;
        float m1 = d1.magnitude, m2 = d2.magnitude, dn = Mathf.Sqrt(m1 * m1 + m2 * m2);
        if (dn < 1e-4f)
        {
            // flat within the probe span: a wall or a thick trunk; any tangent will do as "axis"
            axis = t1; radius = float.PositiveInfinity; confidence = 1f;
            return true;
        }
        Vector3 c = m1 >= m2 ? d1 : d2;                       // the circumferential direction (sign free)
        c = OctoMath.Norm(c - n * OctoMath.Dot(c, n), t1);
        axis = OctoMath.Norm(OctoMath.Cross(n, c), t2);
        // exact for a circle: the probe at tangent offset h lands at angle phi = asin(h / r) round the tube,
        // where the normal has turned by phi: |dn| = 2 sin(phi / 2)
        float phi = 2f * Mathf.Asin(Mathf.Min(dn, 2f) * 0.5f);
        radius = h / Mathf.Max(Mathf.Sin(Mathf.Min(phi, 0.5f * Mathf.PI)), 1e-4f);
        // check along the axis: on a tube the normal does not change that way
        confidence = 1f;
        if (ProbeAt(probe, hit.point + axis * h, n, win, hit.source, slot, out SurfaceHit e))
        {
            float along = (e.normal - n).magnitude;
            confidence = Mathf.Clamp01(1f - along / Mathf.Max(dn, 1e-4f));
        }
        else confidence = 0.25f;
        return true;
    }

    bool ProbeAt(ISurfaceProbe probe, Vector3 at, Vector3 up, float window, int prefer, int slot, out SurfaceHit hit)
    {
        Probes++;
        return probe.Probe(at + up * (0.5f * window), up, window, prefer, slot, out hit);
    }

    // Plans pins for the arm, contact at particle `tip` (usually N-1), going back toward the root.
    //   tipDir      the arm's direction at the tip (decides the wrap's sense: the tip leads around the branch)
    //   rootSide    a point on the root side (the coil advances along the axis toward it)
    //   minFree     particles nearest the root that must stay free
    public void Plan(ISurfaceProbe probe, TentacleChain arm, SurfaceHit contact, int tip, Vector3 tipDir, Vector3 rootSide,
                     int minFree, int slot)
    {
        Count = 0; Spread = false; WrapTurns = 0f;
        TentacleSpec spec = arm.Spec;
        float scale = arm.SizeScale, sp = arm.Spacing;
        int available = Mathf.Min(capacity, tip - minFree + 1);
        if (available <= 0) return;
        float armR = spec.RadiusAt(spec.Spacing * tip) * scale;
        float rootR = spec.RootRadius * scale;
        bool fit = EstimateAxis(probe, contact, Mathf.Max(armR, 0.5f * rootR), slot, out Vector3 axis, out float r, out float conf);
        Axis = axis; Radius = r; Confidence = conf;
        float remaining = available * sp;
        bool wrap = fit && !float.IsInfinity(r) && conf >= MinConfidence
                    && 2f * Mathf.PI * (r + armR) <= spec.WrapReach * remaining && r > 0.5f * armR;
        if (!wrap) { PlanSpread(probe, arm, contact, tip, tipDir, available, slot); return; }
        PlanWrap(probe, arm, contact, tip, tipDir, rootSide, available, axis, r, armR, slot);
    }

    // Spread on a thick trunk or a wall: from the contact back toward the root, one spacing at a time along
    // the surface (the arm's incoming direction made tangent), each step pressed onto the real surface.
    void PlanSpread(ISurfaceProbe probe, TentacleChain arm, SurfaceHit contact, int tip, Vector3 tipDir, int available, int slot)
    {
        Spread = true;
        Vector3 n = OctoMath.Norm(contact.normal, Vector3.up);
        TentacleSpec spec = arm.Spec;
        int count = Mathf.Min(available, Mathf.Max(3, (int)(0.3f * arm.N)));
        Vector3 back = -tipDir;
        Vector3 at = contact.point;
        SurfaceHit h = contact;
        for (int i = 0; i < count; i++)
        {
            int k = tip - i;
            float rk = spec.RadiusAt(spec.Spacing * k) * arm.SizeScale;
            if (i > 0)
            {
                Vector3 dir = OctoMath.Norm(back - n * OctoMath.Dot(back, n), OctoMath.Cross(n, Vector3.right));
                if (!ProbeAt(probe, at + dir * arm.Spacing, n, 3f * rk + arm.Spacing, contact.source, slot, out h)) break;
            }
            Add(k, h.point + h.normal * rk, h.normal, h.source);
            at = h.point;
            n = OctoMath.Norm(h.normal, n);
        }
    }

    void PlanWrap(ISurfaceProbe probe, TentacleChain arm, SurfaceHit contact, int tip, Vector3 tipDir, Vector3 rootSide,
                  int available, Vector3 axis, float r, float armR0, int slot)
    {
        TentacleSpec spec = arm.Spec;
        float sp = arm.Spacing, pitch = spec.WrapPitchDeg * Mathf.Deg2Rad;
        Vector3 n0 = OctoMath.Norm(contact.normal, Vector3.up);
        // the branch centre line through the contact, the coil's sense and its axial advance
        Vector3 centre = contact.point - n0 * r;
        Vector3 c0 = OctoMath.Cross(axis, n0);                        // circumferential at the contact
        float sense = OctoMath.Dot(tipDir, c0) >= 0f ? 1f : -1f;      // tip leads in +sense; we march back: -sense
        float advance = OctoMath.Dot(rootSide - contact.point, axis) >= 0f ? 1f : -1f;
        Add(tip, contact.point + n0 * armR0, n0, contact.source);
        turnsAt[0] = 0f;
        float angle = 0f, axial = 0f, total = 0f;
        Vector3 lastHit = contact.point;
        int since = 0;
        for (int i = 1; i < available; i++)
        {
            int k = tip - i;
            float rk = spec.RadiusAt(spec.Spacing * k) * arm.SizeScale;
            float R = r + rk;
            // one spacing along a helix of radius R and pitch p: dTheta = sp cos p / R, rise = sp sin p
            float dTheta = sp * Mathf.Cos(pitch) / R;
            angle += -sense * dTheta;
            axial += advance * sp * Mathf.Sin(pitch);
            Vector3 radial = OctoMath.Rotate(n0, axis, angle);
            Vector3 guess = centre + axis * axial + radial * R;
            // snap to the real surface along the radial direction
            if (!ProbeAt(probe, guess, radial, 2f * R, contact.source, slot, out SurfaceHit h)) break;
            if (OctoMath.Dot(h.normal, radial) < 0.3f) break;   // not the branch any more (a fork, an edge)
            Add(k, h.point + h.normal * rk, h.normal, h.source);
            lastHit = h.point;
            total += dTheta;
            WrapTurns = total / (2f * Mathf.PI);
            turnsAt[Count - 1] = WrapTurns;
            if (++since >= ReestimateEvery)
            {
                since = 0;
                if (EstimateAxis(probe, h, rk, slot, out Vector3 ax2, out float r2, out float conf2) && conf2 >= MinConfidence && !float.IsInfinity(r2))
                {
                    // keep the axis' sign and re-centre on the latest hit; the angle continues from there
                    if (OctoMath.Dot(ax2, axis) < 0f) ax2 = -ax2;
                    axis = ax2; r = r2;
                    n0 = h.normal; centre = h.point - n0 * r; angle = 0f; axial = 0f;
                }
            }
        }
        Axis = axis; Radius = r;
        // leave the branch on the side that faces the root: the free run from the root to the coil must not
        // cut through the branch (trim trailing pins whose surface faces away from the root)
        while (Count > 1 && OctoMath.Dot(Normal[Count - 1], rootSide - Point[Count - 1]) < 0f) Count--;
        WrapTurns = Count > 0 ? turnsAt[Count - 1] : 0f;
    }

    void Add(int k, Vector3 p, Vector3 n, int source)
    {
        if (Count >= capacity) return;
        Particle[Count] = k; Point[Count] = p; Normal[Count] = n; Source[Count] = source;
        Count++;
    }

    // A wrap around an analytic capsule (prey): no probes, the same helix. Writes pins tip first.
    public void PlanAroundCapsule(TentacleChain arm, int tip, Vector3 a, Vector3 b, float capsuleRadius, Vector3 contactDir, float tipSense, int available)
    {
        Count = 0; Spread = false;
        Vector3 axis = OctoMath.Norm(b - a, Vector3.up);
        float len = (b - a).magnitude;
        Vector3 mid = 0.5f * (a + b);
        Vector3 n0 = OctoMath.Norm(contactDir - axis * OctoMath.Dot(contactDir, axis), OctoMath.Cross(axis, Vector3.right));
        TentacleSpec spec = arm.Spec;
        float sp = arm.Spacing, pitch = spec.WrapPitchDeg * Mathf.Deg2Rad, angle = 0f, axial = 0f;
        available = Mathf.Min(available, capacity);
        for (int i = 0; i < available; i++)
        {
            int k = tip - i;
            float rk = spec.RadiusAt(spec.Spacing * k) * arm.SizeScale, R = capsuleRadius + rk;
            if (i > 0) { angle += -tipSense * sp * Mathf.Cos(pitch) / R; axial += sp * Mathf.Sin(pitch); }
            if (Mathf.Abs(axial) > 0.5f * len) break;
            Vector3 radial = OctoMath.Rotate(n0, axis, angle);
            Add(k, mid + axis * axial + radial * R, radial, SurfaceHit.NoSource);
        }
        WrapTurns = Mathf.Abs(angle) / (2f * Mathf.PI);
    }
}
