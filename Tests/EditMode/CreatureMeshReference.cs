// Creature kit T3.1: a FROZEN copy of the mesh builders as they were before the part emitters (CreatureMeshBuilder
// and WingedMeshBuilder on feat/creature-kit e7e4319, plus CreatureRestPose.BoneWorld), kept only as the reference
// the emitter path must reproduce byte for byte (CreatureMeshEmitterTests.Mesh_Bytes_EqualLegacy). Comparing two
// builds in the same runtime makes the check runtime-independent (no per-runtime golden constants). Do not edit:
// a change here is a change of the reference. Delete together with the legacy species fields (T5.11).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TerrainGen.Tests
{

// Builds a species' skinned mesh once, in the rest pose (CreatureRestPose).
//   position, normal: rest space
//   uv0 = (arc length along the part in metres, angle / 2pi)
//   uv1 = (part id, dorsal coordinate: -1 belly .. +1 spine ridge)
//   uv2 = (bone0, bone1, w0, w1), w0 + w1 = 1
// Layout: body vertices first, as consecutive rings of ringSegments + 1 vertices (seam duplicated)
// from the snout tip to the tail tip (each tip is a ring of identical vertices), then the legs
// (tube, foot pad, toes, per leg), then the two eyes. Vertices are shared along a part, so normals
// are continuous across ring boundaries.
internal static class LegacyCreatureMeshBuilder
{
    public const int PartBody = 0, PartLeg = 1, PartFoot = 2, PartEye = 3;

    const int kHeadCapRings = 4, kTailRings = 8;
    const float kTailTipSlope = -1.5f; // d(radius / end radius) / d(k) at the tail tip: a fine point
    const float kSnoutNarrow = 0.2f;   // extra width taper toward the snout tip (fraction at the tip)
    const float kSnoutFlat = 0.35f;    // extra height taper: the snout is flatter than it is wide
    const float kSnoutDrop = 0.15f;    // the snout's centre line drops by this fraction of head height

    const int kLegSegments = 8;
    const int kKneeRings = 7;          // rings along the knee curve, ends included
    const float kShoulderBlob = 0.6f;  // the shoulder end extends this * upperRadius into the body
    const float kAnkleCap = 0.6f;      // rounded end past the foot point, * lowerRadius

    const int kPadBands = 6;
    const int kToeSegments = 6;
    const float kToeFanDeg = 40f;

    const int kEyeAround = 8, kEyeBands = 6;

    const float kMinRadius = 1e-4f;
    const float kDiffStep = 1e-4f;

    public static Mesh Build(CreatureSpecies sp)
    {
        sp = sp.Compiled.Runtime;   // creature kit: the runtime view (a legacy asset is its own)
        var b = new Builder(sp);
        b.Body();
        for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++) b.Leg(l);
        b.Eyes();
        b.Appendages();
        b.Fins();
        b.Antennae();
        return b.ToMesh();
    }

    sealed class Builder
    {
        readonly CreatureSpecies sp;
        readonly CreatureSim sim;
        readonly Matrix4x4[] bones;
        readonly int n;
        readonly CreatureMeshLists ml = new CreatureMeshLists();   // shared with ArthropodLegMesh
        readonly List<Vector3> pos, nrm;
        readonly List<Vector2> uv0, uv1;
        readonly List<Vector4> uv2;
        readonly List<int> tris;

        public Builder(CreatureSpecies sp)
        {
            this.sp = sp;
            sim = CreatureRestPose.Sim(sp);
            bones = CreatureRestPose.BoneWorld(sim);
            n = sp.JointCount;
            pos = ml.pos; nrm = ml.nrm; uv0 = ml.uv0; uv1 = ml.uv1; uv2 = ml.uv2; tris = ml.tris;
        }

        // ---------------------------------------------------------------- primitives

        void Vertex(Vector3 p, Vector3 normal, float u, float v, int part, float dorsal, Vector4 skin)
        {
            pos.Add(p); nrm.Add(normal);
            uv0.Add(new Vector2(u, v)); uv1.Add(new Vector2(part, Mathf.Clamp(dorsal, -1f, 1f))); uv2.Add(skin);
        }

        static Vector4 Rigid(int bone) => new Vector4(bone, bone, 1f, 0f);
        // bone0 is always the heavier bone (the nearest rigid frame of the vertex)
        static Vector4 Blend(int b0, int b1, float w1) => w1 > 0.5f ? new Vector4(b1, b0, w1, 1f - w1) : new Vector4(b0, b1, 1f - w1, w1);

        // One elliptical ring of m + 1 vertices (seam duplicated), starting at the belly (phi = -pi/2):
        //   p = c + x a cos(phi) + y (d + b sin(phi))
        // The normal is the ellipse normal (x cos/a + y sin/b) tilted along t by the taper slopes:
        //   da, db = d(semi-axis)/d(distance along t), dd = d(centre offset along y)/d(distance along t).
        int Ring(Vector3 c, Vector3 x, Vector3 y, Vector3 t, float a, float b, float d,
                 float da, float db, float dd, float u, int part, Vector4 skin, int m, bool dorsalFromAngle)
        {
            int first = pos.Count;
            a = Mathf.Max(a, kMinRadius); b = Mathf.Max(b, kMinRadius);
            for (int k = 0; k <= m; k++)
            {
                float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / m;
                float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                if (k == m) { cs = 0f; sn = -1f; } // exact seam duplicate
                Vector3 p = c + x * (a * cs) + y * (d + b * sn);
                float tilt = -(cs * cs * da / a + sn * sn * db / b) - sn * dd / b;
                Vector3 nn = (x * (cs / a) + y * (sn / b) + t * tilt).normalized;
                Vertex(p, nn, u, (float)k / m, part, dorsalFromAngle ? sn : nn.y, skin);
            }
            return first;
        }

        // A closed end: m + 1 copies of the tip (angle uv at the middle of each quad).
        int Pole(Vector3 p, Vector3 normal, float u, int part, float dorsal, Vector4 skin, int m)
        {
            int first = pos.Count;
            for (int k = 0; k <= m; k++) Vertex(p, normal, u, (k + 0.5f) / m, part, dorsal, skin);
            return first;
        }

        // Quads between ring A and the next ring B. Outward when cross(ring tangent at phi=0, A->B)
        // points along x (ring basis x = cross(y, A->B)); flip otherwise.
        void Stitch(int a, int b, int m, bool poleA, bool poleB, bool flip)
        {
            for (int k = 0; k < m; k++)
            {
                if (!poleA) Tri(a + k, a + k + 1, b + k, flip);
                if (!poleB) Tri(poleA ? a + k : a + k + 1, b + k + 1, b + k, flip);
            }
        }

        void Tri(int i0, int i1, int i2, bool flip)
        {
            tris.Add(i0);
            if (flip) { tris.Add(i2); tris.Add(i1); } else { tris.Add(i1); tris.Add(i2); }
        }

        // Ellipsoid about local +Z (poles at -az and +az) in the rigid frame m, rigid to one bone.
        void Ellipsoid(Matrix4x4 frame, Vector3 centre, float ax, float ay, float az, int around, int bands, int part, int bone)
        {
            Vector4 skin = Rigid(bone);
            int prev = -1;
            for (int j = 0; j <= bands; j++)
            {
                float beta = -0.5f * Mathf.PI + Mathf.PI * j / bands;
                float u = az * (beta + 0.5f * Mathf.PI);
                int cur;
                if (j == 0 || j == bands)
                {
                    float z = j == 0 ? -1f : 1f;
                    Vector3 nn = frame.MultiplyVector(new Vector3(0f, 0f, z)).normalized;
                    cur = Pole(frame.MultiplyPoint3x4(centre + new Vector3(0f, 0f, az * z)), nn, u, part, nn.y, skin, around);
                }
                else
                {
                    cur = pos.Count;
                    float cb = Mathf.Cos(beta), sb = Mathf.Sin(beta);
                    for (int k = 0; k <= around; k++)
                    {
                        float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / around;
                        float cs = k == around ? 0f : Mathf.Cos(phi), sn = k == around ? -1f : Mathf.Sin(phi);
                        Vector3 lp = centre + new Vector3(ax * cb * cs, ay * cb * sn, az * sb);
                        Vector3 nn = frame.MultiplyVector(new Vector3(cb * cs / ax, cb * sn / ay, sb / az)).normalized;
                        Vertex(frame.MultiplyPoint3x4(lp), nn, u, (float)k / around, part, nn.y, skin);
                    }
                }
                if (prev >= 0) Stitch(prev, cur, around, j == 1, j == bands, false);
                prev = cur;
            }
        }

        // ---------------------------------------------------------------- body

        struct Section { public Vector3 c, t, x, y; public float a, b, d; }

        float HeadLen => Mathf.Max(sp.headCapLength, 1e-3f);
        float TailLen => Mathf.Max(sp.tailTipLength, 1e-3f);
        float SpineLen => sp.spacing * (n - 1);
        float BodyLen => HeadLen + SpineLen + TailLen;

        Vector3 J(int i)
        {
            Vector3[] p = sim.Joints;
            if (i < 0) return 2f * p[0] - p[1];
            if (i > n - 1) return 2f * p[n - 1] - p[n - 2];
            return p[i];
        }

        static float CR(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);
        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
        static Vector3 CRd(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * ((p2 - p0) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t + 3f * (3f * p1 - p0 - 3f * p2 + p3) * (t * t));

        float HW(int i) => sp.joints[Mathf.Clamp(i, 0, n - 1)].halfWidth;
        float HH(int i) => sp.joints[Mathf.Clamp(i, 0, n - 1)].halfHeight;

        // Spine section at u in joint units (0 = head joint .. n-1 = last joint); t points to the head.
        Section Spine(float u)
        {
            u = Mathf.Clamp(u, 0f, n - 1);
            int i = Mathf.Min((int)u, n - 2);
            float f = u - i;
            Vector3 p0 = J(i - 1), p1 = J(i), p2 = J(i + 1), p3 = J(i + 2);
            Vector3 tan = -CRd(p0, p1, p2, p3, f);
            Quaternion r0 = sim.Rotations[i], r1 = sim.Rotations[i + 1];
            Vector3 t = tan.sqrMagnitude > 1e-12f ? tan.normalized : Quaternion.Slerp(r0, r1, f) * Vector3.forward;
            Vector3 up = Vector3.Slerp(r0 * Vector3.up, r1 * Vector3.up, f);
            Vector3 y = Vector3.ProjectOnPlane(up, t);
            y = y.sqrMagnitude > 1e-10f ? y.normalized : Vector3.ProjectOnPlane(Vector3.up, t).normalized;
            return new Section
            {
                c = CR(p0, p1, p2, p3, f), t = t, y = y, x = Vector3.Cross(y, t),
                a = Mathf.Max(kMinRadius, CR(HW(i - 1), HW(i), HW(i + 1), HW(i + 2), f)),
                b = Mathf.Max(kMinRadius, CR(HH(i - 1), HH(i), HH(i + 1), HH(i + 2), f)),
            };
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
            tl.a *= TailProfile(k, TaperSlope(HW(n - 2), HW(n - 1)));
            tl.b *= TailProfile(k, TaperSlope(HH(n - 2), HH(n - 1)));
            return tl;
        }

        // The body profile's slope at the last joint (Catmull-Rom with clamped ends: half the last
        // difference per segment), in tail-profile units d(r / r_end) / d(k), k = distance / tailTipLength.
        float TaperSlope(float prev, float end)
        {
            float perMetre = 0.5f * (end - prev) / sp.spacing;
            return Mathf.Clamp(perMetre * TailLen / Mathf.Max(end, kMinRadius), kTailTipSlope, 0f);
        }

        // Cubic Hermite from 1 (slope m0, matching the body) to 0 (slope kTailTipSlope); monotone for
        // m0 in [kTailTipSlope, 0].
        static float TailProfile(float k, float m0)
        {
            float k2 = k * k, k3 = k2 * k;
            return Mathf.Max(0f, (2f * k3 - 3f * k2 + 1f) + m0 * (k3 - 2f * k2 + k) + kTailTipSlope * (k3 - k2));
        }

        int BodyRing(float s, Vector4 skin, int m)
        {
            float s0 = Mathf.Max(0f, s - kDiffStep), s1 = Mathf.Min(BodyLen, s + kDiffStep);
            Section p = BodyAt(s0), q = BodyAt(s1), c = BodyAt(s);
            float ds = s1 - s0; // toward the head = decreasing s
            return Ring(c.c, c.x, c.y, c.t, c.a, c.b, c.d, (p.a - q.a) / ds, (p.b - q.b) / ds, (p.d - q.d) / ds,
                        s, PartBody, skin, m, true);
        }

        public void Body()
        {
            int m = Mathf.Max(3, sp.ringSegments);
            int rps = Mathf.Max(1, sp.ringsPerSegment);
            float lh = HeadLen;
            var rings = new List<int>();

            Section tip = BodyAt(0f);
            rings.Add(Pole(tip.c + tip.y * tip.d, tip.t, 0f, PartBody, 0f, Rigid(0), m));
            for (int k = kHeadCapRings; k >= 1; k--)
            {
                float theta = 0.5f * Mathf.PI * k / (kHeadCapRings + 1);
                rings.Add(BodyRing(lh - lh * Mathf.Sin(theta), Rigid(0), m));
            }
            for (int i = 0; i < n - 1; i++)
                for (int r = 0; r < rps; r++)
                {
                    float f = (float)r / rps;
                    rings.Add(BodyRing(lh + sp.spacing * (i + f), Blend(i, i + 1, f * f * (3f - 2f * f)), m));
                }
            rings.Add(BodyRing(lh + SpineLen, Rigid(n - 1), m));
            for (int k = 1; k <= kTailRings; k++)
                rings.Add(BodyRing(lh + SpineLen + TailLen * k / (kTailRings + 1), Rigid(n - 1), m));
            Section end = BodyAt(BodyLen);
            rings.Add(Pole(end.c, -end.t, BodyLen, PartBody, 0f, Rigid(n - 1), m));

            // rings run snout -> tail, i.e. along -t, with x = cross(y, t): flip to face outward
            for (int r = 0; r + 1 < rings.Count; r++)
                Stitch(rings[r], rings[r + 1], m, r == 0, r + 2 == rings.Count, true);
        }

        // ---------------------------------------------------------------- legs

        struct LegSample { public Vector3 c, t; public float sigma; public Vector4 skin; public bool pole; }

        public void Leg(int l)
        {
            LegSpec s = sp.legs[l];
            if (s.IsArthropod)
            {
                // rigid segment capsules, knuckles, bands and claws in the rest pose the sim solved
                ArthropodLegMesh.Build(ml, s.segments, sim.LegPoints, sim.LegPointOffset(l), sim.LegPlaneNormal(l), sp.LegBone(l, 0),
                                       s.attachJoint, s.legSides > 0 ? s.legSides : 8, s.toeCount, s.footLength);
                return;
            }
            LegState st = sim.Legs[l];
            int attach = s.attachJoint, bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1), bf = sp.LegBone(l, 2);
            Vector3 S = st.shoulder, E = st.elbow, F = st.foot;
            float lu = Mathf.Max(1e-4f, (E - S).magnitude), ll = Mathf.Max(1e-4f, (F - E).magnitude);
            Vector3 du = bones[bu].rotation * Vector3.forward, dl = bones[bl].rotation * Vector3.forward;
            float ru = Mathf.Max(kMinRadius, s.upperRadius), rl = Mathf.Max(kMinRadius, s.lowerRadius);
            float blob = kShoulderBlob * ru, ankle = kAnkleCap * rl, total = lu + ll;

            float Radius(float sg)
            {
                if (sg < 0f) { float q = sg / blob; return ru * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                if (sg > total) { float q = (sg - total) / ankle; return rl * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                return Mathf.Lerp(ru, rl, sg / total);
            }

            var samples = new List<LegSample>();
            void Add(Vector3 c, Vector3 t, float sigma, Vector4 skin, bool pole = false) =>
                samples.Add(new LegSample { c = c, t = t, sigma = sigma, skin = skin, pole = pole });

            // shoulder blob: a rounded end buried in the body; it and the shoulder ring blend 50/50 body/limb
            Vector4 shoulderSkin = Blend(attach, bu, 0.5f);
            Add(S - du * blob, du, -blob, shoulderSkin, true);
            Add(S - du * (blob * Mathf.Sin(60f * Mathf.Deg2Rad)), du, -blob * Mathf.Sin(60f * Mathf.Deg2Rad), shoulderSkin);
            Add(S - du * (blob * Mathf.Sin(30f * Mathf.Deg2Rad)), du, -blob * Mathf.Sin(30f * Mathf.Deg2Rad), shoulderSkin);
            Add(S, du, 0f, shoulderSkin);
            Add(S + du * (0.4f * lu), du, 0.4f * lu, Rigid(bu));
            // knee: the last and first 35% of the bones follow a rounded curve and blend to 50/50 at the elbow
            float kb = s.KneeBlend; // fraction of each limb bone blended at the knee (LegSpec.kneeBlend)
            Vector3 k0 = S + du * ((1f - kb) * lu), k2 = E + dl * (kb * ll);
            for (int i = 0; i < kKneeRings; i++)
            {
                float t = i / (kKneeRings - 1f), w = t * t * (3f - 2f * t);
                Vector3 c = (1f - t) * (1f - t) * k0 + 2f * (1f - t) * t * E + t * t * k2;
                Vector3 tan = 2f * (1f - t) * (E - k0) + 2f * t * (k2 - E);
                tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : du;
                Add(c, tan, Mathf.Lerp((1f - kb) * lu, lu + kb * ll, t), Blend(bu, bl, w));
            }
            Add(E + dl * (0.6f * ll), dl, lu + 0.6f * ll, Rigid(bl));
            Add(F, dl, total, Rigid(bl));
            // ankle: rounded end past the foot point (inside the pad / under the ground)
            Add(F + dl * (ankle * Mathf.Sin(30f * Mathf.Deg2Rad)), dl, total + ankle * Mathf.Sin(30f * Mathf.Deg2Rad), Rigid(bl));
            Add(F + dl * (ankle * Mathf.Sin(60f * Mathf.Deg2Rad)), dl, total + ankle * Mathf.Sin(60f * Mathf.Deg2Rad), Rigid(bl));
            Add(F + dl * ankle, dl, total + ankle, Rigid(bl), true);

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
                if (q.pole) cur = Pole(q.c, i == 0 ? -q.t : q.t, u, PartLeg, (i == 0 ? -q.t : q.t).y, q.skin, kLegSegments);
                else
                {
                    float r = Radius(q.sigma);
                    float dr = (Radius(q.sigma + kDiffStep) - Radius(q.sigma - kDiffStep)) / (2f * kDiffStep);
                    cur = Ring(q.c, x, y, q.t, r, r, 0f, dr, dr, 0f, u, PartLeg, q.skin, kLegSegments, false);
                }
                if (prev >= 0) Stitch(prev, cur, kLegSegments, samples[i - 1].pole, q.pole, false);
                prev = cur;
            }

            Foot(s, bf);
        }

        // Flattened ellipsoid pad plus toe nubs, all rigid to the foot bone (authored in its frame).
        void Foot(LegSpec s, int bf)
        {
            Matrix4x4 frame = bones[bf];
            float fl = Mathf.Max(1e-3f, s.footLength);
            float ax = 0.3f * fl, az = 0.5f * fl, ay = Mathf.Max(0.4f * s.lowerRadius, 0.12f * fl);
            Vector3 pc = new Vector3(0f, 0.6f * ay, 0.15f * fl);
            Ellipsoid(frame, pc, ax, ay, az, kLegSegments, kPadBands, PartFoot, bf);

            int toes = Mathf.Max(0, s.toeCount);
            float toeLen = 0.5f * fl, r0 = 0.55f * ay, drop = 0.15f * ay - pc.y;
            Vector4 skin = Rigid(bf);
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
                int prev = Pole(frame.MultiplyPoint3x4(st - dir * (0.6f * r0)), back, 0f, PartFoot, back.y, skin, kToeSegments);
                float[] qs = { 0f, 0.35f, 0.7f };
                foreach (float q in qs)
                {
                    Vector3 dq = D(q);
                    Vector3 t = dq.normalized;
                    Vector3 x = Vector3.Cross(Vector3.up, t).normalized;
                    Vector3 y = Vector3.Cross(t, x);
                    float dr = (R(q + kDiffStep) - R(Mathf.Max(0f, q - kDiffStep))) / ((q > 0f ? 2f : 1f) * kDiffStep) / dq.magnitude;
                    float r = R(q);
                    int cur = Ring(frame.MultiplyPoint3x4(C(q)), frame.MultiplyVector(x), frame.MultiplyVector(y), frame.MultiplyVector(t),
                                   r, r, 0f, dr, dr, 0f, 0.6f * r0 + toeLen * q, PartFoot, skin, kToeSegments, false);
                    Stitch(prev, cur, kToeSegments, q == 0f, false, false);
                    prev = cur;
                }
                Vector3 tipT = frame.MultiplyVector(D(1f).normalized);
                int tip = Pole(frame.MultiplyPoint3x4(C(1f)), tipT, 0.6f * r0 + toeLen, PartFoot, tipT.y, skin, kToeSegments);
                Stitch(prev, tip, kToeSegments, false, true, false);
            }
        }

        // ---------------------------------------------------------------- eyes

        public void Eyes()
        {
            float r = Mathf.Max(kMinRadius, sp.eyeRadius);
            for (int e = 0; e < 2; e++)
            {
                int bone = sp.EyeBone(e);
                Ellipsoid(bones[bone], Vector3.zero, r, r, r, kEyeAround, kEyeBands, PartEye, bone);
            }
            // an eye cluster (spiders): more eyes on the eye joint, each rigid to the eye bone of its side
            if (sp.eyeCluster != null)
                foreach (EyeSpot spot in sp.eyeCluster)
                {
                    float rs = Mathf.Max(kMinRadius, spot.radius);
                    for (int e = 0; e < 2; e++)
                    {
                        if (spot.offset.x == 0f && e == 1) break;   // on the midline: one eye
                        Vector3 o = spot.offset; o.x *= e == 0 ? -1f : 1f;
                        int bone = sp.EyeBone(e);
                        Vector3 c = sim.Joints[sp.eyeJoint] + sim.Rotations[sp.eyeJoint] * o;
                        Ellipsoid(Matrix4x4.TRS(c, bones[bone].rotation, Vector3.one), Vector3.zero, rs, rs, rs, kEyeAround, kEyeBands, PartEye, bone);
                    }
                }
        }

        // Rigid curved cones on spine joints (chelicerae with fangs, horns): BodyAppendage.
        public void Appendages()
        {
            if (sp.appendages == null) return;
            foreach (BodyAppendage a in sp.appendages)
            {
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
                    float re = a.fangLength > 0f ? Mathf.Max(a.radiusEnd, kMinRadius) : a.radiusEnd;
                    ArthropodLegMesh.Horn(ml, b, q, a.radiusStart, re, a.length, a.curveDeg, j, PartLeg, kLegSegments, 0f, out Vector3 tip, out Vector3 tipDir);
                    if (a.fangLength > 0f)
                    {
                        Vector3 fu = Vector3.ProjectOnPlane(q * Vector3.up, tipDir);
                        if (fu.sqrMagnitude < 1e-8f) fu = q * Vector3.up;
                        ArthropodLegMesh.Horn(ml, tip, Quaternion.LookRotation(tipDir, fu.normalized), re, 0f, a.fangLength, 70f, j, PartFoot, kLegSegments, a.length, out _, out _);
                    }
                }
            }
        }

        // Membrane strips along the spine (the kit's FinStrip): FinStripMesh on the rest spine's sections.
        public void Fins()
        {
            if (sp.fins == null || sp.fins.Length == 0) return;
            Func<float, SpineSection> at = u =>
            {
                Section s = Spine(u);
                return new SpineSection { c = s.c, t = s.t, x = s.x, y = s.y, a = s.a, b = s.b };
            };
            foreach (FinStripSpec f in sp.fins) FinStripMesh.Build(ml, f, at, n, WingedMeshBuilder.PartMembrane);
        }

        // Antennae (the kit's Antenna): tapered tubes along the rest strands, each ring between its two bones.
        public void Antennae()
        {
            CreatureAntennae an = sim.Antennae;
            if (an == null) return;
            for (int a = 0; a < an.Count; a++)
                AntennaMesh.Build(ml, an.Spec(a), an.Points, an.FirstPoint(a), sp.AntennaBone(a, 0), WingedMeshBuilder.PartMembrane);
        }

        public Mesh ToMesh()
        {
            var mesh = new Mesh { name = (sp != null ? sp.name : "") + " creature" };
            if (pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0, true);
            return mesh;
        }
    }
}


