// Creature kit T4.1 (design §2.3, look layers): the per-species part-layer table. Every mesh vertex carries its
// part id (uv1.x, the material layer); Creature.shader looks the id up in _PartLayers and switches on the row's
// SHADING MODEL (Skin, Chitin, Membrane, Eye, Claw), not on the id. So a new part takes the next free id, gets a
// row here (Registry) and is shaded by an existing model: no shader edit. A species can restyle any row with the
// Layers look module (shading model, tint, smoothness, which layers - pattern, scale grain, sheen, shimmer - it has).
//
// GPU table: _PartLayers = float4[Length], two vectors per part id (ids >= MaxParts use row MaxParts - 1):
//   [2 id]     (model, flags, tint amount, smoothness scale)
//   [2 id + 1] (tint rgb, tint source)   rgb = the row's own colour, LINEAR (decoded once here: SetVectorArray
//              does not decode sRGB, SetColor does); source 0 = that colour, else one of the species' palette
//              colours the renderer already sets with SetColor (TintSource), so legacy pixels are bit-identical.
// Legacy ids 0..9 map to the models they had before the table (LegacyRow); the octopus (4-bone shader family,
// CreatureOctopus.shader) keeps its own shading and never reads this table.
// HLSL mirror: Shaders/Creature/CreatureLayers.hlsl (CreatureShaderLayerParityTests keeps them in step).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // How a part's pixels are shaded (Creature.shader). Values are the HLSL CREATURE_MODEL_* constants.
    //   Skin      the body skin: belly -> flank -> back palette, procedural pattern, scale grain (the shipped look)
    //   Chitin    hard cuticle: the palette and pattern, no scale grain, glossier (x smoothness), little subsurface
    //   Membrane  translucent veined membrane (wings, fins, antennae): Membrane look module colours
    //   Eye       black, glossy
    //   Claw      keratin: the tint over the palette, no grain, glossy, no subsurface (claws, horns, beaks, stingers)
    public enum ShadingModel { Skin = 0, Chitin = 1, Membrane = 2, Eye = 3, Claw = 4 }

    // Which layers a row stacks on its model. Values are the HLSL CREATURE_LAYER_* bits.
    [Flags]
    public enum LayerFlags
    {
        None = 0,
        LimbPattern = 1,   // the pattern's limb branch (mottle / the species' spots) instead of the body's bands
        NoPattern = 2,     // no pattern (feet, eyes)
        FineGrain = 4,     // the limbs' smaller scales
        NoGrain = 8,       // no scale grain
        Sheen = 16,        // the velvet sheen lobe (Chitin look module's sheen; 0 = none)
        Shimmer = 32,      // the iridescent shimmer bands (Shimmer look module; 0 = none)
    }

    // Where a row's tint colour comes from (the HLSL CREATURE_TINT_* values).
    public enum TintSource { Own = 0, Pattern = 1, Band = 2, Dorsal = 3, Belly = 4, Membrane = 5 }

    // One row of the table, authored (sRGB colour) - the C# side of the two float4s.
    [Serializable]
    public struct PartLayer
    {
        public ShadingModel model;
        public LayerFlags flags;
        public TintSource tintSource;
        public float tintAmount;        // 0..1: albedo -> tint after the pattern
        public float smoothnessScale;   // Chitin / Claw: x the species smoothness (0 = the model's default)
        public Color tint;              // sRGB; used when tintSource = Own

        public PartLayer(ShadingModel model, LayerFlags flags, TintSource source = TintSource.Own, float amount = 0f, float smoothness = 0f)
        {
            this.model = model; this.flags = flags; tintSource = source; tintAmount = amount; smoothnessScale = smoothness; tint = Color.black;
        }

        public bool Equals(PartLayer o) => model == o.model && flags == o.flags && tintSource == o.tintSource && tintAmount == o.tintAmount
            && smoothnessScale == o.smoothnessScale && tint.r == o.tint.r && tint.g == o.tint.g && tint.b == o.tint.b;

        public override string ToString() => $"{model} [{flags}] tint {tintSource} {tintAmount:0.##}";
    }

    public static class PartLayers
    {
        public const int MaxParts = 16, Stride = 2, Length = MaxParts * Stride;
        public const string ShaderProperty = "_PartLayers";

        // Default smoothness scales of the glossy models (a row's smoothnessScale 0 picks these).
        public const float ChitinSmoothness = 1.6f, ClawSmoothness = 1.9f;

        const LayerFlags kSkinLayers = LayerFlags.Sheen | LayerFlags.Shimmer;

        // The part-id registry (creature-module rule 9; mesh part ids = material layers): id, what uses it, its row.
        // A new part type takes the next free id (NextFree) and adds its row here; never reuse an id.
        public readonly struct Entry
        {
            public readonly int Id; public readonly string Use; public readonly PartLayer Row; public readonly bool Octopus;
            public Entry(int id, string use, PartLayer row, bool octopus = false) { Id = id; Use = use; Row = row; Octopus = octopus; }
        }

        public static readonly Entry[] Registry =
        {
            new Entry(0, "body skin", new PartLayer(ShadingModel.Skin, kSkinLayers)),
            new Entry(1, "limb", new PartLayer(ShadingModel.Skin, LayerFlags.LimbPattern | LayerFlags.FineGrain | kSkinLayers)),
            new Entry(2, "foot, claw, rigid cone", new PartLayer(ShadingModel.Skin, LayerFlags.NoPattern | LayerFlags.FineGrain | kSkinLayers, TintSource.Pattern, 0.3f)),
            new Entry(3, "eye", new PartLayer(ShadingModel.Eye, LayerFlags.NoPattern | LayerFlags.NoGrain)),
            new Entry(4, "octopus arm", new PartLayer(ShadingModel.Skin, kSkinLayers), true),
            new Entry(5, "octopus web", new PartLayer(ShadingModel.Skin, kSkinLayers), true),
            new Entry(6, "octopus mantle", new PartLayer(ShadingModel.Skin, kSkinLayers), true),
            new Entry(7, "octopus siphon", new PartLayer(ShadingModel.Skin, kSkinLayers), true),
            new Entry(8, "membrane (wings, fins, antennae)", new PartLayer(ShadingModel.Membrane, LayerFlags.NoPattern | LayerFlags.NoGrain)),
            new Entry(9, "arthropod band", new PartLayer(ShadingModel.Skin, kSkinLayers, TintSource.Band, 0.9f)),
        };

        public static int NextFree => Registry.Length;

        // The row a legacy part id had before the table (the shader's old part-id branches). Ids past the registry
        // shade as body skin (what the old shader did with any unknown id).
        public static PartLayer LegacyRow(int id) => id >= 0 && id < Registry.Length ? Registry[id].Row : Registry[0].Row;

        // The species' rows, authored: the registry, then the species' Layers module on top.
        public static PartLayer[] Rows(CreatureSpecies sp)
        {
            var rows = new PartLayer[MaxParts];
            for (int i = 0; i < MaxParts; i++) rows[i] = LegacyRow(i);
            if (sp == null) return rows;
            IReadOnlyList<CreatureModule> modules = KitView.Modules(sp.Compiled.Runtime);
            if (modules != null)
                foreach (CreatureModule m in modules)
                    if (m is Layers l && l.rows != null)
                        foreach (LayerDef d in l.rows)
                            if (d.partId >= 0 && d.partId < MaxParts) rows[d.partId] = d.ToRow();
            return rows;
        }

        // The GPU table (Length float4s). linear: decode the rows' own sRGB colours (a linear-space project;
        // CreatureRenderer passes QualitySettings.activeColorSpace == Linear, as SetColor would decode).
        public static void Encode(PartLayer[] rows, Vector4[] dst, bool linear)
        {
            if (dst == null || dst.Length < Length) throw new ArgumentException($"need {Length} float4s", nameof(dst));
            for (int i = 0; i < MaxParts; i++)
            {
                PartLayer r = i < rows.Length ? rows[i] : LegacyRow(i);
                float smooth = r.smoothnessScale > 0f ? r.smoothnessScale
                    : r.model == ShadingModel.Chitin ? ChitinSmoothness : r.model == ShadingModel.Claw ? ClawSmoothness : 1f;
                dst[Stride * i] = new Vector4((float)(int)r.model, (float)(int)r.flags, Mathf.Clamp01(r.tintAmount), smooth);
                // only a row that tints with its own colour carries one (palette sources: the shader's SetColor values)
                Color c = r.tintSource == TintSource.Own && r.tintAmount > 0f ? (linear ? r.tint.linear : r.tint) : new Color(0f, 0f, 0f, 0f);
                dst[Stride * i + 1] = new Vector4(c.r, c.g, c.b, (float)(int)r.tintSource);
            }
        }

        public static Vector4[] Table(CreatureSpecies sp, bool linear)
        {
            var t = new Vector4[Length];
            Encode(Rows(sp), t, linear);
            return t;
        }
    }

    // One row a species restyles (the Layers look module).
    [Serializable]
    public struct LayerDef
    {
        [Param(ParamUnit.Count, "the part id (material layer) this row styles: 0 body, 1 limb, 2 foot/claw/cone, 3 eye, 8 membrane, 9 band, 10+ new parts (CATALOGUE.md registry)", Min = 0, Max = 15)] public int partId;
        [Param(ParamUnit.Enum, "shading model: Skin, Chitin (no grain, glossy), Membrane, Eye, Claw (keratin: the tint, glossy)")] public ShadingModel model;
        [Param(ParamUnit.Flag, "no pattern on this part")] public bool noPattern;
        [Param(ParamUnit.Flag, "the limb pattern (mottle / spots) instead of the body's")] public bool limbPattern;
        [Param(ParamUnit.Flag, "no scale grain")] public bool noGrain;
        [Param(ParamUnit.Flag, "the limbs' smaller scales")] public bool fineGrain;
        [Param(ParamUnit.Flag, "without the velvet sheen (Chitin module)")] public bool noSheen;
        [Param(ParamUnit.Flag, "without the iridescent shimmer (Shimmer module)")] public bool noShimmer;
        [Param(ParamUnit.Enum, "tint colour: Own (tint below) or a palette colour")] public TintSource tintSource;
        [Param(ParamUnit.SrgbColour, "the row's own tint (tintSource Own)")] public Color tint;
        [Param(ParamUnit.Fraction, "albedo -> tint after the pattern (Claw: how much of the tint)", Min = 0, Max = 1)] public float tintAmount;
        [Param(ParamUnit.Fraction, "Chitin / Claw: x the species smoothness (0 = model default: Chitin 1.6, Claw 1.9)", Min = 0, Max = 3)] public float smoothnessScale;

        public PartLayer ToRow()
        {
            LayerFlags f = LayerFlags.None;
            if (noPattern) f |= LayerFlags.NoPattern;
            if (limbPattern) f |= LayerFlags.LimbPattern;
            if (noGrain) f |= LayerFlags.NoGrain;
            if (fineGrain) f |= LayerFlags.FineGrain;
            if (!noSheen) f |= LayerFlags.Sheen;
            if (!noShimmer) f |= LayerFlags.Shimmer;
            return new PartLayer(model, f, tintSource, tintAmount, smoothnessScale) { tint = tint };
        }
    }

    [Serializable]
    [Module("Layers", CreatureModule.Look, Summary = "Restyles part ids (material layers) for this species: shading model (Skin, Chitin, Membrane, Eye, Claw), tint, smoothness, and which layers (pattern, scale grain, sheen, shimmer) each has. Rows not listed keep the registry's (Kit/PartLayers.cs). A new part needs a row, never a shader edit.")]
    public sealed class Layers : CreatureModule
    {
        [Param(ParamUnit.Struct, "rows, one per restyled part id")] public LayerDef[] rows = new LayerDef[0];

        public override string Validate(in SpeciesCtx c)
        {
            if (rows == null) return null;
            var seen = new HashSet<int>();
            foreach (LayerDef d in rows)
            {
                if (d.partId < 0 || d.partId >= PartLayers.MaxParts) return $"Layers: partId {d.partId} must be in 0..{PartLayers.MaxParts - 1}";
                if (!seen.Add(d.partId)) return $"Layers: part {d.partId} listed twice";
                if (d.tintAmount < 0f || d.tintAmount > 1f) return $"Layers: part {d.partId} tintAmount must be in 0..1";
            }
            return null;
        }
    }
}
