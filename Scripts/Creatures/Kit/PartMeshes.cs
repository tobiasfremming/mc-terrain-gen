// Creature kit T3.1: how each existing part draws itself (CreaturePart.EmitMesh through PartMeshEmitter), in the
// rest pose. The geometry is the pre-kit builders' (CreatureMeshBuilder, WingedMeshBuilder) moved here unchanged,
// one part at a time: every shipped species keeps its mesh byte for byte (CreatureMeshEmitterTests).
// Existing parts read their runtime entry (sp.legs[l], sp.appendages[c], ... at PartCtx.Ordinal: the same objects
// the systems read); a new part type reads its own fields and the emitter's rest pose.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed partial class SpineBody
    {
        internal const int kHeadCapRings = 4, kTailRings = 8;
        internal const float kTailTipSlope = -1.5f; // d(radius / end radius) / d(k) at the tail tip: a fine point
        const float kSnoutNarrow = 0.2f;   // extra width taper toward the snout tip (fraction at the tip)
        const float kSnoutFlat = 0.35f;    // extra height taper: the snout is flatter than it is wide
        const float kSnoutDrop = 0.15f;    // the snout's centre line drops by this fraction of head height

        public override int MeshOrder => 0;

        struct Section { public Vector3 c, t, x, y; public float a, b, d; }

        // Body rings from the snout tip to the tail tip, consecutive rings of ringSegments + 1 vertices (seam
        // duplicated; each tip a ring of identical vertices), skinned to the spine joints.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c) => new BodyMesh(m).Emit();

        // Cubic Hermite from 1 (slope m0, matching the body) to 0 (slope kTailTipSlope); monotone for
        // m0 in [kTailTipSlope, 0]. The tail tip's profile (BodyMesh; FlowBody's tail tip too).
        internal static float TailTipProfile(float k, float m0)
        {
            float k2 = k * k, k3 = k2 * k;
            return Mathf.Max(0f, (2f * k3 - 3f * k2 + 1f) + m0 * (k3 - 2f * k2 + k) + kTailTipSlope * (k3 - k2));
        }

        // What a body draws its rings with (RingSequence): the front pole, a ring at arc s with its skin, the tail pole.
        internal interface IBodyRings
        {
            int FrontPole(int segs);
            int Ring(float s, Vector4 skin, int segs);
            int TailPole(int segs);
        }

        // The body's ring sequence from the front pole to the tail pole (BodyMesh's, shared with FlowBody): kHeadCapRings
        // rings over the head cap (sine-spaced, rigid to joint 0), rps per segment (smoothstep blend of the segment's two
        // joints), the last joint, kTailRings over the tail tip (rigid to the last joint), stitched front to tail with the
        // flip a ring frame x = cross(y, t) along -t needs. lh = head cap length, spine = spacing x (n - 1).
        internal static void RingSequence<R>(PartMeshEmitter m, in R r, int n, float lh, float spacing, float spine, float tailLen, int rps, int segs)
            where R : struct, IBodyRings
        {
            var rings = new List<int>();
            rings.Add(r.FrontPole(segs));
            for (int k = kHeadCapRings; k >= 1; k--)
            {
                float theta = 0.5f * Mathf.PI * k / (kHeadCapRings + 1);
                rings.Add(r.Ring(lh - lh * Mathf.Sin(theta), PartMeshEmitter.Rigid(0), segs));
            }
            for (int i = 0; i < n - 1; i++)
                for (int q = 0; q < rps; q++)
                {
                    float f = (float)q / rps;
                    rings.Add(r.Ring(lh + spacing * (i + f), PartMeshEmitter.Blend(i, i + 1, f * f * (3f - 2f * f)), segs));
                }
            rings.Add(r.Ring(lh + spine, PartMeshEmitter.Rigid(n - 1), segs));
            for (int k = 1; k <= kTailRings; k++)
                rings.Add(r.Ring(lh + spine + tailLen * k / (kTailRings + 1), PartMeshEmitter.Rigid(n - 1), segs));
            rings.Add(r.TailPole(segs));

            // rings run snout -> tail, i.e. along -t, with x = cross(y, t): flip to face outward
            for (int q = 0; q + 1 < rings.Count; q++)
                m.Stitch(rings[q], rings[q + 1], segs, q == 0, q + 2 == rings.Count, true);
        }

        readonly struct BodyMesh : IBodyRings
        {
            readonly PartMeshEmitter m;
            readonly CreatureSpecies sp;
            readonly int n;
            public BodyMesh(PartMeshEmitter m) { this.m = m; sp = m.Species; n = m.Joints; }

            float HeadLen => Mathf.Max(sp.headCapLength, 1e-3f);
            float TailLen => Mathf.Max(sp.tailTipLength, 1e-3f);
            float SpineLen => sp.spacing * (n - 1);
            float BodyLen => HeadLen + SpineLen + TailLen;

            Section Spine(float u)
            {
                SpineSection s = m.SpineAt(u);
                return new Section { c = s.c, t = s.t, x = s.x, y = s.y, a = s.a, b = s.b };
            }

            // Section at s metres from the snout tip (0) to the tail tip (BodyLen).
            Section BodyAt(float s)
            {
                float lh = HeadLen;
                if (s < lh)
                {
                    // head cap: elliptical profile ahead of the head joint, narrowing and flattening into a snout
                    Section h = Spine(0f);
                    float ahead = lh - s, q = ahead / lh;
                    float e = Mathf.Sqrt(Mathf.Max(0f, 1f - q * q));
                    h.c += h.t * ahead;
                    h.d = -kSnoutDrop * h.b * q * q;
                    h.a *= e * (1f - kSnoutNarrow * q * q);
                    h.b *= e * (1f - kSnoutFlat * q * q);
                    return h;
                }
                float tailStart = lh + SpineLen;
                if (s <= tailStart) return Spine((s - lh) / sp.spacing);
                Section tl = Spine(n - 1);
                float back = Mathf.Min(s - tailStart, TailLen), k = back / TailLen;
                tl.c -= tl.t * back;
                // ease out of the body's taper into a fine point: C1 at the last joint (no silhouette kink)
                tl.a *= TailProfile(k, TaperSlope(m.HW(n - 2), m.HW(n - 1)));
                tl.b *= TailProfile(k, TaperSlope(m.HH(n - 2), m.HH(n - 1)));
                return tl;
            }

            // The body profile's slope at the last joint (Catmull-Rom with clamped ends: half the last
            // difference per segment), in tail-profile units d(r / r_end) / d(k), k = distance / tailTipLength.
            float TaperSlope(float prev, float end)
            {
                float perMetre = 0.5f * (end - prev) / sp.spacing;
                return Mathf.Clamp(perMetre * TailLen / Mathf.Max(end, PartMeshEmitter.MinRadius), kTailTipSlope, 0f);
            }

            static float TailProfile(float k, float m0) => TailTipProfile(k, m0);

            int BodyRing(float s, Vector4 skin, int segs)
            {
                float s0 = Mathf.Max(0f, s - PartMeshEmitter.DiffStep), s1 = Mathf.Min(BodyLen, s + PartMeshEmitter.DiffStep);
                Section p = BodyAt(s0), q = BodyAt(s1), c = BodyAt(s);
                float ds = s1 - s0; // toward the head = decreasing s
                return m.Ring(c.c, c.x, c.y, c.t, c.a, c.b, c.d, (p.a - q.a) / ds, (p.b - q.b) / ds, (p.d - q.d) / ds,
                              s, PartMeshEmitter.PartBody, skin, segs, true);
            }

            public int FrontPole(int segs)
            {
                Section tip = BodyAt(0f);
                return m.Pole(tip.c + tip.y * tip.d, tip.t, 0f, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(0), segs);
            }

            public int Ring(float s, Vector4 skin, int segs) => BodyRing(s, skin, segs);

            public int TailPole(int segs)
            {
                Section end = BodyAt(BodyLen);
                return m.Pole(end.c, -end.t, BodyLen, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(n - 1), segs);
            }

            public void Emit() =>
                RingSequence(m, this, n, HeadLen, sp.spacing, SpineLen, TailLen, Mathf.Max(1, sp.ringsPerSegment), Mathf.Max(3, sp.ringSegments));
        }
    }

    public sealed partial class Leg2Bone
    {
        // A vertebrate limb (2026-10-19, "the legs look weird"): a muscled limb, not a hose of even width -
        //   haunch   the root swells to kHaunch x upperRadius and is buried in the body (blended 50/50 with it), so the
        //            limb grows out of a shoulder/hip instead of a pipe stuck on the flank;
        //   upper    thigh / upper arm tapering from the haunch to the knee (kKnee x upperRadius, never thinner than
        //            the shin), the rounded knee blend as before;
        //   lower    a calf / forearm belly (kCalf) then a slim ankle (kAnkle x lowerRadius), ending in a round
        //            ankle knob at the lower bone's end - LegSpec.AnkleHeight above the ground, not in it;
        //   foot     rigid to the foot bone: a lofted foot from the heel under the ankle to the knuckles (a flat
        //            plantigrade sole for a low ankle, a raised hock for a high one), splayed toes lying on the
        //            ground with curved claws (part 2). The ankle knob sits inside the foot at any bend.
        const int kLegSides = 10;
        const int kKneeRings = 7;          // rings along the knee curve, ends included
        const float kHaunch = 1.35f;       // root radius / upperRadius
        const float kHaunchDepth = 1.1f;   // the root's buried end, x the haunch radius behind the shoulder point
        const float kRootBody = 0.3f, kRootLimb = 0.5f;   // limb weight of the buried root rings / the shoulder ring
        const float kKnee = 0.72f;         // knee radius / upperRadius (at least the shin's)
        const float kCalf = 0.16f;         // calf belly, x lowerRadius, peaking a third down the shin
        const float kAnkle = 0.66f;        // ankle radius / lowerRadius
        const float kMuscleDepth = 0.2f;   // thigh / upper arm section: deeper along the bend by this, narrower across
        const float kCalfBack = 2.5f;      // the calf belly's centre behind the shin axis, x its height
        const float kHeel = 0.24f;         // heel behind the ankle, x footLength
        const float kBall = 0.34f;         // the knuckles (toe roots) ahead of the ankle, x footLength
        const int kToeSides = 6;
        const int kClawSides = 5;
        const float kFootYawDeg = 10f;     // the foot turned out from the body's heading (toes splay outward)

        public override int MeshOrder => 1;

        struct LegSample { public Vector3 c, t; public float sigma, r, dr; public Vector4 skin; public bool pole; public int frame; }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            int l = c.Ordinal;
            if (m.IsWingArm(l)) return;
            CreatureSpecies sp = m.Species;
            LegSpec s = sp.legs[l];
            if (s.IsArthropod) { ArthropodLeg.Emit(m, l); return; }   // the runtime leg decides (as before the kit)
            if (s.digitigrade) { Digitigrade(m, s, l); return; }
            Matrix4x4[] bones = m.Bones;
            LegState st = m.Rest.Legs[l];
            int attach = s.attachJoint, bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1), bf = sp.LegBone(l, 2);
            Vector3 S = st.shoulder, E = st.elbow, A = st.ankle;
            float lu = Mathf.Max(1e-4f, (E - S).magnitude), ll = Mathf.Max(1e-4f, (A - E).magnitude);
            Vector3 du = bones[bu].rotation * Vector3.forward, dl = bones[bl].rotation * Vector3.forward;
            float ru = Mathf.Max(PartMeshEmitter.MinRadius, s.upperRadius), rl = Mathf.Max(PartMeshEmitter.MinRadius, s.lowerRadius);
            float rh = kHaunch * ru, rk = Mathf.Max(kKnee * ru, 1.02f * rl), ra = kAnkle * rl;
            float blob = kHaunchDepth * rh, total = lu + ll;

            // radius along the limb, sigma = arc length from the shoulder point (negative: the buried root)
            float Radius(float sg)
            {
                if (sg < 0f) { float q = sg / blob; return rh * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                if (sg <= lu)
                {
                    float u = sg / lu;   // muscle mass near the body, easing out toward the knee
                    return rk + (rh - rk) * Mathf.Pow(1f - u, 1.6f);
                }
                if (sg <= total)
                {
                    float v = (sg - lu) / ll;
                    float belly = Mathf.Sin(Mathf.PI * Mathf.Clamp01(v / 0.7f));
                    return Mathf.Lerp(rk, ra, v * v * (3f - 2f * v) * 0.6f + 0.4f * v) + kCalf * rl * belly;
                }
                float e = (sg - total) / ra;   // the ankle knob: a hemisphere of radius ra
                return ra * Mathf.Sqrt(Mathf.Max(0f, 1f - e * e));
            }

            var samples = new List<LegSample>();
            float Slope(float sg) => (Radius(sg + PartMeshEmitter.DiffStep) - Radius(sg - PartMeshEmitter.DiffStep)) / (2f * PartMeshEmitter.DiffStep);
            // frame: 1 = the upper bone's, 2 = the lower bone's (muscle sections, below), 0 = rotation-minimising
            int fr = 1;
            void Add(Vector3 cc, Vector3 t, float sigma, Vector4 skin, bool pole = false) =>
                samples.Add(new LegSample { c = cc, t = t, sigma = sigma, skin = skin, pole = pole, r = Radius(sigma), dr = Slope(sigma), frame = fr });
            void AddR(Vector3 cc, Vector3 t, float sigma, float r, float dr, Vector4 skin, bool pole = false) =>
                samples.Add(new LegSample { c = cc, t = t, sigma = sigma, skin = skin, pole = pole, r = r, dr = dr, frame = fr });
            // a hemispherical cap of radius r on centre cc, facing dir (+1: toward t, -1: against it)
            void Cap(Vector3 cc, Vector3 t, float sigma, float r, float dir, Vector4 skin)
            {
                if (dir < 0f) AddR(cc - t * r, t, sigma - r, 0f, 0f, skin, true);
                float[] a = dir < 0f ? new[] { 60f, 30f } : new[] { 30f, 60f };
                foreach (float deg in a)
                {
                    float d = r * Mathf.Sin(deg * Mathf.Deg2Rad), rr = r * Mathf.Cos(deg * Mathf.Deg2Rad);
                    AddR(cc + t * (dir * d), t, sigma + dir * d, rr, -dir * d / rr, skin);
                }
                if (dir > 0f) AddR(cc + t * r, t, sigma + r, 0f, 0f, skin, true);
            }

            // the haunch: a rounded root buried in the body; it and the shoulder ring blend 50/50 body/limb
            // (the buried end leans to the body, the limb's share growing toward the shoulder ring: a long gentle
            // gradient keeps a far-swung thick root from shearing through itself)
            Vector4 rootSkin = PartMeshEmitter.Blend(attach, bu, kRootLimb);
            Add(S - du * blob, du, -blob, PartMeshEmitter.Blend(attach, bu, kRootBody), true);
            Add(S - du * (blob * 0.866f), du, -blob * 0.866f, PartMeshEmitter.Blend(attach, bu, kRootBody));
            Add(S - du * (blob * 0.5f), du, -blob * 0.5f, PartMeshEmitter.Blend(attach, bu, 0.5f * (kRootBody + kRootLimb)));
            Add(S, du, 0f, rootSkin);
            // (the body/limb blend fades from 50/50 at the shoulder ring to rigid over the haunch, so a limb swung
            // far from rest bends the thick root smoothly instead of folding it against one rigid ring)
            bool blendKnee = s.kneeBlend > 0f;   // authored: the pre-2026-10-19 skinned knee curve
            float kb = blendKnee ? s.KneeBlend : 0f;
            float upTo = blendKnee ? 1f - kb : 1f;
            int ups = blendKnee ? 3 : 4;
            for (int i = 1; i <= ups; i++)
            {
                float f = upTo * i / (ups + 1f);
                float w = blendKnee ? Mathf.Min(1f, 0.5f + 0.5f * f / Mathf.Max(1e-3f, 0.75f * upTo)) : kRootLimb + (1f - kRootLimb) * f;
                Add(S + du * (f * lu), du, f * lu, w >= 1f ? PartMeshEmitter.Rigid(bu) : PartMeshEmitter.Blend(attach, bu, w));
            }
            int lowerStart = -1;
            if (blendKnee)
            {
                fr = 0;
                // knee: the last and first kb of the bones follow a rounded curve and blend to 50/50 at the elbow
                Vector3 k0 = S + du * ((1f - kb) * lu), k2 = E + dl * (kb * ll);
                for (int i = 0; i < kKneeRings; i++)
                {
                    float t = i / (kKneeRings - 1f), w = t * t * (3f - 2f * t);
                    Vector3 cc = (1f - t) * (1f - t) * k0 + 2f * (1f - t) * t * E + t * t * k2;
                    Vector3 tan = 2f * (1f - t) * (E - k0) + 2f * t * (k2 - E);
                    tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : du;
                    // the profile is a function of the bone stations, not of the curve's length
                    float sg = t < 0.5f ? Mathf.Lerp((1f - kb) * lu, lu, 2f * t) : Mathf.Lerp(lu, lu + kb * ll, 2f * t - 1f);
                    Add(cc, tan, sg, PartMeshEmitter.Blend(bu, bl, w));
                }
            }
            else
            {
                // A ball knee: the upper tube ends in a hemisphere of the knee radius on the knee, rigid to the upper
                // bone; the lower tube starts in the same sphere, rigid to the lower bone. The two caps are one
                // sphere at any bend, so the knee never creases or inverts however far it bends (MaxBend), and
                // the outside of the bend reads as a knee/elbow.
                AddR(E, du, lu, rk, 0f, PartMeshEmitter.Rigid(bu));
                Cap(E, du, lu, rk, 1f, PartMeshEmitter.Rigid(bu));
                lowerStart = samples.Count;
                fr = 2;
                Cap(E, dl, lu, rk, -1f, PartMeshEmitter.Rigid(bl));
                AddR(E, dl, lu, rk, 0f, PartMeshEmitter.Rigid(bl));
            }
            // the shin / forearm, then the ankle knob past the lower bone's end
            fr = 2;
            for (int i = 1; i <= 4; i++)
            {
                float f = kb + (1f - kb) * i / 5f;
                Add(E + dl * (f * ll), dl, lu + f * ll, PartMeshEmitter.Rigid(bl));
            }
            Add(A, dl, total, PartMeshEmitter.Rigid(bl));
            Cap(A, dl, total, ra, 1f, PartMeshEmitter.Rigid(bl));

            // rotation-minimising frame transported from the upper bone's frame (across the knee break too: the
            // lower tube's seam stays on the same side)
            Vector3 x = bones[bu].rotation * Vector3.right;
            int prev = -1;
            for (int i = 0; i < samples.Count; i++)
            {
                LegSample q = samples[i];
                // a muscled limb is not round: in the bone's own frame (+Y the knee's direction) the thigh / upper
                // arm is deeper along the bend than across it, and the calf / forearm belly sits behind the shin
                // (kMuscleDepth, kCalfBack); both fade to round at the haunch, knee and ankle
                Vector3 xs = q.frame == 1 ? bones[bu].rotation * Vector3.right : q.frame == 2 ? bones[bl].rotation * Vector3.right : x;
                Vector3 xt = Vector3.ProjectOnPlane(xs, q.t);
                x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
                Vector3 y = Vector3.Cross(q.t, x);
                float u = q.sigma + blob;
                bool first = i == 0 || i == lowerStart;
                float ea = 1f, eb = 1f, ed = 0f;
                if (q.frame == 1 && q.sigma > 0f && q.sigma < lu)
                {
                    float w = Mathf.Sin(Mathf.PI * q.sigma / lu);
                    ea = 1f - 0.5f * kMuscleDepth * w; eb = 1f + kMuscleDepth * w;
                }
                else if (q.frame == 2 && q.sigma > lu && q.sigma < total)
                {
                    float w = Mathf.Sin(Mathf.PI * (q.sigma - lu) / ll);
                    ea = 1f - 0.3f * kMuscleDepth * w; eb = 1f + 0.5f * kMuscleDepth * w;
                    ed = -kCalfBack * kCalf * rl * Mathf.Sin(Mathf.PI * Mathf.Clamp01((q.sigma - lu) / (0.7f * ll)));
                }
                int cur;
                if (q.pole) cur = m.Pole(q.c, first ? -q.t : q.t, u, PartMeshEmitter.PartLeg, (first ? -q.t : q.t).y, q.skin, kLegSides);
                else cur = m.Ring(q.c, x, y, q.t, q.r * ea, q.r * eb, ed, q.dr * ea, q.dr * eb, 0f, u, PartMeshEmitter.PartLeg, q.skin, kLegSides, false);
                if (prev >= 0 && i != lowerStart) m.Stitch(prev, cur, kLegSides, samples[i - 1].pole, q.pole, false);
                prev = cur;
            }

            Foot(m, s, bf, s.AnkleHeight, ra, total + blob);
        }

        // The foot in the foot bone's frame (origin: the contact point, +Y the ground normal, +Z the heading),
        // rigid to it: the ankle is at (0, ankleHeight, 0). Turned out by kFootYawDeg.
        static void Foot(PartMeshEmitter m, LegSpec s, int bf, float ankleH, float ra, float arc)
        {
            float fl = Mathf.Max(1e-3f, s.footLength);
            Matrix4x4 frame = m.Bones[bf] * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(s.side * kFootYawDeg, Vector3.up), Vector3.one);
            Vector4 skin = PartMeshEmitter.Rigid(bf);
            int toes = Mathf.Max(0, s.toeCount);
            float h = Mathf.Max(ankleH, 0.5f * ra);
            float zh = -kHeel * fl, zb = kBall * fl;
            float rt = Mathf.Max(0.36f * ra, 0.055f * fl);                     // toe root radius
            float pad = Mathf.Max(1.15f * rt, 0.5f * ra);                         // knuckle thickness (half)
            float palmW = Mathf.Max(1.05f * ra, 0.6f * rt * Mathf.Max(2, toes)); // half-width at the knuckles
            float hock = Mathf.Max(0f, h - 2.2f * ra);                            // the heel's sole height: 0 = plantigrade

            // the loft: sections across +Z (planes of x, y), from the heel pole to the front pole
            float Top(float z) => z <= 0f ? Mathf.Lerp(hock + 1.3f * ra, h + 0.3f * ra, Mathf.SmoothStep(0f, 1f, (z - zh) / -zh))
                                         : Mathf.Lerp(h + 0.3f * ra, 2f * pad, Mathf.SmoothStep(0f, 1f, z / zb));
            float Bot(float z) => z <= 0f ? hock : Mathf.Lerp(hock, 0f, Mathf.SmoothStep(0f, 1f, z / zb));
            float Wid(float z) => z <= 0f ? Mathf.Lerp(0.75f * ra, 1.08f * ra, Mathf.SmoothStep(0f, 1f, (z - zh) / -zh))
                                         : Mathf.Lerp(1.08f * ra, palmW, Mathf.SmoothStep(0f, 1f, z / zb));
            float[] zs = { zh + 0.25f * ra, 0.5f * zh, 0f, 0.3f * zb, 0.6f * zb, zb };
            const float d = 1e-3f;
            Vector3 X = frame.MultiplyVector(Vector3.right).normalized, Y = frame.MultiplyVector(Vector3.up).normalized, Z = frame.MultiplyVector(Vector3.forward).normalized;
            int prev = m.Pole(frame.MultiplyPoint3x4(new Vector3(0f, 0.5f * (Top(zh) + Bot(zh)), zh)), -Z, arc, PartMeshEmitter.PartLeg, -Z.y, skin, kLegSides);
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
                float yc = 0.5f * (Top(z) + Bot(z)), b = 0.5f * (Top(z) - Bot(z)), a = Wid(z);
                float da = (Wid(z + d) - Wid(z - d)) / (2f * d);
                float db = 0.5f * ((Top(z + d) - Bot(z + d)) - (Top(z - d) - Bot(z - d))) / (2f * d);
                float dd = 0.5f * ((Top(z + d) + Bot(z + d)) - (Top(z - d) + Bot(z - d))) / (2f * d);
                int cur = m.Ring(frame.MultiplyPoint3x4(new Vector3(0f, yc, z)), X, Y, Z, a, b, 0f, da, db, dd, arc + z - zh,
                                 PartMeshEmitter.PartLeg, skin, kLegSides, false);
                m.Stitch(prev, cur, kLegSides, i == 0, false, false);
                prev = cur;
            }
            int front = m.Pole(frame.MultiplyPoint3x4(new Vector3(0f, pad, zb + 0.6f * pad)), Z, arc + zb - zh + pad, PartMeshEmitter.PartLeg, Z.y, skin, kLegSides);
            m.Stitch(prev, front, kLegSides, false, true, false);

            // toes: fanned from under the knuckles, arched, lying on the ground; the middle ones longest
            float fan = Mathf.Min(50f, 12f * (toes - 1));
            var tipL = new Vector3[toes]; var dirs = new Vector3[toes]; var lens = new float[toes]; var roots = new Vector3[toes];
            for (int i = 0; i < toes; i++)
            {
                float k = toes == 1 ? 0f : i / (toes - 1f) * 2f - 1f;    // -1 .. 1 across the foot
                float yaw = k * fan;
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
                float len = (0.62f - 0.2f * k * k) * fl;
                Vector3 root = new Vector3(k * 0.55f * palmW, rt, zb - 0.6f * pad);
                float arch = 0.7f * rt;
                Vector3 C(float q) => root + dir * (len * q) + Vector3.up * (arch * 4f * q * (1f - q) - 0.25f * rt * q);
                Vector3 D(float q) => dir * len + Vector3.up * (arch * (4f - 8f * q) - 0.25f * rt);
                float R(float q) => rt * (1f - 0.45f * q);
                float[] qs = { 0f, 0.3f, 0.6f, 0.85f, 1f };
                Vector3 back = frame.MultiplyVector(-dir).normalized;
                int tp = m.Pole(frame.MultiplyPoint3x4(root - dir * rt), back, 0f, PartMeshEmitter.PartLeg, back.y, skin, kToeSides);
                bool first = true;
                foreach (float q in qs)
                {
                    Vector3 t = D(q).normalized;
                    Vector3 xx = Vector3.Cross(Vector3.up, t).normalized;
                    Vector3 yy = Vector3.Cross(t, xx);
                    float r = R(q), dr = -0.45f * rt / len;
                    int cur = m.Ring(frame.MultiplyPoint3x4(C(q)), frame.MultiplyVector(xx).normalized, frame.MultiplyVector(yy).normalized,
                                     frame.MultiplyVector(t).normalized, r, 0.85f * r, 0f, dr, 0.85f * dr, 0f, rt + len * q, PartMeshEmitter.PartLeg, skin, kToeSides, false);
                    m.Stitch(tp, cur, kToeSides, first, false, false);
                    tp = cur; first = false;
                }
                Vector3 tipDir = frame.MultiplyVector(D(1f)).normalized;
                int tip = m.Pole(frame.MultiplyPoint3x4(C(1f) + D(1f).normalized * (0.55f * rt)), tipDir, rt + len, PartMeshEmitter.PartLeg, tipDir.y, skin, kToeSides);
                m.Stitch(tp, tip, kToeSides, false, true, false);
                // the claw: from the toe's tip, curling down onto the ground
                Vector3 cd = (D(1f).normalized + Vector3.up * 0.35f).normalized;
                Vector3 cu = Vector3.Cross(cd, Vector3.Cross(Vector3.up, cd));
                Quaternion cq = Quaternion.LookRotation(frame.MultiplyVector(cd).normalized, frame.MultiplyVector(cu).normalized);
                float clawLen = s.clawLength > 0f ? s.clawLength : Mathf.Max(0.16f * fl, 2.2f * rt);
                m.Horn(frame.MultiplyPoint3x4(C(1f) - D(1f).normalized * (0.2f * rt)), cq, 0.62f * R(1f), 0f, clawLen, s.clawCurveDeg > 0f ? s.clawCurveDeg : 75f,
                       bf, PartMeshEmitter.PartFoot, kClawSides, rt + len, out _, out _);
                tipL[i] = C(1f); dirs[i] = dir; lens[i] = len; roots[i] = root;
            }

            // webbing (LegSpec.toeWeb): a thin two-sided membrane between neighbouring toes from their roots to the
            // web fraction of their length, its free edge scalloped back between the toes; rigid to the foot
            float web = Mathf.Clamp01(s.toeWeb);
            if (web <= 0f || toes < 2) return;
            const int kWebRows = 4;
            float wy = 0.55f * rt, thick = 0.12f * rt;
            for (int i = 0; i + 1 < toes; i++)
            {
                for (int side = 0; side < 2; side++)
                {
                    float up = side == 0 ? 1f : -1f;
                    Vector3 nrm = frame.MultiplyVector(Vector3.up * up).normalized;
                    int first = m.Lists.pos.Count;
                    for (int r = 0; r <= kWebRows; r++)
                    {
                        float q = web * r / kWebRows;
                        Vector3 pa = roots[i] + dirs[i] * (lens[i] * q), pb = roots[i + 1] + dirs[i + 1] * (lens[i + 1] * q);
                        float scallop = 1f - 0.35f * (r / (float)kWebRows) * (r / (float)kWebRows);
                        Vector3 pm = Vector3.Lerp(roots[i] + dirs[i] * (lens[i] * q * scallop), roots[i + 1] + dirs[i + 1] * (lens[i + 1] * q * scallop), 0.5f);
                        foreach (Vector3 pp in new[] { pa, pm, pb })
                        {
                            Vector3 lp = new Vector3(pp.x, wy + up * thick, pp.z);
                            m.Vertex(frame.MultiplyPoint3x4(lp), nrm, rt + lens[i] * q, 0f, PartMeshEmitter.PartLeg, nrm.y, skin);
                        }
                    }
                    for (int r = 0; r < kWebRows; r++)
                        for (int k = 0; k < 2; k++)
                        {
                            int a0 = first + r * 3 + k, a1 = a0 + 1, b0 = a0 + 3, b1 = b0 + 1;
                            // front faces along nrm: (b0 - a0) x (a1 - a0) is up for rows running +z and columns +x
                            bool flip = up < 0f;
                            m.Tri(a0, b0, a1, flip); m.Tri(a1, b0, b1, flip);
                        }
                }
            }
        }
    }

    public sealed partial class ArthropodLeg
    {
        public override int MeshOrder => 1;

        // Rigid segment capsules, knuckles, bands and claws in the rest pose the sim solved (ArthropodLegMesh).
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.IsWingArm(c.Ordinal)) return;
            Emit(m, c.Ordinal);
        }

        internal static void Emit(PartMeshEmitter m, int l)
        {
            CreatureSpecies sp = m.Species;
            LegSpec s = sp.legs[l];
            if (!s.IsArthropod) { new Leg2Bone().EmitMesh(m, new PartCtx(sp, -1, l, SizeCtx.One, m.Rest)); return; }   // the runtime leg decides
            CreatureSim sim = m.Rest;
            ArthropodLegMesh.Build(m.Lists, s.segments, sim.LegPoints, sim.LegPointOffset(l), sim.LegPlaneNormal(l), sp.LegBone(l, 0),
                                   s.attachJoint, s.legSides > 0 ? s.legSides : 8, s.toeCount, s.footLength);
        }
    }

    public sealed partial class Eyes
    {
        const int kEyeAround = 8, kEyeBands = 6;

        public override int MeshOrder => 2;

        // Two glossy spheres on the eye bones, then the cluster eyes on the eye joint, each rigid to the eye bone of
        // its side (one eye on the midline).
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSpecies sp = m.Species;
            CreatureSim sim = m.Rest;
            Matrix4x4[] bones = m.Bones;
            float r = Mathf.Max(PartMeshEmitter.MinRadius, sp.eyeRadius);
            for (int e = 0; e < 2; e++)
            {
                int bone = sp.EyeBone(e);
                m.Ellipsoid(bones[bone], Vector3.zero, r, r, r, kEyeAround, kEyeBands, PartMeshEmitter.PartEye, bone);
            }
            if (sp.eyeCluster != null)
                foreach (EyeSpot spot in sp.eyeCluster)
                {
                    float rs = Mathf.Max(PartMeshEmitter.MinRadius, spot.radius);
                    for (int e = 0; e < 2; e++)
                    {
                        if (spot.offset.x == 0f && e == 1) break;   // on the midline: one eye
                        Vector3 o = spot.offset; o.x *= e == 0 ? -1f : 1f;
                        int bone = sp.EyeBone(e);
                        Vector3 cc = sim.Joints[sp.eyeJoint] + sim.Rotations[sp.eyeJoint] * o;
                        m.Ellipsoid(Matrix4x4.TRS(cc, bones[bone].rotation, Vector3.one), Vector3.zero, rs, rs, rs, kEyeAround, kEyeBands, PartMeshEmitter.PartEye, bone);
                    }
                }
        }
    }

    public sealed partial class RigidCone
    {
        const int kSides = 8;

        public override int MeshOrder => 3;

        // A curved cone rigid to its spine joint (the pair when mirrored), a fang continuing from its tip.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (ownBone || c.Ordinal < 0) return;    // the octopus siphon: OctopusMeshBuilder
            CreatureSpecies sp = m.Species;
            CreatureSim sim = m.Rest;
            BodyAppendage a = sp.appendages[c.Ordinal];
            int n = m.Joints;
            int j = Mathf.Clamp(a.joint, 0, n - 1);
            for (int side = 0; side < (a.mirror ? 2 : 1); side++)
            {
                float mx = a.mirror && side == 0 ? -1f : 1f;
                Vector3 o = a.offset, d = a.direction; o.x *= mx; d.x *= mx;
                Quaternion r = sim.Rotations[j];
                Vector3 dir = r * (d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward);
                Vector3 up = Vector3.ProjectOnPlane(r * Vector3.up, dir);
                if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(r * Vector3.forward, dir);
                Quaternion q = Quaternion.LookRotation(dir, up.normalized);
                Vector3 b = sim.Joints[j] + r * o;
                float re = a.fangLength > 0f ? Mathf.Max(a.radiusEnd, PartMeshEmitter.MinRadius) : a.radiusEnd;
                m.Horn(b, q, a.radiusStart, re, a.length, a.curveDeg, j, PartMeshEmitter.PartLeg, kSides, 0f, out Vector3 tip, out Vector3 tipDir);
                if (a.fangLength > 0f)
                {
                    Vector3 fu = Vector3.ProjectOnPlane(q * Vector3.up, tipDir);
                    if (fu.sqrMagnitude < 1e-8f) fu = q * Vector3.up;
                    m.Horn(tip, Quaternion.LookRotation(tipDir, fu.normalized), re, 0f, a.fangLength, 70f, j, PartMeshEmitter.PartFoot, kSides, a.length, out _, out _);
                }
            }
        }
    }

    public sealed partial class FinStrip
    {
        public override int MeshOrder => 4;

        // A membrane strip on the rest spine's sections (FinStripMesh), skinned to the spine.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSpecies sp = m.Species;
            Func<float, SpineSection> at = m.SpineAt;
            FinStripMesh.Build(m.Lists, sp.fins[c.Ordinal], at, m.Joints, PartMeshEmitter.PartMembrane);
        }
    }

    public sealed partial class Antenna
    {
        public override int MeshOrder => 5;

        // A tapered tube along the rest strand, each ring between its two bones (AntennaMesh).
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureAntennae an = m.Rest.Antennae;
            if (an == null) return;
            int a = c.Ordinal;
            AntennaMesh.Build(m.Lists, an.Spec(a), an.Points, an.FirstPoint(a), m.Species.AntennaBone(a, 0), PartMeshEmitter.PartMembrane);
        }
    }

    public sealed partial class Wing
    {
        public override int MeshOrder => 6;

        // The wing arm, its three fingers and the double-sided membrane fan (WingedMeshBuilder's geometry, on the
        // emitter's spread rest pose).
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Wings == null || c.Ordinal >= m.WingSpecs.Length) return;
            WingedMeshBuilder.EmitWing(m, c.Ordinal);
        }
    }
}