// Skinned mesh of a winged species (design section 6), built once in the rest pose:
//   body, hind legs, eyes   CreatureMeshBuilder on a copy of the species without the wing arms, its bone
//                           indices remapped to WingBoneLayout (legs keep their species index, eyes move
//                           past the wing bones)
//   wing arms               tubes shoulder -> rounded elbow -> wrist, a wrist knuckle and a thumb claw
//   fingers                 tapered tubes along the 3 finger bones
//   membrane (part 8)       double-sided: a fan from the wrist over 5 zones between neighbouring rays
//                           (finger 1-2, 2-3, finger 3-trailing rib, trailing rib-rear flank rib,
//                           rear flank rib-forearm), each a 6 x 8 grid skinned to its two rays (weights by
//                           the angular coordinate) and smooth-shaded as one sheet (normals averaged
//                           across zone borders), then the humerus patch, a fan from the elbow between
//                           the last zone's far edge and the humerus out to the flank. The free trailing
//                           edge is scalloped; the leading edge runs along the arm, shoulder -> elbow ->
//                           wrist, and stays on it however the elbow flexes (user feedback 2026-10-18):
//                           the forearm zone rides the arm sector (CreatureWings.ArmSector, exact on the
//                           forearm), the patch blends it into the humerus.
// Rest pose = the bind pose: CreatureRestPose for the spine and hind legs, the wings spread (stroke angle
// 0, unfolded) on the same spine. The ribs' uniform scale (CreatureWings.WriteBones) leaves the membrane
// normals exact.
// Vertex layout as CreatureMeshBuilder: uv0 (distance m, angle 0..1), uv1 (part, dorsal), uv2 (b0, b1, w0, w1).
internal static class LegacyWingedMeshBuilder
{
    // Part ids 4-7 belong to the octopus (arm, web, mantle, siphon); the membrane is 8 (CREATURE_PART_MEMBRANE).
    public const int PartMembrane = 8;
    public const int MembraneZones = 5, ZoneColumns = 6, ZoneRows = 8;
    // the five fan zones and the humerus patch, two sheets each
    public const int MembraneVerticesPerWing = (MembraneZones + 1) * (ZoneColumns + 1) * (ZoneRows + 1) * 2;

