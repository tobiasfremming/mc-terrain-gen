using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Creature kit T1.1 (design §2.2): the generic bone-layout registry. Built from the legacy fields it must
    // give exactly the indices the hand-written formulas gave (spine, legs by SegmentCount, 6 per wing, 2 eyes),
    // for every species asset and every test factory, so every golden hash stays put.
    public class CreatureBoneLayoutTests
    {
        // The legacy formulas, transcribed from CreatureSpecies / LegBoneLayout / WingBoneLayout before T1.1 (a wing:
        // WingBoneLayout.BonesPerWing bones, 7 since the humerus patch bone; antennae appended after the eyes).
        static int LegacyLegBones(CreatureSpecies sp)
        {
            int c = 0;
            if (sp.legs != null) foreach (LegSpec l in sp.legs) c += l != null ? l.SegmentCount : 3;
            return c;
        }
        static int LegacyLegStart(CreatureSpecies sp, int leg)
        {
            int c = 0;
            for (int i = 0; i < leg; i++) c += sp.legs[i] != null ? sp.legs[i].SegmentCount : 3;
            return c;
        }
        // antennae (kit T5.6) append one bone per segment after the eyes; no legacy index moves
        static int AntennaBones(CreatureSpecies sp)
        {
            int c = 0;
            for (int a = 0; a < sp.AntennaCount; a++) c += sp.antennae[a].Segments;
            return c;
        }
        static int LegacyBoneCount(CreatureSpecies sp) => sp.JointCount + LegacyLegBones(sp) + WingBoneLayout.BonesPerWing * sp.WingCount + 2 + AntennaBones(sp);
        static int LegacyLegBone(CreatureSpecies sp, int leg, int part) => sp.JointCount + LegacyLegStart(sp, leg) + part;
        static int LegacyWingBone(CreatureSpecies sp, int w, int k) => sp.JointCount + LegacyLegBones(sp) + WingBoneLayout.BonesPerWing * w + k;
        static int LegacyEyeBone(CreatureSpecies sp, int e) => sp.JointCount + LegacyLegBones(sp) + WingBoneLayout.BonesPerWing * sp.WingCount + e;

        // null when the layout reproduces every legacy index
        internal static string LegacyProblem(CreatureSpecies sp, CreatureBoneLayout layout)
        {
            if (layout.BoneCount != LegacyBoneCount(sp)) return $"BoneCount {layout.BoneCount} != legacy {LegacyBoneCount(sp)}";
            int legs = sp.legs != null ? sp.legs.Length : 0;
            for (int l = 0; l < legs; l++)
                for (int k = 0; k < (sp.legs[l] != null ? sp.legs[l].SegmentCount : 3); k++)
                    if (layout.LegBone(l, k) != LegacyLegBone(sp, l, k)) return $"leg {l} bone {k}: {layout.LegBone(l, k)} != legacy {LegacyLegBone(sp, l, k)}";
            for (int w = 0; w < sp.WingCount; w++)
                for (int k = 0; k < WingBoneLayout.BonesPerWing; k++)
                    if (layout.WingBone(w, k) != LegacyWingBone(sp, w, k)) return $"wing {w} bone {k}: {layout.WingBone(w, k)} != legacy {LegacyWingBone(sp, w, k)}";
            for (int e = 0; e < 2; e++)
                if (layout.EyeBone(e) != LegacyEyeBone(sp, e)) return $"eye {e}: {layout.EyeBone(e)} != legacy {LegacyEyeBone(sp, e)}";
            return layout.Check();
        }

        // the species accessors and the registry agree with the legacy formulas
        static void AssertLegacy(CreatureSpecies sp, string what)
        {
            Assert.IsNull(LegacyProblem(sp, CreatureBoneLayout.FromLegacy(sp)), what + " (registry)");
            Assert.AreEqual(LegacyBoneCount(sp), sp.BoneCount, what + " BoneCount");
            int legs = sp.legs != null ? sp.legs.Length : 0;
            for (int l = 0; l < legs; l++) Assert.AreEqual(LegacyLegBone(sp, l, 1), sp.LegBone(l, 1), what + " LegBone " + l);
            for (int w = 0; w < sp.WingCount; w++) Assert.AreEqual(LegacyWingBone(sp, w, 5), sp.WingBone(w, 5), what + " WingBone " + w);
            Assert.AreEqual(LegacyEyeBone(sp, 1), sp.EyeBone(1), what + " EyeBone");
        }

        // every asset by name (sources never load assets: discovery must not reach engine code); the tests skip
        // the soft-plan (octopus) assets, whose layout is OctopusBody's (CreatureKitAdapterTests pins it)
        static IEnumerable<string> SpineAssets() => CreatureKitAssets.Species();

        static CreatureSpecies Spine(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            if (sp.bodyPlan != CreatureBodyPlan.Spine) Assert.Ignore("cephalopod: OctopusBody's layout");
            return sp;
        }

        [TestCaseSource(nameof(SpineAssets))]
        public void Layout_Legacy_EqualsRegistry_Asset(string name) => AssertLegacy(Spine(name), name);

        [Test]
        public void Layout_Legacy_EqualsRegistry_Factories()
        {
            var made = new List<CreatureSpecies> { CreatureTestSpecies.Lizardish(), CreatureTestSpecies.Snake(), CreatureTestSpecies.Hexapod(), WingedSerpentTestSpecies.Create().species };
            try { foreach (CreatureSpecies sp in made) AssertLegacy(sp, sp.name); }
            finally { foreach (CreatureSpecies sp in made) Object.DestroyImmediate(sp); }
        }

        // Legs + wyvern merged: a winged species whose hind legs are arthropod legs. The wings start after the
        // summed leg bones (3 + 3 + 4 + 4), the eyes after the wings.
        [Test]
        public void Layout_Hybrid_WyvernWithArthropodHindLegs()
        {
            CreatureSpecies sp = WingedSerpentTestSpecies.Create().species;
            try
            {
                for (int l = 2; l < 4; l++)
                    sp.legs[l].segments = new[]
                    {
                        new LegSegment { length = 0.1f, flexWeight = 0.3f }, new LegSegment { length = 0.2f, flexWeight = 1f },
                        new LegSegment { length = 0.2f, flexWeight = -0.5f }, new LegSegment { length = 0.05f },
                    };
                sp.InvalidateLayout();
                AssertLegacy(sp, "hybrid");
                int n = WingedSerpentTestSpecies.Joints;
                Assert.AreEqual(n + 3, sp.LegBone(1, 0));
                Assert.AreEqual(n + 6 + 4, sp.LegBone(3, 0));
                Assert.AreEqual(n + 14, sp.WingBone(0, 0), "wings after the summed leg bones");
                Assert.AreEqual(n + 14 + 2 * WingBoneLayout.BonesPerWing, sp.EyeBone(0));
                Assert.AreEqual(n + 14 + 2 * WingBoneLayout.BonesPerWing + 2, sp.BoneCount);
            }
            finally { Object.DestroyImmediate(sp); }
        }

        // The ranges tile the bones, in block order, for every asset.
        [TestCaseSource(nameof(SpineAssets))]
        public void Layout_Contiguous(string name)
        {
            CreatureBoneLayout layout = Spine(name).Layout;
            Assert.IsNull(layout.Check(), name);
            Assert.AreEqual(PartKind.Body, layout.Ranges[0].Kind, name);
            int last = layout.Ranges.Length - 1;
            while (last > 0 && layout.Ranges[last].Kind == PartKind.Chain) last--;   // antennae come after the legacy blocks
            Assert.AreEqual(PartKind.Sense, layout.Ranges[last].Kind, name + ": eyes last among the legacy blocks");
        }

        // A generic layout: block order, not request order, decides; bone-less parts take no bones.
        [Test]
        public void Layout_Generic_BlockOrder()
        {
            var blocks = new[]
            {
                new BoneBlock(PartKind.Sense, "eyes", 2), new BoneBlock(PartKind.Fin, "dorsal", 0),
                new BoneBlock(PartKind.Limb, "leg.L", 3), new BoneBlock(PartKind.Body, "body", 10), new BoneBlock(PartKind.Chain, "antenna", 4),
            };
            var layout = new CreatureBoneLayout(blocks, CreatureBoneLayout.SpineOrder);
            Assert.AreEqual(19, layout.BoneCount);
            Assert.AreEqual(0, layout.Start(3)); Assert.AreEqual(10, layout.Start(2)); Assert.AreEqual(13, layout.Start(0)); Assert.AreEqual(15, layout.Start(4));
            Assert.AreEqual(4, layout.Ranges.Length, "the bone-less fin has no range");
            Assert.IsNull(layout.Check());
            var soft = new CreatureBoneLayout(blocks, CreatureBoneLayout.SoftOrder);
            Assert.AreEqual(0, soft.Start(4), "soft plan: chains first");
        }

        // RED: the registry with the wings before the legs no longer reproduces the winged serpent's indices.
        [Test]
        public void Red_WingsBeforeLegs_BreaksLegacy()
        {
            CreatureSpecies sp = WingedSerpentTestSpecies.Create().species;
            try
            {
                var wrong = new[] { PartKind.Body, PartKind.Wing, PartKind.Limb, PartKind.Sense };
                string p = LegacyProblem(sp, CreatureBoneLayout.FromLegacy(sp, wrong));
                TestContext.Out.WriteLine(p);
                Assert.IsNotNull(p, "a wrong block order must be caught");
            }
            finally { Object.DestroyImmediate(sp); }
        }
    }
}
