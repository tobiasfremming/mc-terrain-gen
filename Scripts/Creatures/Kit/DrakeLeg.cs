// Creature kit (Apex redesign §5.5): DrakeLeg - a slim dragon hind leg. It walks exactly as a Leg2Bone does (the same
// two-bone LegSpec: TwoBoneIK with a pole, swing/knee limits, gait groups, the ankle lifted ankleHeight off the contact
// point - CreatureLegs runs it unchanged; `digitigrade` stays off so no Leg2Bone mesh path ever draws it). Its mesh is
// its own, built from the rest pose:
//   haunch    a muscular root (haunch x upperRadius) buried in the trunk, its skin blended from the body to the thigh.
//             On a FlowBody it reaches into the DRAWN section (FlowBody.Section in the rest spine's frame, not the
//             joint's halfHeight), so a hip set at or beyond the flank still grows out of the body
//   thigh     lean, a little deeper on the knee's side, tapering to a ball knee (two caps on one sphere: never creases)
//   shin      slim, a small calf high at the back, ending in a knobbly raised hock (the lower bone's end)
//   foot      rigid to the foot bone, hung from the hock: a long metatarsus down and forward to the ball, `toes` forward
//             toes with two knuckles each lying on the ground, a dark curved talon on each (claw layer 2) whose lowest
//             point is solved onto the ground - the gait's foot point is the toes' contact - and an optional hallux
//             (a short back toe off the ground)
// The foot is drawn ankleHeight below the hock the sim draws: when the gait's ankle and the part's hock agree (always,
// through ToLegSpec) the talons touch the ground; a sim without the hock sinks them (the RED proof in the tests).
// Material layers: 1 the limb (Skin: the species' dark limb colour), 2 the talons. Kit-only; off unless a species names it.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("DrakeLeg", PartKind.Limb, Bones = "3", Summary = "Slim dragon hind leg: walks as a Leg2Bone (TwoBoneIK, pole, limits, a raised hock as the ankle), drawn with a muscular haunch blended into the trunk (into a FlowBody's drawn section), a lean thigh, a ball knee, a slim shin, a knobbly raised hock, a long metatarsus, knuckled toes on the ground with dark curved talons touching down at the foot point, an optional hallux.",
          System = "CreatureLegs (gait groups, throttle, sway) + DrakeLeg.EmitMesh (LimbLoft)", MaterialLayers = new[] { 1, 2 }, KitOnly = true,
          UseWhen = "dragons, wyverns, raptors, birds: any digitigrade hind leg standing on toes with talons")]
    public sealed class DrakeLeg : LimbPart
    {
        [Param(ParamUnit.Metres, "thigh (upper bone) length", Min = 0.005f, Max = 2f, Example = "apex 0.24")] public float upperLength;
        [Param(ParamUnit.Metres, "shin (lower bone) length, to the hock", Min = 0.005f, Max = 2f, Example = "apex 0.26")] public float lowerLength;
        [Param(ParamUnit.Metres, "thigh radius (the haunch is haunch x this)")] public float upperRadius;
        [Param(ParamUnit.Metres, "shin radius (slim: the hock, metatarsus and toes follow it)")] public float lowerRadius;
        [Param(ParamUnit.Direction, "knee direction hint in the attach frame (x mirrored); a hind leg's knee points forward (z > 0)")] public Vector3 pole;
        [Param(ParamUnit.Metres, "the hock's height above the toes' contact point (the metatarsus' drop; the gait's ankle height), 0 = 0.3 x (upperLength + lowerLength)", Min = 0f)] public float ankleHeight;
        [Param(ParamUnit.Count, "forward toes", Min = 2, Max = 4, Default = "3")] public int toes = 3;
        [Param(ParamUnit.Flag, "a hallux: a short toe at the back of the metatarsus, off the ground")] public bool hallux;
        [Param(ParamUnit.Metres, "talon length (the middle toe's), 0 = 0.3 x footLength")] public float talonLength;
        [Param(ParamUnit.Degrees, "talon curl over its length", Min = 20, Max = 160, Default = "110")] public float talonCurveDeg = 110f;
        [Param(ParamUnit.Fraction, "haunch radius, x upperRadius", Min = 1f, Max = 3f, Default = "1.5")] public float haunch = 1.5f;

        public override int BoneCount(in SpeciesCtx c) => Leg2Bone.Bones;

        public float Hock => ankleHeight > 0f ? ankleHeight : 0.3f * (upperLength + lowerLength);
        public float TalonLength => talonLength > 0f ? talonLength : 0.3f * footLength;

        public override LegSpec ToLegSpec()
        {
            LegSpec l = BaseSpec();
            l.upperLength = upperLength; l.lowerLength = lowerLength; l.upperRadius = upperRadius; l.lowerRadius = lowerRadius;
            l.pole = pole; l.kneeBlend = 0f; l.ankleHeight = Hock;
            l.clawLength = TalonLength; l.clawCurveDeg = talonCurveDeg;
            l.toeCount = toes + (hallux ? 1 : 0);
            l.digitigrade = false;   // the Leg2Bone digitigrade mesh is not this part's (DrakeLeg.EmitMesh draws it)
            return l;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (!(upperLength > 0f && lowerLength > 0f)) return $"{id}: limb lengths must be > 0";
            if (!(upperRadius > 0f && lowerRadius > 0f)) return $"{id}: limb radii must be > 0";
            if (role == LimbRole.Folded || role == LimbRole.WingArm) return $"{id}: a DrakeLeg walks (use a Leg2Bone for a wing's arm)";
            if (ankleHeight < 0f) return $"{id}: ankleHeight must be >= 0 (a DrakeLeg stands on a raised hock)";
            if (Hock >= 0.8f * (upperLength + lowerLength)) return $"{id}: the hock ({Hock:F3} m) must be below 0.8 x the leg's length";
            if (toes < 2 || toes > 4) return $"{id}: toes must be 2..4";
            if (toeCount != 0) return $"{id}: a DrakeLeg counts its toes in `toes` (+ hallux); leave toeCount 0";
            if (!(footLength > 0f)) return $"{id}: footLength must be > 0";
            if (talonLength < 0f) return $"{id}: talonLength must be >= 0";
            if (talonCurveDeg < 20f || talonCurveDeg > 160f) return $"{id}: talonCurveDeg must be 20..160";
            if (haunch < 1f) return $"{id}: haunch must be >= 1";
            return base.Validate(c);
        }

        // ------------------------------------------------------------------ the mesh

        const int kAround = 12, kToeAround = 8, kTalonAround = 8;
        const float kRootBody = 0.3f, kRootLimb = 0.5f;   // limb weight of the buried root / the haunch ring (Leg2Bone's)
        const float kFootYawDeg = 8f;                     // toes turned out from the heading
        const float kTalonClear = 5e-4f;                  // the lowest talon point this far above the ground (m at scale 1)

        // RED test hook (CreatureDrakeLegTests): the haunch's depth ignores the body (the fixed 1.1 x haunch radius).
        internal static bool IgnoreBodyForTest;

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static float Ellipse(float phi, float a, float b)
        {
            float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            return 1f / Mathf.Sqrt(cs * cs / (a * a) + sn * sn / (b * b));
        }

        // How deep the haunch's buried pole goes behind the hip point (along -du): 1.1 x its radius, and on a FlowBody
        // at least until the pole lies well inside the body's drawn section at the attach joint.
        internal static float RootDepth(PartMeshEmitter m, int joint, Vector3 S, Vector3 du, float rh)
        {
            float blob = 1.1f * rh;
            FlowBody body = Body(m.Species);
            if (body == null || IgnoreBodyForTest) return blob;
            for (int i = 0; i < 60 && !InsideBody(m, body, joint, S - du * blob, 0.85f); i++) blob += 0.1f * rh;
            return blob;
        }

        internal static FlowBody Body(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FlowBody f) return f;
            return null;
        }

        // p lies inside `share` of the FlowBody's drawn section at joint (the rest spine's section frame there).
        internal static bool InsideBody(PartMeshEmitter m, FlowBody body, int joint, Vector3 p, float share)
        {
            SpineSection q = m.SpineAt(joint);
            Vector3 d = p - q.c;
            float x = Vector3.Dot(d, q.x), y = Vector3.Dot(d, q.y);
            float r = body.Section(body.UAtJoint(joint), Mathf.Atan2(y, x), out _);
            return Mathf.Sqrt(x * x + y * y) < share * r;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            int l = c.Ordinal;
            if (m.IsWingArm(l)) return;
            CreatureSpecies sp = m.Species;
            LegSpec s = sp.legs[l];
            Matrix4x4[] bones = m.Bones;
            LegState st = m.Rest.Legs[l];
            int attach = s.attachJoint, bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1), bf = sp.LegBone(l, 2);
            Vector3 S = st.shoulder, E = st.elbow, A = st.ankle;
            float lu = Mathf.Max(1e-4f, (E - S).magnitude), ll = Mathf.Max(1e-4f, (A - E).magnitude);
            Quaternion qu = bones[bu].rotation, ql = bones[bl].rotation;
            Vector3 du = qu * Vector3.forward, dl = ql * Vector3.forward;
            float ru = Mathf.Max(PartMeshEmitter.MinRadius, upperRadius), rl = Mathf.Max(PartMeshEmitter.MinRadius, lowerRadius);
            float rh = haunch * ru, rk = Mathf.Max(0.62f * ru, 1.12f * rl), rhock = 1.18f * rl, rshin = 0.78f * rl;
            float blob = RootDepth(m, attach, S, du, rh);

            // ---- haunch and thigh (the upper bone's +Y is the knee's side: a little more muscle there) ----
            var up = new LimbLoft(kAround);
            Vector3 xu = qu * Vector3.right, yu = qu * Vector3.up;
            up.Pole(S - du * blob, PartMeshEmitter.Blend(attach, bu, kRootBody), 0f);
            foreach (float q in new[] { 0.93f, 0.75f, 0.5f, 0.25f })
            {
                float r = rh * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q));
                up.Ring(S - du * (blob * q), xu, yu, 0f, _ => r, PartMeshEmitter.Blend(attach, bu, Mathf.Lerp(kRootBody, kRootLimb, 1f - q)), blob * (1f - q));
            }
            up.Ring(S, xu, yu, 0f, _ => rh, PartMeshEmitter.Blend(attach, bu, kRootLimb), blob);
            const int nThigh = 8;
            float thighEnd = lu - 0.25f * rk;
            for (int i = 1; i <= nThigh; i++)
            {
                float sg = thighEnd * i / nThigh, f = sg / lu;
                float r = Mathf.Lerp(rh, rk, Smooth((f - 0.1f) / 0.9f));
                float w = Mathf.Sin(Mathf.PI * Mathf.Min(1f, f / 0.9f));
                Vector4 skin = f < 0.6f ? PartMeshEmitter.Blend(attach, bu, kRootLimb + (1f - kRootLimb) * f / 0.6f) : PartMeshEmitter.Rigid(bu);
                float a = r * (1f - 0.1f * w), b = r * (1f + 0.12f * w), d = 0.1f * r * w;
                up.Ring(S + du * sg, xu, yu, d, phi => Ellipse(phi, a, b), skin, blob + sg);
            }
            foreach (float deg in new[] { 0f, 30f, 60f })
            {
                float dd = rk * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rk * Mathf.Cos(deg * Mathf.Deg2Rad);
                up.Ring(E + du * dd, xu, yu, 0f, _ => rr, PartMeshEmitter.Rigid(bu), blob + lu + dd);
            }
            up.Pole(E + du * rk, PartMeshEmitter.Rigid(bu), blob + lu + rk);
            up.Emit(m, PartMeshEmitter.PartLeg);

            // ---- the shin: ball knee, a small calf high at the back (-Y), slim, the knobbly hock ----
            var lo = new LimbLoft(kAround);
            Vector3 xl = ql * Vector3.right, yl = ql * Vector3.up;
            Vector4 rigidL = PartMeshEmitter.Rigid(bl);
            float arc0 = blob + lu;
            lo.Pole(E - dl * rk, rigidL, arc0 - rk);
            foreach (float deg in new[] { 60f, 30f, 0f })
            {
                float dd = rk * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rk * Mathf.Cos(deg * Mathf.Deg2Rad);
                lo.Ring(E - dl * dd, xl, yl, 0f, _ => rr, rigidL, arc0 - dd);
            }
            const int nShin = 9;
            for (int i = 1; i <= nShin; i++)
            {
                float v = (float)i / (nShin + 1);
                float r = v < 0.3f ? Mathf.Lerp(rk, rshin, Smooth(v / 0.3f)) : Mathf.Lerp(rshin, rhock, Smooth((v - 0.75f) / 0.25f));
                float calf = v < 0.55f ? Mathf.Sin(Mathf.PI * v / 0.55f) : 0f;
                float a = r * (1f + 0.05f * calf), b = r * (1f + 0.22f * calf), d = -0.25f * rl * calf;
                lo.Ring(E + dl * (v * ll), xl, yl, d, phi => Ellipse(phi, a, b), rigidL, arc0 + v * ll);
            }
            lo.Ring(A, xl, yl, 0f, phi => Ellipse(phi, rhock, 1.1f * rhock), rigidL, arc0 + ll);
            foreach (float deg in new[] { 30f, 60f })
            {
                float dd = rhock * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rhock * Mathf.Cos(deg * Mathf.Deg2Rad);
                lo.Ring(A + dl * dd, xl, yl, 0f, phi => Ellipse(phi, rr, 1.1f * rr), rigidL, arc0 + ll + dd);
            }
            lo.Pole(A + dl * (1.1f * rhock), rigidL, arc0 + ll + rhock);
            lo.Emit(m, PartMeshEmitter.PartLeg);

            EmitFoot(m, s, bf, A, rl, rhock, arc0 + ll);
        }

        // The foot in the foot bone's frame (origin the contact point, +Y the ground normal, +Z the heading), turned out
        // by kFootYawDeg and hung from the hock the sim draws (A): its ground is Hock below it.
        void EmitFoot(PartMeshEmitter m, LegSpec s, int bf, Vector3 A, float rl, float rhock, float arc)
        {
            Matrix4x4 frame = m.Bones[bf] * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(s.side * kFootYawDeg, Vector3.up), Vector3.one);
            Vector3 hk = frame.inverse.MultiplyPoint3x4(A);   // the hock, local
            float H = Hock, fl = Mathf.Max(1e-3f, footLength);
            Vector3 O = new Vector3(hk.x, hk.y - H, hk.z);    // the toes' ground under the hock
            Vector4 skin = PartMeshEmitter.Rigid(bf);
            Vector3 P(Vector3 lp) => frame.MultiplyPoint3x4(lp);
            Vector3 V(Vector3 lv) => frame.MultiplyVector(lv).normalized;

            float rt = Mathf.Max(0.42f * rl, 0.05f * fl);          // toe root radius
            float zb = Mathf.Max(0.32f * H, 0.22f * fl);           // the ball ahead of the hock
            Vector3 hock = O + Vector3.up * H, B = O + new Vector3(0f, 1.05f * rt, zb);
            Vector3 mt = (B - hock).normalized;
            float mlen = (B - hock).magnitude;
            float rm0 = 0.82f * rl, rm1 = Mathf.Max(0.7f * rl, 1.15f * rt);

            // the metatarsus: from inside the hock down and forward to the ball, a little wider than deep, a knuckle at
            // the ball
            var met = new LimbLoft(kAround);
            Vector3 mx = V(Vector3.right), my = V(Vector3.Cross(mt, Vector3.right));
            Vector3 tw = V(mt);
            met.Pole(P(hock - mt * (0.8f * rhock)), skin, arc - 0.8f * rhock);
            for (int i = 0; i <= 6; i++)
            {
                float f = i / 6f, r = Mathf.Lerp(rm0, rm1, f) * (1f - 0.1f * Mathf.Sin(Mathf.PI * f));
                met.Ring(P(hock + mt * (f * mlen)), mx, my, 0f, phi => Ellipse(phi, 1.1f * r, 0.9f * r), skin, arc + f * mlen);
            }
            foreach (float deg in new[] { 30f, 60f })
            {
                float dd = rm1 * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rm1 * Mathf.Cos(deg * Mathf.Deg2Rad);
                met.Ring(P(B + mt * dd), mx, my, 0f, phi => Ellipse(phi, 1.1f * rr, 0.9f * rr), skin, arc + mlen + dd);
            }
            met.Pole(P(B + mt * rm1), skin, arc + mlen + rm1);
            met.Emit(m, PartMeshEmitter.PartLeg);

            // the forward toes: lying on the ground from the ball, two knuckles, a talon each
            float tl = TalonLength;
            float fan = toes > 1 ? 24f : 0f;
            for (int i = 0; i < toes; i++)
            {
                float k = toes == 1 ? 0f : i / (toes - 1f) * 2f - 1f;
                Vector3 dir = Quaternion.AngleAxis(k * fan, Vector3.up) * Vector3.forward;
                float len = (0.62f - 0.14f * k * k) * fl;
                Vector3 root = new Vector3(B.x + k * 0.5f * rm1, O.y + rt, B.z) + dir * (0.2f * rt);
                Toe(m, frame, skin, root, dir, len, rt, O.y, tl * (1f - 0.15f * k * k), bf, arc + mlen, false);
            }
            if (hallux)
            {
                // a short toe from the back of the metatarsus' lower end, pointing back and in, off the ground
                Vector3 dir = new Vector3(-0.35f * s.side, -0.3f, -1f).normalized;
                Vector3 root = hock + mt * (0.72f * mlen) + new Vector3(0f, 0f, -0.6f * rm1);
                Toe(m, frame, skin, root, dir, 0.26f * fl, 0.8f * rt, O.y + 0.2f * H, 0.6f * tl, bf, arc + 0.72f * mlen, true);
            }
        }

        // A toe from root along dir (foot-local), lying with its underside on `ground` when level (or straight along dir
        // for the hallux), knuckles at 40 % and 72 %, its tip a pole and a talon curling down whose lowest point is put
        // kTalonClear above `ground` (the hallux's: at `ground`, its own raised floor).
        void Toe(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, Vector3 root, Vector3 dir, float len, float rt, float ground,
                 float talon, int bf, float arc, bool raised)
        {
            float tipR = 0.6f * rt;
            float drop = raised ? 0f : (rt - tipR);                  // a level toe keeps its underside on the ground
            Vector3 C(float u) => root + dir * (len * u) - Vector3.up * (drop * u) + Vector3.up * (0.25f * rt * 4f * u * (1f - u));
            Vector3 D(float u) => (dir * len - Vector3.up * drop + Vector3.up * (0.25f * rt * (4f - 8f * u))).normalized;
            float R(float u)
            {
                float g1 = (u - 0.4f) / 0.08f, g2 = (u - 0.72f) / 0.07f;
                return Mathf.Lerp(rt, tipR, u) * (1f + 0.18f * Mathf.Exp(-g1 * g1) + 0.14f * Mathf.Exp(-g2 * g2));
            }
            var loft = new LimbLoft(kToeAround);
            loft.Pole(frame.MultiplyPoint3x4(C(0f) - D(0f) * rt), skin, arc - rt);
            foreach (float u in new[] { 0f, 0.15f, 0.3f, 0.4f, 0.5f, 0.62f, 0.72f, 0.82f, 0.92f, 1f })
            {
                Vector3 t = D(u);
                Vector3 x = Vector3.Cross(Vector3.up, t);
                x = x.sqrMagnitude > 1e-8f ? x.normalized : Vector3.right;
                Vector3 y = Vector3.Cross(t, x);
                float r = R(u);
                loft.Ring(frame.MultiplyPoint3x4(C(u)), frame.MultiplyVector(x).normalized, frame.MultiplyVector(y).normalized, 0f,
                          phi => Ellipse(phi, r, 0.85f * r), skin, arc + len * u);
            }
            loft.Pole(frame.MultiplyPoint3x4(C(1f) + D(1f) * (0.6f * tipR)), skin, arc + len + 0.6f * tipR);
            loft.Emit(m, PartMeshEmitter.PartLeg);

            // the talon: from inside the toe's tip, pitched down until its lowest point meets the ground
            Vector3 basePt = C(1f) - D(1f) * (0.3f * tipR);
            Vector3 flatDir = new Vector3(D(1f).x, 0f, D(1f).z);
            flatDir = flatDir.sqrMagnitude > 1e-8f ? flatDir.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flatDir).normalized;
            float r0 = 0.95f * tipR, flat = 0.7f;
            float target = ground + (raised ? 0f : kTalonClear);
            float lo = -1.2f, hi = 1.4f;   // pitch (rad, + = down) bracket
            float Low(float a) => TalonLow(basePt, Quaternion.AngleAxis(a * Mathf.Rad2Deg, side), flatDir, r0, talon, talonCurveDeg, flat);
            if (Low(lo) < target) hi = lo;           // already below at the highest pitch: take it (never happens on a level toe)
            else if (Low(hi) > target) lo = hi;      // cannot reach: pitch fully down
            for (int it = 0; it < 40 && hi > lo; it++)
            {
                float mid = 0.5f * (lo + hi);
                if (Low(mid) > target) lo = mid; else hi = mid;
            }
            float pitch = 0.5f * (lo + hi);
            Quaternion pq = Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, side);
            Vector3 cd = pq * flatDir, cu = pq * Vector3.up;
            LimbLoft.Claw(m, frame.MultiplyPoint3x4(basePt), frame.MultiplyVector(cd), frame.MultiplyVector(cu),
                          r0, talon, talonCurveDeg, flat, bf, PartMeshEmitter.PartFoot, kTalonAround, arc + len);
        }

        // The lowest point (foot-local y) of LimbLoft.Claw's talon from basePt along q * dir0, curling toward -(q * up):
        // its base pole, each ring's lowest point (the support of its ellipse) and the tip.
        static float TalonLow(Vector3 basePt, Quaternion q, Vector3 dir0, float r0, float len, float curveDeg, float flat)
        {
            Vector3 dir = q * dir0;
            Vector3 side = PartMeshEmitter.Unit(Vector3.Cross(q * Vector3.up, dir), Vector3.right);
            Vector3 up = Vector3.Cross(dir, side);
            float bend = Mathf.Max(1e-3f, curveDeg * Mathf.Deg2Rad), k = len / bend;
            float low = (basePt - dir * (0.6f * r0)).y;
            const int rings = 6;
            for (int i = 0; i <= rings; i++)
            {
                float u = (float)i / rings;
                Vector3 c = basePt + dir * (k * Mathf.Sin(bend * u)) - up * (k - k * Mathf.Cos(bend * u));
                if (i == rings) { low = Mathf.Min(low, c.y); break; }
                Vector3 y = up * Mathf.Cos(bend * u) + dir * Mathf.Sin(bend * u);
                float r = r0 * Mathf.Pow(1f - u, 0.75f) * (i == 0 ? 0.92f : 1f), a = r * flat;
                low = Mathf.Min(low, c.y - Mathf.Sqrt(a * side.y * a * side.y + r * y.y * r * y.y));
            }
            return low;
        }
    }
}
