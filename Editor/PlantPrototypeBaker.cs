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

    // Impostor atlas defaults. 12 frames is where two-frame blending stops
    // ghosting on a tree; 128 px per frame is 4x what a 10 m tree needs at
    // the distance it switches to the card.
    public const int kImpostorFrames = 12;
    public const int kImpostorFrameSize = 128;

    public static PlantPrototypeSet Bake(PlantProfile profile, int variantCount, int lodCount, string assetPath,
                                         bool impostors = true)
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

        if (impostors) BakeImpostors(set, profile, assetPath);
        else
        {
            set.impostorAtlas = null;
            set.impostorMaterial = null;
            set.impostorFrames = 0;
        }

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

    // ---- impostors -----------------------------------------------------------
    //
    // Every variant's LOD0 mesh rendered from kImpostorFrames angles around
    // its up axis into one atlas: a row per variant, a column per frame. At
    // runtime a plant past impostorDistance is a single quad showing the
    // frame nearest the view angle (see PlantImpostor.shader), which is what
    // lets a species stay visible to the horizon for two triangles each.
    //
    // Rendered through a CommandBuffer rather than a temporary Camera: a
    // camera renders the open scene too, and hiding a planet from it is more
    // work than drawing two meshes by hand. The atlas is written as a PNG next
    // to the set rather than as a sub-asset, because this project serializes
    // assets as text and a 2048x1024 texture as YAML hex is 16 MB.
    static void BakeImpostors(PlantPrototypeSet set, PlantProfile profile, string assetPath)
    {
        Shader bakeShader = Shader.Find("Hidden/MarchingCubes/Plant Impostor Bake");
        Shader cardShader = Shader.Find("MarchingCubes/Plant Impostor");
        if (bakeShader == null || cardShader == null)
        {
            Debug.LogWarning("[PlantPrototypeBaker] impostor shaders not found (Shaders/PlantImpostor*.shader) -- skipping impostors", set);
            return;
        }

        int frames = kImpostorFrames, fs = kImpostorFrameSize;
        int variants = set.variants.Length;
        int atlasW = frames * fs, atlasH = variants * fs;

        Color bark = MaterialColor(set.barkMaterial, profile.stemColor);
        Color leaf = MaterialColor(set.leafMaterial, bark);

        var bakeMat = new Material(bakeShader) { hideFlags = HideFlags.HideAndDontSave };
        var barkProps = new MaterialPropertyBlock(); barkProps.SetColor("_Color", bark);
        var leafProps = new MaterialPropertyBlock(); leafProps.SetColor("_Color", leaf);

        var rt = new RenderTexture(atlasW, atlasH, 24, RenderTextureFormat.ARGB32) { name = "ImpostorBake" };
        rt.Create();

        // Background: the leaf colour at alpha 0. Bilinear filtering and
        // mipmaps blend edge texels with their neighbours, and blending toward
        // black paints a dark halo round every card; toward the plant's own
        // colour it is invisible.
        var cmd = new UnityEngine.Rendering.CommandBuffer { name = "PlantImpostorBake" };
        cmd.SetRenderTarget(rt);
        cmd.ClearRenderTarget(true, true, new Color(leaf.r, leaf.g, leaf.b, 0f));

        var cards = new Mesh[variants];
        for (int v = 0; v < variants; v++)
        {
            PlantVariant variant = set.variants[v];
            Mesh mesh = variant.lods[0];
            Bounds b = variant.bounds;

            // The card is centred on the plant's axis (its root is the origin)
            // and spans its full height. Horizontal reach is the farthest
            // bounds corner from the axis, so no view angle clips a branch.
            float r = 0f;
            for (int c = 0; c < 4; c++)
            {
                float x = (c & 1) == 0 ? b.min.x : b.max.x;
                float z = (c & 2) == 0 ? b.min.z : b.max.z;
                r = Mathf.Max(r, Mathf.Sqrt(x * x + z * z));
            }
            float h = Mathf.Max(b.size.y, 1e-3f);
            r = Mathf.Max(r, 1e-3f) * 1.04f;
            float yMin = b.min.y - h * 0.02f, yMax = b.max.y + h * 0.02f;
            float yMid = 0.5f * (yMin + yMax);
            float halfH = 0.5f * (yMax - yMin);
            float dist = 2f * r + halfH + 1f;

            for (int i = 0; i < frames; i++)
            {
                // Same angle convention the shader decodes: frame i looks at
                // the plant from atan2(x, z) = 2*pi*i/N.
                float a = i * (2f * Mathf.PI / frames);
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 camPos = new Vector3(0f, yMid, 0f) + dir * dist;
                Quaternion camRot = Quaternion.LookRotation(-dir, Vector3.up);
                // Unity's view matrix: inverse camera transform with Z flipped.
                Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(camPos, camRot, Vector3.one).inverse;
                // Rendering into a texture through a raw command buffer on a
                // UV-starts-at-top API (D3D, Metal, Vulkan) lands each frame
                // upside down in the readback while the viewport rows stay in
                // order -- measured, not assumed: a tip-up triangle came back
                // tip-down in row 0. Flipping the projection's vertical axis
                // corrects the content without touching the rows.
                float flip = SystemInfo.graphicsUVStartsAtTop ? -1f : 1f;
                Matrix4x4 proj = Matrix4x4.Ortho(-r, r, -halfH * flip, halfH * flip, 0.01f, 2f * dist + 1f);

                cmd.SetViewport(new Rect(i * fs, v * fs, fs, fs));
                cmd.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(proj, true));
                cmd.DrawMesh(mesh, Matrix4x4.identity, bakeMat, 0, 0, barkProps);
                if (mesh.subMeshCount > 1) cmd.DrawMesh(mesh, Matrix4x4.identity, bakeMat, 1, 0, leafProps);
            }

            cards[v] = BuildCard(r, yMin, yMax, frames, v, variants);
            cards[v].name = profile.name + "_v" + v + "_Impostor";
        }

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        var tex = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, atlasW, atlasH), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(bakeMat);

        string pngPath = Path.ChangeExtension(assetPath, null) + "_Impostor.png";
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);

        if (AssetImporter.GetAtPath(pngPath) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096;
            ti.SaveAndReimport();
        }
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);

        Material card = set.impostorMaterial;
        if (card == null)
        {
            card = new Material(cardShader) { name = set.name + " Impostor" };
            AssetDatabase.AddObjectToAsset(card, set);
        }
        card.shader = cardShader;
        card.enableInstancing = true;
        card.SetTexture("_MainTex", atlas);
        card.SetFloat("_Frames", frames);
        // Cards read brighter than the meshes they replace: a mesh trunk is
        // mostly sky-lit, a card is lit as one up-facing surface. 0.3 matched
        // best against URP Lit at a low sun; raise it if cards go dark at noon.
        card.SetFloat("_Ambient", 0.3f);
        EditorUtility.SetDirty(card);

        for (int v = 0; v < variants; v++)
        {
            set.variants[v].impostor = cards[v];
            AssetDatabase.AddObjectToAsset(cards[v], set);
        }
        set.impostorAtlas = atlas;
        set.impostorMaterial = card;
        set.impostorFrames = frames;
    }

    // The quad the impostor shader turns toward the camera. x is the card's
    // horizontal axis (the shader rotates it about the plant's Y), y is
    // height in the plant's own metres. uv.x covers one frame; the shader
    // adds the frame offset. uv.y is this variant's row.
    static Mesh BuildCard(float r, float yMin, float yMax, int frames, int row, int rows)
    {
        float u1 = 1f / frames;
        float v0 = row / (float)rows, v1 = (row + 1) / (float)rows;
        var m = new Mesh { name = "Impostor" };
        m.SetVertices(new[]
        {
            new Vector3(-r, yMin, 0f), new Vector3(r, yMin, 0f), new Vector3(r, yMax, 0f), new Vector3(-r, yMax, 0f),
        });
        m.SetUVs(0, new[] { new Vector2(0f, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(0f, v1) });
        m.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        // The card can face any way, so its bounds are the cylinder it sweeps.
        m.bounds = new Bounds(new Vector3(0f, 0.5f * (yMin + yMax), 0f), new Vector3(2f * r, yMax - yMin, 2f * r));
        return m;
    }

    static Color MaterialColor(Material m, Color fallback)
    {
        if (m == null) return fallback;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color")) return m.GetColor("_Color");
        return fallback;
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
    bool _impostors = true;

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
            _impostors = EditorGUILayout.Toggle(new GUIContent("Bake impostors",
                "Also render every variant from " + PlantPrototypeBaker.kImpostorFrames + " angles into an atlas, so the "
                + "species can be drawn as a single card beyond PlantWorld's impostorDistance."), _impostors);

            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile)).Replace('\\', '/') + "/Prototypes";
            string path = dir + "/" + profile.name + "_Prototypes.asset";
            EditorGUILayout.LabelField("output", path, EditorStyles.miniLabel);

            bool ok = profile.grammar != null && profile.grammar.IsValid;
            using (new EditorGUI.DisabledScope(!ok))
                if (GUILayout.Button(ok ? "Bake " + _variants + " variants x " + _lods + " LODs" : "Grammar does not parse"))
                {
                    var set = PlantPrototypeBaker.Bake(profile, _variants, _lods, path, _impostors);
                    if (set != null) { Selection.activeObject = set; EditorGUIUtility.PingObject(set); }
                }

            var existing = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(path);
            if (existing != null)
            {
                var sb = new System.Text.StringBuilder("Baked " + existing.bakedAt + " — "
                    + existing.variants.Length + " variants, height " + existing.maxHeight.ToString("F1") + " m\ntriangles: ");
                for (int i = 0; i < existing.lodTriangles.Length; i++)
                    sb.Append("LOD" + i + " " + existing.lodTriangles[i].ToString("N0") + "   ");
                sb.Append(existing.HasImpostor
                    ? "\nimpostors: " + existing.impostorFrames + " frames, " + (existing.impostorAtlas != null
                        ? existing.impostorAtlas.width + "x" + existing.impostorAtlas.height + " atlas" : "atlas MISSING -- rebake")
                    : "\nimpostors: none -- this species stops at cullDistance instead of switching to a card");
                EditorGUILayout.HelpBox(sb.ToString(), MessageType.Info);
            }
        }
    }
}
