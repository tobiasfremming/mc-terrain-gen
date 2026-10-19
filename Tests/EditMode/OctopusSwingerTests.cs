using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The two-armed tree swinger (design docs/superpowers/specs/2026-10-19-two-armed-swinger-design.md): the octopus
    // code path with two long arms that brachiate (gibbon: release only while the other arm holds) and short mouth
    // tentacles that only feed. Analytic surfaces; lengths x scale, times x sqrt(scale).
    public class OctopusSwingerTests
    {
        const float Dt = 1f / 60f;

        // The swinger's body plan at scale 1, as shipped (Config/Creatures/Swinger.asset): a small sac of a body, two
        // long ribbon arms (minHolds 1), five short mouth tentacles.
        internal static CephalopodSpec Spec()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Swinger");
            Assert.IsNotNull(sp, "Config/Creatures/Swinger.asset");
            return sp.cephalopod;
        }

        internal static OctopusController Rig(CephalopodSpec spec, AnalyticSurfaces surf, Vector3 at, float s, uint seed, out OctopusBody body)
        {
            body = new OctopusBody(spec, at * s, Quaternion.identity, s);
            return new OctopusController(body, surf, null, seed);
        }

        // Travel to goal for `seconds` x sqrt(s), counting the fewest holds seen while hanging (after the first hold).
        internal static int TravelMinHolds(OctopusController c, Vector3 goal, float seconds, out int transfers)
        {
            OctopusBody body = c.Body;
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = 0.6f };
            int T = (int)(60 * seconds * body.TimeScale), min = int.MaxValue;
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                if (!c.Board.Grounded) min = Mathf.Min(min, c.Board.AnchoredCount);
            }
            transfers = c.Coordinator.Swings;
            return min;
        }

        // ------------------------------------------------------------------ the species asset, size, GC

        // The shipped species (Config/Creatures/Swinger.asset) is the body plan the tests use: a cephalopod with two
        // arms that let go only while the other holds, 4-6 mouth tentacles, much smaller than the tree octopus.
        [Test] public void Swinger_Asset_ShippedValues()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Swinger"), oct = CreatureKitAssets.Load("TreeOctopus");
            Assert.IsNotNull(sp, "Config/Creatures/Swinger.asset");
            Assert.AreEqual(CreatureBodyPlan.Cephalopod, sp.bodyPlan);
            Assert.IsNull(sp.Validate());
            CephalopodSpec c = sp.cephalopod;
            Assert.AreEqual(2, c.ArmCount); Assert.AreEqual(1, c.MinHolds);
            Assert.That(c.MouthCount, Is.InRange(4, 6));
            Assert.LessOrEqual(c.mouth.Length, 0.3f * c.arm.Length + 1e-4f, "mouth tentacles are short");
            float bodySize = sp.scale * c.MantleLength, octSize = oct.scale * oct.cephalopod.MantleLength;
            TestContext.Out.WriteLine($"swinger mantle {bodySize:F2} m, arm {sp.scale * c.arm.Length:F2} m; octopus mantle {octSize:F2} m, arm {oct.scale * oct.cephalopod.arm.Length:F2} m");
            Assert.Less(bodySize, 0.4f * octSize, "the body is much smaller than the tree octopus's");
            Assert.Greater(sp.look.sizeJitter, 0f);
        }

        // An individual's size spread (species scale x 0.9 / 1.1): both brachiate bar to bar (never from nothing) and
        // eat the spider with a mouth tentacle; transfers per sqrt-scaled time agree within 2.
        [Test] public void Swinger_SizeVariation_0_9_1_1()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Swinger");
            var log = new StringBuilder(); bool bad = false; int t09 = 0, t11 = 0;
            foreach (float f in new[] { 0.9f, 1.1f })
            {
                float s = sp.scale * f;
                var surf = OctopusSwingTests.Bars(s, 0.9f, 8);
                OctopusController c = Rig(sp.cephalopod, surf, new Vector3(0.48f, 2.06f, 0.02f), s, 1, out OctopusBody body);
                c.AnchorNearby();
                for (int k = 0; k < 120; k++) c.Tick(Dt);
                int min = TravelMinHolds(c, new Vector3(0.9f * 7f, 2f, 0f) * s, 30f, out int transfers);
                if (f < 1f) t09 = transfers; else t11 = transfers;
                bool eat = FeedsOnBars(sp.cephalopod, s, out float eatenAt);
                bool ok = min >= 1 && transfers >= 2 && eat;
                if (!ok) bad = true;
                log.AppendLine($"x{f} (scale {s:F2}): fewest holds {min}, transfers {transfers}, eaten {(eat ? eatenAt.ToString("F1") + " s" : "no")}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "an individual size does not brachiate or feed (see the log)");
            Assert.LessOrEqual(Mathf.Abs(t09 - t11), 2, "the two sizes swing differently");
        }

        // The feeding scenario of Swinger_Feeds_WithMouthTentaclesOnly at scale s: eaten (by a mouth) within 45 s x sqrt(s).
        internal static bool FeedsOnBars(CephalopodSpec spec, float s, out float eatenAt)
        {
            var surf = OctopusSwingTests.Bars(s, 0.9f, 8);
            OctopusController c = Rig(spec, surf, new Vector3(0.48f, 2.06f, 0.02f), s, 3, out OctopusBody body);
            var brain = new OctopusBrain(3); c.Brain = brain;
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            var prey = new OctopusBrainTests.TestPrey { Pos = new Vector3(2.7f - 0.06f - 0.03f, 2.5f, 0.1f) * s, Radius = 0.03f * s, Half = 0.04f * s };
            brain.Prey.Add(prey);
            int eaten = 0; eatenAt = -1f;
            c.Events.Eaten += _ => eaten++;
            int T = (int)(60 * 45f * body.TimeScale);
            for (int f = 0; f < T && eaten == 0; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < body.ArmCount; a++) if (c.Agents[a].Mode == TentacleAgent.State.Strike) return false;
            }
            if (eaten > 0) eatenAt = c.Time;
            return eaten == 1 && c.Feeder.Grabs == 1;
        }

        // Zero GC per tick: hanging, swinging, perceiving, feeding (the counter's positive control first). The warm-up
        // runs through the body's first sleep (OctopusBody allocates its sleep snapshot once, then), then the prey
        // appears and the hunt is measured.
        [Test] public void Swinger_NoGc()
        {
            GcAllocs.AssertCounterWorks();
            CreatureSpecies sp = CreatureKitAssets.Load("Swinger");
            float s = sp.scale;
            var surf = OctopusSwingTests.Bars(s, 0.9f, 8);
            OctopusController c = Rig(sp.cephalopod, surf, new Vector3(0.48f, 2.06f, 0.02f), s, 3, out OctopusBody body);
            var brain = new OctopusBrain(3); c.Brain = brain;
            c.AnchorNearby();
            var prey = new OctopusBrainTests.TestPrey { Pos = new Vector3(2.7f - 0.06f - 0.03f, 2.5f, 0.1f) * s, Radius = 0.03f * s, Half = 0.04f * s };
            var bones = new Matrix4x4[body.BoneCount];
            bool slept = false;
            for (int f = 0; f < 60 * 30 && !slept; f++) { c.Tick(Dt); body.WriteBoneWorld(bones, 0); slept = body.Asleep; }
            Assert.IsTrue(slept, "the warm-up rest never slept");
            brain.Prey.Add(prey);
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 60 * 25; f++) { c.Tick(Dt); body.WriteBoneWorld(bones, 0); } });
            TestContext.Out.WriteLine($"allocations over 25 s: {allocs}; meals {brain.Meals}, mouth grabs {c.Feeder.Grabs}, swings {c.Coordinator.Swings}");
            Assert.AreEqual(0, allocs);
            Assert.Greater(c.Feeder.Grabs + c.Coordinator.Swings, 0, "the run exercised nothing");
        }

        // ------------------------------------------------------------------ the look

        // FNV-1a over every mesh array's exact bits (positions, normals, uv0-3, triangles, bone count).
        internal static ulong MeshHash(OctopusMeshBuilder.Data d)
        {
            ulong h = 14695981039346656037UL;
            void F(float f) { h = (h ^ (uint)System.BitConverter.SingleToInt32Bits(f)) * 1099511628211UL; }
            foreach (Vector3 v in d.pos) { F(v.x); F(v.y); F(v.z); }
            foreach (Vector3 v in d.nrm) { F(v.x); F(v.y); F(v.z); }
            foreach (Vector2 v in d.uv0) { F(v.x); F(v.y); }
            foreach (Vector2 v in d.uv1) { F(v.x); F(v.y); }
            foreach (Vector4 v in d.uv2) { F(v.x); F(v.y); F(v.z); F(v.w); }
            foreach (Vector4 v in d.uv3) { F(v.x); F(v.y); F(v.z); F(v.w); }
            foreach (int t in d.tris) h = (h ^ (uint)t) * 1099511628211UL;
            return (h ^ (uint)d.BoneCount) * 1099511628211UL;
        }

        // The look features are data that default to off: the tree octopus's mesh is byte-identical. Unity's Mono rounds
        // differently from the offline .NET harness, so each runtime has its own pin (as the goldens do).
        // Re-pinned deliberately 2026-10-19: the arm tubes carry the curve-skin flag in uv3 (OctopusArmSkin; positions,
        // normals and the other channels unchanged). The Unity value was pinned from the Editor.
