using NUnit.Framework;

namespace TerrainGen.Tests
{
    public class RigidbodyGuardLogicTests
    {
        [Test] public void InsideSolidAlwaysRescues() =>
            Assert.That(TerrainRigidbodyGuard.Decide(colliderBelow: true, fieldGroundBelow: true, insideSolid: true, wasHeld: false), Is.EqualTo(GuardState.Rescued));
        [Test] public void FieldGroundButNoColliderHolds() =>
            Assert.That(TerrainRigidbodyGuard.Decide(colliderBelow: false, fieldGroundBelow: true, insideSolid: false, wasHeld: false), Is.EqualTo(GuardState.HeldNoGround));
        [Test] public void ColliderBelowFrees() =>
            Assert.That(TerrainRigidbodyGuard.Decide(colliderBelow: true, fieldGroundBelow: true, insideSolid: false, wasHeld: true), Is.EqualTo(GuardState.Free));
        // Nothing below in the field either: the body is simply falling (e.g.
        // dropped from > probeDistance), and a held one must be let go.
        [Test] public void NoColliderAndNoFieldGroundFrees() =>
            Assert.That(TerrainRigidbodyGuard.Decide(colliderBelow: false, fieldGroundBelow: false, insideSolid: false, wasHeld: true), Is.EqualTo(GuardState.Free));
    }
}
