// Creature kit (2026-10-20, "remake legs for the crocodile and the Komodo dragon"): SprawlLeg - a heavy sprawling
// vertebrate limb. It walks exactly as a Leg2Bone does (the same LegSpec: TwoBoneIK with a pole, swing/knee limits,
// gait groups, ankle, toe peel - CreatureLegs runs it unchanged); only its look is its own:
//   root      a broad flank mass, buried in the body and stretched along the spine (rootStretch), its skin blended
//             from the body to the limb, so the shoulder / hip grows out of the trunk instead of a tube on a barrel
//   upper     upper arm / thigh with a muscle belly on its top (muscle, muscleAt)
//   joint     a ball elbow / knee (never creases at any bend) wrapped in loose skin folds on both bones, deepest in
//             the crease (folds, foldHeight)
//   lower     a thick forearm / shin with a muscle belly near the joint, optionally armoured with rows of large
//             scales (scaleRows, scaleAround, scaleHeight), a heavy wrist / ankle (wrist, wristFolds)
//   foot      DigitFoot: a broad sole, digits one by one (digitLengths, phalanges, digitSpreadDeg), claws on the inner
//             clawedDigits (clawLength, clawCurveDeg, clawThickness), webbing (toeWeb)
// Material layers: 1 the limb and foot, 2 the claws (restyle with Layers, e.g. dark keratin).
// Kit-only part (no legacy CreatureSpecies field); off unless a species names it, so every shipped mesh is unchanged.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SprawlLeg", PartKind.Limb, Bones = "3", Summary = "Heavy sprawling vertebrate limb: walks as a Leg2Bone (TwoBoneIK, pole, limits, ankle), drawn with a flank mass blended into the trunk, a muscled upper limb, a ball joint in loose skin folds, a thick forearm/shin (optional rows of large scales), a heavy wrist and a foot of individually described digits with thick curved claws and webbing.",
          System = "CreatureLegs (gait groups, throttle, sway) + SprawlLeg.EmitMesh (LimbLoft, DigitFoot)", MaterialLayers = new[] { 1, 2 }, KitOnly = true,
          UseWhen = "crocodilians, monitors, tegus, salamanders, any heavy-bodied sprawling or semi-erect walker")]
    public sealed class SprawlLeg : LimbPart
    {
        // ---- the walking limb (as Leg2Bone) ----
        [Param(ParamUnit.Metres, "upper bone length", Min = 0.005f, Max = 2f, Example = "komodo fore 0.13")] public float upperLength;
        [Param(ParamUnit.Metres, "lower bone length", Min = 0.005f, Max = 2f, Example = "komodo fore 0.12")] public float lowerLength;
        [Param(ParamUnit.Metres, "upper bone radius (the limb's thickness by the root)")] public float upperRadius;
        [Param(ParamUnit.Metres, "lower bone radius")] public float lowerRadius;
        [Param(ParamUnit.Direction, "knee direction hint in the attach frame (x mirrored): front z < 0 (elbow back), rear z > 0 (knee forward)")] public Vector3 pole;
        [Param(ParamUnit.Metres, "ankle height above the foot's contact point, 0 = 1.2 x lowerRadius, < 0 = none")] public float ankleHeight;
        // ---- the root ----
        [Param(ParamUnit.Fraction, "flank mass radius, x upperRadius", Min = 1f, Max = 3f, Default = "1.5")] public float rootMass = 1.5f;
        [Param(ParamUnit.Fraction, "flank mass stretched along the spine, x its radius", Min = 1f, Max = 2.5f, Default = "1.4")] public float rootStretch = 1.4f;
        [Param(ParamUnit.Fraction, "how deep the flank mass reaches into the body, x its radius", Min = 0.5f, Max = 2f, Default = "1.1")] public float rootDepth = 1.1f;
        // ---- muscles ----
        [Param(ParamUnit.Fraction, "muscle belly on top of the upper arm / thigh, x upperRadius", Min = 0f, Max = 1f, Default = "0.25")] public float muscle = 0.25f;
        [Param(ParamUnit.Fraction, "where along the upper bone the belly is fullest", Min = 0.1f, Max = 0.9f, Default = "0.35")] public float muscleAt = 0.35f;
        [Param(ParamUnit.Fraction, "elbow / knee ball radius, x upperRadius (at least 1.05 x lowerRadius)", Min = 0.4f, Max = 1.5f, Default = "0.8")] public float kneeSize = 0.8f;
        [Param(ParamUnit.Fraction, "forearm / calf belly near the joint, x lowerRadius", Min = 0f, Max = 1f, Default = "0.2")] public float forearm = 0.2f;
        [Param(ParamUnit.Fraction, "wrist / ankle radius, x lowerRadius", Min = 0.4f, Max = 1.2f, Default = "0.75")] public float wrist = 0.75f;
        // ---- skin ----
        [Param(ParamUnit.Count, "loose skin folds on each side of the elbow / knee (0 = smooth)", Min = 0, Max = 6)] public int folds;
        [Param(ParamUnit.Fraction, "fold height, x the joint radius", Min = 0f, Max = 0.3f, Default = "0.08")] public float foldHeight = 0.08f;
        [Param(ParamUnit.Count, "folds round the wrist / ankle (0 = none)", Min = 0, Max = 4)] public int wristFolds;
        [Param(ParamUnit.Count, "rows of large scales along the forearm / shin (0 = none)", Min = 0, Max = 12)] public int scaleRows;
        [Param(ParamUnit.Count, "large scales round the forearm / shin", Min = 3, Max = 12, Default = "6")] public int scaleAround = 6;
        [Param(ParamUnit.Fraction, "large-scale relief, x lowerRadius", Min = 0f, Max = 0.3f, Default = "0.07")] public float scaleHeight = 0.07f;
        [Param(ParamUnit.Count, "mesh: vertices round the limb, 0 = 16", Min = 0, Max = 32)] public int around;
        // ---- the foot ----
        [Param(ParamUnit.Fraction, "each digit's length, x footLength, inner (I) to outer; empty = a lizard's (front 0.45 0.65 0.8 0.85 0.55, hind with the 4th longest)")] public float[] digitLengths = new float[0];
        [Param(ParamUnit.Count, "phalanges per digit, inner to outer; empty = the reptile formula 2 3 4 5 3 (hind 2 3 4 5 4)")] public int[] phalanges = new int[0];
        [Param(ParamUnit.Degrees, "fan of the digits from the innermost to the outermost", Min = 0, Max = 160, Default = "70")] public float digitSpreadDeg = 70f;
        [Param(ParamUnit.Degrees, "the whole foot turned outward from the heading", Min = -30, Max = 60, Default = "12")] public float footYawDeg = 12f;
        [Param(ParamUnit.Metres, "digit radius at the root, 0 = 0.075 x footLength")] public float digitRadius;
        [Param(ParamUnit.Fraction, "sole width at the knuckles, x the digits' spread (0 = auto)", Min = 0f, Max = 3f)] public float palmWidth;
        [Param(ParamUnit.Count, "digits with a claw, counted from the inner one (-1 = all)", Min = -1, Max = 6, Default = "-1")] public int clawedDigits = -1;
        [Param(ParamUnit.Metres, "claw length, 0 = 0.18 x footLength", Example = "komodo 0.03")] public float clawLength;
        [Param(ParamUnit.Degrees, "claw curl over its length, 0 = 60")] public float clawCurveDeg;
        [Param(ParamUnit.Fraction, "claw base radius, x the digit's tip radius", Min = 0.2f, Max = 1.5f, Default = "0.8")] public float clawThickness = 0.8f;
        [Param(ParamUnit.Fraction, "webbing between the digits, share of their length (0 = none)", Min = 0f, Max = 1f)] public float toeWeb;

        const float kShoulder = 1.12f;   // the limb's radius where it leaves the flank, x upperRadius

        public override int BoneCount(in SpeciesCtx c) => Leg2Bone.Bones;
        public override int MeshOrder => 1;
        protected override void CopyArrays()
        {
            digitLengths = (float[])digitLengths?.Clone() ?? new float[0];
            phalanges = (int[])phalanges?.Clone() ?? new int[0];
        }

        public override LegSpec ToLegSpec()
        {
            LegSpec l = BaseSpec();
            l.upperLength = upperLength; l.lowerLength = lowerLength; l.upperRadius = upperRadius; l.lowerRadius = lowerRadius;
            l.pole = pole; l.kneeBlend = 0f; l.ankleHeight = ankleHeight;
            l.clawLength = clawLength; l.clawCurveDeg = clawCurveDeg; l.toeWeb = toeWeb;
            return l;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (!(upperLength > 0f && lowerLength > 0f)) return $"{id}: limb lengths must be > 0";
            if (!(upperRadius > 0f && lowerRadius > 0f)) return $"{id}: limb radii must be > 0";
            if (role == LimbRole.Folded || role == LimbRole.WingArm) return $"{id}: a SprawlLeg walks (use a Leg2Bone for a wing's arm)";
            if (toeCount < 1 || toeCount > 6) return $"{id}: a SprawlLeg needs 1..6 digits (toeCount)";
            if (digitLengths != null && digitLengths.Length != 0 && digitLengths.Length != toeCount) return $"{id}: digitLengths needs one entry per digit ({toeCount})";
            if (phalanges != null && phalanges.Length != 0 && phalanges.Length != toeCount) return $"{id}: phalanges needs one entry per digit ({toeCount})";
            if (digitLengths != null) foreach (float d in digitLengths) if (!(d > 0f && d <= 3f)) return $"{id}: digitLengths must be in (0, 3]";
            if (phalanges != null) foreach (int p in phalanges) if (p < 0 || p > 6) return $"{id}: phalanges must be in 0..6";
            if (rootMass < 1f || rootStretch < 1f || rootDepth <= 0f) return $"{id}: rootMass and rootStretch must be >= 1, rootDepth > 0";
            if (wrist <= 0f || kneeSize <= 0f) return $"{id}: wrist and kneeSize must be > 0";
            if (scaleRows > 0 && scaleAround < 3) return $"{id}: scaleAround must be >= 3";
            return base.Validate(c);
        }

        // Repeat taper (CompiledSpecies.Taper): lengths x f.
        internal void Taper(float f)
        {
            upperLength *= f; lowerLength *= f; upperRadius *= f; lowerRadius *= f; ankleHeight *= f; clawLength *= f; digitRadius *= f;
        }

        // ------------------------------------------------------------------ the foot's digits

        static readonly float[] kFore5 = { 0.45f, 0.65f, 0.8f, 0.85f, 0.55f }, kHind5 = { 0.4f, 0.62f, 0.82f, 1f, 0.62f };
        static readonly int[] kForeP = { 2, 3, 4, 5, 3 }, kHindP = { 2, 3, 4, 5, 4 };

        // Digit length (x footLength) and phalanges of digit i, inner to outer; a hind foot has its knee pole forward.
        public float DigitLength(int i, bool hind)
        {
            if (digitLengths != null && digitLengths.Length == toeCount) return digitLengths[i];
            float[] t = hind ? kHind5 : kFore5;
            float f = toeCount == 1 ? 0.5f : (float)i / (toeCount - 1);
            return Sample(t, f);
        }

        public int Phalanges(int i, bool hind)
        {
            if (phalanges != null && phalanges.Length == toeCount) return phalanges[i];
            int[] t = hind ? kHindP : kForeP;
            float f = toeCount == 1 ? 0.5f : (float)i / (toeCount - 1);
            return t[Mathf.Clamp(Mathf.RoundToInt(f * (t.Length - 1)), 0, t.Length - 1)];
        }

        static float Sample(float[] t, float f)
        {
            float x = f * (t.Length - 1);
            int i = Mathf.Min((int)x, t.Length - 2);
            return Mathf.Lerp(t[i], t[i + 1], x - i);
        }

        // ------------------------------------------------------------------ the mesh

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
            Vector3 du = bones[bu].rotation * Vector3.forward, dl = bones[bl].rotation * Vector3.forward;
            float ru = Mathf.Max(PartMeshEmitter.MinRadius, s.upperRadius), rl = Mathf.Max(PartMeshEmitter.MinRadius, s.lowerRadius);
            float rh = rootMass * ru, rs = Mathf.Min(rh, kShoulder * ru), rk = Mathf.Max(kneeSize * ru, 1.05f * rl), rw = wrist * rl;
            float blob = rootDepth * rh, total = lu + ll;
            int ar = around > 0 ? Mathf.Max(8, around) : 16;
            bool hind = s.pole.z > 0f;

            // the body's long axis at the attach joint (the flank mass stretches along it), the world up of the rest
            // pose (the muscle sits on top), the crease of the bend (folds deepest there)
            Vector3 spineT = m.Rest.Rotations[attach] * Vector3.forward;
            Vector3 up = m.Rest.Rotations[attach] * Vector3.up;
            Vector3 creaseU = PartMeshEmitter.Unit(dl - du * Vector3.Dot(dl, du), -up);   // on the upper tube, toward the lower bone
            Vector3 creaseL = PartMeshEmitter.Unit(-du + dl * Vector3.Dot(du, dl), -up);  // on the lower tube, toward the upper bone

            float FoldRidge(float dist, int count, float span)
            {
                // count ridges spaced span / count apart, starting just off the joint ball, fading out
                if (count <= 0 || dist < 0f || dist > span) return 0f;
                float w = span / count, f = dist / w;
                float ridge = Mathf.Pow(0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * f), 2f);
                return ridge * (1f - dist / span);
            }

            // ---- upper tube: buried root, shoulder, upper arm, ending in the ball elbow (rigid to the upper bone) ----
            var up1 = new LimbLoft(ar);
            Vector3 x0 = bones[bu].rotation * Vector3.right;
            float foldSpan = Mathf.Max(1, folds) * 0.5f * rk;
            void UpperRing(float sg, Vector4 skin)
            {
                Vector3 c = S + du * sg;
                Vector3 x = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(x0, du), x0), y = Vector3.Cross(du, x);
                // base radius: the root ellipsoid, then the limb tapering to the joint
                float r0;
                // inside the body the mass is fullest (rh) half way in, rounding off at the buried end; where it
                // leaves the flank it is only kShoulder x upperRadius, so the limb emerges from a broad base
                if (sg < 0f)
                {
                    float q = -sg / blob;
                    r0 = (rs + (rh - rs) * Mathf.Sin(0.5f * Mathf.PI * Mathf.Min(1f, q / 0.5f))) * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q));
                }
                else { float u = sg / lu; r0 = rk + (rs - rk) * Mathf.Pow(1f - u, 1.4f); }
                float u01 = Mathf.Clamp01(sg / lu);
                // the stretch along the spine: full in the body, gone by mid upper arm
                float stretch = 1f + (rootStretch - 1f) * (sg <= 0f ? Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(-sg / (0.5f * blob))) : 0.6f * Mathf.Clamp01(1f - sg / (0.4f * lu)));
                Vector3 sDir = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(spineT, du), x);
                float psi = Mathf.Atan2(Vector3.Dot(sDir, y), Vector3.Dot(sDir, x));
                Vector3 topDir = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(up, du), y);
                float tpsi = Mathf.Atan2(Vector3.Dot(topDir, y), Vector3.Dot(topDir, x));
                float belly = sg > 0f ? muscle * ru * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u01, Mathf.Log(0.5f) / Mathf.Log(muscleAt)))), 1.5f) : 0f;
                float cpsi = Mathf.Atan2(Vector3.Dot(creaseU, y), Vector3.Dot(creaseU, x));
                float fold = foldHeight * rk * FoldRidge(lu - rk * 0.55f - sg, folds, foldSpan);
                up1.Ring(c, x, y, 0f, phi =>
                {
                    float cs = Mathf.Cos(phi - psi), sn = Mathf.Sin(phi - psi);
                    float a = r0 * stretch, b = r0;
                    float rr = 1f / Mathf.Sqrt(cs * cs / (a * a) + sn * sn / (b * b));
                    float lobe = Mathf.Max(0f, Mathf.Cos(phi - tpsi));
                    rr += belly * lobe * lobe;
                    rr += fold * (0.55f + 0.45f * Mathf.Cos(phi - cpsi));
                    return rr;
                }, skin, sg + blob);
            }
            up1.Pole(S - du * blob, PartMeshEmitter.Blend(attach, bu, 0.3f), 0f);
            float[] rootQ = { 0.93f, 0.75f, 0.5f, 0.25f };
            foreach (float q in rootQ) UpperRing(-blob * q, PartMeshEmitter.Blend(attach, bu, Mathf.Lerp(0.3f, 0.5f, 1f - q)));
            UpperRing(0f, PartMeshEmitter.Blend(attach, bu, 0.5f));
            int nUp = Mathf.Max(8, Mathf.CeilToInt(lu / Mathf.Max(1e-4f, Mathf.Min(0.1f * lu, foldSpan / Mathf.Max(1, folds) / 3f))));
            float upEnd = lu - 0.2f * rk;   // the last ring sits on the ball, under its equator
            for (int i = 1; i <= nUp; i++)
            {
                float sg = upEnd * i / nUp, f = sg / lu;
                float w = Mathf.Min(1f, 0.5f + 0.5f * f / 0.8f);
                UpperRing(sg, w >= 1f ? PartMeshEmitter.Rigid(bu) : PartMeshEmitter.Blend(attach, bu, w));
            }
            // the ball: a cap of the joint radius round the elbow
            {
                Vector3 x = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(x0, du), x0), y = Vector3.Cross(du, x);
                foreach (float deg in new[] { 0f, 30f, 60f })
                {
                    float d = rk * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rk * Mathf.Cos(deg * Mathf.Deg2Rad);
                    up1.Ring(E + du * d, x, y, 0f, _ => rr, PartMeshEmitter.Rigid(bu), lu + blob + d);
                }
                up1.Pole(E + du * rk, PartMeshEmitter.Rigid(bu), lu + blob + rk);
            }
            up1.Emit(m, PartMeshEmitter.PartLeg);

            // ---- lower tube: from the ball through the forearm / shin to the wrist knob (rigid to the lower bone) ----
            var lo = new LimbLoft(ar);
            Vector3 xl0 = bones[bl].rotation * Vector3.right;
            Vector3 xl = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(xl0, dl), xl0), yl = Vector3.Cross(dl, xl);
            Vector4 rigidL = PartMeshEmitter.Rigid(bl);
            lo.Pole(E - dl * rk, rigidL, lu + blob - rk);
            foreach (float deg in new[] { 60f, 30f, 0f })
            {
                float d = rk * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rk * Mathf.Cos(deg * Mathf.Deg2Rad);
                lo.Ring(E - dl * d, xl, yl, 0f, _ => rr, rigidL, lu + blob - d);
            }
            float cpsiL = Mathf.Atan2(Vector3.Dot(creaseL, yl), Vector3.Dot(creaseL, xl));
            Vector3 backDir = -yl;   // the calf / forearm belly sits behind the bone (Leg2Bone's kCalfBack side)
            float bpsi = Mathf.Atan2(Vector3.Dot(backDir, yl), Vector3.Dot(backDir, xl));
            float scaleStart = rk * 0.9f, scaleEnd = ll - rw * 0.9f;
            float wristSpan = Mathf.Max(1, wristFolds) * 0.45f * rw;
            int nLo = Mathf.Max(Mathf.Max(10, scaleRows * 4), Mathf.CeilToInt(ll / Mathf.Max(1e-4f, foldSpan / Mathf.Max(1, folds) / 3f)));
            for (int i = 1; i <= nLo; i++)
            {
                float t = ll * i / nLo;
                float v = t / ll;
                float baseR = Mathf.Lerp(rk, rw, Mathf.SmoothStep(0f, 1f, v));
                float belly = forearm * rl * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v / 0.75f))), 1.3f) * (1f - 0.3f * v);
                float fold = foldHeight * rk * FoldRidge(t - rk * 0.55f, folds, foldSpan);
                float wfold = foldHeight * rw * 1.2f * FoldRidge(ll - rw * 0.4f - t, wristFolds, wristSpan);
                float rowF = scaleRows > 0 && t > scaleStart && t < scaleEnd ? (t - scaleStart) / (scaleEnd - scaleStart) * scaleRows : -1f;
                lo.Ring(E + dl * t, xl, yl, 0f, phi =>
                {
                    float rr = baseR;
                    float lobe = Mathf.Max(0f, Mathf.Cos(phi - bpsi));
                    rr += belly * lobe * lobe;
                    rr += fold * (0.55f + 0.45f * Mathf.Cos(phi - cpsiL));
                    rr += wfold;
                    if (rowF >= 0f)
                    {
                        // a row of large scales: raised plates with grooves between them, offset by half a scale per row
                        int row = (int)rowF;
                        float fu = rowF - row;
                        float fv = phi / (2f * Mathf.PI) * scaleAround + 0.5f * (row & 1);
                        fv -= Mathf.Floor(fv);
                        float plate = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * fu)), 0.35f) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * fv)), 0.35f);
                        rr += scaleHeight * rl * (plate - 0.5f);
                    }
                    return rr;
                }, rigidL, lu + blob + t);
            }
            foreach (float deg in new[] { 30f, 60f })
            {
                float d = rw * Mathf.Sin(deg * Mathf.Deg2Rad), rr = rw * Mathf.Cos(deg * Mathf.Deg2Rad);
                lo.Ring(A + dl * d, xl, yl, 0f, _ => rr, rigidL, total + blob + d);
            }
            lo.Pole(A + dl * rw, rigidL, total + blob + rw);
            lo.Emit(m, PartMeshEmitter.PartLeg);

            // ---- the foot ----
            float fl = Mathf.Max(1e-3f, s.footLength);
            int n = Mathf.Clamp(toeCount, 1, 6);
            float rd = digitRadius > 0f ? digitRadius : 0.075f * fl;
            var digits = new DigitSpec[n];
            int clawed = clawedDigits < 0 ? n : clawedDigits;
            const float kInnerShare = 0.22f;   // digit I points a little inward of forward, the fan opens outward
            for (int i = 0; i < n; i++)
            {
                float f = n == 1 ? 0.5f : (float)i / (n - 1);
                float len = DigitLength(i, hind) * fl;
                digits[i] = new DigitSpec
                {
                    length = len,
                    radius = rd * Mathf.Lerp(0.85f, 1.05f, Mathf.Sin(Mathf.PI * f)) * Mathf.Sqrt(Mathf.Clamp(len / (0.7f * fl), 0.6f, 1.4f)),
                    phalanges = Phalanges(i, hind),
                    yawDeg = digitSpreadDeg * (f - kInnerShare),
                    across = n == 1 ? 0f : 2f * f - 1f,
                    claw = i < clawed,
                };
            }
            float spreadW = 0.42f * rd * 2f * n;
            var foot = new DigitFoot.Spec
            {
                footLength = fl, ankleHeight = s.AnkleHeight, ankleRadius = rw, yawDeg = footYawDeg, side = s.side,
                palmWidth = (palmWidth > 0f ? palmWidth : 1f) * Mathf.Max(1.1f * rw, 0.85f * spreadW), palmThick = 1.25f, knuckleBump = 0.12f,
                clawLength = clawLength, clawCurveDeg = clawCurveDeg, clawThickness = clawThickness, clawFlat = 0.62f,
                web = toeWeb, digits = digits,
            };
            DigitFoot.Emit(m, bf, foot, total + blob + rw, ar, 8);
        }
    }
}
