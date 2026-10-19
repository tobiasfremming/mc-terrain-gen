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
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed class BendyLegSystem : IPartRuntime
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
        }

        public PartStage Stage => PartStage.AfterLimbs;

        // The displayed (flexed) point k of bendy leg i (k = 0 the coxa .. n the foot); tests and the debug view.
        public int LegCount => leg.Length;
        public int SimLeg(int i) => leg[i];
        public int PointCount(int i) => count[i];
        public Vector3 Displayed(int i, int k) => inited ? X[first[i] + k] : sim.LegPoints[sim.LegPointOffset(leg[i]) + k];
        public Vector3 Deviation(int i, int k) => inited ? X[first[i] + k] - T[first[i] + k] : Vector3.zero;

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
                }
                inited = true;
                return;
            }
            for (int i = 0; i < leg.Length; i++)
            {
                int o = sim.LegPointOffset(leg[i]), f = first[i], n = count[i] - 1;
                // the IK pose and its accelerations
                for (int k = 0; k <= n; k++)
                {
                    int q = f + k;
                    T[q] = P[o + k];
                    Vector3 v = (T[q] - Tp[q]) / dt;
                    Vector3 a = primed ? (v - V[q]) / dt : Vector3.zero;   // the first velocity has no history
                    float am = a.magnitude;
                    A[q] = am > kMaxAccel ? a * (kMaxAccel / am) : a;
                    V[q] = v;
                    Tp[q] = T[q];
                }
                LegState st = sim.Legs[leg[i]];
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
            }
            primed = true;
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
