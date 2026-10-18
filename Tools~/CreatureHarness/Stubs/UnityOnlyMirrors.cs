// Pure statics of Unity-only sources (excluded in CreatureHarness.csproj) that compiled tests reach. Each is a
// verbatim copy of the real member; keep them in sync (the kit tests that use them also run in Unity, where
// the real class is compiled instead).
using UnityEngine;

// CreatureLab.cs (MonoBehaviour): the sizing statics.
public static class CreatureLab
{
    public static float EnclosureRadius(CreatureSpecies sp, float maxScale) => CreatureKit.LabHabitat.EnclosureRadius(sp, maxScale, kAviaryRadius);
    internal const float kAviaryRadius = 14f, kAviaryHeight = 10f;
    internal const float kEnclosureGap = LabLayout.kEnclosureGap;
    public static float MaxScale(CreatureSpecies sp) => MaxScale(sp, sp.scale);
    public static float MaxScale(CreatureSpecies sp, float scale) => scale * (1f + Mathf.Abs(sp.Compiled.Runtime.look.sizeJitter));
}

// ClimbingBrain.cs (needs CreatureSurfaces, excluded): the kit's BrainFactory and ClimbExcursion name the type.
// Offline it exists only so they compile and the brain-type checks hold; it never runs (Target throws).
public sealed class ClimbingBrain : ICreatureBrain
{
    public ClimbingBrain(EnclosureArea area, int seed, float climbChance = 0.35f) { }
    public Vector3 Target(CreatureSim sim, float dt) => throw new System.NotSupportedException("ClimbingBrain runs in Unity only (CreatureSurfaces)");
}

namespace TerrainGen.Tests
{
    // CreatureWorldTests.cs (needs CreatureWorld): its shared constant.
    public partial class CreatureWorldTests
    {
        internal const string kMonitorPath = "Assets/TerrainGen/Config/Creatures/DesertMonitor.asset";
    }
}