    const int kTubeSegments = 8, kFingerSegments = 6, kKneeRings = 7;
    const float kShoulderBlob = 0.6f, kWristCap = 0.8f;

    // Bone world matrices of the rest (bind) pose, WingBoneLayout order.
    public static Matrix4x4[] RestBoneWorld(CreatureSpecies sp, WingSpec[] wings)
    {
        CreatureSim sim = CreatureRestPose.Sim(sp);
        return RestBoneWorld(sp, wings, sim, out _);
    }

    public static Matrix4x4[] BindPoses(CreatureSpecies sp, WingSpec[] wings)
    {
        Matrix4x4[] m = RestBoneWorld(sp, wings);
        for (int i = 0; i < m.Length; i++) m[i] = m[i].inverse;
        return m;
    }

    static Matrix4x4[] RestBoneWorld(CreatureSpecies sp, WingSpec[] wings, CreatureSim sim, out CreatureWings cw)
    {
        // the sim writes spine, legs and eyes (CreatureSpecies' layout is WingBoneLayout); the spread wings
        // override the arms' leg bones and write the finger and rib bones
        var m = new Matrix4x4[sp.BoneCount];
        sim.WriteBoneWorld(m, 0);
        cw = new CreatureWings(sp, wings, sim.Joints, sim.Rotations, 1f);
        cw.WriteBones(m, 0);
        return m;
    }

