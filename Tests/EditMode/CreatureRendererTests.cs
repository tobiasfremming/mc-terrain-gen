using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    public class CreatureRendererTests
    {
        const string kShader = "MarchingCubes/Creature";
        const string kMaterialPath = "Assets/TerrainGen/Config/Creatures/Creature.mat";

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

        CreatureRenderer NewRenderer(CreatureSpecies sp)
        {
            Shader sh = Shader.Find(kShader);
            var mat = Keep(sh != null ? new Material(sh) : new Material(Shader.Find("Hidden/InternalErrorShader")));
            var r = new CreatureRenderer(sp, mat);
            renderers.Add(r);
            return r;
        }

        static CreatureSim RestSim(CreatureSpecies sp, Vector3 at) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, at, Vector3.forward);

        [Test] public void Instance_StructIs16Bytes()
        {
            Assert.AreEqual(16, Marshal.SizeOf<CreatureInstance>());
        }

        [Test] public void Renderer_GrowsWithoutLosingSims()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var r = NewRenderer(sp);
            var first = new List<CreatureSim>();
            for (int i = 0; i < 3; i++)
            {
                var s = RestSim(sp, new Vector3(i * 2f, 0f, 0f));
                first.Add(s);
                r.Add(s, new CreatureInstance { seed = i, sizeScale = 1f });
            }
            Assert.IsTrue(r.Remove(first[1]));
            Assert.IsFalse(r.Remove(first[1]), "removing twice must report false");
            for (int i = 0; i < 40; i++)
                r.Add(RestSim(sp, new Vector3(i * 2f, 0f, 5f)), new CreatureInstance { seed = 10 + i, sizeScale = 1f });
            Assert.AreEqual(42, r.Count);
            Assert.IsTrue(r.Remove(first[0]), "a sim added before the growth must still be there");
            Assert.IsTrue(r.Remove(first[2]), "a sim added before the growth must still be there");
            Assert.AreEqual(40, r.Count);
            Assert.DoesNotThrow(() => r.Render());
        }

        [Test] public void Renderer_SkinMatricesAreIdentityAtRest()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var r = NewRenderer(sp);
            r.Add(RestSim(sp, Vector3.zero), new CreatureInstance { seed = 1f, sizeScale = 1f });
            r.Add(RestSim(sp, Vector3.zero), new CreatureInstance { seed = 2f, sizeScale = 1f });
            r.Render();
            Matrix4x4[] skin = r.SkinForTest;
            int count = r.Count * sp.BoneCount;
            Assert.GreaterOrEqual(skin.Length, count);
            for (int i = 0; i < count; i++)
                for (int k = 0; k < 16; k++)
                {
                    float want = k % 5 == 0 ? 1f : 0f; // diagonal of a column-major 4x4
                    Assert.AreEqual(want, skin[i][k], 1e-4f, $"skin[{i}] element {k}");
                }
        }

        [Test] public void Renderer_SkinMovesWithTheSim()
        {
            // The same rest pose shifted by d: every skin matrix is a pure translation by d.
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var r = NewRenderer(sp);
            var d = new Vector3(3f, 0f, -2f);
            r.Add(RestSim(sp, d), new CreatureInstance { sizeScale = 1f });
            r.Render();
            Matrix4x4[] skin = r.SkinForTest;
            for (int b = 0; b < sp.BoneCount; b++)
            {
                Vector3 p = skin[b].MultiplyPoint3x4(new Vector3(0.1f, 0.2f, 0.3f));
                Assert.Less((p - (new Vector3(0.1f, 0.2f, 0.3f) + d)).magnitude, 1e-4f, $"bone {b}");
            }
        }

        [Test] public void CreatureShader_CompilesWithoutErrors()
        {
            Shader sh = Shader.Find(kShader);
            Assert.IsNotNull(sh, kShader + " not found");
            // A fresh import (re)starts the shader compiler with the project's package include paths;
            // without it CompileVariant can fail to resolve "Packages/..." when nothing was imported
            // this session.
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(sh), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            sh = Shader.Find(kShader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(sh), "ShaderHasError");

            // Compile every pass for D3D11 with the keyword sets the renderer actually hits (no graphics
            // device needed, so this also bites in -nographics batch runs).
            string[][] forward =
            {
                new string[0],
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "FOG_EXP2", "_SCREEN_SPACE_OCCLUSION" },
                new[] { "_MAIN_LIGHT_SHADOWS_SCREEN", "FOG_LINEAR" },
                new[] { "_MAIN_LIGHT_SHADOWS" },
            };
            var passes = new Dictionary<string, string[][]>
            {
                { "Forward", forward },
                { "ShadowCaster", new[] { new string[0], new[] { "_CASTING_PUNCTUAL_LIGHT_SHADOW" } } },
                { "DepthOnly", new[] { new string[0] } },
                { "DepthNormals", new[] { new string[0], new[] { "_GBUFFER_NORMALS_OCT" } } },
            };
            ShaderData data = ShaderUtil.GetShaderData(sh);
            ShaderData.Subshader sub = data.GetSerializedSubshader(0);
            var seen = new HashSet<string>();
            var errors = new List<string>();
            for (int p = 0; p < sub.PassCount; p++)
            {
                ShaderData.Pass pass = sub.GetPass(p);
                if (!passes.TryGetValue(pass.Name, out string[][] sets)) continue;
                seen.Add(pass.Name);
                foreach (string[] kw in sets)
                    foreach (ShaderType st in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        ShaderData.VariantCompileInfo info = pass.CompileVariant(st, kw, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                        if (info.Success) continue;
                        foreach (ShaderMessage m in info.Messages)
                            if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                                errors.Add($"{pass.Name} {st} [{string.Join(" ", kw)}]: {m.message} ({m.file}:{m.line})");
                        if (info.Messages.Length == 0) errors.Add($"{pass.Name} {st} [{string.Join(" ", kw)}]: failed without messages");
                    }
            }
            foreach (string name in passes.Keys) Assert.IsTrue(seen.Contains(name), "missing pass " + name);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        // The path the Editor takes when it draws: URP renders a camera, sets its global keywords
        // (main-light shadows, soft shadows, SSAO, fog) and the Editor compiles the variant on demand
        // through Library/ShaderCache. ShaderData.CompileVariant above bypasses all of that and
        // passed while the Editor drew the Forward pass with the error shader (fix round 1).
        // Material.SetPass or a CommandBuffer draw do not trigger that compile; Camera.Render does.
        [Test] public void CreatureShader_EditorRuntimeCompile_NoErrors()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("needs a graphics device (interactive Editor); batch -nographics compiles no variants on demand");
            Shader sh = Shader.Find(kShader);
            Assert.IsNotNull(sh, kShader + " not found");
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(sh), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            sh = Shader.Find(kShader);

            var errors = new List<string>();
            void OnLog(string msg, string stack, LogType type)
            {
                if (msg.Contains("Shader error in '" + kShader + "'")) errors.Add(msg);
            }
            bool async = ShaderUtil.allowAsyncCompilation;
            var root = Keep(new GameObject("CreatureShaderProbe") { hideFlags = HideFlags.HideAndDontSave });
            var rt = new RenderTexture(64, 64, 24);
            Application.logMessageReceived += OnLog;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var camGo = new GameObject("cam") { hideFlags = HideFlags.HideAndDontSave };
                camGo.transform.SetParent(root.transform);
                camGo.transform.position = new Vector3(0f, 1f, -3f);
                camGo.transform.LookAt(Vector3.zero);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.targetTexture = rt;
                var lightGo = new GameObject("sun") { hideFlags = HideFlags.HideAndDontSave };
                lightGo.transform.SetParent(root.transform);
                lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.hideFlags = HideFlags.HideAndDontSave;
                cube.transform.SetParent(root.transform);
                cube.GetComponent<MeshRenderer>().sharedMaterial = Keep(new Material(sh));
                cam.Render();
                cam.targetTexture = null;
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                ShaderUtil.allowAsyncCompilation = async;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsFalse(ShaderUtil.ShaderHasError(sh), "ShaderHasError after the on-demand compile");
        }

        [Test] public void CreatureMaterial_UsesCreatureShader()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kMaterialPath);
            Assert.IsNotNull(mat, kMaterialPath);
            Assert.IsNotNull(mat.shader);
            Assert.AreEqual(kShader, mat.shader.name);
        }

        // Ledger (Task 7): ~1 in 4 positioned captures missed the creature. The draw's culling bounds must hold
        // every skinned vertex, for the shipped monitor at its scale, walking a tight turn.
        [Test] public void Renderer_Bounds_ContainEverySkinnedVertex()
        {
            var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            Assert.IsNotNull(sp);
            var r = NewRenderer(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, new Vector3(3f, 0f, -2f), Vector3.forward, sp.scale * 1.08f);
            r.Add(sim, new CreatureInstance { sizeScale = sim.SizeScale });
            Vector3[] v = r.Mesh.vertices;
            var w = new List<Vector4>(); r.Mesh.GetUVs(2, w);
            int outside = 0; float worst = 0f;
            for (int t = 0; t < 240; t++)
            {
                float a = t / 60f * 2f;
                sim.Tick(1f / 60f, new Vector3(3f, 0f, -2f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * sp.scale);
                if (t % 20 != 19) continue;
                r.Render();
                Bounds b = r.LastBounds;
                Matrix4x4[] skin = r.SkinForTest;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector4 s = w[i];
                    Vector3 p = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                    if (!b.Contains(p)) { outside++; worst = Mathf.Max(worst, Mathf.Sqrt(b.SqrDistance(p))); }
                }
            }
            Assert.AreEqual(0, outside, $"vertices outside the culling bounds (worst {worst:F3} m)");
        }
    }
}
