using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Winged serpent tests that need the engine (MeshColliders, the renderer, Unity's GC counter); the pure
    // ones (WingedSerpent*Tests) also run offline.
    public class CreatureWingedEditorTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        WingedSerpentTestSpecies.Serpent Serpent()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            return s;
        }

        public const string kAssetPath = "Assets/TerrainGen/Config/Creatures/WingedSerpent.asset";

        // The lab's asset is the tested factory, field for field where it matters (generated from the same
        // numbers; scratch gen_asset.py).
        [Test] public void WingedSerpentAsset_MatchesFactory()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kAssetPath);
            Assert.IsNotNull(asset, kAssetPath);
            Assert.IsNull(asset.Validate());
            Assert.IsTrue(asset.CanFly);
            var s = Serpent();
            CreatureSpecies f = s.species;
            Assert.AreEqual(f.JointCount, asset.JointCount);
            Assert.AreEqual(f.BoneCount, asset.BoneCount);
            Assert.AreEqual(f.spacing, asset.spacing, 1e-6f);
            for (int i = 0; i < f.JointCount; i++)
            {
                Assert.AreEqual(f.joints[i].halfWidth, asset.joints[i].halfWidth, 1e-5f, "halfWidth " + i);
                Assert.AreEqual(f.joints[i].clearance, asset.joints[i].clearance, 1e-5f, "clearance " + i);
                Assert.AreEqual(f.joints[i].bendLimitDeg, asset.joints[i].bendLimitDeg, 1e-5f, "bend " + i);
                Assert.AreEqual(f.joints[i].rigid, asset.joints[i].rigid, "rigid " + i);
            }
            for (int l = 0; l < f.legs.Length; l++)
            {
                Assert.AreEqual(f.legs[l].attachJoint, asset.legs[l].attachJoint);
                Assert.AreEqual(f.legs[l].side, asset.legs[l].side);
                Assert.AreEqual((f.legs[l].restOffset - asset.legs[l].restOffset).magnitude, 0f, 1e-5f, "restOffset " + l);
                Assert.AreEqual(f.legs[l].upperLength, asset.legs[l].upperLength, 1e-6f);
                Assert.AreEqual(f.legs[l].lowerLength, asset.legs[l].lowerLength, 1e-6f);
            }
            for (int w = 0; w < f.WingCount; w++)
            {
                WingSpec a = asset.wings[w], b = f.wings[w];
                Assert.AreEqual(b.leg, a.leg);
                Assert.AreEqual((b.restTip - a.restTip).magnitude, 0f, 1e-5f);
                for (int k = 0; k < 3; k++)
                {
                    Assert.AreEqual(b.fingerLengths[k], a.fingerLengths[k], 1e-6f);
                    Assert.AreEqual(b.fingerSpreadDeg[k], a.fingerSpreadDeg[k], 1e-5f);
                    Assert.AreEqual(b.fingerFoldDeg[k], a.fingerFoldDeg[k], 1e-5f);
                }
                Assert.AreEqual(b.membraneRearJoint, a.membraneRearJoint);
                Assert.AreEqual(b.trailingRib, a.trailingRib, 1e-6f);
            }
            Assert.AreEqual(f.flight.cruiseSpeed, asset.flight.cruiseSpeed, 1e-6f);
            Assert.AreEqual(f.flight.kGrip, asset.flight.kGrip, 1e-4f);
            Assert.AreEqual(f.flight.bankGain, asset.flight.bankGain, 1e-6f);
            Assert.AreEqual(f.flight.climbIntegral, asset.flight.climbIntegral, 1e-6f);
            Assert.AreEqual(f.flight.headStabilise, asset.flight.headStabilise, 1e-6f);
            Assert.AreEqual(f.look.membrane.r, asset.look.membrane.r, 1e-5f);
        }

        // The lab gives a flying species an aviary: 14 m radius, 10 m roof, rock perches (registered surfaces),
        // a FlightBrain with the perches.
        [Test] public void Lab_PlacesAviary_ForFlyer()
        {
            var s = Serpent();
            var go = new GameObject("LabTest"); trash.Add(go);
            var world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/Creature.mat");
            world.SetGround(new FlatGroundProbe(), _ => Vector3.up);
            var lab = go.AddComponent<CreatureLab>();
            lab.species = new[] { s.species };
            var player = new GameObject("Player"); trash.Add(player);
            lab.Place(world, player.transform, Vector3.zero);
            CreatureEnclosure enc = lab.EnclosureFor(0);
            CreatureSim sim = lab.SimFor(0);
            // 14 m x 10 m at scale 1, grown for the largest individual (scale x (1 + sizeJitter))
            float big = Mathf.Max(1f, CreatureLab.MaxScale(s.species));
            Assert.AreEqual(14f * big, enc.radius, 1e-3f, "aviary radius");
            Assert.IsTrue(sim.Bounds.HasValue);
            Assert.AreEqual(10f * big, sim.Bounds.Value.maxHeight, 1e-3f, "aviary roof");
            Assert.IsInstanceOf<FlightBrain>(world.BrainOf(0));
            int rocks = 0;
            foreach (Transform t in enc.transform) if (t.name == "Rock perch") { rocks++; Assert.IsNotNull(t.GetComponent<CreatureSurfaceMesh>()); }
            Assert.AreEqual(lab.aviaryRocks, rocks, "rock perches");
            Assert.AreEqual(lab.aviaryRocks, enc.obstacles.Count, "rocks are walked round");
            Assert.IsTrue(FlightBrain.FitsAviary(s.species, sim.Bounds.Value, sim.SizeScale));
        }

        // Land on a real branch mesh (a MeshCollider through PhysicsSurfaceProbe and the composite ground, as
        // CreatureWorld sets a flyer up): perched on the bark, lengths exact.
        [Test] public void Land_Branch_RealMesh()
        {
            var s = Serpent();
            using (var geo = new ClimbTestMeshes())
            using (PhysicsSurfaceProbe.Backfaces())
            {
                MeshCollider branch = geo.Branch(new Vector3(-1.5f, 2.2f, 8f), 0.2f, 3f);
                var ground = new CompositeSurfaceProbe(new FlatGroundProbe(), new PhysicsSurfaceProbe(geo.Registry));
                var sim = new CreatureSim(s.species, ground, _ => Vector3.up, Vector3.zero, Vector3.right);
                Assert.IsTrue(sim.Flight.ProbeDown(new Vector3(0f, 4f, 8f), out Vector3 top, out Vector3 n, out int src), "the branch answers a probe down");
                TestContext.WriteLine($"branch top {top}, normal {n}, source {src} (collider {branch.GetInstanceID()})");
                WingedSerpentModeTests.FlyAndLand(sim, top, src, 45f, out int ticks);
                Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed");
                for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
                int g = sim.Flight.Girdle;
                TestContext.WriteLine($"landed after {ticks * kDt:F1} s, girdle {sim.Joints[g]}, perched {sim.Flight.Perched}");
                Assert.IsTrue(sim.Flight.Perched, "perched on the branch");
                Assert.Greater(sim.Joints[g].y, 2.2f, "the girdle rests on the branch, not the ground");
                Assert.Less(WingedSerpentModeTests.MaxSegmentError(sim), 1e-3f);
            }
        }

        // The renderer's culling bounds hold every skinned vertex, wings spread in the air (span past the body).
        [Test] public void Renderer_Bounds_ContainWings()
        {
            var s = Serpent();
            var sim = new CreatureSim(s.species, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Shader sh = Shader.Find("MarchingCubes/Creature");
            var mat = new Material(sh != null ? sh : Shader.Find("Hidden/InternalErrorShader"));
            trash.Add(mat);
            var r = new CreatureRenderer(s.species, mat);
            try
            {
                r.Add(sim, CreatureWorld.Individual(s.species, 1));
                sim.Flight.Launch(4f);
                var verts = r.Mesh.vertices;
                var uv2 = new List<Vector4>(); r.Mesh.GetUVs(2, uv2);
                for (int t = 0; t < 300; t++)
                {
                    sim.Tick(kDt, sim.Joints[0]);
                    if (t % 30 != 0) continue;
                    r.Render();
                    Bounds b = r.LastBounds;
                    Matrix4x4[] skin = r.SkinForTest;
                    for (int i = 0; i < verts.Length; i += 3)
                    {
                        Vector4 w = uv2[i];
                        Vector3 p = w.z * skin[(int)w.x].MultiplyPoint3x4(verts[i]) + w.w * skin[(int)w.y].MultiplyPoint3x4(verts[i]);
                        Assert.IsTrue(b.Contains(p), $"vertex {i} at {p} outside the bounds {b} (tick {t}, mode {sim.Mode})");
                    }
                }
            }
            finally { r.Dispose(); }
        }

        // No managed allocation per tick, ground and air.
        [Test] public void Flyer_NoAlloc()
        {
            var s = Serpent();
            var sim = new CreatureSim(s.species, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            var bones = new Matrix4x4[s.species.BoneCount];
            for (int t = 0; t < 30; t++) sim.Tick(kDt, sim.Joints[0] + Vector3.forward);
            GcAllocs.AssertCounterWorks();
            int ground = GcAllocs.Count(() => { for (int t = 0; t < 120; t++) { sim.Tick(kDt, sim.Joints[0] + Vector3.forward); sim.WriteBoneWorld(bones, 0); } });
            sim.Flight.Launch(4f);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
            int air = GcAllocs.Count(() => { for (int t = 0; t < 120; t++) { sim.Tick(kDt, sim.Joints[0]); sim.WriteBoneWorld(bones, 0); } });
            Assert.AreEqual(FlightMode.Air, sim.Mode);
            Assert.AreEqual(0, ground, "allocations walking");
            Assert.AreEqual(0, air, "allocations flying");
        }
    }
}

