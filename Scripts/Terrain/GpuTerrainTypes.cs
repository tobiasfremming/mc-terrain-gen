using System.Runtime.InteropServices;
using UnityEngine;

// GPU-side counterparts of the density field hierarchy -- see the "GPU
// Compute-Shader Acceleration for Terrain Density Fields" plan. Struct
// layouts here MUST match Shaders/Compute/Density{Dune,Alien,Canyon,Frost}.hlsl's
// structs field-for-field (same order, same count) -- StructuredBuffer
// layout is positional, not name-matched. If you add/remove/reorder a field
// on one side, mirror it on the other immediately.

// Matches SandTerrain.shader's Biome.SurfaceStyle-driven dispatch pattern,
// applied to density instead of shading. Values must match the
// MC_FIELDTYPE_* defines in DensityBiomeBlend.hlsl.
public enum GpuFieldType
{
    None = -1, // not GPU-capable; BiomeDensityField/PlanetField fall back to CPU-only for the whole world if any biome slot resolves to this
    Dune = 0,
    Alien = 1,
    Canyon = 2,
    Frost = 3,
}

[StructLayout(LayoutKind.Sequential)]
public struct DuneGpuParams
{
    public float seed;
    public float windDegrees;
    public float baseHeight;
    public float megaWavelength;
    public float megaHeight;
    public float megaSinuosity;
    public float megaSegmentation;
    public float secondaryWavelength;
    public float secondaryHeight;
    public float secondaryWindOffset;
    public float secondarySinuosity;
    public float secondarySegmentation;
    public float secondaryPatchScale;
    public float rippleWavelength;
    public float rippleHeight;
    public float supplyScale;
    public float sizeVariationScale;
    public float plainScale;
    public float plainAmplitude;
}

[StructLayout(LayoutKind.Sequential)]
public struct AlienGpuParams
{
    public float seed;
    public float latticeFreq;
    public float lattice1Amp;
    public float lattice2Amp;
    public float hillAmp;
    public float wallDetailFreq;
    public float enableTunnel;
    public float pathAmpA;
    public float pathFreqA;
    public float pathAmpB;
    public float pathFreqB;
    public float pathDriftAmp;
    public float pathDriftFreq;
    public float tunnelRadiusX;
    public float tunnelRadiusY;
    public float tunnelBlend;
    public float pebbleScale;
    public float pebbleAmp;
    public float tnlPebbleOffset;
    public float tunnelReinforce;
}

[StructLayout(LayoutKind.Sequential)]
public struct CanyonGpuParams
{
    public float seed;
    public float floorHeight;
    public float thScale;
    public float thLo;
    public float thHi;
    public float peakScale;
    public float peakBase;
    public float peakRange;
    public float detailScale;
    public float detailAmp;
    public float rrScale;
    public float rrAmp;
    public float disScale;
    public float disSquash;
    public float disAmp;
    public float enableSpires;
    public float spireCellSize;
    public float spireChance;
    public float spireRadius;
    public float spireHeight;
    public float spireIrregularity;
    public float spireMaxBaseHeight;
    public float spireBaseFadeBand;
    public float enableBridges;
    public float bridgeCellSize;
    public float bridgeChance;
    public float bridgeMaxCenterHeight;
    public float bridgeHalfSpanMin;
    public float bridgeHalfSpanMax;
    public float bridgeTubeRadiusMin;
    public float bridgeTubeRadiusMax;
    public float bridgeWalkwayHalfWidth;
    public float bridgeLegEmbedMargin;
    public float bridgeSmoothing;
}

[StructLayout(LayoutKind.Sequential)]
public struct FrostGpuParams
{
    public float seed;
    public float baseHeight;
    public float windDegrees;
    public float plainScale;
    public float plainAmplitude;
    public float peakScale;
    public float peakOctaves;
    public float peakAmp;
    public float sastrugiEnabled;
    public float sastrugiWavelengthAcross;
    public float sastrugiWavelengthAlong;
    public float sastrugiOctaves;
    public float sastrugiAmp;
    public float sastrugiAsymmetry;
    public float permafrostEnabled;
    public float polygonScale;
    public float polygonTroughDepth;
    public float polygonEdgeWidth;
    public float crevassesEnabled;
    public float crevasseScale;
    public float crevasseDepth;
    public float crevasseEdgeWidth;
    public float crevassePatchScale;
    public float hummocksEnabled;
    public float hummockScale;
    public float hummockAmp;
}

// Union of all 4 leaf types (mirrors LeafParams in DensityBiomeBlend.hlsl) --
// nested, not flattened, so field names never collide across types. Every
// biome slot's buffer entry carries all four sub-structs; only the one
// matching that slot's GpuFieldType is ever read on the GPU side.
[StructLayout(LayoutKind.Sequential)]
public struct LeafGpuParams
{
    public DuneGpuParams dune;
    public AlienGpuParams alien;
    public CanyonGpuParams canyon;
    public FrostGpuParams frost;
}

// Mirrors BiomeBlendParams in DensityBiomeBlend.hlsl.
[StructLayout(LayoutKind.Sequential)]
public struct BiomeBlendGpuParams
{
    public float seed;
    public float regionScale;
    public float sharpness;
    public float biomeCount;
}

// Mirrors PlanetParams in DensityPlanet.hlsl. HLSL float3 is 12 bytes with
// no implicit padding to 16 here since it's followed by another float
// (radius) -- Unity's ComputeBuffer/StructuredBuffer packing matches HLSL's
// natural packing for this layout, so no manual padding field is needed.
[StructLayout(LayoutKind.Sequential)]
public struct PlanetGpuParams
{
    public float isPlanet;
    public Vector3 center;
    public float radius;
}

// One grid-fill request -- mirrors GridRequest in TerrainDensity.compute.
[StructLayout(LayoutKind.Sequential)]
public struct GpuGridRequest
{
    public Vector3 origin;
    public float step;
    public uint countX, countY, countZ;
    public uint outputOffset;
    public uint voxelOffset;

    public uint VoxelCount => countX * countY * countZ;
}
