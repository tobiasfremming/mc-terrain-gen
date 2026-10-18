// Cloak (generic, every creature shader's Forward pass only): the body dissolves by a per-instance
// `cloak` 0..1 through a blue-noise-like screen dither mixed with a surface noise, so it breaks up into
// drifting patches rather than a uniform screen-door. A thin band just above the dissolve threshold glints.
// ShadowCaster, DepthOnly and DepthNormals do NOT call this: the body vanishes, its shadow and its contact
// AO remain (the queued "shadow-only" look).
#ifndef CREATURE_CLOAK_INCLUDED
#define CREATURE_CLOAK_INCLUDED

// Interleaved gradient noise (Jimenez 2014) on the pixel grid: well-distributed per-pixel thresholds.
float CreatureIGN(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// Clips the pixel when cloaked; returns the edge glint strength (0..1) for the survivors.
//   pixel       SV_POSITION.xy
//   surfaceN    a 0..1 noise on the body surface (stable as it moves)
float CreatureCloak(float cloak, float2 pixel, float surfaceN)
{
    if (cloak <= 0.0) return 0.0;
    float t = 0.65 * surfaceN + 0.35 * CreatureIGN(pixel);   // per-pixel threshold
    float edge = t - cloak * 1.08;                             // > 0 survives
    clip(edge);
    return (1.0 - smoothstep(0.0, 0.06, edge)) * saturate(cloak * 4.0);
}

#endif
