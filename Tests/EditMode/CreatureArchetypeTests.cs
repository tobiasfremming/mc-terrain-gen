using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The kit's body-plan archetypes (Kit/Archetypes): ArthropodPlan (Spider, Crab) expands at compose into ordinary
    // parts and default modules from a few anatomical numbers, using the kit services (RestStance: feet and body height
    // from the leg's rest pose; GaitDefaults: steps as fractions of the leg, times in Froude units). Checks: the spider
    // form is the legacy tarantula ported (parity: shape, rest stance, gait); every generated walking leg stands in its
    // own rest shape (s ~ 0, pitch ~ 0); the override path (plan numbers -> an explicit part with a generated id,
    // mirrored or one side -> added parts; an explicit module replaces the default of its type); validation never
    // throws (legPairs 1 / 6, a formula of the wrong length, posture at its bounds); a generated Tuck stays last.
    public class CreatureArchetypeTests
    {
        // A species made from parts and modules in code (kitVersion 1), its runtime view.
        internal static CreatureSpecies Make(string name, IEnumerable<CreaturePart> parts, IEnumerable<CreatureModule> modules = null, float scale = 1f)
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = name; sp.scale = scale; sp.kitVersion = 1;
            sp.parts = new List<CreaturePart>(parts);
            sp.modules = modules != null ? new List<CreatureModule>(modules) : new List<CreatureModule>();
            return sp;
        }

        static ArthropodPlan Spider() => new ArthropodPlan { id = "plan", form = ArthropodForm.Spider, bodyLength = 0.41f };
        static ArthropodPlan Crab() => new ArthropodPlan { id = "plan", form = ArthropodForm.Crab, bodyLength = 0.3f };

        static CompiledSpecies Compose(params CreaturePart[] parts) => CompiledSpecies.Compose("t", new List<CreaturePart>(parts), new List<CreatureModule>());

        // ------------------------------------------------------------ parity: the spider form is the tarantula

        [Test] public void SpiderForm_IsTheTarantula()
        {
            CreatureSpecies a = Make("ArchSpider", new[] { Spider() }).Compiled.Runtime, t = CreatureKitAssets.Load("Tarantula");
            Assert.AreEqual(t.legs.Length, a.legs.Length, "legs");
            Assert.AreEqual(t.JointCount, a.JointCount, "joints");
            for (int i = 0; i < t.JointCount; i++)
            {
                Assert.AreEqual(t.joints[i].halfWidth, a.joints[i].halfWidth, 0.002f, $"joint {i} halfWidth");
                Assert.AreEqual(t.joints[i].clearance, a.joints[i].clearance, 0.004f, $"joint {i} clearance");
            }
            Assert.AreEqual(t.subSteps, a.subSteps, "sub-steps");
            Assert.AreEqual(t.speed, a.speed, 0.05f * t.speed, "cruise speed (Froude)");
            Assert.AreEqual(t.turnRateDeg, a.turnRateDeg, 1e-3f, "turn rate");
            for (int l = 0; l < t.legs.Length; l++)
            {
                LegSpec p = a.legs[l], q = t.legs[l];
                Assert.AreEqual(q.feeler, p.feeler, $"leg {l} feeler");
                Assert.AreEqual(q.attachJoint, p.attachJoint, $"leg {l} attach");
                Assert.AreEqual(q.gaitGroup, p.gaitGroup, $"leg {l} gait group");
                Assert.AreEqual(q.segments.Length, p.segments.Length, $"leg {l} segments");
                for (int s = 0; s < q.segments.Length; s++)
                {
                    Assert.AreEqual(q.segments[s].length, p.segments[s].length, 0.03f * q.segments[s].length + 1e-4f, $"leg {l} seg {s} length");
                    Assert.AreEqual(q.segments[s].restBendDeg, p.segments[s].restBendDeg, 1e-3f, $"leg {l} seg {s} rest");
                }
                float L = q.TotalLength;
                Assert.AreEqual(0f, (p.restOffset - q.restOffset).magnitude, 0.05f * L, $"leg {l} rest point");
                if (q.feeler) continue;
                // the gait, number for number (fractions of the leg, one Froude step time)
                Assert.AreEqual(q.stepDuration, p.stepDuration, 0.02f * q.stepDuration, $"leg {l} step time");
                Assert.AreEqual(q.stepLift, p.stepLift, 0.05f * q.stepLift, $"leg {l} lift");
                Assert.AreEqual(q.stepThreshold, p.stepThreshold, 0.05f * q.stepThreshold, $"leg {l} threshold");
                Assert.AreEqual(q.overshoot, p.overshoot, 0.05f * q.overshoot, $"leg {l} overshoot");
                Assert.AreEqual(q.footLength, p.footLength, 0.1f * q.footLength, $"leg {l} foot");
                // the rest stance through the real IK: the same leg shape standing
                RestReport rp = RestStance.Report(a, l), rq = RestStance.Report(t, l);
                Assert.AreEqual(rq.s, rp.s, 0.5f, $"leg {l} rest synergy");
                Assert.AreEqual(rq.pitchDeg, rp.pitchDeg, 3f, $"leg {l} rest pitch");
            }
        }

        // The rest stance through the real IK: the reach synergy stays at its rest (s ~ 0) and the leg plane is barely
        // pitched, so what stands is the leg shape the family template authors.
        [TestCase(ArthropodForm.Spider, 0f), TestCase(ArthropodForm.Crab, 0f), TestCase(ArthropodForm.Crab, 0.8f), TestCase(ArthropodForm.Crab, 1.6f)]
        public void EveryForm_LegsStandInTheirRestShape(ArthropodForm form, float legLength)
        {
            var plan = new ArthropodPlan { id = "plan", form = form, bodyLength = form == ArthropodForm.Crab ? 0.3f : 0.41f, legLength = legLength };
            CreatureSpecies a = Make("rest", new[] { plan });
            Assert.IsNull(a.Compiled.Error, a.Compiled.Error);
            CreatureSpecies sp = a.Compiled.Runtime;
            int walking = 0;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                if (sp.legs[l].feeler || !sp.legs[l].IsArthropod) continue;
                walking++;
                RestReport r = RestStance.Report(sp, l);
                Assert.IsFalse(r.clamped, $"leg {l} out of reach at rest");
                Assert.Less(Mathf.Abs(r.s), 0.5f, $"leg {l}: reach synergy at rest");
                Assert.Less(Mathf.Abs(r.pitchDeg), 5f, $"leg {l}: leg plane pitch at rest");
            }
            Assert.Greater(walking, 0);
        }

        // ------------------------------------------------------------ the override path

        // An explicit part with a mirrored leg's base id replaces both generated sides; the other parts and the
        // generated modules stay; an explicit module replaces the generated default of its type.
        [Test] public void ExplicitMirroredId_ReplacesBothSides()
        {
            CompiledSpecies baseline = Compose(Crab());
            Assert.IsNull(baseline.Error);
            var mine = (ArthropodLeg)baseline.Find("leg1.L").Clone();
            mine.id = "leg1"; mine.mirror = true; mine.at.side = 0;
            for (int i = 0; i < mine.segments.Length; i++) mine.segments[i].radiusStart *= 2f;
            var skin = new Skin { dorsal = new Color(0.1f, 0.2f, 0.9f) };
            CompiledSpecies c = CompiledSpecies.Compose("t", new List<CreaturePart> { Crab(), mine }, new List<CreatureModule> { skin });
            Assert.IsNull(c.Error);
            Assert.AreEqual(baseline.Parts.Count, c.Parts.Count, "same parts, one pair replaced");
            float r0 = ((ArthropodLeg)baseline.Find("leg1.L")).segments[2].radiusStart;
            Assert.AreEqual(2f * r0, ((ArthropodLeg)c.Find("leg1.L")).segments[2].radiusStart, 1e-6f);
            Assert.AreEqual(2f * r0, ((ArthropodLeg)c.Find("leg1.R")).segments[2].radiusStart, 1e-6f);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.9f), c.Module<Skin>().dorsal);
            int skins = 0; foreach (CreatureModule m in c.Modules) if (m is Skin) skins++;
            Assert.AreEqual(1, skins, "one Skin");
            Assert.IsNotNull(c.Module<Walk>(), "the generated Walk stays");
            Assert.AreEqual("plan", c.GeneratedBy("leg0.L"));
            Assert.IsNull(c.GeneratedBy("leg1.L"), "an explicit part is not generated");
        }

        // An explicit part with one side's id replaces only that side.
        [Test] public void ExplicitSideId_ReplacesOneSide()
        {
            CompiledSpecies baseline = Compose(Crab());
            var mine = (ArthropodLeg)baseline.Find("leg1.R").Clone();
            for (int i = 0; i < mine.segments.Length; i++) mine.segments[i].radiusStart *= 2f;
            CompiledSpecies c = Compose(Crab(), mine);
            Assert.IsNull(c.Error);
            Assert.AreEqual(baseline.Parts.Count, c.Parts.Count);
            float r0 = ((ArthropodLeg)baseline.Find("leg1.L")).segments[2].radiusStart;
            Assert.AreEqual(r0, ((ArthropodLeg)c.Find("leg1.L")).segments[2].radiusStart, 1e-6f, "the left stays generated");
            Assert.AreEqual(2f * r0, ((ArthropodLeg)c.Find("leg1.R")).segments[2].radiusStart, 1e-6f, "the right is the explicit one");
        }

        // Added parts are layered on (a spine field on the generated carapace).
        [Test] public void AddedPart_IsLayeredOn()
        {
            var teeth = new SpineField { id = "teeth", mirror = true, loft = "carapace", count = 4, tMin = 0.5f, tMax = 0.8f, lengthMin = 0.01f, lengthMax = 0.012f, baseRatio = 0.5f };
            CompiledSpecies c = Compose(Crab(), teeth);
            Assert.IsNull(c.Error, c.Error);
            Assert.IsNotNull(c.Find("teeth.L"));
            Assert.IsNull(c.GeneratedBy("teeth.L"));
        }

        // ------------------------------------------------------------ validation: errors, never exceptions

        [TestCase(ArthropodForm.Spider, 1), TestCase(ArthropodForm.Spider, 6), TestCase(ArthropodForm.Crab, 1), TestCase(ArthropodForm.Crab, 6)]
        public void LegPairs1And6_Compose(ArthropodForm form, int pairs)
        {
            var plan = new ArthropodPlan { id = "plan", form = form, bodyLength = 0.3f, legPairs = pairs };
            CompiledSpecies c = Compose(plan);
            Assert.IsNull(c.Error, c.Error);
            int walking = 0;
            foreach (LimbPart l in c.Limbs) if (l.Walks) walking++;
            Assert.AreEqual(2 * pairs, walking);
        }

        [Test] public void WrongFormulaLength_IsComposeError()
        {
            var plan = Crab(); plan.legFormula = new[] { 1f, 0.9f };
            CompiledSpecies c = null;
            Assert.DoesNotThrow(() => c = Compose(plan));
            StringAssert.Contains("legFormula", c.Error);
            var bad = Crab(); bad.legPairs = 9;
            Assert.DoesNotThrow(() => c = Compose(bad));
            StringAssert.Contains("legPairs", c.Error);
            bad = Crab(); bad.bodyLength = 0f;
            Assert.DoesNotThrow(() => c = Compose(bad));
            StringAssert.Contains("bodyLength", c.Error);
            bad = Crab(); bad.posture = 2f;
            Assert.DoesNotThrow(() => c = Compose(bad));
            StringAssert.Contains("posture", c.Error);
        }

        // posture at its bounds: every rest bend inside its hinge range and the leg model valid (monotone synergy).
        [TestCase(ArthropodForm.Spider, 0.6f), TestCase(ArthropodForm.Spider, 1.4f), TestCase(ArthropodForm.Crab, 0.6f), TestCase(ArthropodForm.Crab, 1.4f)]
        public void PostureBounds_StayInLimits(ArthropodForm form, float posture)
        {
            var plan = new ArthropodPlan { id = "plan", form = form, bodyLength = 0.3f, posture = posture };
            CompiledSpecies c = Compose(plan);
            Assert.IsNull(c.Error, c.Error);
            CreatureSpecies sp = Make("posture", new[] { plan }).Compiled.Runtime;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                foreach (LegSegment s in sp.legs[l].segments)
                {
                    Assert.GreaterOrEqual(s.restBendDeg, s.minBendDeg, $"leg {l}");
                    Assert.LessOrEqual(s.restBendDeg, s.maxBendDeg, $"leg {l}");
                }
                Assert.IsNull(sp.LegModel(l).Error, $"leg {l}");
            }
        }

        // ------------------------------------------------------------ the rest-stance service

        [Test] public void RestStance_Tarantula_IsRestShape()
        {
            CreatureSpecies t = CreatureKitAssets.Load("Tarantula");
            for (int l = 0; l < t.legs.Length; l++)
            {
                if (t.legs[l].feeler) continue;
                RestReport r = RestStance.Report(t, l);
                Assert.Less(Mathf.Abs(r.s), 0.5f, $"leg {l}");
                Assert.Less(Mathf.Abs(r.pitchDeg), 5f, $"leg {l}");
            }
        }

        // RED for the report: a crab leg whose rest point was typed by hand (the pre-archetype stone crab's pair 2: its
        // rest point at (0.25, 0.10) under a 0.12 m clearance) is bent by the IK to reach it.
        [Test] public void Red_HandTypedRestPoint_IsNotRestShape()
        {
            var leg = new ArthropodLeg
            {
                id = "leg", at = new PartSocket(1, 0, new Vector3(0.085f, -0.035f, 0.01f)), mirror = true, restOffset = new Vector3(0.25f, 0f, 0.1f),
                stepThreshold = 0.06f, stepDuration = 0.15f, stepLift = 0.035f, contactAngleDeg = 40f,
                segments = new[]
                {
                    new SegmentDef { length = 0.026f, radiusStart = 0.023f, radiusEnd = 0.021f, restBendDeg = -35, minBendDeg = -60, maxBendDeg = 0 },
                    new SegmentDef { length = 0.088f, radiusStart = 0.022f, radiusEnd = 0.019f, restBendDeg = 30, minBendDeg = 0, maxBendDeg = 70, flexWeight = 0.4f, knuckle = 1.08f },
                    new SegmentDef { length = 0.038f, radiusStart = 0.018f, radiusEnd = 0.016f, restBendDeg = -40, minBendDeg = -90, maxBendDeg = 0, flexWeight = -0.5f },
                    new SegmentDef { length = 0.06f, radiusStart = 0.015f, radiusEnd = 0.012f, restBendDeg = -40, minBendDeg = -100, maxBendDeg = 0, flexWeight = -1f },
                    new SegmentDef { length = 0.042f, radiusStart = 0.009f, radiusEnd = 0.003f, restBendDeg = 15, minBendDeg = -40, maxBendDeg = 80 },
                },
            };
            var body = new CreatureKit.SpineBody { id = "body", spacing = 0.05f, joints = new JointDef[5] };
            for (int i = 0; i < 5; i++) body.joints[i] = new JointDef { halfWidth = 0.08f, halfHeight = 0.045f, clearance = 0.12f, bendLimitDeg = 6f, rigid = true };
            var eyes = new Eyes { id = "eyes", at = new PartSocket(0, 0, new Vector3(0.05f, 0.05f, 0.05f)), radius = 0.01f };
            CreatureSpecies sp = Make("handTyped", new CreaturePart[] { body, eyes, leg }, new CreatureModule[] { new Walk { speed = 0.2f, turnRateDeg = 90f, accel = 1.5f, pattern = GaitPattern.Tetrapod } }).Compiled.Runtime;
            RestReport r = RestStance.Report(sp, 0);
            TestContext.Out.WriteLine($"hand-typed rest point: s {r.s:F1} deg, pitch {r.pitchDeg:F1} deg");
            Assert.Greater(Mathf.Abs(r.s) + Mathf.Abs(r.pitchDeg), 5f, "the report sees the leg bent to reach a typed point");
        }

        // The crab's carapace rides clear of the ground at rest: its lowest point >= 0.15 x its half height up; legs too
        // short to hold it there are a compose error (not a shell dragged on the ground).
        [TestCase(0f), TestCase(0.75f), TestCase(1.6f)]
        public void CrabPlan_BellyGap_Positive(float legLength)
        {
            var plan = new ArthropodPlan { id = "plan", form = ArthropodForm.Crab, bodyLength = 0.3f, legLength = legLength };
            CompiledSpecies c = Compose(plan);
            Assert.IsNull(c.Error, c.Error);
            var shell = (RigidLoft)c.Find("carapace");
            var body = (CreatureKit.SpineBody)c.Body;
            JointDef j = body.joints[shell.at.joint];
            float lowest = float.PositiveInfinity, half = 0f;
            for (int k = 0; k <= 40; k++)
            {
                RigidLoft.Section q = shell.SectionAt(k / 40f);
                half = Mathf.Max(half, q.b);
                for (int a = 0; a < 48; a++) lowest = Mathf.Min(lowest, shell.Lumped(q, 2f * Mathf.PI * a / 48f).y);
            }
            float gap = j.clearance + lowest;
            TestContext.Out.WriteLine($"legLength {legLength}: belly gap {gap * 1000f:F1} mm, shell half height {half * 1000f:F1} mm");
            Assert.GreaterOrEqual(gap, 0.15f * half);
        }

        [Test] public void CrabPlan_LegsTooShort_IsComposeError()
        {
            var plan = new ArthropodPlan { id = "plan", form = ArthropodForm.Crab, bodyLength = 0.3f, legLength = 0.5f };
            CompiledSpecies c = null;
            Assert.DoesNotThrow(() => c = Compose(plan));
            StringAssert.Contains("belly", c.Error);
        }

        // ------------------------------------------------------------ the gait-defaults service

        [Test] public void GaitDefaults_FromLegLengthAndFroude()
        {
            const float L = 0.4f;
            Assert.AreEqual(0.12f * L, GaitDefaults.Threshold * L, 1e-6f);
            Assert.AreEqual(0.07f * L, GaitDefaults.Overshoot * L, 1e-6f);
            Assert.AreEqual(1.8f * Mathf.Sqrt(L / 9.81f), GaitDefaults.StepDuration(1.8f, L), 1e-6f);
            Assert.AreEqual(0.08f * Mathf.Sqrt(9.81f * L), GaitDefaults.Speed(0.08f, L), 1e-6f);
        }

        // ------------------------------------------------------------ the shipped crabs on the plan

        // Both shipped crabs are made from the crab plan, stand in their legs' rest shape and walk sideways.
        [TestCase("RockCrab"), TestCase("TentacleCrab")]
        public void ShippedCrabs_FromThePlan_RestShape_Sideways(string name)
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(name);
            Assert.IsTrue(asset.parts[0] is ArthropodPlan p && p.form == ArthropodForm.Crab, "the first part is the crab plan");
            CreatureSpecies sp = asset.Compiled.Runtime;
            Assert.IsTrue(sp.sideways, "crabs walk sideways (user decision 2026-10-20)");
            for (int l = 0; l < sp.legs.Length; l++)
            {
                if (sp.legs[l].feeler) continue;
                RestReport r = RestStance.Report(sp, l);
                Assert.Less(Mathf.Abs(r.s), 0.5f, $"{name} leg {l}: rest synergy");
                Assert.Less(Mathf.Abs(r.pitchDeg), 5f, $"{name} leg {l}: rest pitch");
            }
        }

        // The stone crab is compact and chunky (user decision): its feet stand within 0.9 carapace widths of the midline
        // (a leg span under 1.8 widths), its legs are thick (merus radius >= 0.08 of the leg) and flattened.
        [Test] public void StoneCrab_IsCompactAndChunky()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("RockCrab");
            var shell = (RigidLoft)sp.Compiled.Find("carapace");
            float W = 0f;
            foreach (LoftKey k in shell.keys) W = Mathf.Max(W, 2f * k.halfWidth);
            for (int l = 0; l < sp.legs.Length; l++)
            {
                LegSpec q = sp.legs[l];
                if (q.feeler) continue;
                Assert.Less(q.restOffset.x, 0.9f * W, $"leg {l}: foot out {q.restOffset.x:F3} m of a {W:F3} m shell");
                Assert.GreaterOrEqual(q.segments[2].radiusStart, 0.08f * q.TotalLength, $"leg {l}: a thick merus");
                Assert.Greater(q.segments[2].flatten, 0f, $"leg {l}: a flat merus");
            }
        }

        // The stone crab's tuck is the plan's, derived from its legs and shell (no hand-fitted fold rows in the asset).
        [Test] public void StoneCrab_TuckIsDerived()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("RockCrab");
            foreach (CreaturePart p in asset.parts) Assert.IsFalse(p is Tuck, "no Tuck part in the asset");
            Assert.AreEqual("plan", asset.Compiled.GeneratedBy("tuck"));
        }

        // ------------------------------------------------------------ a generated Tuck is the last part

        [Test] public void GeneratedTuck_IsLast()
        {
            var plan = Crab(); plan.tuck = true;
            var teeth = new SpineField { id = "teeth", mirror = true, loft = "carapace", count = 4, tMin = 0.5f, tMax = 0.8f, lengthMin = 0.01f, lengthMax = 0.012f, baseRatio = 0.5f };
            CompiledSpecies c = Compose(plan, teeth);
            Assert.IsNull(c.Error, c.Error);
            Assert.IsTrue(c.Parts[c.Parts.Count - 1] is Tuck, "the Tuck is last");
            Assert.AreEqual("plan", c.GeneratedBy("tuck"));
        }
    }
}
