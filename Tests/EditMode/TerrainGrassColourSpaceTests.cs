using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Biome ground colours (Biome.colorFlat/colorSteep) and grass colours
    // (Biome.grassColorBase/Tip) are Inspector colours, authored in sRGB, and
    // both reach their shaders through SetVector(Array), which -- unlike
    // SetColor -- never converts. In this Linear project each path must turn
    // them into linear exactly once; before the fix neither did, so ground and
    // grass both shaded about twice as bright as authored.
    //
    //  - Ground: converted at upload (MCChunkManager.ShadingColor). Checked on
    //    the CPU: the published _BiomeShadeFlat/_BiomeShadeSteep globals must
    //    hold the linear value (sRGB 0.5 -> 0.214; it was 0.5).
    //  - Grass: kept sRGB through the RGB565 blade record and decoded in
    //    Grass.shader's BuildBlade. Checked on the GPU through the shader's
    //    AlbedoDebug pass into a float target. The per-blade random tint scales
    //    every channel alike, so the test compares red to green (green = 1.0):
    //    decoded, sRGB 16/31 gives 0.229; read as linear it gives 0.516.
    public class TerrainGrassColourSpaceTests
    {
        static readonly string[] kGlobalVectors = { "_BiomeSelParams", "_BiomeSelPlanet", "_BiomeSelFlags", "_BiomeSelBias0", "_BiomeSelBias1" };
        static readonly string[] kGlobalArrays = { "_BiomeShadeA", "_BiomeShadeFlat", "_BiomeShadeSteep", "_BiomeShadeLines" };

        [Test]
        public void BiomeShadingGlobals_AreUploadedLinear()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("only meaningful in Linear colour space");

            var biome = ScriptableObject.CreateInstance<Biome>();
            biome.hideFlags = HideFlags.HideAndDontSave;
            biome.colorFlat = new Color(0.5f, 0.5f, 0.5f, 1f);
            biome.colorSteep = new Color(0.25f, 0.5f, 0.75f, 1f);
            var world = ScriptableObject.CreateInstance<BiomeDensityField>();
            world.hideFlags = HideFlags.HideAndDontSave;
            world.biomes = new[] { biome };

            // The globals belong to whatever world the Editor is showing; put
            // them back afterwards.
            var savedVectors = new Vector4[kGlobalVectors.Length];
            for (int i = 0; i < kGlobalVectors.Length; i++) savedVectors[i] = Shader.GetGlobalVector(kGlobalVectors[i]);
            var savedArrays = new Vector4[kGlobalArrays.Length][];
            for (int i = 0; i < kGlobalArrays.Length; i++) savedArrays[i] = Shader.GetGlobalVectorArray(kGlobalArrays[i]);
            try
            {
                MCChunkManager.PublishBiomeShadingGlobals(world, null);
                Vector4[] flat = Shader.GetGlobalVectorArray("_BiomeShadeFlat");
                Vector4[] steep = Shader.GetGlobalVectorArray("_BiomeShadeSteep");
                Assert.IsNotNull(flat, "_BiomeShadeFlat not published");
                Assert.IsNotNull(steep, "_BiomeShadeSteep not published");

                float half = Mathf.GammaToLinearSpace(0.5f);   // 0.214
                Assert.That(flat[0].x, Is.EqualTo(half).Within(1e-3f),
                    "_BiomeShadeFlat[0].r = " + flat[0].x + " for sRGB 0.5; 0.214 is linear, 0.5 means uploaded as sRGB");
                Assert.That(steep[0].x, Is.EqualTo(Mathf.GammaToLinearSpace(0.25f)).Within(1e-3f), "steep r");
                Assert.That(steep[0].y, Is.EqualTo(half).Within(1e-3f), "steep g");
                Assert.That(steep[0].z, Is.EqualTo(Mathf.GammaToLinearSpace(0.75f)).Within(1e-3f), "steep b");
            }
            finally
            {
                for (int i = 0; i < kGlobalVectors.Length; i++) Shader.SetGlobalVector(kGlobalVectors[i], savedVectors[i]);
                for (int i = 0; i < kGlobalArrays.Length; i++)
                    if (savedArrays[i] != null && savedArrays[i].Length > 0) Shader.SetGlobalVectorArray(kGlobalArrays[i], savedArrays[i]);
                Object.DestroyImmediate(world);
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void ShadingColor_MatchesSetColorConversion()
        {
            var c = new Color(0.13f, 0.30f, 0.06f, 1f);
            Vector4 v = MCChunkManager.ShadingColor(c);
            Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            Assert.That(v.x, Is.EqualTo(expected.r).Within(1e-6f));
            Assert.That(v.y, Is.EqualTo(expected.g).Within(1e-6f));
            Assert.That(v.z, Is.EqualTo(expected.b).Within(1e-6f));
            Assert.That(v.w, Is.EqualTo(1f).Within(1e-6f), "alpha passes through");
        }

        // Same layout as GrassCommon.hlsl's Blade / GrassSystem.Blade (24 bytes).
        [StructLayout(LayoutKind.Sequential)]
        struct TestBlade { public Vector3 pos; public uint normalOct; public uint seed; public uint colors; }

        [Test]
        public void GrassShader_DecodesSrgbBladeColour()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("only meaningful in Linear colour space");
            Shader sh = Shader.Find("MarchingCubes/Grass");
            Assert.IsNotNull(sh, "MarchingCubes/Grass not found");
            var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            int pass = mat.FindPass("AlbedoDebug");
            Assert.GreaterOrEqual(pass, 0, "AlbedoDebug pass");

            // RGB565 with R = 16/31 (sRGB 0.516), G = 63/63 (1.0), B = 0; base
            // and tip equal, so the albedo is uniform along the blade.
            uint c565 = 16u | (63u << 5);
            var blade = new TestBlade
            {
                pos = new Vector3(0f, -0.9f, 0.5f),        // clip space: identity view-projection below
                normalOct = 32768u | (65535u << 16),        // GrassPackOct((0, 1, 0))
                seed = 0u,                                  // facing byte 0: blade spans +X, face toward the camera
                colors = c565 | (c565 << 16),
            };

            var blades = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<TestBlade>());
            var visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            var rt = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rt.Create();
            try
            {
                blades.SetData(new[] { blade });
                visible.SetData(new uint[] { 0u });

                var mpb = new MaterialPropertyBlock();
                mpb.SetBuffer("_Blades", blades);
                mpb.SetBuffer("_VisibleBlades", visible);
                mpb.SetInt("_LodOffset", 0);
                mpb.SetInt("_Segments", 1);
                mpb.SetVector("_BladeSize", new Vector4(1.8f, 1.5f, 0f, 0f));   // height, width, no variation
                mpb.SetVector("_WindDir", new Vector4(1f, 0f, 0f, 0f));         // no wind
                mpb.SetVector("_WindParams", new Vector4(0f, 1f, 0f, 0f));      // no flutter, no static lean
                mpb.SetVector("_BendPos", Vector4.zero);
                mpb.SetVector("_FadeParams", new Vector4(1e6f, 2e6f, 0f, 0f));
                mpb.SetVector("_PlanetCenter", Vector4.zero);

                var cmd = new CommandBuffer { name = "GrassColourSpaceTest" };
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true, true, Color.clear);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                // segments = 1: vertices 0, 1 are the root corners, 2 the tip.
                cmd.DrawProcedural(Matrix4x4.identity, mat, pass, MeshTopology.Triangles, 3, 1, mpb);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                var tex = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Color px = tex.GetPixel(4, 4);
                Object.DestroyImmediate(tex);

                Assert.That(px.g, Is.GreaterThan(0.5f), "the blade did not cover the sampled pixel (g = " + px.g + ")");
                float ratio = px.r / px.g;
                float expected = Mathf.GammaToLinearSpace(16f / 31f);   // 0.229
                Assert.That(ratio, Is.EqualTo(expected).Within(0.02f),
                    "Grass albedo red/green = " + ratio + " for sRGB 16/31 over 1.0; 0.229 means decoded once, 0.516 means read as linear");
            }
            finally
            {
                blades.Release();
                visible.Release();
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(mat);
            }
        }
    }
}
