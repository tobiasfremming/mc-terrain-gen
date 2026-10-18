using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit T2.5: the kit format (kitVersion 1, [SerializeReference] parts + modules, sugar expanded at
    // load). KitMonitor.asset is DesertMonitor converted (`CreatureHarness kit-convert DesertMonitor KitMonitor`:
    // two mirrored legs, the Diagonal pattern) and must run bit-identically to it.
    public class CreatureKitFormatTests
    {
        const string kKit = "KitMonitor", kLegacy = "DesertMonitor";

        // Every asset's [SerializeReference] objects resolve to a kit type, and every list entry has its object.
        [TestCaseSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
        public void Kit_NoMissingTypes(string name)
        {
            string[] lines = System.IO.File.ReadAllLines(CreatureKitAssets.FullPath(CreatureKitAssets.PathOf(name)));
            List<string> p = CreatureKitAssets.KitYamlProblems(lines).FindAll(s => s.Contains("missing type") || s.Contains("RefIds") || s.Contains("null entry"));
            Assert.IsEmpty(p, string.Join("\n", p));
            CreatureSpecies a = CreatureKitAssets.LoadAsset(name);
            if (a.kitVersion > 0)
            {
                Assert.Greater(a.parts.Count, 0, name + ": a kit species with no parts");
                foreach (CreaturePart part in a.parts) Assert.IsNotNull(part, name + ": a part did not load (missing type)");
                foreach (CreatureModule m in a.modules) Assert.IsNotNull(m, name + ": a module did not load (missing type)");
            }
        }

        // Every key in the RefIds data is a field of its class: a misspelt parameter would silently read 0.
        [TestCaseSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
        public void Kit_YamlKeysAreKnownFields(string name)
        {
            string[] lines = System.IO.File.ReadAllLines(CreatureKitAssets.FullPath(CreatureKitAssets.PathOf(name)));
            List<string> p = CreatureKitAssets.KitYamlProblems(lines);
            Assert.IsEmpty(p, string.Join("\n", p));
        }

        static string[] KitText() => System.IO.File.ReadAllLines(CreatureKitAssets.FullPath(CreatureKitAssets.PathOf(kKit)));

        // RED: a typo in a part's key, a nested struct key and a module key are each caught; a renamed class is a
        // missing type.
        [Test]
        public void Red_Kit_YamlTypoAndMissingType_Caught()
        {
            string[] good = KitText();
            Assert.IsEmpty(CreatureKitAssets.KitYamlProblems(good), "the fixture itself is clean");
            string[] typo = (string[])good.Clone();
            int a = Array.FindIndex(typo, l => l.Trim() == "upperLength: 0.145");
            int b = Array.FindIndex(typo, l => l.Trim().StartsWith("halfHeight:"));
            int c = Array.FindIndex(typo, l => l.Trim().StartsWith("turnRateDeg:"));
            typo[a] = typo[a].Replace("upperLength", "upperLenght");
            typo[b] = typo[b].Replace("halfHeight", "halfHieght");
            typo[c] = typo[c].Replace("turnRateDeg", "turnRate");
            List<string> p = CreatureKitAssets.KitYamlProblems(typo);
            TestContext.Out.WriteLine(string.Join("\n", p));
            Assert.IsTrue(p.Exists(s => s.Contains("'upperLenght' in Leg2Bone")), "part key");
            Assert.IsTrue(p.Exists(s => s.Contains("'halfHieght' in JointDef")), "nested struct key");
            Assert.IsTrue(p.Exists(s => s.Contains("'turnRate' in Walk")), "module key");
            string[] renamed = (string[])good.Clone();
            int t = Array.FindIndex(renamed, l => l.Contains("class: Eyes,"));
            renamed[t] = renamed[t].Replace("class: Eyes,", "class: EyePair,");
            Assert.IsTrue(CreatureKitAssets.KitYamlProblems(renamed).Exists(s => s.Contains("missing type CreatureKit.EyePair")));
        }

        // The converted monitor: kitVersion 1 composes; its runtime view has the legacy monitor's fields, and 600
        // ticks of bones and the mesh hash-equal (and so equal the LegacyLegsGoldenTests constants).
        [Test]
        public void Converted_Monitor_EqualsLegacy()
        {
            CreatureSpecies kit = CreatureKitAssets.LoadAsset(kKit), legacy = CreatureKitAssets.LoadAsset(kLegacy);
            Assert.AreEqual(1, kit.kitVersion);
            Assert.IsNull(kit.Validate(), kit.Validate());
            Assert.AreEqual(0, kit.legs?.Length ?? 0, "the asset holds no legacy legs: they come from the parts");
            CompiledSpecies c = kit.Compiled;
            Assert.AreEqual(4, c.Limbs.Count, "two mirrored legs");
            Assert.AreEqual("leg0.L leg0.R leg2.L leg2.R", string.Join(" ", c.Limbs.ConvertAll(l => l.id)));
            CreatureSpecies rt = c.Runtime;
            Assert.AreNotSame(kit, rt);
            Assert.IsNull(KitCompare.FirstDifference(legacy, rt, CreatureKitAdapterTests.Unused(legacy)), "runtime fields");
            Assert.AreEqual(legacy.BoneCount, kit.BoneCount, "the asset's BoneCount comes from the compiled layout");
            Assert.AreEqual(LegacyLegsGoldenTests.BoneHash(legacy, null), LegacyLegsGoldenTests.BoneHash(kit, null), "600 ticks of bone matrices");
            Assert.AreEqual(LegacyLegsGoldenTests.MeshHash(legacy), LegacyLegsGoldenTests.MeshHash(kit), "mesh");
        }

        // The runtime view is rebuilt in place (identity kept for the renderer) when the asset is recompiled.
        [Test]
        public void Kit_Recompile_ReusesRuntimeView()
        {
            CreatureSpecies kit = CreatureKitAssets.LoadAsset(kKit);
            CreatureSpecies first = kit.Compiled.Runtime;
            kit.InvalidateCompiled();
            Assert.AreSame(first, kit.Compiled.Runtime);
            Assert.AreEqual(kit.scale, first.scale);
        }

        // A kit species that does not compose reports it through Validate (CreatureWorld.Spawn refuses it).
        [Test]
        public void Kit_ComposeError_FailsValidate()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            try
            {
                sp.name = "Broken"; sp.kitVersion = 1;
                sp.parts.Add(CreatureKitComposeTests.Body(10));
                sp.parts.Add(new CreatureKit.Wing { id = "wing", arm = "nope" });
                sp.modules.Add(new Walk());
                StringAssert.Contains("unknown part 'nope'", sp.Validate());
            }
            finally { Object.DestroyImmediate(sp); }
        }
    }
}
