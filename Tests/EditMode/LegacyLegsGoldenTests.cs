using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The multi-segment leg work (design 2026-10-17-multi-segment-legs-spider-design.md) must leave every
    // two-bone species bit-identical: the same bone matrices every tick of a weaving, tightly turning walk and
    // the same mesh. The hashes were captured on feat/creature-climbing f4bda2a, before any integration change.
    // Unity (Mono) and the offline .NET harness round transcendental functions differently, so each runtime has
    // its own constants; a 0 constant means "not captured yet" and reports the value to paste in.
    public class LegacyLegsGoldenTests
    {
        const string kDir = "Assets/TerrainGen/Config/Creatures/";
        const int kTicks = 600;

#if UNITY_EDITOR
        // Unity 6000.2.6f2 / Mono: captured on the integration merge a6337d5 (legs + octopus + winged serpent),
        // not on f4bda2a. They guard against future changes only; that the merge itself kept the legacy legs
        // bit-identical is shown by the offline harness constants below, which still match on a6337d5.
        static readonly Dictionary<string, (ulong bones, ulong mesh)> Golden = new Dictionary<string, (ulong, ulong)>
        {
            { "DesertMonitor", (0x0UL, 0x0UL) },   // capture in Unity: leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0x90BEBD53968C4A95, mesh 0xE56A6E3CC28CF98A)
            { "TestHexapod", (0x0UL, 0x0UL) },   // capture in Unity: leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0x74CBED3B3EEC2DE0, mesh 0xC9E071DE0867056F)
            { "DesertSnake", (0x3004A76C9481746FUL, 0x5022CF597CF3F50CUL) },     // rigid head over rims (R38, 2026-10-19; was 0xAB4B7C25AE2BFAA9)
            { "Lizardish", (0x0UL, 0x0UL) },   // capture in Unity: leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0xFD91F9955B3A64A2, mesh 0x139A617E5D148B58)
        };
#else
        // the offline harness (.NET 8, scratchpad legs_impl), captured on f4bda2a
        static readonly Dictionary<string, (ulong bones, ulong mesh)> Golden = new Dictionary<string, (ulong, ulong)>
        {
            { "DesertMonitor", (0x0413E426BA709E47UL, 0x5D13E2FAFEDB4D7FUL) },   // leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0x72B52B3229301DDB, mesh 0xC330116046C3CEEC)
            { "TestHexapod", (0x4146114CB4C2F943UL, 0xCC7C12EEA7B28B95UL) },   // leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0x54A1ED3EDAA99E05, mesh 0x026EBCE6D39A2C0E)
            { "DesertSnake", (0x421BED59F8301011UL, 0xAA47EE710711F7EAUL) },     // rigid head over rims (R38, 2026-10-19; was 0x5788FB67C4BAF321)
            { "Lizardish", (0xCCD2DE5C46E2BCB1UL, 0xECFC13A26B122ACEUL) },   // leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0x6CECE9DD201C6DD4, mesh 0x54AF3E0105515CDE)
        };
#endif

        const ulong kPrime = 1099511628211UL, kBasis = 14695981039346656037UL;
        static ulong F(ulong h, float f) => (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * kPrime;

        // FNV-1a over every bone matrix every tick, and the hashes after each 100 ticks (to locate a divergence).
        internal static ulong BoneHash(CreatureSpecies sp, List<ulong> blocks)
        {
            float sc = sp.scale;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sc);
            var bones = new Matrix4x4[sp.BoneCount];
            ulong h = kBasis;
            for (int t = 0; t < kTicks; t++)
            {
                float tt = t / 60f;
                sim.Tick(1f / 60f, new Vector3(2f * Mathf.Sin(0.6f * tt), 0f, 2f * Mathf.Cos(0.4f * tt) + 1f) * sc);
                sim.WriteBoneWorld(bones, 0);
                for (int b = 0; b < bones.Length; b++)
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++) h = F(h, bones[b][r, c]);
                if (t % 100 == 99) blocks?.Add(h);
            }
            return h;
        }

        internal static ulong MeshHash(CreatureSpecies sp)
        {
            Mesh mesh = CreatureMeshBuilder.Build(sp);
            ulong h = kBasis;
            foreach (var v in mesh.vertices) { h = F(h, v.x); h = F(h, v.y); h = F(h, v.z); }
            foreach (var v in mesh.normals) { h = F(h, v.x); h = F(h, v.y); h = F(h, v.z); }
            var u2 = new List<Vector2>(); var u4 = new List<Vector4>();
            for (int ch = 0; ch < 2; ch++) { mesh.GetUVs(ch, u2); foreach (var v in u2) { h = F(h, v.x); h = F(h, v.y); } }
            mesh.GetUVs(2, u4);
            foreach (var v in u4) { h = F(h, v.x); h = F(h, v.y); h = F(h, v.z); h = F(h, v.w); }
            foreach (int i in mesh.triangles) h = (h ^ (uint)i) * kPrime;
            UnityEngine.Object.DestroyImmediate(mesh);
            return h;
        }

        static void Check(string name, CreatureSpecies sp)
        {
            var blocks = new List<ulong>();
            ulong bones = BoneHash(sp, blocks), mesh = MeshHash(sp);
            var want = Golden[name];
            string got = $"{name}: bones 0x{bones:X16}UL, mesh 0x{mesh:X16}UL; 100-tick blocks {string.Join(" ", blocks.ConvertAll(b => b.ToString("X16")))}";
            TestContext.Out.WriteLine(got);
            if (want.bones == 0UL) Assert.Inconclusive("golden not captured for this runtime yet: " + got);
            Assert.AreEqual(want.bones, bones, "bone matrices changed: " + got);
            Assert.AreEqual(want.mesh, mesh, "mesh changed: " + got);
        }

        static CreatureSpecies Load(string name) => AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kDir + name + ".asset");

        [Test] public void LegacyLegs_BitIdentical_DesertMonitor() => Check("DesertMonitor", Load("DesertMonitor"));
        [Test] public void LegacyLegs_BitIdentical_TestHexapod() => Check("TestHexapod", Load("TestHexapod"));
        [Test] public void LegacyLegs_BitIdentical_DesertSnake() => Check("DesertSnake", Load("DesertSnake"));

        [Test] public void LegacyLegs_BitIdentical_Lizardish()
        {
            var sp = CreatureTestSpecies.Lizardish();
            try { Check("Lizardish", sp); }
            finally { UnityEngine.Object.DestroyImmediate(sp); }
        }
    }
}
