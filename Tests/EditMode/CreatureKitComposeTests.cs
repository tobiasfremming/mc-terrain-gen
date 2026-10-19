using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Creature kit T2.3: compose expands the authoring sugar (mirror, Repeat, gait patterns, wing arms),
    // validates and lays out bones.
    public class CreatureKitComposeTests
    {
        internal static CreatureKit.SpineBody Body(int joints) => new CreatureKit.SpineBody
        {
            id = "body", jointCount = joints, spacing = 0.05f,
            profile = new[] { new ProfileKey { t = 0f, halfWidth = 0.03f, halfHeight = 0.02f, clearance = 0.05f, bendLimitDeg = 20f },
                              new ProfileKey { t = 1f, halfWidth = 0.01f, halfHeight = 0.01f, clearance = 0.03f, bendLimitDeg = 20f } },
        };

        internal static Leg2Bone Leg(string id, int joint, bool mirror = true) => new Leg2Bone
        {
            id = id, at = new PartSocket(joint, 0, new Vector3(0.04f, -0.02f, 0f)), mirror = mirror,
            restOffset = new Vector3(0.2f, 0f, 0.05f), upperLength = 0.1f, lowerLength = 0.1f, upperRadius = 0.02f, lowerRadius = 0.015f,
            pole = new Vector3(0.6f, 0.6f, -0.9f), stepThreshold = 0.1f, stepDuration = 0.12f, stepLift = 0.03f,
        };

        static ArthropodLeg Segmented(string id)
        {
            var seg = new SegmentDef[4];
            for (int i = 0; i < 4; i++) seg[i] = new SegmentDef { length = 0.1f, radiusStart = 0.01f, radiusEnd = 0.008f, flexWeight = i == 2 ? 1f : 0.2f };
            return new ArthropodLeg { id = id, mirror = true, segments = seg, restOffset = new Vector3(0.3f, 0f, 0f), stepThreshold = 0.05f, stepDuration = 0.2f };
        }

        internal static Eyes TwoEyes(int joint = 0) => new Eyes { id = "eyes", at = new PartSocket(joint, 0, new Vector3(0.02f, 0.01f, 0.01f)), radius = 0.006f };

        static CompiledSpecies Compose(List<CreaturePart> parts, params CreatureModule[] modules) => CompiledSpecies.Compose("test", parts, modules);

        [Test]
        public void Compose_Mirror_LThenR()
        {
            Leg2Bone front = Leg("front", 4);
            var parts = new List<CreaturePart> { Body(12), front, Leg("rear", 8), TwoEyes() };
            CompiledSpecies c = Compose(parts, new Walk());
            Assert.IsNull(c.Error, c.Error);
            Assert.AreEqual("front.L front.R rear.L rear.R", string.Join(" ", c.Limbs.ConvertAll(l => l.id)));
            Assert.AreEqual(-1, c.Limbs[0].at.side); Assert.AreEqual(1, c.Limbs[1].at.side);
            Assert.IsFalse(c.Limbs[0].mirror);
            Assert.AreEqual(0, front.at.side, "the asset's part is not changed");
            Assert.IsTrue(front.mirror);
            Assert.AreNotSame(c.Limbs[0], c.Limbs[1]);
            // layout: 12 spine, 4 x 3 leg bones, then the eyes
            Assert.AreEqual(12 + 12 + 2, c.Layout.BoneCount);
            Assert.AreEqual(12 + 3, c.Layout.Start(c.Parts.IndexOf(c.Limbs[1])));
        }

        [Test]
        public void Compose_Repeat_SpreadsJointsAndTapers()
        {
            var rep = new Repeat { id = "leg", count = 5, fromJoint = 2, toJoint = 10, lastScale = 0.5f, template = Segmented("t") };
            var parts = new List<CreaturePart> { Body(14), rep, TwoEyes() };
            CompiledSpecies c = Compose(parts, new Walk { pattern = GaitPattern.Alternate });
            Assert.IsNull(c.Error, c.Error);
            Assert.AreEqual(10, c.Limbs.Count);
            Assert.AreEqual("leg0.L", c.Limbs[0].id); Assert.AreEqual("leg0.R", c.Limbs[1].id); Assert.AreEqual("leg4.R", c.Limbs[9].id);
            int[] joints = { 2, 4, 6, 8, 10 };
            for (int k = 0; k < 5; k++)
            {
                var l = (ArthropodLeg)c.Limbs[2 * k];
                Assert.AreEqual(joints[k], l.at.joint, "copy " + k);
                Assert.AreEqual(0.1f * (1f - 0.5f * k / 4f), l.segments[0].length, 1e-6f, "taper of copy " + k);
                Assert.AreEqual(0.3f * (1f - 0.5f * k / 4f), l.restOffset.x, 1e-6f);
                Assert.AreEqual(0, c.Limbs[2 * k].gaitGroup, "Alternate: left side 0"); Assert.AreEqual(1, c.Limbs[2 * k + 1].gaitGroup);
            }
            Assert.AreEqual(0.1f, ((ArthropodLeg)rep.template).segments[0].length, "the template is not changed");
            Assert.AreEqual(14 + 10 * 4 + 2, c.Layout.BoneCount);
            Assert.IsNull(c.Layout.Check());
        }

        // The patterns reproduce the shipped gait groups of every legged species asset (legs re-authored with
        // gaitGroup -1 and the pattern its leg count suits).
        // by name only (sources never load assets); legless and soft-plan species are skipped in the test
        static IEnumerable<string> LeggedAssets() => CreatureKitAssets.Species();

        internal static string GaitProblem(CreatureSpecies sp, GaitPattern pattern, int waveGroups = 4, bool waveTailToHead = false)
        {
            var parts = new List<CreaturePart> { Body(sp.JointCount), TwoEyes() };
            for (int l = 0; l < sp.legs.Length; l++)
            {
                LimbPart p = sp.legs[l].IsArthropod ? (LimbPart)ArthropodLeg.From(sp.legs[l], "leg" + l) : Leg2Bone.From(sp.legs[l], "leg" + l);
                p.gaitGroup = -1;
                parts.Add(p);
            }
            CompiledSpecies c = CompiledSpecies.Compose(sp.name, parts, new CreatureModule[] { new Walk { pattern = pattern, waveGroups = waveGroups, waveTailToHead = waveTailToHead } });
            if (c.Error != null) return c.Error;
            for (int l = 0; l < sp.legs.Length; l++)
                if (c.Limbs[l].gaitGroup != sp.legs[l].gaitGroup) return $"{sp.name} {pattern}: leg {l} group {c.Limbs[l].gaitGroup}, shipped {sp.legs[l].gaitGroup}";
            return null;
        }

        static GaitPattern PatternFor(CreatureSpecies sp)
        {
            int walking = 0;
            foreach (LegSpec l in sp.legs) if (!l.feeler) walking++;
            return walking <= 4 ? GaitPattern.Diagonal : walking <= 6 ? GaitPattern.Tripod : GaitPattern.Tetrapod;
        }

        [TestCaseSource(nameof(LeggedAssets))]
        public void Gait_Diagonal_Tripod_Tetrapod_MatchShippedGroups(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            if (sp.bodyPlan != CreatureBodyPlan.Spine || sp.legs == null || sp.legs.Length == 0) Assert.Ignore("no legs");
            // a wave walker (T5.8): its own Wave pattern, with its group count and direction, reproduces its groups
            Walk w = sp.Compiled.Module<Walk>();
            if (w != null && w.pattern == GaitPattern.Wave) { Assert.IsNull(GaitProblem(sp, GaitPattern.Wave, w.waveGroups, w.waveTailToHead), name); return; }
            Assert.IsNull(GaitProblem(sp, PatternFor(sp)), name);
        }

        // RED: Alternate (sides alternate) is not the tarantula's gait - the check bites.
        [Test]
        public void Red_Gait_AlternateOnTarantula_Fails()
        {
            string p = GaitProblem(CreatureKitAssets.Load("Tarantula"), GaitPattern.Alternate);
            TestContext.Out.WriteLine(p);
            Assert.IsNotNull(p);
        }

        [Test]
        public void Wing_ArmRefUnknown_Fails()
        {
            var parts = new List<CreaturePart> { Body(20), Leg("arm", 4), TwoEyes(), new CreatureKit.Wing { id = "wing", mirror = true, arm = "arms" } };
            CompiledSpecies c = Compose(parts, new Walk());
            StringAssert.Contains("unknown part 'arms.L'", c.Error);
            Assert.IsNull(c.Layout, "no layout for an invalid species");
        }

        [Test]
        public void Wing_Mirrored_NamesMirroredArm_AndMakesItAWingArm()
        {
            var parts = new List<CreaturePart> { Body(20), Leg("arm", 4), Leg("hind", 12), TwoEyes(), new CreatureKit.Wing { id = "wing", mirror = true, arm = "arm" } };
            CompiledSpecies c = Compose(parts, new Walk());
            Assert.IsNull(c.Error, c.Error);
            Assert.AreEqual("arm.L", c.Wings[0].arm); Assert.AreEqual("arm.R", c.Wings[1].arm);
            Assert.AreEqual(LimbRole.WingArm, c.Limbs[0].role); Assert.AreEqual(LimbRole.Walk, c.Limbs[2].role);
            Assert.AreEqual(0, c.Limbs[0].gaitGroup); Assert.AreEqual(1, c.Limbs[1].gaitGroup, "wing arms walk in the Diagonal pattern");
            Assert.AreEqual(20 + 12, c.Layout.Start(c.Parts.IndexOf(c.Wings[0])), "wings after every limb bone");
        }

        [Test]
        public void Validate_CatchesDuplicatesSecondBodyAndMissingEyes()
        {
            StringAssert.Contains("duplicate id", Compose(new List<CreaturePart> { Body(10), Leg("a", 2, false), Leg("a", 3, false), TwoEyes() }, new Walk()).Problems.Find(s => s.Contains("duplicate")) ?? "none");
            StringAssert.Contains("second Body", string.Join("|", Compose(new List<CreaturePart> { Body(10), Body(10), TwoEyes() }).Problems));
            StringAssert.Contains("Eyes", string.Join("|", Compose(new List<CreaturePart> { Body(10) }).Problems));
            StringAssert.Contains("Wave", string.Join("|", Compose(new List<CreaturePart> { Body(10), TwoEyes() }, new Walk { pattern = GaitPattern.Wave, waveGroups = 1 }).Problems), "a wave needs 2..15 groups");
            StringAssert.Contains("needs Walk", string.Join("|", Compose(new List<CreaturePart> { Body(10), TwoEyes() }, new Slither()).Problems));
        }

        // A RigidCone keeps its mirror (one part, a pair of cones, no bones), unlike the parts with bones.
        [Test]
        public void Compose_RigidCone_KeepsMirror()
        {
            var cone = new RigidCone { id = "fang", mirror = true, length = 0.02f, radiusStart = 0.004f };
            CompiledSpecies c = Compose(new List<CreaturePart> { Body(6), TwoEyes(), cone }, new Walk());
            Assert.IsNull(c.Error, c.Error);
            RigidCone got = (RigidCone)c.Find("fang");
            Assert.IsNotNull(got); Assert.IsTrue(got.mirror);
            Assert.AreEqual(6 + 2, c.Layout.BoneCount);
        }
    }
}
