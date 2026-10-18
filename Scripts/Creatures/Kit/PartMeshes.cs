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
        const int kHeadCapRings = 4, kTailRings = 8;
        const float kTailTipSlope = -1.5f; // d(radius / end radius) / d(k) at the tail tip: a fine point
        const float kSnoutNarrow = 0.2f;   // extra width taper toward the snout tip (fraction at the tip)
        const float kSnoutFlat = 0.35f;    // extra height taper: the snout is flatter than it is wide
        const float kSnoutDrop = 0.15f;    // the snout's centre line drops by this fraction of head height

        public override int MeshOrder => 0;

        struct Section { public Vector3 c, t, x, y; public float a, b, d; }

        // Body rings from the snout tip to the tail tip, consecutive rings of ringSegments + 1 vertices (seam
        // duplicated; each tip a ring of identical vertices), skinned to the spine joints.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c) => new BodyMesh(m).Emit();

        readonly struct BodyMesh
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

            // Cubic Hermite from 1 (slope m0, matching the body) to 0 (slope kTailTipSlope); monotone for
            // m0 in [kTailTipSlope, 0].
            static float TailProfile(float k, float m0)
            {
                float k2 = k * k, k3 = k2 * k;
                return Mathf.Max(0f, (2f * k3 - 3f * k2 + 1f) + m0 * (k3 - 2f * k2 + k) + kTailTipSlope * (k3 - k2));
            }

            int BodyRing(float s, Vector4 skin, int segs)
            {
                float s0 = Mathf.Max(0f, s - PartMeshEmitter.DiffStep), s1 = Mathf.Min(BodyLen, s + PartMeshEmitter.DiffStep);
                Section p = BodyAt(s0), q = BodyAt(s1), c = BodyAt(s);
                float ds = s1 - s0; // toward the head = decreasing s
                return m.Ring(c.c, c.x, c.y, c.t, c.a, c.b, c.d, (p.a - q.a) / ds, (p.b - q.b) / ds, (p.d - q.d) / ds,
                              s, PartMeshEmitter.PartBody, skin, segs, true);
            }

            public void Emit()
            {
                int segs = Mathf.Max(3, sp.ringSegments);
                int rps = Mathf.Max(1, sp.ringsPerSegment);
                float lh = HeadLen;
                var rings = new List<int>();

                Section tip = BodyAt(0f);
                rings.Add(m.Pole(tip.c + tip.y * tip.d, tip.t, 0f, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(0), segs));
                for (int k = kHeadCapRings; k >= 1; k--)
                {
                    float theta = 0.5f * Mathf.PI * k / (kHeadCapRings + 1);
                    rings.Add(BodyRing(lh - lh * Mathf.Sin(theta), PartMeshEmitter.Rigid(0), segs));
                }
                for (int i = 0; i < n - 1; i++)
                    for (int r = 0; r < rps; r++)
                    {
                        float f = (float)r / rps;
                        rings.Add(BodyRing(lh + sp.spacing * (i + f), PartMeshEmitter.Blend(i, i + 1, f * f * (3f - 2f * f)), segs));
                    }
                rings.Add(BodyRing(lh + SpineLen, PartMeshEmitter.Rigid(n - 1), segs));
                for (int k = 1; k <= kTailRings; k++)
                    rings.Add(BodyRing(lh + SpineLen + TailLen * k / (kTailRings + 1), PartMeshEmitter.Rigid(n - 1), segs));
                Section end = BodyAt(BodyLen);
                rings.Add(m.Pole(end.c, -end.t, BodyLen, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(n - 1), segs));

                // rings run snout -> tail, i.e. along -t, with x = cross(y, t): flip to face outward
                for (int r = 0; r + 1 < rings.Count; r++)
                    m.Stitch(rings[r], rings[r + 1], segs, r == 0, r + 2 == rings.Count, true);
            }
        }
    }

    public sealed partial class Leg2Bone
    {
        const int kLegSegments = 8;
        const int kKneeRings = 7;          // rings along the knee curve, ends included
        const float kShoulderBlob = 0.6f;  // the shoulder end extends this * upperRadius into the body
        const float kAnkleCap = 0.6f;      // rounded end past the foot point, * lowerRadius
        const int kPadBands = 6;
        const int kToeSegments = 6;
        const float kToeFanDeg = 40f;

        public override int MeshOrder => 1;

        struct LegSample { public Vector3 c, t; public float sigma; public Vector4 skin; public bool pole; }

        // Tube shoulder (a blob buried in the body) -> rounded knee -> ankle cap, then the foot pad and toes. A wing
        // arm is drawn by its Wing.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            int l = c.Ordinal;
            if (m.IsWingArm(l)) return;
            CreatureSpecies sp = m.Species;
            LegSpec s = sp.legs[l];
            if (s.IsArthropod) { ArthropodLeg.Emit(m, l); return; }   // the runtime leg decides (as before the kit)
            Matrix4x4[] bones = m.Bones;
            LegState st = m.Rest.Legs[l];
            int attach = s.attachJoint, bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1), bf = sp.LegBone(l, 2);
            Vector3 S = st.shoulder, E = st.elbow, F = st.foot;
            float lu = Mathf.Max(1e-4f, (E - S).magnitude), ll = Mathf.Max(1e-4f, (F - E).magnitude);
            Vector3 du = bones[bu].rotation * Vector3.forward, dl = bones[bl].rotation * Vector3.forward;
            float ru = Mathf.Max(PartMeshEmitter.MinRadius, s.upperRadius), rl = Mathf.Max(PartMeshEmitter.MinRadius, s.lowerRadius);
            float blob = kShoulderBlob * ru, ankle = kAnkleCap * rl, total = lu + ll;

            float Radius(float sg)
            {
                if (sg < 0f) { float q = sg / blob; return ru * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                if (sg > total) { float q = (sg - total) / ankle; return rl * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                return Mathf.Lerp(ru, rl, sg / total);
            }

            var samples = new List<LegSample>();
            void Add(Vector3 cc, Vector3 t, float sigma, Vector4 skin, bool pole = false) =>
                samples.Add(new LegSample { c = cc, t = t, sigma = sigma, skin = skin, pole = pole });

            // shoulder blob: a rounded end buried in the body; it and the shoulder ring blend 50/50 body/limb
            Vector4 shoulderSkin = PartMeshEmitter.Blend(attach, bu, 0.5f);
            Add(S - du * blob, du, -blob, shoulderSkin, true);
            Add(S - du * (blob * Mathf.Sin(60f * Mathf.Deg2Rad)), du, -blob * Mathf.Sin(60f * Mathf.Deg2Rad), shoulderSkin);
            Add(S - du * (blob * Mathf.Sin(30f * Mathf.Deg2Rad)), du, -blob * Mathf.Sin(30f * Mathf.Deg2Rad), shoulderSkin);
            Add(S, du, 0f, shoulderSkin);
            Add(S + du * (0.4f * lu), du, 0.4f * lu, PartMeshEmitter.Rigid(bu));
            // knee: the last and first 35% of the bones follow a rounded curve and blend to 50/50 at the elbow
            float kb = s.KneeBlend; // fraction of each limb bone blended at the knee (LegSpec.kneeBlend)
            Vector3 k0 = S + du * ((1f - kb) * lu), k2 = E + dl * (kb * ll);
            for (int i = 0; i < kKneeRings; i++)
            {
                float t = i / (kKneeRings - 1f), w = t * t * (3f - 2f * t);
                Vector3 cc = (1f - t) * (1f - t) * k0 + 2f * (1f - t) * t * E + t * t * k2;
                Vector3 tan = 2f * (1f - t) * (E - k0) + 2f * t * (k2 - E);
                tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : du;
                Add(cc, tan, Mathf.Lerp((1f - kb) * lu, lu + kb * ll, t), PartMeshEmitter.Blend(bu, bl, w));
            }
            Add(E + dl * (0.6f * ll), dl, lu + 0.6f * ll, PartMeshEmitter.Rigid(bl));
            Add(F, dl, total, PartMeshEmitter.Rigid(bl));
            // ankle: rounded end past the foot point (inside the pad / under the ground)
            Add(F + dl * (ankle * Mathf.Sin(30f * Mathf.Deg2Rad)), dl, total + ankle * Mathf.Sin(30f * Mathf.Deg2Rad), PartMeshEmitter.Rigid(bl));
            Add(F + dl * (ankle * Mathf.Sin(60f * Mathf.Deg2Rad)), dl, total + ankle * Mathf.Sin(60f * Mathf.Deg2Rad), PartMeshEmitter.Rigid(bl));
            Add(F + dl * ankle, dl, total + ankle, PartMeshEmitter.Rigid(bl), true);

            // rotation-minimising frame transported from the upper bone's frame
            Vector3 x = bones[bu].rotation * Vector3.right;
            int prev = -1;
            for (int i = 0; i < samples.Count; i++)
            {
                LegSample q = samples[i];
                Vector3 xt = Vector3.ProjectOnPlane(x, q.t);
                x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
                Vector3 y = Vector3.Cross(q.t, x);
                float u = q.sigma + blob;
                int cur;
                if (q.pole) cur = m.Pole(q.c, i == 0 ? -q.t : q.t, u, PartMeshEmitter.PartLeg, (i == 0 ? -q.t : q.t).y, q.skin, kLegSegments);
                else
                {
                    float r = Radius(q.sigma);
                    float dr = (Radius(q.sigma + PartMeshEmitter.DiffStep) - Radius(q.sigma - PartMeshEmitter.DiffStep)) / (2f * PartMeshEmitter.DiffStep);
                    cur = m.Ring(q.c, x, y, q.t, r, r, 0f, dr, dr, 0f, u, PartMeshEmitter.PartLeg, q.skin, kLegSegments, false);
                }
                if (prev >= 0) m.Stitch(prev, cur, kLegSegments, samples[i - 1].pole, q.pole, false);
                prev = cur;
            }

            Foot(m, s, bf);
        }

        // Flattened ellipsoid pad plus toe nubs, all rigid to the foot bone (authored in its frame).
        static void Foot(PartMeshEmitter m, LegSpec s, int bf)
        {
            Matrix4x4 frame = m.Bones[bf];
            float fl = Mathf.Max(1e-3f, s.footLength);
            float ax = 0.3f * fl, az = 0.5f * fl, ay = Mathf.Max(0.4f * s.lowerRadius, 0.12f * fl);
            Vector3 pc = new Vector3(0f, 0.6f * ay, 0.15f * fl);
            m.Ellipsoid(frame, pc, ax, ay, az, kLegSegments, kPadBands, PartMeshEmitter.PartFoot, bf);

            int toes = Mathf.Max(0, s.toeCount);
            float toeLen = 0.5f * fl, r0 = 0.55f * ay, drop = 0.15f * ay - pc.y;
            Vector4 skin = PartMeshEmitter.Rigid(bf);
            for (int i = 0; i < toes; i++)
            {
                float yaw = toes == 1 ? 0f : Mathf.Lerp(-kToeFanDeg, kToeFanDeg, i / (toes - 1f));
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
                float ext = 1f / Mathf.Sqrt(dir.x * dir.x / (ax * ax) + dir.z * dir.z / (az * az));
                Vector3 st = pc + dir * (0.45f * ext);
                Vector3 C(float q) => st + dir * (toeLen * q) + Vector3.up * (drop * q * q);
                Vector3 D(float q) => dir * toeLen + Vector3.up * (2f * drop * q);
                float R(float q) => r0 * Mathf.Pow(Mathf.Max(0f, 1f - q), 0.6f);

                Vector3 back = frame.MultiplyVector(-dir);
                int prev = m.Pole(frame.MultiplyPoint3x4(st - dir * (0.6f * r0)), back, 0f, PartMeshEmitter.PartFoot, back.y, skin, kToeSegments);
                float[] qs = { 0f, 0.35f, 0.7f };
                foreach (float q in qs)
                {
                    Vector3 dq = D(q);
                    Vector3 t = dq.normalized;
                    Vector3 x = Vector3.Cross(Vector3.up, t).normalized;
                    Vector3 y = Vector3.Cross(t, x);
                    float dr = (R(q + PartMeshEmitter.DiffStep) - R(Mathf.Max(0f, q - PartMeshEmitter.DiffStep))) / ((q > 0f ? 2f : 1f) * PartMeshEmitter.DiffStep) / dq.magnitude;
                    float r = R(q);
                    int cur = m.Ring(frame.MultiplyPoint3x4(C(q)), frame.MultiplyVector(x), frame.MultiplyVector(y), frame.MultiplyVector(t),
                                     r, r, 0f, dr, dr, 0f, 0.6f * r0 + toeLen * q, PartMeshEmitter.PartFoot, skin, kToeSegments, false);
                    m.Stitch(prev, cur, kToeSegments, q == 0f, false, false);
                    prev = cur;
                }
                Vector3 tipT = frame.MultiplyVector(D(1f).normalized);
                int tip = m.Pole(frame.MultiplyPoint3x4(C(1f)), tipT, 0.6f * r0 + toeLen, PartMeshEmitter.PartFoot, tipT.y, skin, kToeSegments);
                m.Stitch(prev, tip, kToeSegments, false, true, false);
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
