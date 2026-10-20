// Creature kit: body-plan ARCHETYPES. An archetype is a sugar part (PartKind.Sugar) that compose (CompiledSpecies)
// expands, like Repeat, into a complete animal's ordinary parts plus default modules, from a handful of anatomical
// numbers; the kit services (Kit/Services: RestStance, GaitDefaults) derive everything that follows from anatomy.
// The runtime, the checks, the catalogue and the lab see only the ordinary parts.
//
// Override path, in three steps:
//   1. the plan's numbers (0 = the form's default);
//   2. an explicit part in the asset with a generated id replaces it: a mirrored part's base id ("leg1") replaces
//      both sides, one side's id ("leg1.R") only that side; an explicit module replaces the generated default of its type;
//   3. added parts and modules are layered on (spines on the generated carapace, hairs on a generated leg).
// A generated Tuck (a posture part that must be last) is moved after every other part.
using System.Collections.Generic;

namespace CreatureKit
{
    public interface IArchetype
    {
        // parts: append the generated parts (compose skips the ids the asset lists itself); modules: the species'
        // modules so far - add a default only when its type is missing. Throw InvalidOperationException for a plan
        // that cannot be built (compose reports it as an error).
        void Generate(List<CreaturePart> parts, List<CreatureModule> modules);
    }
}
