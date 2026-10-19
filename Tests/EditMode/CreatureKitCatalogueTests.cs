using System;
using NUnit.Framework;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Creature kit T2.6: the generated catalogue (Config/Creatures/CATALOGUE.md, catalogue.json) is checked in and
    // must match what the part/module types and the species assets produce now. Stale: regenerate (Unity menu
    // Marching Cubes > Creature Kit > Regenerate Catalogue, or `CreatureHarness catalogue --write`) and commit.
    public class CreatureKitCatalogueTests
    {
        static string Read(string assetPath)
        {
            string full = CreatureKitAssets.FullPath(assetPath);
            return System.IO.File.Exists(full) ? System.IO.File.ReadAllText(full).Replace("\r\n", "\n") : null;
        }

        static void Build(out string md, out string json) => CatalogueWriter.Build(CreatureKitAssets.CatalogueSpecies(), out md, out json);

        internal static string FreshProblem(string want, string have, string what)
        {
            if (have == null) return what + " missing";
            if (want == have) return null;
            string[] a = want.Split('\n'), b = have.Split('\n');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (a[i] != b[i]) return $"{what} stale at line {i + 1}:\n  generated: {a[i]}\n  checked in: {b[i]}";
            return $"{what} stale: {a.Length} generated lines vs {b.Length} checked in";
        }

        [Test]
        public void Catalogue_IsFresh()
        {
            Build(out string md, out string json);
            string hint = "\nregenerate: menu Marching Cubes > Creature Kit > Regenerate Catalogue, or CreatureHarness catalogue --write";
            Assert.IsNull(FreshProblem(md, Read(CatalogueWriter.MarkdownPath), "CATALOGUE.md"), hint);
            Assert.IsNull(FreshProblem(json, Read(CatalogueWriter.JsonPath), "catalogue.json"), hint);
        }

        // RED: an edited catalogue is reported stale, at the edited line.
        [Test]
        public void Red_Catalogue_Edited_IsStale()
        {
            Build(out string md, out _);
            string edited = md.Replace("| **Leg2Bone** |", "| **Leg2Bones** |");
            string p = FreshProblem(md, edited, "CATALOGUE.md");
            TestContext.Out.WriteLine(p);
            StringAssert.Contains("Leg2Bones", p);
        }

        [Test]
        public void Catalogue_ListsEveryPartAndUsedBy()
        {
            Build(out string md, out string json);
            foreach (Type t in KitTypes.Parts) { StringAssert.Contains("| **" + KitTypes.NameOf(t) + "** |", md); StringAssert.Contains("### " + KitTypes.NameOf(t) + "\n", md); }
            foreach (Type t in KitTypes.Modules) { StringAssert.Contains("| **" + KitTypes.NameOf(t) + "** |", md); StringAssert.Contains("\"name\": \"" + KitTypes.NameOf(t) + "\"", json); }
            // used by: the walkers' parts name their species
            string spine = Row(md, "| **SpineBody** |"), arthro = Row(md, "| **ArthropodLeg** |"), wing = Row(md, "| **Wing** |"), soft = Row(md, "| **SoftBody** |");
            StringAssert.Contains("DesertMonitor", spine); StringAssert.Contains("KitMonitor", spine);
            StringAssert.Contains("Tarantula", arthro); StringAssert.DoesNotContain("DesertMonitor", arthro);
            StringAssert.Contains("WingedSerpent", wing);
            StringAssert.Contains("TreeOctopus", soft);
            // parameters carry units and size rules; the registry names the next free layer
            StringAssert.Contains("| `upperLength` | float | Metres | x s |", md);
            StringAssert.Contains("| `stepDuration` | float | Seconds | x sqrt(s) |", md);
            StringAssert.Contains("| `segments[].length` | float | Metres | x s |", md);
            StringAssert.Contains("next free: " + PartLayers.NextFree, md);   // grows as parts take ids (12-14: the big lizards)
            StringAssert.Contains("| KitMonitor | kit | Spine |", md);
        }

        static string Row(string md, string start)
        {
            int i = md.IndexOf(start, StringComparison.Ordinal);
            Assert.GreaterOrEqual(i, 0, start);
            int e = md.IndexOf('\n', i);
            return md.Substring(i, e - i);
        }
    }
}
