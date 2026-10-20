// Creature kit: QuadLaunch - a quadrupedal launch (a pterosaur's, a vampire bat's): the flyer crouches on its four
// limbs, then vaults - the folded wing arms push the body up off the ground far faster than the shipped launch's hop,
// the wings open at the top and the first downstroke follows. Data only: written into the flight spec at compose
// (FlightSpec.launchCrouch / launchCrouchDepth / vaultSpeed, read by CreatureFlight.Launch); a flyer without it keeps
// the shipped launch exactly.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Module("QuadLaunch", CreatureModule.Locomotion, Summary = "A quadrupedal launch: a crouch on the four limbs (the body lowered by crouchDepth over crouchSeconds), then a vault at vaultSpeed off the folded wing arms; the wings open at the top (CreatureFlight.Launch). Without it a flyer hops off at the shipped 2.4 m/s.",
            Needs = "Fly", KitOnly = true)]
    public sealed class QuadLaunch : CreatureModule
    {
        [Param(ParamUnit.Seconds, "the crouch before the vault", Min = 0.05f, Max = 2, Example = "apex 0.4")] public float crouchSeconds = 0.4f;
        [Param(ParamUnit.Metres, "how far the body sinks in the crouch (every joint's clearance)", Example = "apex 0.12")] public float crouchDepth = 0.12f;
        [Param(ParamUnit.MetresPerSecond, "the vault's upward speed (the shipped launch: 2.4)", Example = "apex 4")] public float vaultSpeed = 4f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Module<Fly>() == null) return "QuadLaunch needs Fly";
            if (!(crouchSeconds > 0f) || crouchDepth < 0f || !(vaultSpeed > 0f)) return "QuadLaunch: crouchSeconds > 0, crouchDepth >= 0, vaultSpeed > 0";
            return null;
        }

        public override void Configure(CreatureSpecies runtime)
        {
            if (runtime.flight == null) return;
            runtime.flight.launchCrouch = crouchSeconds; runtime.flight.launchCrouchDepth = crouchDepth; runtime.flight.vaultSpeed = vaultSpeed;
        }
    }
}
