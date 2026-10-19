using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The SkyDragon on the ground (user feedback 2026-10-19): four limbs - the wing arms, on which it wing-walks like
    // the winged serpent (a tall knuckle stance on the thumb claws, the folded fan lifted, the membrane along the
    // flank), and real hind legs (thigh, shin, a long clawed foot) that carry the hips.
    public class CreatureDragonGroundTests
    {
        const float kDt = 1f / 60f;

        // "The dragon should only have 4 limbs": two wing arms and two hind legs, nothing else walks.
        [Test] public void Dragon_HasFourLimbs_WingArmsAndHindLegs()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            Assert.AreEqual(4, sp.legs.Length, "four limbs");
            Assert.AreEqual(2, sp.WingCount, "two wings");
            var arm = new bool[sp.legs.Length];
            for (int w = 0; w < sp.WingCount; w++) arm[sp.wings[w].leg] = true;
            int hind = 0;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                if (arm[l]) continue;
                hind++;
                LegSpec g = sp.legs[l];
                Assert.Greater(g.attachJoint, sp.legs[sp.wings[0].leg].attachJoint, "the hind legs behind the wings");
            }
            Assert.AreEqual(2, hind, "two hind legs");
            // "wings should be where its arms currently are": the wing arms at joint 14 (the old fore legs), not 6
            Assert.AreEqual(14, sp.legs[sp.wings[0].leg].attachJoint, "the wings sit back on the body");
        }

        // "The legs are just stumps": the hind legs are long enough to carry the hips at their stance clearance with
        // a bent knee (reach 1.15..1.6 x the hip's height over the foot) and their foot is a real foot (>= 35 % of the
        // shin); the shin and thigh are slim (length >= 6 x radius). Standing and walking, both hind feet stay planted
        // on the ground and the hips ride at their clearance.
        [Test] public void HindLegs_RealProportions_CarryTheHips()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            var arm = new bool[sp.legs.Length];
            for (int w = 0; w < sp.WingCount; w++) arm[sp.wings[w].leg] = true;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                if (arm[l]) continue;
                LegSpec g = sp.legs[l];
                float hip = sp.joints[g.attachJoint].clearance + g.shoulderOffset.y;
                float reach = g.upperLength + g.lowerLength;
                TestContext.WriteLine($"leg {l}: thigh {g.upperLength:F3} shin {g.lowerLength:F3} foot {g.footLength:F3} (r {g.upperRadius:F3}/{g.lowerRadius:F3}); hip {hip:F3} m up, reach {reach / hip:F2} x");
                Assert.That(reach / hip, Is.InRange(1.15f, 1.6f), "long enough to stand on with a bent knee");
                Assert.GreaterOrEqual(g.footLength, 0.35f * g.lowerLength, "a real foot");
                Assert.GreaterOrEqual(g.upperLength, 6f * g.upperRadius, "a slim thigh");
                Assert.GreaterOrEqual(g.lowerLength, 6f * g.lowerRadius, "a slim shin");
                Assert.GreaterOrEqual(g.toeCount, 3, "clawed toes");
            }
            int hl = 0;
            while (arm[hl]) hl++;
            float s = sp.scale;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s);
            float worstFoot = 0f, hipLo = float.MaxValue, hipHi = float.MinValue;
            for (int t = 0; t < 600; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                if (t < 120) continue;
                for (int l = 0; l < sp.legs.Length; l++)
                {
                    if (arm[l] || sim.Legs[l].stepping) continue;
                    worstFoot = Mathf.Max(worstFoot, Mathf.Abs(sim.Legs[l].foot.y));
                }
                float y = sim.Joints[sp.legs[hl].attachJoint].y / s;
                hipLo = Mathf.Min(hipLo, y); hipHi = Mathf.Max(hipHi, y);
            }
            TestContext.WriteLine($"walking: planted hind feet within {worstFoot * 100f:F2} cm of the ground; hips at {hipLo:F3}..{hipHi:F3} x s");
            Assert.Less(worstFoot, 0.02f * s, "planted feet on the ground");
            Assert.Greater(hipLo, 0.8f * sp.joints[sp.legs[hl].attachJoint].clearance, "the hips stand tall");
        }

        // The winged serpent's WingWalk_FoldedWingsClearTheGround on the dragon: wing-walking straight, weaving and
        // circling at minimum radius at 0.9x, 1x and 1.1x, the folded fingers sweep up (the middle finger >= 15 deg
        // above the girdle's plane) and stay >= 2 cm x size above the ground; the membrane stays >= 2 cm x size above
        // it walking straight and never touches it (>= 2 mm x size outside the trunk's plan outline, >= 0 under the
        // trunk) in the hardest turns; the thumb claws stand on it. The hand (within the thumb's length of the foot
        // point) is exempt: it stands there.
        [TestCase(0.9f)] [TestCase(1f)] [TestCase(1.1f)]
        public void WingWalk_MembraneClearsTheGround(float sizeFactor)
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            float size = sp.scale * sizeFactor;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var finger = new bool[bind.Length];
            var hand = new bool[bind.Length];
            for (int w = 0; w < sp.WingCount; w++)
            {
                for (int k = 0; k < sp.wings[w].fingerLengths.Length; k++) finger[sp.WingBone(w, k)] = true;
                hand[sp.LegBone(sp.wings[w].leg, 2)] = true;
            }
            var mem = new List<int>(); var fing = new List<int>(); var thumb = new List<int>();
            // the wing membrane: membrane-shaded vertices on the wing bones (not the mane, whiskers or leg fins: LegFin
            // has its own ground check, CreatureLegFinTests)
            int wLo = sp.WingBone(0, 0), wHi = sp.WingBone(sp.WingCount - 1, WingBoneLayout.Bones(sp.wings[sp.WingCount - 1]) - 1);
            for (int i = 0; i < v.Length; i++)
            {
                Vector4 k = uv2[i];
                int part = Mathf.RoundToInt(uv1[i].x);
                if (part == WingedMeshBuilder.PartMembrane && (int)k.x >= wLo && (int)k.x <= wHi) mem.Add(i);
                else if (k.z > 0.999f && finger[(int)k.x]) fing.Add(i);
                else if (part == CreatureMeshBuilder.PartFoot && k.z > 0.999f && hand[(int)k.x]) thumb.Add(i);
            }
            Assert.Greater(thumb.Count, 0, "thumb claw vertices");
            float handR = sp.wings[0].thumbLength * size, clear = 0.02f * size, turnClear = 0.002f * size;
            var skin = new Matrix4x4[bind.Length];
            Vector3 Pos(int i) { Vector4 s = uv2[i]; return s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]); }
            foreach (string run in new[] { "straight", "weave", "circle" })
            {
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, size);
                var world = new Matrix4x4[bind.Length];
                float memMin = float.MaxValue, underMin = float.MaxValue, fingMin = float.MaxValue, thumbMin = float.MaxValue, liftMin = float.MaxValue;
                string at = "";
                float ts = Mathf.Sqrt(size);
                for (int t = 0; t < 600; t++)
                {
                    Vector3 target;
                    if (run == "straight") target = new Vector3(0f, 0f, 1000f);
                    else if (run == "weave")
                    {
                        Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                        target = sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * size);
                    }
                    else { float a = t / 60f * 3f / ts; target = new Vector3(0f, 0f, 0.25f * size) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.12f * size); }
                    sim.Tick(kDt, target);
                    if (t < 30 || t % 3 != 0) continue;
                    Assert.AreEqual(FlightMode.Ground, sim.Mode);
                    sim.WriteBoneWorld(world, 0);
                    for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                    Vector3 f0 = sim.Legs[sp.wings[0].leg].foot, f1 = sim.Legs[sp.wings[1].leg].foot;
                    bool Hand(Vector3 p) => (p - f0).magnitude < handR || (p - f1).magnitude < handR;
                    foreach (int i in mem)
                    {
                        Vector3 p = Pos(i);
                        if (Hand(p)) continue;
                        if (UnderTrunk(sim, sp, p, size)) underMin = Mathf.Min(underMin, p.y);
                        else if (p.y < memMin) { memMin = p.y; at = $"t={t} vertex {i}"; }
                    }
                    foreach (int i in fing) { Vector3 p = Pos(i); if (!Hand(p)) fingMin = Mathf.Min(fingMin, p.y); }
                    foreach (int i in thumb) thumbMin = Mathf.Min(thumbMin, Pos(i).y);
                    var cw = sim.Flight.Wings;
                    for (int w = 0; w < sp.WingCount; w++)
                    {
                        Vector3 up = sim.Rotations[sp.legs[sp.wings[w].leg].attachJoint] * Vector3.up;
                        int mid = cw.FingerIndex(w, sp.wings[w].fingerLengths.Length / 2);
                        liftMin = Mathf.Min(liftMin, Mathf.Asin(Mathf.Clamp(Vector3.Dot(cw.FingerDir[mid], up), -1f, 1f)) * Mathf.Rad2Deg);
                    }
                }
                TestContext.WriteLine($"x{sizeFactor} {run}: membrane >= {memMin * 100f:F1} cm ({at}), under the trunk >= {underMin * 100f:F1} cm, fingers >= {fingMin * 100f:F1} cm, middle finger >= {liftMin:F0} deg up, thumb claws down to {thumbMin * 100f:F1} cm");
                Assert.Greater(liftMin, 15f, $"x{sizeFactor} {run}: the folded fingers point up");
                Assert.Greater(fingMin, clear, $"x{sizeFactor} {run}: a finger near the ground");
                Assert.Greater(memMin, run == "straight" ? clear : turnClear, $"x{sizeFactor} {run}: the membrane drags ({at})");
                Assert.GreaterOrEqual(underMin, 0f, $"x{sizeFactor} {run}: the membrane under the trunk is in the ground");
                Assert.That(thumbMin, Is.InRange(-0.005f * size, 0.01f * size), $"x{sizeFactor} {run}: the thumb claws stand on the ground");
            }
        }

        // A point inside the trunk's plan outline (the spine's segments widened by the joints' half-widths).
        static bool UnderTrunk(CreatureSim sim, CreatureSpecies sp, Vector3 p, float size)
        {
            for (int j = 0; j + 1 < sp.JointCount; j++)
            {
                Vector3 a = sim.Joints[j], ab = sim.Joints[j + 1] - a, ap = p - a;
                ab.y = 0f; ap.y = 0f;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                if ((ap - ab * t).magnitude <= sp.joints[j].halfWidth * size) return true;
            }
            return false;
        }
    }
}
