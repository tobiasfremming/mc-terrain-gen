using System.IO;
using LSystems;
using UnityEditor;
using UnityEngine;

// Runs the grammar N times, sweeps each result into meshes at every LOD, and
// writes the lot as one asset.
//
// Everything lives as SUB-ASSETS of the set: a species is one file on disk
// rather than 48 loose meshes, and deleting the set cannot orphan them.
//
// The meshes PlantMeshBuilder hands back are marked DontSaveInEditor, because
// its usual caller is a live preview that must never bloat a scene. A baked
// mesh is the exact opposite -- it exists to be saved -- so the flag is cleared
// here. Missing that produces an asset file containing nothing, silently.
public static class PlantPrototypeBaker
{
    // Rough ceiling for something you intend to scatter thousands of. Not
    // enforced -- a hero tree can exceed it deliberately -- but it must be said
    // out loud, because nothing else in the pipeline notices.
    public const int kTriangleBudget = 20000;

    public static PlantPrototypeSet Bake(PlantProfile profile, int variantCount, int lodCount, string assetPath)
    {
        if (profile == null || profile.grammar == null || !profile.grammar.IsValid) return null;
        variantCount = Mathf.Clamp(variantCount, 1, 64);
        lodCount = Mathf.Clamp(lodCount, 1, 3);

        LSystemGrammar g = profile.grammar.Grammar;
        if (g == null) return null;

        PlantPrototypeSet set = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(assetPath);
        bool fresh = set == null;
        if (fresh) set = ScriptableObject.CreateInstance<PlantPrototypeSet>();
        else foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            if (sub != set && sub != null) Object.DestroyImmediate(sub, true); // drop the previous bake's meshes

        set.source = profile;
        set.variants = new PlantVariant[variantCount];
        set.lodTriangles = new int[lodCount];

        var rewriter = new LSystemRewriter { MaxModules = Mathf.Max(1000, profile.maxModules) };
        LSkeleton skel = null;
        int iters = profile.iterations >= 0 ? profile.iterations : profile.grammar.EffectiveIterations;
        float maxHeight = 0f;

        try
        {
            for (int v = 0; v < variantCount; v++)
            {
                EditorUtility.DisplayProgressBar("Baking " + profile.name,
                    "variant " + (v + 1) + " / " + variantCount, v / (float)variantCount);

                // One derivation per variant, reused across LODs -- the LODs
                // must be the SAME plant with less detail, not different plants.
                uint seed = profile.seed + (uint)v * 7919u;
                LModuleString word = rewriter.Rewrite(g, iters, seed);
                skel = TurtleInterpreter.Build(word, profile.turtle, skel);

                var variant = new PlantVariant { seed = seed, lods = new Mesh[lodCount] };
                for (int l = 0; l < lodCount; l++)
                {
                    PlantMeshLod lod = l == 0 ? PlantMeshLod.Lod0 : (l == 1 ? PlantMeshLod.Lod1 : PlantMeshLod.Lod2);
                    Mesh m = PlantMeshBuilder.Build(skel, profile, lod);   // fresh mesh, never the reuse buffer
                    m.hideFlags = HideFlags.None;                          // baked meshes are saved, unlike previews
                    m.name = profile.name + "_v" + v + "_LOD" + l;
                    m.Optimize();                                          // reorder for the vertex cache
                    variant.lods[l] = m;
                    set.lodTriangles[l] += (int)((m.GetIndexCount(0) + m.GetIndexCount(1)) / 3);
                }
                variant.bounds = variant.lods[0].bounds;
                maxHeight = Mathf.Max(maxHeight, variant.bounds.max.y);
                set.variants[v] = variant;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        set.maxHeight = maxHeight;
        set.bakedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        set.lod0TrianglesPerVariant = set.lodTriangles.Length > 0 ? set.lodTriangles[0] / Mathf.Max(1, variantCount) : 0;

        Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
        if (fresh) AssetDatabase.CreateAsset(set, assetPath);

        set.barkMaterial = EnsureMaterial(set, set.barkMaterial, "Bark", profile.stemColor);
        Color leaf = profile.stemColor;
        foreach (var p in profile.parts)
            if (p != null && p.enabled && p.shape != PlantPartShape.None) { leaf = p.color; break; }
        set.leafMaterial = EnsureMaterial(set, set.leafMaterial, "Leaf", leaf);

        foreach (var variant in set.variants)
            foreach (var m in variant.lods)
                AssetDatabase.AddObjectToAsset(m, set);

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath);

        // Size is not a detail here. This project serializes assets as TEXT
        // (EditorSettings m_SerializationMode: 2), so a mesh is written as
        // YAML and a heavy prototype set runs to hundreds of megabytes -- which
        // then goes into git. Baking the fractal test grammars produced a
        // 241 MB asset before this check existed.
        var info = new FileInfo(assetPath);
        set.assetBytes = info.Exists ? info.Length : 0;
        EditorUtility.SetDirty(set);

        if (set.lod0TrianglesPerVariant > kTriangleBudget)
            Debug.LogWarning("[PlantPrototypeBaker] '" + profile.name + "' bakes to "
                + set.lod0TrianglesPerVariant.ToString("N0") + " triangles per variant at LOD0 ("
                + (set.assetBytes / 1048576f).ToString("F0") + " MB on disk). World vegetation wants roughly "
                + kTriangleBudget.ToString("N0") + " or fewer -- lower the grammar's iterations, or treat this as a "
                + "hero asset rather than something to scatter.", set);
        return set;
    }

    // Placeholder materials so a freshly baked species renders immediately.
    // Step 5 replaces these with the real bark/leaf-atlas art; keeping them as
    // sub-assets means swapping them is a field change, not a rebake.
    static Material EnsureMaterial(PlantPrototypeSet set, Material existing, string name, Color color)
    {
        Material m = existing;
        if (m == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = set.name + " " + name };
            AssetDatabase.AddObjectToAsset(m, set);
        }
        // Without this Graphics.RenderMeshInstanced throws outright:
        // "Material needs to enable instancing". Every prototype material
        // exists to be instanced, so it is never not wanted.
        m.enableInstancing = true;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        EditorUtility.SetDirty(m);
        return m;
    }
}