    // A winged creature's bones from its sim (spine, legs, eyes) with these wings on top (tests).
    public static void WriteBones(CreatureSim sim, CreatureWings wings, Matrix4x4[] dst, int offset = 0)
    {
        sim.WriteBoneWorld(dst, offset);
        wings.WriteBones(dst, offset);
    }

    public static Mesh Build(CreatureSpecies sp) => Build(sp, sp.wings);
    public static Matrix4x4[] BindPoses(CreatureSpecies sp) => BindPoses(sp, sp.wings);

    public static Mesh Build(CreatureSpecies sp, WingSpec[] wings)
    {
        var b = new Builder(sp, wings);
        b.BodyAndHindLegs();
        for (int w = 0; w < b.WingCount; w++) { b.Arm(w); b.Fingers(w); b.Membrane(w); }
        return b.ToMesh();
    }

    sealed class Builder
    {
        readonly CreatureSpecies sp;
        readonly WingSpec[] wings;
        readonly CreatureWings cw;
        readonly Matrix4x4[] bones;
        readonly int n, legs;
        readonly List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>();
        readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
        readonly List<Vector4> uv2 = new List<Vector4>();
        readonly List<int> tris = new List<int>();

        public int WingCount => wings.Length;

        public Builder(CreatureSpecies sp, WingSpec[] wings)
        {
            this.sp = sp; this.wings = wings ?? new WingSpec[0];
            n = sp.JointCount; legs = sp.legs != null ? sp.legs.Length : 0;
            CreatureSim sim = CreatureRestPose.Sim(sp);
            bones = RestBoneWorld(sp, this.wings, sim, out cw);
        }

