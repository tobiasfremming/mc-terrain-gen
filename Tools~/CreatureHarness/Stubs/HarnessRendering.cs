// The GPU side of CreatureRenderer as no-ops (both modes; in RealUnity mode these shadow the engine types on
// purpose, CS0436, so no engine object with a native finalizer is ever created), so its CPU side
// (skin matrices, culling bounds) runs offline exactly as under `-nographics` (graphicsDeviceType Null).
using System;

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Null = 4, Direct3D11 = 2 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
}

namespace UnityEngine
{
    public static class SystemInfo { public static Rendering.GraphicsDeviceType graphicsDeviceType => Rendering.GraphicsDeviceType.Null; }
    public static class Application { public static bool isPlaying => false; public static bool isBatchMode => true; }
    public class Shader : Object
    {
        static readonly System.Collections.Generic.Dictionary<string, int> ids = new System.Collections.Generic.Dictionary<string, int>();
        public static int PropertyToID(string name) { lock (ids) { if (!ids.TryGetValue(name, out int id)) ids[name] = id = ids.Count + 1; return id; } }
    }
    public class Material : Object { public Shader shader; }
    public sealed class MaterialPropertyBlock
    {
        public void SetBuffer(int id, GraphicsBuffer b) { }
        public void SetInt(int id, int v) { }
        public void SetFloat(int id, float v) { }
        public void SetColor(int id, Color v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetMatrix(int id, Matrix4x4 v) { }
        public void SetVectorArray(int id, Vector4[] v) { }
    }
    // the renderer decodes its look-layer colours like SetColor would (a linear-space project)
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
    public static class QualitySettings { public static ColorSpace activeColorSpace => ColorSpace.Linear; }
    public sealed class GraphicsBuffer : IDisposable
    {
        public enum Target { Structured = 16 }
        public GraphicsBuffer(Target t, int count, int stride) => throw new NotSupportedException("harness: no GPU");
        public void SetData(Array a) { }
        public void SetData(Array a, int managedStart, int bufferStart, int count) { }
        public void Dispose() { }
    }
    public struct RenderParams
    {
        public RenderParams(Material m) { material = m; shadowCastingMode = Rendering.ShadowCastingMode.On; receiveShadows = true; matProps = null; worldBounds = default; }
        public Material material; public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows;
        public MaterialPropertyBlock matProps; public Bounds worldBounds;
    }
    public static class Graphics { public static void RenderMeshPrimitives(in RenderParams rp, Mesh mesh, int submesh, int count) { } }
}
