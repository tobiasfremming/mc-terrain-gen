using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    public class CreatureEnclosureTests
    {
        const string kLabConfigPath = "Assets/TerrainGen/Config/Creatures/CreatureLabWorldConfig.asset";
        const string kDesertDensityPath = "Assets/TerrainGen/Config/Test/DesertWorld.asset";

        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        [Test] public void Curb_FollowsGround()
        {
            Mesh m = CreatureEnclosure.BuildCurb(new FlatGroundProbe(2f), new Vector3(3f, 5f, -1f), Vector3.up, 8f, 0.12f, 0.3f, 64);
            trash.Add(m);
            Vector3[] v = m.vertices;
            Assert.Greater(v.Length, 0);
            foreach (Vector3 p in v)
            {
                Assert.GreaterOrEqual(p.y, 2f - 0.03f - 1e-4f, "vertex below the sunk base");
                Assert.LessOrEqual(p.y, 2f + 0.12f + 1e-4f, "vertex above the curb top");
            }
        }

        [Test] public void Curb_IsClosedRing()
        {
            const int segs = 48;
            Mesh m = CreatureEnclosure.BuildCurb(new FlatGroundProbe(0f), Vector3.zero, Vector3.up, 8f, 0.12f, 0.3f, segs);
            trash.Add(m);
            Vector3[] v = m.vertices;
            Assert.AreEqual(0, v.Length % (segs + 1), "vertex count is not a whole number of cross-sections");
            int per = v.Length / (segs + 1);
            for (int i = 0; i < per; i++)
                Assert.Less((v[i] - v[segs * per + i]).magnitude, 1e-5f, "seam cross-section vertex " + i);
            Assert.AreEqual(0, m.triangles.Length % 3);
            Assert.Greater(m.triangles.Length, 0);
        }

        [Test] public void Area_RadiusScalesWithSpecies()
        {
            var sp = CreatureTestSpecies.Lizardish(); trash.Add(sp);
            var go = new GameObject("EnclosureTest"); trash.Add(go);
            go.transform.position = new Vector3(1f, 2f, 3f);
            var e = go.AddComponent<CreatureEnclosure>();
            e.radius = 8f;
            EnclosureArea a = e.AreaFor(sp, 1.1f);
            Assert.AreEqual(8f - 1.1f * (sp.MaxLateralReach + 0.2f), a.safeRadius, 1e-5f);
            Assert.AreEqual(8f, a.radius, 1e-6f);
            Assert.Less((a.center - go.transform.position).magnitude, 1e-5f);
        }

        [Test] public void Lab_ArcSlots_Spaced()
        {
            Vector3 origin = new Vector3(10f, 3f, -4f), fwd = new Vector3(1f, 0f, 1f).normalized;
            const int n = 4;
            for (int k = 0; k + 1 < n; k++)
            {
                Vector3 a = CreatureLab.ArcSlot(origin, fwd, k, n, 25f, 30f);
                Vector3 b = CreatureLab.ArcSlot(origin, fwd, k + 1, n, 25f, 30f);
                Assert.AreEqual(25f, (a - b).magnitude, 0.5f, "slots " + k + "/" + (k + 1));
                Assert.AreEqual(30f, (a - origin).magnitude, 1e-3f);
            }
            // a single species sits straight ahead
            Vector3 s = CreatureLab.ArcSlot(origin, fwd, 0, 1, 25f, 30f);
            Assert.Less((s - (origin + fwd * 30f)).magnitude, 1e-3f);
        }

        // Reads the serialized fields as text: loading the WorldConfig would pull in its density field and
        // everything that references (textures included), which ran the Editor out of memory once.
        [Test] public void LabConfig_PlantsOff_FlatWorld()
        {
            Assert.AreEqual(typeof(WorldConfig), AssetDatabase.GetMainAssetTypeAtPath(kLabConfigPath), kLabConfigPath);
            string yaml = File.ReadAllText(kLabConfigPath);
            StringAssert.IsMatch(@"(?m)^  enablePlants: 0\s*$", yaml, "enablePlants");
            StringAssert.IsMatch(@"(?m)^  useGlobe: 0\s*$", yaml, "useGlobe");
            string desertGuid = AssetDatabase.AssetPathToGUID(kDesertDensityPath);
            Assert.IsNotEmpty(desertGuid, kDesertDensityPath);
            Match m = Regex.Match(yaml, @"(?m)^  defaultDensity: \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}\s*$");
            Assert.IsTrue(m.Success, "defaultDensity is not an asset reference");
            Assert.AreEqual(desertGuid, m.Groups[1].Value, "defaultDensity is not DesertWorld");
        }
    }
}