        bool IsWingArm(int leg)
        {
            for (int w = 0; w < wings.Length; w++) if (wings[w].leg == leg) return true;
            return false;
        }

        // ---------------------------------------------------------------- body (CreatureMeshBuilder)

        public void BodyAndHindLegs()
        {
            var keep = new List<int>();
            for (int l = 0; l < legs; l++) if (!IsWingArm(l)) keep.Add(l);
            var copy = ScriptableObject.CreateInstance<CreatureSpecies>();
            copy.name = sp.name;
            copy.scale = sp.scale; copy.spacing = sp.spacing; copy.joints = sp.joints;
            copy.headCapLength = sp.headCapLength; copy.tailTipLength = sp.tailTipLength;
            copy.legs = new LegSpec[keep.Count];
            for (int k = 0; k < keep.Count; k++) copy.legs[k] = sp.legs[keep[k]];
            copy.speed = sp.speed; copy.turnRateDeg = sp.turnRateDeg; copy.accel = sp.accel; copy.turnSlowdown = sp.turnSlowdown;
            copy.undulationDeg = sp.undulationDeg; copy.undulationHz = sp.undulationHz;
            copy.gaitSwayDeg = sp.gaitSwayDeg; copy.gaitRollDeg = sp.gaitRollDeg; copy.subSteps = sp.subSteps;
            copy.eyeJoint = sp.eyeJoint; copy.eyeOffset = sp.eyeOffset; copy.eyeRadius = sp.eyeRadius;
            copy.ringSegments = sp.ringSegments; copy.ringsPerSegment = sp.ringsPerSegment; copy.look = sp.look;
            // rigid cones and fin strips ride the spine bones (indices unchanged by the remap)
            copy.appendages = sp.appendages; copy.fins = sp.fins; copy.antennae = sp.antennae;
            Mesh body = LegacyCreatureMeshBuilder.Build(copy);
            int kl = keep.Count;
            UnityEngine.Object.DestroyImmediate(copy);

            var v = body.vertices; var nn = body.normals; var t = body.triangles;
            var a = new List<Vector2>(); var c = new List<Vector2>(); var s = new List<Vector4>();
            body.GetUVs(0, a); body.GetUVs(1, c); body.GetUVs(2, s);
            UnityEngine.Object.DestroyImmediate(body);
            int baseIndex = pos.Count;
            for (int i = 0; i < v.Length; i++)
            {
                Vector4 sk = s[i];
                sk.x = Remap((int)(sk.x + 0.5f), kl, keep);
                sk.y = Remap((int)(sk.y + 0.5f), kl, keep);
                pos.Add(v[i]); nrm.Add(nn[i]); uv0.Add(a[i]); uv1.Add(c[i]); uv2.Add(sk);
            }
            for (int i = 0; i < t.Length; i++) tris.Add(baseIndex + t[i]);
        }

