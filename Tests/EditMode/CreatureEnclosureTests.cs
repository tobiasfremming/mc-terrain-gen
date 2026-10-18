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

        // A rebuilt curb (every lab rescale, R22) must free the mesh of the curb it replaces, and so must the
        // enclosure when it goes away.
        [Test] public void Curb_Rebuild_DestroysTheOldMesh()
        {
            var go = new GameObject("EnclosureLeakTest");
            trash.Add(go);
            var e = go.AddComponent<CreatureEnclosure>();
            e.segments = 16;
            e.Build(new FlatGroundProbe());
            Mesh first = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.IsNotNull(first);
            e.Build(new FlatGroundProbe());
            Mesh second = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.IsTrue(first == null, "the replaced curb's mesh leaked");
            Assert.IsTrue(second != null);
            // edit mode sends no OnDestroy to a plain MonoBehaviour: call it as the engine would
            typeof(CreatureEnclosure).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(e, null);
            Assert.IsTrue(second == null, "the curb's mesh outlived its enclosure");
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

        // Flat ground (y = 0) whose normal leans away from one sweet spot: the farther a point lies from it,
        // the steeper the lab's flatness search thinks it is, so every enclosure is drawn toward the spot.
        sealed class SweetSpotProbe : IGroundProbe
        {
            readonly Vector3 spot;
            public SweetSpotProbe(Vector3 spot) { this.spot = spot; }
            public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
            {
                point = new Vector3(near.x, 0f, near.z);
                Vector3 d = point - spot; d.y = 0f;
                normal = (Vector3.up + 0.03f * d).normalized;
                return true;
            }
        }

        // Task 8 minor: each enclosure searches searchRadius (15 m) around its arc slot for the flattest spot,
        // while neighbouring slots are only ~25 m apart, so two neighbours drawn toward the same flat patch
        // overlapped. Placed centres must stay at least r_a + r_b + the enclosure gap apart.
        [Test] public void Lab_Placement_NeighboursNeverOverlap()
        {
            var go = new GameObject("LabTest"); trash.Add(go);
            var world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/Creature.mat");
            var lab = go.AddComponent<CreatureLab>();
            const int n = 3;
            lab.species = new CreatureSpecies[n];
            for (int k = 0; k < n; k++) { lab.species[k] = CreatureTestSpecies.Lizardish(); trash.Add(lab.species[k]); }
            var player = new GameObject("Player"); trash.Add(player);
            // the middle slot (straight ahead of the player) is the flattest place around
            float slotRadius = Mathf.Max(lab.arcRadius, CreatureLab.EnclosureRadius(lab.species[0], 1f) + 4f + lab.searchRadius);
            Vector3 spot = new Vector3(0f, 0f, slotRadius);
            world.SetGround(new SweetSpotProbe(spot), _ => Vector3.up);
            try
            {
                lab.Place(world, player.transform, Vector3.zero);
                var c = new Vector3[n]; var r = new float[n];
                for (int k = 0; k < n; k++) { c[k] = lab.EnclosureFor(k).transform.position; r[k] = lab.EnclosureFor(k).radius; }
                // the search is live: the outer enclosures were drawn toward the spot, off their slots
                Vector3 slot0 = CreatureLab.ArcSlot(Vector3.zero, Vector3.forward, Vector3.up, 0, n, Mathf.Max(lab.spacing, 2f * r[0] + CreatureLab.kEnclosureGap), slotRadius);
                Assert.Less((c[0] - spot).magnitude, (slot0 - spot).magnitude - 1f, "the flatness search did not move the enclosure: the test does not bite");
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        Vector3 d = c[a] - c[b]; d.y = 0f;
                        TestContext.Out.WriteLine($"enclosures {a}/{b}: centres {d.magnitude:F2} m apart, need {r[a] + r[b] + CreatureLab.kEnclosureGap:F2} m");
                        Assert.GreaterOrEqual(d.magnitude, r[a] + r[b] + CreatureLab.kEnclosureGap - 1e-3f, $"enclosures {a} and {b} overlap");
                    }
            }
            finally
            {
                typeof(CreatureWorld).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(world, null);
            }
        }

        // R22: changing a species' scale (or the lab's override) respawns its creature at the new size, rebuilds
        // the enclosure for it, and drops the old creature from the world and its renderer.
        [Test] public void Lab_ScaleChange_RespawnsAtNewSize()
        {
            var sp = CreatureTestSpecies.Lizardish(); trash.Add(sp);
            sp.scale = 3f;
            var go = new GameObject("LabTest"); trash.Add(go);
            var world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/Creature.mat");
            world.SetGround(new FlatGroundProbe(), _ => Vector3.up);
            var lab = go.AddComponent<CreatureLab>();
            lab.species = new[] { sp };
            var player = new GameObject("Player"); trash.Add(player);
            try
            {
                lab.Place(world, player.transform, Vector3.zero);
                CreatureSim before = lab.SimFor(0);
                float r0 = lab.EnclosureFor(0).radius;
                Assert.IsNotNull(before);
                Assert.AreEqual(1, world.Sims.Count);

                sp.scale = 6f;
                lab.ApplyScales();
                CreatureSim after = lab.SimFor(0);
                Assert.AreNotSame(before, after, "the creature was not respawned");
                Assert.AreEqual(2f * before.SizeScale, after.SizeScale, 1e-4f, "size scale");
                Assert.AreEqual(2f * r0, lab.EnclosureFor(0).radius, 1e-3f, "enclosure radius");
                Assert.AreEqual(1, world.Sims.Count, "the old creature is still in the world");
                Assert.AreSame(after, world.Sims[0]);
                var rs = (System.Collections.Generic.List<CreatureRenderer>)typeof(CreatureWorld).GetField("renderers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(world);
                Assert.AreEqual(1, rs[0].Count, "the old creature is still drawn");

                // an override wins over the species' scale
                lab.scaleOverrides = new[] { 4.5f };
                lab.ApplyScales();
                Assert.AreEqual(4.5f / 6f * after.SizeScale, lab.SimFor(0).SizeScale, 1e-4f, "override scale");
                Assert.AreEqual(1, world.Sims.Count);
                // no change, no respawn
                CreatureSim same = lab.SimFor(0);
                lab.ApplyScales();
                Assert.AreSame(same, lab.SimFor(0));

                EnclosureArea area = same.Bounds.Value;
                Assert.AreEqual(lab.EnclosureFor(0).radius, area.radius, 1e-4f);
                for (int t = 0; t < 1800; t++)
                {
                    world.TickAll(1f / 30f);
                    for (int i = 0; i < same.Joints.Length; i++)
                        Assert.LessOrEqual(area.PlanarDistance(same.Joints[i]), area.radius, "joint " + i + " t=" + same.Time);
                    for (int l = 0; l < same.Legs.Length; l++)
                        Assert.LessOrEqual(area.PlanarDistance(same.Legs[l].foot), area.radius, "foot " + l + " t=" + same.Time);
                }
            }
            finally
            {
                // edit mode sends no OnDestroy: release the renderers by hand
                typeof(CreatureWorld).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(world, null);
            }
        }
    }
}
