// Creature kit: the digitigrade leg mesh of a Leg2Bone (LegSpec.digitigrade; user 2026-10-20, "make real-looking
// dragon legs"): a dragon's / raptor's hind leg on the same three bones -
//   thigh       a thick haunch out of the hip (blended into the body like the vertebrate limb's), a heavy thigh with
//               two muscle masses (the front quadriceps and the rear hamstrings: ellipsoids rigid to the thigh,
//               thighMass x upperRadius) tapering to a ball knee;
//   shin        lean, a little calf high at the back, ending in a knobbly hock (the ankle, ankleHeight above the
//               ground: the lower bone's end), scutes down its front (LegSpec.scutes);
//   foot        rigid to the foot bone: the long metatarsus from the hock down and forward to the ball, a heel tuber
//               behind the hock and an optional spur (spurLength), three thick forward toes with knuckles lying on
//               the ground, each ending in a big curved talon (clawLength, clawCurveDeg), a hallux behind when
//               toeCount >= 4, scutes on the metatarsus' front and on top of the toes.
// Everything but the buried root is rigid to one bone and the knee is a ball (two caps on one sphere): nothing creases
// or inverts at any bend. Size: all in metres at scale 1 (the bones carry the size).
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed partial class Leg2Bone
    {
        const int kDigiSides = 12, kDigiToeSides = 8, kMuscleAround = 10, kMuscleBands = 6, kScuteAround = 8, kScuteBands = 4;

        struct Sec { public Vector3 c, t; public float a, b, d, s; public Vector4 skin; public bool pole; }

        // A loft through sections: elliptical rings (a across x, b along y = t x x, centre offset d along y) in a frame
        // transported from x0, poles at flagged ends; the taper slopes for the normals from the neighbours.
        static void Loft(PartMeshEmitter m, List<Sec> q, Vector3 x0, int sides, int part)
        {
            Vector3 x = x0;
            int prev = -1;
            for (int i = 0; i < q.Count; i++)
            {
                Sec a = q[i];
                Vector3 xt = x - a.t * Vector3.Dot(x, a.t);
                x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
                Vector3 y = Vector3.Cross(a.t, x);
                int cur;
                if (a.pole)
                {
                    Vector3 pn = i == 0 ? -a.t : a.t;
                    cur = m.Pole(a.c, pn, a.s, part, pn.y, a.skin, sides);
                }
                else
                {
                    int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(q.Count - 1, i + 1);
                    Sec p0 = q[i0], p1 = q[i1];
                    float ds = Mathf.Max(1e-5f, Vector3.Dot(p1.c - p0.c, a.t));
                    float da = (p0.pole || p1.pole) ? 0f : (p1.a - p0.a) / ds, db = (p0.pole || p1.pole) ? 0f : (p1.b - p0.b) / ds;
                    float dd = (p0.pole || p1.pole) ? 0f : (p1.d - p0.d) / ds;
                    cur = m.Ring(a.c, x, y, a.t, a.a, a.b, a.d, da, db, dd, a.s, part, a.skin, sides, false);
                }
                if (prev >= 0) m.Stitch(prev, cur, sides, q[i - 1].pole, a.pole, false);
                prev = cur;
            }
        }

        // A hemispherical cap of radius r on centre c: dir +1 closes the loft past c along t, -1 opens it before c.
        static void Cap(List<Sec> q, Vector3 c, Vector3 t, float r, float s, float dir, Vector4 skin)
        {
            if (dir < 0f) q.Add(new Sec { c = c - t * r, t = t, s = s - r, skin = skin, pole = true });
            float[] degs = dir < 0f ? new[] { 60f, 30f } : new[] { 30f, 60f };
            foreach (float deg in degs)
            {
                float dd = r * Mathf.Sin(deg * Mathf.Deg2Rad), rr = r * Mathf.Cos(deg * Mathf.Deg2Rad);
                q.Add(new Sec { c = c + t * (dir * dd), t = t, a = rr, b = rr, s = s + dir * dd, skin = skin });
            }
            if (dir > 0f) q.Add(new Sec { c = c + t * r, t = t, s = s + r, skin = skin, pole = true });
        }

        // A flat overlapping plate (a scute) on a surface: centre c, its long axis along t, its thin axis along n.
        static void Scute(PartMeshEmitter m, Vector3 c, Vector3 t, Vector3 n, float width, float thick, float length, int bone)
        {
            Vector3 tt = (t - n * Vector3.Dot(t, n));
            if (tt.sqrMagnitude < 1e-10f) return;
            Matrix4x4 f = Matrix4x4.TRS(c, Quaternion.LookRotation(tt.normalized, n), Vector3.one);
            m.Ellipsoid(f, Vector3.zero, width, thick, length, kScuteAround, kScuteBands, PartMeshEmitter.PartLeg, bone);
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void Digitigrade(PartMeshEmitter m, LegSpec s, int l)
        {
            CreatureSpecies sp = m.Species;
            Matrix4x4[] bones = m.Bones;
            LegState st = m.Rest.Legs[l];
            int attach = s.attachJoint, bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1), bf = sp.LegBone(l, 2);
            Vector3 S = st.shoulder, E = st.elbow, A = st.ankle;
            float lu = Mathf.Max(1e-4f, (E - S).magnitude), ll = Mathf.Max(1e-4f, (A - E).magnitude);
            Quaternion qu = bones[bu].rotation, ql = bones[bl].rotation;
            Vector3 du = qu * Vector3.forward, dl = ql * Vector3.forward;
            float ru = Mathf.Max(PartMeshEmitter.MinRadius, s.upperRadius), rl = Mathf.Max(PartMeshEmitter.MinRadius, s.lowerRadius);
            float rh = 1.4f * ru, rk = Mathf.Max(0.62f * ru, 1.1f * rl), rhock = 1.12f * rl;
            float blob = kHaunchDepth * rh;
            float mass = s.thighMass > 0f ? s.thighMass : 0.5f;

            // ---- the thigh: buried haunch, heavy thigh, ball knee
            float Thigh(float f) => Mathf.Lerp(rh, rk, Smooth((f - 0.3f) / 0.7f));
            var q = new List<Sec>();
            void Add(List<Sec> list, Vector3 c, Vector3 t, float a, float b, float d, float sg, Vector4 skin) =>
                list.Add(new Sec { c = c, t = t, a = a, b = b, d = d, s = sg, skin = skin });
            q.Add(new Sec { c = S - du * blob, t = du, s = 0f, skin = PartMeshEmitter.Blend(attach, bu, kRootBody), pole = true });
            Add(q, S - du * (blob * 0.866f), du, 0.5f * rh, 0.5f * rh, 0f, 0.134f * blob, PartMeshEmitter.Blend(attach, bu, kRootBody));
            Add(q, S - du * (blob * 0.5f), du, 0.866f * rh, 0.866f * rh, 0f, 0.5f * blob, PartMeshEmitter.Blend(attach, bu, 0.5f * (kRootBody + kRootLimb)));
            Add(q, S, du, rh, rh, 0f, blob, PartMeshEmitter.Blend(attach, bu, kRootLimb));
            for (int i = 1; i <= 5; i++)
            {
                float f = i / 6f, r = Thigh(f), w = Mathf.Sin(Mathf.PI * f);
                // a thick thigh swung far (trailing in flight) folds a short body/limb gradient: blend over 2/3 of it
                Vector4 skin = f < 0.66f ? PartMeshEmitter.Blend(attach, bu, kRootLimb + (1f - kRootLimb) * f / 0.66f) : PartMeshEmitter.Rigid(bu);
                Add(q, S + du * (f * lu), du, r * (1f - 0.12f * w), r * (1f + 0.15f * w), 0f, blob + f * lu, skin);
            }
            Add(q, E, du, rk, rk, 0f, blob + lu, PartMeshEmitter.Rigid(bu));
            Cap(q, E, du, rk, blob + lu, 1f, PartMeshEmitter.Rigid(bu));
            Loft(m, q, qu * Vector3.right, kDigiSides, PartMeshEmitter.PartLeg);

            // the thigh's muscle masses (rigid to it; bone-local: +Z along the thigh, +Y the knee's side)
            float rm = Thigh(0.4f);
            m.Ellipsoid(bones[bu], new Vector3(0f, 0.42f * rm, 0.44f * lu), (0.55f + 0.25f * mass) * rm, (0.4f + 0.3f * mass) * rm, 0.36f * lu,
                        kMuscleAround, kMuscleBands, PartMeshEmitter.PartLeg, bu);
            m.Ellipsoid(bones[bu], new Vector3(0f, -0.38f * rm, 0.36f * lu), (0.5f + 0.25f * mass) * rm, (0.38f + 0.28f * mass) * rm, 0.34f * lu,
                        kMuscleAround, kMuscleBands, PartMeshEmitter.PartLeg, bu);
            m.Ellipsoid(bones[bu], new Vector3(0f, 0.05f * rm, 0.2f * lu), (0.62f + 0.2f * mass) * rm, (0.5f + 0.2f * mass) * rm, 0.3f * lu,
                        kMuscleAround, kMuscleBands, PartMeshEmitter.PartLeg, bu);

            // ---- the shin: lean, a high calf at the back, the knobbly hock
            var w2 = new List<Sec>();
            float Shin(float v) => Mathf.Lerp(rk, 0.78f * rl, Smooth(v / 0.35f));
            Cap(w2, E, dl, rk, blob + lu, -1f, PartMeshEmitter.Rigid(bl));
            Add(w2, E, dl, rk, rk, 0f, blob + lu, PartMeshEmitter.Rigid(bl));
            for (int i = 1; i <= 6; i++)
            {
                float v = i / 7f, r = Shin(v);
                float calf = Mathf.Sin(Mathf.PI * Mathf.Clamp01(v / 0.5f)) * (v < 0.5f ? 1f : 0f);
                Add(w2, E + dl * (v * ll), dl, r * (1f + 0.1f * calf), r * (1f + 0.3f * calf), -0.35f * rl * calf, blob + lu + v * ll, PartMeshEmitter.Rigid(bl));
            }
            Add(w2, A - dl * (0.4f * rhock), dl, 0.95f * rhock, 0.95f * rhock, 0f, blob + lu + ll - 0.4f * rhock, PartMeshEmitter.Rigid(bl));
            Add(w2, A, dl, rhock, rhock, 0f, blob + lu + ll, PartMeshEmitter.Rigid(bl));
            Cap(w2, A, dl, rhock, blob + lu + ll, 1f, PartMeshEmitter.Rigid(bl));
            Loft(m, w2, ql * Vector3.right, kDigiSides, PartMeshEmitter.PartLeg);
            if (s.scutes)
            {
                // down the shin's front (+Y of the lower bone: the knee's side)
                for (int i = 0; i < 6; i++)
                {
                    float v = 0.24f + 0.12f * i, r = Shin(v);
                    Vector3 n = ql * Vector3.up;
                    Scute(m, E + dl * (v * ll) + n * (0.78f * r), dl, n, 0.62f * r, 0.2f * r, 0.085f * ll, bl);
                }
            }

            DigitigradeFoot(m, s, bf, s.AnkleHeight, rl, rhock, blob + lu + ll);
        }

        // The digitigrade foot in the foot bone's frame (origin: the contact point, +Y the ground normal, +Z the
        // heading; the hock at (0, h, 0)), turned out by kFootYawDeg, rigid to the foot bone.
        static void DigitigradeFoot(PartMeshEmitter m, LegSpec s, int bf, float h, float rl, float rhock, float arc)
        {
            float fl = Mathf.Max(1e-3f, s.footLength);
            h = Mathf.Max(h, 2f * rhock);
            Matrix4x4 frame = m.Bones[bf] * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(s.side * kFootYawDeg, Vector3.up), Vector3.one);
            Vector3 X = frame.MultiplyVector(Vector3.right).normalized, Y = frame.MultiplyVector(Vector3.up).normalized, Z = frame.MultiplyVector(Vector3.forward).normalized;
            Vector4 skin = PartMeshEmitter.Rigid(bf);
            int toes = Mathf.Max(1, s.toeCount);
            int fore = toes >= 4 ? 3 : toes;
            float rt = Mathf.Max(0.5f * rl, 0.075f * fl);          // toe root radius
            float ball = 1.05f * rt;                                // the ball's height (the toes' roots)
            float zb = Mathf.Max(0.42f * h, 0.25f * fl);            // the ball ahead of the hock
            Vector3 hock = new Vector3(0f, h, 0f), B = new Vector3(0f, ball, zb);
            Vector3 mt = (B - hock).normalized;
            float mlen = (B - hock).magnitude;
            float rm0 = 0.8f * rl, rm1 = Mathf.Max(0.72f * rl, 1.1f * rt);
            Vector3 P(Vector3 lp) => frame.MultiplyPoint3x4(lp);
            Vector3 V(Vector3 lv) => frame.MultiplyVector(lv).normalized;

            // the metatarsus: from inside the hock down to the ball, a little wider than deep, a knob at the ball
            var q = new List<Sec>();
            Vector3 tw = V(mt);
            Cap(q, P(hock), tw, rm0, arc, -1f, skin);
            for (int i = 0; i <= 4; i++)
            {
                float f = i / 4f, r = Mathf.Lerp(rm0, rm1, f) * (1f - 0.08f * Mathf.Sin(Mathf.PI * f));
                q.Add(new Sec { c = P(hock + mt * (f * mlen)), t = tw, a = 1.08f * r, b = 0.92f * r, s = arc + f * mlen, skin = skin });
            }
            Cap(q, P(B), tw, 1.08f * rm1, arc + mlen, 1f, skin);
            Loft(m, q, X, kDigiSides, PartMeshEmitter.PartLeg);
            // the heel tuber behind the hock, and the spur
            m.Ellipsoid(frame, new Vector3(0f, h + 0.1f * rhock, -0.75f * rhock), 0.75f * rhock, 0.8f * rhock, 0.75f * rhock, kMuscleAround, kMuscleBands, PartMeshEmitter.PartLeg, bf);
            if (s.spurLength > 0f)
            {
                Vector3 sd = new Vector3(0f, -0.35f, -1f).normalized;
                Quaternion sq = Quaternion.LookRotation(V(sd), V(Vector3.Cross(sd, Vector3.right) * -1f));
                m.Horn(P(new Vector3(0f, h - 0.3f * rhock, -1.1f * rhock)), sq, 0.4f * rl, 0f, s.spurLength, 35f, bf, PartMeshEmitter.PartFoot, 6, arc, out _, out _);
            }
            if (s.scutes)
            {
                Vector3 front = (Vector3.forward - mt * Vector3.Dot(Vector3.forward, mt)).normalized;   // the metatarsus' front face
                for (int i = 0; i < 5; i++)
                {
                    float f = 0.14f + 0.17f * i, r = Mathf.Lerp(rm0, rm1, f);
                    Scute(m, P(hock + mt * (f * mlen) + front * (0.8f * r)), tw, V(front), 0.72f * r, 0.22f * r, 0.11f * mlen, bf);
                }
            }

            // the toes: thick, lying on the ground from the ball, a knuckle per phalanx, a big curved talon each
            float clawLen = s.clawLength > 0f ? s.clawLength : Mathf.Max(0.3f * fl, 3f * rt);
            float curl = s.clawCurveDeg > 0f ? s.clawCurveDeg : 100f;
            float fan = fore > 1 ? 24f : 0f;
            for (int i = 0; i < fore; i++)
            {
                float k = fore == 1 ? 0f : i / (fore - 1f) * 2f - 1f;
                Vector3 dir = Quaternion.AngleAxis(k * fan, Vector3.up) * Vector3.forward;
                float len = (0.72f - 0.16f * k * k) * fl;
                Vector3 root = B + new Vector3(k * 0.55f * rm1, rt - ball, 0f) + dir * (0.3f * rt);
                Toe(m, frame, skin, root, dir, len, rt, clawLen, curl, s.scutes, bf, arc + mlen, 0.75f * rt, 0f);
            }
            if (toes >= 4)
            {
                // the hallux: a short toe on the inner back of the metatarsus' foot end, pointing back and in, off the
                // ground
                Vector3 dir = new Vector3(-0.35f * s.side, -0.25f, -1f).normalized;
                Vector3 root = hock + mt * (0.78f * mlen) + new Vector3(-0.4f * s.side * rm1, 0f, -0.6f * rm1);
                Toe(m, frame, skin, root, dir, 0.3f * fl, 0.75f * rt, 0.6f * clawLen, curl, false, bf, arc, 0f, 0f);
            }
        }

        // A toe from root along dir (foot frame), arched by `arch` and lying on y = its radius at the tip when on the
        // ground, two knuckles, a pole tip and a talon curling down.
        static void Toe(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, Vector3 root, Vector3 dir, float len, float rt, float clawLen,
                        float curl, bool scutes, int bf, float arc, float arch, float unused)
        {
            float tipR = 0.55f * rt;
            Vector3 C(float u) => root + dir * (len * u) + Vector3.up * (arch * 4f * u * (1f - u) + (tipR - rt) * u * (dir.y >= -0.05f ? 1f : 0f));
            Vector3 D(float u) => (dir * len + Vector3.up * (arch * (4f - 8f * u) + (tipR - rt) * (dir.y >= -0.05f ? 1f : 0f))).normalized;
            float R(float u)
            {
                float g1 = (u - 0.38f) / 0.08f, g2 = (u - 0.7f) / 0.07f;
                return Mathf.Lerp(rt, tipR, u) * (1f + 0.16f * Mathf.Exp(-g1 * g1) + 0.13f * Mathf.Exp(-g2 * g2));
            }
            var q = new List<Sec>();
            Vector3 t0 = frame.MultiplyVector(D(0f)).normalized;
            q.Add(new Sec { c = frame.MultiplyPoint3x4(C(0f)) - t0 * rt, t = t0, s = arc, skin = skin, pole = true });
            float[] us = { 0f, 0.15f, 0.3f, 0.38f, 0.46f, 0.6f, 0.7f, 0.8f, 0.92f, 1f };
            foreach (float u in us)
            {
                float r = R(u);
                q.Add(new Sec { c = frame.MultiplyPoint3x4(C(u)), t = frame.MultiplyVector(D(u)).normalized, a = r, b = 0.85f * r, s = arc + len * u, skin = skin });
            }
            Vector3 t1 = frame.MultiplyVector(D(1f)).normalized;
            q.Add(new Sec { c = frame.MultiplyPoint3x4(C(1f)) + t1 * (0.6f * tipR), t = t1, s = arc + len, skin = skin, pole = true });
            Vector3 xr = Vector3.Cross(Vector3.up, dir);
            Loft(m, q, frame.MultiplyVector(xr.sqrMagnitude > 1e-8f ? xr.normalized : Vector3.right).normalized, kDigiToeSides, PartMeshEmitter.PartLeg);
            if (scutes)
                for (int i = 0; i < 3; i++)
                {
                    float u = 0.2f + 0.27f * i, r = R(u);
                    Vector3 up = Vector3.Cross(D(u), Vector3.Cross(Vector3.up, D(u))).normalized;
                    Scute(m, frame.MultiplyPoint3x4(C(u) + up * (0.72f * 0.85f * r)), frame.MultiplyVector(D(u)), frame.MultiplyVector(up).normalized,
                          0.75f * r, 0.25f * r, 0.12f * len, bf);
                }
            // the talon: from the toe's tip, a thick curved cone curling down onto the ground
            Vector3 cd = (D(1f) + Vector3.up * 0.45f).normalized;
            Vector3 cu = Vector3.Cross(cd, Vector3.Cross(Vector3.up, cd));
            if (cu.sqrMagnitude < 1e-8f) cu = Vector3.up;
            Quaternion cq = Quaternion.LookRotation(frame.MultiplyVector(cd).normalized, frame.MultiplyVector(cu.normalized).normalized);
            m.Horn(frame.MultiplyPoint3x4(C(1f) - D(1f) * (0.3f * tipR)), cq, 0.95f * tipR, 0f, clawLen, curl, bf, PartMeshEmitter.PartFoot, 7, arc + len, out _, out _);
        }
    }
}
