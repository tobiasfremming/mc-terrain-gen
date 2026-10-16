using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds a species' skinned mesh once, in the rest pose (CreatureRestPose).
//   position, normal: rest space
//   uv0 = (arc length along the part in metres, angle / 2pi)
//   uv1 = (part id, dorsal coordinate: -1 belly .. +1 spine ridge)
//   uv2 = (bone0, bone1, w0, w1), w0 + w1 = 1
// Layout: body vertices first, as consecutive rings of ringSegments + 1 vertices (seam duplicated)
// from the snout tip to the tail tip (each tip is a ring of identical vertices), then the legs
// (tube, foot pad, toes, per leg), then the two eyes. Vertices are shared along a part, so normals
// are continuous across ring boundaries.
public static class CreatureMeshBuilder
{
    public const int PartBody = 0, PartLeg = 1, PartFoot = 2, PartEye = 3;

    const int kHeadCapRings = 4, kTailRings = 8;
    const float kTailTipSlope = -1.5f; // d(radius / end radius) / d(k) at the tail tip: a fine point
    const float kSnoutNarrow = 0.2f;   // extra width taper toward the snout tip (fraction at the tip)
    const float kSnoutFlat = 0.35f;    // extra height taper: the snout is flatter than it is wide
    const float kSnoutDrop = 0.15f;    // the snout's centre line drops by this fraction of head height

    const int kLegSegments = 8;
    const float kKneeBlend = 0.45f;    // fraction of each limb bone blended at the knee (R12; 0.45 for R16 knee flexion)
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
        var b = new Builder(sp);
        b.Body();
        for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++) b.Leg(l);
        b.Eyes();
        return b.ToMesh();
    }

    sealed class Builder
    {
        readonly CreatureSpecies sp;
        readonly CreatureSim sim;
        readonly Matrix4x4[] bones;
        readonly int n;
        readonly List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>();
        readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
        readonly List<Vector4> uv2 = new List<Vector4>();
        readonly List<int> tris = new List<int>();

        public Builder(CreatureSpecies sp)
        {
            this.sp = sp;
            sim = CreatureRestPose.Sim(sp);
            bones = CreatureRestPose.BoneWorld(sim);
            n = sp.JointCount;
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
            Vector3 k0 = S + du * ((1f - kKneeBlend) * lu), k2 = E + dl * (kKneeBlend * ll);
            for (int i = 0; i < kKneeRings; i++)
            {
                float t = i / (kKneeRings - 1f), w = t * t * (3f - 2f * t);
                Vector3 c = (1f - t) * (1f - t) * k0 + 2f * (1f - t) * t * E + t * t * k2;
                Vector3 tan = 2f * (1f - t) * (E - k0) + 2f * t * (k2 - E);
                tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : du;
                Add(c, tan, Mathf.Lerp((1f - kKneeBlend) * lu, lu + kKneeBlend * ll, t), Blend(bu, bl, w));
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
