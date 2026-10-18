using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Creature kit T2.6: regenerates Config/Creatures/CATALOGUE.md and catalogue.json (Kit/CatalogueWriter) from the
// part/module types and every species asset in Config/Creatures. Derived, never hand-edited; the EditMode test
// Catalogue_IsFresh fails when they are stale. The offline harness writes the same bytes
// (`CreatureHarness catalogue --write`).
//
// Menu: Marching Cubes / Creature Kit / Regenerate Catalogue. Batch: -executeMethod CreatureKitCatalogue.Generate
public static class CreatureKitCatalogue
{
    const string kFolder = "Assets/TerrainGen/Config/Creatures";

    [MenuItem("Marching Cubes/Creature Kit/Regenerate Catalogue")]
    public static void GenerateFromMenu() => Debug.Log(Generate());

    public static string Generate()
    {
        CreatureKit.CatalogueWriter.Build(Species(), out string md, out string json);
        bool changed = Write(CreatureKit.CatalogueWriter.MarkdownPath, md) | Write(CreatureKit.CatalogueWriter.JsonPath, json);
        if (changed) AssetDatabase.Refresh();
        return changed ? "Creature kit catalogue regenerated: " + CreatureKit.CatalogueWriter.MarkdownPath : "Creature kit catalogue is up to date";
    }

    // every CreatureSpecies under Config/Creatures, by asset name (ordinal), as the kit tests see them
    static List<CreatureSpecies> Species()
    {
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:CreatureSpecies", new[] { kFolder })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort((a, b) => string.CompareOrdinal(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b)));
        var list = new List<CreatureSpecies>();
        foreach (string p in paths) { var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(p); if (sp != null) list.Add(sp); }
        return list;
    }

    static bool Write(string assetPath, string text)
    {
        string full = Path.GetFullPath(assetPath);
        if (File.Exists(full) && File.ReadAllText(full).Replace("\r\n", "\n") == text) return false;
        File.WriteAllText(full, text);
        return true;
    }
}
