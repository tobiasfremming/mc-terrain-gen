using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Plant vertex colours are the profile's Inspector colours, authored in
    // sRGB and written to the mesh as raw bytes. In a Linear project the
    // shaders must decode them once before lighting; reading them as linear
    // makes every plant about twice as bright (the lavender-white Baobab).
    //
    // Both shaders are checked on the GPU through their self-lit path (vertex
    // alpha 0), where the output is the albedo itself: a quad of vertex colour
    // sRGB 0.5 is drawn into an sRGB render target and read back. Decoded once,
    // linear 0.214 re-encodes to byte 128; read as linear, 0.5 encodes to 188.
    public class PlantColourSpaceTests
    {
        const byte kExpected = 128;   // sRGB 0.5 -> linear 0.214 -> sRGB target -> 0.5
        const int kTolerance = 3;

        static byte RenderGrey(Material mat, int pass)
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { new Vector3(-1, -1, 0.5f), new Vector3(1, -1, 0.5f), new Vector3(1, 1, 0.5f), new Vector3(-1, 1, 0.5f) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            var c = new Color32(128, 128, 128, 0);            // sRGB 0.5, alpha 0 = fully self-lit
            mesh.SetColors(new[] { c, c, c, c });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);

            var rt = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32);   // sRGB in a Linear project
            rt.Create();
            string[] fog = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
            bool[] fogWas = new bool[fog.Length];
            for (int i = 0; i < fog.Length; i++) { fogWas[i] = Shader.IsKeywordEnabled(fog[i]); Shader.DisableKeyword(fog[i]); }
            try
            {
                var cmd = new CommandBuffer { name = "PlantColourSpaceTest" };
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true, true, Color.black);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Color32 px = tex.GetPixels32()[4 * 8 + 4];
                Object.DestroyImmediate(tex);
                return px.g;
            }
            finally
            {
                for (int i = 0; i < fog.Length; i++) if (fogWas[i]) Shader.EnableKeyword(fog[i]);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(mesh);
            }
        }

        static Material NewMaterial(string shaderName)
        {
            Shader sh = Shader.Find(shaderName);
            Assert.IsNotNull(sh, shaderName + " not found");
            return new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }

        [Test]
        public void PlantShader_DecodesSrgbVertexColour()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("only meaningful in Linear colour space");
            Material mat = NewMaterial("MarchingCubes/Plant");
            try
            {
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_EmissionStrength", 1f);
                int pass = mat.FindPass("Forward");
                Assert.GreaterOrEqual(pass, 0, "Forward pass");
                byte g = RenderGrey(mat, pass);
                Assert.That(Mathf.Abs(g - kExpected), Is.LessThanOrEqualTo(kTolerance),
                    "Plant Forward wrote " + g + " for sRGB 0.5; 128 means decoded once, 188 means read as linear");
            }
            finally { Object.DestroyImmediate(mat); }
        }

        [Test]
        public void ImpostorBakeShader_DecodesSrgbVertexColour()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("only meaningful in Linear colour space");
            Material mat = NewMaterial("Hidden/MarchingCubes/Plant Impostor Bake");
            try
            {
                mat.SetColor("_Color", Color.white);
                byte g = RenderGrey(mat, 0);
                Assert.That(Mathf.Abs(g - kExpected), Is.LessThanOrEqualTo(kTolerance),
                    "Impostor bake wrote " + g + " for sRGB 0.5; 128 means decoded once, 188 means read as linear");
            }
            finally { Object.DestroyImmediate(mat); }
        }
    }
}
