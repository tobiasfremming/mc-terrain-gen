#ifndef MC_DENSITY_GROVE_INCLUDED
#define MC_DENSITY_GROVE_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU half of LSystemGroveField.
//
// Every other leaf in this pipeline is closed-form: give it twenty floats and
// it reproduces the whole field. A grove cannot be, because its shape comes
// from deriving an L-system -- variable-length, branch-heavy, pointer-chasing
// work with no sane HLSL form. So the field is SPLIT rather than refused:
//
//   CPU  builds each plot's capsules once and caches them (LSystemGroveField
//        .BuildGpuAtlas), which is amortised over the whole plot
//   GPU  evaluates the round-cone SDF over them, which runs per voxel and is
//        therefore the half that actually costs
//
// TerrainGpuSampler uploads the atlas for the plots a dispatch can reach.
// A sample whose plot is outside the resident rectangle simply sees ground --
// which cannot happen in practice, because the rectangle is derived from the
// same requests being dispatched.
//
// PARITY. Every function below mirrors a specific piece of
// LSystemGroveField.cs. If the two drift, the symptom is not a wrong-looking
// grove -- it is cracks where a GPU-sampled chunk meets a CPU-sampled one.
// Change them together.

// Mirrors GroveGpuParams in Scripts/Terrain/GpuTerrainTypes.cs, field for
// field -- StructuredBuffer layout is positional.
struct GroveParams
{
    float seed;
    float groundHeight;
    float groundAmp;
    float groundScale;
    float groundOctaves;
    float blend;
    float plotSize;
};

// Mirrors GroveCapsuleGpu / LSystemGroveField.Capsule.
struct GroveCapsule
{
    float3 a;
    float  ra;
    float3 b;
    float  rb;
};

// Mirrors GrovePlotGpu / LSystemGroveField.Plot. The three *Base fields are
// offsets into the shared atlas arrays; cell items are plot-relative, so the
// capsule index is capsuleBase + item.
struct GrovePlot
{
    float minX;
    float minZ;
    float maxX;
    float maxZ;
    float invCell;
    int   res;           // 0 => empty plot, skip
    int   cellStartBase;
    int   itemBase;
    int   capsuleBase;
    int   cellYBase;
};

// Mirrors GroveAtlasGpuParams. plotsX == 0 means no atlas is resident.
struct GroveAtlas
{
    int pxMin;
    int pzMin;
    int plotsX;
    int plotsZ;
};

StructuredBuffer<GroveCapsule> _GroveCapsules;
StructuredBuffer<GrovePlot>    _GrovePlots;
StructuredBuffer<int>          _GroveCellStart;
StructuredBuffer<int>          _GroveCellItems;
StructuredBuffer<float2>       _GroveCellY;      // per cell: padded [minY, maxY]
StructuredBuffer<GroveAtlas>   _GroveAtlasBuf;   // [1]

// Mirrors LSystemGroveField.SMax. Returns the EXACT max once the arguments are
// more than k apart, which is why folding hundreds of far-away capsules in
// cannot inflate the ground.
float MC_GroveSMax(float a, float b, float k)
{
    if (k <= 0.0) return max(a, b);
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return b * (1.0 - h) + a * h + h * (1.0 - h) * k;
}

// Mirrors LSystemGroveField.Capsule.Distance -- Inigo Quilez's exact
// sdRoundCone. The branchy form is what makes it exact rather than a bound,
// and the value is used directly as a density in metres, so the branches stay.
// HLSL sign() returns 0 at 0 exactly as Mathf.Sign does, so the comparisons
// carry over unchanged.
float MC_GroveRoundCone(float3 p, GroveCapsule c)
{
    float3 ba = c.b - c.a;
    float l2 = dot(ba, ba);
    float3 pa = p - c.a;

    // Tip markers are stored as a degenerate capsule rather than a second
    // primitive type; this is where they become spheres.
    if (l2 < 1e-8) return length(pa) - c.ra;

    float rr = c.ra - c.rb;
    float a2 = l2 - rr * rr;
    float il2 = 1.0 / l2;

    float y = dot(pa, ba);
    float z = y - l2;

    float3 xp = pa * l2 - ba * y;
    float x2 = dot(xp, xp);

    float y2 = y * y * l2;
    float z2 = z * z * l2;

    float k = sign(rr) * rr * rr * x2;
    if (sign(z) * a2 * z2 > k) return sqrt(x2 + z2) * il2 - c.rb;
    if (sign(y) * a2 * y2 < k) return sqrt(x2 + y2) * il2 - c.ra;
    return (sqrt(max(0.0, x2 * a2 * il2)) + y * rr) * il2 - c.ra;
}

