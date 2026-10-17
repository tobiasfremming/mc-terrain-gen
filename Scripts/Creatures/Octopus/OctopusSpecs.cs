using System;
using UnityEngine;

// Species data for a cephalopod body plan (tree octopus design, 2026-10-17, section 1). Plain serialisable
// structs: they will become CreatureSpecies fields (bodyPlan Cephalopod) once the ICreatureBody refactor
// lands. Every field follows the LegSpec rule "0 = default", so an asset authored before a field existed
// still gets the tuned value. Lengths in metres at scale 1.
[Serializable]
public struct TentacleSpec
{
    public int segments;            // particles (= bones) per arm
    public float length;            // root to tip at rest
    public float rootRadius, tipRadius;
    public float taper;             // radius profile exponent: r = tip + (root - tip) (1 - s/L)^taper
    public float flatten;           // ventral half-height / half-width (dorsal half is round)
    public float bendLimitDeg;      // cone half-angle per joint
    public float tipCurlDeg;        // rest curl toward ventral at the last joint (rises ~quadratically from the root)
    public float minStretch, maxStretch; // segment length bounds, x rest spacing
    public float maxTwistDeg;       // twist between consecutive frames
    public float twistRateDeg;      // how fast the twist follows its target, deg/s
    public float wrapSqueeze, bulge;     // cross-section scale on a wrapped run / just past it
    public float muscle;            // muscle tone 0..1 at rest (curvature target stiffness)
    public float damping;           // velocity damping relative to the root/body, 1/s
    public float massPerArm;        // kg at scale 1 (mass scales with size^3)
    public float sinusAmpDeg, sinusHz, sinusWaves; // travelling wave while reaching
    // Phase B (agent / wrap); stored here so the asset layout is final.
    public float wrapPitchDeg, wrapReach, reachSpeed, strikeTime;
    public int suckerRows;
    public float suckerSize;

    public int Segments => segments >= 4 ? segments : 24;
    public float Length => length > 0f ? length : 1.6f;
    public float RootRadius => rootRadius > 0f ? rootRadius : 0.05f;
    public float TipRadius => tipRadius > 0f ? tipRadius : 0.006f;
    public float Taper => taper > 0f ? taper : 0.9f;
    public float Flatten => flatten > 0f ? flatten : 0.85f;
    public float BendLimitDeg => bendLimitDeg > 0f ? bendLimitDeg : 28f;
    public float TipCurlDeg => tipCurlDeg > 0f ? tipCurlDeg : 24f;
    public float MinStretch => minStretch > 0f ? minStretch : 0.85f;
    public float MaxStretch => maxStretch > 0f ? maxStretch : 1.35f;
    public float MaxTwistDeg => maxTwistDeg > 0f ? maxTwistDeg : 20f;
    public float TwistRateDeg => twistRateDeg > 0f ? twistRateDeg : 240f;
    public float WrapSqueeze => wrapSqueeze > 0f ? wrapSqueeze : 0.92f;
    public float Bulge => bulge > 0f ? bulge : 1.05f;
    public float Muscle => muscle > 0f ? Mathf.Min(muscle, 1f) : 0.35f;
    public float Damping => damping > 0f ? damping : 1.2f;
    public float MassPerArm => massPerArm > 0f ? massPerArm : 0.3f;
    public float SinusAmpDeg => sinusAmpDeg > 0f ? sinusAmpDeg : 9f;
    public float SinusHz => sinusHz > 0f ? sinusHz : 1.1f;
    public float SinusWaves => sinusWaves > 0f ? sinusWaves : 1.5f;
    public float WrapPitchDeg => wrapPitchDeg > 0f ? wrapPitchDeg : 12f;
    public float WrapReach => wrapReach > 0f ? wrapReach : 0.6f;
    public float ReachSpeed => reachSpeed > 0f ? reachSpeed : 1.2f;
    public float StrikeTime => strikeTime > 0f ? strikeTime : 0.15f;
    public int SuckerRows => suckerRows > 0 ? suckerRows : 2;
    public float SuckerSize => suckerSize > 0f ? suckerSize : 0.6f;

    public float Spacing => Length / (Segments - 1);

    // Arm radius at arc length s (metres at scale 1) from the root.
    public float RadiusAt(float s)
    {
        float q = Mathf.Clamp01(1f - s / Length);
        return TipRadius + (RootRadius - TipRadius) * Mathf.Pow(q, Taper);
    }

    public static TentacleSpec Default => new TentacleSpec();
}

