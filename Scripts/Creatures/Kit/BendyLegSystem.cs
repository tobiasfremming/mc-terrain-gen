// Creature kit (2026-10-20, the GolfballStrider): BendyLegSystem - the flex of every BendyLeg of one individual, one
// system in flat arrays (owned by the species' first BendyLeg). CreatureLegs solves each leg's IK pose (its points:
// coxa, joints, ankle, foot) and writes its bones; this runtime runs after (stage AfterLimbs, every sub-step) and
// draws the leg FLEXED: each point past the coxa carries a deviation D from its IK point on a damped spring
//   D'' = -w^2 (D - sag) - 2 zeta w D' - inertia * a_IK + coupling w^2 (D_prev + D_next - 2 D)
// driven by its own IK point's acceleration (a joint lags when the body bobs, turns or the leg swings, and wobbles
// after), stiffer at the root (w x 1.5) than at the tip (x 0.8), coupled to its neighbours (a continuous rod), sagging
// mid-leg. The tip is pinned to its foot by 1 - whip sin(pi stepT): exactly on a planted foot (the gait's no-slide
// holds for the drawn leg), loose mid-swing. Bones: from the displayed points, z-scaled so the segments meet.
// Froude: w / sqrt(s), sag and the bound x s, accelerations size-free. Zero GC after construction; main thread.
//
// Knees first (hipStiffness > 0, 2026-10-20): the synergy IK pitches its whole chain onto the target (a bobbing body
// or a lifted foot swings the leg rigidly about the hip). Before the flex, each walking leg is re-posed in the IK's own
// leg plane with the IK's ankle: the joint angles that reach that ankle while moving least from the leg's reference
// shape, weighted per joint (the hip's elevation hipStiffness, the arch's knees softest, straight joints stiffer) -
// a few Newton steps of the weighted least-change IK, stateless (from the reference every time, so it cannot drift).
// Mid-swing the reference lifts the hip by swingHipDeg and folds the arch by swingKneeDeg (sin(pi stepT)): the knee
// rises high and folds, the distal joints open and close through the stride. Joint limits are the leg's own.
// Clearance (bodyMargin > 0): the drawn segments are kept outside every IBodyObstacle (the DimpledOrb's ellipsoid,
// inflated by the margin and the leg's radius): segment vs ellipsoid in the ellipsoid's unit space, the joints pushed
// out along its normal (never the coxa or a planted foot).
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed class BendyLegSystem : IPartRuntime, ICapsuleSet
    {
        // RED test hook: a planted tip that is not pinned (the drawn foot slides) must be caught.
        internal static bool UnpinForTest;

        const float kMaxAccel = 60f;    // m/s^2: the drive is clamped (a teleport is not a kick)
        const float kRootStiff = 1.5f, kTipStiff = 0.8f;
        const float kFootFade = 0.3f;   // share of the leg next to a planted foot over which the drawn flex fades out

        readonly CreatureSim sim;
        readonly int[] leg, first, count, boneStart, attach;   // per bendy leg: sim leg, first point, points (n + 1)
        readonly float[] omega, zeta, inertia, sag, whip, coupling, maxDev, radius;
        readonly bool[] feeler;
        readonly float[] segLen;        // per point (segment starting there), scale 1; the last of a leg unused
        readonly float[] frac;          // per point: arc fraction from the coxa
        readonly Vector3[] T, Tp, V, A, D, Dv, X;   // IK point, its last, velocity, acceleration; deviation, its rate; displayed
        bool inited, primed;
        // knees first, per bendy leg and per point (the solved joints: point k < n - 1 holds joint k's bend, k = 0 the
        // hip's elevation in the leg plane); scratch th/px/py per point
        readonly float[] hipK, swingHip, swingKnee, margin;
        readonly float[] thRef, wInv, fold, jLo, jHi, th, px, py;
        IBodyObstacle[] obstacles;

        // Hooks for tests: the knees-first pose and the clearance pass can be switched off per system (RED proofs).
        internal bool KneesFirstOff, ClearanceOff;

        public BendyLegSystem(CreatureSim sim, List<int> legs, List<BendyLeg> parts)
        {
            this.sim = sim;
            int c = legs.Count, pts = 0;
            leg = new int[c]; first = new int[c]; count = new int[c]; boneStart = new int[c]; attach = new int[c];
            omega = new float[c]; zeta = new float[c]; inertia = new float[c]; sag = new float[c]; whip = new float[c];
            coupling = new float[c]; maxDev = new float[c]; radius = new float[c]; feeler = new bool[c];
            CreatureSpecies sp = sim.Species;
            for (int i = 0; i < c; i++)
            {
                LegSpec s = sp.legs[legs[i]];
                leg[i] = legs[i]; first[i] = pts; count[i] = s.SegmentCount + 1; pts += count[i];
                boneStart[i] = sp.LegBone(legs[i], 0); attach[i] = s.attachJoint; feeler[i] = s.feeler;
                BendyLeg p = parts[i];
                omega[i] = 2f * Mathf.PI * p.wobbleHz; zeta[i] = p.wobbleDamping; inertia[i] = p.inertia; sag[i] = p.sag;
                whip[i] = Mathf.Clamp01(p.whip); coupling[i] = p.coupling; maxDev[i] = p.maxDeflect * p.length; radius[i] = p.rootRadius;
            }
            segLen = new float[pts]; frac = new float[pts];
            for (int i = 0; i < c; i++)
            {
                LegSegment[] seg = sp.legs[leg[i]].segments;
                float total = 0f, a = 0f;
                for (int k = 0; k < seg.Length; k++) total += seg[k].length;
                for (int k = 0; k < count[i]; k++)
                {
                    frac[first[i] + k] = total > 0f ? a / total : 0f;
                    if (k < seg.Length) { segLen[first[i] + k] = seg[k].length; a += seg[k].length; }
                }
            }
            T = new Vector3[pts]; Tp = new Vector3[pts]; V = new Vector3[pts]; A = new Vector3[pts]; D = new Vector3[pts]; Dv = new Vector3[pts]; X = new Vector3[pts];

            hipK = new float[c]; swingHip = new float[c]; swingKnee = new float[c]; margin = new float[c];
            thRef = new float[pts]; wInv = new float[pts]; fold = new float[pts]; jLo = new float[pts]; jHi = new float[pts];
            th = new float[pts]; px = new float[pts]; py = new float[pts];
            for (int i = 0; i < c; i++)
            {
                BendyLeg p = parts[i];
                hipK[i] = feeler[i] ? 0f : p.hipStiffness; swingHip[i] = p.swingHipDeg; swingKnee[i] = p.swingKneeDeg; margin[i] = p.bodyMargin;
                LegSegment[] seg = sp.legs[leg[i]].segments;
                int m = seg.Length - 1;
                float bmax = 0f;
                for (int k = 1; k < m; k++) bmax = Mathf.Max(bmax, Mathf.Abs(seg[k].restBendDeg));
                for (int k = 0; k < m; k++)
                {
                    int q = first[i] + k;
                    float g = bmax > 0f && k > 0 ? Mathf.Abs(seg[k].restBendDeg) / bmax : 0f;   // 1 at the sharpest knee
                    fold[q] = g;
                    wInv[q] = k == 0 ? (hipK[i] > 0f ? 1f / hipK[i] : 1f) : 0.2f + g;          // the knees softest
                    jLo[q] = (seg[k].minBendDeg - seg[k].restBendDeg) * Mathf.Deg2Rad;        // limits about the reference
                    jHi[q] = (seg[k].maxBendDeg - seg[k].restBendDeg) * Mathf.Deg2Rad;
                }
            }
        }

        public PartStage Stage => PartStage.AfterLimbs;

        // ICapsuleSet: every drawn segment of every bendy leg (radius: the leg's root radius, conservative).
        int capsules = -1;
        public int CapsuleCount
        {
            get
            {
                if (capsules < 0) { capsules = 0; for (int i = 0; i < leg.Length; i++) capsules += count[i] - 1; }
                return capsules;
            }
        }

        public void Capsule(int c, out Vector3 a, out Vector3 b, out float r)
        {
            int i = 0;
            while (c >= count[i] - 1) { c -= count[i] - 1; i++; }
            a = Displayed(i, c); b = Displayed(i, c + 1); r = radius[i] * sim.SizeScale;
        }

        // The displayed (flexed) point k of bendy leg i (k = 0 the coxa .. n the foot); tests and the debug view.
        public int LegCount => leg.Length;
        public int SimLeg(int i) => leg[i];
        public int PointCount(int i) => count[i];
        public Vector3 Displayed(int i, int k) => inited ? X[first[i] + k] : sim.LegPoints[sim.LegPointOffset(leg[i]) + k];
        // The flex spring's deviation of point k from the (re-posed) IK point (the clearance push is not counted).
        public Vector3 Deviation(int i, int k) => inited ? D[first[i] + k] : Vector3.zero;

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float s = sim.SizeScale, ts = Mathf.Sqrt(Mathf.Max(1e-4f, s));
            Vector3[] P = sim.LegPoints;
            if (!inited)
            {
                for (int i = 0; i < leg.Length; i++)
                {
                    int o = sim.LegPointOffset(leg[i]);
                    for (int k = 0; k < count[i]; k++) { int q = first[i] + k; T[q] = Tp[q] = X[q] = P[o + k]; V[q] = D[q] = Dv[q] = Vector3.zero; }
                    if (hipK[i] > 0f) Angles(i, P, o, thRef);   // the reference shape: the first (rest) IK pose
                }
                FindObstacles();
                for (int i = 0; i < leg.Length; i++) if (margin[i] > 0f && obstacles.Length > 0) Clear(i, true);
                inited = true;
                return;
            }
            for (int i = 0; i < leg.Length; i++)
            {
                int o = sim.LegPointOffset(leg[i]), f = first[i], n = count[i] - 1;
                LegState st = sim.Legs[leg[i]];
                // the IK pose (knees first: re-posed in its plane) and its accelerations
                for (int k = 0; k <= n; k++) T[f + k] = P[o + k];
                if (hipK[i] > 0f && !KneesFirstOff) KneesFirst(i, st.stepping ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(st.stepT)) : 0f);
                for (int k = 0; k <= n; k++)
                {
                    int q = f + k;
                    Vector3 v = (T[q] - Tp[q]) / dt;
                    Vector3 a = primed ? (v - V[q]) / dt : Vector3.zero;   // the first velocity has no history
                    float am = a.magnitude;
                    A[q] = am > kMaxAccel ? a * (kMaxAccel / am) : a;
                    V[q] = v;
                    Tp[q] = T[q];
                }
                float pin = feeler[i] ? 1f - whip[i] : st.stepping ? 1f - whip[i] * Mathf.Sin(Mathf.PI * Mathf.Clamp01(st.stepT)) : 1f;
                if (UnpinForTest) pin = 1f - whip[i];
                Vector3 down = -sim.Up[attach[i]];
                float w0 = omega[i] / ts, z = zeta[i], cp = coupling[i], lim = maxDev[i] * s;
                // sub-steps for stability of the stiffest point (semi-implicit Euler: w dt well below 2)
                float wMax = w0 * kRootStiff * Mathf.Sqrt(1f + 4f * cp);
                int sub = Mathf.Clamp(Mathf.CeilToInt(wMax * dt / 0.4f), 1, 16);
                float h = dt / sub;
                for (int it = 0; it < sub; it++)
                    for (int k = 1; k <= n; k++)
                    {
                        int q = f + k;
                        float w = w0 * Mathf.Lerp(kRootStiff, kTipStiff, frac[q]), w2 = w * w;
                        Vector3 eq = k < n ? down * (sag[i] * s * Mathf.Sin(Mathf.PI * frac[q])) : Vector3.zero;
                        Vector3 left = k > 1 ? D[q - 1] : Vector3.zero;
                        Vector3 right = k < n ? D[q + 1] : D[q];
                        Vector3 acc = -w2 * (D[q] - eq) - 2f * z * w * Dv[q] - inertia[i] * A[q] + cp * w2 * (left + right - 2f * D[q]);
                        Dv[q] += acc * h;
                        D[q] += Dv[q] * h;
                        float m = D[q].magnitude;
                        if (m > lim) { D[q] *= lim / m; Dv[q] -= Vector3.Project(Dv[q], D[q]); }
                    }
                // a planted tip rests: its spring starts from rest at the next lift-off
                if (pin >= 1f) { D[f + n] = Vector3.zero; Dv[f + n] = Vector3.zero; }
                // near a planted foot the drawn flex fades out (the last kFootFade of the leg): the ankle cannot swing
                // round a pinned tip and fold the tarsus; eased in with the pin, so a touchdown never pops
                X[f] = T[f];
                for (int k = 1; k < n; k++)
                {
                    float env = Mathf.Lerp(1f, Mathf.Clamp01((1f - frac[f + k]) / kFootFade), pin);
                    X[f + k] = T[f + k] + D[f + k] * env;
                }
                X[f + n] = T[f + n] + D[f + n] * (1f - pin);
                if (margin[i] > 0f && !ClearanceOff && obstacles.Length > 0) Clear(i, pin >= 1f);
            }
            primed = true;
        }

        void FindObstacles()
        {
            int c = 0;
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is IBodyObstacle) c++;
            obstacles = new IBodyObstacle[c];   // once, on the first step
            c = 0;
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is IBodyObstacle b) obstacles[c++] = b;
        }

        // The leg plane of bendy leg i: ex outward (horizontal), ey up; the IK's planar chain lies in it.
        void Plane(int i, out Vector3 ex, out Vector3 ey)
        {
            Vector3 pn = sim.LegPlaneNormal(leg[i]);
            Vector3 u = sim.Up[attach[i]];
            ey = u - pn * Vector3.Dot(u, pn);
            ey = ey.sqrMagnitude > 1e-10f ? ey.normalized : Vector3.up;
            ex = Vector3.Cross(ey, pn);
        }

        // The relative bends (rad) of a chain's solved segments in leg i's plane: k = 0 the first segment's elevation.
        void Angles(int i, Vector3[] P, int o, float[] dst)
        {
            Plane(i, out Vector3 ex, out Vector3 ey);
            int f = first[i], m = count[i] - 2;
            float prev = 0f;
            for (int k = 0; k < m; k++)
            {
                Vector3 d = P[o + k + 1] - P[o + k];
                float a = Mathf.Atan2(Vector3.Dot(d, ey), Vector3.Dot(d, ex));
                dst[f + k] = Mathf.Repeat(a - prev + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
                prev = a;
            }
        }

        // Forward kinematics of th in the plane (scale 1): px/py of points 0..m.
        void FK(int i, int m)
        {
            int f = first[i];
            float a = 0f, x = 0f, y = 0f;
            px[f] = 0f; py[f] = 0f;
            for (int k = 0; k < m; k++)
            {
                a += th[f + k];
                x += segLen[f + k] * Mathf.Cos(a); y += segLen[f + k] * Mathf.Sin(a);
                px[f + k + 1] = x; py[f + k + 1] = y;
            }
        }

        const int kKneeIter = 8;

        // Knees first: re-poses T (the IK's points) of bendy leg i in its plane - the same coxa and ankle, joint angles
        // that move least from the (swing-shaped) reference, weighted per joint. lift = sin(pi stepT) in a swing, else 0.
        void KneesFirst(int i, float lift)
        {
            int f = first[i], n = count[i] - 1, m = n - 1;
            float s = sim.SizeScale;
            Plane(i, out Vector3 ex, out Vector3 ey);
            Vector3 root = T[f];
            Vector3 v = (T[f + m] - root) / s;
            float ax = Vector3.Dot(v, ex), ay = Vector3.Dot(v, ey);
            float hip = swingHip[i] * Mathf.Deg2Rad * lift, knee = swingKnee[i] * Mathf.Deg2Rad * lift;
            float total = 0f;
            for (int k = 0; k < m; k++) { int q = f + k; th[q] = thRef[q] + (k == 0 ? hip : -knee * fold[q]); total += segLen[q]; }
            float err = float.MaxValue;
            for (int it = 0; it < kKneeIter; it++)
            {
                FK(i, m);
                float ex2 = px[f + m], ey2 = py[f + m];
                float rx = ax - ex2, ry = ay - ey2;
                err = Mathf.Sqrt(rx * rx + ry * ry);
                if (err < 1e-5f * total) break;
                // J_k = perp(e - p_k); dth = W^-1 J^T (J W^-1 J^T)^-1 r: the least weighted change that reaches the ankle
                float a11 = 0f, a12 = 0f, a22 = 0f;
                for (int k = 0; k < m; k++)
                {
                    int q = f + k;
                    float jx = -(ey2 - py[q]), jy = ex2 - px[q], w = wInv[q];
                    a11 += w * jx * jx; a12 += w * jx * jy; a22 += w * jy * jy;
                }
                float lam = 1e-4f * total * total;
                a11 += lam; a22 += lam;
                float det = a11 * a22 - a12 * a12;
                if (!(det > 1e-20f)) break;
                float bx = (a22 * rx - a12 * ry) / det, by = (a11 * ry - a12 * rx) / det;
                for (int k = 0; k < m; k++)
                {
                    int q = f + k;
                    float jx = -(ey2 - py[q]), jy = ex2 - px[q];
                    float t = th[q] + wInv[q] * (jx * bx + jy * by);
                    if (k > 0) t = Mathf.Clamp(t, thRef[q] + jLo[q], thRef[q] + jHi[q]);
                    th[q] = t;
                }
            }
            FK(i, m);
            {
                float rx = ax - px[f + m], ry = ay - py[f + m];
                err = Mathf.Sqrt(rx * rx + ry * ry);
            }
            // an ankle the shape cannot reach within its limits: back to the IK's pose, eased over 1..3 % of the leg
            float keep = 1f - Mathf.SmoothStep(0f, 1f, (err / total - 0.01f) / 0.02f);
            if (keep <= 0f) return;
            // the last residual: the chain turned and stretched onto the ankle about the coxa (tiny)
            float ex3 = px[f + m], ey3 = py[f + m];
            float le = Mathf.Sqrt(ex3 * ex3 + ey3 * ey3), la = Mathf.Sqrt(ax * ax + ay * ay);
            float rot = Mathf.Atan2(ay, ax) - Mathf.Atan2(ey3, ex3), sc = le > 1e-9f ? la / le : 1f;
            float cr = Mathf.Cos(rot) * sc, sr = Mathf.Sin(rot) * sc;
            for (int k = 1; k < m; k++)
            {
                int q = f + k;
                float x = px[q] * cr - py[q] * sr, y = px[q] * sr + py[q] * cr;
                Vector3 w = root + (ex * x + ey * y) * s;
                T[q] = keep >= 1f ? w : Vector3.Lerp(T[q], w, keep);
            }
        }

        // Keeps the drawn segments of bendy leg i outside every obstacle (inflated by the margin and the leg's root
        // radius): a few passes of segment vs ellipsoid in its unit space, the segment's closest point moved out to the
        // surface by its movable ends (barycentric shares). The coxa and a planted foot never move.
        void Clear(int i, bool planted)
        {
            int f = first[i], n = count[i] - 1;
            float pad = (margin[i] + radius[i]) * sim.SizeScale;
            for (int o = 0; o < obstacles.Length; o++)
            {
                obstacles[o].Ellipsoid(out Vector3 c, out Quaternion r, out Vector3 h);
                Vector3 ax = new Vector3(h.x + pad, h.y + pad, h.z + pad);
                Vector3 inv = new Vector3(1f / ax.x, 1f / ax.y, 1f / ax.z);
                Quaternion ri = Quaternion.Inverse(r);
                for (int pass = 0; pass < 4; pass++)
                {
                    bool moved = false;
                    for (int k = 0; k < n; k++)
                    {
                        bool moveA = k > 0, moveB = k + 1 < n || !planted;
                        Vector3 a = Vector3.Scale(ri * (X[f + k] - c), inv), b = Vector3.Scale(ri * (X[f + k + 1] - c), inv);
                        Vector3 ab = b - a;
                        float l2 = ab.sqrMagnitude;
                        float t = l2 > 1e-12f ? Mathf.Clamp01(-Vector3.Dot(a, ab) / l2) : 0f;
                        Vector3 p = a + ab * t;
                        float d = p.magnitude;
                        if (d >= 1f) continue;
                        float wa = moveA ? 1f - t : 0f, wb = moveB ? t : 0f, ww = wa * wa + wb * wb;
                        if (ww < 1e-3f) continue;   // the closest point sits on a fixed end (a coxa inside: authoring)
                        Vector3 nrm = d > 1e-6f ? p / d : Vector3.up;
                        Vector3 push = nrm * ((1f - d) * 1.001f / ww);
                        if (moveA) X[f + k] += r * Vector3.Scale(push * wa, ax);
                        if (moveB) X[f + k + 1] += r * Vector3.Scale(push * wb, ax);
                        moved = true;
                    }
                    if (!moved) break;
                }
            }
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            if (!inited) return;   // the IK's own bones (CreatureLegs wrote them): the rest pose and the bind poses
            float s = sim.SizeScale;
            for (int i = 0; i < leg.Length; i++)
            {
                int f = first[i], n = count[i] - 1;
                Vector3 pn = sim.LegPlaneNormal(leg[i]);
                for (int k = 0; k < n; k++)
                {
                    Vector3 a = X[f + k], b = X[f + k + 1];
                    float zs = (b - a).magnitude / Mathf.Max(1e-6f, segLen[f + k] * s);
                    dst[offset + boneStart[i] + k] = Matrix4x4.TRS(a, ArthropodLegIK.SegmentRotation(a, b, pn), new Vector3(s, s, s * zs));
                }
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            if (!inited) return;
            for (int i = 0; i < leg.Length; i++)
            {
                float r = 2f * radius[i] * sim.SizeScale;
                for (int k = 0; k < count[i]; k++)
                {
                    Vector3 p = X[first[i] + k];
                    lo = Vector3.Min(lo, p - new Vector3(r, r, r)); hi = Vector3.Max(hi, p + new Vector3(r, r, r));
                }
            }
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            if (!inited) return;
            var c = new Color(0.9f, 0.6f, 1f);
            for (int i = 0; i < leg.Length; i++)
                for (int k = 0; k + 1 < count[i]; k++) d.Line(X[first[i] + k], X[first[i] + k + 1], c);
        }
    }

    public sealed partial class BendyLeg
    {
        // One BendyLegSystem per individual, owned by the species' first BendyLeg.
        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null) return null;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(c.Species);
            int[] ordinal = KitView.Ordinals(parts);
            var legs = new List<int>();
            var bendy = new List<BendyLeg>();
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] is BendyLeg b)
                {
                    if (legs.Count == 0 && i != c.Index) return null;   // not the first: the first owns the system
                    legs.Add(ordinal[i]); bendy.Add(b);
                }
            return legs.Count > 0 ? new BendyLegSystem(c.Sim, legs, bendy) : null;
        }
    }
}
