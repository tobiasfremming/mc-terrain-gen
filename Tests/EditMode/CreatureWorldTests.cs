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
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 60; f++) { update(); w.TickAll(1f / 60f); late(); }
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, delta, "bytes allocated over 60 frames");
            Assert.Greater((w.Sims[0].Joints[0] - start).magnitude, 0.1f, "the sims never moved: test does not bite");
            Assert.GreaterOrEqual(w.LastSimMs, 0.0);
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
            Assert.AreEqual(2 * (joints - 1) + 2 * 12 * joints + 8 * legs + 2 * 64, n, "chain + ellipses + legs/rest crosses + safe disc");
            Assert.AreEqual(0, n % 2, "lines are vertex pairs");
            for (int f = 0; f < 5; f++) { w.TickAll(1f / 60f); view.BuildLines(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 60; f++) { w.TickAll(1f / 60f); view.BuildLines(); }
            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated building the debug lines");
        }
    }
}
