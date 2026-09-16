using NUnit.Framework;

namespace TerrainGen.Tests
{
    public class RigidbodyGuardLogicTests
    {
        [Test] public void InsideSolidAlwaysRescues() =>
            Assert.That(TerrainRigidbodyGuard.Decide(colliderBelow: true, insideSolid: true, wasHeld: false), Is.EqualTo(GuardState.Rescued));
        [Test] public void NoColliderBelowHolds() =>
            Assert.That(TerrainRigidbodyGuard.Decide(false, false, false), Is.EqualTo(GuardState.HeldNoGround));
        [Test] public void ColliderBelowFrees() =>
            Assert.That(TerrainRigidbodyGuard.Decide(true, false, true), Is.EqualTo(GuardState.Free));
    }
}