#if UNITY_EDITOR
        internal const ulong kOctopusMeshHash = 0x4AA0186A87801DE3UL;   // Unity 6000.2.6f2 / Mono (pinned from the Editor 2026-10-19)
#else
        internal const ulong kOctopusMeshHash = 0xAB640B665C01ECDAUL;   // offline .NET
#endif
        [Test] public void Look_OctopusMeshUnchanged()
        {
            Assert.AreEqual(kOctopusMeshHash.ToString("X16"), MeshHash(OctopusMeshBuilder.BuildData(CephalopodSpec.Default)).ToString("X16"));
        }

        // Stranger than the octopus, clearly a relative: eyes on stalks (the eye stands eyeStalk off the head), a
        // pear-shaped sac, much wider low on the mantle than mid-way (the octopus's is only a little), a dark beak
        // cone at the oral pole inside the mouth ring, banded arms (look data the shader reads); the mesh winds
        // outward with no degenerate triangle, and in hanging, swinging and feeding poses no triangle of the body,
        // eyes, stalks, beak, siphon - or arm tube (since the display chain and curve skin, 2026-10-19; before, the
        // arms inverted 60-90 triangles at joints in these two-arm swing poses) - inverts.
        [Test] public void Look_StrangerFeatures_NoInvertedTris()
        {
            CephalopodSpec sp = Spec(), oct = CephalopodSpec.Default;
            Assert.Greater(sp.eyeStalk, 0f); Assert.Greater(sp.beakLength, 0f); Assert.Greater(sp.mantlePear, 0f);
            Assert.Greater(sp.look.ArmBands, 0f); Assert.AreEqual(0f, oct.look.ArmBands, "the octopus has no bands");
            OctopusShape.Eye(sp, 0, out Vector3 eye, out _);
            float off = OctopusShape.ShellDistance(sp, eye, out _);
            Assert.Greater(off, 0.7f * sp.eyeStalk, "the eye stands on its stalk");
            float lowS = OctopusShape.Profile(sp, 0.82f) / OctopusShape.Profile(sp, 0.5f), lowO = OctopusShape.Profile(oct, 0.82f) / OctopusShape.Profile(oct, 0.5f);
            TestContext.Out.WriteLine($"eye off the shell {off:F3} m (stalk {sp.eyeStalk}); sac low/mid radius {lowS:F2} (octopus {lowO:F2})");
            Assert.Greater(lowS, lowO + 0.3f, "a pear: much wider low on the sac than the octopus's mantle");

            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Vector3 pole = OctopusShape.Axis(sp, 0f);
            int beak = 0;
            for (int i = 0; i < d.pos.Count; i++)
                if (Mathf.RoundToInt(d.uv1[i].x) == OctopusMeshBuilder.PartSiphon && d.pos[i].y < pole.y - 0.5f * sp.beakLength) beak++;
            Assert.Greater(beak, 10, "a beak below the oral pole");
            for (int k = 0; k < d.tris.Count; k += 3)
            {
                int i0 = d.tris[k], i1 = d.tris[k + 1], i2 = d.tris[k + 2];
                Vector3 fn = Vector3.Cross(d.pos[i1] - d.pos[i0], d.pos[i2] - d.pos[i0]);
                Assert.Greater(fn.magnitude, 1e-11f, $"degenerate triangle {k / 3}");
                Assert.Greater(Vector3.Dot(fn, d.nrm[i0] + d.nrm[i1] + d.nrm[i2]), 0f, $"triangle {k / 3} (part {d.uv1[i0].x}) winds inward");
            }
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            var surf = OctopusSwingTests.Bars(1f, 0.9f, 8);
            OctopusController c = Rig(sp, surf, new Vector3(0.48f, 2.06f, 0.02f), 1f, 3, out OctopusBody body);
            var brain = new OctopusBrain(3); c.Brain = brain;
            c.AnchorNearby();
            brain.Prey.Add(new OctopusBrainTests.TestPrey { Pos = new Vector3(2.61f, 2.5f, 0.1f), Radius = 0.03f, Half = 0.04f });
            var world = new Matrix4x4[body.BoneCount]; var skin = new Matrix4x4[body.BoneCount];
            int worst = 0, worstArm = 0; string first = null, firstArm = null;
            for (int f = 0; f < 60 * 20; f++)
            {
                c.Tick(Dt);
                if (f % 20 != 0) continue;
                body.WriteBoneWorld(world, 0);
                for (int b = 0; b < world.Length; b++) skin[b] = OctoMath.MulAffine(world[b], bind[b]);
                OctopusMeshTests.CountInverted(d, skin, bind, out string fi, out int[] perPart);
                int arm = perPart[OctopusMeshBuilder.PartArm], rest = 0;
                for (int p = 0; p < perPart.Length; p++) if (p != OctopusMeshBuilder.PartArm) rest += perPart[p];
                if (rest > worst) { worst = rest; first = fi; }
                if (arm > worstArm) { worstArm = arm; firstArm = fi; }
            }
            TestContext.Out.WriteLine($"worst inverted outside the arms {worst} {first}; arms {worstArm} {firstArm}; meals {brain.Meals}");
            Assert.AreEqual(0, worst, first);
            Assert.AreEqual(0, worstArm, firstArm);
        }

        // ------------------------------------------------------------------ the lab

        // Its own grove: the roster hands EVERY grove species (not only the first) to an OctopusLab of its own, in
        // roster order, and the shipped roster lists the swinger.
        [Test] public void Lab_Roster_EveryGroveSpeciesGetsAGrove()
        {
            var r = ScriptableObject.CreateInstance<CreatureLabRoster>();
            foreach (string n in new[] { "DesertMonitor", "TreeOctopus", "Swinger" })
                r.entries.Add(new CreatureLabRoster.Entry { species = CreatureKitAssets.LoadAsset(n) });
            r.SplitGroves(out List<CreatureLabRoster.Entry> enclosures, out List<CreatureSpecies> groves);
            Assert.AreEqual(1, enclosures.Count);
            Assert.AreEqual(2, groves.Count);
            Assert.AreEqual("TreeOctopus", groves[0].name); Assert.AreEqual("Swinger", groves[1].name);
            r.Split(out _, out CreatureSpecies first);
            Assert.AreEqual("TreeOctopus", first.name, "the single-grove split is unchanged");

            var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureLabRoster>(CreatureLabRoster.AssetPath);
            bool listed = false;
            foreach (CreatureLabRoster.Entry e in shipped.entries) if (e.species != null && e.species.name == "Swinger") listed = true;
            Assert.IsTrue(listed, "the swinger is in the lab roster");
        }

        // ------------------------------------------------------------------ mouth tentacles

        // The mouth tentacles are a second chain set: their bones follow the 6 soft bones (arms, soft, mouths,
        // eyes, siphon), the octopus (no mouths) keeps its 201 bones, the rest pose skins rigidly, every mouth bone
        // carries skin, and the chains ride along with the body as it hangs and swings.
        [Test] public void Swinger_Mouth_BonesAndMesh()
        {
            CephalopodSpec sp = Spec();
            Assert.AreEqual(201, OctopusBody.BoneCountFor(CephalopodSpec.Default), "octopus bone layout unchanged");
            var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity, 1f);
            Assert.AreEqual(5, body.MouthCount);
            Assert.AreEqual(8, body.Mouth[0].N);
            int expect = 2 * 24 + OctopusBody.SoftBoneCount + 5 * 8 + 3;
            Assert.AreEqual(expect, body.BoneCount);
            Assert.AreEqual(expect, OctopusBody.BoneCountFor(sp));
            Assert.AreEqual(2 * 24 + OctopusBody.SoftBoneCount, body.MouthBone(0, 0));
            Assert.AreEqual(body.MouthBone(4, 7) + 1, body.EyeBone(0));
            Assert.AreEqual(expect - 1, body.SiphonBone);

            // rest pose: bind x world = identity
            Matrix4x4[] world = OctopusRestPose.BoneWorld(sp), bind = OctopusRestPose.BindPoses(sp);
            for (int b = 0; b < world.Length; b++)
            {
                Matrix4x4 m = world[b] * bind[b];
                for (int i = 0; i < 16; i++) Assert.AreEqual(i % 5 == 0 ? 1f : 0f, m[i], 1e-4f, $"bone {b}");
            }
            // mesh: indices in range, every mouth bone skinned
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Assert.AreEqual(expect, d.BoneCount);
            var used = new bool[expect];
            for (int v = 0; v < d.uv2.Count; v++)
            {
                Vector4 a = d.uv2[v], e = d.uv3[v];
                foreach (float f in new[] { a.x, a.y, e.x, e.y }) Assert.That((int)f, Is.InRange(0, expect - 1), "skin index");
                if (a.z > 0f) used[(int)a.x] = true;
                if (a.w > 0f) used[(int)a.y] = true;
            }
            for (int m = 0; m < 5; m++) for (int k = 0; k < 8; k++) Assert.IsTrue(used[body.MouthBone(m, k)], $"mouth {m} bone {k} carries no skin");

            // they ride along: hanging from bars, the mouth roots stay at their rest distance from the head
            var surf = OctopusSwingTests.Bars(1f, 0.9f, 4);
            OctopusController c = Rig(sp, surf, new Vector3(0.48f, 2.06f, 0.02f), 1f, 1, out OctopusBody hb);
            c.AnchorNearby();
            float rest = (hb.Mouth[0].X[0] - hb.HeadCentre).magnitude;
            float worst = 0f;
            for (int f = 0; f < 300; f++)
            {
                c.Tick(Dt);
                for (int m = 0; m < 5; m++) worst = Mathf.Max(worst, Mathf.Abs((hb.Mouth[m].X[0] - hb.HeadCentre).magnitude - rest));
                for (int m = 0; m < 5; m++) for (int k = 0; k < hb.Mouth[m].N; k++) Assert.IsFalse(float.IsNaN(hb.Mouth[m].X[k].x), "NaN");
            }
            TestContext.Out.WriteLine($"mouth root rest distance {rest:F3} m, worst deviation {worst:F3} m; mouth tip spread {(hb.Mouth[0].X[7] - hb.Mouth[2].X[7]).magnitude:F3} m");
            Assert.Less(worst, 0.3f * rest + 0.01f, "the mouth roots do not follow the body");
        }

        // ------------------------------------------------------------------ feeding

        // A small spider on the side of a bar three bars away: the swinger sees it, swings close, creeps the last
        // bit, grabs it with a MOUTH tentacle (never an arm: no arm strikes, the holds never drop to zero) and eats
        // it (the Eaten event). At 1x and 1.5x.
        [Test] public void Swinger_Feeds_WithMouthTentaclesOnly()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 1.5f })
            {
                var surf = OctopusSwingTests.Bars(s, 0.9f, 8);
                OctopusController c = Rig(Spec(), surf, new Vector3(0.48f, 2.06f, 0.02f), s, 3, out OctopusBody body);
                var brain = new OctopusBrain(3); c.Brain = brain;
                Assert.IsNotNull(c.Feeder, "a body with mouth tentacles feeds with them");
                c.AnchorNearby();
                for (int f = 0; f < 120; f++) c.Tick(Dt);
                var prey = new OctopusBrainTests.TestPrey { Pos = new Vector3(2.7f - 0.06f - 0.03f, 2.5f, 0.1f) * s, Radius = 0.03f * s, Half = 0.04f * s };
                brain.Prey.Add(prey);
                int eaten = 0, grabbed = 0, armStrikes = 0, min = int.MaxValue; float eatenAt = -1f;
                c.Events.Eaten += _ => eaten++; c.Events.Grabbed += _ => grabbed++;
                int T = (int)(60 * 45f * body.TimeScale);
                for (int f = 0; f < T && eatenAt < 0f; f++)
                {
                    c.Tick(Dt);
                    if (!c.Board.Grounded) min = Mathf.Min(min, c.Board.AnchoredCount);
                    for (int a = 0; a < body.ArmCount; a++)
                        if (c.Agents[a].Mode == TentacleAgent.State.Strike || c.Agents[a].Mode == TentacleAgent.State.Hold || c.Board.Role[a] == ArmRole.Strike) armStrikes++;
                    if (eaten > 0) eatenAt = c.Time;
                }
                bool ok = eaten == 1 && grabbed == 1 && c.Feeder.Grabs == 1 && armStrikes == 0 && min >= 1;
                if (!ok) bad = true;
                log.AppendLine($"scale {s}: sightings {brain.Sightings}, brain {brain.Mode}, grabbed {grabbed} (mouth grabs {c.Feeder.Grabs}), eaten {eaten} at {eatenAt:F1} s, arm strike frames {armStrikes}, fewest holds {min}, head {body.HeadCentre / s}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "the swinger does not catch and eat the spider with its mouth tentacles (see the log)");
        }

        // ------------------------------------------------------------------ the two-arm grip

        // Gibbon brachiation over bars an arm's length apart: it lets one arm go only while the other holds - the
        // hold count never drops to zero while hanging - and it does swing from bar to bar.
        [Test] public void Swinger_TwoArmGrip_NeverZeroHolds()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 1.5f })
                for (uint seed = 1; seed <= 2; seed++)
                {
                    var surf = OctopusSwingTests.Bars(s, 0.9f, 8);
                    OctopusController c = Rig(Spec(), surf, new Vector3(0.45f + 0.03f * seed, 2.06f, 0.02f), s, seed, out OctopusBody body);
                    Assert.AreEqual(2, c.AnchorNearby(), "both arms take hold at spawn");
                    for (int f = 0; f < 120; f++) c.Tick(Dt);
                    int min = TravelMinHolds(c, new Vector3(0.9f * 7f, 2f, 0f) * s, 40f, out int transfers);
                    float dx = body.HeadCentre.x / s;
                    bool ok = min >= 1 && transfers >= 2;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s} seed {seed}: fewest holds {min}, transfers {transfers}, dx {dx:F2}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "the swinger dropped every hold or never swung (see the log)");
        }

        // The octopus test grove (OctopusGroves.Row) sized to the swinger's arm (its 1.2 m arm is 0.75 of the
        // octopus's 1.6 m): grove scale g = 0.75 s.
        internal const float GroveFit = 0.75f;

        internal static OctopusController InGrove(CephalopodSpec spec, float s, int layout, out OctopusBody body, out AnalyticSurfaces grove)
        {
            float g = GroveFit * s;
            grove = OctopusGroves.Row(4, g, 2.6f, layout);
            body = new OctopusBody(spec, new Vector3(0.6f, 1.6f, 0.3f) * g, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, (uint)layout) { Hints = new CapsuleHints(grove, 0.3f * g) };
            Assert.GreaterOrEqual(c.AnchorNearby(), 1, "no hold at spawn");
            for (int f = 0; f < 240; f++) c.Tick(Dt);
            return c;
        }

        // Purposeful brachiation through a grove: given a goal in the far tree of four, it plans a route over the
        // branch hints and swings hand over hand to it - at 1x and 1.5x, at least 2 of 3 layouts arrive within
        // 40 s x sqrt(s), never hanging from nothing.
        [Test] public void Swinger_Brachiates_AcrossGrove()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 1.5f })
            {
                int arrived = 0;
                for (int layout = 1; layout <= 3; layout++)
                {
                    OctopusController c = InGrove(Spec(), s, layout, out OctopusBody body, out _);
                    float g = GroveFit * s;
                    Vector3 goal = new Vector3(3f * 2.6f, 2.4f, 0f) * g;
                    c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = 0.6f };
                    int T = (int)(60 * 40f * body.TimeScale), min = int.MaxValue; float at = -1f;
                    for (int f = 0; f < T && at < 0f; f++)
                    {
                        c.Tick(Dt);
                        if (!c.Board.Grounded) min = Mathf.Min(min, c.Board.AnchoredCount);
                        if (c.Coordinator.SwingArrived) at = f * Dt / body.TimeScale;
                    }
                    if (at >= 0f) arrived++;
                    if (min < 1) bad = true;
                    log.AppendLine($"scale {s} layout {layout}: arrived {(at >= 0f ? at.ToString("F1") + " s/ts" : "never")}, swings {c.Coordinator.Swings} (branch {c.Coordinator.BranchSwings}), fewest holds {min}, x {body.HeadCentre.x / g:F2} of {goal.x / g:F2}");
                }
                if (arrived < 2) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "the swinger does not brachiate across the grove (see the log)");
        }
    }
}
