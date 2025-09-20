using UnityEngine;

public abstract class DensityField : ScriptableObject
{
    [Tooltip("The isovalue of the surface you want to extract. Keep 0 unless you need a shift.")]
    public float isoLevel = 0f;

    // Return *signed* density: negative = solid, positive = air.
    public abstract float Sample(Vector3 worldPos);

    // Convenience so MC can always march the zero level.
    public virtual float SampleMinusIso(Vector3 worldPos) => Sample(worldPos) - isoLevel;

    // Step used for gradient finite-difference (normals). Override if needed.
    public virtual float GradientStep(float cellSize) => 0.5f * cellSize;

}
