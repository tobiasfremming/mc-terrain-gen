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

    [Header("Level of Detail")]
    public float[] lodDistances = { 6f, 12f, 24f, 48f };   // VERY strict distance thresholds
    public int[] lodSamplings = { 1, 2, 4, 8 };            // Density sampling per LOD
    public Vector3Int[] lodCellCounts = {                   // Cells per LOD (optional)
        new Vector3Int(32, 32, 32),  // LOD 0: Full detail (center chunk only)
        new Vector3Int(24, 24, 24),  // LOD 1: High detail (immediate neighbors)
        new Vector3Int(16, 16, 16),  // LOD 2: Medium detail (middle ring)
        new Vector3Int(8, 8, 8)      // LOD 3: Low detail (outer ring)
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
    }
}
