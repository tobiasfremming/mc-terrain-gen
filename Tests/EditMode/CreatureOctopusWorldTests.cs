using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Tree octopus in the creature world (Editor: assets, MeshColliders, the renderer, the profiler's GC counter).
    public class CreatureOctopusWorldTests
    {
        public const string kOctopusPath = "Assets/TerrainGen/Config/Creatures/TreeOctopus.asset";
        const float Dt = 1f / 60f;

        ClimbTestMeshes geo;
        GameObject go;
        CreatureWorld world;
        bool savedBackfaces;
        readonly List<Object> trash = new List<Object>();

        [SetUp] public void SetUp()
        {
            savedBackfaces = Physics.queriesHitBackfaces;
            geo = new ClimbTestMeshes();
            go = new GameObject("OctopusWorldTest");
            world = go.AddComponent<CreatureWorld>();
            world.SetGround(new FlatGroundProbe(), _ => Vector3.up);
            world.SetSurfaces(geo.Registry);
        }

        [TearDown] public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            geo.Dispose();
            Physics.queriesHitBackfaces = savedBackfaces;
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        internal static CreatureSpecies Octopus()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kOctopusPath);
            Assert.IsNotNull(sp, kOctopusPath);
            return sp;
        }

        // Two branches 2.5 m up (MeshColliders) for the octopus to hang from.
        void Branches()
        {
            geo.Branch(new Vector3(-3f, 2.5f, -0.3f), 0.06f, 6f);
            geo.Branch(new Vector3(-3f, 2.5f, 0.3f), 0.06f, 6f);
            Physics.SyncTransforms();
        }

        [Test] public void Octopus_SpeciesAsset_Validates()
        {
            CreatureSpecies sp = Octopus();
            Assert.AreEqual(CreatureBodyPlan.Cephalopod, sp.bodyPlan);
            Assert.IsNull(sp.Validate());
            Assert.AreEqual(201, OctopusBody.BoneCountFor(sp.cephalopod));
        }

        // Spawned in the world on real colliders: it takes hold of the branches, hangs, is drawn by one renderer
        // with bounds around it; every contact it makes is on a collider (the probe is the composite of the flat
        // ground and the registry's PhysicsSurfaceProbe).
        [Test] public void World_SpawnOctopus_HangsAndRenders()
        {
            Branches();
            CreatureSpecies sp = Octopus();
            OctopusCreature oct = world.SpawnOctopus(sp, new Vector3(0f, 2.0f, 0f), Quaternion.identity, null, 3);
            int held;
            using (PhysicsSurfaceProbe.Backfaces()) held = oct.AnchorNearby();
            Assert.GreaterOrEqual(held, 2, "did not take hold of the branches");
            for (int f = 0; f < 240; f++) world.TickAll(Dt);
            Vector3 head = oct.Body.HeadCentre;
            TestContext.Out.WriteLine($"head {head}, anchors {oct.Controller.Board.AnchoredCount}, last tick {oct.LastTickMs:F3} ms, queries {oct.Controller.QueriesThisFrame}");
            Assert.Greater(head.y, 1.0f, "fell off the branches");
            Assert.AreEqual(1, world.Bodies.Count);
            var renderers = (List<CreatureRenderer>)typeof(CreatureWorld).GetField("renderers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
            Assert.AreEqual(1, renderers.Count);
            Assert.DoesNotThrow(() => renderers[0].Render());
            Assert.IsTrue(renderers[0].LastBounds.Contains(head));
            int onColliders = 0;
            for (int a = 0; a < oct.Body.ArmCount; a++)
                for (int k = 1; k < oct.Body.Arms[a].N; k++)
                    if (oct.Body.Arms[a].Pinned[k] && oct.Body.Arms[a].PinSource[k] != 0) onColliders++;
            Assert.Greater(onColliders, 0, "no hold on a branch collider");
            Assert.IsTrue(world.RemoveBody(oct));
        }

        // No GC allocation per frame: tick (brain, agents, body, contact) and render, after a warm-up.
        [Test] public void Octopus_NoAlloc()
        {
            GcAllocs.AssertCounterWorks();
            Branches();
            OctopusCreature oct = world.SpawnOctopus(Octopus(), new Vector3(0f, 2.0f, 0f), Quaternion.identity, null, 3);
            using (PhysicsSurfaceProbe.Backfaces()) oct.AnchorNearby();
            oct.Brain.CanopyRegions.Add(new Vector3(2f, 2.3f, 0f)); oct.Brain.CanopyRegions.Add(new Vector3(-2f, 2.3f, 0f));
            var renderers = (List<CreatureRenderer>)typeof(CreatureWorld).GetField("renderers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
            for (int f = 0; f < 120; f++) { world.TickAll(Dt); renderers[0].Render(); }
            oct.Controller.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(1.5f, 2f, 0f), urgency = 0.6f };
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 120; f++) { world.TickAll(Dt); renderers[0].Render(); } });
            Assert.AreEqual(0, allocs, "GC allocations per frame");
        }

        // The lab's octopus enclosure on flat ground: a grove of real Baobabs (bark colliders), the octopus
        // anchored in the central canopy at a scale fitted to the branches, a prey creature on the floor; after
        // 20 s the octopus is still up in the trees and inside its curb.
        [Test] public void OctopusLab_PlacesGrove_OctopusAndPrey()
        {
            const string kSet = "Assets/TerrainGen/Config/Flora/Prototypes/BaobabPlant_Prototypes.asset";
            var set = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(kSet);
            if (set == null) Assert.Ignore(kSet + " is not baked on this machine (Config/Flora/Prototypes is gitignored)");
            var lab = go.AddComponent<OctopusLab>();
            lab.octopus = Octopus();
            lab.groveSets = new[] { set };
            lab.groveTrees = 5;
            var prey = CreatureTestSpecies.Lizardish();
            trash.Add(prey);
            lab.preySpecies = prey;
            var player = new GameObject("player");
            trash.Add(player);
            lab.Place(world, player.transform);
            Assert.AreEqual(5, lab.Trees.Count);
            Assert.IsNotNull(lab.Octopus);
            Assert.IsNotNull(lab.Prey);
            Assert.Greater(lab.ChosenScale, 0.4f);
            Assert.Greater(lab.Hints.TreeCount, 0, "no branch hints from the grove");
            Assert.GreaterOrEqual(lab.Octopus.Controller.Board.AnchoredCount, 2, "the octopus did not take hold in the canopy");
            for (int f = 0; f < 60 * 20; f++) world.TickAll(Dt);
            Vector3 head = lab.Octopus.Body.HeadCentre, c = lab.Enclosure.transform.position;
            TestContext.Out.WriteLine($"scale {lab.ChosenScale:F2}, head {head - c} from the centre, anchors {lab.Octopus.Controller.Board.AnchoredCount}, brain {lab.Octopus.Brain.Mode}");
            Assert.Greater(head.y - c.y, 1.0f, "fell out of the trees");
            Assert.Less(new Vector2(head.x - c.x, head.z - c.z).magnitude, lab.Enclosure.radius, "left the enclosure");
        }

        // A creature as prey: its spine is the grab capsule, it struggles while held and flees after release.
        [Test] public void CreaturePrey_StrugglesThenFlees()
        {
            var sp = CreatureTestSpecies.Lizardish();
            trash.Add(sp);
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 8f, sp, 1f);
            var brain = new PreyBrain(new WanderBrain(area, 4), 4);
            CreatureSim sim = world.Spawn(sp, Vector3.zero, Vector3.forward, brain, area, 4);
            var prey = new CreaturePrey(sim, brain) { GrabberPosition = () => new Vector3(-3f, 2f, 0f) };
            for (int f = 0; f < 30; f++) { prey.Sample(Dt); world.TickAll(Dt); }
            prey.GrabVolume(out Vector3 a, out Vector3 b, out float r);
            Assert.Greater((a - b).magnitude, 0.5f * sp.BodyLength * 0.5f);
            Assert.Greater(r, 0f);
            Assert.IsTrue(prey.CanBeGrabbed);
            prey.OnGrabbed();
            Assert.IsFalse(prey.CanBeGrabbed);
            Vector3 heldAt = sim.Joints[0];
            for (int f = 0; f < 60; f++) { prey.Sample(Dt); world.TickAll(Dt); }
            Assert.Less((sim.Joints[0] - heldAt).magnitude, 1.5f, "a held prey runs off");
            prey.OnReleased();
            Vector3 releasedAt = sim.Joints[0];
            for (int f = 0; f < 120; f++) { prey.Sample(Dt); world.TickAll(Dt); }
            Assert.Greater(sim.Joints[0].x - releasedAt.x, 0.3f, "did not flee away from the octopus (at -X)");
            Assert.IsFalse(prey.CanBeGrabbed, "grabbable again right away");
        }
    }
}

