// Shared between GrassScatter.compute and Grass.shader: the blade record,
// hashing, and the packing helpers. Keep in sync with GrassSystem.Blade.
#ifndef MC_GRASS_COMMON_INCLUDED
#define MC_GRASS_COMMON_INCLUDED

#define GRASS_LOD_COUNT 3u

struct Blade
{
    float3 pos;      // world-space root
    uint normalOct;  // oct-encoded blade up (2 x 16 bit)
    uint seed;       // low 24 bits random, high 8 bits baked AO
    uint colors;     // RGB565 base colour (low 16) | RGB565 tip colour (high 16), biome-blended at scatter time
};

// ------------------------------------------------------------------ hashing
uint GrassHash(uint x)
{
    x ^= x >> 16; x *= 0x7FEB352Du;
    x ^= x >> 15; x *= 0x846CA68Bu;
    x ^= x >> 16;
    return x;
}

uint GrassHashInt3(int3 p)
{
    uint h = (uint)p.x * 0x8DA6B343u ^ (uint)p.y * 0xD8163841u ^ (uint)p.z * 0xCB1AB31Fu;
    return GrassHash(h);
}

float GrassHash01(uint x) { return (float)(GrassHash(x) & 0x00FFFFFFu) * (1.0 / 16777216.0); }

// Random byte k of a seed's 24 random bits, as 0..1.
float GrassSeedByte(uint seed, uint k) { return (float)((seed >> (8u * k)) & 0xFFu) * (1.0 / 255.0); }

// ------------------------------------------------------------------ packing
float2 GrassOctWrap(float2 v) { return (1.0 - abs(v.yx)) * (v.xy >= 0.0 ? 1.0 : -1.0); }

uint GrassPackOct(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    float2 e = n.z >= 0.0 ? n.xy : GrassOctWrap(n.xy);
    e = saturate(e * 0.5 + 0.5);
    uint2 q = (uint2)(e * 65535.0 + 0.5);
    return q.x | (q.y << 16);
}

float3 GrassUnpackOct(uint p)
{
    float2 e = float2(p & 0xFFFFu, p >> 16) * (2.0 / 65535.0) - 1.0;
    float3 n = float3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t : t;
    return normalize(n);
}

// Two RGB565 colours in one word: 5/6/5 bits is plenty for grass that gets
// a +-20% random tint per blade anyway, and it keeps the blade record at
// 24 bytes without caring how many biomes exist.
uint GrassPack565(float3 c)
{
    uint3 q = (uint3)(saturate(c) * float3(31.0, 63.0, 31.0) + 0.5);
    return q.x | (q.y << 5) | (q.z << 11);
}

float3 GrassUnpack565(uint p)
{
    return float3(p & 31u, (p >> 5) & 63u, (p >> 11) & 31u) * float3(1.0 / 31.0, 1.0 / 63.0, 1.0 / 31.0);
}

uint GrassPackUnorm4x8(float4 v)
{
    uint4 q = (uint4)(saturate(v) * 255.0 + 0.5);
    return q.x | (q.y << 8) | (q.z << 16) | (q.w << 24);
}

float4 GrassUnpackUnorm4x8(uint p)
{
    return float4(p & 0xFFu, (p >> 8) & 0xFFu, (p >> 16) & 0xFFu, p >> 24) * (1.0 / 255.0);
}

#endif
