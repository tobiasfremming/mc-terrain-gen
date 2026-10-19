using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Creature kit T3.2: flight becomes a locomotion mode (ILocomotionMode) and the antennae and eyes part runtimes
    // (IPartRuntime) of CreatureSim. The flyers must stay bit-identical through it: every bone matrix of every tick
    // of a walk, a launch, a climb to altitude, cruising turns and (the dragon) a barrel roll. Captured offline on
    // feat/creature-kit 41d0685, before the stage change; Unity (Mono) rounds differently, so it has its own
    // constants (0 = not captured yet: the test reports the value to paste in).
    public class CreatureFlightGoldenTests
    {
        const string kDir = "Assets/TerrainGen/Config/Creatures/";
        const float kDt = 1f / 60f;

#if UNITY_EDITOR
        static readonly Dictionary<string, ulong> Golden = new Dictionary<string, ulong>
        {
            { "WingedSerpent", 0x0UL },   // capture in Unity: leg look 2026-10-19: ankle, swing-only upper bone, toe peel (was 0x24CCE26149E5522B)
            { "SkyDragon", 0x0UL },       // capture in Unity: dragon v4 + leg look 2026-10-19: ankle, swing-only upper bone, toe peel
        };
#else
        static readonly Dictionary<string, ulong> Golden = new Dictionary<string, ulong>
        {
            { "WingedSerpent", 0xFC0434CD8302994FUL },   // leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x7FAAA303233C33AD: merge/next 2026-10-19: rigid head 2 + tall stance, flank patches, avoidance (rigid head alone 0x852A4AC6FE4DCB92, wyvern-trees alone 0x41E6D8C3D4F1B5E9)
            { "SkyDragon", 0x8A7C78C28100AB37UL },       // leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x530A5EAF772E2602: dragon v4: chest, Caraxes legs + leg fins, wing look (2026-10-19)
        };
#endif

        const ulong kPrime = 1099511628211UL, kBasis = 14695981039346656037UL;
        static ulong F(ulong h, float f) => (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * kPrime;

        internal static ulong FlightHash(CreatureSpecies sp, List<ulong> blocks)
        {
            float s = sp.scale;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s);
            var bones = new Matrix4x4[sp.BoneCount];
            ulong h = kBasis;
            void Hash(int t)
            {
                sim.WriteBoneWorld(bones, 0);
                for (int b = 0; b < bones.Length; b++)
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++) h = F(h, bones[b][r, c]);
                if (t % 100 == 99) blocks?.Add(h);
            }
            Vector3 ahead = new Vector3(0f, 0f, 100f) * s;
            int tick = 0;
            bool flew = false;
            for (int t = 0; t < 60; t++, tick++) { sim.Tick(kDt, ahead); Hash(tick); }
            Assert.IsTrue(sim.Flight.Launch(5f * s), sp.name + " launches");
            for (int t = 0; t < 540; t++, tick++)
            {
                if (t == 300 && sim.Flight.Twist != null)
                    sim.Flight.Twist.Begin(TwistManoeuvre.BarrelRoll, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up);
                // cruise ahead, then a wide turn
                Vector3 dir = Quaternion.AngleAxis(t < 200 ? 0f : 0.3f * (t - 200), Vector3.up) * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + dir * (5f * s));
                flew |= sim.Mode == FlightMode.Air;
                Hash(tick);
            }
            Assert.IsTrue(flew, sp.name + " reached the air mode");
            return h;
        }

        static void Check(string name)
        {
            CreatureSpecies a = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kDir + name + ".asset");
            Assert.IsNotNull(a, name);
            CreatureSpecies sp = a.Compiled.Runtime;
            var blocks = new List<ulong>();
            ulong got = FlightHash(sp, blocks);
            string msg = $"{name}: flight bones 0x{got:X16}UL; 100-tick blocks {string.Join(" ", blocks.ConvertAll(b => b.ToString("X16")))}";
            TestContext.Out.WriteLine(msg);
            ulong want = Golden[name];
            if (want == 0UL) Assert.Inconclusive("golden not captured for this runtime yet: " + msg);
            Assert.AreEqual(want, got, "flight bone matrices changed: " + msg);
        }

        [Test] public void Flight_BitIdentical_WingedSerpent() => Check("WingedSerpent");
        [Test] public void Flight_BitIdentical_SkyDragon() => Check("SkyDragon");
    }

    // The walking species LegacyLegsGoldenTests does not pin (arthropods, the flyers on the ground, the kit-format
    // monitor): every bone of LegacyLegsGoldenTests' weaving 600-tick walk. Captured offline on 41d0685 like the
    // flight goldens (Unity: 0 = report the value).
    public class CreatureWalkGoldenTests
    {
#if UNITY_EDITOR
        static readonly Dictionary<string, ulong> Golden = new Dictionary<string, ulong>
        {
            // da59bfc (2026-10-19): rigid head (+ feat/wyvern-trees data for the serpent); KitMonitor = DesertMonitor's bones
            { "DesertBeetle", 0x0UL /* capture in Unity: leg look beetle legs (2026-10-19); was 0x6F8C1E247D6717C9 */ }, { "DaddyLongLegs", 0x528567DD58C2C0E5UL }, { "Tarantula", 0x109D36AB222C53A6UL /* R38; was 0x3FFDCDB621DEB7FD */ },
            { "WingedSerpent", 0x0UL /* capture in Unity: leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x052FEB1EAD20C942 */ }, { "SkyDragon", 0x0UL /* capture in Unity: dragon v4 + leg look 2026-10-19: ankle, swing-only upper bone, toe peel */ }, { "KitMonitor", 0x0UL /* capture in Unity: leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x90BEBD53968C4A95 */ },
        };
#else
        static readonly Dictionary<string, ulong> Golden = new Dictionary<string, ulong>
        {
            // rigid head (2026-10-19): head joints 2, 2, 3, 2, 2 (auto), 3 (auto, = DesertMonitor's, still bit-identical to it)
            { "DesertBeetle", 0x544F9C034A2997F0UL /* leg look beetle legs (2026-10-19); was 0xF971D4AF2276C7F0 */ }, { "DaddyLongLegs", 0xE95F7961F0A7D560UL }, { "Tarantula", 0xC3636620EEAB2695UL /* R38; was 0xF1CB64350C3D6F9D */ },
            { "WingedSerpent", 0x5E5143BC2A6E894FUL /* leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x6F1262984FBBED19: merge/next 2026-10-19: rigid head + tall stance (rigid head alone 0x8A85F58517392C26, wyvern-trees alone 0xF0B147085F1B2FD2) */ }, { "SkyDragon", 0xBB7F5AC4A3E23DA2UL /* leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x79804CFF954EA099 */ }, { "KitMonitor", 0x0413E426BA709E47UL /* leg look 2026-10-19: ankle, swing-only upper bone, toe peel; was 0x72B52B3229301DDB */ },
        };
#endif

        [TestCase("DesertBeetle"), TestCase("DaddyLongLegs"), TestCase("Tarantula"), TestCase("WingedSerpent"), TestCase("SkyDragon"), TestCase("KitMonitor")]
        public void Walk_BitIdentical(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            Assert.IsNotNull(sp, name);
            var blocks = new List<ulong>();
            ulong got = LegacyLegsGoldenTests.BoneHash(sp, blocks);
            string msg = $"{name}: walk bones 0x{got:X16}UL; 100-tick blocks {string.Join(" ", blocks.ConvertAll(b => b.ToString("X16")))}";
            TestContext.Out.WriteLine(msg);
            ulong want = Golden[name];
            if (want == 0UL) Assert.Inconclusive("golden not captured for this runtime yet: " + msg);
            Assert.AreEqual(want, got, "walking bone matrices changed: " + msg);
        }
    }
}