        int Remap(int b, int keptLegs, List<int> keep)
        {
            if (b < n) return b;
            int r = b - n;
            if (r < 3 * keptLegs) return WingBoneLayout.LegBone(n, keep[r / 3], r % 3);
            r -= 3 * keptLegs;
            if (r < 2) return WingBoneLayout.EyeBone(n, legs, wings.Length, r);
            return sp.AntennaBone(0, 0) + r - 2;   // antenna bones: contiguous after the eyes in both layouts
        }

        // ---------------------------------------------------------------- primitives (after CreatureMeshBuilder)

        void Vertex(Vector3 p, Vector3 normal, float u, float v, int part, float dorsal, Vector4 skin)
        {
            pos.Add(p); nrm.Add(normal);
            uv0.Add(new Vector2(u, v)); uv1.Add(new Vector2(part, Mathf.Clamp(dorsal, -1f, 1f))); uv2.Add(skin);
        }

        static Vector4 Rigid(int bone) => new Vector4(bone, bone, 1f, 0f);
        static Vector4 Blend(int b0, int b1, float w1) => w1 > 0.5f ? new Vector4(b1, b0, w1, 1f - w1) : new Vector4(b0, b1, 1f - w1, w1);

        void Tri(int i0, int i1, int i2, bool flip)
        {
            tris.Add(i0);
            if (flip) { tris.Add(i2); tris.Add(i1); } else { tris.Add(i1); tris.Add(i2); }
        }

        struct Sample { public Vector3 c, t; public float r, dr, s; public Vector4 skin; public bool pole; }

        // A tube through the samples: circular rings in a rotation-minimising frame started from x0, poles
        // at the flagged ends. Outward winding (ring basis x, y = t x x).
        void Tube(List<Sample> q, Vector3 x0, int m, int part)
        {
            Vector3 x = x0;
            int prev = -1; bool prevPole = false;
            for (int i = 0; i < q.Count; i++)
            {
                Sample a = q[i];
                Vector3 xt = x - a.t * Vector3.Dot(x, a.t);
                x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
                Vector3 y = Vector3.Cross(a.t, x);
                int cur = pos.Count;
                if (a.pole)
                {
                    Vector3 pn = i == 0 ? -a.t : a.t;
                    for (int k = 0; k <= m; k++) Vertex(a.c, pn, a.s, (k + 0.5f) / m, part, pn.y, a.skin);
                }
                else
                    for (int k = 0; k <= m; k++)
                    {
                        float phi = 2f * Mathf.PI * k / m;
                        float cs = k == m ? 1f : Mathf.Cos(phi), sn = k == m ? 0f : Mathf.Sin(phi);
                        Vector3 radial = x * cs + y * sn;
                        Vector3 nn = (radial - a.t * a.dr).normalized;
                        Vertex(a.c + radial * Mathf.Max(1e-4f, a.r), nn, a.s, (float)k / m, part, nn.y, a.skin);
                    }
                if (prev >= 0)
                    for (int k = 0; k < m; k++)
                    {
                        if (!prevPole) Tri(prev + k, prev + k + 1, cur + k, false);
                        if (!a.pole) Tri(prevPole ? prev + k : prev + k + 1, cur + k + 1, cur + k, false);
                    }
                prev = cur; prevPole = a.pole;
            }
        }

        static Sample S(Vector3 c, Vector3 t, float r, float dr, float s, Vector4 skin, bool pole = false) =>
            new Sample { c = c, t = t, r = r, dr = dr, s = s, skin = skin, pole = pole };

        // ---------------------------------------------------------------- arm, fingers

