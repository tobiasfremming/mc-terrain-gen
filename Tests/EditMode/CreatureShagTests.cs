using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's Shag part (2026-10-20, the Delver's fur): validation (limb names must exist); the locks (its layer: the Delver puts them on id 12, restyled as fur) are
    // rigid to joints of the run or to the named limbs' upper bones, every vertex finite with a unit normal, none below
    // the rest pose's ground, and there are as many as the rows ask for; RED: without groundClear long locks reach the
    // ground.
    public class CreatureShagTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        [Test] public void Shag_Validate_RejectsBadData()
        {
            var limb = new DigLeg { id = "fore.L", at = new PartSocket(4, -1, Vector3.zero), upperLength = 0.1f, lowerLength = 0.1f, upperRadius = 0.03f, lowerRadius = 0.03f, footLength = 0.05f, toeCount = 3 };
            var ctx = new SpeciesCtx(new CreaturePart[] { limb }, new CreatureModule[0], 20, false);
            var ok = new Shag { id = "fur", at = new PartSocket(3, 0, Vector3.zero), toJoint = 12, limbs = new[] { "fore" } };
            Assert.IsNull(ok.Validate(ctx));
            var bad = (Shag)ok.Clone(); bad.limbs = new[] { "wing" };
            Assert.IsNotNull(bad.Validate(ctx), "unknown limb");
            bad = (Shag)ok.Clone(); bad.dorsalTo = -1f;
            Assert.IsNotNull(bad.Validate(ctx));
            bad = (Shag)ok.Clone(); bad.toJoint = 20;
            Assert.IsNotNull(bad.Validate(ctx));
            bad = (Shag)ok.Clone(); bad.length = 0f;
            Assert.IsNotNull(bad.Validate(ctx));
        }

        static string Problem(CreatureSpecies asset, List<Object> trash, out int locks)
        {
            CreatureSpecies sp = asset.Compiled.Runtime;
            Shag shag = CreatureBandShellTests.Find<Shag>(asset);
            Mesh m = CreatureKitAssets.BuildMesh(asset); trash.Add(m);
            Vector3[] v = m.vertices, n = m.normals; int[] t = m.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>(); m.GetUVs(1, uv1); m.GetUVs(2, uv2);
            var allowed = new HashSet<int>();
            for (int j = shag.at.joint; j <= shag.toJoint; j++) allowed.Add(j);
            for (int l = 0; l < sp.legs.Length; l++) allowed.Add(sp.LegBone(l, 0));
            var mark = new bool[v.Length];
            locks = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.RoundToInt(uv1[i].x) != shag.layer) continue;
                mark[i] = true;
                if (!allowed.Contains((int)uv2[i].x) || uv2[i].z < 0.999f) return $"vertex {i}: skinned to {uv2[i]}";
                if (float.IsNaN(v[i].x + v[i].y + v[i].z) || Mathf.Abs(n[i].magnitude - 1f) > 1e-3f) return $"vertex {i}: not finite / unit";
                if (v[i].y < -1e-5f) return $"vertex {i}: {v[i].y * 1000f:F1} mm below the ground";
            }
            locks = CreatureDigLegTests.Pieces(t, mark, v);
            return null;
        }

        [Test] public void Shag_Delver_LocksRigid_AboveTheGround()
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            Assert.IsNull(Problem(asset, trash, out int locks));
            Shag shag = CreatureBandShellTests.Find<Shag>(asset);
            int rows = Mathf.RoundToInt((shag.toJoint - shag.at.joint) * shag.perJoint) + 1;
            Assert.GreaterOrEqual(locks, (int)(0.9f * rows * 2 * shag.around), "body locks (some fade to nothing at the run's ends)");
        }

        [Test] public void Shag_Red_NoGroundClearReachesTheGround()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("Delver");
            Shag shag = null; float len = 0f, gc = 0f, dr = 0f;
            foreach (CreaturePart p in asset.parts) if (p is Shag s) { shag = s; len = s.length; gc = s.groundClear; dr = s.droop; s.length = 0.4f; s.groundClear = 0f; s.droop = 1f; }
            try
            {
                asset.InvalidateCompiled();
                string e = Problem(asset, trash, out _);
                // without the clamp the locks would hang through the ground; with groundClear 0 they stop exactly on it
                Assert.IsNull(e);
                shag.groundClear = 0f;
                var mesh = CreatureKitAssets.BuildMesh(asset); trash.Add(mesh);
                float lo = float.MaxValue; var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1); Vector3[] v = mesh.vertices;
                for (int i = 0; i < v.Length; i++) if (Mathf.RoundToInt(uv1[i].x) == shag.layer) lo = Mathf.Min(lo, v[i].y);
                Assert.Less(lo, 0.003f, "long locks reach down to the ground (the clamp bites)");
            }
            finally { shag.length = len; shag.groundClear = gc; shag.droop = dr; asset.InvalidateCompiled(); }
        }
    }
}