namespace TerrainGen.Tests.Perf
{
    // Flight cost (design section 7): air <= 0.05 ms per flyer, ground <= 0.06 ms, 20 flyers <= 1 ms.
    // Median per frame over 300 frames after a warm-up, the FlightBrain driving.
    [Category("Perf")]
    public class CreatureFlightPerfTests
    {
        const float kDt = 1f / 60f;

        static double Median(List<CreatureSim> sims, List<FlightBrain> brains, int frames)
        {
            var sw = new Stopwatch();
            var times = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                sw.Restart();
                for (int i = 0; i < sims.Count; i++) sims[i].Tick(kDt, brains[i].Target(sims[i], kDt));
                sw.Stop();
                times[f] = sw.Elapsed.TotalMilliseconds;
            }
            System.Array.Sort(times);
            return times[frames / 2];
        }

        static void Spawn(CreatureSpecies sp, int count, out List<CreatureSim> sims, out List<FlightBrain> brains)
        {
            sims = new List<CreatureSim>(); brains = new List<FlightBrain>();
            var surfaces = WingedSerpentBrainTests.Perches(out PerchSpot[] spots);
            EnclosureArea area = WingedSerpentBrainTests.Aviary(sp);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.4f;
                var at = new Vector3(4f * Mathf.Cos(a), 0f, 4f * Mathf.Sin(a));
                sims.Add(new CreatureSim(sp, surfaces, _ => Vector3.up, at, Vector3.forward, 1f, i) { Bounds = area });
                brains.Add(new FlightBrain(area, i + 1, spots));
            }
        }

        [Test] public void Perf_FlyerAir()
        {
            var s = WingedSerpentTestSpecies.Create();
            try
            {
                Spawn(s.species, 1, out var sims, out var brains);
                sims[0].Flight.Launch(5f);
                for (int t = 0; t < 120; t++) sims[0].Tick(kDt, sims[0].Joints[0]);
                var air = new List<CreatureSim> { sims[0] };
                // the brain would land after a while: steer by goals only for the timed frames
                var sw = new Stopwatch(); var times = new double[300];
                for (int f = 0; f < 300; f++)
                {
                    sims[0].Flight.SetGoals(5f, 5f, 0.3f);
                    sw.Restart(); sims[0].Tick(kDt, sims[0].Joints[0]); sw.Stop();
                    times[f] = sw.Elapsed.TotalMilliseconds;
                }
                System.Array.Sort(times);
                double median = times[150];
                TestContext.Out.WriteLine($"1 flyer in the air: median {median:F4} ms per frame (mode {sims[0].Mode})");
                Assert.AreEqual(FlightMode.Air, sims[0].Mode);
                Assert.LessOrEqual(median, 0.05, "air ms per flyer per frame");
            }
            finally { Object.DestroyImmediate(s.species); }
        }

        [Test] public void Perf_FlyerGround()
        {
            var s = WingedSerpentTestSpecies.Create();
            try
            {
                var sim = new CreatureSim(s.species, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
                for (int t = 0; t < 60; t++) sim.Tick(kDt, sim.Joints[0] + Vector3.forward * 3f);
                var sw = new Stopwatch(); var times = new double[300];
                for (int f = 0; f < 300; f++) { sw.Restart(); sim.Tick(kDt, sim.Joints[0] + Vector3.forward * 3f); sw.Stop(); times[f] = sw.Elapsed.TotalMilliseconds; }
                System.Array.Sort(times);
                TestContext.Out.WriteLine($"1 flyer walking (wing-walk): median {times[150]:F4} ms per frame");
                Assert.LessOrEqual(times[150], 0.06, "ground ms per flyer per frame");
            }
            finally { Object.DestroyImmediate(s.species); }
        }

        [Test] public void Perf_20Flyers()
        {
            var s = WingedSerpentTestSpecies.Create();
            try
            {
                Spawn(s.species, 20, out var sims, out var brains);
                Median(sims, brains, 600); // warm-up: most are in the air by now
                double median = Median(sims, brains, 300);
                int airborne = 0; foreach (var sim in sims) if (sim.Flight.Airborne) airborne++;
                TestContext.Out.WriteLine($"20 flyers (FlightBrain, {airborne} airborne at the end): median {median:F3} ms per frame");
                Assert.LessOrEqual(median, 1.0, "20 flyers ms per frame");
            }
            finally { Object.DestroyImmediate(s.species); }
        }
    }
}
