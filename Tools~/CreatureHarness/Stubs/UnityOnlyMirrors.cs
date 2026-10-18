// Pure statics of Unity-only sources (excluded in CreatureHarness.csproj) that compiled tests reach. Each is a
// verbatim copy of the real member; keep them in sync (the kit tests that use them also run in Unity, where
// the real class is compiled instead).
using UnityEngine;

// CreatureLab.cs (MonoBehaviour): the sizing statics.
public static class CreatureLab
{
    public static float EnclosureRadius(CreatureSpecies sp, float maxScale)
        => sp != null && sp.CanFly ? kAviaryRadius * Mathf.Max(1f, maxScale) : Mathf.Max(5f, 2f * sp.BodyLength * maxScale);
    internal const float kAviaryRadius = 14f, kAviaryHeight = 10f;
    public static float MaxScale(CreatureSpecies sp) => MaxScale(sp, sp.scale);
    public static float MaxScale(CreatureSpecies sp, float scale) => scale * (1f + Mathf.Abs(sp.look.sizeJitter));
}

namespace TerrainGen.Tests
{
    // CreatureWorldTests.cs (needs CreatureWorld): its shared constant.
    public partial class CreatureWorldTests
    {
        internal const string kMonitorPath = "Assets/TerrainGen/Config/Creatures/DesertMonitor.asset";
    }
}
