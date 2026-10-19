using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit T4.1, the Unity half (the offline half: CreaturePartLayerTests). Needs the Editor and a graphics
    // device; written offline, first run in the T4.1 Editor visit.
    //   Shader_Layers_LegacyIdsUnchanged  every shipped spine species drawn at rest from three views with the frozen
    //                                     pre-table shader (Tests/EditMode/Shaders/CreatureLegacyReference.shader)
    //                                     and with MarchingCubes/Creature - same mesh, bones and property block (the
    //                                     renderer's own, table included): every pixel within 1/255
    //   CreatureShader_EditorRuntimeCompile_NoErrors lives in CreatureRendererTests (the on-demand variant compile)
    public class CreatureShaderLayerTests
    {
        const string kShader = "MarchingCubes/Creature", kLegacy = "Hidden/TerrainGen/CreatureLegacyReference";
        const int kSize = 192;

        readonly List<Object> trash = new List<Object>();
        readonly List<CreatureRenderer> renderers = new List<CreatureRenderer>();

        [TearDown]
        public void TearDown()
        {
            foreach (var r in renderers) r.Dispose();
            renderers.Clear();
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        T Keep<T>(T o) where T : Object { trash.Add(o); return o; }

        [Test]
        public void LegacyReference_CompilesWithoutErrors()
        {
            Shader sh = Shader.Find(kLegacy);
            Assert.IsNotNull(sh, kLegacy + " not found (Tests/EditMode/Shaders imported?)");
            Assert.IsFalse(ShaderUtil.ShaderHasError(sh), kLegacy + " has errors");
            Assert.IsFalse(ShaderUtil.ShaderHasError(Shader.Find(kShader)), kShader + " has errors");
        }

        [Test]
        public void Shader_Layers_LegacyIdsUnchanged()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("needs a graphics device (interactive Editor)");
            Shader cur = Shader.Find(kShader), legacy = Shader.Find(kLegacy);
            Assert.IsNotNull(cur); Assert.IsNotNull(legacy);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;   // compile the variants now, not as cyan placeholders
            var matCur = Keep(new Material(cur)); var matOld = Keep(new Material(legacy));
            // the shipped material's look constants on both (the reference has the same properties)
            var shipped = AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/Creature.mat");
            if (shipped != null) { matCur.CopyPropertiesFromMaterial(shipped); matOld.CopyPropertiesFromMaterial(shipped); }
            var rt = new RenderTexture(kSize, kSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(kSize, kSize, TextureFormat.RGBA32, false);
            var report = new List<string>();
            try
            {
                int species = 0;
                foreach (string name in CreatureKitAssets.Species())
                {
                    CreatureSpecies sp = CreatureKitAssets.Load(name);
                    if (sp == null || sp.bodyPlan == CreatureBodyPlan.Cephalopod) continue;   // the octopus: its own shader family
                    var r = new CreatureRenderer(sp, matCur);
                    renderers.Add(r);
                    var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
                    r.Add(sim, new CreatureInstance { seed = 3.7f, sizeScale = sp.scale });
                    r.Render();   // fills the property block (bones, instances, palette, the layer table)
                    Bounds b = r.LastBounds;
                    float dist = b.extents.magnitude * 2.6f + 0.05f;
                    Vector3[] views = { new Vector3(1f, 0.6f, 0.4f), new Vector3(0.1f, 1f, -0.2f), new Vector3(-0.6f, -0.35f, 0.7f) };
                    int maxDiff = 0, covered = 0, over = 0;
                    foreach (Vector3 v in views)
                    {
                        Vector3 eye = b.center + v.normalized * dist;
                        Color32[] a = Draw(r, matCur, eye, b.center, dist, rt, tex), o = Draw(r, matOld, eye, b.center, dist, rt, tex);
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (a[i].a > 0 || o[i].a > 0) covered++;
                            int d = Mathf.Max(Mathf.Abs(a[i].r - o[i].r), Mathf.Max(Mathf.Abs(a[i].g - o[i].g), Mathf.Abs(a[i].b - o[i].b)));
                            if (Mathf.Abs(a[i].a - o[i].a) > 0) d = 255;   // coverage changed
                            maxDiff = Mathf.Max(maxDiff, d);
                            if (d > 1) over++;
                        }
                    }
                    TestContext.Out.WriteLine($"{name}: {covered} covered pixels, max channel difference {maxDiff}/255, {over} over 1/255");
                    Assert.Greater(covered, 300, name + ": the creature is not in view");
                    if (over > 0) report.Add($"{name}: {over} pixels over 1/255 (max {maxDiff})");
                    species++;
                }
                Assert.Greater(species, 4);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                RenderTexture.active = null;
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            Assert.IsEmpty(report, string.Join("\n", report));
        }

        // One Forward-pass draw of the renderer's single instance into rt, read back.
        static Color32[] Draw(CreatureRenderer r, Material mat, Vector3 eye, Vector3 at, float dist, RenderTexture rt, Texture2D tex)
        {
            int pass = mat.FindPass("Forward");
            Assert.GreaterOrEqual(pass, 0, mat.shader.name + ": no Forward pass");
            var cmd = new CommandBuffer { name = "CreatureShaderLayerTests" };
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, 0f));
            Matrix4x4 view = Matrix4x4.TRS(eye, Quaternion.LookRotation(at - eye, Mathf.Abs(Vector3.Dot((at - eye).normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up), new Vector3(1f, 1f, -1f)).inverse;
            Matrix4x4 proj = Matrix4x4.Perspective(35f, 1f, dist * 0.05f, dist * 4f);
            cmd.SetViewProjectionMatrices(view, proj);
            cmd.DrawMeshInstancedProcedural(r.Mesh, 0, mat, pass, 1, r.PropsForTest);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, kSize, kSize), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex.GetPixels32();
        }
    }
}
