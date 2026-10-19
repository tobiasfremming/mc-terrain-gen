using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Stumble part (2026-10-20, the GolfballStrider's quirky gait): every so often (seeded per individual) the
    // body lurches down at its front and springs back, and the creature stops for part of it to readjust. Validation,
    // the timeline (gaps 0.5..1.5 x every, dips of the authored depth at the front, a lurch), the halt, Froude timing
    // at 0.9x / 1.1x, and a RED proof (a stumble-free run never dips).
    public class CreatureStumbleTests
    {
        const string kSpecies = CreatureBendyLegTests.kSpecies;

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static Stumble Ok() => new Stumble { id = "stumble", every = 5f, seconds = 0.6f, depth = 0.06f, lurch = 0.6f, haltShare = 0.5f };

        [Test] public void Stumble_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(3)));
            Stumble bad;
            bad = (Stumble)Ok().Clone(); bad.every = 0f; Assert.IsNotNull(bad.Validate(Ctx(3)), "every 0");
            bad = (Stumble)Ok().Clone(); bad.seconds = 0f; Assert.IsNotNull(bad.Validate(Ctx(3)), "no duration");
            bad = (Stumble)Ok().Clone(); bad.seconds = 6f; Assert.IsNotNull(bad.Validate(Ctx(3)), "longer than the gap");
            bad = (Stumble)Ok().Clone(); bad.depth = -0.01f; Assert.IsNotNull(bad.Validate(Ctx(3)), "negative depth");
            bad = (Stumble)Ok().Clone(); bad.lurch = 1.5f; Assert.IsNotNull(bad.Validate(Ctx(3)), "lurch > 1");
            bad = (Stumble)Ok().Clone(); bad.haltShare = -0.1f; Assert.IsNotNull(bad.Validate(Ctx(3)), "halt < 0");
            Assert.AreEqual(0, Ok().BoneCount(Ctx(3)));
        }

        static StumbleRuntime Runtime(CreatureSim w)
        {
            foreach (IPartRuntime r in w.PartRuntimes) if (r is StumbleRuntime s) return s;
            return null;
        }

        // Walks `seconds`; returns the stumble count, the deepest front and back lifts (m, < 0 = down) and halted frames.
        static void Walk(CreatureSpecies rt, float scale, float seconds, int seed, out int count, out float front, out float back, out int halted, float[] starts = null)
        {
            CreatureSim w = CreatureBendyLegTests.Sim(rt, scale, seed);
            StumbleRuntime r = Runtime(w);
            Assert.IsNotNull(r, "the strider stumbles");
            count = 0; front = 0f; back = 0f; halted = 0;
            bool was = false;
            int frames = Mathf.RoundToInt(seconds * 60f);
            for (int f = 0; f < frames; f++)
            {
                w.Tick(Mathf.Sqrt(scale) / 60f, w.Joints[0] + Vector3.forward * (2f * scale));
                if (r.Active && !was) { if (starts != null && count < starts.Length) starts[count] = f; count++; }
                was = r.Active;
                if (w.JointLift != null) { front = Mathf.Min(front, w.JointLift[0]); back = Mathf.Min(back, w.JointLift[w.JointLift.Length - 1]); }
                if (w.Halt) halted++;
            }
        }

        [Test] public void Stumble_Timeline_DipsLurchesAndHalts()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            Stumble p = null;
            foreach (CreaturePart q in KitView.Parts(rt)) if (q is Stumble s) p = s;
            Assert.IsNotNull(p);
            Walk(rt, 1f, 60f, 3, out int count, out float front, out float back, out int halted);
            Assert.That(count, Is.InRange(Mathf.FloorToInt(60f / (1.5f * p.every + p.seconds)), Mathf.CeilToInt(60f / (0.5f * p.every)) + 1), "stumbles in a minute");
            Assert.AreEqual(-p.depth, front, 0.02f * p.depth, "the front dips by depth");
            Assert.AreEqual(-p.depth * (1f - p.lurch), back, 0.02f * p.depth + 1e-4f, "the back dips less (lurch)");
            Assert.Greater(halted, 0, "it stops to readjust");
            Assert.Less(halted, count * p.seconds * p.haltShare * 60f + count + 1, "only for haltShare of each stumble");
        }

        // Froude: at 0.9x and 1.1x (same seed) the stumbles start on the same frames and dip x s.
        [Test] public void Stumble_FroudeSimilar()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            var a = new float[4]; var b = new float[4];
            Walk(rt, 0.9f, 30f, 5, out int ca, out float fa, out _, out _, a);
            Walk(rt, 1.1f, 30f, 5, out int cb, out float fb, out _, out _, b);
            Assert.AreEqual(ca, cb);
            Assert.Greater(ca, 1);
            for (int i = 0; i < Mathf.Min(4, ca); i++) Assert.AreEqual(a[i], b[i], 1f, $"stumble {i} start frame");
            Assert.AreEqual(fa / 0.9f, fb / 1.1f, 1e-3f, "depth x s");
        }

        // RED: an individual whose next stumble is never due does not dip (the timeline check above bites).
        [Test] public void Stumble_Red_NoStumbleNoDip()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(kSpecies);
            Stumble p = null;
            foreach (CreaturePart q in asset.parts) if (q is Stumble s) p = s;
            float keep = p.every;
            p.every = 1e5f;
            try
            {
                asset.InvalidateCompiled();
                Walk(asset.Compiled.Runtime, 1f, 20f, 3, out int count, out float front, out _, out int halted);
                Assert.AreEqual(0, count); Assert.AreEqual(0f, front); Assert.AreEqual(0, halted);
            }
            finally { p.every = keep; asset.InvalidateCompiled(); }
        }
    }
}
