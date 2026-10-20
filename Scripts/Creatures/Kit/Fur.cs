// Creature fur (feat/creature-fur): the Fur look module. A species with it grows hair on the regions it lists, in
// two layers that are drawn AFTER the body by the fur system (Scripts/Creatures/Fur, Resources/CreatureFur):
//   Undercoat  short dense fur as SHELLS: the furry triangles of the body mesh drawn `shells` times, each pushed out
//              along the skinned normal, a procedural hair mask cut per shell (CreatureFurShell.shader).
//   Strands    long shaggy fur, manes and tufts as GPU BLADES (the grass idea, on a body): roots sampled once per
//              species on the rest mesh (FurBuilder), skinned per frame with the renderer's own bone matrices and
//              simulated as two-segment springs that trail the body and droop (FurSim.compute), drawn as tapered
//              camera-facing locks (CreatureFurStrands.shader).
// Off by default: a species without the module draws exactly as before (no extra draw, no extra buffer).
// Authored at scale 1: lengths and widths x sizeScale, the spring's frequency / sqrt(sizeScale) (Froude).
using System;
using UnityEngine;

namespace CreatureKit
{
    // Which layer a region grows.
    public enum FurKind { Undercoat = 0, Strands = 1 }

    // One furry region of the body: where (part ids, arc window along the part, dorsal window), how much, how long,
    // which colours, how it lies and how it moves. Its weight at a vertex is part x arc window x dorsal window
    // (soft edges); a strand's length shrinks toward the edges with the weight.
    [Serializable]
    public struct FurRegion
    {
        [Param(ParamUnit.Text, "a name for the region (debug, the catalogue)")] public string name;
        [Param(ParamUnit.Enum, "Undercoat (shells: short dense fur) or Strands (blades: long fur, manes, tufts)")] public FurKind kind;
        [Param(ParamUnit.Count, "bit mask of the part ids it grows on (bit 0 body, bit 1 limb, ...; the claw/eye/membrane/jaw/teeth ids are refused)", Example = "body 1, body + limbs 3")] public int partMask;
        [Param(ParamUnit.Metres, "arc window start along the part (uv0.x, rest metres: the body from the snout, a limb from its root)")] public float arcFrom;
        [Param(ParamUnit.Metres, "arc window end")] public float arcTo;
        [Param(ParamUnit.Metres, "soft edge outside the arc window (0 = hard)")] public float arcEdge;
        [Param(ParamUnit.Fraction, "dorsal window start (-1 belly .. +1 ridge)", Min = -1, Max = 1)] public float dorsalFrom;
        [Param(ParamUnit.Fraction, "dorsal window end", Min = -1, Max = 1)] public float dorsalTo;
        [Param(ParamUnit.Fraction, "soft edge outside the dorsal window (0 = hard)", Min = 0, Max = 2)] public float dorsalEdge;
        [Param(ParamUnit.Metres, "hair length (strands: the whole lock; undercoat: the outer shell's height)", Example = "mane 0.06, undercoat 0.008")] public float length;
        [Param(ParamUnit.Fraction, "length jitter (+-)", Min = 0, Max = 0.9f)] public float lengthJitter;
        [Param(ParamUnit.Fraction, "strands: roots per square metre of the rest (scale-1) surface; undercoat: hairs per square centimetre of it", Example = "mane 30000, undercoat 60")] public float density;
        [Param(ParamUnit.Metres, "strands: the lock's width at the root; undercoat: a hair's radius at the root", Example = "mane 0.004, undercoat 0.0006")] public float width;
        [Param(ParamUnit.SrgbColour, "colour at the root")] public Color rootColour;
        [Param(ParamUnit.SrgbColour, "colour at the tip")] public Color tipColour;
        [Param(ParamUnit.Fraction, "toward the body's own palette and pattern at the root (0 = the region colours only)", Min = 0, Max = 1)] public float bodyTint;
        [Param(ParamUnit.Degrees, "how far the hair rises off the skin (0 flat .. 90 standing up)", Min = 0, Max = 90, Example = "pelt 20, mane 55")] public float liftDeg;
        [Param(ParamUnit.Fraction, "comb toward the tail (along the body)", Min = 0, Max = 1)] public float flowBack;
        [Param(ParamUnit.Fraction, "comb toward the ground (down the flanks and legs)", Min = 0, Max = 1)] public float flowDown;
        [Param(ParamUnit.Degrees, "per-hair direction jitter", Min = 0, Max = 60)] public float jitterDeg;
        [Param(ParamUnit.Fraction, "strands: how much the tips gather into locks with their neighbours", Min = 0, Max = 1)] public float clump;
        [Param(ParamUnit.Count, "strands: strands per lock", Min = 1, Max = 32)] public int clumpSize;
        [Param(ParamUnit.Hz, "strands: spring frequency (how fast a hair swings back; / sqrt(s))", Min = 0.2f, Max = 20, Example = "mane 3, tufts 2")] public float stiffnessHz;
        [Param(ParamUnit.Fraction, "strands: damping ratio (0.2 swishy .. 1 dead)", Min = 0, Max = 2)] public float damping;
        [Param(ParamUnit.Fraction, "how much gravity pulls the hair down (strands: x g against the spring; undercoat: the outer shells' sag)", Min = 0, Max = 2)] public float droop;
    }

