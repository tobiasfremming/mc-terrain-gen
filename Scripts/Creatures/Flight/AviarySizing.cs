using UnityEngine;

// The lab aviary's size for a flying species (CreatureLab, the kit's Lab check). The base aviary (14 m x 10 m
// at scale 1, design decision 4) grows with the largest individual's size up to kScaleCap; past that - and
// for any flyer whose flight needs more room - the aviary follows the flight itself:
//   radius: its cruise turn (FlightBrain.FitsAviary wants two turn radii of safe disc) and its landing final,
//           plus its reach, and room for its body length;
//   height: three times its minimum altitude plus room for its tallest twisting manoeuvre (AirTwist) twice
//           over (cruise altitudes are drawn from 0.4..0.8 of the roof).
// A small flyer (the winged serpent) keeps exactly the base rule.
public static class AviarySizing
{
    public const float kScaleCap = 1.5f;
    const float kTurnRadii = 2.6f, kLandingTurns = 2.2f, kBodyLengths = 1.6f, kAltitudes = 3f, kRises = 2f;

    public static float Radius(CreatureSpecies sp, float maxScale, float baseRadius)
    {
        float legacy = baseRadius * Mathf.Max(1f, maxScale);
        if (sp == null) return legacy;
        sp = sp.Compiled.Runtime;
        if (!sp.CanFly) return legacy;
        float based = baseRadius * Mathf.Clamp(maxScale, 1f, kScaleCap);
        float v = sp.flight.cruiseSpeed * Mathf.Sqrt(maxScale);
        float turn = v * v / (FlightModel.G * Mathf.Tan(sp.flight.maxBankDeg * Mathf.Deg2Rad));
        float reach = maxScale * (sp.MaxLateralReach + 0.2f);
        // the turn, the landing approach (FlightBrain's final: flareDistance plus about two turn radii), the body
        float need = Mathf.Max(kTurnRadii * turn + reach, sp.flight.flareDistance * maxScale + kLandingTurns * turn + reach);
        need = Mathf.Max(need, kBodyLengths * sp.BodyLength * maxScale);
        return Mathf.Max(based, need);
    }

    public static float Height(CreatureSpecies sp, float maxScale, float baseHeight)
    {
        float legacy = baseHeight * Mathf.Max(1f, maxScale);
        if (sp == null) return legacy;
        sp = sp.Compiled.Runtime;
        if (!sp.CanFly) return legacy;
        float based = baseHeight * Mathf.Clamp(maxScale, 1f, kScaleCap);
        AirTwistSpec t = sp.flight.twist;
        float rise = Mathf.Max(Mathf.Max(t.corkscrewAmplitude, t.barrelRadius), t.loopRadius) * 2f * maxScale;
        float need = kAltitudes * sp.flight.minAltitude * maxScale + kRises * rise;
        return Mathf.Max(based, need);
    }
}
