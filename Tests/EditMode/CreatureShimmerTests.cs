using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The kit's Shimmer look (the SkyDragon's signature): it reaches the runtime look of the dragon only; every other
    // species keeps shimmer 0, so Creature.shader's shimmer branch never runs for them (their pixels unchanged; the
    // Unity pixel guard in CreatureShaderTests covers the shader itself).
    public class CreatureShimmerTests
    {
        [Test] public void Shimmer_OnTheDragon_OffForEveryOtherSpecies()
        {
            foreach (string name in CreatureKitAssets.Species())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(name);
                if (name == "SkyDragon")
                {
                    Assert.Greater(sp.look.shimmer, 0f, "the dragon shimmers");
                    Assert.Greater(sp.look.shimmerWavelength, 0f);
                    Assert.Greater(sp.look.shimmerPeriod, 0f);
                }
                else Assert.AreEqual(0f, sp.look.shimmer, name + ": no shimmer");
            }
        }

        [Test] public void Shimmer_Validate()
        {
            var ok = new CreatureKit.Shimmer { strength = 0.5f, wavelength = 0.2f, period = 2f };
            var ctx = new CreatureKit.SpeciesCtx(null, null, 10, false);
            Assert.IsNull(ok.Validate(ctx));
            ok.period = 0f;
            Assert.IsNotNull(ok.Validate(ctx), "a band needs a period");
        }
    }
}
