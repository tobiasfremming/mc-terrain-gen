using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CollisionTilePlannerTests
    {
        static HashSet<Vector3Int> Plan(float r, Vector3 p, System.Func<Vector3Int, bool> covered = null)
        {
            var set = new HashSet<Vector3Int>();
            CollisionTilePlanner.Plan(new List<AnchorSphere> { new() { position = p, radius = r } }, 16f, covered ?? (_ => false), set);
            return set;
        }

        [Test]
        public void SmallRadiusInsideOneTileGivesOneTile()
        {
            var s = Plan(1f, new Vector3(8, 8, 8));
            Assert.That(s, Is.EquivalentTo(new[] { new Vector3Int(0, 0, 0) }));
        }

        [Test]
        public void RadiusCrossingFacesIncludesNeighboursButNotFarCorners()
        {
            var s = Plan(3f, new Vector3(1, 8, 8)); // 1 m from the x=0 face
            Assert.That(s, Does.Contain(new Vector3Int(-1, 0, 0)));
            Assert.That(s, Does.Contain(new Vector3Int(0, 0, 0)));
            Assert.That(s.Count, Is.EqualTo(2));
        }

        [Test]
        public void NegativeCoordinatesFloorCorrectly()
        {
            var s = Plan(0.5f, new Vector3(-0.25f, -15.9f, -16.1f));
            Assert.That(s, Is.EquivalentTo(new[] { new Vector3Int(-1, -1, -2) }));
        }

        [Test]
        public void ClipmapCoveredTilesAreExcluded()
        {
            var s = Plan(20f, new Vector3(8, 8, 8), c => c == new Vector3Int(0, 0, 0));
            // Does.Not.Contain(object) would be the natural spelling here, but the
            // nunit.framework.dll this Unity version ships resolves ConstraintExpression's
            // instance Contain(...) to a string-only overload, so `Does.Not.Contain`
            // (unlike the static `Does.Contain`) fails to compile for a non-string
            // argument. Same assertion, spelled to avoid that overload.
            Assert.That(s.Contains(new Vector3Int(0, 0, 0)), Is.False);
            Assert.That(s, Does.Contain(new Vector3Int(1, 0, 0)));
        }
    }
}
