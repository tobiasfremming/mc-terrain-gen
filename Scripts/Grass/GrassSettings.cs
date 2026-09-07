using UnityEngine;

// Everything that tunes the GPU grass, as an asset shared by TerrainGrass (the
// world) and GrassLab (the test scene). Which ground grows grass, how densely
// and in what colours is NOT here: that lives on each Biome (grassDensity,
// grassColorBase/Tip) so it travels with the biome like its shading does.
[CreateAssetMenu(fileName = "GrassSettings", menuName = "Marching Cubes/Grass Settings")]
public class GrassSettings : ScriptableObject
{
    [Header("Blade shape (m)")]
    public float bladeHeight = 0.55f;
    public float bladeWidth = 0.05f;
    [Range(0f, 0.9f)] public float heightVariation = 0.35f;
    [Range(0f, 0.9f)] public float widthVariation = 0.25f;
    [Tooltip("Random static lean per blade, in blade heights. 0 = all blades stand straight before the wind touches them.")]
    [Range(0f, 1f)] public float lean = 0.25f;
    [Tooltip("0 = blades stand along the terrain normal, 1 = along gravity. Real grass grows toward the light, so mostly up.")]
    [Range(0f, 1f)] public float upBlend = 0.6f;

    [Header("Wind")]
    [Tooltip("How far the tip is pushed, in blade heights, at full gust. WorldConfig.windStrength multiplies this when TerrainGrass has a config.")]
    [Range(0f, 2f)] public float windStrength = 0.45f;
    public float windSpeed = 1.2f;
    [Tooltip("Metres. Size of the gust waves travelling over the field.")]
    public float gustScale = 14f;
    [Range(0f, 0.5f)] public float flutter = 0.06f;

    [Header("Placement")]
    [Tooltip("Ground steeper than this (degrees) starts thinning out.")]
    [Range(0f, 90f)] public float slopeStartDeg = 22f;
    [Tooltip("Ground steeper than this (degrees) has no grass.")]
    [Range(0f, 90f)] public float slopeEndDeg = 45f;
    [Tooltip("Multiplies every biome's grassDensity.")]
    public float densityScale = 1f;
    [Tooltip("Density multiplier per clipmap level. Coarser rings are far away and already thinned by the distance fade, so they need far fewer blades scattered.")]
    public float[] levelDensity = { 1f, 0.3f };
    [Tooltip("Highest clipmap level that gets grass. 0 = only the ring around the player (~48 m), 1 = out to ~96 m.")]
    [Range(0, 3)] public int maxLevel = 1;

    [Header("Distance")]
    [Tooltip("Blades start thinning out (m). Up to here every blade draws.")]
    public float fadeStart = 28f;
    [Tooltip("No blades beyond this (m).")]
    public float fadeEnd = 70f;
    [Tooltip("Within this distance blades have 5 segments.")]
    public float lod0Distance = 10f;
    [Tooltip("Within this distance blades have 3 segments; beyond it, one triangle.")]
    public float lod1Distance = 26f;

    [Header("Interaction")]
    [Tooltip("Blades within this radius of the viewer bend away from it. 0 = off.")]
    public float trampleRadius = 1.1f;

    [Header("Budget")]
    [Tooltip("Blade records per chunk slot. A 16 m LOD0 chunk at 60 blades/m^2 needs ~15k on flat ground, more on slopes. Overflow is dropped, never crashes.")]
    public int slotCapacity = 20480;
    [Tooltip("How many chunks can carry grass at once. Pool memory = slots x capacity x 24 bytes.")]
    public int maxSlots = 96;
    [Tooltip("Visible blades per LOD bin per frame. The cull kernel clamps to this.")]
    public int maxVisiblePerLod = 1 << 20;
    [Tooltip("Chunk (re)scatters per frame. Each is one small compute dispatch; this only bounds the burst at world load.")]
    public int scattersPerFrame = 24;

    [Header("Rendering")]
    public Material material;
    public ComputeShader compute;
    public bool castShadows = false;
    public bool receiveShadows = true;

    public long PoolBytes => (long)Mathf.Max(1, maxSlots) * Mathf.Max(64, slotCapacity) * GrassSystem.BladeStride;

    void OnValidate()
    {
        bladeHeight = Mathf.Max(0.01f, bladeHeight);
        bladeWidth = Mathf.Max(0.001f, bladeWidth);
        slopeEndDeg = Mathf.Max(slopeEndDeg, slopeStartDeg + 0.5f);
        fadeEnd = Mathf.Max(fadeEnd, fadeStart + 1f);
        lod1Distance = Mathf.Max(lod1Distance, lod0Distance + 0.5f);
        slotCapacity = Mathf.Clamp(slotCapacity, 64, 1 << 18);
        maxSlots = Mathf.Clamp(maxSlots, 1, 2048);
        maxVisiblePerLod = Mathf.Clamp(maxVisiblePerLod, 1024, 1 << 22);
        scattersPerFrame = Mathf.Max(1, scattersPerFrame);
        if (levelDensity == null || levelDensity.Length == 0) levelDensity = new[] { 1f };
    }
}
