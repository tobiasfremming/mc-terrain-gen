using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The whole lab as the scene builds it (Marching Cubes / Rebuild Creature Lab: the roster, the 2x Baobab shade
    // tree, the groves with their trees and prey) placed on the real lab terrain (DesertWorld, a field probe as
    // CreatureWorld uses) from the scene's spawn: every enclosure, tree, rock and post stands on the terrain surface,
    // no two curbs overlap, the player's spot is outside every curb and every creature starts inside its own.
    // 2026-10-19 RED: six enclosures on the ring (~70 m out) were left at the player's height, 8-11 m under the
    // dunes, with their trees (the field probe from the player's height never reached the dune tops; LabGround).
    public class CreatureLabTerrainTests
    {
        const string kDesertWorld = "Assets/TerrainGen/Config/Test/DesertWorld.asset";
        const string kBaobab = "Assets/TerrainGen/Config/Flora/Prototypes/BaobabPlant_Prototypes.asset";
        static readonly string[] kGroveSets =
        {
            "Assets/TerrainGen/Config/Flora/Prototypes/OakPlant_Prototypes.asset",
            "Assets/TerrainGen/Config/Flora/Prototypes/TreePlant_Prototypes.asset",
        };
        const float kTol = 0.03f;   // "on the ground": within 3 cm of the surface (after each object's own sink)

        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        [Test] public void Lab_OnDesertWorld_EverythingOnTheGround()
        {
            var field = AssetDatabase.LoadAssetAtPath<DensityField>(kDesertWorld);
            Assert.IsNotNull(field, kDesertWorld);
            var baobab = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(kBaobab);
            if (baobab == null) Assert.Ignore(kBaobab + " is not baked on this machine (Config/Flora/Prototypes is gitignored)");
            var roster = AssetDatabase.LoadAssetAtPath<CreatureLabRoster>(CreatureLabRoster.AssetPath);
            Assert.IsNotNull(roster, CreatureLabRoster.AssetPath);
            var probe = new FieldGroundProbe(field);
            IGroundProbe column = LabGround.Wrap(probe);
            float Ground(Vector3 p)
            {
                Assert.IsTrue(column.Probe(p, Vector3.up, LabLayout.kProbeWindow, out Vector3 g, out _), "no ground in the column at " + p);
                return g.y;
            }

            var go = new GameObject("LabTerrainTest"); trash.Add(go);
            var world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/Creature.mat");
            world.SetGround(probe, _ => Vector3.up);
            var lab = go.AddComponent<CreatureLab>();
            lab.roster = roster;
            lab.borderMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/EnclosureBorder.mat");
            lab.enclosurePlant = baobab;
            lab.plantScale = 2f;
            // the groves as CreatureLabScene builds them
            roster.SplitGroves(out _, out List<CreatureSpecies> groves);
            var sets = new List<PlantPrototypeSet>();
            foreach (string p in kGroveSets) { var s = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(p); if (s != null) sets.Add(s); }
            var octos = new List<OctopusLab>();
            if (sets.Count > 0)
                foreach (CreatureSpecies grove in groves)
                {
                    var octo = go.AddComponent<OctopusLab>();
                    octo.octopus = grove; octo.groveSets = sets.ToArray(); octo.treeScale = 1f; octo.logEvents = false; octo.showRoles = false;
                    octo.preySpecies = AssetDatabase.LoadAssetAtPath<CreatureSpecies>("Assets/TerrainGen/Config/Creatures/DesertBeetle.asset");
                    if (grove.cephalopod.MouthCount > 0)
                    {
                        octo.treeScale = 0.6f; octo.groveTrees = 5; octo.canopyOverlap = 0.6f;
                        octo.preySpecies = AssetDatabase.LoadAssetAtPath<CreatureSpecies>("Assets/TerrainGen/Config/Creatures/TreeSpider.asset");
                        octo.preyCount = 4; octo.preyClimbs = true; octo.seed = 47;
                    }
                    octos.Add(octo);
                }

            // the scene's spawn: (0, 160, 0) facing +Z, dropped to the ground (CreatureLab.Start waits for that)
            var player = new GameObject("Player"); trash.Add(player);
            Vector3 origin = new Vector3(0f, Ground(new Vector3(0f, 0f, 0f)), 0f);
            player.transform.SetPositionAndRotation(origin + Vector3.up, Quaternion.identity);
            try
            {
                lab.Place(world, player.transform, origin);
                foreach (OctopusLab octo in octos) octo.Place(world, player.transform);

                var bad = new List<string>();
                int checkedObjs = 0;
                // enclosures: centres on the ground, curbs apart
                CreatureEnclosure[] encs = go.GetComponentsInChildren<CreatureEnclosure>();
                Assert.AreEqual(roster.entries.Count, encs.Length, "one enclosure per roster entry (groves included)");
                var placed = new List<Vector4>();
                foreach (CreatureEnclosure e in encs)
                {
                    Vector3 c = e.transform.position;
                    float dy = c.y - Ground(c); checkedObjs++;
                    if (Mathf.Abs(dy) > kTol) bad.Add($"{e.name}: centre {dy:F2} m off the ground");
                    foreach (Vector4 o in placed)
                    {
                        float d = new Vector2(c.x - o.x, c.z - o.z).magnitude;
                        if (d < e.radius + o.w + LabLayout.kEnclosureGap - 1e-3f) bad.Add($"{e.name} overlaps the enclosure at {(Vector3)o} ({d:F1} m apart, need {e.radius + o.w + LabLayout.kEnclosureGap:F1})");
                    }
                    placed.Add(new Vector4(c.x, c.y, c.z, e.radius));
                }
                // trees: their pivot sunk 0.1 x scale below the ground point
                foreach (EnclosurePlant p in go.GetComponentsInChildren<EnclosurePlant>())
                {
                    Vector3 at = p.transform.position;
                    float dy = at.y + 0.1f * p.scale - Ground(at); checkedObjs++;
                    if (Mathf.Abs(dy) > kTol) bad.Add($"{p.transform.parent.name}/{p.name}: base {dy:F2} m off the ground");
                }
                // rocks (pivot at the base, sunk 0.08 x height) and aviary posts (a cylinder of half-height localScale.y)
                foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    Transform t = mf.transform;
                    if (t.name == "Rock" || t.name == "Rock perch")
                    {
                        float dy = t.position.y + 0.08f * mf.sharedMesh.bounds.size.y - Ground(t.position); checkedObjs++;
                        if (Mathf.Abs(dy) > kTol + 0.02f * mf.sharedMesh.bounds.size.y) bad.Add($"{t.parent.name}/{t.name}: base {dy:F2} m off the ground");
                    }
                    else if (t.name == "Aviary post")
                    {
                        float dy = t.position.y - t.localScale.y - Ground(t.position); checkedObjs++;
                        if (Mathf.Abs(dy) > kTol) bad.Add($"{t.parent.name}/post: base {dy:F2} m off the ground");
                    }
                }
                // creatures: inside their own enclosure and not under the ground
                foreach (CreatureSim s in world.Sims)
                {
                    Vector3 h = s.Joints[0];
                    if (h.y < Ground(h) - 0.05f) bad.Add($"{s.Species.name} spawned under the ground ({h})");
                    if (s.Bounds.HasValue)
                    {
                        EnclosureArea a = s.Bounds.Value;
                        float d = new Vector2(h.x - a.center.x, h.z - a.center.z).magnitude;
                        if (d > a.radius) bad.Add($"{s.Species.name} spawned outside its enclosure ({d:F1} m out of {a.radius:F1})");
                    }
                }
                foreach (OctopusLab octo in octos)
                {
                    if (octo.Octopus == null) { bad.Add(octo.octopus.name + ": no octopus spawned"); continue; }
                    Vector3 h = octo.Octopus.Body.HeadCentre, c = octo.Enclosure.transform.position;
                    if (new Vector2(h.x - c.x, h.z - c.z).magnitude > octo.Enclosure.radius) bad.Add(octo.octopus.name + " spawned outside its grove");
                    if (h.y < Ground(h)) bad.Add(octo.octopus.name + " spawned under the ground");
                }
                // the player's spot: outside every curb
                Vector3 pl = player.transform.position;
                if (!LabLayout.Clear(pl, Vector3.up, 1f, placed)) bad.Add($"the player stands inside an enclosure at {pl}");
                if (pl.y < Ground(pl) - 0.05f) bad.Add($"the player is under the ground at {pl}");

                TestContext.Out.WriteLine($"{encs.Length} enclosures, {checkedObjs} objects checked; origin {origin}");
                Assert.IsEmpty(bad, string.Join("\n", bad));
            }
            finally
            {
                typeof(CreatureWorld).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(world, null);
            }
        }
    }
}
