using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The SkyDragon's v4 look (user feedback 2026-10-19): "make the wings look better", "wing flap stuff on the legs ...
    // like the legs of Caraxes", "a larger chest so it has muscles to support the wings". The two new kit parts (Swell:
    // the keeled chest; LegFin: the leg frills) and the wing's look fields (WingSpec: scallopByChord, membraneBillow,
    // membraneDetail, fingerKnuckles, wristKnuckle) - all off (0) for every other species, which keep their meshes byte
    // for byte (CreatureMeshEmitterTests).
    public class CreatureDragonLookTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints, params CreaturePart[] parts) => new SpeciesCtx(parts, new CreatureModule[0], joints, false);

        static T Part<T>(CreatureSpecies sp) where T : CreaturePart
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T t) return t;
            return null;
        }

        static List<T> Parts<T>(CreatureSpecies sp) where T : CreaturePart
        {
            var l = new List<T>();
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T t) l.Add(t);
            return l;
        }

        sealed class MeshData
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv1 = new List<Vector2>(); public List<Vector4> uv2 = new List<Vector4>();
        }

        MeshData Mesh(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var d = new MeshData { v = m.vertices, n = m.normals, t = m.triangles };
            m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        // ------------------------------------------------------------------ Swell (the chest)

        [Test] public void Swell_Validate_RejectsBadData()
        {
            var ok = new Swell { id = "s", at = new PartSocket(4, 0, Vector3.zero), toJoint = 20, peakAt = 0.3f, keel = 0.05f, breadth = 0.03f };
            Assert.IsNull(ok.Validate(Ctx(30)));
            var bad = (Swell)ok.Clone(); bad.toJoint = 4;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "empty stretch");
            bad = (Swell)ok.Clone(); bad.toJoint = 30;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "past the last joint");
            bad = (Swell)ok.Clone(); bad.keel = 0f; bad.breadth = 0f; bad.hump = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "no swell at all");
            bad = (Swell)ok.Clone(); bad.peakAt = 1f;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "the peak at an end");
            Assert.AreEqual(0, ok.BoneCount(Ctx(30)), "mesh only: skinned to the spine");
        }

        // The dragon's chest: a deep keel under the wing roots (the girdle joint), its lowest point at least the keel
        // below the body's own section there, broad at the flanks; skinned to the spine as the body rings are (two
        // neighbouring joints); unit outward normals; at both ends it sinks inside the body (no edge).
        [Test] public void Swell_Chest_DeepKeelAtTheWingRoots()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            Swell chest = Part<Swell>(sp);
            Assert.IsNotNull(chest, "the dragon has a chest");
            int girdle = sp.legs[sp.wings[0].leg].attachJoint;
            Assert.That(girdle, Is.InRange(chest.at.joint + 1, chest.toJoint - 1), "the wing roots sit in the chest");
            Assert.Greater(chest.Amount(girdle), 0.7f, "near its fullest at the wing roots");
            MeshData d = Mesh(sp);
            CreatureSim rest = CreatureRestPose.Sim(sp);
            WingedMeshBuilder.StraightenBind(sp, sp.wings, rest);
            Vector3 gj = rest.Joints[girdle];
            float below = 0f, side = 0f; int body = 0;
            for (int i = 0; i < d.v.Length; i++)
            {
                if (Mathf.RoundToInt(d.uv1[i].x) != PartMeshEmitter.PartBody) continue;
                Vector4 s = d.uv2[i];
                if ((int)s.x < chest.at.joint || (int)s.x > chest.toJoint) continue;
                if (Mathf.Abs(d.v[i].z - gj.z) > 2f * sp.spacing) continue;
                below = Mathf.Max(below, gj.y - d.v[i].y);
                side = Mathf.Max(side, Mathf.Abs(d.v[i].x - gj.x));
                body++;
                Assert.AreEqual(1f, d.n[i].magnitude, 1e-3f, $"vertex {i}: unit normal");
                Assert.AreEqual(1f, s.z + s.w, 1e-5f, "two-bone spine skin");
                Assert.LessOrEqual(Mathf.Abs((int)s.x - (int)s.y), 1, "neighbouring joints");
            }
            float hh = sp.joints[girdle].halfHeight, hw = sp.joints[girdle].halfWidth;
            TestContext.WriteLine($"chest at joint {girdle}: {below:F3} m below the spine (body {hh:F3}, keel {chest.keel:F3}), {side:F3} m to the side (body {hw:F3})");
            Assert.Greater(below, hh + 0.8f * chest.keel, "a deep keeled chest");
            Assert.Greater(side, hw + 0.5f * chest.breadth, "broad at the wing roots");
            Assert.Greater(below, 2.2f * hh, "at least 2.2x the body's own depth below the spine");
            // the ends sink inside the body
            Assert.AreEqual(0f, chest.Amount(chest.at.joint), 1e-6f);
            Assert.AreEqual(0f, chest.Amount(chest.toJoint), 1e-6f);
            // and the body is slimmer at the waist behind the chest than at the chest
            int waist = Mathf.RoundToInt(0.46f * (sp.JointCount - 1));
            Assert.Less(sp.joints[waist].halfWidth, 0.8f * (hw + chest.breadth), "a waist before the hips");
        }

        // ------------------------------------------------------------------ LegFin (the leg frills)

        [Test] public void LegFin_Validate_RejectsBadData()
        {
            var leg = new Leg2Bone { id = "hind.L", upperLength = 0.2f, lowerLength = 0.2f };
            var ok = new LegFin { id = "f.L", at = new PartSocket(10, -1, Vector3.zero), leg = "hind", thighHeight = 0.05f, shinHeight = 0.08f };
            Assert.AreEqual("hind.L", ok.LegId, "a mirrored fin names the mirrored leg");
            Assert.IsNull(ok.Validate(Ctx(30, leg, ok)));
            var bad = (LegFin)ok.Clone(); bad.leg = "nope";
            Assert.IsNotNull(bad.Validate(Ctx(30, leg, bad)), "names no leg");
            bad = (LegFin)ok.Clone(); bad.thighHeight = 0f; bad.shinHeight = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(30, leg, bad)), "no fin");
            bad = (LegFin)ok.Clone(); bad.groundFold = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(30, leg, bad)), "folds to nothing");
            Assert.AreEqual(2, ok.BoneCount(Ctx(30, leg, ok)), "a thigh fin and a shin fin bone");
        }

        // Skinned LegFin vertices (rigid to their fin bones) of a sim now.
        static List<Vector3> FinVertices(CreatureSpecies sp, MeshData d, Matrix4x4[] bind, CreatureSim sim, List<int> finBones)
        {
            var world = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(world, 0);
            var o = new List<Vector3>();
            for (int i = 0; i < d.v.Length; i++)
            {
                if (Mathf.RoundToInt(d.uv1[i].x) != PartMeshEmitter.PartMembrane) continue;
                int b = (int)d.uv2[i].x;
                if (!finBones.Contains(b)) continue;
                o.Add((world[b] * bind[b]).MultiplyPoint3x4(d.v[i]));
            }
            return o;
        }

        static List<int> FinBones(CreatureSpecies sp)
        {
            var l = new List<int>();
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] is LegFin) { int s = sp.Layout.Start(i); l.Add(s); l.Add(s + 1); }
            return l;
        }

        // On the ground the fins fold flat along the legs and never reach the ground (walking straight and circling, at
        // 0.9x and 1.1x); in level flight they spread (fold < 5 %) and turn level toward the body's side: the fin's
        // plane level in the hip's frame (its normal within 30 deg of the hip's up; the trailing leg's own slope tilts
        // it a little) and its spread pointing outward - with the hind legs trailing
        // back and out (FlightSpec.legTuck*) a rear lift surface on each side.
        [TestCase(0.9f)]
        [TestCase(1.1f)]
        public void LegFin_FoldOnTheGround_SpreadLevelInTheAir(float sizeFactor)
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            float s = sp.scale * sizeFactor;
            Assert.AreEqual(2, Parts<LegFin>(sp).Count, "a fin pair, one per hind leg");
            MeshData d = Mesh(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            List<int> finBones = FinBones(sp);

            // ground: walk straight, then circle
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 3);
            float lowest = float.MaxValue; LegFinRuntime fr = null;
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is LegFinRuntime lf) { fr = lf; break; }
            Assert.IsNotNull(fr, "a fin runtime");
            for (int t = 0; t < 600; t++)
            {
                Vector3 target = t < 300 ? sim.Joints[0] + Vector3.forward * s : new Vector3(Mathf.Cos(t * 0.02f), 0f, Mathf.Sin(t * 0.02f)) * (0.6f * s);
                sim.Tick(kDt, target);
                if (t < 60) continue;
                foreach (Vector3 p in FinVertices(sp, d, bind, sim, finBones)) lowest = Mathf.Min(lowest, p.y);
            }
            TestContext.WriteLine($"x{sizeFactor}: ground fold {fr.Fold:F3}, lowest fin vertex {lowest / s:F4} x size above the ground");
            Assert.Greater(fr.Fold, 0.95f, "folded on the ground");
            Assert.Greater(lowest, 0.01f * s, "the folded fins never reach the ground");

            // air: level flight
            var air = CreatureDragonTwistTests.AirborneDragon(sp, s, 6f * s, out _);
            for (int t = 0; t < 240; t++) { air.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * air.TimeScale, 0f); air.Tick(kDt, air.Joints[0]); }
            var world = new Matrix4x4[sp.BoneCount];
            air.WriteBoneWorld(world, 0);
            foreach (IPartRuntime r in air.PartRuntimes)
            {
                if (!(r is LegFinRuntime lf)) continue;
                Assert.Less(lf.Fold, 0.05f, "spread in the air");
                int side = lf.Part.at.side < 0 ? -1 : 1, hip = sp.legs[lf.Part.LegIndex(sp)].attachJoint;
                // in the hip's frame (the swimming, rolling body: level = the body's own horizontal there)
                Vector3 sideDir = air.Rotations[hip] * new Vector3(side, 0f, 0f), hipUp = air.Rotations[hip] * Vector3.up;
                for (int k = 0; k < LegFin.Fins; k++)
                {
                    Matrix4x4 m = world[lf.FirstBone + k];
                    Vector3 normal = m.MultiplyVector(Vector3.right).normalized, spread = -m.MultiplyVector(Vector3.up).normalized;
                    float tilt = Vector3.Angle(normal, hipUp); tilt = Mathf.Min(tilt, 180f - tilt);
                    TestContext.WriteLine($"  {lf.Part.id} fin {k}: plane {tilt:F1} deg from level (flex {lf.Flex:F1}), spread . side {Vector3.Dot(spread, sideDir):F2}");
                    // level within 30 deg plus the wings' own droop and wingbeat flex (LegFin.airDroopDeg, flapDeg)
                    Assert.Less(tilt, 30f + Mathf.Abs(lf.Part.airDroopDeg) + lf.Part.flapDeg, "the fin lies level: a lift surface");
                    Assert.Greater(Vector3.Dot(spread, sideDir), 0.5f, "spread toward the side, away from the body");
                }
            }
        }

        // The leg wings are wings (user 2026-10-19, "make the leg wing flaps larger so that it can actually work like a
        // wing"): their membrane per side is at least a quarter of a main wing's, the shin wing reaches out behind the
        // leg further than the shin is long, the ribs are drawn; in level flight they flex with the wingbeat (at least
        // half of flapDeg peak to peak) and in a glide they settle (the flex eases out).
        [Test] public void LegFin_LegWings_SizedAndFlexing()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            MeshData d = Mesh(sp);
            var fin = new HashSet<int>(FinBones(sp));
            int wLo = sp.WingBone(0, 0), wHi = sp.WingBone(sp.WingCount - 1, WingBoneLayout.Bones(sp.wings[sp.WingCount - 1]) - 1);
            float aFin = 0f, aWing = 0f; int ribVerts = 0;
            for (int k = 0; k < d.t.Length; k += 3)
            {
                int a = d.t[k], b = (int)d.uv2[a].x;
                if (Mathf.RoundToInt(d.uv1[a].x) != PartMeshEmitter.PartMembrane) continue;
                float area = 0.5f * Vector3.Cross(d.v[d.t[k + 1]] - d.v[a], d.v[d.t[k + 2]] - d.v[a]).magnitude;
                if (fin.Contains(b)) aFin += area; else if (b >= wLo && b <= wHi) aWing += area;
            }
            for (int i = 0; i < d.v.Length; i++)
                if (Mathf.RoundToInt(d.uv1[i].x) == PartMeshEmitter.PartLeg && fin.Contains((int)d.uv2[i].x)) ribVerts++;
            LegFin lf = Parts<LegFin>(sp)[0];
            LegSpec leg = sp.legs[lf.LegIndex(sp)];
            float reach = 0f;
            for (int i = 0; i < lf.Ribs; i++) reach = Mathf.Max(reach, -lf.RibPoint(leg, 1, i, 1f).y);
            TestContext.WriteLine($"leg wings {aFin:F4} m2 vs main wings {aWing:F4} m2 (ratio {aFin / aWing:F2}), shin wing reach {reach:F3} m (shin {leg.lowerLength:F3} m), rib vertices {ribVerts}");
            Assert.Greater(aFin / aWing, 0.25f, "leg wings at least a quarter of the main wings' membrane");
            Assert.Greater(reach, leg.lowerLength, "the shin wing reaches out behind the leg further than the shin is long");
            Assert.Greater(ribVerts, 0, "finger-like ribs");

            float s = sp.scale;
            var air = CreatureDragonTwistTests.AirborneDragon(sp, s, 6f * s, out _);
            LegFinRuntime fr = null;
            foreach (IPartRuntime r in air.PartRuntimes) if (r is LegFinRuntime x) { fr = x; break; }
            float lo = float.MaxValue, hi = float.MinValue;
            for (int t = 0; t < 120; t++)
            {
                air.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * air.TimeScale, 0f); air.Tick(kDt, air.Joints[0]);
                lo = Mathf.Min(lo, fr.Flex); hi = Mathf.Max(hi, fr.Flex);
            }
            TestContext.WriteLine($"level flight: flex {lo:F1}..{hi:F1} deg");
            Assert.Greater(hi - lo, 0.5f * lf.flapDeg, "the leg wings flex with the wingbeat");
            int gl = 0;
            for (int t = 0; t < 1200 && gl < 45; t++)
            {
                air.Flight.SetGoals(1.2f * s, air.Flight.Model.ForwardSpeed, 0f); air.Tick(kDt, air.Joints[0]);
                gl = air.Flight.Model.Gliding ? gl + 1 : 0;
            }
            Assert.GreaterOrEqual(gl, 45, "a glide");
            TestContext.WriteLine($"gliding 0.75 s: flex {fr.Flex:F2} deg");
            Assert.Less(Mathf.Abs(fr.Flex), 0.25f * lf.flapDeg, "settling in a glide");
        }

        // ------------------------------------------------------------------ the wing's look

        // Knuckled finger bones (thicker than the plain taper at a third of the length), a wrist knuckle, the membrane
        // bellied between the fingers (its middle off the flat fan by a few % of the zone's width) on a finer grid, and
        // the scallop scaled to each zone's chord (a narrow zone's arc stays shallow).
        [Test] public void Wing_Look_KnucklesBillowScallops()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            WingSpec ws = sp.wings[0];
            Assert.IsTrue(ws.scallopByChord, "arcs by chord: no spikes");
            Assert.Greater(ws.membraneBillow, 0.03f, "a bellied membrane");
            Assert.GreaterOrEqual(ws.membraneDetail, 2, "a finer membrane grid");
            Assert.Greater(ws.fingerKnuckles, 0.3f, "knuckled finger bones");
            Assert.Greater(ws.wristKnuckle, 1f, "a wrist knuckle");
            Assert.Greater(ws.armMuscle, 0.3f, "a muscled dragon arm (2026-10-20)");
            Assert.Greater(ws.forearmTwin, 0.3f, "a two-bone forearm");
            MeshData d = Mesh(sp);
            // the membrane: every vertex accounted for at the finer grid (the bat-wing test counts them), and its sheet
            // off the plane of its zone: measure the spread of each finger-zone's vertices along the zone normal
            CreatureWings cw; Matrix4x4[] bones = WingedMeshBuilder.RestBoneWorld(sp, sp.wings, RestSim(sp), out cw);
            WingState st = cw.State[0];
            int f0 = sp.WingBone(0, 0), f1 = sp.WingBone(0, 1);
            Vector3 a = cw.FingerTip[cw.FingerIndex(0, 0)] - st.wrist, b = cw.FingerTip[cw.FingerIndex(0, 1)] - st.wrist;
            Vector3 nz = Vector3.Cross(a, b).normalized;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < d.v.Length; i++)
            {
                if (Mathf.RoundToInt(d.uv1[i].x) != WingedMeshBuilder.PartMembrane) continue;
                Vector4 s = d.uv2[i];
                bool pair = ((int)s.x == f0 && (int)s.y == f1) || ((int)s.x == f1 && (int)s.y == f0);
                if (!pair) continue;
                float h = Vector3.Dot(d.v[i] - st.wrist, nz);
                lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            }
            float width = (cw.FingerTip[cw.FingerIndex(0, 0)] - cw.FingerTip[cw.FingerIndex(0, 1)]).magnitude;
            TestContext.WriteLine($"zone 0: off its plane {lo:F4}..{hi:F4} m (tips {width:F3} m apart)");
            Assert.Greater(hi - lo, 0.02f * width, "the membrane bellies out between the fingers");
            Assert.Less(hi - lo, 0.2f * width, "a belly, not a bag");
        }

        static CreatureSim RestSim(CreatureSpecies sp)
        {
            CreatureSim sim = CreatureRestPose.Sim(sp);
            WingedMeshBuilder.StraightenBind(sp, sp.wings, sim);
            return sim;
        }
    }
}