// Mirrors LSystemGroveField.Ground. MC_Fbm already matches TerrainNoise.Fbm
// exactly, including the filter-width argument, so this is a transcription.
float MC_GroveGround(float wx, float wz, GroveParams p, float fw)
{
    float s = p.groundScale > 0.01 ? p.groundScale : 0.01;
    float n = MC_Fbm(wx / s, wz / s, (int)p.groundOctaves, (uint)((int)p.seed) + 4919u, fw / s);
    return p.groundHeight + n * p.groundAmp;
}

// Mirrors LSystemGroveField.Plot.Cell, including the "outside the grid => this
// plot cannot matter" early-out. That is exactly true rather than merely
// convenient, because the grid's bounds come from the capsules' footprints
// already inflated by radius + blend.
int MC_GrovePlotCell(GrovePlot pl, float wx, float wz)
{
    if (pl.res <= 0) return -1;
    if (wx < pl.minX || wx > pl.maxX || wz < pl.minZ || wz > pl.maxZ) return -1;
    int cx = clamp((int)((wx - pl.minX) * pl.invCell), 0, pl.res - 1);
    int cz = clamp((int)((wz - pl.minZ) * pl.invCell), 0, pl.res - 1);
    return cz * pl.res + cx;
}

// Mirrors LSystemGroveField.Sample.
float EvaluateGroveDensity(float3 worldPos, GroveParams p, float fw)
{
    float d = MC_GroveGround(worldPos.x, worldPos.z, p, fw) - worldPos.y;

    GroveAtlas atlas = _GroveAtlasBuf[0];
    if (atlas.plotsX <= 0 || atlas.plotsZ <= 0) return d;

    int px = (int)floor(worldPos.x / p.plotSize);
    int pz = (int)floor(worldPos.z / p.plotSize);

    // dz outer, dx inner, both -1..1, then CSR order within a cell -- the SAME
    // visiting order as LSystemGroveField.Sample. MC_GroveSMax is not exactly
    // associative, so a different order yields a slightly different density,
    // and "slightly different" at a chunk boundary is a crack.
    //
    // The 3x3 window is only sufficient because Fit() clamps every structure
    // to half a plot horizontally. Nothing here re-derives that; it is
    // inherited from the CPU side and breaks silently if that clamp goes.
    [loop] for (int dz = -1; dz <= 1; dz++)
    {
        [loop] for (int dx = -1; dx <= 1; dx++)
        {
            int gx = px + dx - atlas.pxMin;
            int gz = pz + dz - atlas.pzMin;
            if (gx < 0 || gz < 0 || gx >= atlas.plotsX || gz >= atlas.plotsZ) continue;

            GrovePlot pl = _GrovePlots[gz * atlas.plotsX + gx];
            int cell = MC_GrovePlotCell(pl, worldPos.x, worldPos.z);
            if (cell < 0) continue;

            // The bucket grid is XZ-only, so without this a voxel far under or
            // over a spire still walked every capsule in that column. The
            // bounds are already padded by radius + blend when the plot is
            // built, so this rejects on exactly the same rule the XZ footprint
            // uses -- and Plot.Accumulate applies it identically on the CPU.
            float2 yr = _GroveCellY[pl.cellYBase + cell];
            if (worldPos.y < yr.x || worldPos.y > yr.y) continue;

            int begin = _GroveCellStart[pl.cellStartBase + cell];
            int end   = _GroveCellStart[pl.cellStartBase + cell + 1];
            [loop] for (int i = begin; i < end; i++)
            {
                GroveCapsule c = _GroveCapsules[pl.capsuleBase + _GroveCellItems[pl.itemBase + i]];
                d = MC_GroveSMax(d, -MC_GroveRoundCone(worldPos, c), p.blend);
            }
        }
    }

    return d;
}

#endif // MC_DENSITY_GROVE_INCLUDED