        public void Arm(int w)
        {
            WingSpec ws = wings[w];
            LegSpec arm = sp.legs[ws.leg];
            WingState st = cw.State[w];
            int bu = WingBoneLayout.LegBone(n, ws.leg, 0), bl = WingBoneLayout.LegBone(n, ws.leg, 1), bh = WingBoneLayout.LegBone(n, ws.leg, 2);
            Vector3 S0 = st.shoulder, E = st.elbow, Wr = st.wrist;
            float lu = Mathf.Max(1e-4f, (E - S0).magnitude), ll = Mathf.Max(1e-4f, (Wr - E).magnitude);
            Vector3 du = (E - S0) / lu, dl = (Wr - E) / ll;
            float ru = Mathf.Max(1e-4f, arm.upperRadius), rl = Mathf.Max(1e-4f, arm.lowerRadius);
            float blob = kShoulderBlob * ru, cap = kWristCap * rl, total = lu + ll;
            float drLimb = (rl - ru) / total;
            var q = new List<Sample>();
            Vector4 shoulderSkin = Blend(arm.attachJoint, bu, 0.5f);
            q.Add(S(S0 - du * blob, du, 0f, 0f, 0f, shoulderSkin, true));
            for (int k = 1; k <= 2; k++)
            {
                float a = (90f - 30f * k) * Mathf.Deg2Rad; // 60, 30 deg: rounded end buried in the body
                float r = ru * Mathf.Cos(a);
                q.Add(S(S0 - du * (blob * Mathf.Sin(a)), du, r, Mathf.Tan(a) * ru / blob, blob - blob * Mathf.Sin(a), shoulderSkin));
            }
            q.Add(S(S0, du, ru, drLimb, blob, shoulderSkin));
            q.Add(S(S0 + du * (0.4f * lu), du, Mathf.Lerp(ru, rl, 0.4f * lu / total), drLimb, blob + 0.4f * lu, Rigid(bu)));
            float kb = arm.KneeBlend;
            Vector3 k0 = S0 + du * ((1f - kb) * lu), k2 = E + dl * (kb * ll);
            for (int i = 0; i < kKneeRings; i++)
            {
                float t = i / (kKneeRings - 1f), wt = t * t * (3f - 2f * t);
                Vector3 c = (1f - t) * (1f - t) * k0 + 2f * (1f - t) * t * E + t * t * k2;
                Vector3 tan = 2f * (1f - t) * (E - k0) + 2f * t * (k2 - E);
                tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : du;
                float sg = Mathf.Lerp((1f - kb) * lu, lu + kb * ll, t);
                q.Add(S(c, tan, Mathf.Lerp(ru, rl, sg / total), drLimb, blob + sg, Blend(bu, bl, wt)));
            }
            q.Add(S(E + dl * (0.6f * ll), dl, Mathf.Lerp(ru, rl, (lu + 0.6f * ll) / total), drLimb, blob + lu + 0.6f * ll, Rigid(bl)));
            q.Add(S(Wr, dl, rl, 0f, blob + total, Blend(bl, bh, 0.5f)));
            for (int k = 1; k <= 2; k++)
            {
                float a = 30f * k * Mathf.Deg2Rad;
                q.Add(S(Wr + dl * (cap * Mathf.Sin(a)), dl, rl * Mathf.Cos(a), -Mathf.Tan(a) * rl / cap, blob + total + cap * Mathf.Sin(a), Rigid(bh)));
            }
            q.Add(S(Wr + dl * cap, dl, 0f, 0f, blob + total + cap, Rigid(bh), true));
            Tube(q, bones[bu].rotation * Vector3.right, kTubeSegments, CreatureMeshBuilder.PartLeg);

            // thumb claw: a short curved cone off the wrist's leading edge, rigid to the hand
            Vector3 fwd = -cw.Side(w) * Vector3.Cross(st.handNormal, st.handOut); // the leading edge
            Vector3 dir = (fwd * 0.8f + st.handNormal * 0.3f - dl * 0.2f).normalized;
            float len = Mathf.Max(1e-3f, ws.thumbLength), r0 = 0.45f * rl;
            Vector3 root = Wr + fwd * (0.6f * rl);
            var c2 = new List<Sample>();
            c2.Add(S(root - dir * (0.5f * r0), dir, 0f, 0f, 0f, Rigid(bh), true));
            for (int k = 0; k < 3; k++)
            {
                float t = k / 3f;
                Vector3 bend = -st.handNormal * (0.25f * len * t * t);
                c2.Add(S(root + dir * (len * t) + bend, (dir - st.handNormal * (0.5f * t)).normalized, r0 * (1f - 0.85f * t), -r0 * 0.85f / len, len * t, Rigid(bh)));
            }
            c2.Add(S(root + dir * len - st.handNormal * (0.25f * len), (dir - st.handNormal * 0.5f).normalized, 0f, 0f, len, Rigid(bh), true));
            Tube(c2, st.handNormal, kFingerSegments, CreatureMeshBuilder.PartFoot);
        }

        public void Fingers(int w)
        {
            WingSpec ws = wings[w];
            WingState st = cw.State[w];
            float r0 = Mathf.Max(1e-4f, ws.fingerRadius);
            for (int k = 0; k < WingSpec.Fingers; k++)
            {
                int bone = WingBoneLayout.WingBone(n, legs, w, k);
                Vector3 d = cw.FingerDir[w * WingSpec.Fingers + k];
                float len = ws.fingerLengths[k];
                var q = new List<Sample>();
                Vector4 rig = Rigid(bone);
                float dr = -0.75f * r0 / len;
                q.Add(S(st.wrist - d * r0, d, 0f, 0f, 0f, rig, true));
                q.Add(S(st.wrist, d, r0, dr, r0, rig));
                q.Add(S(st.wrist + d * (0.35f * len), d, r0 * (1f - 0.26f), dr, r0 + 0.35f * len, rig));
                q.Add(S(st.wrist + d * (0.7f * len), d, r0 * (1f - 0.52f), dr, r0 + 0.7f * len, rig));
                q.Add(S(st.wrist + d * (0.96f * len), d, r0 * 0.25f, dr, r0 + 0.96f * len, rig));
                q.Add(S(st.wrist + d * len, d, 0f, 0f, r0 + len, rig, true));
                Tube(q, st.handNormal, kFingerSegments, CreatureMeshBuilder.PartLeg);
            }
        }

        // ---------------------------------------------------------------- membrane