    [Serializable]
    [Module("Fur", CreatureModule.Look, KitOnly = true, Needs = "SpineBody",
            Summary = "Hair in regions: an Undercoat of shells (short dense fur) and Strands (long fur, manes, tufts) as GPU blades skinned with the body's bone matrices and simulated as springs that trail the motion and droop (Scripts/Creatures/Fur, Resources/CreatureFur). Off = no extra draw.")]
    public sealed class Fur : CreatureModule
    {
        [Param(ParamUnit.Struct, "the furry regions")] public FurRegion[] regions = new FurRegion[0];
        [Param(ParamUnit.Count, "seed of the root placement (deterministic per species)")] public int seed = 1;
        [Param(ParamUnit.Count, "most strands per individual (densities scale down to fit)", Min = 0, Max = 65536)] public int maxStrands = 8000;
        [Param(ParamUnit.Count, "undercoat shells (0 = no undercoat)", Min = 0, Max = 32)] public int shells = 14;
        [Param(ParamUnit.Count, "individuals of this species with strands at once (the nearest)", Min = 0, Max = 64)] public int maxSimulated = 6;
        [Param(ParamUnit.Metres, "strands: full count closer than this to the camera (x sizeScale)", Example = "6")] public float strandFadeStart = 5f;
        [Param(ParamUnit.Metres, "strands: none past this (x sizeScale)", Example = "14")] public float strandFadeEnd = 12f;
        [Param(ParamUnit.Metres, "undercoat: shells thin to 3 by this distance (x sizeScale); none past twice it")] public float shellFadeEnd = 20f;
        [Param(ParamUnit.Fraction, "how much of the body's pattern shows through the fur", Min = 0, Max = 1)] public float pattern = 0.8f;
        [Param(ParamUnit.Fraction, "darker roots (the fur's ambient occlusion)", Min = 0, Max = 1)] public float rootOcclusion = 0.65f;
        [Param(ParamUnit.Fraction, "soft anisotropic highlight along the hair (Kajiya-Kay)", Min = 0, Max = 1)] public float highlight = 0.3f;
        [Param(ParamUnit.Fraction, "light through the tips against the sun", Min = 0, Max = 2)] public float translucency = 0.5f;

        // Part ids that never grow hair: claws/cones (2), eyes (3), membranes (8), pincers/jaws (11), teeth (13).
        public const int ForbiddenParts = (1 << 2) | (1 << 3) | (1 << 8) | (1 << 11) | (1 << 13);

        public bool HasKind(FurKind k)
        {
            if (regions == null) return false;
            foreach (FurRegion r in regions) if (r.kind == k && r.length > 0f && r.density > 0f) return true;
            return false;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return "Fur needs a SpineBody";
            if (regions == null || regions.Length == 0) return "Fur: no regions";
            if (regions.Length > FurBuilder.MaxRegions) return $"Fur: at most {FurBuilder.MaxRegions} regions";
            if (maxStrands < 0 || shells < 0 || shells > 32 || maxSimulated < 0) return "Fur: maxStrands, shells (0..32), maxSimulated >= 0";
            if (!(strandFadeEnd > strandFadeStart) || strandFadeStart < 0f || !(shellFadeEnd > 0f)) return "Fur: fade distances must be strandFadeStart < strandFadeEnd, shellFadeEnd > 0";
            for (int i = 0; i < regions.Length; i++)
            {
                FurRegion r = regions[i];
                string n = $"Fur region {i} '{r.name}'";
                if (r.partMask == 0) return n + ": partMask is empty";
                if ((r.partMask & ForbiddenParts) != 0) return n + ": partMask includes claws, eyes, membranes, jaws or teeth";
                if (!(r.arcTo > r.arcFrom) || r.arcEdge < 0f) return n + ": arcFrom < arcTo, arcEdge >= 0";
                if (!(r.dorsalTo > r.dorsalFrom) || r.dorsalEdge < 0f) return n + ": dorsalFrom < dorsalTo, dorsalEdge >= 0";
                if (!(r.length > 0f) || !(r.density > 0f) || !(r.width > 0f)) return n + ": length, density and width must be > 0";
                if (r.lengthJitter < 0f || r.lengthJitter > 0.9f) return n + ": lengthJitter in 0..0.9";
                if (r.kind == FurKind.Strands)
                {
                    if (!(r.stiffnessHz > 0f) || r.damping < 0f) return n + ": stiffnessHz > 0, damping >= 0";
                    if (r.clumpSize < 1 || r.clump < 0f || r.clump > 1f) return n + ": clumpSize >= 1, clump in 0..1";
                }
            }
            return null;
        }

        public override string ToString() => "Fur";
    }
}
