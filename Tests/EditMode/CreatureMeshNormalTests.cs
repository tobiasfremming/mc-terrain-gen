using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The glowing heads and tails (user bug 2026-10-19: "the dragon's head is glowing, same with the snake's head and
    // tail ... a white light so bright it masks out everything"). FinStripMesh normalised its vertex normals with
    // Vector3.normalized, which returns ZERO below a magnitude of 1e-5; a small fin's cross product (the sand eel's
    // mm-sized dorsal and anal fins at their tapered ends, the SkyDragon's mane / beard / cheek frills / tail tuft at
    // the tuft valleys) is ~1e-6, so those vertices carried (0,0,0). Creature.shader's normalize() made that a NaN
    // pixel, and URP's bloom spread the NaN into a screen-filling white blow-out around the head and tail.
    // Every vertex of every species' rest mesh must have a unit normal, finite attributes and a registered part id.
    // Pure CPU: runs offline (the harness Vector3.normalized has Unity's 1e-5 cut-off) and in Unity.
    // The GPU half: CreatureShaderLayerTests.Shader_HeadTail_NoNanOrBlowout.
    [TestFixtureSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
    public class CreatureMeshNormalTests
    {
        readonly string name;
        public CreatureMeshNormalTests(string species) { name = species; }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        [Test] public void Mesh_UnitNormals_FiniteAttributes_RegisteredParts()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            Assert.IsNotNull(sp, name);
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) Assert.Ignore("cephalopod: OctopusMeshBuilder, its own shader family");
            MeshBytes m = MeshBytes.Current(sp);
            Assert.Greater(m.pos.Length, 0, name + ": empty mesh");
            Assert.AreEqual(m.pos.Length, m.nrm.Length, name + ": normal count");
            int bad = 0; string first = null;
            for (int i = 0; i < m.pos.Length; i++)
            {
                Vector3 p = m.pos[i], n = m.nrm[i];
                Vector2 a = m.uv0[i], b = m.uv1[i];
                Vector4 w = m.uv2[i];
                string why = null;
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) why = "position not finite";
                else if (!Finite(n.x) || !Finite(n.y) || !Finite(n.z) || Mathf.Abs(n.magnitude - 1f) > 1e-3f) why = $"normal {n.x:G4},{n.y:G4},{n.z:G4} (|n| {n.magnitude:G4})";
                else if (!Finite(a.x) || !Finite(a.y) || !Finite(b.x) || !Finite(b.y) || !Finite(w.x) || !Finite(w.y) || !Finite(w.z) || !Finite(w.w)) why = "uv not finite";
                else
                {
                    int part = Mathf.RoundToInt(b.x);
                    if (Mathf.Abs(b.x - part) > 1e-4f || part < 0 || part >= PartLayers.Registry.Length) why = $"part id {b.x} not in the registry (0..{PartLayers.Registry.Length - 1})";
                }
                if (why == null) continue;
                if (bad++ == 0) first = $"vertex {i} (part {b.x:G3}, uv0 {a.x:F3},{a.y:F3}, uv1.y {b.y:F2}): {why}";
            }
            Assert.AreEqual(0, bad, $"{name}: {bad} bad vertices; first {first}");
        }
    }
}
