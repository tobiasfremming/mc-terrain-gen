using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Arm tube skinning in real poses (swing, grip, wrap, strike) of the tree octopus and the swinger: the arm is
    // drawn from its display chain (TentacleChain.DisplayX: the particles with every joint and segment kept
    // drawable) with the curve skin (OctopusArmSkin, the shader's arm curve).
    public class OctopusArmSkinTests
    {
        const float Dt = 1f / 60f;

        internal struct Sweep
        {
            public int frames, badFrames, worst, worstAt, total; public float maxBend, maxBendBad; public int jointsOver; public string first;
            public float mismatch, displayMismatch, displayOffsetMax; public double displayOffsetSum; public int displayOffsetN, displayOffsetFar;
        }

        // A pose scenario: species, size, seed; ticked `seconds` x sqrt(s), sampled every 10 frames.
        internal static Sweep Measure(CephalopodSpec sp, string scenario, float s, uint seed, float seconds)
        {
            var body = default(OctopusBody);
            OctopusController c;
            float limit = sp.arm.BendLimitDeg;
            switch (scenario)
            {
                case "bars":   // two-arm hand-over-hand along bars toward a prey, then feeding
                {
                    c = OctopusSwingerTests.Rig(sp, OctopusSwingTests.Bars(s, 0.9f, 8), new Vector3(0.48f, 2.06f, 0.02f), s, seed, out body);
                    var brain = new OctopusBrain(seed); c.Brain = brain;
                    c.AnchorNearby();
                    brain.Prey.Add(new OctopusBrainTests.TestPrey { Pos = new Vector3(2.61f, 2.5f, 0.1f) * s, Radius = 0.03f * s, Half = 0.04f * s });
                    break;
                }
                case "grove":  // brachiation across four trees (grip, wrap, swing, transfer)
                {
                    float g = sp.arm.Length / 1.6f * s;
                    var grove = OctopusGroves.Row(4, g, 2.6f, (int)seed);
                    body = new OctopusBody(sp, new Vector3(0.6f, 1.6f, 0.3f) * g, Quaternion.identity, s);
                    c = new OctopusController(body, grove, null, seed) { Hints = new CapsuleHints(grove, 0.3f * g) };
                    c.AnchorNearby();
                    c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(3f * 2.6f, 2.4f, 0f) * g, urgency = 0.6f };
                    break;
                }
                default:       // "strike": hanging under a two-bar canopy, a prey below in reach
                {
                    var canopy = new AnalyticSurfaces().AddCapsule(new Vector3(-0.3f, 2.5f, -3f) * s, new Vector3(-0.3f, 2.5f, 3f) * s, 0.06f * s)
                                                       .AddCapsule(new Vector3(0.3f, 2.5f, -3f) * s, new Vector3(0.3f, 2.5f, 3f) * s, 0.06f * s)
                                                       .AddGround(0f);
                    body = new OctopusBody(sp, new Vector3(0f, 2.0f, 0f) * s, Quaternion.identity, s);
                    c = new OctopusController(body, canopy, null, seed);
                    var brain = new OctopusBrain(seed); c.Brain = brain;
                    c.AnchorNearby();
                    for (int f = 0; f < 120; f++) c.Tick(Dt);
                    float L = sp.arm.Length * s;
                    brain.Prey.Add(new OctopusBrainTests.TestPrey { Pos = body.HeadCentre + new Vector3(0.12f, -0.75f, 0.3f) * L });
                    break;
                }
            }
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            var world = new Matrix4x4[body.BoneCount]; var skin = new Matrix4x4[body.BoneCount];
            var w = new Sweep { first = "none" };
            int T = (int)(60 * seconds * body.TimeScale);
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                if (f % 10 != 9) continue;
                float mis = 0f;
                for (int a = 0; a < body.ArmCount; a++)
                    for (int k = 0; k < body.Arms[a].N - 1; k++)
                        mis = Mathf.Max(mis, Vector3.Angle(OctoMath.Rotate(body.Arms[a].Frames[k], Vector3.forward), body.Arms[a].X[k + 1] - body.Arms[a].X[k]));
                w.mismatch = Mathf.Max(w.mismatch, mis);
                body.WriteBoneWorld(world, 0);
                for (int a = 0; a < body.ArmCount; a++)
                {
                    TentacleChain ch = body.Arms[a];
                    for (int k = 0; k < ch.N; k++)
                    {
                        if (k < ch.N - 1) w.displayMismatch = Mathf.Max(w.displayMismatch, Vector3.Angle(OctoMath.Rotate(ch.DisplayFrames[k], Vector3.forward), ch.DisplayX[k + 1] - ch.DisplayX[k]));
                        float off = (ch.DisplayX[k] - ch.X[k]).magnitude / ch.Spacing;
                        w.displayOffsetMax = Mathf.Max(w.displayOffsetMax, off); w.displayOffsetSum += off; w.displayOffsetN++;
                        if (off > 0.5f) w.displayOffsetFar++;
                    }
                }
                for (int b = 0; b < world.Length; b++) skin[b] = OctoMath.MulAffine(world[b], bind[b]);
                OctopusMeshTests.CountInverted(d, skin, bind, out string fi, out int[] per);
                int inv = per[OctopusMeshBuilder.PartArm];
                float mb = MaxBend(body, out int over, limit);
                w.frames++; w.maxBend = Mathf.Max(w.maxBend, mb); w.jointsOver = Mathf.Max(w.jointsOver, over);
                w.total += inv;
                if (inv > 0) { w.badFrames++; w.maxBendBad = Mathf.Max(w.maxBendBad, mb); }
                if (inv > w.worst) { w.worst = inv; w.worstAt = f; w.first = fi + "\n" + Bends(body, c); }
            }
            return w;
        }

        internal static string Bends(OctopusBody body, OctopusController c)
        {
            var sb = new StringBuilder();
            for (int a = 0; a < body.ArmCount; a++)
            {
                TentacleChain ch = body.Arms[a];
                sb.Append($"   arm {a} {c.Board.Role[a],-6} tone {ch.Tone:F2}:");
                for (int k = 1; k < ch.N - 1; k++)
                    sb.Append($" {(ch.Pinned[k] ? "*" : "")}{Vector3.Angle(ch.X[k] - ch.X[k - 1], ch.X[k + 1] - ch.X[k]):F0}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // The largest angle between neighbouring arm segments (deg); `over`: joints past limit + 2 deg.
        internal static float MaxBend(OctopusBody body, out int over, float limit)
        {
            float m = 0f; over = 0;
            for (int a = 0; a < body.ArmCount; a++)
            {
                TentacleChain ch = body.Arms[a];
                for (int k = 1; k < ch.N - 1; k++)
                {
                    Vector3 u = ch.X[k] - ch.X[k - 1], v = ch.X[k + 1] - ch.X[k];
                    float ang = Vector3.Angle(u, v);
                    m = Mathf.Max(m, ang);
                    if (ang > limit + 2f) over++;
                }
            }
            return m;
        }

        // In real poses - two-arm hand-over-hand along bars and feeding, brachiation across a grove (grip, wrap,
        // swing, transfer), a strike from a canopy - of the tree octopus and the swinger, at two sizes and two
        // seeds, sampled every 10 frames: no arm tube triangle inverts (CPU skinning as the shader does it). RED
        // 2026-10-19: 20-288 per frame. The log reports the largest joint angle the simulated chain shows - it still
        // folds (a contact pass after the solve, tethers, eased pins on their way) - and how far the drawn arm keeps
        // from the particles. Measured 2026-10-19 over 72 such runs (seeds 1-6): 0 inverted triangles.
        [Test] public void ArmTubes_RealPoses_NoInvertedTris()
        {
            var log = new StringBuilder(); int worst = 0; string first = "none";
            var species = new (string name, CephalopodSpec spec)[] { ("TreeOctopus", CephalopodSpec.Default), ("Swinger", OctopusSwingerTests.Spec()) };
            foreach (var (name, spec) in species)
                foreach (string scen in new[] { "bars", "grove", "strike" })
                    foreach (float s in new[] { 1f, 1.6f })
                        foreach (uint seed in new uint[] { 1, 3 })
                        {
                            Sweep w = Measure(spec, scen, s, seed, scen == "strike" ? 8f : 20f);
                            if (w.worst > worst) { worst = w.worst; first = $"{name} {scen} s {s} seed {seed} frame {w.worstAt}: {w.first}"; }
                            log.AppendLine($"{name,-11} {scen,-6} s {s:F1} seed {seed}: inverted arm tris worst {w.worst,3} (frames {w.badFrames}/{w.frames}), simulated chain: max bend {w.maxBend:F0} deg, joints past the {spec.arm.BendLimitDeg:F0} deg limit {w.jointsOver}; drawn arm off the particles mean {w.displayOffsetSum / Mathf.Max(1, w.displayOffsetN):F3}, max {w.displayOffsetMax:F2} spacings");
                        }
            TestContext.Out.WriteLine(log.ToString());
            Assert.AreEqual(0, worst, "inverted arm triangles; worst " + first);
        }

        // Every arm frame points along its own segment after the tick (the controller's contact passes move arm
        // particles after the body took its frames, and the frames are re-taken after them; before, a frame pointed
        // up to 168 deg away from its segment), and every drawn bone along its display segment. The drawn arm keeps
        // to the particles: on average within 0.05 spacings, rarely (under 1.5 % of the particle samples) more than half
        // a spacing off - where the simulated chain is torn (measured 2026-10-19: mean 0.00-0.04, 0.9 % over half a
        // spacing, at most 4.5 spacings: a torn tail drawn at the arm's length).
        [Test] public void ArmBones_FollowTheirSegments_AfterContact()
        {
            var log = new StringBuilder(); float worst = 0f, worstDisplay = 0f; double offSum = 0; int offN = 0, far = 0;
            foreach (var (name, spec) in new (string, CephalopodSpec)[] { ("TreeOctopus", CephalopodSpec.Default), ("Swinger", OctopusSwingerTests.Spec()) })
                foreach (string scen in new[] { "bars", "grove", "strike" })
                {
                    Sweep w = Measure(spec, scen, 1f, 3, scen == "strike" ? 8f : 20f);
                    worst = Mathf.Max(worst, w.mismatch); worstDisplay = Mathf.Max(worstDisplay, w.displayMismatch);
                    offSum += w.displayOffsetSum; offN += w.displayOffsetN; far += w.displayOffsetFar;
                    log.AppendLine($"{name,-11} {scen,-6}: frame / segment angle at most {w.mismatch:F2} deg, drawn bone / display segment {w.displayMismatch:F2} deg; drawn arm off the particles mean {w.displayOffsetSum / Mathf.Max(1, w.displayOffsetN):F3}, max {w.displayOffsetMax:F2} spacings, {w.displayOffsetFar} particle samples over 0.5");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.Less(worst, 1f, "an arm frame does not follow its segment");
            Assert.Less(worstDisplay, 1f, "a drawn arm bone does not follow its display segment");
            Assert.Less(offSum / offN, 0.05, "the drawn arm strays from the particles");
            Assert.Less(far, 0.015 * offN, "the drawn arm is often far from the particles");
        }

        // The curve skin draws a tight wrap the linear blend turned inside out: arm 0 held (pinned) round a thin
        // branch as a helix at 78 deg per joint from its third joint - a wrap the display leaves as it is (pins move
        // only past PinDisplayConeDeg) - skins with no inverted triangle; the 2-bone linear blend of the same bones
        // (what a shader ignoring the flag draws) inverts some on the thicker octopus arm.
        [Test] public void CurveSkin_TightWrap_NoInvertedTris()
        {
            int linear = 0;
            foreach (var (name, spec) in new (string, CephalopodSpec)[] { ("TreeOctopus", CephalopodSpec.Default), ("Swinger", OctopusSwingerTests.Spec()) })
            {
                OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(spec);
                Matrix4x4[] bind = OctopusRestPose.BindPoses(spec);
                var body = OctopusRestPose.Body(spec);
                TentacleChain arm = body.Arms[0];
                OctopusMeshTests.PoseHelix(arm, 3, 78f * Mathf.Deg2Rad, 6f * Mathf.Deg2Rad, out float radius);
                for (int k = 3; k < arm.N; k++) arm.Pin(k, arm.X[k], Vector3.up);
                var world = new Matrix4x4[body.BoneCount]; var skin = new Matrix4x4[body.BoneCount];
                body.WriteBoneWorld(world, 0);
                float moved = 0f;
                for (int k = 3; k < arm.N; k++) moved = Mathf.Max(moved, (arm.DisplayX[k] - arm.X[k]).magnitude);   // the held run
                for (int b = 0; b < world.Length; b++) skin[b] = OctoMath.MulAffine(world[b], bind[b]);
                int curve = OctopusMeshTests.CountInverted(d, skin, bind, out string first, out int[] per) > 0 ? per[OctopusMeshBuilder.PartArm] : 0;
                OctopusMeshTests.CountInverted(d, skin, bind, out _, out int[] perLinear, true);
                TestContext.Out.WriteLine($"{name}: helix radius {radius:F3} m, display moved the held run {moved * 1000f:F2} mm; inverted arm triangles: curve {curve}, linear blend {perLinear[OctopusMeshBuilder.PartArm]}");
                Assert.Less(moved, 1e-4f, "the display moved a legal wrap");
                Assert.AreEqual(0, curve, "the curve skin inverts a tight wrap; " + first);
                linear += perLinear[OctopusMeshBuilder.PartArm];
            }
            Assert.Greater(linear, 0, "the wrap is not tight enough to bite (the thicker octopus arm's linear blend inverts)");
        }
    }
}
