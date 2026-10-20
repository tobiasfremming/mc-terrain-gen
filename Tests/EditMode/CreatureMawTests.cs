using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's Maw part (2026-10-20, the Delver's head): validation; the mesh (skull rigid to its joint, mandible to
    // the jaw bone, a tooth row per lip side, the mouth lining on both); the gape follows CreatureSim.Display (full at
    // gapeDeg within gapeSeconds x sqrt s, back to the breathing gape after), Froude-timed at 0.9x / 1.1x; the jaw bone
    // turns about the hinge (the hinge point never moves relative to the head); RED: a Maw that ignores Display fails.
    public class CreatureMawTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static MawKey K(float z, float hw, float top, float bot) => new MawKey { z = z, halfWidth = hw, top = top, bottom = bot };
        static Maw Ok() => new Maw
        {
            id = "maw", at = new PartSocket(0, 0, new Vector3(0f, -0.02f, -0.06f)),
            skull = new[] { K(-0.05f, 0.07f, 0.06f, 0f), K(0.05f, 0.06f, 0.06f, 0f), K(0.15f, 0.03f, 0.03f, 0f) },
            mandible = new[] { K(-0.04f, 0.06f, 0f, -0.04f), K(0.05f, 0.05f, 0f, -0.03f), K(0.14f, 0.02f, 0f, -0.015f) },
        };

        [Test] public void Maw_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(20)));
            var bad = (Maw)Ok().Clone(); bad.skull = new[] { K(0f, 0.05f, 0.05f, 0f), K(0.1f, 0.04f, 0.04f, 0f) };
            Assert.IsNotNull(bad.Validate(Ctx(20)), "two sections");
            bad = (Maw)Ok().Clone(); bad.mandible[1].z = -0.1f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "z not increasing");
            bad = (Maw)Ok().Clone(); bad.skull[0].top = -0.01f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "top below bottom");
            bad = (Maw)Ok().Clone(); bad.gapeDeg = 90f;
            Assert.IsNotNull(bad.Validate(Ctx(20)));
            bad = (Maw)Ok().Clone(); bad.mouthLayer = 5;
            Assert.IsNotNull(bad.Validate(Ctx(20)));
            bad = (Maw)Ok().Clone(); bad.toothLength = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(20)));
        }

        [Test] public void Maw_Mesh_SkullJawTeethAndLining()
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            CreatureSpecies sp = asset.Compiled.Runtime;
            Maw maw = CreatureBandShellTests.Find<Maw>(asset);
            int jaw = sp.Layout.Start(Index(asset, maw));
            Mesh m = CreatureKitAssets.BuildMesh(asset); trash.Add(m);
            Vector3[] v = m.vertices; int[] t = m.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>(); m.GetUVs(1, uv1); m.GetUVs(2, uv2);
            int skullLining = 0, jawLining = 0;
            var teethUp = new bool[v.Length]; var teethDown = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int part = Mathf.RoundToInt(uv1[i].x), b = (int)uv2[i].x;
                if (part == maw.mouthLayer && b == maw.at.joint) skullLining++;
                if (part == maw.mouthLayer && b == jaw) jawLining++;
                if (part == maw.toothLayer && b == maw.at.joint) teethUp[i] = true;
                if (part == maw.toothLayer && b == jaw) teethDown[i] = true;
            }
            Assert.Greater(skullLining, 20, "palate lining"); Assert.Greater(jawLining, 20, "mouth floor lining");
            Assert.AreEqual(2 * maw.teethPerSide, CreatureDigLegTests.Pieces(t, teethUp, v), "upper teeth");
            Assert.AreEqual(2 * maw.teethPerSide, CreatureDigLegTests.Pieces(t, teethDown, v), "lower teeth");
        }

        static int Index(CreatureSpecies asset, CreaturePart p)
        {
            var parts = asset.Compiled.Parts;
            for (int i = 0; i < parts.Count; i++) if (ReferenceEquals(parts[i], p)) return i;
            return -1;
        }

        static MawRuntime Runtime(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is MawRuntime m) return m;
            return null;
        }

        // Display 1 opens it fully within gapeSeconds (Froude time), Display 0 lets it fall back to the breath; frames
        // to open are the same at 0.9x and 1.1x when the tick is Froude-scaled.
        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Maw_Gape_FollowsDisplay_FroudeTimed(float rel)
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            CreatureSpecies sp = asset.Compiled.Runtime;
            Maw maw = CreatureBandShellTests.Find<Maw>(asset);
            float s = asset.scale * rel, dt = Mathf.Sqrt(s) / 60f;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 3);
            MawRuntime mr = Runtime(sim);
            Assert.IsNotNull(mr);
            for (int f = 0; f < 30; f++) sim.Tick(dt, sim.Joints[0]);
            Assert.LessOrEqual(mr.Angle, maw.breathDeg + 1e-3f, "breathing at most");
            sim.Display = 1f;
            int frames = 0;
            while (mr.Angle < maw.gapeDeg - 1e-3f && frames < 600) { sim.Tick(dt, sim.Joints[0]); frames++; }
            Assert.AreEqual(Mathf.CeilToInt(maw.gapeSeconds * 60f), frames, 1, "frames to the full gape (Froude)");
            // the jaw bone turns about the hinge: the hinge point is fixed relative to the head joint
            var world = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(world, 0);
            Vector3 hingeJaw = world[mr.Bone].MultiplyPoint3x4(Vector3.zero);
            Vector3 hingeHead = world[maw.at.joint].MultiplyPoint3x4(maw.Hinge);
            Assert.Less((hingeJaw - hingeHead).magnitude, 1e-4f * s, "hinge");
            sim.Display = 0f;
            for (int f = 0; f < 120; f++) sim.Tick(dt, sim.Joints[0]);
            Assert.LessOrEqual(mr.Angle, maw.breathDeg + 1e-3f, "shut again");
        }

        // RED: without the Display channel (gapeDeg 0) the same request never opens it.
        [Test] public void Maw_Red_NoGapeNeverOpens()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("Delver");
            Maw maw = null; float g = 0f;
            foreach (CreaturePart p in asset.parts) if (p is Maw m) { maw = m; g = m.gapeDeg; m.gapeDeg = 0f; }
            try
            {
                asset.InvalidateCompiled();
                var sim = new CreatureSim(asset.Compiled.Runtime, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, asset.scale, 3);
                sim.Display = 1f;
                for (int f = 0; f < 120; f++) sim.Tick(1f / 60f, sim.Joints[0]);
                Assert.Less(Runtime(sim).Angle, g - 1f);
            }
            finally { maw.gapeDeg = g; asset.InvalidateCompiled(); }
        }
    }
}
