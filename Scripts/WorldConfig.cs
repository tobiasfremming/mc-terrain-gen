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

    [Header("Game Integration")]
    [Tooltip("On: PlayerBootstrap spawns the demo capsule player and DesertAtmosphere sets fog/far plane at play start. Turn OFF in the game, which brings its own player, camera and lighting.")]
    public bool demoBootstrap = true;
    [Tooltip("Layer every terrain chunk and collision tile is put on. The terrain package cannot add layers to the game's TagManager: create a 'Terrain' layer in the game and select its index here. NavMesh, ground probes and Rigidbody guards all use this.")]
    [Range(0, 31)] public int terrainLayer = 0;

    public int TerrainLayerMask => 1 << terrainLayer;

    [Tooltip("Collision tiles (collider-only LOD0 meshes around TerrainAnchors outside the player's LOD0 ring) started per frame.")]
    [Range(1, 16)] public int maxTileBuildsPerFrame = 2;
    [Tooltip("Hard cap on live collision tiles; the nearest-to-an-anchor win when exceeded.")]
    public int maxCollisionTiles = 512;

    [Tooltip("NavMesh agent type ids (Navigation window > Agents) to bake for. 0 = Humanoid.")]
    public int[] navAgentTypeIds = { 0 };
    [Tooltip("Minimum seconds between NavMesh updates while terrain collision keeps changing.")]
    public float navUpdateInterval = 0.5f;
    [Tooltip("Bake a height mesh so agents' feet follow bumpy marching-cubes ground instead of the simplified nav polygons.")]
    public bool navBuildHeightMesh = true;
    [Tooltip("Game layers whose physics colliders (buildings, props) are included in the terrain NavMesh. Skipped: colliders attached to a Rigidbody (players, crates, anything that moves), colliders with a NavMeshAgent or CharacterController on themselves or a parent, and the terrain's own colliders. NavMeshModifier components are NOT honoured -- every remaining collider bakes as walkable area 0.")]
    public LayerMask navGameLayers = 0;

    [Header("Planet Mode")]
    [Tooltip("Render the world as a sphere instead of a flat plane.")]
    public bool useGlobe = false;
    [Tooltip("Used when Use Globe is on, instead of defaultDensity. A PlanetField already wraps its own terrain (its Surface field) plus radius/center -- point that at whatever BiomeWorld you want the globe to use (typically the same one defaultDensity points at, but doesn't have to be).")]
    public PlanetField planetField;

    // What MCChunkManager actually generates from. Kept as one indirection so
    // toggling useGlobe is the only thing callers need to check.
    public DensityField EffectiveDensity => (useGlobe && planetField != null) ? (DensityField)planetField : defaultDensity;

    [Header("Vegetation")]
    [Tooltip("Master switch. Off means PlantScatter draws nothing and builds nothing -- not merely hidden, so it costs zero. Turn it off to measure terrain in isolation.")]
    public bool enablePlants = true;
    [Tooltip("Which species grow here, and how thickly. Leave empty for a bare world.")]
    public PlantWorld plantWorld;

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

        // EffectiveDensity silently falls back to flat (defaultDensity) if
        // useGlobe is on but planetField isn't wired up -- no error, no
        // visual glitch, just a world that looks flat when you expected a
        // sphere. Warn loudly instead of leaving that to be a mystery.
        if (useGlobe && planetField == null)
            Debug.LogWarning($"[WorldConfig] '{name}': Use Globe is on but Planet Field is unassigned -- " +
                "falling back to the flat defaultDensity until a PlanetField is set.", this);

        TerrainTuning.NotifyChanged(); // so flipping Use Globe hot-reloads in Play mode, same as any Biome tweak
    }
}
