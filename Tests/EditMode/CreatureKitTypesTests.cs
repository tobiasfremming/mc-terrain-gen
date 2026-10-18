using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Creature kit T2.1/T2.2: the part and module types document themselves (the catalogue reads [Part],
    // [Module] and a [Param] on every serialized field), and their data helpers behave.
    public class CreatureKitTypesTests
    {
        [Test]
        public void KitAttributes_EveryPublicFieldHasParam()
        {
            var problems = new List<string>();
            foreach (Type t in KitTypes.Parts) problems.AddRange(KitTypes.ParamProblems(t));
            foreach (Type t in KitTypes.Modules) problems.AddRange(KitTypes.ParamProblems(t));
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // catalogue names are unique (the YAML and the catalogue refer to types by them)
        [Test]
        public void KitTypes_NamesUnique()
        {
            var seen = new HashSet<string>();
            foreach (Type t in KitTypes.Parts) Assert.IsTrue(seen.Add(KitTypes.NameOf(t)), "duplicate " + KitTypes.NameOf(t));
            foreach (Type t in KitTypes.Modules) Assert.IsTrue(seen.Add(KitTypes.NameOf(t)), "duplicate " + KitTypes.NameOf(t));
        }

        // RED: an undocumented field, a nested struct field and a missing [Part] are all caught.
        [Serializable] struct RedNested { [Param(ParamUnit.Metres, "ok")] public float a; public float undocumented; }
        [Serializable] sealed class RedPart : CreaturePart { public float noParam; [Param(ParamUnit.Struct, "nested")] public RedNested nested; }

        [Test]
        public void Red_KitAttributes_UndocumentedFieldsCaught()
        {
            List<string> p = KitTypes.ParamProblems(typeof(RedPart));
            TestContext.Out.WriteLine(string.Join("\n", p));
            Assert.IsTrue(p.Exists(s => s.Contains("no [Part]")), "missing [Part]");
            Assert.IsTrue(p.Exists(s => s.EndsWith("RedPart.noParam: no [Param]")), "top-level field");
            Assert.IsTrue(p.Exists(s => s.EndsWith("RedPart.nested.undocumented: no [Param]")), "nested struct field");
            Assert.IsFalse(p.Exists(s => s.Contains("nested.a")), "a documented nested field is fine");
        }

        [Test]
        public void UnitRules_FollowFroude()
        {
            Assert.AreEqual(2f, UnitRules.SizeFactor(ParamUnit.Metres, 2f));
            Assert.AreEqual(Mathf.Sqrt(2f), UnitRules.SizeFactor(ParamUnit.Seconds, 2f), 1e-6f);
            Assert.AreEqual(Mathf.Sqrt(2f), UnitRules.SizeFactor(ParamUnit.MetresPerSecond, 2f), 1e-6f);
            Assert.AreEqual(1f / Mathf.Sqrt(2f), UnitRules.SizeFactor(ParamUnit.Hz, 2f), 1e-6f);
            Assert.AreEqual(1f, UnitRules.SizeFactor(ParamUnit.Degrees, 2f));
            Assert.AreEqual(1f, UnitRules.SizeFactor(ParamUnit.Acceleration, 2f));
        }
    
        // T2.2: a profile-authored spine is exact at its keys, monotone between them (no bulge past the widest
        // key), flat at the ends, with the neck joints rigid.
        [Test]
        public void SpineProfile_Interpolates()
        {
            var body = new CreatureKit.SpineBody
            {
                id = "body", jointCount = 11, rigidHead = 2,
                profile = new[]
                {
                    new ProfileKey { t = 0f, halfWidth = 0.03f, halfHeight = 0.025f, clearance = 0.06f, bendLimitDeg = 20f },
                    new ProfileKey { t = 0.3f, halfWidth = 0.07f, halfHeight = 0.05f, clearance = 0.1f, bendLimitDeg = 20f },
                    new ProfileKey { t = 1f, halfWidth = 0.008f, halfHeight = 0.008f, clearance = 0.03f, bendLimitDeg = 25f },
                },
            };
            Assert.IsNull(body.Validate(new SpeciesCtx(new[] { body }, null, 11, false)));
            SpineJoint[] j = body.BuildJoints();
            Assert.AreEqual(11, j.Length);
            Assert.AreEqual(0.03f, j[0].halfWidth); Assert.AreEqual(0.07f, j[3].halfWidth, "joint 3 sits on the t = 0.3 key");
            Assert.AreEqual(0.008f, j[10].halfWidth); Assert.AreEqual(25f, j[10].bendLimitDeg);
            for (int i = 1; i <= 3; i++) Assert.Greater(j[i].halfWidth, j[i - 1].halfWidth, "rising to the widest key");
            for (int i = 4; i <= 10; i++) Assert.Less(j[i].halfWidth, j[i - 1].halfWidth, "tapering to the tail");
            for (int i = 0; i < 11; i++) Assert.LessOrEqual(j[i].halfWidth, 0.07f, "no overshoot past the widest key");
            // dense: never outside the neighbouring keys' range, continuous
            float prev = body.Profile(0f, 0);
            for (int k = 1; k <= 1000; k++)
            {
                float v = body.Profile(k / 1000f, 0);
                Assert.That(v, Is.InRange(0.008f, 0.07f));
                Assert.Less(Mathf.Abs(v - prev), 0.002f, "continuous at t = " + k / 1000f);
                prev = v;
            }
            Assert.IsFalse(j[0].rigid); Assert.IsTrue(j[1].rigid); Assert.IsTrue(j[2].rigid); Assert.IsFalse(j[3].rigid);
            Assert.AreEqual(0.04f + 0.06f * 10 + 0.05f, new CreatureKit.SpineBody { jointCount = 11, spacing = 0.06f, headCapLength = 0.04f, tailTipLength = 0.05f }.BodyLength, 1e-6f);
        }

        [Test]
        public void SpineProfile_Validate_RejectsBadKeys()
        {
            var body = new CreatureKit.SpineBody { id = "body", jointCount = 8, profile = new[] { new ProfileKey { t = 0f }, new ProfileKey { t = 0.8f } } };
            Assert.IsNotNull(body.Validate(new SpeciesCtx(new[] { body }, null, 8, false)), "must end at t = 1");
            body.profile = new ProfileKey[0];
            Assert.IsNotNull(body.Validate(new SpeciesCtx(new[] { body }, null, 8, false)), "needs keys");
        }
    }
}