        public void Membrane(int w)
        {
            WingSpec ws = wings[w];
            WingState st = cw.State[w];
            Vector3 W = st.wrist;
            // rays in fan order: fingers 0..2 (lengths), ribs 0..2 (to the flank)
            var ray = new Vector3[WingSpec.Fingers + WingSpec.Ribs];
            var bone = new int[ray.Length];
            for (int k = 0; k < WingSpec.Fingers; k++)
            {
                ray[k] = cw.FingerTip[w * WingSpec.Fingers + k] - W;
                bone[k] = WingBoneLayout.WingBone(n, legs, w, k);
            }
            for (int k = 0; k < WingSpec.Ribs; k++)
            {
                ray[WingSpec.Fingers + k] = cw.RibTarget[w * WingSpec.Ribs + k] - W;
                bone[WingSpec.Fingers + k] = WingBoneLayout.WingBone(n, legs, w, WingBoneLayout.FirstRib + k);
            }
            // The last zone runs from the rear flank rib to the forearm (the front flank rib only shapes the
            // fold); its bone is the arm sector.
            ray[ray.Length - 1] = st.elbow - W;
            // One smooth sheet: each zone's plane normal, averaged onto the rays between zones; a vertex takes
            // its ray normals blended by its angle fraction, so neighbouring zones share the normal where
            // they meet (both border vertices ride the same bone). The ribs' uniform scale keeps it exact.
            var zn = new Vector3[MembraneZones];
            for (int z = 0; z < MembraneZones; z++)
            {
                Vector3 c = Vector3.Cross(ray[z], ray[z + 1]);
                if (Vector3.Dot(c, st.handNormal) < 0f) c = -c;
                zn[z] = c.sqrMagnitude > 1e-12f ? c.normalized : st.handNormal;
            }
            // the arm zone (on the arm sector) keeps the sector's normal axis
            Vector3 sectorN = cw.ArmSector(w).MultiplyVector(Vector3.forward).normalized;
            zn[MembraneZones - 1] = sectorN;
            var rn = new Vector3[ray.Length];
            for (int k = 0; k < ray.Length; k++)
            {
                Vector3 sum = (k > 0 ? zn[k - 1] : Vector3.zero) + (k < MembraneZones ? zn[k] : Vector3.zero);
                rn[k] = sum.sqrMagnitude > 1e-12f ? sum.normalized : st.handNormal;
            }
            var chord = new Vector3[ZoneColumns + 1]; // the arm zone's far edge: rear flank attach -> elbow
            for (int z = 0; z < MembraneZones; z++)
            {
                Vector3 A = ray[z], B = ray[z + 1];
                Vector3 nz = zn[z];
                // the free trailing edge (fingers to the rear flank attach) is scalloped; the last zone runs from
                // the rear flank rib to the forearm
                bool free = z < MembraneZones - 1;
                bool arm = z == MembraneZones - 1;
                // Columns are spaced evenly in angle (slerp between the rays) out to the straight edge A-B, and
                // weighted by that angle fraction. Evenly spaced columns survive the zone compressing when
                // the wing folds (two-ray skinning of columns spaced along the chord crowds the columns at
                // the longer ray until they cross).
                float la = A.magnitude, lb = B.magnitude;
                Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
                float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    float sg = sheet == 0 ? 1f : -1f;
                    Vector3 sn = sg * nz;
                    int first = pos.Count;
                    for (int j = 0; j <= ZoneRows; j++)
                    {
                        float v = (float)j / ZoneRows;
                        for (int i = 0; i <= ZoneColumns; i++)
                        {
                            float u = (float)i / ZoneColumns;
                            float edge = free ? 1f - ws.scallop * Mathf.Sin(Mathf.PI * u) : 1f;
                            float ph = u * th;
                            float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                            float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                            Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                            Vector3 p = W + dir * (reach * v * Mathf.Lerp(1f, edge, v * v));
                            if (arm && j == ZoneRows) chord[i] = p;
                            float dist = (p - W).magnitude;
                            // The arm zone rides the arm sector (exact on the forearm and on its own plane, whose
                            // normal it keeps), its seam column the rear flank rib (shared with zone 3).
                            Vector4 skin = !arm ? Blend(bone[z], bone[z + 1], u) : Rigid(i == 0 ? bone[z] : bone[z + 1]);
                            Vector3 vn = (sg * (arm && i > 0 ? sectorN : Vector3.Lerp(rn[z], rn[z + 1], u))).normalized;
                            // uv1.y: the sheet's sign x (0.1 + 0.9 x radial fraction): the shader's trailing rim
                            Vertex(p, vn, dist, (z + u) / MembraneZones, PartMembrane, sg * (0.1f + 0.9f * v), skin);
                        }
                    }
                    Grid(first, sn);
                }
            }
            HumerusPatch(w, st, chord, WingBoneLayout.WingBone(n, legs, w, WingBoneLayout.HumerusPatch), sectorN);
        }

        // winding: front faces along sn; row 0 is a pole (one triangle per quad)
        void Grid(int first, Vector3 sn) => GridRC(first, sn, ZoneRows, ZoneColumns);

        void GridRC(int first, Vector3 sn, int rows, int cols)
        {
            int c = cols + 1;
            Vector3 p00 = pos[first + c], p01 = pos[first + c + 1], p10 = pos[first + 2 * c];
            bool flip = Vector3.Dot(Vector3.Cross(p01 - p00, p10 - p00), sn) < 0f;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = first + j * c + i, b = a + 1, d = a + c, e = d + 1;
                    if (j > 0) Tri(a, b, d, flip);
                    Tri(b, e, d, flip);
                }
        }

        // The membrane between the humerus and the flank: a fan from the elbow between the arm zone's far edge
        // (elbow -> rear flank attach, on the arm sector) and the humerus, out to the flank (rear flank attach ->
        // shoulder). Rows run out from the elbow (row 0, the pole) through the arm zone's far-edge points, so the
        // two share that edge vertex for vertex; columns are spaced evenly in angle. All of it rides the humerus
        // patch bone (CreatureWings.HumerusPatch), exact on that triangle, so both shared edges stay exact.
        void HumerusPatch(int w, WingState st, Vector3[] chord, int patchBone, Vector3 nz)
        {
            Vector3 E = st.elbow, W = st.wrist;
            Vector3 A = chord[0] - E, B = st.shoulder - E;           // to the rear flank attach, to the shoulder
            float la = A.magnitude, lb = B.magnitude;
            Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
            float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
            Vector3 np = Vector3.Cross(B, A);
            if (Vector3.Dot(np, nz) < 0f) np = -np;
            np = np.sqrMagnitude > 1e-12f ? np.normalized : nz;
            const int rows = ZoneColumns, cols = ZoneRows;          // a zone block, transposed
            for (int sheet = 0; sheet < 2; sheet++)
            {
                float sg = sheet == 0 ? 1f : -1f;
                int first = pos.Count;
                for (int j = 0; j <= rows; j++)
                {
                    // row j passes through the far-edge point chord[rows - j] (chord[ZoneColumns] is the elbow)
                    float v = Mathf.Clamp01((chord[rows - j] - E).magnitude / Mathf.Max(1e-6f, la));
                    for (int i = 0; i <= cols; i++)
                    {
                        float u = (float)i / cols;
                        float ph = u * th;
                        float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                        float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                        Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                        Vector3 p = i == 0 ? chord[rows - j] : E + dir * (reach * v);
                        Vector4 skin = Rigid(patchBone); // exact on the elbow, rear attach, shoulder triangle
                        Vertex(p, sg * (i == 0 ? nz : np), (p - W).magnitude, (MembraneZones - u) / MembraneZones, PartMembrane, sg * (0.1f + 0.9f * v), skin);
                    }
                }
                GridRC(first, sg * np, rows, cols);
            }
        }

        public Mesh ToMesh()
        {
            var mesh = new Mesh { name = (sp != null ? sp.name : "") + " winged creature" };
            if (pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0, true);
            return mesh;
        }
    }
}

    // CreatureRestPose.BoneWorld before T3.3 (the bind pose source).
    internal static class LegacyRestPose
    {
        public static Matrix4x4[] BoneWorld(CreatureSpecies sp)
        {
            sp = sp.Compiled.Runtime;
            if (sp.WingCount > 0) return LegacyWingedMeshBuilder.RestBoneWorld(sp, sp.wings);
            CreatureSim sim = CreatureRestPose.Sim(sp);
            var m = new Matrix4x4[sim.Species.BoneCount];
            sim.WriteBoneWorld(m, 0);
            return m;
        }
    }
}
