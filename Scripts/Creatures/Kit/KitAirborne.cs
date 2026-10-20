// Creature kit: generic contracts for animals the legacy systems did not foresee (added with the sky manta, all
// off by default - a species without these parts and modules is untouched, bit for bit):
//   ISpineChainBody  a Body part other than SpineBody that lays out the spine chain the sim runs (RayBody): its
//                    joints and mesh-free legacy fields, as SpineBody writes them.
//   IAirborne        a locomotion module that keeps a species in the air without the legacy Wing/Fly pair (Soar):
//                    the lab gives it an aviary sized by the module, the kit checks give it its roof and its own
//                    brain. CreatureSpecies.CanFly stays the legacy wings' flag.
//   IFlapDriver      what a flapping mode tells the parts that flap with it (RayWing): the stroke's phase and
//                    amplitude, the turn's asymmetry and the body's acceleration (the wings' inertia).
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public interface ISpineChainBody
    {
        int JointCount { get; }
        float Spacing { get; }
        float HeadCapLength { get; }
        float TailTipLength { get; }
        int RigidHeadJoints { get; }
        SpineJoint[] BuildJoints();
    }

    public interface IAirborne
    {
        // the aviary for the largest individual (maxScale); base* = the lab's default aviary at scale 1
        float AviaryRadius(CreatureSpecies sp, float maxScale, float baseRadius);
        float AviaryHeight(CreatureSpecies sp, float maxScale, float baseHeight);
        // the flight fits the area (turns inside the safe disc, cruises under the roof)
        bool FitsAviary(CreatureSpecies sp, EnclosureArea area, float sizeScale);
    }

    public interface IFlapDriver
    {
        float StrokePhase { get; }       // radians; the root's dihedral is sin(phase): + up
        float StrokeAmplitude { get; }   // 0 gliding .. 1 full beat (eased)
        float StrokeHz { get; }          // this individual's beat frequency (Froude-scaled)
        float Asymmetry { get; }         // -1..1: + the right wing beats bigger (a turn to the left)
        float Bank { get; }              // radians, + right wing down
        Vector3 BodyAcceleration { get; } // world m/s^2 of the body (the wings' inertia reacts to it)
    }

    public static class Airborne
    {
        // The species' airborne locomotion module, or null (every shipped species: null).
        public static IAirborne Of(CreatureSpecies sp)
        {
            if (sp == null) return null;
            IReadOnlyList<CreatureModule> mods = KitView.Modules(sp.Compiled.Runtime);
            for (int i = 0; i < mods.Count; i++) if (mods[i] is IAirborne a) return a;
            return null;
        }
    }
}
