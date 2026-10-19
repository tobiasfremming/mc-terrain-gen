// Creature kit T4.1: the per-species part-layer table (C#: Kit/PartLayers.cs, set by CreatureRenderer with
// SetVectorArray). Creature.shader looks each fragment's part id (uv1.x) up here and switches on the row's
// SHADING MODEL, not on the id, so a new part with the next free id needs a C# row, never a shader edit.
//   _PartLayers[2 id]     (model, flags, tint amount, smoothness scale)
//   _PartLayers[2 id + 1] (tint rgb LINEAR (decoded in C#), tint source)
// Ids >= CREATURE_LAYER_PARTS read the last row. The constants below mirror the C# enums
// (CreatureShaderLayerParityTests reads this file and compares).
#ifndef CREATURE_LAYERS_INCLUDED
#define CREATURE_LAYERS_INCLUDED

#define CREATURE_LAYER_PARTS  16
#define CREATURE_LAYER_LENGTH 32

// ShadingModel
#define CREATURE_MODEL_SKIN     0
#define CREATURE_MODEL_CHITIN   1
#define CREATURE_MODEL_MEMBRANE 2
#define CREATURE_MODEL_EYE      3
#define CREATURE_MODEL_CLAW     4

// LayerFlags
#define CREATURE_LAYER_LIMB_PATTERN 1u
#define CREATURE_LAYER_NO_PATTERN   2u
#define CREATURE_LAYER_FINE_GRAIN   4u
#define CREATURE_LAYER_NO_GRAIN     8u
#define CREATURE_LAYER_SHEEN        16u
#define CREATURE_LAYER_SHIMMER      32u

// TintSource
#define CREATURE_TINT_OWN      0
#define CREATURE_TINT_PATTERN  1
#define CREATURE_TINT_BAND     2
#define CREATURE_TINT_DORSAL   3
#define CREATURE_TINT_BELLY    4
#define CREATURE_TINT_MEMBRANE 5

float4 _PartLayers[CREATURE_LAYER_LENGTH];

struct CreatureLayer
{
    int model;
    uint flags;
    float tintAmount;
    float smoothnessScale;
    float3 tint;
};

// The row of a part id. The palette colours are the species' (SetColor-decoded) ones a tint source can name.
CreatureLayer CreatureLayerOf(int part, float3 patternC, float3 bandC, float3 dorsalC, float3 bellyC, float3 membraneC)
{
    int row = clamp(part, 0, CREATURE_LAYER_PARTS - 1) * 2;
    float4 a = _PartLayers[row], b = _PartLayers[row + 1];
    CreatureLayer l;
    l.model = (int)(a.x + 0.5);
    l.flags = (uint)(a.y + 0.5);
    l.tintAmount = a.z;
    l.smoothnessScale = a.w;
    int src = (int)(b.w + 0.5);
    l.tint = src == CREATURE_TINT_PATTERN ? patternC
           : src == CREATURE_TINT_BAND ? bandC
           : src == CREATURE_TINT_DORSAL ? dorsalC
           : src == CREATURE_TINT_BELLY ? bellyC
           : src == CREATURE_TINT_MEMBRANE ? membraneC
           : b.rgb;
    return l;
}

bool CreatureLayerHas(CreatureLayer l, uint bit) { return (l.flags & bit) != 0u; }

// CreaturePatternMode's mode: 0 none, 1 body, 2 limb.
int CreatureLayerPatternMode(CreatureLayer l)
{
    if (l.model == CREATURE_MODEL_EYE || CreatureLayerHas(l, CREATURE_LAYER_NO_PATTERN)) return 0;
    return CreatureLayerHas(l, CREATURE_LAYER_LIMB_PATTERN) ? 2 : 1;
}

// Scale grain off: eyes, the hard models (Chitin, Claw) and rows that say so.
bool CreatureLayerNoGrain(CreatureLayer l)
{
    return l.model == CREATURE_MODEL_EYE || l.model == CREATURE_MODEL_CHITIN || l.model == CREATURE_MODEL_CLAW
        || CreatureLayerHas(l, CREATURE_LAYER_NO_GRAIN);
}

#endif
