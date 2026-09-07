using UnityEngine;

// One biome = terrain shape + surface material, as a designer-editable asset.
// Everything that defines a biome lives here (or on the referenced terrain
// field, whose subclass exposes all its shape variables in the inspector):
//
//   terrain       - the density field: a heightfield (dunes) or a pure 3D
//                   SDF-style volume field (canyon, alien rock)
//   surfaceStyle  - which shading module in SandTerrain.shader renders this
//                   biome (Sand/Canyon/Alien/Frost/Dolomite/
//                   Mountain/DesertMountain). THIS is what makes the
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

    // Must match SandTerrain.shader's style dispatch (0=Sand,1=Canyon,2=Alien,3=Frost,4=Dolomite,5=Mountain,6=DesertMountain).
    public enum SurfaceStyle { Sand, Canyon, Alien, Frost, Dolomite, Mountain, DesertMountain }

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

    [Header("Flora")]
    [Tooltip("Species that grow in this biome. Each candidate plant is accepted with probability equal to this biome's weight at its spot, so stands thin out over the few metres where two biomes cross-fade. Species for every biome go on PlantWorld.species instead. Edits rebuild the forest.")]
    public PlantSpecies[] flora = new PlantSpecies[0];

    [Tooltip("Colony grammar: a 2D L-system (yaw and f only) walked over the ground once per plot, plus the neighbouring plots' colonies reaching in, whose markers are species letters from the Flora list. Set, it is the ONLY thing that places this biome's plants and the rarity presets are ignored. Unset: uniform scatter by perPlot/plotChance.")]
    public LSystems.LSystemGrammarAsset colony;
    [Tooltip("Chance a given plot seeds a colony at all. Lower = more empty ground between stands.")]
    [Range(0f, 1f)] public float colonyChance = 0.6f;

    [Tooltip("Metres. Size of the thicket-and-glade pattern that gates every plant of this biome (colony or uniform): a world-anchored noise, so glades run across plot edges.")]
    public float coverScale = 140f;
    [Tooltip("Noise value below which ground is a glade. 0 covers about half the biome; raise it for more open ground.")]
    [Range(-1f, 1f)] public float coverThreshold = 0f;
    [Tooltip("Width of the soft edge between glade and thicket, in noise units.")]
    [Range(0.01f, 1f)] public float coverSoftness = 0.3f;

    [Header("Grass")]
    [Tooltip("Blades per m^2 of flat ground in this biome. 0 = no grass. Scattered on the GPU by TerrainGrass wherever this biome's vertex weight is, thinning out over slopes and across the biome's cross-fade; a Dolomite terrain also stops it at its meadow line.")]
    public float grassDensity = 0f;
    public Color grassColorBase = new Color(0.13f, 0.30f, 0.06f);
    public Color grassColorTip = new Color(0.58f, 0.74f, 0.27f);

    void OnValidate()
    {
        PlantSpecies.ValidateAll(flora);
        coverScale = Mathf.Max(1f, coverScale);
        grassDensity = Mathf.Max(0f, grassDensity);
        TerrainTuning.NotifyChanged();
    }
}

// Central "terrain settings changed" signal so inspector tweaks to any biome
// or density-field asset hot-reload the world while playing.
public static class TerrainTuning
{
    public static event System.Action Changed;
    public static void NotifyChanged() => Changed?.Invoke();
}
