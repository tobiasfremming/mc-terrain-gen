// Creature kit archetype: ArthropodPlan - a whole spider or crab from a handful of anatomical numbers (see
// Archetypes/IArchetype.cs for archetypes and the override path). Compose expands it into ordinary parts and default
// modules, deriving everything else the way the legacy spider generator did (legs_impl/gen_species.py):
//   * legs from a family template (LegFamilies: arachnid 7 segments, decapod 6), scaled per pair by a leg formula;
//   * legs fanned radially (rest yaw front .. back) and attached on the VISIBLE body (the spider's profile, the
//     crab's carapace margin - CrabShell's outline), never on the hidden spine;
//   * the rest stance from the leg's own rest pose (RestStance: the body height that puts the reference leg's foot on
//     the ground unbent, then each pair's ground radius), so the IK draws the authored leg (s = 0, pitch ~ 0);
//   * gait numbers as fractions of the leg and Froude times (GaitDefaults);
//   * crab: chelipeds held in their own rest shape (a crusher and a cutter), eye stalks that fold, an optional rock
//     tuck whose fold poses are derived from the legs' and the shell's geometry (CrabFold), sideways travel.
// Generated ids: spider - body, leg0..legN-1 (mirrored), palp, chelicera, eyes; crab - body, carapace, leg0..legN-1
// (mirrored), crusher, cutter, crusherClaw, cutterClaw, stalk (mirrored), eyes, tuck. Default modules (only when the
// asset lists none of the type): Walk, Wander, Skin, Chitin.
// Every length is a fraction of bodyLength (or of the leg); author at scale 1. Compose time only: it allocates.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public enum ArthropodForm { Spider = 0, Crab = 1 }

    // FormDefault: crabs walk sideways, spiders forward.
    public enum ArthropodTravel { FormDefault = 0, Forward = 1, Sideways = 2 }

    [Serializable]
    [Part("ArthropodPlan", PartKind.Sugar, Bones = "= the generated parts", Summary = "Archetype: a whole arthropod - spider (prosoma + opisthosoma, 7-segment legs, palps, chelicerae) or crab (carapace, 6-segment legs on its margin, a crusher and a cutter cheliped, folding eye stalks, optional rock tuck, sideways walking) - from a few anatomical numbers. Rest stance solved from the leg shape (RestStance), gait from leg length (GaitDefaults, Froude). Explicit parts with a generated id override it; added parts layer on.",
          System = "compose (CompiledSpecies, IArchetype) + RestStance, GaitDefaults, LegFamilies, CrabShell, CrabFold", KitOnly = true, UseWhen = "spiders, crabs: start here, then add or override parts")]
    public sealed class ArthropodPlan : CreaturePart, IArchetype
    {
        [Param(ParamUnit.Enum, "body plan: Spider (prosoma + opisthosoma, arachnid legs) or Crab (carapace, decapod legs, chelipeds)")] public ArthropodForm form;
        [Param(ParamUnit.Metres, "spider: prosoma + abdomen length; crab: carapace length", Example = "tarantula 0.41, stone crab 0.31")] public float bodyLength = 0.4f;
        [Param(ParamUnit.Fraction, "spider: prosoma width / bodyLength (0 = 0.34); crab: carapace width / length (0 = 1.3)", Min = 0, Max = 3)] public float bodyWidth;
        [Param(ParamUnit.Fraction, "spider: abdomen size x the tarantula's (0 = 1); crab: dome height / carapace half width (0 = 0.42)", Min = 0, Max = 3)] public float bodyBulk;
        [Param(ParamUnit.Count, "walking leg pairs (0 = 4)", Min = 0, Max = 6)] public int legPairs;
        [Param(ParamUnit.Fraction, "longest walking leg / body (spider: / bodyLength; crab: / carapace WIDTH) (0 = 1.05)", Min = 0, Max = 4)] public float legLength;
        [Param(ParamUnit.Fraction, "relative leg lengths front to back, one per pair, normalised to the longest (empty = the family's: spider 4-1-2-3, crab II-III longest)")] public float[] legFormula = new float[0];
        [Param(ParamUnit.Degrees, "rest yaw of the front pair from forward (0 = spider 32, crab 52)", Min = 0, Max = 180)] public float frontYawDeg;
        [Param(ParamUnit.Degrees, "rest yaw of the hind pair from forward (0 = spider 152, crab 142)", Min = 0, Max = 180)] public float backYawDeg;
        [Param(ParamUnit.Fraction, "leg thickness x the template's (0 = 1)", Min = 0, Max = 3)] public float legBulk;
        [Param(ParamUnit.Fraction, "crab: walking legs flattened (broad, flat merus and propodus: height = (1 - this) x width) (0 = 0.35, < 0 = round)", Min = -1, Max = 0.8f)] public float legFlatten;
        [Param(ParamUnit.Fraction, "posture: rest bends x this (0 = spider 1, crab 1.15 - arched legs with room to reach out and pull in sideways; > 1 higher knees, a lower body)", Min = 0, Max = 1.4f)] public float posture;
        [Param(ParamUnit.Fraction, "step time in Froude units t sqrt(g / L) (0 = spider 1.8, crab 1.25)", Min = 0, Max = 5)] public float stepFroude;
        [Param(ParamUnit.Fraction, "cruise speed in Froude units v / sqrt(g L) (0 = spider 0.08, crab 0.1)", Min = 0, Max = 1)] public float speedFroude;
        [Param(ParamUnit.Fraction, "crab: claw size x the default (0 = 1, < 0 = no chelipeds); spider: palp size (0 = 1, < 0 = none)", Min = -1, Max = 3)] public float clawSize;
        [Param(ParamUnit.Fraction, "crab: the crusher (right) claw x the cutter (0 = 1.3)", Min = 0, Max = 3)] public float crusherRatio;
        [Param(ParamUnit.Fraction, "crab: carapace lumps / its width (0 = 0.035, < 0 = smooth)", Min = -1, Max = 0.2f)] public float lumps;
        [Param(ParamUnit.Fraction, "crab: the lumps 0 = smooth bumps .. 1 = ridged facets (weathered rock) (0 = 0.35)", Min = 0, Max = 1)] public float lumpRidge;
        [Param(ParamUnit.Enum, "crab: carapace outline - Oval (stone crab: widest ahead of the middle, a blunt front arc) or Pear (king crab: widest behind, narrowing to the front)")] public CrabOutline outline;
        [Param(ParamUnit.Degrees, "crab: rest yaw of the chelipeds from forward (0 = 30; small = held across the face)", Min = 0, Max = 90)] public float clawYawDeg;
        [Param(ParamUnit.Flag, "crab: hides as a rock when scared - a Tuck part whose folds are derived from the legs and the shell (legs flat on the ground under the shell's margin, claws across the face, stalks into their orbits)")] public bool tuck;
        [Param(ParamUnit.Seconds, "crab tuck: mean time up and about between random scares (0 = only real scares, CreatureSim.Scare)", Min = 0, Example = "stone crab 14")] public float tuckScareSeconds;
        [Param(ParamUnit.Enum, "how it travels: FormDefault (crab sideways, spider forward), Forward, Sideways (the generated Walk's travel)")] public ArthropodTravel travel;

        // ------------------------------------------------------------ form defaults

        bool Crab => form == ArthropodForm.Crab;
        LegFamily Family => Crab ? LegFamily.Decapod : LegFamily.Arachnid;
        float BodyWidth => bodyWidth > 0f ? bodyWidth : Crab ? 1.3f : 0.34f;
        float Bulk => bodyBulk > 0f ? bodyBulk : Crab ? 0.42f : 1f;
        int Pairs => legPairs > 0 ? legPairs : 4;
        float LegLength => legLength > 0f ? legLength : 1.05f;
        float FrontYaw => frontYawDeg > 0f ? frontYawDeg : Crab ? 52f : 32f;
        float BackYaw => backYawDeg > 0f ? backYawDeg : Crab ? 142f : 152f;
        float LegBulk => legBulk > 0f ? legBulk : 1f;
        float LegFlatten => !Crab ? 0f : legFlatten > 0f ? legFlatten : legFlatten < 0f ? 0f : 0.35f;
        float Posture => posture > 0f ? posture : Crab ? 1.15f : 1f;
        float StepFroude => stepFroude > 0f ? stepFroude : Crab ? 1.25f : 1.8f;
        float SpeedFroude => speedFroude > 0f ? speedFroude : Crab ? 0.1f : 0.08f;
        float ClawSize => clawSize == 0f ? 1f : clawSize;
        float CrusherRatio => crusherRatio > 0f ? crusherRatio : 1.3f;
        float Lumps => lumps == 0f ? 0.035f : Mathf.Max(0f, lumps);
        float ClawYaw => clawYawDeg > 0f ? clawYawDeg : 30f;
        float Contact => LegFamilies.ContactDeg(Family);
        bool Sideways => travel == ArthropodTravel.Sideways || (travel == ArthropodTravel.FormDefault && Crab);
        float LiftFraction => Crab ? 0.045f : 0.03f;
        // the longest walking leg (the gait's reference length)
        float Lmax => Crab ? LegLength * BodyWidth * bodyLength : LegLength * bodyLength;

        float Formula(int p) => LegFamilies.Formula(Family, legFormula, p, Pairs);

        // ------------------------------------------------------------ generate

        public void Generate(List<CreaturePart> parts, List<CreatureModule> modules)
        {
            if (Crab) GenerateCrab(parts); else GenerateSpider(parts);
            AddModules(modules);
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // A walking leg standing in its rest shape: its foot where the leg's rest pose puts it on the ground
        // (RestStance.GroundRadius for this coxa height), at the pair's rest yaw; the gait from GaitDefaults.
        ArthropodLeg WalkingLeg(string legId, int joint, Vector3 shoulder, SegmentDef[] segs, float yawDeg, float coxaHeight)
        {
            float r = RestStance.GroundRadius(segs, Contact, coxaHeight);
            float L = 0f; foreach (SegmentDef s in segs) L += s.length;
            float yaw = yawDeg * Mathf.Deg2Rad;
            return new ArthropodLeg
            {
                id = legId, at = new PartSocket(joint, 0, shoulder), mirror = true, role = LimbRole.Walk, gaitGroup = -1,
                restOffset = V(shoulder.x + Mathf.Sin(yaw) * r, 0f, shoulder.z + Mathf.Cos(yaw) * r),
                stepThreshold = GaitDefaults.Threshold * L, stepLift = LiftFraction * L, overshoot = GaitDefaults.Overshoot * L,
                stepDuration = GaitDefaults.StepDuration(StepFroude, Lmax),   // one step time for every pair (the longest leg's)
                // a crab walking sideways swings its front and hind coxae further round (its legs pull and push across the body)
                forwardSwingLimitDeg = Crab ? 35f : 18f, backSwingLimitDeg = Crab ? 35f : 18f, swingRetract = 0.12f, liftDelay = 0.1f, legSides = 10,
                contactAngleDeg = Contact, segments = segs,
                toeCount = Crab ? 0 : 2, footLength = Crab ? 0f : 0.0326f * Lmax,
            };
        }

        // A feeler (palp, cheliped) posed in its own rest shape: its tip where the rest pose puts it (gen_species
        // feeler_legs), so the IK holds it unbent.
        static ArthropodLeg Feeler(string fid, int joint, Vector3 sh, SegmentDef[] segs, float contact, float yawDeg, float drop)
        {
            Vector2 f = RestStance.Foot(segs, contact);
            float yaw = yawDeg * Mathf.Deg2Rad;
            return new ArthropodLeg
            {
                id = fid, at = new PartSocket(joint, 0, sh), mirror = true, role = LimbRole.Feeler, gaitGroup = 0,
                restOffset = V(sh.x + Mathf.Sin(yaw) * f.x, sh.y + f.y - drop, sh.z + Mathf.Cos(yaw) * f.x),
                contactAngleDeg = contact, segments = segs, legSides = 10,
            };
        }

        // ---- spider: the tarantula, ported and parameterised

        // tarantula joints (gen_species): half width, half height, clearance offset from joint 1's, bend, rigid.
        // 0..2 prosoma, 3 pedicel, 4..6 opisthosoma.
        static readonly float[,] kSpiderJoints =
        {
            { 0.05f, 0.03f, -0.003f, 10, 0 }, { 0.07f, 0.04f, 0f, 0, 1 }, { 0.065f, 0.035f, -0.002f, 0, 1 },
            { 0.03f, 0.025f, -0.015f, 15, 0 }, { 0.085f, 0.075f, 0.01f, 15, 0 }, { 0.095f, 0.085f, 0.015f, 0, 1 }, { 0.07f, 0.065f, 0.005f, 0, 1 },
        };

        void GenerateSpider(List<CreaturePart> parts)
        {
            float k = bodyLength / 0.41f, wf = BodyWidth / 0.34f, ab = Bulk;
            int n = Pairs;
            float spacing = 0.055f * k;
            float Lm = Lmax;
            float bulk = LegBulk * k / (Lm / 0.43f);   // radii follow the body (the tarantula: one radius set for all pairs)
            float shY = -0.02f * k;
            // the longest pair stands unbent: its natural foot drop is the coxa height
            float h = RestStance.CoxaHeight(LegFamilies.Segments(Family, Lm, bulk, Posture), Contact);
            float clr1 = h - shY;
            var joints = new JointDef[7];
            for (int i = 0; i < 7; i++)
            {
                bool abd = i >= 4;
                joints[i] = new JointDef
                {
                    halfWidth = kSpiderJoints[i, 0] * k * (abd ? ab : wf), halfHeight = kSpiderJoints[i, 1] * k * (abd ? ab : 1f),
                    clearance = clr1 + kSpiderJoints[i, 2] * k * (abd ? ab : 1f), bendLimitDeg = kSpiderJoints[i, 3], rigid = kSpiderJoints[i, 4] > 0f,
                };
            }
            parts.Add(new SpineBody { id = "body", spacing = spacing, headCapLength = 0.03f * k, tailTipLength = 0.05f * k * ab, joints = joints, rigidHeadJoints = 3, ringSegments = 18, ringsPerSegment = 3 });

            // pairs along the prosoma (the tarantula: z +0.03 .. -0.30 body lengths), on its flank (0.75 x half width)
            for (int p = 0; p < n; p++)
            {
                float u = n == 1 ? 0.5f : p / (n - 1f);
                float z = Mathf.Lerp(0.03f, -0.30f, u) * bodyLength;
                int j = Mathf.Clamp(Mathf.RoundToInt(-z / spacing), 0, 2);
                float hw = 0.5f * (joints[Mathf.Clamp(Mathf.FloorToInt(-z / spacing), 0, 2)].halfWidth + joints[Mathf.Clamp(Mathf.CeilToInt(-z / spacing), 0, 2)].halfWidth);
                Vector3 sh = V(Mathf.Min(0.75f * hw, 0.06f * k * wf), shY, z + j * spacing);
                SegmentDef[] segs = LegFamilies.Segments(Family, Lm * Formula(p), bulk, Posture);
                parts.Add(WalkingLeg("leg" + p, j, sh, segs, Mathf.Lerp(FrontYaw, BackYaw, u), joints[j].clearance + sh.y));
            }

            // pedipalps: a short 5-segment leg posed in its rest shape (a feeler), chelicerae with fangs, the eye cluster
            if (ClawSize > 0f)
            {
                float c = k * ClawSize;
                var palp = new[]
                {
                    new SegmentDef { length = 0.02f * c, radiusStart = 0.016f * c, radiusEnd = 0.015f * c, restBendDeg = -20, minBendDeg = -50, maxBendDeg = 20 },
                    new SegmentDef { length = 0.05f * c, radiusStart = 0.014f * c, radiusEnd = 0.013f * c, restBendDeg = 40, minBendDeg = 0, maxBendDeg = 80, flexWeight = 0.3f, knuckle = 1.1f },
                    new SegmentDef { length = 0.02f * c, radiusStart = 0.013f * c, radiusEnd = 0.012f * c, restBendDeg = -50, minBendDeg = -100, maxBendDeg = 0, flexWeight = -1f, knuckle = 1.1f, bandStart = 0, bandEnd = 1 },
                    new SegmentDef { length = 0.04f * c, radiusStart = 0.012f * c, radiusEnd = 0.011f * c, restBendDeg = -20, minBendDeg = -70, maxBendDeg = 20, flexWeight = -0.5f },
                    new SegmentDef { length = 0.03f * c, radiusStart = 0.011f * c, radiusEnd = 0.009f * c, restBendDeg = 30, minBendDeg = -40, maxBendDeg = 90 },
                };
                parts.Add(Feeler("palp", 0, V(0.025f * k, -0.012f * k, 0.035f * k), palp, 30f, 18f, 0f));
            }
            parts.Add(new RigidCone { id = "chelicera", at = new PartSocket(0, 0, V(0.016f * k, -0.004f * k, 0.03f * k)), mirror = true, direction = V(0.05f, -0.45f, 1f),
                                      length = 0.045f * k, radiusStart = 0.017f * k, radiusEnd = 0.012f * k, curveDeg = 35f, fangLength = 0.028f * k });
            parts.Add(new Eyes
            {
                id = "eyes", at = new PartSocket(0, 0, V(0.008f * k, 0.031f * k, 0.02f * k)), radius = 0.004f * k,
                cluster = new[]
                {
                    new EyeSpotDef { offset = V(0.012f * k, 0.031f * k, 0.012f * k), radius = 0.003f * k },
                    new EyeSpotDef { offset = V(0.004f * k, 0.034f * k, 0.026f * k), radius = 0.0035f * k },
                    new EyeSpotDef { offset = V(0.014f * k, 0.03f * k, 0.023f * k), radius = 0.0025f * k },
                },
            });
        }

        // ---- crab: a carapace (RigidLoft) on a hidden rigid spine, legs on its margin

        // The carapace's geometry in joint 0's frame (scale 1): it spans z = +0.15 CL (front) .. -0.85 CL (back); its
        // centre line rides cy above the joints; t = 0 at the back.
        internal struct Shell
        {
            public CrabOutline o; public float CL, W, bulk, cy, zFront, zBack, spacing;
            public float HalfWidth(float t) => CrabShell.At(o, t, 1) * 0.5f * W;
            public float HalfHeight(float t) => CrabShell.At(o, t, 2) * bulk * 0.5f * W;
            public float Rise(float t) => CrabShell.At(o, t, 3) * bulk * 0.5f * W;
            // the underside's height (joint frame) at t, xf of the half width out: the belly's superellipse
            public float Under(float t, float xf)
            {
                float e = 2f / Mathf.Max(1f, CrabShell.At(o, t, 5));
                float c = Mathf.Pow(Mathf.Clamp01(xf), 1f / e), sn = Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
                return cy + Rise(t) - CrabShell.At(o, t, 6) * HalfHeight(t) * Mathf.Pow(sn, e);
            }
            public float Z(float t) => zBack + t * (zFront - zBack);
            public float T(float z) => (z - zBack) / (zFront - zBack);
        }

        internal Shell CrabShellOf()
        {
            float CL = bodyLength, W = BodyWidth * CL;
            var s = new Shell { o = outline, CL = CL, W = W, bulk = Bulk, zFront = 0.15f * CL, zBack = -0.85f * CL, spacing = 0.175f * CL };
            // the hidden spine rides in the middle of the shell's height (dome above, the flat belly below)
            s.cy = 0.5f * (CrabShell.At(outline, 0.62f, 6) - 1f) * s.HalfHeight(0.62f);
            return s;
        }

        void GenerateCrab(List<CreaturePart> parts)
        {
            Shell S = CrabShellOf();
            float CL = S.CL, W = S.W, HW = 0.5f * W, cy = S.cy, spacing = S.spacing;
            int n = Pairs;
            float Lm = Lmax;
            // radii share the leg length at the default leg (1.05 x the carapace width); a longer leg thickens only with
            // the square root of its extra length (or a long leg becomes a club), a shorter one thins likewise
            float bulk = LegBulk * Mathf.Sqrt(1.05f * W / Lm);
            Vector2 span = CrabShell.LegSpan(outline);
            var shoulders = new Vector3[n]; var attach = new int[n]; var segsOf = new SegmentDef[n][];
            int refPair = 0; float best = 0f;
            for (int p = 0; p < n; p++) if (Formula(p) > best) { best = Formula(p); refPair = p; }
            for (int p = 0; p < n; p++)
            {
                float u = n == 1 ? 0.5f : p / (n - 1f);
                float t = Mathf.Lerp(span.x, span.y, u);
                float z = S.Z(t);
                int j = Mathf.Clamp(Mathf.RoundToInt(-z / spacing), 0, 4);
                // under the margin: 0.86 of the half width out, on the shell's flat underside (the coxae on the sternum's edge)
                shoulders[p] = V(0.86f * S.HalfWidth(t), S.Under(t, 0.86f), z + j * spacing);
                attach[p] = j;
                segsOf[p] = LegFamilies.Segments(Family, Lm * Formula(p), bulk, Posture, LegFlatten);
            }
            // body height: the reference pair stands unbent (its coxa at its natural foot drop)
            float h = RestStance.CoxaHeight(segsOf[refPair], Contact) - shoulders[refPair].y;
            var joints = new JointDef[5];
            for (int i = 0; i < 5; i++)
            {
                float t = S.T(-i * spacing);
                joints[i] = new JointDef { halfWidth = 0.5f * S.HalfWidth(t), halfHeight = Mathf.Min(0.6f * S.HalfHeight(0.62f), 0.6f * S.HalfHeight(t)), clearance = h, bendLimitDeg = 6f, rigid = true };
            }
            parts.Add(new SpineBody { id = "body", spacing = spacing, headCapLength = 0.1f * CL, tailTipLength = 0.06f * CL, joints = joints, rigidHeadJoints = 4, ringSegments = 20, ringsPerSegment = 3 });

            int keysN = CrabShell.Keys(outline);
            var keys = new LoftKey[keysN];
            for (int i = 0; i < keysN; i++)
            {
                float t = CrabShell.Key(outline, i, 0);
                keys[i] = new LoftKey
                {
                    t = t, halfWidth = Mathf.Max(0.004f, CrabShell.Key(outline, i, 1) * HW), halfHeight = Mathf.Max(0.003f, CrabShell.Key(outline, i, 2) * S.bulk * HW),
                    rise = CrabShell.Key(outline, i, 3) * S.bulk * HW, squareTop = CrabShell.Key(outline, i, 4), squareBottom = CrabShell.Key(outline, i, 5),
                    belly = CrabShell.Key(outline, i, 6),
                };
            }
            const int cj = 2;
            var shell = new RigidLoft
            {
                id = "carapace", at = new PartSocket(cj, 0, V(0f, cy, S.zBack + cj * spacing)), direction = V(0f, 0f, 1f), length = S.zFront - S.zBack, keys = keys,
                curveDeg = 4f, around = 48, rings = 32, lumpHeight = Lumps * W * 0.5f, lumpSize = 0.15f * W, lumpSeed = 7, lumpSymmetric = true, lumpRidge = lumpRidge > 0f ? lumpRidge : 0.35f,
            };
            parts.Add(shell);
            // the belly rides clear of the ground (>= 0.15 of the dome's height): legs too short for the shell are an error
            float gap = h + LowestPoint(shell), dome = S.HalfHeight(0.62f);
            if (gap < kBellyGap * dome)
                throw new InvalidOperationException($"the legs hold the shell's belly {gap * 1000f:F1} mm off the ground (< {kBellyGap} of the dome's {dome * 1000f:F0} mm): lengthen legLength or lower posture");
            var walking = new ArthropodLeg[n];
            for (int p = 0; p < n; p++)
            {
                float u = n == 1 ? 0.5f : p / (n - 1f);
                walking[p] = WalkingLeg("leg" + p, attach[p], shoulders[p], segsOf[p], Mathf.Lerp(FrontYaw, BackYaw, u), h + shoulders[p].y);
                parts.Add(walking[p]);
            }

            // chelipeds: the stone crab's arm and claw, in carapace lengths, held in their own rest shape low in front of
            // the face with the claws turned across it; a crusher (right) and a cutter (left)
            var arms = new List<ArthropodLeg>();
            if (ClawSize > 0f)
            {
                float tA = CrabShell.ClawT(outline), zA = S.Z(tA);
                // on the underside of the front margin
                Vector3 sh = V(0.6f * S.HalfWidth(tA), S.Under(tA, 0.6f), zA);
                for (int side = -1; side <= 1; side += 2)
                {
                    bool crusher = side > 0;
                    float kk = CL * ClawSize * (crusher ? 1f : 1f / CrusherRatio);
                    float th = crusher ? 1f : 0.85f;
                    var segs = new[]
                    {
                        new SegmentDef { length = 0.105f * kk, radiusStart = 0.062f * kk, radiusEnd = 0.06f * kk, restBendDeg = -20, minBendDeg = -50, maxBendDeg = 20 },
                        new SegmentDef { length = 0.21f * kk, radiusStart = 0.066f * kk, radiusEnd = 0.07f * kk, restBendDeg = 15, minBendDeg = -20, maxBendDeg = 70, flexWeight = 0.4f, knuckle = 1.1f },
                        new SegmentDef { length = 0.174f * kk, radiusStart = 0.072f * kk, radiusEnd = 0.07f * kk, restBendDeg = -40, minBendDeg = -120, maxBendDeg = 0, flexWeight = -1f, knuckle = 1.1f },
                        new SegmentDef { length = 0.096f * kk, radiusStart = 0.06f * kk, radiusEnd = 0.058f * kk, restBendDeg = -10, minBendDeg = -60, maxBendDeg = 20, flexWeight = -0.3f },
                    };
                    ArthropodLeg arm = Feeler(crusher ? "crusher" : "cutter", 0, sh, segs, 0f, ClawYaw, -0.15f * h);
                    // the claw held clear of the ground: the arm's rest shape pitched up about its shoulder until the
                    // claw's tip is its palm's height up (the IK holds a pitched rest shape unbent)
                    float minY = -h + 0.6f * 0.218f * kk * th + 0.02f * CL;
                    if (arm.restOffset.y < minY)
                    {
                        Vector3 d = arm.restOffset - sh;
                        float horiz = new Vector2(d.x, d.z).magnitude, len = d.magnitude;
                        float want = Mathf.Asin(Mathf.Clamp((minY - sh.y) / Mathf.Max(1e-6f, len), -1f, 1f));
                        Vector2 hz = new Vector2(d.x, d.z) / Mathf.Max(1e-6f, horiz);
                        arm.restOffset = sh + new Vector3(hz.x, 0f, hz.y) * (len * Mathf.Cos(want)) + Vector3.up * (len * Mathf.Sin(want));
                    }
                    arm.mirror = false; arm.at.side = side; arm.kneeLiftDeg = 10f; arm.legSides = 12;
                    parts.Add(arm);
                    arms.Add(arm);
                    parts.Add(new Chela
                    {
                        id = crusher ? "crusherClaw" : "cutterClaw", leg = arm.id, palmLength = 0.48f * kk, palmHeight = 0.218f * kk * th, palmWidth = 0.157f * kk * th,
                        wrist = 0.55f, square = 2.2f, fingerLength = 0.283f * kk, fingerHeight = 0.109f * kk * th, fingerWidth = 0.85f, dactylScale = 1.06f,
                        openDeg = 8f, curveDeg = 20f, pitchDeg = 6f, rollDeg = 0f, yawDeg = 90f, tipRound = 0.35f, teeth = crusher ? 3 : 6,
                        toothSize = (crusher ? 0.048f : 0.025f) * kk, molar = crusher ? 2f : 1f, tipFraction = 0.72f, seed = crusher ? 1 : 2, around = 14,
                    });
                }
            }
            // eye stalks at the front margin (their own bones: they fold into their orbits in a tuck), eyes on the tips
            float tE = CrabShell.EyeT(outline), zE = S.Z(tE);
            Vector3 stalkBase = V(0.3f * S.HalfWidth(tE), cy + 0.2f * S.HalfHeight(tE), zE);
            Vector3 dir = V(0.45f, 0.5f, 0.75f);
            float sl = 0.085f * CL;
            parts.Add(new EyeStalk
            {
                id = "stalk", at = new PartSocket(0, 0, stalkBase), mirror = true, direction = dir, tuckDirection = V(-0.1f, -0.8f, -0.6f),
                length = sl, radiusStart = 0.024f * CL, radiusEnd = 0.019f * CL, carriesEye = true, layer = 1,
            });
            float eyeR = 0.034f * CL;
            parts.Add(new Eyes { id = "eyes", at = new PartSocket(0, 0, stalkBase + dir.normalized * (sl + 0.004f)), radius = eyeR });

            if (tuck)
                parts.Add(CrabFold.Make("tuck", this, S, shell, joints, walking, arms, tuckScareSeconds));
        }

        const float kBellyGap = 0.15f;

        // The loft's lowest point (its joint frame, y), lumps included.
        internal static float LowestPoint(RigidLoft shell)
        {
            float lowest = float.PositiveInfinity;
            for (int k = 0; k <= 40; k++)
            {
                RigidLoft.Section q = shell.SectionAt(k / 40f);
                for (int a = 0; a < 48; a++) lowest = Mathf.Min(lowest, shell.Lumped(q, 2f * Mathf.PI * a / 48f).y);
            }
            return lowest;
        }

        // ------------------------------------------------------------ default modules (one look recipe per form)

        void AddModules(List<CreatureModule> modules)
        {
            bool Has<T>() { foreach (CreatureModule m in modules) if (m is T) return true; return false; }
            if (!Has<Walk>())
                modules.Add(new Walk
                {
                    speed = GaitDefaults.Speed(SpeedFroude, Lmax), turnRateDeg = Crab ? 100f : 70f, accel = Crab ? 1.5f : 1f, turnSlowdown = 0.5f,
                    gaitSwayDeg = Crab ? 0f : 8f, gaitRollDeg = Crab ? 2f : 6f, subSteps = Crab ? 2 : 1, pattern = GaitPattern.Tetrapod,
                    bob = new GaitBobDef { amplitude = 0.01f * bodyLength, hz = Crab ? 2f : 1.2f, damping = 0.7f },
                    travel = Sideways ? TravelMode.Sideways : TravelMode.Forward,
                });
            if (!Has<Wander>()) modules.Add(new Wander());
            if (!Has<Skin>())
                modules.Add(Crab
                    ? new Skin { dorsal = new Color(0.40f, 0.16f, 0.12f), flank = new Color(0.48f, 0.24f, 0.17f), belly = new Color(0.75f, 0.62f, 0.48f), pattern = new Color(0.62f, 0.60f, 0.55f),
                                 patternType = CreaturePattern.Spots, patternScale = 30f, patternContrast = 0.45f, patternSoftness = 0.5f, spotScale = 0.3f, smoothness = 0.3f, rimStrength = 0.3f, subsurface = 0.05f,
                                 sizeJitter = 0.06f, hueJitter = 0.01f, valueJitter = 0.06f }
                    : new Skin { dorsal = new Color(0.05f, 0.044f, 0.04f), flank = new Color(0.055f, 0.048f, 0.043f), belly = new Color(0.065f, 0.055f, 0.048f), pattern = new Color(0.03f, 0.027f, 0.025f),
                                 smoothness = 0.22f, rimStrength = 0.9f, subsurface = 0.05f, sizeJitter = 0.05f, hueJitter = 0.006f, valueJitter = 0.06f });
            if (!Has<Chitin>())
                modules.Add(Crab ? new Chitin { bandColor = new Color(0.08f, 0.06f, 0.05f) }
                                 : new Chitin { bandColor = new Color(0.85f, 0.42f, 0.12f), sheen = 0.6f, sheenColor = new Color(0.55f, 0.5f, 0.45f) });
        }

        protected override void CopyArrays() { legFormula = (float[])legFormula?.Clone(); }

        // Every number in range: a compose error, never an exception (an out-of-range template throws inside Generate,
        // and compose reports that too).
        public override string Validate(in SpeciesCtx c)
        {
            if (!(bodyLength > 0f)) return $"{id}: bodyLength must be > 0";
            if (legPairs < 0 || legPairs > 6) return $"{id}: legPairs must be 0 (4) .. 6";
            if (legFormula != null && legFormula.Length > 0)
            {
                if (legFormula.Length != Pairs) return $"{id}: legFormula needs one value per pair ({Pairs}), or none";
                foreach (float f in legFormula) if (!(f > 0f)) return $"{id}: legFormula values must be > 0";
            }
            if (posture != 0f && (posture < 0.6f || posture > 1.4f)) return $"{id}: posture must be 0 (the form's) or 0.6..1.4";
            if (bodyWidth < 0f || bodyBulk < 0f || legLength < 0f || legBulk < 0f || stepFroude < 0f || speedFroude < 0f || crusherRatio < 0f || tuckScareSeconds < 0f)
                return $"{id}: bodyWidth, bodyBulk, legLength, legBulk, stepFroude, speedFroude, crusherRatio, tuckScareSeconds must be >= 0";
            if (frontYawDeg < 0f || frontYawDeg > 180f || backYawDeg < 0f || backYawDeg > 180f || clawYawDeg < 0f || clawYawDeg > 90f)
                return $"{id}: frontYawDeg, backYawDeg 0..180, clawYawDeg 0..90";
            if (tuck && !Crab) return $"{id}: tuck is a crab option";
            return null;
        }
    }
}
