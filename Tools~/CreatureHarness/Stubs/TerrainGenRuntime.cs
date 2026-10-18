// TerrainGen.Runtime types outside Scripts/Creatures that the creature sources and tests reference
// (both modes: these are project types, not engine types). Only the members the creature code uses.
public abstract class DensityField : UnityEngine.ScriptableObject
{
    public abstract float Sample(UnityEngine.Vector3 p, float fw);
    public float Sample(UnityEngine.Vector3 p) => Sample(p, 0f);
    public virtual bool TryGetHeightBounds(out float minH, out float maxH) { minH = maxH = 0f; return false; }
    protected virtual void OnValidate() { }
    public virtual UnityEngine.Vector3 Gradient(UnityEngine.Vector3 p, float eps, float fw)
    {
        float dx = Sample(p + new UnityEngine.Vector3(eps, 0, 0), fw) - Sample(p - new UnityEngine.Vector3(eps, 0, 0), fw);
        float dy = Sample(p + new UnityEngine.Vector3(0, eps, 0), fw) - Sample(p - new UnityEngine.Vector3(0, eps, 0), fw);
        float dz = Sample(p + new UnityEngine.Vector3(0, 0, eps), fw) - Sample(p - new UnityEngine.Vector3(0, 0, eps), fw);
        return new UnityEngine.Vector3(dx, dy, dz) / (2f * eps);
    }
}
