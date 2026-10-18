using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit T2.4: a legacy species through the kit. sp.Compiled.Runtime is the asset itself (the runtime
    // reads the same objects - the goldens prove it stays bit-identical); its parts describe it exactly: the round
    // trip legacy -> parts -> compose -> legacy fields gives the same fields and the same bones and mesh, with and
    // without the mirror / gait-pattern sugar.
    public class CreatureKitAdapterTests
    {
        static IEnumerable<string> Assets() => CreatureKitAssets.Species();

        static List<CreatureSpecies> Factories() => new List<CreatureSpecies>
        {
            CreatureTestSpecies.Lizardish(), CreatureTestSpecies.Snake(), CreatureTestSpecies.Hexapod(), WingedSerpentTestSpecies.Create().species,
        };

        // Fields the runtime never reads in that configuration (documented, so a lossy adapter cannot hide here).
        internal static Func<string, bool> Unused(CreatureSpecies sp) => path =>
        {
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return path != "cephalopod" && !path.StartsWith("cephalopod.") && path != "look" && !path.StartsWith("look.") && path != "bodyPlan" && path != "scale";
            if (path == "cephalopod" || path.StartsWith("cephalopod.")) return true;                 // spine plan: unused
            if (path == "undulationHz") return sp.undulationDeg == 0f;                                  // no wave
            if (path.StartsWith("climb.")) return !sp.climb.canClimb;                                   // not a climber
            if (path == "flight" || path.StartsWith("flight.")) return sp.WingCount == 0;               // no wings
            if (path.StartsWith("legs[") && path.IndexOf("].") > 0)
            {
                int l = int.Parse(path.Substring(5, path.IndexOf(']') - 5));
                string f = path.Substring(path.IndexOf(']') + 2);
                bool arthropod = sp.legs[l].IsArthropod;
                if (arthropod) return f == "upperLength" || f == "lowerLength" || f == "upperRadius" || f == "lowerRadius" || f.StartsWith("pole") || f == "kneeBlend";
                return f == "contactAngleDeg" || f == "kneeLiftDeg" || f == "swingRetract" || f == "elevMinDeg" || f == "elevMaxDeg"
                       || f == "liftDelay" || f == "kneeJoint" || f == "legSides";
            }
            return false;
        };

        static CreatureSpecies RoundTrip(CreatureSpecies sp, bool fold)
        {
            List<CreaturePart> parts = LegacySpeciesAdapter.ToParts(sp, fold, out List<CreatureModule> modules);
            CompiledSpecies c = CompiledSpecies.Compose(sp.name, parts, modules);
            Assert.IsNull(c.Error, sp.name + (fold ? " folded" : "") + ": " + c.Error);
            var rt = ScriptableObject.CreateInstance<CreatureSpecies>();
            rt.name = sp.name; rt.scale = sp.scale;
            c.WriteLegacyFields(rt);
            return rt;
        }

        static void AssertRoundTrip(CreatureSpecies sp)
        {
            foreach (bool fold in new[] { false, true })
            {
                CreatureSpecies rt = RoundTrip(sp, fold);
                try
                {
                    string what = sp.name + (fold ? " (mirror/pattern sugar)" : "");
                    Assert.IsNull(KitCompare.FirstDifference(sp, rt, Unused(sp)), what + ": fields");
                    if (sp.bodyPlan != CreatureBodyPlan.Spine) continue;
                    Assert.AreEqual(LegacyLegsGoldenTests.BoneHash(sp, null), LegacyLegsGoldenTests.BoneHash(rt, null), what + ": 600 ticks of bones");
                    Assert.AreEqual(LegacyLegsGoldenTests.MeshHash(sp), LegacyLegsGoldenTests.MeshHash(rt), what + ": mesh");
                }
                finally { Object.DestroyImmediate(rt); }
            }
        }

        [TestCaseSource(nameof(Assets))]
        public void Adapter_Compiled_EqualsLegacyFields(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            CompiledSpecies c = sp.Compiled;
            Assert.IsNull(c.Error, name + ": " + c.Error);
            Assert.AreSame(sp, c.Runtime, "a legacy asset is its own runtime view (the same objects)");
            if (sp.bodyPlan == CreatureBodyPlan.Spine)
            {
                // the compiled parts lay out the same bones as the legacy fields
                Assert.IsNull(CreatureBoneLayoutTests.LegacyProblem(sp, c.Layout), name + " layout");
                Assert.AreEqual(sp.legs?.Length ?? 0, c.Limbs.Count);
                for (int l = 0; l < c.Limbs.Count; l++) Assert.AreEqual(sp.legs[l].gaitGroup, c.Limbs[l].gaitGroup, "gait group of leg " + l);
            }
            else Assert.AreEqual(OctopusBody.BoneCountFor(sp.cephalopod), c.Layout.BoneCount, name + ": the soft-plan registry = OctopusBody's layout");
            AssertRoundTrip(sp);
        }

        [Test]
        public void Adapter_Compiled_EqualsLegacyFields_Factories()
        {
            List<CreatureSpecies> made = Factories();
            try
            {
                foreach (CreatureSpecies sp in made)
                {
                    Assert.IsNull(sp.Compiled.Error, sp.name + ": " + sp.Compiled.Error);
                    Assert.AreSame(sp, sp.Compiled.Runtime);
                    AssertRoundTrip(sp);
                }
            }
            finally { foreach (CreatureSpecies sp in made) Object.DestroyImmediate(sp); }
        }

        // The sugar is used where it reproduces the asset: the monitor folds to two mirrored legs with the
        // Diagonal pattern; the tarantula keeps its Tetrapod groups.
        [Test]
        public void Adapter_Fold_UsesMirrorAndPattern()
        {
            List<CreaturePart> parts = LegacySpeciesAdapter.ToParts(CreatureKitAssets.Load("DesertMonitor"), true, out List<CreatureModule> mods);
            int mirrored = 0;
            foreach (CreaturePart p in parts) if (p is LimbPart l) { Assert.IsTrue(l.mirror, l.id); Assert.AreEqual(-1, l.gaitGroup); mirrored++; }
            Assert.AreEqual(2, mirrored);
            Assert.AreEqual(GaitPattern.Diagonal, mods.Find(m => m is Walk) is Walk w ? w.pattern : GaitPattern.Explicit);
            Assert.IsNotNull(mods.Find(m => m is CreatureKit.Climb), "the monitor climbs");
            Assert.IsNotNull(mods.Find(m => m is ClimbExcursion));

            LegacySpeciesAdapter.ToParts(CreatureKitAssets.Load("Tarantula"), true, out mods);
            Assert.AreEqual(GaitPattern.Tetrapod, ((Walk)mods.Find(m => m is Walk)).pattern);
        }

        // RED: a lossy adapter is caught - dropping one leg's stepLift changes the fields and the bones.
        [Test]
        public void Red_Adapter_LossyLeg_Caught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("DesertMonitor");
            CreatureSpecies rt = RoundTrip(sp, false);
            try
            {
                rt.legs[2].stepLift *= 0.5f;
                string d = KitCompare.FirstDifference(sp, rt, Unused(sp));
                TestContext.Out.WriteLine(d);
                Assert.IsNotNull(d);
                StringAssert.Contains("legs[2].stepLift", d);
                Assert.AreNotEqual(LegacyLegsGoldenTests.BoneHash(sp, null), LegacyLegsGoldenTests.BoneHash(rt, null));
            }
            finally { Object.DestroyImmediate(rt); }
        }
    }
}
