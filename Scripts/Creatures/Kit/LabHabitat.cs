// Creature kit T3.5 (design §2.3): the lab habitat a species needs, from its parts and modules - no per-type code
// in the lab:
//   Grove      a soft body or the Ambush behaviour: the octopus grove (OctopusLab: climbable trees, prey)
//   Aviary     it can fly (Fly with wings and a cruise speed): posts, trees, rock perches, a roof (AviarySizing)
//   Climbable  Climb: an enclosure whose shade tree it may climb (ClimbingBrain)
//   Plain      otherwise: an enclosure with a shade tree it walks round
// Sizes: an aviary follows the flight (AviarySizing), an enclosure is 2 body lengths of radius, at least 5 m, and
// large enough that the head's safe disc (EnclosureArea.For: the radius less the lateral reach and margin, x size)
// keeps kMinSafeLengths body lengths of room - a long creature with long legs (a giant centipede) is not penned
// into tight circles. Every other shipped species' enclosure is unchanged by that floor (their safe discs are
// >= 1.66 body lengths already).
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public enum Habitat { Plain = 0, Climbable = 1, Aviary = 2, Grove = 3 }

    public static class LabHabitat
    {
        public static Habitat For(CreatureSpecies sp)
        {
            sp = sp.Compiled.Runtime;
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return Habitat.Grove;
            IReadOnlyList<CreatureModule> mods = KitView.Modules(sp);
            bool climb = false;
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i] is Ambush) return Habitat.Grove;
                if (mods[i] is Climb) climb = true;
            }
            if (sp.CanFly) return Habitat.Aviary;
            return climb && sp.climb.canClimb ? Habitat.Climbable : Habitat.Plain;
        }

        // The enclosure (or aviary) radius for the species' largest individual (size scale maxScale).
        public const float MinSafeLengths = 1.6f;

        public static float EnclosureRadius(CreatureSpecies sp, float maxScale, float aviaryRadius)
        {
            if (sp != null && For(sp) == Habitat.Aviary) return AviarySizing.Radius(sp, maxScale, aviaryRadius);
            CreatureSpecies rt = sp.Compiled.Runtime;
            float length = rt.BodyLength * maxScale;
            float safeFloor = MinSafeLengths * length + maxScale * (rt.MaxLateralReach + EnclosureArea.DefaultMargin);
            return Mathf.Max(5f, Mathf.Max(2f * length, safeFloor));
        }
    }
}
