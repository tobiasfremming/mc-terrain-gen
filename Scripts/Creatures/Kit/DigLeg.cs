// Creature kit (2026-10-20, the Delver): DigLeg - a heavy digging limb. It walks exactly as a Leg2Bone does (the same
// LegSpec: TwoBoneIK with a pole, swing/knee limits, gait groups, ankle - CreatureLegs runs it unchanged); its look is
// its own, built for a burrower (mole, armadillo, pangolin, badger):
//   root      a shoulder / hip mass buried in the trunk with a deltoid hump on top (shoulderMass, rootDepth), its skin
//             blended from the body to the limb
//   upper     a short thick humerus / thigh tapering to a ball elbow / knee (never creases at any bend)
//   lower     a forearm that swells toward the wrist (forearmSwell: a mole's broad digging forearm), flattened side to side
//   hand      a broad spade (handWidth x lowerRadius, handThick), turned out (handYawDeg), its sole flat on the ground
//   claws     toeCount scimitar claws from the knuckle line, the middle ones longest, tall and flat (clawWidth), curling
//             down so their tips meet the ground - placed by solving the curl for the knuckle height
// `digs` marks a digging limb: a Burrow part poses it in its scoop stroke (Kit/Burrow.cs).
// Material layers: 1 the limb and hand, 11 the claws (Claw shading: keratin). Kit-only; off unless a species names it.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("DigLeg", PartKind.Limb, Bones = "3", Summary = "Heavy digging limb: walks as a Leg2Bone (TwoBoneIK, pole, limits, ankle), drawn with a buried shoulder mass and deltoid hump, a short thick upper limb, a ball joint, a forearm swelling to the wrist, a broad spade hand turned out and big flat scimitar claws (middle longest) whose tips meet the ground. `digs` lets a Burrow part pose it in a scoop stroke.",
          System = "CreatureLegs (gait groups, throttle, sway) + DigLeg.EmitMesh (LimbLoft)", MaterialLayers = new[] { 1, 11 }, KitOnly = true,
          UseWhen = "burrowers and diggers: moles, armadillos, pangolins, badgers, aardvarks, any heavy limb with spade claws")]
    public sealed class DigLeg : LimbPart
    {
        [Param(ParamUnit.Metres, "upper bone length", Min = 0.005f, Max = 2f, Example = "delver fore 0.12")] public float upperLength;
        [Param(ParamUnit.Metres, "lower bone length", Min = 0.005f, Max = 2f, Example = "delver fore 0.115")] public float lowerLength;
        [Param(ParamUnit.Metres, "upper bone radius")] public float upperRadius;
        [Param(ParamUnit.Metres, "lower bone radius")] public float lowerRadius;
        [Param(ParamUnit.Direction, "knee direction hint in the attach frame (x mirrored): front z < 0 (elbow back), rear z > 0 (knee forward)")] public Vector3 pole;
        [Param(ParamUnit.Metres, "wrist / ankle height above the foot's contact point, 0 = 1.2 x lowerRadius, < 0 = none")] public float ankleHeight;
        [Param(ParamUnit.Fraction, "shoulder / hip mass radius, x upperRadius", Min = 1f, Max = 3f, Default = "1.6")] public float shoulderMass = 1.6f;
        [Param(ParamUnit.Fraction, "how deep the mass reaches into the body, x its radius", Min = 0.3f, Max = 2f, Default = "1.2")] public float rootDepth = 1.2f;
        [Param(ParamUnit.Fraction, "forearm swelling toward the wrist, x lowerRadius (0 = a plain taper)", Min = 0f, Max = 1f, Default = "0.3")] public float forearmSwell = 0.3f;
        [Param(ParamUnit.Fraction, "hand half-width at the knuckles, x lowerRadius", Min = 0.5f, Max = 3f, Default = "1.5")] public float handWidth = 1.5f;
        [Param(ParamUnit.Fraction, "hand half-thickness, x lowerRadius", Min = 0.2f, Max = 1.5f, Default = "0.55")] public float handThick = 0.55f;
        [Param(ParamUnit.Degrees, "the hand turned outward from the heading", Min = -30, Max = 60, Default = "15")] public float handYawDeg = 15f;
        [Param(ParamUnit.Metres, "the longest claw's length (0 = 0.9 x footLength)", Example = "delver fore 0.11")] public float clawLength;
        [Param(ParamUnit.Degrees, "claw curl over its length", Min = 10, Max = 140, Default = "70")] public float clawCurveDeg = 70f;
        [Param(ParamUnit.Fraction, "claw width / height at the base (flat side to side)", Min = 0.2f, Max = 1f, Default = "0.55")] public float clawWidth = 0.55f;
        [Param(ParamUnit.Degrees, "fan of the claws from the inner to the outer", Min = 0, Max = 120, Default = "35")] public float clawSpreadDeg = 35f;
        [Param(ParamUnit.Flag, "a digging limb: a Burrow part poses it in its scoop stroke")] public bool digs;
        [Param(ParamUnit.Count, "mesh: vertices round the limb, 0 = 16", Min = 0, Max = 32)] public int around;

        public override int BoneCount(in SpeciesCtx c) => Leg2Bone.Bones;
        public override int MeshOrder => 1;

        public float ClawLength => clawLength > 0f ? clawLength : 0.9f * footLength;

        public override LegSpec ToLegSpec()
        {
            LegSpec l = BaseSpec();
            l.upperLength = upperLength; l.lowerLength = lowerLength; l.upperRadius = upperRadius; l.lowerRadius = lowerRadius;
            l.pole = pole; l.kneeBlend = 0f; l.ankleHeight = ankleHeight; l.clawLength = clawLength;
            return l;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (!(upperLength > 0f && lowerLength > 0f)) return $"{id}: limb lengths must be > 0";
            if (!(upperRadius > 0f && lowerRadius > 0f)) return $"{id}: limb radii must be > 0";
            if (role == LimbRole.Folded || role == LimbRole.WingArm) return $"{id}: a DigLeg walks (use a Leg2Bone for a wing's arm)";
            if (toeCount < 1 || toeCount > 6) return $"{id}: a DigLeg needs 1..6 claws (toeCount)";
            if (clawLength < 0f) return $"{id}: clawLength must be >= 0";
            if (!(footLength > 0f)) return $"{id}: footLength must be > 0";
            if (shoulderMass < 1f || rootDepth <= 0f) return $"{id}: shoulderMass >= 1, rootDepth > 0";
            if (!(handWidth > 0f) || !(handThick > 0f) || !(clawWidth > 0f)) return $"{id}: handWidth, handThick, clawWidth > 0";
            if (forearmSwell < 0f) return $"{id}: forearmSwell >= 0";
            return base.Validate(c);
        }

        // ------------------------------------------------------------------ the mesh

        static float Ellipse(float phi, float a, float b)
        {
            float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            return 1f / Mathf.Sqrt(cs * cs / (a * a) + sn * sn / (b * b));
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            int l = c.Ordinal;
            CreatureSpecies sp = m.Species;
            LegSpec s = sp.legs[l];
            Matrix4x4[] bones = m.Bones;
            LegState st = m.Rest.Legs[l];
            int attach = s.attachJoint, bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1), bf = sp.LegBone(l, 2);
            Vector3 S = st.shoulder, E = st.elbow, A = st.ankle;
            float lu = Mathf.Max(1e-4f, (E - S).magnitude), ll = Mathf.Max(1e-4f, (A - E).magnitude);
            Vector3 du = (E - S) / lu, dl = (A - E) / ll;
            float ru = Mathf.Max(PartMeshEmitter.MinRadius, s.upperRadius), rl = Mathf.Max(PartMeshEmitter.MinRadius, s.lowerRadius);
            float rh = shoulderMass * ru, blob = rootDepth * rh;
            float rf = rl * (1f + forearmSwell);                  // the forearm's fullest
            float rk = Mathf.Max(0.82f * ru, 1.06f * rl);          // ball elbow / knee
            float rw = 0.85f * rl;                                 // wrist
            int ar = around > 0 ? Mathf.Max(8, around) : 16;
            Vector3 up = m.Rest.Rotations[attach] * Vector3.up;

            // ---- upper: buried mass, deltoid hump on top, taper to the ball joint ----
            var up1 = new LimbLoft(ar);
            Vector3 x0 = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(bones[bu].rotation * Vector3.right, du), Vector3.Cross(up, du));
            Vector3 y0 = Vector3.Cross(du, x0);
            Vector3 topDir = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(up, du), y0);
            float tpsi = Mathf.Atan2(Vector3.Dot(topDir, y0), Vector3.Dot(topDir, x0));
            void UpperRing(float sg, Vector4 skin)
            {
                float r0;
                if (sg < 0f)
                {
                    float q = -sg / blob;
                    r0 = rh * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q));
                }
                else
                {
                    float u = sg / lu;
                    r0 = Mathf.Lerp(rh, ru, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.45f)));
                    r0 = Mathf.Lerp(r0, rk, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.45f) / 0.55f)));
                }
                float hump = 0.32f * rh * Mathf.Exp(-Mathf.Pow((sg - 0.05f * lu) / (0.45f * blob + 0.2f * lu), 2f));
                up1.Ring(S + du * sg, x0, y0, 0f, phi =>
                {
                    float lobe = Mathf.Max(0f, Mathf.Cos(phi - tpsi));
                    return Ellipse(phi, r0 * 1.08f, r0) + hump * lobe * lobe;
                }, skin, sg + blob);
            }
            up1.Pole(S - du * blob, PartMeshEmitter.Blend(attach, bu, 0.3f), 0f);
            foreach (float q in new[] { 0.93f, 0.75f, 0.5f, 0.25f }) UpperRing(-blob * q, PartMeshEmitter.Blend(attach, bu, Mathf.Lerp(0.3f, 0.5f, 1f - q)));
            UpperRing(0f, PartMeshEmitter.Blend(attach, bu, 0.5f));
            const int nUp = 10;
            float upEnd = lu - 0.2f * rk;
            for (int i = 1; i <= nUp; i++)
            {
                float sg = upEnd * i / nUp, f = sg / lu;
                float w = Mathf.Min(1f, 0.5f + 0.5f * f / 0.7f);
                UpperRing(sg, w >= 1f ? PartMeshEmitter.Rigid(bu) : PartMeshEmitter.Blend(attach, bu, w));
            }
            foreach (float deg in new[] { 0f, 30f, 60f })
            {
                float d = rk * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rk * Mathf.Cos(deg * Mathf.Deg2Rad);
                up1.Ring(E + du * d, x0, y0, 0f, _ => rr, PartMeshEmitter.Rigid(bu), lu + blob + d);
            }
            up1.Pole(E + du * rk, PartMeshEmitter.Rigid(bu), lu + blob + rk);
            up1.Emit(m, PartMeshEmitter.PartLeg);

            // ---- lower: ball, forearm swelling toward the wrist, flattened side to side (rigid to the lower bone) ----
            var lo = new LimbLoft(ar);
            Vector3 side = PartMeshEmitter.Unit(Vector3.Cross(up, dl), bones[bl].rotation * Vector3.right);
            Vector3 xl = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(side, dl), side), yl = Vector3.Cross(dl, xl);
            Vector4 rigidL = PartMeshEmitter.Rigid(bl);
            lo.Pole(E - dl * rk, rigidL, lu + blob - rk);
            foreach (float deg in new[] { 60f, 30f, 0f })
            {
                float d = rk * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rk * Mathf.Cos(deg * Mathf.Deg2Rad);
                lo.Ring(E - dl * d, xl, yl, 0f, _ => rr, rigidL, lu + blob - d);
            }
            const int nLo = 12;
            for (int i = 1; i <= nLo; i++)
            {
                float t = ll * i / nLo, v = t / ll;
                // elbow -> the full forearm at 65 % -> the wrist
                float r = v < 0.65f ? Mathf.Lerp(rk, rf, Mathf.SmoothStep(0f, 1f, v / 0.65f)) : Mathf.Lerp(rf, rw, Mathf.SmoothStep(0f, 1f, (v - 0.65f) / 0.35f));
                float flat = Mathf.Lerp(1f, 0.82f, Mathf.Sin(Mathf.PI * Mathf.Min(1f, v / 0.9f)));
                lo.Ring(E + dl * t, xl, yl, 0f, phi => Ellipse(phi, r, r * flat), rigidL, lu + blob + t);
            }
            foreach (float deg in new[] { 30f, 60f })
            {
                float d = rw * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rw * Mathf.Cos(deg * Mathf.Deg2Rad);
                lo.Ring(A + dl * d, xl, yl, 0f, _ => rr, rigidL, lu + blob + ll + d);
            }
            lo.Pole(A + dl * rw, rigidL, lu + blob + ll + rw);
            lo.Emit(m, PartMeshEmitter.PartLeg);

            EmitHand(m, s, bf, rw, rl, lu + blob + ll);
        }

        // The spade hand and its claws, rigid to the hand bone (origin on the ground under the wrist, +z the heading).
        void EmitHand(PartMeshEmitter m, LegSpec s, int bf, float rw, float rl, float arc)
        {
            Matrix4x4 frame = m.Bones[bf] * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(s.side * handYawDeg, Vector3.up), Vector3.one);
            Vector4 skin = PartMeshEmitter.Rigid(bf);
            float fl = Mathf.Max(1e-3f, s.footLength);
            float h = Mathf.Max(s.AnkleHeight, 0.6f * rw);
            float hw = handWidth * rl, ht = handThick * rl;
            float zh = -0.3f * fl, zk = 0.55f * fl;
            // a section: top and bottom heights, half-width, by z (heel -> wrist -> knuckles)
            float Top(float z) => z <= 0f ? Mathf.Lerp(0.55f * h, h + 0.6f * rw, Mathf.SmoothStep(0f, 1f, (z - zh) / -zh))
                                          : Mathf.Lerp(h + 0.6f * rw, 2f * ht, Mathf.SmoothStep(0f, 1f, z / zk));
            float Wid(float z) => z <= 0f ? Mathf.Lerp(0.75f * rw, 1.05f * rw, Mathf.SmoothStep(0f, 1f, (z - zh) / -zh))
                                          : Mathf.Lerp(1.05f * rw, hw, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(z / (0.8f * zk))));
            Vector3 X = frame.MultiplyVector(Vector3.right).normalized, Y = frame.MultiplyVector(Vector3.up).normalized;
            var palm = new LimbLoft(Mathf.Max(12, around > 0 ? around : 16));
            palm.Pole(frame.MultiplyPoint3x4(new Vector3(0f, 0.5f * Top(zh), zh - 0.1f * rw)), skin, arc);
            foreach (float z in new[] { zh + 0.25f * rw, 0.6f * zh, 0.25f * zh, 0f, 0.25f * zk, 0.5f * zk, 0.75f * zk, zk })
            {
                float top = Top(z), b = 0.5f * top, a = Wid(z);
                palm.Ring(frame.MultiplyPoint3x4(new Vector3(0f, b, z)), X, Y, 0f, phi =>
                {
                    float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    float e = sn < 0f ? 3f : 2.2f;   // a flat sole, a domed back
                    return 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(cs) / a, e) + Mathf.Pow(Mathf.Abs(sn) / b, e), 1f / e);
                }, skin, arc + z - zh);
            }
            palm.Pole(frame.MultiplyPoint3x4(new Vector3(0f, ht, zk + 0.6f * ht)), skin, arc + zk - zh + ht);
            palm.Emit(m, PartMeshEmitter.PartLeg);

            // claws: from the knuckle line, the middle longest; each one's tilt solved so its tip meets the ground
            int n = Mathf.Clamp(s.toeCount, 1, 6);
            float cl = ClawLength;
            float bend = Mathf.Max(1e-3f, clawCurveDeg * Mathf.Deg2Rad);
            for (int i = 0; i < n; i++)
            {
                float f = n == 1 ? 0.5f : (float)i / (n - 1);
                float across = 2f * f - 1f;
                float len = cl * (1f - 0.32f * Mathf.Pow(Mathf.Abs(across), 1.5f));
                float r0 = Mathf.Min(0.9f * ht, 0.16f * len + 0.25f * ht);
                float yaw = clawSpreadDeg * (f - 0.5f) * -s.side;   // fans outward on either side
                Vector3 basePt = new Vector3(across * 0.72f * hw, 0.95f * ht, zk - 0.15f * ht);
                // the arc's tip relative to its base for a level start: (k sin b, -k (1 - cos b)); tilt it by a so the tip's
                // lowest edge (a little above its centre line) touches y = 0
                float k = len / bend;
                float dz = k * Mathf.Sin(bend), dy = -k * (1f - Mathf.Cos(bend));
                float R = Mathf.Sqrt(dz * dz + dy * dy), phi0 = Mathf.Atan2(dy, dz);
                float want = 0.03f * r0 - basePt.y;
                float a = Mathf.Asin(Mathf.Clamp(want / Mathf.Max(1e-6f, R), -1f, 1f)) - phi0;
                a = Mathf.Clamp(a, -0.6f, 1.2f);
                Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-a * Mathf.Rad2Deg, Vector3.right);
                Vector3 dir = q * Vector3.forward, cup = q * Vector3.up;
                LimbLoft.Claw(m, frame.MultiplyPoint3x4(basePt), frame.MultiplyVector(dir), frame.MultiplyVector(cup),
                              r0, len, clawCurveDeg, clawWidth, bf, PartMeshEmitter.PartPincer, 8, arc + zk - zh);
            }
        }
    }
}
