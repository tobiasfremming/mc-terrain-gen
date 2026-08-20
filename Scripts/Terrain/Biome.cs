using UnityEngine;

// One biome = terrain shape + surface material, as a designer-editable asset.
// Everything that defines a biome lives here (or on the referenced terrain
// field, whose subclass exposes all its shape variables in the inspector):
//
//   terrain       - the density field: a heightfield (dunes) or a pure 3D
//                   SDF-style volume field (canyon, alien rock)
//   surfaceStyle  - which shading module in SandTerrain.shader renders this
//                   biome (Sand/Canyon/Alien/Frost). THIS is what makes the
//                   shader data-driven: it travels with the Biome asset, not
//                   with the asset's position in BiomeWorld's biomes list.
//                   Swap DesertBiome and FrostBiome's positions in that list
//                   and each still renders with ITS OWN style.
//   hardness      - 0 = soft (footprints sink in, e.g. sand)
//                   1 = hard (no footprints, e.g. canyon rock)
//                   blended smoothly across biome transitions
//   palette / textures - pushed to the terrain material at startup, for
//                   whichever vertex-color channel (0=implicit, R, G, B) this
//                   biome ends up occupying at runtime
//
// Adding a world biome = create one of these + add it to the BiomeWorld list
// (order only affects density-blend/region weighting, never shading).
// Edits to any value hot-reload the terrain in play mode (TerrainTuning).
[CreateAssetMenu(fileName = "Biome", menuName = "Marching Cubes/Biome")]
public class Biome : ScriptableObject
{
    public string displayName;

    [Header("Terrain shape")]
    public DensityField terrain;

    [Header("Region")]
    [Tooltip("Positive = this biome claims more of the world.")]
    public float bias = 0f;

    // Must match SandTerrain.shader's style dispatch (0=Sand,1=Canyon,2=Alien,3=Frost).
    public enum SurfaceStyle { Sand, Canyon, Alien, Frost }

    [Header("Surface material")]
    [Tooltip("Which shading module renders this biome's surface. Independent of this biome's position in the BiomeWorld list.")]
    public SurfaceStyle surfaceStyle = SurfaceStyle.Sand;
    [Tooltip("0 = soft sand (deep footprints) ... 1 = hard rock (no footprints).")]
    [Range(0f, 1f)] public float hardness = 0f;
    [Tooltip("How sharply this biome's shading cross-fades in, independent of BiomeDensityField.sharpness (which only shapes the terrain surface). 1 = no change from the raw vertex-baked weight.")]
    [Range(0.2f, 6f)] public float blendSharpness = 1f;
    public Color colorFlat = new Color(0.83f, 0.55f, 0.26f);
    public Color colorSteep = new Color(0.68f, 0.40f, 0.17f);
    [Tooltip("Used when surfaceStyle is Sand or Alien (triplanar-textured styles). Canyon/Frost are procedural and ignore these. If more than one biome shares a style, whichever one occupies a vertex-color channel at runtime supplies the texture for that style.")]
    public Texture2D albedo;
    public Texture2D normalMap;

    void OnValidate() => TerrainTuning.NotifyChanged();
}

// Central "terrain settings changed" signal so inspector tweaks to any biome
// or density-field asset hot-reload the world while playing.
public static class TerrainTuning
{
    public static event System.Action Changed;
    public static void NotifyChanged() => Changed?.Invoke();
}
