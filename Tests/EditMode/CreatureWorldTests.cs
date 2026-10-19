using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    public class CreatureWorldTests
    {
        internal const string kMonitorPath = "Assets/TerrainGen/Config/Creatures/DesertMonitor.asset";
        const string kMaterialPath = "Assets/TerrainGen/Config/Creatures/Creature.mat";

        readonly List<Object> trash = new List<Object>();
        GameObject go;
        CreatureWorld world;

        [TearDown]
        public void TearDown()
        {
            // Edit mode never sends Awake/OnDestroy to a plain MonoBehaviour: release the renderers by hand.
            if (world != null) Call(world, "OnDestroy")();
            if (go != null) Object.DestroyImmediate(go);
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static Action Call(CreatureWorld w, string method)
        {
            MethodInfo m = typeof(CreatureWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(m, method);
            return (Action)Delegate.CreateDelegate(typeof(Action), w, m);
        }

        CreatureWorld NewWorld()
        {
            go = new GameObject("CreatureWorldTest");
            world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = AssetDatabase.LoadAssetAtPath<Material>(kMaterialPath);
            world.SetGround(new FlatGroundProbe(), _ => Vector3.up);
            return world;
        }

        [Test] public void World_SpawnTickRender_NoAllocAfterWarmup()
        {
            var w = NewWorld();
            var sp = CreatureTestSpecies.Lizardish(); trash.Add(sp);
            for (int i = 0; i < 3; i++)
            {
                Vector3 c = new Vector3(4f * i, 0f, 0f);
                var area = EnclosureArea.For(c, Vector3.up, 2f, sp, 1f);
                w.Spawn(sp, c, Vector3.forward, new WanderBrain(area, 11 + i, 0.1f, 0.3f), area, 100 + i);
            }
            // a DesertMonitor too, so the gait sway and roll (R17) run under the GC check
            var monitor = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kMonitorPath);
            Assert.IsNotNull(monitor, kMonitorPath);
            Assert.Greater(monitor.gaitSwayDeg, 0f);
            var marea = EnclosureArea.For(new Vector3(0f, 0f, 8f), Vector3.up, 4f, monitor, 1f);
            w.Spawn(monitor, marea.center, Vector3.forward, new WanderBrain(marea, 5, 0.1f, 0.3f), marea, 200);
            Assert.AreEqual(4, w.Sims.Count);
            Vector3 start = w.Sims[0].Joints[0];
            Action update = Call(w, "Update"), late = Call(w, "LateUpdate");
            // Update reads Time.deltaTime, which edit mode may hold at 0: also tick with a real dt.
            for (int f = 0; f < 10; f++) { update(); w.TickAll(1f / 60f); late(); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 60; f++) { update(); w.TickAll(1f / 60f); late(); } });
            Assert.AreEqual(0, allocs, "managed allocations over 60 frames");
            Assert.Greater((w.Sims[0].Joints[0] - start).magnitude, 0.1f, "the sims never moved: test does not bite");
            Assert.GreaterOrEqual(w.LastSimMs, 0.0);
        }

        // The chunk manager's EnsureField replaces the physics field (and a scene reload the edit system) with
        // new objects: the world must probe the new field, drop the creatures' cached ground from the old one,
        // and listen to the new edit system only.
        [Test] public void World_PhysicsFieldRebuilt_RebindsProbeCachesAndEdits()
        {
            go = new GameObject("CreatureWorldRebindTest");
            world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = AssetDatabase.LoadAssetAtPath<Material>(kMaterialPath);
            var a = ScriptableObject.CreateInstance<PlaneField>(); trash.Add(a);
            var b = ScriptableObject.CreateInstance<PlaneField>(); trash.Add(b);
            b.height = 0.3f;
            var modsA = new TerrainModificationSystem(0.5f, 10f);
            var modsB = new TerrainModificationSystem(0.5f, 10f);
            world.BindTerrain(a, modsA);

            var sp = CreatureTestSpecies.Lizardish(); trash.Add(sp);
            var sim = world.Spawn(sp, Vector3.zero, Vector3.forward, null, null, 1);
            for (int f = 0; f < 30; f++) world.TickAll(1f / 60f);
            float clear = sp.joints[2].clearance * sim.SizeScale;
            Assert.AreEqual(clear, sim.Joints[2].y, 0.01f, "on the first field");
            Assert.AreEqual(1, Subscribers(modsA));

            world.BindTerrain(b, modsB);
            Assert.IsTrue(world.Ground.Probe(new Vector3(0f, 1f, 0f), Vector3.up, 2f, out Vector3 g, out _));
            Assert.AreEqual(0.3f, g.y, 1e-3f, "the shared probe still reads the old field");
            for (int f = 0; f < 120; f++) world.TickAll(1f / 60f);
            Assert.AreEqual(0.3f + clear, sim.Joints[2].y, 0.01f, "the creature still stands on its cached ground from the old field");
            Assert.AreEqual(0, Subscribers(modsA), "still subscribed to the old edit system");
            Assert.AreEqual(1, Subscribers(modsB), "not subscribed to the new edit system");
        }

        static int Subscribers(TerrainModificationSystem mods)
        {
            FieldInfo f = typeof(TerrainModificationSystem).GetField("RegionModified", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "RegionModified backing field");
            var d = (Delegate)f.GetValue(mods);
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        [Test] public void World_Spawn_InstanceFromSeed_WithinJitter()
        {
            var w = NewWorld();
            var sp = CreatureTestSpecies.Lizardish(); trash.Add(sp);
            sp.look.sizeJitter = 0.1f;
            var a = w.Spawn(sp, Vector3.zero, Vector3.forward, null, null, 7);
            var b = w.Spawn(sp, new Vector3(3f, 0f, 0f), Vector3.forward, null, null, 7);
            var c = w.Spawn(sp, new Vector3(6f, 0f, 0f), Vector3.forward, null, null, 8);
            Assert.AreEqual(a.SizeScale, b.SizeScale, "same seed, same individual");
            foreach (var s in new[] { a, b, c }) Assert.That(s.SizeScale, Is.InRange(0.9f, 1.1f));
            Assert.AreNotEqual(a.SizeScale, c.SizeScale, "different seeds should differ");
            Assert.DoesNotThrow(() => w.TickAll(1f / 60f)); // null brain: stands still
        }

        [Test] public void DesertMonitor_Validates()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kMonitorPath);
            Assert.IsNotNull(sp, kMonitorPath);
            Assert.IsNull(sp.Validate());
        }

        [Test] public void DesertMonitor_Is1_5mLong()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kMonitorPath);
            Assert.IsNotNull(sp, kMonitorPath);
            Assert.That(sp.BodyLength, Is.InRange(1.5f, 1.65f));
        }

        [Test] public void DebugView_BuildsLines_NoAllocAfterWarmup()
        {
            var w = NewWorld();
            var sp = CreatureTestSpecies.Lizardish(); trash.Add(sp);
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 3f, sp, 1f);
            w.Spawn(sp, Vector3.zero, Vector3.forward, new WanderBrain(area, 3), area, 1);
            var view = go.AddComponent<CreatureDebugView>();
            int n = view.BuildLines();
            int legs = sp.legs.Length, joints = sp.JointCount;
            // per leg 10 vertices: shoulder-knee, knee-ankle, ankle-foot (the leg look's raised ankle, 2026-10-19: was 8
            // with knee-foot) and the rest cross's two lines
            Assert.AreEqual(2 * (joints - 1) + 2 * 12 * joints + 10 * legs + 2 * 64, n, "chain + ellipses + legs/rest crosses + safe disc");
            Assert.AreEqual(0, n % 2, "lines are vertex pairs");
            for (int f = 0; f < 5; f++) { w.TickAll(1f / 60f); view.BuildLines(); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 60; f++) { w.TickAll(1f / 60f); view.BuildLines(); } });
            Assert.AreEqual(0, allocs, "managed allocations building the debug lines");
        }
    }
}