[Serializable]
public struct CephalopodSpec
{
    public int armCount;            // arms around the web ring (bone layout assumes the same count per species)
    public TentacleSpec arm;
    public float bodyMass;          // kg at scale 1, spread over the lattice (head heavier)
    public float webRadius;         // arm root ring radius
    public float webDepth;          // web skirt reaches this fraction of the arm length along each arm
    public float headRadius, mantleRadius, mantleLength;
    public float mantleTiltDeg;     // mantle axis tilt back from vertical
    public float eyeRadius;
    public float siphonLength, siphonRadius;
    public float beta;              // shape matching: blend of linear (beta) and rigid (1 - beta) goals
    public float headStiffness, tipStiffness; // cluster stiffness per 1/180 s (a default sub-step), head -> mantle tip
    public float breathAmp, breathHz;
    public float bodyDamping;       // lattice velocity damping relative to the body, 1/s
    public int subSteps, iterations;

    public int ArmCount => armCount >= 2 ? armCount : 8;
    public float BodyMass => bodyMass > 0f ? bodyMass : 2.4f;
    public float WebRadius => webRadius > 0f ? webRadius : 0.1f;
    public float WebDepth => webDepth > 0f ? webDepth : 0.2f;
    public float HeadRadius => headRadius > 0f ? headRadius : 0.12f;
    public float MantleRadius => mantleRadius > 0f ? mantleRadius : 0.15f;
    public float MantleLength => mantleLength > 0f ? mantleLength : 0.4f;
    public float MantleTiltDeg => mantleTiltDeg > 0f ? mantleTiltDeg : 50f;
    public float EyeRadius => eyeRadius > 0f ? eyeRadius : 0.03f;
    public float SiphonLength => siphonLength > 0f ? siphonLength : 0.07f;
    public float SiphonRadius => siphonRadius > 0f ? siphonRadius : 0.018f;
    public float Beta => beta > 0f ? Mathf.Min(beta, 1f) : 0.7f;
    public float HeadStiffness => headStiffness > 0f ? Mathf.Min(headStiffness, 1f) : 0.6f;
    public float TipStiffness => tipStiffness > 0f ? Mathf.Min(tipStiffness, 1f) : 0.03f;
    // breathAmp < 0 disables breathing (tests); 0 = default
    public float BreathAmp => breathAmp > 0f ? breathAmp : breathAmp < 0f ? 0f : 0.03f;
    public float BreathHz => breathHz > 0f ? breathHz : 0.4f;
    public float BodyDamping => bodyDamping > 0f ? bodyDamping : 0.3f;
    public int SubSteps => subSteps > 0 ? subSteps : 3;
    public int Iterations => iterations > 0 ? iterations : 3;

    public OctopusLook look;

    public static CephalopodSpec Default => new CephalopodSpec();

    // null when valid, else the first problem found.
    public string Validate()
    {
        if (armCount != 0 && armCount < 2) return "cephalopod.armCount must be >= 2";
        if (arm.segments != 0 && arm.segments < 4) return "cephalopod.arm.segments must be >= 4";
        if (arm.minStretch > 0f && arm.maxStretch > 0f && arm.minStretch > arm.maxStretch) return "cephalopod.arm stretch range is inverted";
        return null;
    }
}

// The octopus look (Shaders/CreatureOctopus.shader), on top of CreatureLook's palette (dorsal / flank /
// belly / pattern = the dark chromatophore colour). 0 = default for every number.
[Serializable]
public struct OctopusLook
{
    public Color sucker, subsurface;      // sucker rims / cups; red-orange light through thin arms and web
    public float chromaScale;             // chromatophore cells per metre
    public float papillae;                // papillae bump strength (normal only)
    public float suckerSize;              // sucker radius as a fraction of the arm radius
    public float cloudSpeed;              // passing-cloud bands, m/s along the body
    public float wetness;                 // clear-coat strength
    public float rimGlint;                // cloak edge glint

    public float ChromaScale => chromaScale > 0f ? chromaScale : 55f;
    public float Papillae => papillae > 0f ? papillae : 0.5f;
    public float SuckerSize => suckerSize > 0f ? suckerSize : 0.32f;
    public float CloudSpeed => cloudSpeed > 0f ? cloudSpeed : 0.35f;
    public float Wetness => wetness > 0f ? wetness : 0.8f;
    public float RimGlint => rimGlint > 0f ? rimGlint : 0.6f;
    public Color Sucker => sucker.a > 0f ? sucker : new Color(0.93f, 0.78f, 0.68f, 1f);
    public Color Subsurface => subsurface.a > 0f ? subsurface : new Color(0.95f, 0.35f, 0.18f, 1f);
}
