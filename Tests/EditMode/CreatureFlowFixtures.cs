using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Fixtures for kit-part tests: build a kit species in code, tick a sim, read the bones the renderer would skin
    // with, and count triangles that turn inside out when posed. Reuses CreaturePteroWingTests.Skinned.
    internal static class FlowKit
    {
        internal const float kDt = 1f / 60f;

        internal static CreatureSpecies Build(string name, float scale, IEnumerable<CreaturePart> parts, IEnumerable<CreatureModule> modules)
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = name; sp.scale = scale; sp.kitVersion = 1;
            sp.parts = new List<CreaturePart>(parts); sp.modules = new List<CreatureModule>(modules);
            CompiledSpecies c = sp.Compiled;
            if (c.Error != null) throw new AssertionException($"FlowKit.Build({name}): {c.Error}");
            return c.Runtime;
        }

        internal static CreatureSim Sim(CreatureSpecies sp, float sizeScale, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sizeScale, seed);

        internal static Matrix4x4[] Bones(CreatureSim sim)
        {
            var w = new Matrix4x4[sim.Species.BoneCount];
            sim.WriteBoneWorld(w, 0);
            return w;
        }

        internal static int InvertedTriangles(CreatureSpecies sp, CreatureSim sim, out string where)
        {
            var m = new CreaturePteroWingTests.Skinned(sp);
            m.Pose(sim);
            return m.Inverted(-1, out where);
        }

        internal static Vector3[] SkinnedVertices(CreatureSpecies sp, CreatureSim sim, int part = -1)
        {
            var m = new CreaturePteroWingTests.Skinned(sp);
            m.Pose(sim);
            var o = new List<Vector3>();
            for (int i = 0; i < m.P.Length; i++) if (part < 0 || m.Part(i) == part) o.Add(m.P[i]);
            return o.ToArray();
        }

        internal static void Run(CreatureSim sim, float seconds, Vector3 target)
        {
            int n = Mathf.CeilToInt(seconds / kDt);
            for (int i = 0; i < n; i++) sim.Tick(kDt, target);
        }

        internal static Mesh Mesh(CreatureSpecies sp) => CreatureKitAssets.BuildMesh(sp);
    }

    public class CreatureFlowFixturesTests
    {
        [Test] public void FlowKit_BuildsAKitSpecies()
        {
            var parts = new List<CreaturePart> { CreatureKitComposeTests.Body(8), CreatureKitComposeTests.Leg("leg", 3), CreatureKitComposeTests.TwoEyes() };
            CreatureSpecies sp = FlowKit.Build("FlowFixture", 1f, parts, new CreatureModule[] { new Walk() });
            Assert.IsNull(sp.Validate());
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            FlowKit.Run(sim, 1f, new Vector3(0f, 0f, 2f));
            Assert.AreEqual(sp.BoneCount, FlowKit.Bones(sim).Length);
            Assert.AreEqual(0, FlowKit.InvertedTriangles(sp, sim, out string where), where);
            Assert.Greater(FlowKit.SkinnedVertices(sp, sim).Length, 0);
        }
    }
}