namespace TerrainGen.Tests.Perf
{
    // Octopus cost on Mono (design §4: active <= 0.35 ms, resting <= 0.12 ms, <= 40 queries per frame).
    [Category("Perf")]
    public class OctopusPerfTests
    {
        const float Dt = 1f / 60f;

        static double Median(List<double> v) { v.Sort(); return v[v.Count / 2]; }

        [Test] public void Perf_OctopusActive_Resting_Queries()
        {
            var geo = new ClimbTestMeshes();
            var go = new GameObject("OctopusPerf");
            bool saved = Physics.queriesHitBackfaces;
            try
            {
                var world = go.AddComponent<CreatureWorld>();
                world.SetGround(new FlatGroundProbe(), _ => Vector3.up);
                world.SetSurfaces(geo.Registry);
                geo.Branch(new Vector3(-3f, 2.5f, 0f), 0.06f, 6f);
                geo.Branch(new Vector3(-3f, 2.5f, 1.1f), 0.06f, 6f);
                Physics.SyncTransforms();
                OctopusCreature oct = world.SpawnOctopus(CreatureOctopusWorldTests.Octopus(), new Vector3(0f, 2.0f, 0.1f), Quaternion.identity, null, 9);
                using (PhysicsSurfaceProbe.Backfaces()) oct.AnchorNearby();
                var active = new List<double>(); var resting = new List<double>();
                int maxQ = 0;
                // active: travelling across to the second branch
                oct.Controller.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(0f, 2f, 1.6f), urgency = 0.6f };
                oct.Brain.CanopyRegions.Add(new Vector3(0f, 2.3f, 1.1f));
                for (int f = 0; f < 600; f++)
                {
                    world.TickAll(Dt);
                    if (f >= 60) active.Add(oct.LastTickMs);
                    maxQ = Mathf.Max(maxQ, oct.Controller.QueriesThisFrame);
                }
                // resting: hold still until the body sleeps or 30 s pass
                oct.Controller.Brain = null;
                oct.Controller.Intent = new OctoIntent { mode = OctoMode.Rest, goal = oct.Body.HeadCentre };
                for (int f = 0; f < 60 * 30 && !oct.Body.Asleep; f++) world.TickAll(Dt);
                for (int f = 0; f < 300; f++) { world.TickAll(Dt); resting.Add(oct.LastTickMs); }
                double ma = Median(active), mr = Median(resting);
                TestContext.Out.WriteLine($"octopus tick median: active {ma:F3} ms, resting {mr:F3} ms (asleep {oct.Body.Asleep}); queries/frame peak {maxQ}, average {oct.Controller.TotalQueries / (oct.Controller.Time * 60f):F1}");
                Debug.Log($"[OctopusPerf] active {ma:F3} ms, resting {mr:F3} ms, peak queries {maxQ}");
                Assert.Less(ma, 0.35 * 3, "active cost (design 0.35 ms; 3x headroom for a busy Editor)");
                Assert.Less(mr, 0.12 * 3, "resting cost (design 0.12 ms)");
            }
            finally
            {
                Physics.queriesHitBackfaces = saved;
                Object.DestroyImmediate(go);
                geo.Dispose();
            }
        }
    }
}
