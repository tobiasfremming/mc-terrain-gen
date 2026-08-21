using UnityEngine;

[CreateAssetMenu(fileName="WorldConfig", menuName="Marching Cubes/World Config")]
public class WorldConfig : ScriptableObject
{
    [Header("Grid & Space")]
    public Vector3Int cellsPerChunk = new(32,32,32);
    public float cellSize = 0.5f;
    public float isoLevel = 0f;
    [Range(1,8)] public int densitySampling = 1;
    public int viewRadius = 8;

    public bool showDebugInfo = true;

    [Header("Clipmap LOD")]
    [Tooltip("Number of LOD rings. Each ring's chunks cover 2x the world size of the previous; view distance is roughly chunk size * chunksPerLevel * 2^(levels-1) / 2.")]
    [Range(1, 12)] public int clipmapLevels = 6;
    [Tooltip("Chunks per axis in each ring's box. Must be even and >= 6 (box alignment math). 6 => 189 chunks per ring.")]
    public int chunksPerLevel = 6;

    [Header("Level of Detail (legacy, unused)")]
    public float[] lodDistances = { 6f, 12f, 24f, 48f };   // superseded by the clipmap

    // NOTE: lodSamplings/lodCellCounts are no longer used. Each LOD level now
    // simply halves cellsPerChunk (computed in MCChunkManager) so that adjacent
    // LODs always have the exact 2:1 resolution ratio the Transvoxel
    // transition cells require.
    [System.Obsolete("Unused - LOD resolution is derived from cellsPerChunk >> lodLevel")]
    public int[] lodSamplings = { 1, 2, 4, 4 };
    [System.Obsolete("Unused - LOD resolution is derived from cellsPerChunk >> lodLevel")]
    public Vector3Int[] lodCellCounts = {
        new Vector3Int(32, 32, 32),
        new Vector3Int(16, 16, 16),
        new Vector3Int(8, 8, 8),
        new Vector3Int(4, 4, 4),
    };
public bool useAdaptiveLOD = true;
public float lodUpdateInterval = 0.5f; // How often to recalculate LOD

    [Header("Noise & Determinism")]
    public int worldSeed = 1337;
    public Vector2 noiseOffset2D = new(10000f, 10000f);

    [Header("Environment (shared with shaders/VFX)")]
    public Vector3 windDir = new(1,0,0);
    public float windStrength = 1f;
    public float seaLevel = 0f;
    public Vector2 slopeThresholds = new(0.35f, 0.6f); // sand↔rock mask
    public float triplanarScale = 1f;


    [Header("Defaults")]
    public DensityField defaultDensity;

    [Header("Planet Mode")]
    [Tooltip("Render the world as a sphere instead of a flat plane.")]
    public bool useGlobe = false;
    [Tooltip("Used when Use Globe is on, instead of defaultDensity. A PlanetField already wraps its own terrain (its Surface field) plus radius/center -- point that at whatever BiomeWorld you want the globe to use (typically the same one defaultDensity points at, but doesn't have to be).")]
    public PlanetField planetField;

    // What MCChunkManager actually generates from. Kept as one indirection so
    // toggling useGlobe is the only thing callers need to check.
    public DensityField EffectiveDensity => (useGlobe && planetField != null) ? (DensityField)planetField : defaultDensity;

    [Header("Debug")]
    public bool showChunkBounds = true;
    public int configVersion = 1;

    private void OnValidate()
    {
        cellsPerChunk = new Vector3Int(
            Mathf.Max(1, cellsPerChunk.x),
            Mathf.Max(1, cellsPerChunk.y),
            Mathf.Max(1, cellsPerChunk.z));
        cellSize = Mathf.Max(0.0001f, cellSize);
        densitySampling = Mathf.Max(1, densitySampling);
        windDir = (windDir.sqrMagnitude < 1e-6f) ? new Vector3(1,0,0) : windDir.normalized;
        TerrainTuning.NotifyChanged(); // so flipping Use Globe hot-reloads in Play mode, same as any Biome tweak
    }
}
