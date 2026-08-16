using UnityEngine;

// One biome = terrain shape + surface material, as a designer-editable asset.
// Everything that defines a biome lives here (or on the referenced terrain
// field, whose subclass exposes all its shape variables in the inspector):
//
//   terrain   - the density field: a heightfield (dunes) or a pure 3D
//               SDF-style volume field (canyon, alien rock)
//   hardness  - 0 = soft (footprints sink in, e.g. sand)
//               1 = hard (no footprints, e.g. canyon rock)
//               blended smoothly across biome transitions
//   palette / textures - pushed to the terrain material at startup
//
// Adding a world biome = create one of these + add it to the BiomeWorld list.
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

    [Header("Surface material")]
    [Tooltip("0 = soft sand (deep footprints) ... 1 = hard rock (no footprints).")]
    [Range(0f, 1f)] public float hardness = 0f;
    public Color colorFlat = new Color(0.83f, 0.55f, 0.26f);
    public Color colorSteep = new Color(0.68f, 0.40f, 0.17f);
    [Tooltip("Optional per-biome detail textures (must share size/format if you later extend the shader to texture arrays). Currently biome 0's textures drive the shared triplanar set.")]
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
