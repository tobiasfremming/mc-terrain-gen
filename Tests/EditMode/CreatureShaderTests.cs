using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Tree octopus task 5: the octopus shader, the generic cloak and the 32-byte instance data.
    public class CreatureShaderTests
    {
        const string kRoot = "Assets/TerrainGen/Shaders/";

        static string Source(string file) => File.ReadAllText(kRoot + file);

        // Both creature shaders compile (the Editor's shader compiler; an errored shader draws nothing).
        [Test] public void Shaders_HaveNoErrors()
        {
            foreach (string name in new[] { "MarchingCubes/Creature", CreatureWorld.OctopusShaderName })
            {
                Shader sh = Shader.Find(name);
                Assert.IsNotNull(sh, name + " not found");
                Assert.IsFalse(ShaderUtil.ShaderHasError(sh), name + " has errors");
            }
        }

        // The cloak clips only in the Forward pass: the ShadowCaster, DepthOnly and DepthNormals passes never
        // call it, so a cloaked creature keeps its shadow and contact AO (Cloak_ShadowRemains).
        [Test] public void Cloak_ShadowRemains()
        {
            foreach (string file in new[] { "Creature.shader", "CreatureOctopus.shader" })
            {
                string src = Source(file);
                MatchCollection passes = Regex.Matches(src, "Name \"(\\w+)\"");
                Assert.GreaterOrEqual(passes.Count, 4, file);
                for (int p = 0; p < passes.Count; p++)
                {
                    int start = passes[p].Index, end = p + 1 < passes.Count ? passes[p + 1].Index : src.Length;
                    string body = src.Substring(start, end - start);
                    bool cloaks = body.Contains("CreatureCloak(");
                    if (passes[p].Groups[1].Value == "Forward") Assert.IsTrue(cloaks, file + ": Forward does not cloak");
                    else Assert.IsFalse(cloaks, $"{file}: pass {passes[p].Groups[1].Value} cloaks (its shadow / depth would vanish)");
                }
            }
        }

        // C# CreatureInstance and the HLSL struct match field for field (8 floats, 32 bytes).
        [Test] public void Instance_HlslMatchesCSharp()
        {
            string src = Source("Creature/CreatureSkin.hlsl");
            Match m = Regex.Match(src, "struct CreatureInstance\\s*\\{([^}]*)\\}");
            Assert.IsTrue(m.Success);
            var fields = Regex.Matches(m.Groups[1].Value, "float\\s+(\\w+);");
            string[] want = { "seed", "hueShift", "valueShift", "sizeScale", "cloak", "chroma", "flash", "cloudPhase" };
            Assert.AreEqual(want.Length, fields.Count);
            for (int i = 0; i < want.Length; i++) Assert.AreEqual(want[i], fields[i].Groups[1].Value);
            var cs = typeof(CreatureInstance).GetFields();
            Assert.AreEqual(want.Length, cs.Length);
            for (int i = 0; i < want.Length; i++) Assert.AreEqual(want[i], cs[i].Name);
        }

        // Skin_Cofactor_MonitorUnchanged: the spine shader's skinning is the former code (the cofactor normal
        // and the four-bone blend exist only under CREATURE_FOUR_BONES, which only the octopus shader defines).
        [Test] public void Skin_Cofactor_MonitorUnchanged()
        {
            Assert.IsFalse(Source("Creature.shader").Contains("CREATURE_FOUR_BONES"), "the spine shader must not take the octopus skin path");
            Assert.IsTrue(Source("CreatureOctopus.shader").Contains("#define CREATURE_FOUR_BONES"));
            string skin = Source("Creature/CreatureSkin.hlsl");
            Assert.IsTrue(skin.Contains("o.normalWS = normalize(mul((float3x3)m, v.normalOS));"), "the spine normal path changed");
        }
    }
}