// The bake controls live on the profile, because that is the asset you are
// already looking at when you decide a plant is finished.
[CustomEditor(typeof(PlantProfile))]
public class PlantProfileEditor : Editor
{
    int _variants = 8;
    int _lods = 3;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var profile = (PlantProfile)target;

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Bake prototypes", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Freezes this profile into meshes the world can instance. Runtime never derives the grammar again.",
                EditorStyles.wordWrappedMiniLabel);

            _variants = EditorGUILayout.IntSlider("Variants", _variants, 1, 32);
            _lods = EditorGUILayout.IntSlider("LOD levels", _lods, 1, 3);

            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile)).Replace('\\', '/') + "/Prototypes";
            string path = dir + "/" + profile.name + "_Prototypes.asset";
            EditorGUILayout.LabelField("output", path, EditorStyles.miniLabel);

            bool ok = profile.grammar != null && profile.grammar.IsValid;
            using (new EditorGUI.DisabledScope(!ok))
                if (GUILayout.Button(ok ? "Bake " + _variants + " variants x " + _lods + " LODs" : "Grammar does not parse"))
                {
                    var set = PlantPrototypeBaker.Bake(profile, _variants, _lods, path);
                    if (set != null) { Selection.activeObject = set; EditorGUIUtility.PingObject(set); }
                }

            var existing = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(path);
            if (existing != null)
            {
                var sb = new System.Text.StringBuilder("Baked " + existing.bakedAt + " — "
                    + existing.variants.Length + " variants, height " + existing.maxHeight.ToString("F1") + " m\ntriangles: ");
                for (int i = 0; i < existing.lodTriangles.Length; i++)
                    sb.Append("LOD" + i + " " + existing.lodTriangles[i].ToString("N0") + "   ");
                EditorGUILayout.HelpBox(sb.ToString(), MessageType.Info);
            }
        }
    }
}
