// Creature kit (design §2.3, §3a): the metadata every component type carries. The catalogue generator
// (CatalogueWriter), the size rules and the generic kit tests all read these attributes, so a new part or
// module documents itself: declare the class with [Part]/[Module] and every field with [Param].
using System;

namespace CreatureKit
{
    // How a parameter follows the individual's sizeScale s (Froude similarity): lengths x s, times and speeds
    // x sqrt(s), rates / sqrt(s); angles, fractions, counts and accelerations are size-free.
    public enum ParamUnit
    {
        Metres,          // x s
        Seconds,         // x sqrt(s)
        MetresPerSecond, // x sqrt(s)
        Hz,              // / sqrt(s)
        DegPerSecond,    // / sqrt(s)
        Acceleration,    // m/s^2: size-free under Froude scaling (L / T^2 = s / s)
        Degrees,         // size-free
        Direction,       // a direction (normalised by its user), size-free
        Fraction,        // size-free, usually 0..1
        Count,           // integer, size-free
        Joint,           // a spine joint index
        PartRef,         // the id of another part of the same species
        Flag,            // bool
        SrgbColour,      // authored sRGB (Inspector colour), decoded once where it enters shading
        Enum,            // one of the listed names
        Text,            // a name or id
        Struct,          // a nested kit struct or array of them; its own fields carry [Param]
        Spec,            // an existing runtime spec class held by value (WingSpec, FlightSpec, CephalopodSpec):
                         // its fields are documented where it is defined (creature-module skill)
    }

    public static class UnitRules
    {
        public static float SizeFactor(ParamUnit u, float s)
        {
            switch (u)
            {
                case ParamUnit.Metres: return s;
                case ParamUnit.Seconds: case ParamUnit.MetresPerSecond: return (float)Math.Sqrt(s);
                case ParamUnit.Hz: case ParamUnit.DegPerSecond: return 1f / (float)Math.Sqrt(s);
                default: return 1f;
            }
        }

        public static string Rule(ParamUnit u)
        {
            switch (u)
            {
                case ParamUnit.Metres: return "x s";
                case ParamUnit.Seconds: case ParamUnit.MetresPerSecond: return "x sqrt(s)";
                case ParamUnit.Hz: case ParamUnit.DegPerSecond: return "/ sqrt(s)";
                default: return "-";
            }
        }
    }

    // A part type: what the animal IS (body, limb, wing, chain, fin, rigid cone, sense organ).
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PartAttribute : Attribute
    {
        public readonly string Name; public readonly PartKind Kind;
        public string Summary = "", System = "", Status = "exists", Branch = "", UseWhen = "";
        public string Bones = "0";           // how many bones it takes, for the catalogue ("= joints", "3", ...)
        public int[] MaterialLayers = new int[0];
        // no legacy CreatureSpecies form (it exists only in kit assets): legacy-equivalence tests skip a species
        // that uses it (CreatureKitAssets.KitOnlyReason)
        public bool KitOnly;
        public PartAttribute(string name, PartKind kind) { Name = name; Kind = kind; }
    }

    // A module type: what the animal DOES (Locomotion, Behaviour) or how it LOOKS (Look).
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ModuleAttribute : Attribute
    {
        public readonly string Name, Category;
        public string Summary = "", Status = "exists", Needs = "", Branch = "";
        public bool KitOnly;   // no legacy form (see PartAttribute.KitOnly)
        public ModuleAttribute(string name, string category) { Name = name; Category = category; }
    }

    // One authored field: its unit (and so its size rule), what it means, its range and an example.
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ParamAttribute : Attribute
    {
        public readonly ParamUnit Unit; public readonly string Meaning;
        public float Min = float.NaN, Max = float.NaN;
        public string Default = "", Example = "";
        public ParamAttribute(ParamUnit unit, string meaning) { Unit = unit; Meaning = meaning; }
        // fixed format: the catalogue must be byte-identical from Mono (Unity) and .NET (the harness)
        public string Range => float.IsNaN(Min) && float.IsNaN(Max) ? "" :
            (float.IsNaN(Min) ? "" : Min.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)) + ".." +
            (float.IsNaN(Max) ? "" : Max.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture));
    }
}
