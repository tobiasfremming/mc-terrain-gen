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
            { "WingedSerpent", 0UL },
            { "SkyDragon", 0UL },
        };
#else
        static readonly Dictionary<string, ulong> Golden = new Dictionary<string, ulong>
        {
            { "WingedSerpent", 0xCBCDCC3065F639EDUL },
            { "SkyDragon", 0xEF27A79A9CD7F5A9UL },
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
            { "DesertBeetle", 0UL }, { "DaddyLongLegs", 0UL }, { "Tarantula", 0UL },
            { "WingedSerpent", 0UL }, { "SkyDragon", 0UL }, { "KitMonitor", 0UL },
        };
#else
        static readonly Dictionary<string, ulong> Golden = new Dictionary<string, ulong>
        {
            { "DesertBeetle", 0x1EA0C97520747EE5UL }, { "DaddyLongLegs", 0x575349E45CB83CE9UL }, { "Tarantula", 0x03EC33CC22FCFA8AUL },
            { "WingedSerpent", 0x725173037414F8EAUL }, { "SkyDragon", 0xB86BDAF4AECA355CUL }, { "KitMonitor", 0x53B537BC3A04F006UL },
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
