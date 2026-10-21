using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // FleshWing (Apex redesign §1 Stance / Wings, §5.2): the soft, thick folding wing it walks on. The fixture: the
    // FlowBody fixture's body (CreatureFlowBodyTests.Body, keel off: 24 joints at 0.1 m, the chest and its hump at
    // joint ~7), a mirrored wing arm (Leg2Bone: a short humerus 0.2, a long forearm 0.5, the wrist 0.22 m behind the
    // shoulder and 0.06 m out, pole (0.2, 1, -0.4)) carrying a mirrored 5-finger FleshWing on the part's own defaults
    // (FleshWing.DefaultSpec: fold 160-168 deg clamped by the rear rib, foldGap 3.5, groundLiftDeg 68, groundTallStance,
    // thumbPlant, fanRearRollsWithFan, flankPatches 6, the rear attach half way from the shoulder to the hips; bindFold
    // 0.3, spanMaxDeg 20), mirrored DrakeLeg hind legs at the hips, Eyes; Walk + Fly + FlightExcursion; numbers from the
    // middle of the scanned passing region (see the task report). Invariants: the sail is thick (not a plane) with a
    // round rim and closed (no open edge); folded on the ground each blade stands well above the back and behind the
    // shoulder, steep, broad, on its own side of the body; it stands on the drawn club; nothing inverts through the fold,
    // a launch and a landing (rear attach at the default and at the hips; the body's triangles too); the outer wing lags
    // the root and the trailing rays the leading one; no GC; Froude-similar chains. Each has a RED. FlyModule() turns on
    // all three stance-bound flight flags (airNeckStance, launchHeadOnNeck, touchdownAtStance) and the segment-wise launch
    // tail (launchTailBySegment: a launch from standing or a circling walk no longer folds the tail), so every test here
    // flies with them; with them the Apex's own body traits - the steep raised neck and the keel - fly, fold and land without a
    // body flip, the head no less steady (FlowBody_Winged_SteepNeckKeel_*). With the flags off a winged FlowBody still
    // shows the flips - even this plain body's neck front in the launch - which only the REDs cover.
    public class CreatureFleshWingTests
    {
        internal const float kDt = 1f / 60f;
        internal const int kShoulder = 7, kHips = 11;

        internal static int RearJoint => kShoulder + Mathf.RoundToInt(0.6f * (kHips - kShoulder));

        // the part's own default wing (FleshWing.DefaultSpec): the fixture runs on the part's defaults
        internal static WingSpec Spec() => FleshWing.DefaultSpec();

        internal static FleshWing Wing() => new FleshWing { id = "wing", at = new PartSocket(kShoulder, 0, Vector3.zero), mirror = true, arm = "arm" };

        internal static Leg2Bone Arm() => new Leg2Bone
        {
            id = "arm", at = new PartSocket(kShoulder, 0, new Vector3(0.1f, 0.02f, 0f)), mirror = true,
            restOffset = new Vector3(0.06f, 0f, -0.22f), upperLength = 0.2f, lowerLength = 0.5f, upperRadius = 0.04f, lowerRadius = 0.03f,
            pole = new Vector3(0.2f, 1f, -0.4f), stepThreshold = 0.14f, stepDuration = 0.3f, stepLift = 0.06f,
        };

        internal static DrakeLeg Hind() => new DrakeLeg
        {
            id = "hind", at = new PartSocket(kHips, 0, new Vector3(0.08f, -0.05f, 0f)), mirror = true,
            restOffset = new Vector3(0.18f, 0f, 0f), upperLength = 0.24f, lowerLength = 0.26f, upperRadius = 0.034f, lowerRadius = 0.02f,
            pole = new Vector3(0.3f, 0.2f, 0.9f), ankleHeight = 0.14f, footLength = 0.12f, hallux = true,
            stepThreshold = 0.16f, stepDuration = 0.25f, stepLift = 0.06f,
        };

        internal const float kAirNeckStance = 0.55f;

        internal static Fly FlyModule()
        {
            var f = new Fly();
            FlightSpec s = f.spec;
            s.cruiseSpeed = 3f; s.minSpeed = 2f; s.maxSpeed = 6f; s.kGrip = 0.4f; s.vGripMin = 2.2f; s.kLift = 0.75f;
            s.strokeRateMin = 2.5f; s.strokeRateMax = 6f; s.amplitudeMin = 0.7f; s.glideDemand = 0.1f;
            s.airUndulationDeg = 0f; s.airUndulationHz = 0f; s.tailSag = 0f; s.headStabilise = 0.3f;
            s.launchSpeed = 2.5f; s.flareDistance = 2.2f; s.minAltitude = 1.2f; s.touchdownTogether = true; s.airLevel = 0.8f;
            // the body is bound at its stance (FlowBody.restAtStance): the air neck keeps a share of the stance's bends (in
            // the steep-neck scan's flip-free 0.2 - 0.9, where the head bob is least - see the task-8b report), the launching
            // head turns with its neck, the touchdown starts the walking body at its stance
            s.airNeckStance = kAirNeckStance; s.launchHeadOnNeck = true; s.touchdownAtStance = true;
            // (task 9b) the launch hands the trunk and tail to the air chain segment by segment: a tail curled from a
            // circling walk no longer folds back on itself (Launch_From*_TailNeverFolds)
            s.launchTailBySegment = true;
            return f;
        }

        // the fixture; edit: changes to the wing (both sides), arm: to the wing arm
        internal static CreatureSpecies Species(System.Action<FleshWing> edit = null, System.Action<Leg2Bone> arm = null, string name = "FleshWingFixture", FlowBody withBody = null, System.Action<FlightSpec> fly = null)
        {
            FleshWing w = Wing(); edit?.Invoke(w);
            Leg2Bone a = Arm(); arm?.Invoke(a);
            // the body: the FlowBody fixture (CreatureFlowBodyTests.Body) without its keel (the Apex's steep neck and keel:
            // FlowBody_Winged_SteepNeckKeel_NoBodyFlips)
            FlowBody body = withBody;
            if (body == null) { body = CreatureFlowBodyTests.Body(); body.profile[2].keel = 0f; }
            var parts = new List<CreaturePart> { body, a, Hind(), w, CreatureKitComposeTests.TwoEyes() };
            Fly flyModule = FlyModule(); fly?.Invoke(flyModule.spec);
            var mods = new CreatureModule[] { new Walk { speed = 0.45f, turnRateDeg = 60f, accel = 1.5f, turnSlowdown = 0.5f }, flyModule, new FlightExcursion() };
            return FlowKit.Build(name, 1f, parts, mods);
        }

        internal static FleshWing Part(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FleshWing f) return f;
            return null;
        }

        internal static FleshWingRuntime Runtime(CreatureSim sim, int w = 0)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is FleshWingRuntime f && f.Wing == w) return f;
            return null;
        }

        // the bones of wing w: its flight bones and its own (the part's range), and its arm's leg bones
        internal static bool WingBone(CreatureSpecies sp, int w, int bone, bool armToo)
        {
            int first = sp.WingBone(w, 0), count = Part(sp).FlightBones + Part(sp).ExtraBones;
            if (bone >= first && bone < first + count) return true;
            if (!armToo) return false;
            int leg = sp.wings[w].leg;
            return bone >= sp.LegBone(leg, 0) && bone <= sp.LegBone(leg, 2);
        }

        // ------------------------------------------------------------ data

        [Test] public void FleshWing_Composes_FlightBonesThenItsOwn()
        {
            CreatureSpecies sp = Species();
            Assert.IsNull(sp.Validate());
            Assert.AreEqual(2, sp.WingCount);
            FleshWing p = Part(sp);
            Assert.AreEqual(5, p.spec.FingerCount);
            Assert.AreEqual(p.FlightBones + 5 * (p.spanSegments - 1) + 1, p.BoneCount(default), "the flight's, the span segments, the fan sector");
            CompiledSpecies c = sp.Compiled;
            for (int i = 0; i < c.Parts.Count; i++)
                if (c.Parts[i] is FleshWing fw)
                    Assert.AreEqual(c.Layout.Start(i), sp.WingBone(PteroWing.WingIndex(c.Parts, i), 0), "the flight's bones start the block");
            var sim = FlowKit.Sim(sp, 1f);
            Assert.IsNotNull(Runtime(sim, 0)); Assert.IsNotNull(Runtime(sim, 1));
        }

        [Test] public void FleshWing_Validate_RejectsBadData()
        {
            var ctx = new SpeciesCtx(new CreaturePart[] { Arm(), Wing() }, new CreatureModule[0], 24, false);
            FleshWing ok = Wing();
            Assert.IsNull(ok.Validate(ctx));
            FleshWing Bad(System.Action<FleshWing> f) { var b = (FleshWing)ok.Clone(); f(b); return b; }
            Assert.IsNotNull(Bad(b => b.rootThickness = 0f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.edgeRound = 0.1f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.spanSegments = 0).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.bindFold = 0.9f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.padRadius = 0f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.spec.scallop = 0.1f).Validate(ctx), "the Wing's look fields stay 0");
            Assert.IsNotNull(Bad(b => { b.spec.fingerLengths = new[] { 1f, 0.8f, 0.6f }; b.spec.fingerSpreadDeg = new[] { 0f, 20f, 40f }; b.spec.fingerFoldDeg = new[] { 150f, 158f, 166f }; }).Validate(ctx), "4-5 fingers");
        }

        [Test] public void FleshWing_ThicknessLaw()
        {
            FleshWing p = Wing();
            Assert.AreEqual(p.rootThickness, p.ThicknessAt(0f, 0f), 1e-6f);
            Assert.AreEqual(p.tipThickness, p.ThicknessAt(1f, 0f), 1e-6f);
            Assert.AreEqual(Mathf.Lerp(p.rootThickness, p.tipThickness, Mathf.Pow(0.5f, 0.8f)) * (1f - 0.25f * 0.7f), p.ThicknessAt(0.5f, 0.5f), 1e-6f);
            Assert.AreEqual(0.5f * p.tipThickness, p.ThicknessAt(1f, 1f), 1e-6f, "never below half the tip thickness");
        }

        // ------------------------------------------------------------ thick, not a plane

        // The spread wing (the rest mesh with bindFold 0, or the glide pose's bones), wing 0's sail in the girdle frame: at
        // mid-span (the middle of its lateral extent) the largest dorsal-to-ventral distance (twin skin vertices: one uv0,
        // opposite normals) >= kThick x the local chord (the section's fore-aft extent); every rim ring's radius (half the
        // span of its semicircle) > kRim x tipThickness.
        internal const float kThick = 0.06f, kRim = 0.4f;

        internal static string ThickProblem(CreatureSpecies sp, out float ratio, out float rimMin)
        {
            ratio = 0f; rimMin = float.MaxValue;
            FleshWing p = Part(sp);
            var m = new CreaturePteroWingTests.Skinned(sp);
            // spread: the rest bones, the wing re-posed at fold 0 (a bindFold bind is part-folded)
            CreatureSim rest = CreatureRestPose.BindSim(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            Matrix4x4[] world = CreatureRestPose.BoneWorld(sp);
            if (p.bindFold > 0f)
            {
                var cw = new CreatureWings(sp, sp.wings, rest.Joints, rest.Rotations, 1f);
                cw.WriteBones(world, 0);
                var spreadSim = FlowKit.Sim(sp, 1f);
                Runtime(spreadSim, 0).WriteRestSegments(world, 0); Runtime(spreadSim, 1).WriteRestSegments(world, 0);
            }
            var P = new Vector3[m.V.Length];
            for (int i = 0; i < P.Length; i++) { Vector4 s = m.Uv2[i]; P[i] = s.z * (world[(int)s.x] * bind[(int)s.x]).MultiplyPoint3x4(m.V[i]) + s.w * (world[(int)s.y] * bind[(int)s.y]).MultiplyPoint3x4(m.V[i]); }
            int g = sp.legs[sp.wings[0].leg].attachJoint, side = sp.legs[sp.wings[0].leg].side;
            Vector3 o = rest.Joints[g], right = rest.Rotations[g] * Vector3.right * side, fwd = rest.Rotations[g] * Vector3.forward;
            // wing 0's sail vertices (not the arm's loft or the pad: those ride the leg bones)
            var sail = new List<int>();
            for (int i = 0; i < P.Length; i++) if (WingBone(sp, 0, (int)m.Uv2[i].x, false)) sail.Add(i);
            if (sail.Count == 0) return "no sail vertices";
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (int i in sail) { float x = Vector3.Dot(P[i] - o, right); lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            float mid = 0.5f * (lo + hi), band = 0.03f * (hi - lo);
            // twins: same uv0, opposite normals; the section's fore-aft extent
            var byUv = new Dictionary<Vector2, List<int>>();
            float fMin = float.MaxValue, fMax = float.MinValue, thick = 0f;
            foreach (int i in sail)
            {
                if (m.Uv0[i].y >= FleshWing.RimMark - 0.01f) continue;
                if (Mathf.Abs(Vector3.Dot(P[i] - o, right) - mid) > band) continue;
                float f = Vector3.Dot(P[i] - o, fwd);
                fMin = Mathf.Min(fMin, f); fMax = Mathf.Max(fMax, f);
                if (!byUv.TryGetValue(m.Uv0[i], out List<int> l)) byUv[m.Uv0[i]] = l = new List<int>();
                l.Add(i);
            }
            Vector3[] N = NormalsOf(sp);
            foreach (List<int> l in byUv.Values)
                for (int a = 0; a < l.Count; a++)
                    for (int b = a + 1; b < l.Count; b++)
                        if (Vector3.Dot(N[l[a]], N[l[b]]) < -0.5f) thick = Mathf.Max(thick, (P[l[a]] - P[l[b]]).magnitude);
            float chord = fMax - fMin;
            if (!(chord > 0f)) return "no mid-span section";
            ratio = thick / chord;
            // rims: each ring (one uv0 among the rim's vertices): half its largest extent
            var rings = new Dictionary<Vector2, List<int>>();
            foreach (int i in sail)
            {
                if (m.Uv0[i].y < FleshWing.RimMark - 0.01f) continue;
                if (!rings.TryGetValue(m.Uv0[i], out List<int> l)) rings[m.Uv0[i]] = l = new List<int>();
                l.Add(i);
            }
            if (rings.Count == 0) return "no rim";
            foreach (List<int> l in rings.Values)
            {
                if (l.Count < 3) continue;   // a cap's apex (where a rim closes onto the skins' meeting point), not a ring
                float d = 0f;
                foreach (int a in l) foreach (int b in l) d = Mathf.Max(d, (P[a] - P[b]).magnitude);
                rimMin = Mathf.Min(rimMin, 0.5f * d);
            }
            TestContext.Out.WriteLine($"mid-span: thickness {thick * 1000f:F1} mm over a chord of {chord:F3} m = {ratio:F3}; thinnest rim radius {rimMin * 1000f:F2} mm ({rimMin / p.tipThickness:F2} x tipThickness)");
            if (ratio < kThick) return $"at mid-span the sail is {thick * 1000f:F1} mm thick over a {chord:F3} m chord ({ratio:F3} < {kThick})";
            if (!(rimMin > kRim * p.tipThickness)) return $"a rim's radius is {rimMin * 1000f:F2} mm (<= {kRim} x tipThickness): a hard sheet edge";
            return null;
        }

        static Vector3[] NormalsOf(CreatureSpecies sp)
        {
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] n = mesh.normals;
            Object.DestroyImmediate(mesh);
            return n;
        }

        [Test] public void FleshWing_IsThick_NotAPlane()
        {
            string e = ThickProblem(Species(), out _, out _);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_FleshWing_Flat()
        {
            string e = ThickProblem(Species(w => { w.rootThickness = 0.0005f; w.tipThickness = 0.0005f; w.rootSwell = 0f; w.stripeDepth = 0f; }), out float r, out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e, "a flat sheet must fail the thickness test");
        }
    
        // ------------------------------------------------------------ the folded blades

        static Vector3 Ahead(CreatureSim sim, float s) => sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s);

        // the trunk's highest dorsal point (FlowBody vertices on joints from just ahead of the shoulder back; the raised
        // neck and head are not the back), skinned
        static float HumpY(CreatureSpecies sp, CreaturePteroWingTests.Skinned m)
        {
            float y = float.MinValue;
            for (int i = 0; i < m.P.Length; i++)
            {
                int b = (int)m.Uv2[i].x;
                if (m.Part(i) == PartMeshEmitter.PartBody && b < sp.JointCount && b >= kShoulder - 1) y = Mathf.Max(y, m.P[i].y);
            }
            return y;
        }

        // Walking `seconds` (a weave), every frame: each blade's tip (wing w's highest skinned vertex) is above the trunk's
        // highest dorsal point by >= kAbove x that point's height above the ground, and, standing on its planted club, behind
        // the shoulder joint (along the girdle's forward levelled). Null or the first miss; worst: the smallest margin over kAbove x hump (m).
        internal const float kAbove = 0.5f;

        internal static string BladeProblem(CreatureSpecies sp, float seconds, out float worstMargin, out float worstBehind)
        {
            worstMargin = float.MaxValue; worstBehind = float.MaxValue;
            float s = sp.scale;
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, s);
            int n = Mathf.RoundToInt(seconds / kDt);
            string miss = null;
            for (int t = 0; t < n; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(40f * Mathf.Sin(t / 70f), Vector3.up) * fwd * (2f * s));
                if (t < 30) continue;   // the first steps settle the stance
                m.Pose(sim);
                float hump = HumpY(sp, m);
                // behind along the ground (the girdle's forward levelled: a chest-up girdle is pitched nose-up)
                Vector3 sh = sim.Joints[kShoulder], gf = Vector3.ProjectOnPlane(sim.Rotations[kShoulder] * Vector3.forward, Vector3.up).normalized;
                for (int w = 0; w < sp.WingCount; w++)
                {
                    int top = -1;
                    for (int i = 0; i < m.P.Length; i++) if (WingBone(sp, w, (int)m.Uv2[i].x, false) && (top < 0 || m.P[i].y > m.P[top].y)) top = i;
                    LegState ls = sim.Legs[sp.wings[w].leg];
                    bool planted = !ls.stepping || ls.stepT < 0f;   // stepping, the blade swings forward with its arm
                    float margin = m.P[top].y - hump - kAbove * hump, behind = -Vector3.Dot(m.P[top] - sh, gf);
                    if (margin < worstMargin) worstMargin = margin;
                    if (planted && behind < worstBehind) worstBehind = behind;
                    if (miss == null && margin < 0f) miss = $"t={sim.Time:F2} s: wing {w}'s tip {m.P[top].y:F3} m is not {kAbove} x the hump ({hump:F3} m) above it";
                    if (miss == null && planted && behind <= 0f) miss = $"t={sim.Time:F2} s: wing {w}'s tip is {-behind:F3} m ahead of the shoulder joint";
                }
            }
            TestContext.Out.WriteLine($"blades: smallest margin over {kAbove} x hump {worstMargin:F3} m, nearest behind the shoulder {worstBehind:F3} m");
            return miss;
        }

        [Test] public void FleshWing_FoldedBlade_StandsAboveTheBack()
        {
            string e = BladeProblem(Species(), 10f, out _, out _);
            Assert.IsNull(e, e);
        }

        // RED: the fingers folded only part way (about a right angle off the forearm) - the folded blades lie low
        [Test] public void Red_FleshWing_Low()
        {
            string e = BladeProblem(Species(w => w.spec.fingerFoldDeg = new[] { 96f, 100f, 104f, 108f, 112f }), 2f, out _, out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e, "fingers folded part way: the blades lie low");
        }

        // Walking straight (3 s, every 5th frame after the first steps): each folded blade standing on its planted club
        // (wrist -> its highest skinned vertex) stands >= kSteepDeg above the horizontal, and the blades rise side by side
        // close along the body: each tip on its own wing's side of the body's mid-plane by >= kOwnSide x s (signed: a tip
        // that crosses over the back fails), the two tips >= kTipGap x s apart across the body, and each tip within the
        // body's half width at the shoulder + kSide x the blade's length of the mid-plane (levelled girdle frame).
        // lateral: the problem is the side clause (not the steepness).
        internal const float kSteepDeg = 60f, kSide = 0.25f, kOwnSide = 0.05f, kTipGap = 0.15f;

        internal static string SteepProblem(CreatureSpecies sp, out float minElev, out float maxSide) => SteepProblem(sp, out minElev, out maxSide, out _, out _, out _);

        internal static string SteepProblem(CreatureSpecies sp, out float minElev, out float maxSide, out float minOwn, out float minGap, out bool lateral)
        {
            minElev = 90f; maxSide = float.MinValue; minOwn = float.MaxValue; minGap = float.MaxValue; lateral = false;
            float s = sp.scale;
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, s);
            string miss = null;
            var tips = new Vector3[sp.WingCount]; var have = new bool[sp.WingCount];
            for (int t = 0; t < 180; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 50f * s));
                if (t < 30 || t % 5 != 0) continue;
                m.Pose(sim);
                Vector3 gf = Vector3.ProjectOnPlane(sim.Rotations[kShoulder] * Vector3.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, gf);
                Vector3 mid = sim.Joints[kShoulder];
                for (int w = 0; w < sp.WingCount; w++)
                {
                    have[w] = false;
                    LegState ls = sim.Legs[sp.wings[w].leg];
                    if (ls.stepping && ls.stepT >= 0f) continue;   // the blade swings with its step
                    int top = -1;
                    for (int i = 0; i < m.P.Length; i++) if (WingBone(sp, w, (int)m.Uv2[i].x, false) && (top < 0 || m.P[i].y > m.P[top].y)) top = i;
                    tips[w] = m.P[top]; have[w] = true;
                    Vector3 wr = sim.Flight.Wings.State[w].wrist, ax = m.P[top] - wr;
                    float L = ax.magnitude, elev = Mathf.Asin(ax.y / L) * Mathf.Rad2Deg;
                    // the wing's own side: the side its wrist stands on (a mirrored arm's side sign)
                    float sideSign = Mathf.Sign(Vector3.Dot(wr - mid, right));
                    float own = sideSign * Vector3.Dot(m.P[top] - mid, right);
                    float side = Mathf.Abs(Vector3.Dot(m.P[top] - mid, right)) - (sp.joints[kShoulder].halfWidth * s + kSide * L);
                    minElev = Mathf.Min(minElev, elev); maxSide = Mathf.Max(maxSide, side); minOwn = Mathf.Min(minOwn, own);
                    if (miss == null && elev < kSteepDeg) miss = $"t={sim.Time:F2} s: wing {w}'s folded blade stands only {elev:F0} deg above the horizontal";
                    if (miss == null && own < kOwnSide * s) { miss = $"t={sim.Time:F2} s: wing {w}'s blade tip is {own:F3} m out on its own side (< {kOwnSide}): it crosses over the back"; lateral = true; }
                    if (miss == null && side > 0f) { miss = $"t={sim.Time:F2} s: wing {w}'s blade tip splays {side:F3} m beyond the body's side + {kSide} x its length"; lateral = true; }
                }
                if (sp.WingCount == 2 && have[0] && have[1])
                {
                    float gap = Mathf.Abs(Vector3.Dot(tips[0] - tips[1], right));
                    minGap = Mathf.Min(minGap, gap);
                    if (miss == null && gap < kTipGap * s) { miss = $"t={sim.Time:F2} s: the blade tips are only {gap:F3} m apart across the body (< {kTipGap})"; lateral = true; }
                }
            }
            TestContext.Out.WriteLine($"blades: least steep {minElev:F0} deg; tips out on their own side >= {minOwn:F3} m, apart >= {minGap:F3} m; beyond the side allowance by at most {maxSide:F3} m");
            return miss;
        }

        [Test] public void FleshWing_FoldedBlade_StandsSteep_CloseToTheBody()
        {
            string e = SteepProblem(Species(), out _, out _);
            Assert.IsNull(e, e);
        }

        // RED (lateral alone): the wrists closer in and the fan lifted steeply - the blades stand steep, but their tips lean in
        // over the back and cross
        [Test] public void Red_FleshWing_TipsCross()
        {
            string e = SteepProblem(Species(w => w.spec.groundLiftDeg = 60f, a => a.restOffset = new Vector3(0.14f, 0f, -0.22f)), out float el, out _, out _, out _, out bool lateral);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
            Assert.IsTrue(lateral, "the lateral clause fails (not the steepness)");
            Assert.GreaterOrEqual(el, kSteepDeg, "steep enough: only the side bites");
        }

        // RED: the earlier pose (a short forearm reaching forward, the fan lifted steeply about the rear rib) - the blades lean
        // back like a roof plank and splay out
        [Test] public void Red_FleshWing_RoofPlank()
        {
            string e = SteepProblem(Species(w => w.spec.groundLiftDeg = 72f, a => { a.upperLength = 0.2f; a.lowerLength = 0.44f; a.restOffset = new Vector3(0.24f, 0f, 0.12f); }), out _, out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // Folded on the ground (2 s of walking), wing 0's free sail (the fan on the rays' bones): its length from the wrist to
        // its highest point, its width = the largest extent of the slab at the middle (0.45 .. 0.55 of the length) across
        // that axis in the fan's plane (its thickness left out).
        internal const float kBroad = 0.12f;

        internal static string BroadProblem(CreatureSpecies sp, out float ratio)
        {
            ratio = 0f;
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, sp.scale);
            FlowKit.Run(sim, 2f, Ahead(sim, sp.scale));
            m.Pose(sim);
            int sector = sp.WingBone(0, WingBoneLayout.ArmRibOf(sp.wings[0])), patch0 = sp.WingBone(0, WingBoneLayout.HumerusPatchOf(sp.wings[0]));
            int patches = WingBoneLayout.FlankPatchCount(sp.wings[0]);
            var pts = new List<Vector3>();
            for (int i = 0; i < m.P.Length; i++)
            {
                int b = (int)m.Uv2[i].x;
                if (!WingBone(sp, 0, b, false) || b == sector || (b >= patch0 && b < patch0 + patches)) continue;
                pts.Add(m.P[i]);
            }
            Vector3 wrist = sim.Flight.Wings.State[0].wrist, top = wrist;
            foreach (Vector3 q in pts) if (q.y > top.y) top = q;
            Vector3 axis = top - wrist; float L = axis.magnitude; axis /= L;
            Vector3 fn = sim.Flight.Wings.FanNormal[0];
            fn = (fn - axis * Vector3.Dot(fn, axis)).normalized;
            var slab = new List<Vector3>();
            foreach (Vector3 q in pts)
            {
                float t = Vector3.Dot(q - wrist, axis) / L;
                if (t > 0.45f && t < 0.55f) slab.Add(q - axis * Vector3.Dot(q - wrist, axis) - fn * Vector3.Dot(q - wrist, fn));
            }
            float width = 0f;
            for (int a = 0; a < slab.Count; a++) for (int b = a + 1; b < slab.Count; b++) width = Mathf.Max(width, (slab[a] - slab[b]).magnitude);
            ratio = width / L;
            TestContext.Out.WriteLine($"folded blade: length {L:F3} m, width at the middle {width:F3} m = {ratio:F3} of the length");
            return ratio >= kBroad ? null : $"the folded blade is {width:F3} m wide at its middle over a length of {L:F3} m ({ratio:F3} < {kBroad}): a stick, not a leaf";
        }

        [Test] public void FleshWing_FoldedBlade_IsBroad()
        {
            string e = BroadProblem(Species(), out _);
            Assert.IsNull(e, e);
        }

        // RED: the fingers folded into one bundle (the fold angles 0.5 deg apart, no fold gap, the trailing rib beside them,
        // a thin sail) - a stick
        [Test] public void Red_FleshWing_Narrow()
        {
            string e = BroadProblem(Species(w => { w.spec.fingerMinGapDeg = 0.5f; w.spec.fingerFoldGapDeg = 0.5f; w.spec.fingerFoldDeg = new[] { 160f, 160.5f, 161f, 161.5f, 162f }; w.spec.trailingRib = 0.2f; w.rootThickness = 0.02f; w.armBulk = 0.02f; }), out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // ------------------------------------------------------------ standing on the pad

        // The kit's drawn-feet rule (KitChecks.DrawnFeetProblem: a wing-walking arm's drawn foot is CreatureWings.ThumbTip,
        // which the pad's far pole is) and the pad itself: over the same weaving walk, each planted wing arm's lowest pad
        // vertex (the pad rides the hand bone alone) drifts < kPadDrift x the arm's length from where it was at the plant,
        // and stays within kPadHeight x s of the ground.
        internal const float kPadDrift = 0.01f, kPadHeight = 0.012f;

        internal static string PadProblem(CreatureSpecies sp, out float drift, out float lowest)
        {
            drift = 0f; lowest = float.MaxValue;
            string feet = KitChecks.DrawnFeetProblem(sp, out _, out _);
            if (feet != null) return feet;
            float s = sp.scale;
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, s);
            var was = new bool[sp.WingCount]; var at0 = new Vector3[sp.WingCount];
            float high = float.MinValue;
            for (int t = 0; t < 900; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * s));
                m.Pose(sim);
                for (int w = 0; w < sp.WingCount; w++)
                {
                    int leg = sp.wings[w].leg, hand = sp.LegBone(leg, 2);
                    LegState ls = sim.Legs[leg];
                    bool planted = !ls.stepping || ls.stepT < 0f;
                    if (!planted) { was[w] = false; continue; }
                    int low = -1;
                    for (int i = 0; i < m.P.Length; i++) if ((int)m.Uv2[i].x == hand && m.Uv2[i].z > 0.999f && (low < 0 || m.P[i].y < m.P[low].y)) low = i;
                    if (low < 0) return "no pad vertices on the hand bone";
                    Vector3 q = m.P[low];
                    if (!was[w]) at0[w] = q;
                    was[w] = true;
                    float len = sp.legs[leg].TotalLength * s;
                    drift = Mathf.Max(drift, (q - at0[w]).magnitude / len);
                    lowest = Mathf.Min(lowest, q.y); high = Mathf.Max(high, q.y);
                }
            }
            TestContext.Out.WriteLine($"pad: lowest point drift max {drift * 100f:F2} % of the arm, height {lowest / s * 1000f:F1} .. {high / s * 1000f:F1} mm (scale 1)");
            if (drift >= kPadDrift) return $"the planted pad's lowest point slides {drift * 100f:F2} % of the arm's length";
            if (lowest < -kPadHeight * s || high > kPadHeight * s) return $"the planted pad's lowest point is {lowest / s * 1000f:F1} .. {high / s * 1000f:F1} mm off the ground";
            return null;
        }

        [Test] public void FleshWing_StandsOnThePad()
        {
            string e = PadProblem(Species(), out _, out _);
            Assert.IsNull(e, e);
        }

        // RED: without thumbPlant the drawn contact skates over a plant (the kit's drawn-feet rule bites)
        [Test] public void Red_FleshWing_PadSkates()
        {
            string e = PadProblem(Species(w => w.spec.thumbPlant = false), out _, out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // ------------------------------------------------------------ the fold sweep, a launch, a landing

        // A walk, a launch, the fold swept 0 -> 1 in 20 steps in the air (DiveFold, held 6 ticks each) and back, a landing
        // and a walk: no triangle turns over (CreaturePteroWingTests.Skinned's measure, the kit's) - the wings' and the body's.
        // Null or the first.
        internal static string SweepProblem(CreatureSpecies sp, out int frames)
        {
            frames = 0;
            float s = sp.scale, T = Mathf.Sqrt(s);
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, s);
            string worst = null; int f = 0;
            void Check(string when)
            {
                m.Pose(sim); f++;
                for (int k = 0; k < m.T.Length && worst == null; k += 3)
                {
                    int i0 = m.T[k], i1 = m.T[k + 1], i2 = m.T[k + 2], b = (int)m.Uv2[i0].x;
                    Vector3 rest = Vector3.Cross(m.V[i1] - m.V[i0], m.V[i2] - m.V[i0]);
                    Vector3 now = Vector3.Cross(m.P[i1] - m.P[i0], m.P[i2] - m.P[i0]);
                    if (Vector3.Dot(now, m.Skin(b).MultiplyVector(rest)) > 0f) continue;
                    bool wing = false;
                    for (int w = 0; w < sp.WingCount; w++) wing |= WingBone(sp, w, b, true);
                    string at = $"{when}: triangle {k / 3} part {m.Part(i0)} bones {m.Uv2[i0]}";
                    worst = wing ? at : "body " + at;
                }
            }
            for (int t = 0; t < 60; t++) { sim.Tick(kDt, Ahead(sim, s)); if (t % 3 == 0) Check($"walk t{t}"); }
            if (!sim.Flight.Launch(6f * s)) return "no launch";
            for (int t = 0; t < 240; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); if (t % 2 == 0) Check($"launch/air t{t} mode {sim.Mode}"); }
            for (int k = 0; k <= 40; k++)
            {
                float fold = k <= 20 ? k / 20f : (40 - k) / 20f;
                for (int t = 0; t < 6; t++)
                {
                    sim.Flight.DiveFold = fold;
                    sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f);
                    sim.Tick(kDt, sim.Joints[0]);
                    Check($"fold {fold:F2} mode {sim.Mode}");
                }
            }
            sim.Flight.DiveFold = 0f;
            for (int t = 0; t < 120; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); }
            sim.Flight.Land(sim.Flight.Model.Position + sim.Flight.Model.Forward * (8f * s) - Vector3.up * sim.Flight.Model.Altitude);
            for (int t = 0; t < 7200 && sim.Mode != FlightMode.Ground; t++) { sim.Tick(kDt, sim.Joints[0]); if (t % 2 == 0) Check($"landing t{t} mode {sim.Mode}"); }
            if (sim.Mode != FlightMode.Ground) return "it never landed";
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, s)); if (t % 3 == 0) Check($"after t{t}"); }
            frames = f;
            return worst;
        }

        [Test] public void FleshWing_FoldSweep_NoInvertedTriangles()
        {
            string e = SweepProblem(Species(), out int frames);
            TestContext.Out.WriteLine($"{frames} frames checked");
            Assert.IsNull(e, e);
            // every frame (BodyFlips): no body triangle turns over either
            SortedSet<string> flips = BodyFlips(Species());
            Assert.IsEmpty(flips, "body flips: " + string.Join(" | ", flips));
        }

        [Test] public void FleshWing_RearAttachAtHips_NoInvertedTriangles()
        {
            string e = SweepProblem(Species(w => w.spec.membraneRearJoint = kHips), out int frames);
            TestContext.Out.WriteLine($"{frames} frames checked");
            Assert.IsNull(e, e);
        }

        [Test] public void FleshWing_MaxBend()
        {
            string e = KitChecks.MaxBendProblem(Species());
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ the chains

        // In a beating climb, each signal is a bone's elevation over the girdle (asin of its +Z along the girdle's up): the
        // leading finger's span segments (root .. outer) and the chord rays behind it (fingers 1 .. F-1: their root segments).
        // A segment's lag: how long after the root's upper turn it peaks; a ray's: the shift that best correlates it with the
        // leading finger's root. The chain rule:
        // the outer segment lags the root by >= kTipLag of the beat; the rays lag the leading finger more and more toward the
        // trailing edge, the last by >= kTrailLag of the beat. Null or the problem; report: the lags.
        internal const float kTipLag = 0.05f, kTrailLag = 0.025f;

        internal static string ChainProblem(CreatureSpecies sp, float s, out string report)
        {
            report = "";
            float T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            FleshWingRuntime rt = Runtime(sim, 0);
            FleshWing p = Part(sp);
            int S = p.Segments, F = sp.wings[0].FingerCount;
            var bones = new Matrix4x4[sp.BoneCount];
            int n = S + F - 1;
            var sig = new List<float>[n];
            for (int i = 0; i < n; i++) sig[i] = new List<float>();
            sim.Flight.Launch(6f * s);
            int beats0 = -1, beats1 = 0; float time = 0f;
            for (int t = 0; t < 1500; t++)
            {
                sim.Flight.SetGoals(12f * s, sp.flight.cruiseSpeed * T, 0f);   // climbing: beating all the while
                sim.Tick(kDt, sim.Joints[0]);
                if (t < 300 || sim.Mode != FlightMode.Air) continue;
                if (beats0 < 0) beats0 = sim.Flight.Wings.State[0].beats;
                beats1 = sim.Flight.Wings.State[0].beats; time += kDt;
                sim.WriteBoneWorld(bones, 0);
                Vector3 up = sim.Rotations[sim.Flight.Girdle] * Vector3.up;
                float El(int b) => Mathf.Asin(Mathf.Clamp(Vector3.Dot(((Vector3)bones[b].GetColumn(2)).normalized, up), -1f, 1f)) * Mathf.Rad2Deg;
                for (int k = 0; k < S; k++) sig[k].Add(El(p.SegmentBone(sp, 0, rt.FirstExtraBone, 0, k)));
                for (int r = 1; r < F; r++) sig[S + r - 1].Add(El(sp.WingBone(0, r)));
            }
            int nb = beats1 - beats0;
            if (nb < 4) return $"only {nb} beats";
            float period = time / nb;
            int maxLag = Mathf.RoundToInt(0.5f * period / kDt);
            var lag = new float[n];
            for (int i = 1; i < n; i++) lag[i] = Lag(sig[0], sig[i], maxLag) * kDt / period;
            var sb = new System.Text.StringBuilder($"beat {period / T:F2} s (scale 1); lag behind the leading finger's root, x the beat: span segments");
            for (int k = 1; k < S; k++) sb.Append($" {lag[k]:F3}");
            sb.Append("; chord rays");
            for (int r = 1; r < F; r++) sb.Append($" {lag[S + r - 1]:F3}");
            report = sb.ToString();
            for (int k = 2; k < S; k++)
                if (lag[k] < lag[k - 1] - 1e-4f) return $"segment {k} lags less than segment {k - 1}: not a chain";
            if (lag[S - 1] < kTipLag) return $"the outer segment lags the root by only {lag[S - 1]:F3} of the beat (< {kTipLag})";
            for (int r = 2; r < F; r++)
                if (lag[S + r - 1] < lag[S + r - 2] - 1e-4f) return $"ray {r} lags less than ray {r - 1}: the trailing edge does not follow the leading edge";
            if (lag[S + F - 2] < kTrailLag) return $"the trailing ray lags the leading finger by only {lag[S + F - 2]:F3} of the beat (< {kTrailLag})";
            return null;
        }

        // the shift (ticks, 0..maxLag, to a fraction of a tick: a parabola through the best and its neighbours) by which y
        // best follows x (largest correlation of the mean-free series)
        static float Lag(List<float> x, List<float> y, int maxLag)
        {
            int N = Mathf.Min(x.Count, y.Count);
            double mx = 0, my = 0;
            for (int i = 0; i < N; i++) { mx += x[i]; my += y[i]; }
            mx /= N; my /= N;
            var cs = new double[maxLag + 1];
            int best = 0;
            for (int L = 0; L <= maxLag; L++)
            {
                double c = 0;
                for (int i = 0; i + L < N; i++) c += (x[i] - mx) * (y[i + L] - my);
                cs[L] = c / (N - L);
                if (cs[L] > cs[best]) best = L;
            }
            if (best == 0 || best == maxLag) return best;
            double a = cs[best - 1], b = cs[best], d = cs[best + 1], den = a - 2 * b + d;
            return den < 0 ? best + (float)(0.5 * (a - d) / den) : best;
        }

        [Test] public void FleshWing_Chain_TipLagsRoot()
        {
            CreatureSpecies sp = Species();
            string e = ChainProblem(sp, sp.scale, out string report);
            TestContext.Out.WriteLine(report);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_FleshWing_StiffChain()
        {
            CreatureSpecies sp = Species(w => { w.spanHz = 40f; w.chordHz = 40f; w.spanDamping = 1f; w.chordDamping = 1f; });
            string e = ChainProblem(sp, sp.scale, out string report);
            TestContext.Out.WriteLine($"RED: {e} ({report})");
            Assert.IsNotNull(e);
        }

        // The chains stay inside their limits and allocate nothing: beats, a folded dive and its pull-out, bones every tick.
        [Test] public void FleshWing_NoGC()
        {
            CreatureSpecies sp = Species();
            FleshWing p = Part(sp);
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            FleshWingRuntime rt = Runtime(sim, 0);
            var bones = new Matrix4x4[sp.BoneCount];
            float worstSpan = 0f;
            void Track() { for (int k = 0; k < rt.Fingers; k++) for (int g = 1; g < rt.Segments; g++) worstSpan = Mathf.Max(worstSpan, Mathf.Abs(rt.SpanBend(k, g))); }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 600; t++) { sim.Flight.SetGoals(8f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Track(); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 120; t++) { sim.Flight.DiveFold = t < 60 ? 0.9f : 0f; sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); sim.WriteBoneWorld(bones, 0); Track(); }
            });
            TestContext.Out.WriteLine($"largest span bend {worstSpan:F1} deg (max {p.spanMaxDeg})");
            Assert.AreEqual(0, allocs);
            Assert.LessOrEqual(worstSpan, p.spanMaxDeg + 1e-3f);
        }

        // Froude similarity: the 0.9x and 1.1x individuals beating the same climb in the same Froude time (ticks of dt x
        // sqrt k, goals x k) draw the same chain curves - the outer segment's bend and the trailing ray's angle - within
        // kFroude of their range.
        internal const float kFroude = 0.02f;

        static void ChainCurve(CreatureSpecies sp, float k, List<float> bend, List<float> ray)
        {
            float s = sp.scale * k, T = Mathf.Sqrt(s), dt = kDt * Mathf.Sqrt(k);
            var sim = FlowKit.Sim(sp, s);
            FleshWingRuntime rt = Runtime(sim, 0);
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 900; t++)
            {
                sim.Flight.SetGoals(12f * s, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(dt, sim.Joints[0]);
                if (t < 300) continue;
                bend.Add(rt.SpanBend(0, rt.Segments - 1)); ray.Add(rt.ChordAngle(rt.ChordRays));
            }
        }

        internal static string FroudeProblem(CreatureSpecies sp, out float worst)
        {
            worst = 0f;
            var ba = new List<float>(); var ra = new List<float>(); var bb = new List<float>(); var rb = new List<float>();
            ChainCurve(sp, 0.9f, ba, ra); ChainCurve(sp, 1.1f, bb, rb);
            float Range(List<float> l) { float lo = float.MaxValue, hi = float.MinValue; foreach (float x in l) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); } return hi - lo; }
            float rB = Mathf.Max(1e-3f, Range(ba)), rR = Mathf.Max(1e-3f, Range(ra));
            for (int i = 0; i < ba.Count && i < bb.Count; i++)
                worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(ba[i] - bb[i]) / rB, Mathf.Abs(ra[i] - rb[i]) / rR));
            TestContext.Out.WriteLine($"0.9x vs 1.1x: chain curves differ by at most {worst * 100f:F2} % of their range (outer bend range {rB:F2} deg, trailing ray range {rR:F2} deg)");
            return worst <= kFroude ? null : $"the chain curves of the 0.9x and 1.1x individuals differ by {worst * 100f:F2} % of their range in Froude time";
        }

        [Test] public void FleshWing_Froude()
        {
            string e = FroudeProblem(Species(), out _);
            Assert.IsNull(e, e);
        }

        // RED: the chains stepped in plain seconds (no 1/sqrt size on their frequencies) - the two sizes part
        [Test] public void Red_FleshWing_NotFroude()
        {
            FleshWingRuntime.IgnoreSizeForTest = true;
            try
            {
                string e = FroudeProblem(Species(), out _);
                TestContext.Out.WriteLine("RED: " + e);
                Assert.IsNotNull(e);
            }
            finally { FleshWingRuntime.IgnoreSizeForTest = false; }
        }

        // RED: no fold gaps (the folded rays close onto each other) - the sail's zones collapse and turn over
        [Test] public void Red_FleshWing_NoFoldGap()
        {
            string e = SweepProblem(Species(w => { w.spec.fingerMinGapDeg = 0.5f; w.spec.fingerFoldGapDeg = 0.3f; w.spec.fingerFoldDeg = new[] { 165f, 165.5f, 166f, 166.5f, 167f }; }), out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // The optional hook (hookLength > 0): drawn off the club's front on the hand bone, curling forward and down, it stays
        // above the contact - the planted club's lowest point is still the walked contact (the pad rule passes as it is).
        [Test] public void FleshWing_Hook_KeepsTheContact()
        {
            CreatureSpecies plain = Species(), hooked = Species(w => w.hookLength = 0.05f);
            Mesh a = FlowKit.Mesh(plain), b = FlowKit.Mesh(hooked);
            int na = a.vertexCount, nb = b.vertexCount;
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
            Assert.Greater(nb, na, "the hook is drawn");
            string e = PadProblem(hooked, out float drift, out float low);
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ closed: no hole in the wing

        // The rest mesh's triangles of wing 0 (first vertex on its bones or its arm's), their vertices welded by position: every
        // edge is shared by at least two triangles - the skins, rims, caps, the arm and the club close; no open boundary (a hole
        // that only another part's flesh would hide). open: the open edges found; at: the first.
        internal static int OpenEdges(CreatureSpecies sp, out string at)
        {
            at = null;
            var m = new CreaturePteroWingTests.Skinned(sp);
            var id = new Dictionary<(int, int, int), int>();
            int Weld(Vector3 p)
            {
                var k = (Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                if (!id.TryGetValue(k, out int v)) id[k] = v = id.Count;
                return v;
            }
            var edges = new Dictionary<long, int>();
            var where = new Dictionary<long, Vector3>();
            for (int t = 0; t < m.T.Length; t += 3)
            {
                if (!WingBone(sp, 0, (int)m.Uv2[m.T[t]].x, true)) continue;
                int[] w = { Weld(m.V[m.T[t]]), Weld(m.V[m.T[t + 1]]), Weld(m.V[m.T[t + 2]]) };
                for (int e = 0; e < 3; e++)
                {
                    int a = w[e], b = w[(e + 1) % 3];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    edges.TryGetValue(key, out int c); edges[key] = c + 1;
                    where[key] = m.V[m.T[t + e]];
                }
            }
            int open = 0;
            foreach (var kv in edges) if (kv.Value < 2) { open++; if (at == null) at = $"an edge at {where[kv.Key]} used by one triangle"; }
            TestContext.Out.WriteLine($"wing 0: {edges.Count} edges, {open} open");
            return open;
        }

        [Test] public void FleshWing_Closed_NoOpenEdge()
        {
            int open = OpenEdges(Species(), out string at);
            Assert.AreEqual(0, open, at);
        }

        // RED: the rims left out - the sheets' edges are holes
        [Test] public void Red_FleshWing_OpenWithoutRims()
        {
            FleshWing.NoRimsForTest = true;
            try
            {
                int open = OpenEdges(Species(), out string at);
                TestContext.Out.WriteLine($"RED: {open} open edges ({at})");
                Assert.Greater(open, 0);
            }
            finally { FleshWing.NoRimsForTest = false; }
        }

        // ------------------------------------------------------------ every bone skinned (task 10)

        // The kit's layout rule (CreatureKitTests 3): every wing bone carries skin. The sail rides the fan and arm sectors,
        // not the rear flank rib (wing bone FirstRib + 1) nor the tall stance's fan-side copy of it (FanRearRib); those carry
        // the wrist knuckles, which must stay hidden in the club: through a walk, a launch and the flight (every 5th frame),
        // each knuckle vertex lies inside the club (a ray from it crosses the club's closed surface an odd number of times).
        internal static string KnuckleProblem(CreatureSpecies sp, out int checkedVerts)
        {
            int nChecked = 0;
            string e = KnuckleRun(sp, ref nChecked);
            checkedVerts = nChecked;
            return e;
        }

        static string KnuckleRun(CreatureSpecies sp, ref int checkedOut)
        {
            int checkedVerts = 0;
            float s = sp.scale, T = Mathf.Sqrt(s);
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, s);
            WingSpec ws = sp.wings[0];
            int rear = sp.WingBone(0, WingBoneLayout.FirstRibOf(ws) + 1), fanRear = sp.WingBone(0, WingBoneLayout.FanRearRib(ws)), hand = sp.LegBone(ws.leg, 2);
            var knuckle = new List<int>(); var club = new List<int>();
            for (int i = 0; i < m.V.Length; i++) { int b = (int)m.Uv2[i].x; if (b == rear || b == fanRear) knuckle.Add(i); }
            for (int k = 0; k < m.T.Length; k += 3)
            {
                int i0 = m.T[k];
                if ((int)m.Uv2[i0].x == hand && m.Uv2[i0].z == 1f && m.Part(i0) == PartMeshEmitter.PartBody) club.Add(k);
            }
            if (knuckle.Count == 0) return "no knuckle vertices";
            string Check(string when)
            {
                m.Pose(sim);
                foreach (int i in knuckle)
                {
                    checkedVerts++;
                    int odd = 0;
                    foreach (Vector3 d in new[] { new Vector3(1f, 0.13f, 0.07f), new Vector3(-0.11f, 1f, 0.05f), new Vector3(0.09f, -0.17f, -1f) })
                    {
                        int n = 0;
                        foreach (int k in club) if (Crosses(m.P[i], d.normalized, m.P[m.T[k]], m.P[m.T[k + 1]], m.P[m.T[k + 2]])) n++;
                        if ((n & 1) == 1) odd++;
                    }
                    if (odd < 2) return $"{when} t={sim.Time:F2} s: knuckle vertex {i} is outside the club";
                }
                return null;
            }
            string miss = null;
            for (int t = 0; t < 120 && miss == null; t++) { sim.Tick(kDt, Ahead(sim, s)); if (t % 5 == 0) miss = Check("walk"); }
            if (miss == null) sim.Flight.Launch(6f * s);
            for (int t = 0; t < 240 && miss == null; t++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]);
                if (t % 5 == 0) miss = Check(sim.Mode.ToString());
            }
            checkedOut = checkedVerts;
            return miss;
        }

        static bool Crosses(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-14f) return false;
            float inv = 1f / det; Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, qv) * inv; if (v < 0f || u + v > 1f) return false;
            return Vector3.Dot(e2, qv) * inv > 0f;
        }

        [Test] public void FleshWing_EveryBoneSkinned_KnucklesHiddenInTheClub()
        {
            CreatureSpecies sp = Species();
            Assert.IsNull(KitChecks.LayoutProblem(sp));
            string e = KnuckleProblem(sp, out int n);
            TestContext.Out.WriteLine($"{n} knuckle vertex samples inside the club");
            Assert.IsNull(e, e);
        }

        // RED: the knuckles left out - two wing bones per wing carry no skin
        [Test] public void Red_FleshWing_NoKnuckles_BonesWithoutSkin()
        {
            FleshWing.NoKnucklesForTest = true;
            try
            {
                string e = KitChecks.LayoutProblem(Species());
                TestContext.Out.WriteLine("RED: " + e);
                StringAssert.Contains("wing 0 bone", e);
            }
            finally { FleshWing.NoKnucklesForTest = false; }
        }

        // RED: knuckles four times as large stick out of the club
        [Test] public void Red_FleshWing_BigKnuckles_ShowOutsideTheClub()
        {
            FleshWing.KnuckleScaleForTest = 4f;
            try
            {
                string e = KnuckleProblem(Species(), out _);
                TestContext.Out.WriteLine("RED: " + e);
                StringAssert.Contains("outside the club", e);
            }
            finally { FleshWing.KnuckleScaleForTest = 1f; }
        }

        // ------------------------------------------------------------ the Apex's body in flight (task 8b)

        // The Apex's own body traits - the steep raised neck (CrestBeakHead's) and the sharp keel - bound at their stance
        // (FlowBody.restAtStance) fly, fold and land without a body flip once the flight carries the stance-bound body
        // (FlyModule's airNeckStance, launchHeadOnNeck, touchdownAtStance). Without them (the REDs) three body flips came
        // back: the neck's front over the walking head in the launch, the keel's belly seam in the air (the air chain laid
        // the chest's bend 31-35 deg the other way from its bind's), and the chest kink and a neck segment tipped past
        // vertical at touchdown (the walking body hauled up from the hovering pose, its legs drooping it).

        // every body flip of the sweep (walk, launch and climb, fold 0 -> 1 -> 0, landing, the wing-walk after), every frame,
        // as "phase mode bones"
        internal static SortedSet<string> BodyFlips(CreatureSpecies sp)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = FlowKit.Sim(sp, s);
            var seen = new SortedSet<string>();
            void Check(string phase)
            {
                m.Pose(sim);
                for (int k = 0; k < m.T.Length; k += 3)
                {
                    int i0 = m.T[k], i1 = m.T[k + 1], i2 = m.T[k + 2], b = (int)m.Uv2[i0].x;
                    if (b >= sp.JointCount) continue;
                    Vector3 rest = Vector3.Cross(m.V[i1] - m.V[i0], m.V[i2] - m.V[i0]);
                    Vector3 now = Vector3.Cross(m.P[i1] - m.P[i0], m.P[i2] - m.P[i0]);
                    if (Vector3.Dot(now, m.Skin(b).MultiplyVector(rest)) > 0f) continue;
                    seen.Add($"{phase} {sim.Mode} joints {b}-{(int)m.Uv2[i0].y}");
                }
            }
            for (int t = 0; t < 60; t++) { sim.Tick(kDt, Ahead(sim, s)); Check("walk"); }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 240; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Check("air"); }
            for (int k = 0; k <= 40; k++)
            {
                float fold = k <= 20 ? k / 20f : (40 - k) / 20f;
                for (int t = 0; t < 6; t++) { sim.Flight.DiveFold = fold; sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Check("fold"); }
            }
            sim.Flight.DiveFold = 0f;
            for (int t = 0; t < 120; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); }
            sim.Flight.Land(sim.Flight.Model.Position + sim.Flight.Model.Forward * (8f * s) - Vector3.up * sim.Flight.Model.Altitude);
            for (int t = 0; t < 7200 && sim.Mode != FlightMode.Ground; t++) { sim.Tick(kDt, sim.Joints[0]); Check("landing"); }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, s)); Check("after"); }
            return seen;
        }

        internal static CreatureSpecies SteepNeckKeel(System.Action<FlightSpec> fly = null) =>
            Species(withBody: CreatureCrestBeakHeadTests.Neck(), name: "FleshWingSteepNeckKeel", fly: fly);

        [Test] public void FlowBody_Winged_SteepNeckKeel_NoBodyFlips()
        {
            CreatureSpecies sp = SteepNeckKeel();
            FlowBody body = null;
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FlowBody fb) body = fb;
            Assert.IsTrue(body.restAtStance && body.profile[2].keel > 0f, "the steep neck bound at its stance, with the keel");
            SortedSet<string> flips = BodyFlips(sp);
            Assert.IsEmpty(flips, "body flips: " + string.Join(" | ", flips));
            string e = SweepProblem(sp, out int frames);
            TestContext.Out.WriteLine($"{frames} frames checked, no body flip in any frame");
            Assert.IsNull(e, e);
        }

        // RED: the shipped air chain (airNeckStance 0) lays the raised neck flat - the keel's belly seam turns over in the air.
        [Test] public void Red_SteepNeckKeel_FlatAirNeck_KeelSeamTurns()
        {
            SortedSet<string> flips = BodyFlips(SteepNeckKeel(f => f.airNeckStance = 0f));
            TestContext.Out.WriteLine("flips: " + string.Join(" | ", flips));
            Assert.IsTrue(Has(flips, "fold Air joints 6-7"), "the keel seam at the chest turns over in the air fold");
        }

        // RED: the whole stance in the air (airNeckStance 1) bends the neck root past its bind's already deep bend - the back
        // under the hump turns over (the share's passing range is 0.2 - 0.9).
        [Test] public void Red_SteepNeckKeel_FullStanceAirNeck_BackTurns()
        {
            SortedSet<string> flips = BodyFlips(SteepNeckKeel(f => f.airNeckStance = 1f));
            TestContext.Out.WriteLine("flips: " + string.Join(" | ", flips));
            Assert.IsTrue(Has(flips, "air Launch joints 6-7") || Has(flips, "air Air joints 6-7"), "the neck root turns over in the air");
        }

        // RED: the shipped launch (launchHeadOnNeck off) keeps the head on the walking heading while the neck rises - the
        // fixture's own body (the one-time known neck-front flip; the steep neck with airNeckStance happens to rise less).
        [Test] public void Red_LevelLaunchHead_NeckFrontTurns()
        {
            SortedSet<string> flips = BodyFlips(Species(fly: f => f.launchHeadOnNeck = false));
            TestContext.Out.WriteLine("flips: " + string.Join(" | ", flips));
            Assert.IsTrue(Has(flips, "air Launch joints 0-1"), "the neck's front turns over in the launch");
        }

        // RED: the shipped touchdown (touchdownAtStance off) - the chest kinks and a neck segment tips over.
        [Test] public void Red_SteepNeckKeel_HoverTouchdown_ChestKinks()
        {
            SortedSet<string> flips = BodyFlips(SteepNeckKeel(f => f.touchdownAtStance = false));
            TestContext.Out.WriteLine("flips: " + string.Join(" | ", flips));
            Assert.IsTrue(Has(flips, "landing Touchdown joints 7-8") || Has(flips, "landing Touchdown joints 6-7"), "the chest kinks at touchdown");
        }

        // The head's and the girdle's vertical bob (peak to peak about a 1 s running mean) in 6 s of steady cruise after a
        // launch and 6 s of climb-out; the ratio head / girdle (< 1: the head steadier than the shoulders).
        internal static float BobRatio(CreatureSpecies sp, out float head, out float girdle)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            for (int t = 0; t < 30; t++) sim.Tick(kDt, Ahead(sim, s));
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 360; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); }
            int g = sim.Flight.Girdle, N = 360, W = Mathf.RoundToInt(0.5f / kDt);
            var hy = new float[N]; var gy = new float[N];
            for (int t = 0; t < N; t++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]);
                hy[t] = sim.Joints[0].y; gy[t] = sim.Joints[g].y;
            }
            float PP(float[] y)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = W; i < y.Length - W; i++)
                {
                    float m = 0f;
                    for (int j = -W; j <= W; j++) m += y[i + j];
                    m /= 2 * W + 1;
                    lo = Mathf.Min(lo, y[i] - m); hi = Mathf.Max(hi, y[i] - m);
                }
                return hi - lo;
            }
            head = PP(hy); girdle = PP(gy);
            return head / Mathf.Max(1e-6f, girdle);
        }

        // The stance-bound flight keeps the head at least as steady as the shipped air neck does (airNeckStance 0).
        [Test] public void FlowBody_Winged_SteepNeckKeel_HeadNoLessSteady()
        {
            float on = BobRatio(SteepNeckKeel(), out float h, out float g);
            float off = BobRatio(SteepNeckKeel(f => f.airNeckStance = 0f), out _, out _);
            TestContext.Out.WriteLine($"head / girdle bob: {on:F3} (head {h:F3} m, girdle {g:F3} m); airNeckStance 0: {off:F3}");
            Assert.Less(on, 1f, "the head steadier than the shoulders");
            Assert.LessOrEqual(on, off, "no less steady than the shipped air neck");
        }

        // RED: the whole stance in the air (airNeckStance 1) stiffens the neck - the head bobs more than with the shipped one.
        [Test] public void Red_SteepNeckKeel_FullStanceAirNeck_HeadBobs()
        {
            float on = BobRatio(SteepNeckKeel(f => f.airNeckStance = 1f), out _, out _);
            float off = BobRatio(SteepNeckKeel(f => f.airNeckStance = 0f), out _, out _);
            TestContext.Out.WriteLine($"head / girdle bob: {on:F3}; airNeckStance 0: {off:F3}");
            Assert.Greater(on, off, "the stiff neck bobs the head more");
        }

        // The largest pitch of any spine segment while the body is on the ground (Ground and Touchdown modes): a straight
        // walk, tight turns both ways, the sweep's launch, flight and the touchdown ease, the walk after. A segment past
        // vertical rolls its walking frame 180 deg (the up projected across a segment that has tipped over).
        internal static float MaxWalkPitch(CreatureSpecies sp, out string at)
        {
            float s = sp.scale, T = Mathf.Sqrt(s), worst = 0f; string where = null;
            var sim = FlowKit.Sim(sp, s);
            void Check(string when)
            {
                if (sim.Mode != FlightMode.Ground && sim.Mode != FlightMode.Touchdown) return;
                for (int i = 1; i < sp.JointCount; i++)
                {
                    Vector3 d = sim.Joints[i - 1] - sim.Joints[i];
                    float p = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
                    if (Mathf.Abs(p) > worst) { worst = Mathf.Abs(p); where = $"{when} {sim.Mode} segment {i - 1}-{i}"; }
                }
            }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, s)); Check("walk"); }
            foreach (float side in new[] { 1f, -1f })
                for (int t = 0; t < 240; t++)
                {
                    Vector3 fw = sim.Rotations[0] * Vector3.forward, rt = sim.Rotations[0] * Vector3.right;
                    sim.Tick(kDt, sim.Joints[0] + (0.3f * fw + side * rt) * s);
                    Check(side > 0f ? "turn right" : "turn left");
                }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 240; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); }
            sim.Flight.Land(sim.Flight.Model.Position + sim.Flight.Model.Forward * (8f * s) - Vector3.up * sim.Flight.Model.Altitude);
            for (int t = 0; t < 7200 && sim.Mode != FlightMode.Ground; t++) { sim.Tick(kDt, sim.Joints[0]); Check("landing"); }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, s)); Check("after"); }
            at = where;
            return worst;
        }

        internal const float kMaxWalkPitch = 85f;

        [Test] public void FlowBody_Winged_SteepNeckKeel_NoSegmentTipsOver()
        {
            float p = MaxWalkPitch(SteepNeckKeel(), out string at);
            TestContext.Out.WriteLine($"largest walking segment pitch {p:F1} deg ({at})");
            Assert.Less(p, kMaxWalkPitch, at);
        }

        // RED: a steeper neck (the head 0.25 m higher over the same chest) - a neck segment tips past vertical on the ground.
        [Test] public void Red_SteeperNeck_SegmentTipsOver()
        {
            FlowBody body = CreatureCrestBeakHeadTests.Neck();
            body.profile[0].clearance += 0.25f; body.profile[1].clearance += 0.2f;
            float p = MaxWalkPitch(Species(withBody: body, name: "FleshWingSteeperNeck"), out string at);
            TestContext.Out.WriteLine($"largest walking segment pitch {p:F1} deg ({at})");
            Assert.GreaterOrEqual(p, kMaxWalkPitch, "the steeper neck tips a segment over");
        }

        // ------------------------------------------------------------ the launch's tail (task 9b)

        // A launch out of 15 s of circling walk (fromStanding: then 10 s standing still): through the launch and the first
        // 0.5 s of the air (x sqrt s), every tail joint behind the hips bends at most its limit (+ 0.5 deg) and, at 10 Hz,
        // no triangle of the posed mesh inverts. The shipped launch (launchTailBySegment off) lerped the walking body's
        // positions toward the air chain's: the curled walking tail and the air tail trailing the vault lie far apart and
        // the blend folded the tail back on itself (bends up to 178 deg, hundreds of inverted tail triangles).
        internal static string LaunchTailProblem(CreatureSpecies sp, bool fromStanding, out float worstExcess, out int inverted)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            worstExcess = float.MinValue; inverted = 0;
            var sim = FlowKit.Sim(sp, s);
            int n = sp.JointCount;
            Vector3 c = sim.Joints[0];
            for (int k = 0; k < 900; k++)
            {
                float a = k * kDt * 0.8f;
                sim.Tick(kDt * T, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1.5f * s));
            }
            if (fromStanding) { Vector3 stand = sim.Joints[0]; for (int k = 0; k < 600; k++) sim.Tick(kDt * T, stand); }
            if (!sim.Flight.Launch(6f * s)) return "no launch";
            string first = null, inv = null;
            int air = 0;
            for (int k = 0; k < 600 && air < 30; k++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(kDt * T, sim.Joints[0]);
                if (sim.Mode != FlightMode.Launch) air++;
                Vector3[] J = sim.Joints;
                for (int j = kHips + 1; j < n - 1; j++)
                {
                    float ex = Vector3.Angle(J[j] - J[j - 1], J[j + 1] - J[j]) - sp.joints[j].bendLimitDeg;
                    if (ex > worstExcess) worstExcess = ex;
                    if (ex > 0.5f && first == null) first = $"tick {k} ({sim.Mode}): tail joint {j} bends {ex + sp.joints[j].bendLimitDeg:F1} deg > its limit {sp.joints[j].bendLimitDeg:F1}";
                }
                if (k % 6 == 5)
                {
                    int bad = FlowKit.InvertedTriangles(sp, sim, out string w);
                    if (bad > 0 && inv == null) inv = $"tick {k} ({sim.Mode}): {bad} inverted ({w})";
                    inverted += bad;
                }
            }
            if (first != null) return first;
            if (inverted > 0) return $"{inverted} inverted triangles; first {inv}";
            return null;
        }

        [Test] public void Launch_FromStanding_TailNeverFolds()
        {
            string e = LaunchTailProblem(Species(), true, out float ex, out int inv);
            TestContext.Out.WriteLine($"launch from standing (after a circling walk): worst tail bend - limit {ex:F2} deg, {inv} inverted triangles");
            Assert.IsNull(e, e);
        }

        [Test] public void Launch_FromCircling_TailNeverFolds()
        {
            string e = LaunchTailProblem(Species(), false, out float ex, out int inv);
            TestContext.Out.WriteLine($"launch out of a circling walk: worst tail bend - limit {ex:F2} deg, {inv} inverted triangles");
            Assert.IsNull(e, e);
        }

        // RED: the shipped launch (launchTailBySegment off) folds the tail back on itself.
        [Test] public void Red_Launch_FromStanding_PositionBlend_TailFolds()
        {
            string e = LaunchTailProblem(Species(fly: f => f.launchTailBySegment = false), true, out float ex, out int inv);
            TestContext.Out.WriteLine($"RED: {e}");
            Assert.IsNotNull(e);
            Assert.Greater(ex, 90f, "the tail folds back (bend far past its limit)");
        }

        [Test] public void Red_Launch_FromCircling_PositionBlend_TailFolds()
        {
            string e = LaunchTailProblem(Species(fly: f => f.launchTailBySegment = false), false, out float ex, out int inv);
            TestContext.Out.WriteLine($"RED: {e}");
            Assert.IsNotNull(e);
            Assert.Greater(ex, 90f, "the tail folds back (bend far past its limit)");
        }

        // RED: the segment blend without its bend cones - joints whose air weights differ turn by different shares and the
        // tail bends past its limits (no fold, but 16 deg over).
        [Test] public void Red_Launch_FromStanding_NoCones_TailOverBends()
        {
            CreatureFlight.NoLaunchTailConeForTest = true;
            try
            {
                string e = LaunchTailProblem(Species(), true, out float ex, out _);
                TestContext.Out.WriteLine($"RED: {e}");
                TestContext.Out.WriteLine($"worst tail bend - limit {ex:F2} deg");
                Assert.IsNotNull(e);
                Assert.Greater(ex, 10f, "the blend itself bends the tail well past its limits (not only the walking body's own 1.3 deg)");
            }
            finally { CreatureFlight.NoLaunchTailConeForTest = false; }
        }

        // RED: the segment blend with the walking frames on the tail - a rising tail segment swept through vertical rolls its
        // frame over and the tail's skin inverts.
        [Test] public void Red_Launch_FromStanding_WalkingFrames_TailSkinInverts()
        {
            CreatureFlight.WalkingLaunchTailFramesForTest = true;
            try
            {
                string e = LaunchTailProblem(Species(), true, out float ex, out int inv);
                TestContext.Out.WriteLine($"RED: {e}");
                Assert.IsNotNull(e);
                Assert.Greater(inv, 0, "the tail's skin inverts");
            }
            finally { CreatureFlight.WalkingLaunchTailFramesForTest = false; }
        }

        static bool Has(SortedSet<string> flips, string prefix)
        {
            foreach (string f in flips) if (f.StartsWith(prefix)) return true;
            return false;
        }
}
}
