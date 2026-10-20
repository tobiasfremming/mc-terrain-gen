using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Creature kit T3.5: the lab roster (Config/Creatures/CreatureLabRoster.asset) replaces the Rebuild menu's
    // species list and the lab's per-type ifs; each entry's habitat comes from its species' modules.
    public class CreatureKitLabTests
    {
        static CreatureLabRoster Roster()
        {
            var r = AssetDatabase.LoadAssetAtPath<CreatureLabRoster>(CreatureLabRoster.AssetPath);
            Assert.IsNotNull(r, CreatureLabRoster.AssetPath);
            return r;
        }

        [Test] public void Roster_Loads_EveryEntryResolves()
        {
            CreatureLabRoster r = Roster();
            Assert.Greater(r.entries.Count, 0);
            var seen = new HashSet<string>();
            foreach (CreatureLabRoster.Entry e in r.entries)
            {
                Assert.IsNotNull(e.species, "an entry names no species (a missing asset?)");
                Assert.IsTrue(seen.Add(e.species.name), e.species.name + " is listed twice");
                Assert.IsNull(e.species.Validate(), e.species.name);
                Assert.GreaterOrEqual(e.scaleOverride, 0f, e.species.name);
                Assert.That(e.count >= 0 && e.count <= 12 && e.rocks >= 0 && e.rocks <= 16, e.species.name + ": count 0..12, rocks 0..16");
                Assert.IsFalse(CreatureKitAssets.IsTestFixture(e.species.name), e.species.name + " is a test fixture, not a lab animal");
            }
        }

        // A variant is a new animal, never a replacement (2026-10-20, the user: "I wanted a new large variant, not to
        // overwrite it"): the StriderMatriarch joins the lab next to the small GolfballStrider, which keeps its own
        // asset, its scale 1 and its own enclosure.
        [Test] public void Roster_KeepsTheGolfballStrider_NextToItsMatriarch()
        {
            CreatureSpecies small = null, giant = null; float smallOverride = 0f;
            foreach (CreatureLabRoster.Entry e in Roster().entries)
            {
                if (e.species == null) continue;
                if (e.species.name == "GolfballStrider") { small = e.species; smallOverride = e.scaleOverride; }
                if (e.species.name == "StriderMatriarch") giant = e.species;
            }
            Assert.IsNotNull(small, "the small GolfballStrider is in the lab");
            Assert.IsNotNull(giant, "the StriderMatriarch is in the lab");
            Assert.AreNotSame(small, giant, "two assets, not one");
            Assert.AreEqual(1f, small.Compiled.Runtime.scale, 1e-6f, "the small one keeps its size");
            Assert.That(smallOverride <= 1.5f, "no override blows the small one up");
            Assert.Greater(giant.Compiled.Runtime.scale, 4f * small.Compiled.Runtime.scale, "the matriarch is the giant");
            Assert.AreNotEqual(Habitat.Grove, LabHabitat.For(small), "the small one gets an enclosure of its own");
        }

        // Adding to the roster is the last step of the new-animal workflow: a species asset that is not a test
        // fixture and not in the roster is reported (a warning: a work-in-progress animal may wait).
        [Test] public void Roster_ContainsEveryNonTestSpecies()
        {
            var listed = new HashSet<string>();
            foreach (CreatureLabRoster.Entry e in Roster().entries) if (e.species != null) listed.Add(e.species.name);
            var missing = new List<string>();
            foreach (string n in CreatureKitAssets.Species())
                if (!CreatureKitAssets.IsTestFixture(n) && !listed.Contains(n)) missing.Add(n);
            TestContext.WriteLine("roster: " + string.Join(", ", listed));
            if (missing.Count > 0) Debug.LogWarning("[CreatureKitLabTests] not in the lab roster (CreatureLabRoster.asset): " + string.Join(", ", missing));
        }

        // Habitat from the modules equals the pre-kit rule from the legacy fields for every asset, and the shipped
        // animals get the habitats the lab gave them.
        [Test] public void Habitat_Resolves()
        {
            var expect = new Dictionary<string, Habitat>
            {
                { "DesertMonitor", Habitat.Climbable }, { "KitMonitor", Habitat.Climbable }, { "DesertSnake", Habitat.Plain },
                { "DesertBeetle", Habitat.Plain }, { "DaddyLongLegs", Habitat.Plain }, { "Tarantula", Habitat.Plain },
                { "WingedSerpent", Habitat.Aviary }, { "SkyDragon", Habitat.Aviary }, { "TreeOctopus", Habitat.Grove },
                { "FinnedSandEel", Habitat.Plain }, { "GiantCentipede", Habitat.Plain }, { "Hexadrake", Habitat.Aviary },
                { "TentacleCrab", Habitat.Plain }, { "RockCrab", Habitat.Plain }, { "Scolopendra", Habitat.Plain },
                { "Crocodile", Habitat.Plain }, { "KomodoDragon", Habitat.Plain }, { "GolfballStrider", Habitat.Plain },
                { "SkyManta", Habitat.Aviary },
                { "Delver", Habitat.Plain },
                { "Pterodactyl", Habitat.Aviary },
                { "StriderMatriarch", Habitat.Plain },
                { "Apex", Habitat.Aviary },
            };
            foreach (string n in CreatureKitAssets.Species())
            {
                CreatureSpecies asset = CreatureKitAssets.LoadAsset(n);
                Habitat h = LabHabitat.For(asset);
                // an airborne kit module (Soar) has no legacy-field form: its habitat is pinned by the table only
                if (Airborne.Of(asset) == null) Assert.AreEqual((int)CreatureKitAssets.LegacyHabitatOf(asset.Compiled.Runtime), (int)h, n + ": modules vs legacy fields");
                if (expect.TryGetValue(n, out Habitat want)) Assert.AreEqual(want, h, n);
            }
            CreatureLabRoster r = Roster();
            r.Split(out List<CreatureLabRoster.Entry> enclosures, out CreatureSpecies grove);
            Assert.IsNotNull(grove, "the grove animal (OctopusLab)");
            Assert.AreEqual(Habitat.Grove, LabHabitat.For(grove));
            foreach (CreatureLabRoster.Entry e in enclosures) Assert.AreNotEqual(Habitat.Grove, LabHabitat.For(e.species), e.species.name);
        }

        // Flat ground (y = 0) whose normal leans away from one sweet spot: every enclosure is drawn toward it, so
        // neighbours compete for the same patch (CreatureEnclosureTests' probe).
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

        // Every roster entry placed as CreatureLab places it (default spacing, arc, search, the 14 m aviary rule):
        // no two curbs within the enclosure gap, flat ground and a contested sweet spot alike.
        [Test] public void Lab_Placement_NeighboursNeverOverlap()
        {
            CreatureLabRoster r = Roster();
            r.Split(out List<CreatureLabRoster.Entry> list, out _);
            int n = list.Count;
            var radius = new float[n];
            for (int k = 0; k < n; k++)
            {
                CreatureSpecies sp = list[k].species;
                float scale = list[k].scaleOverride > 0f ? list[k].scaleOverride : sp.scale;
                radius[k] = LabHabitat.EnclosureRadius(sp, CreatureLab.MaxScale(sp, scale), 14f);
            }
            const float spacing = 25f, arcRadius = 30f, search = 15f;   // CreatureLab's defaults
            LabLayout.Arc(radius, spacing, arcRadius, search, out _, out float slotRadius);
            foreach (IGroundProbe ground in new IGroundProbe[] { new FlatGroundProbe(), new SweetSpotProbe(new Vector3(0f, 0f, slotRadius)) })
            {
                var placed = new List<Vector4>();
                Vector3[] c = LabLayout.Centres(ground, Vector3.zero, Vector3.forward, Vector3.up, radius, spacing, arcRadius, search, null, placed);
                Assert.AreEqual(n, placed.Count);
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        Vector3 d = c[a] - c[b]; d.y = 0f;
                        Assert.GreaterOrEqual(d.magnitude, radius[a] + radius[b] + LabLayout.kEnclosureGap - 1e-3f,
                            $"{ground.GetType().Name}: {list[a].species.name} and {list[b].species.name} overlap ({d.magnitude:F2} m apart)");
                    }
                // the player stands outside every curb
                for (int k = 0; k < n; k++) Assert.Greater(new Vector2(c[k].x, c[k].z).magnitude, radius[k] + LabLayout.kViewGap - 1e-3f, list[k].species.name);
            }
        }
    }
}
