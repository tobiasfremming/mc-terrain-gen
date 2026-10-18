// The harness checks itself: an assertion that cannot fail makes every offline result vacuous.
using NUnit.Framework;

namespace TerrainGen.Tests.Harness
{
    public class HarnessSelfTests
    {
        static bool Fails(TestDelegate d) { try { d(); return false; } catch (AssertionException) { return true; } }

        // RED proof 2026-10-18: a golden hash off by one in the last bit passed while AreEqual compared numbers as double.
        [Test] public void AreEqual_UlongOffByOne_Fails()
            => Assert.IsTrue(Fails(() => Assert.AreEqual(0x53B537BC3A04F007UL, 0x53B537BC3A04F006UL)), "ulong compare is not exact");

        [Test] public void AreEqual_MixedNumbers_ByValue()
        {
            Assert.AreEqual(3, 3L);
            Assert.AreEqual(0, 0f);
            Assert.IsTrue(Fails(() => Assert.AreEqual(1, 2)));
            Assert.IsTrue(Fails(() => Assert.AreEqual(1f, 1.001f, 1e-4f)));
        }

        [Test] public void Species_LoadFromYaml_AllKnownKeys()
        {
            string[] g = UnityEditor.AssetDatabase.FindAssets("t:CreatureSpecies", new[] { "Assets/TerrainGen/Config/Creatures" });
            Assert.Greater(g.Length, 5, "species assets found");
            foreach (string id in g)
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(id);
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(p), p);
                Assert.AreEqual(0, HarnessYaml.UnknownKeys(HarnessPaths.Full(p)).Count, p + ": " + string.Join(", ", HarnessYaml.UnknownKeys(HarnessPaths.Full(p))));
            }
        }
    }
}
