using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class NavPatchPlannerTests
    {
        static List<NavAnchor> A(params (Vector3 p, float r)[] xs)
        {
            var l = new List<NavAnchor>();
            for (int i = 0; i < xs.Length; i++) l.Add(new NavAnchor { position = xs[i].p, radius = xs[i].r, primary = i == 0 });
            return l;
        }

        [Test]
        public void FlatIsOnePatchAtOriginWithIdentityRotation()
        {
            var p = new NavPatchPlanner();
            var r = p.Plan(A((new Vector3(100, 5, 40), 40), (new Vector3(300, 0, 40), 40)));
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r[0].origin, Is.EqualTo(Vector3.zero));
            Assert.That(r[0].rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(r[0].localBounds.Contains(new Vector3(100 - 39, 5, 40)), Is.True);
            Assert.That(r[0].localBounds.Contains(new Vector3(300 + 39, 0, 40)), Is.True);
        }

        [Test]
        public void FlatBoundsAreQuantisedSoSmallMovesDoNotChangeThem()
        {
            var p = new NavPatchPlanner();
            var b1 = p.Plan(A((new Vector3(100, 0, 0), 40)))[0].localBounds;
            var b2 = p.Plan(A((new Vector3(103, 0, 0), 40)))[0].localBounds;
            Assert.That(b2, Is.EqualTo(b1));
        }

        [Test]
        public void FlatExtentIsCappedAroundPrimary()
        {
            var p = new NavPatchPlanner { maxFlatExtent = 512 };
            var b = p.Plan(A((Vector3.zero, 40), (new Vector3(5000, 0, 0), 40)))[0].localBounds;
            Assert.That(b.size.x, Is.LessThanOrEqualTo(512f + 1e-3f));
            Assert.That(b.Contains(new Vector3(5000, 0, 0)), Is.False);
        }

        [Test]
        public void RadialPatchUpMatchesRadialDirection()
        {
            var p = new NavPatchPlanner { radial = true, planetCenter = Vector3.zero };
            Vector3 pos = new Vector3(0, 0, 1000);
            var r = p.Plan(A((pos, 40)));
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(Vector3.Dot(r[0].rotation * Vector3.up, Vector3.forward), Is.GreaterThan(0.999f));
            Vector3 local = Quaternion.Inverse(r[0].rotation) * (pos - r[0].origin);
            Assert.That(r[0].localBounds.Contains(local), Is.True);
        }

        [Test]
        public void RadialFarApartAnchorsGetSeparateNonOverlappingPatches()
        {
            var p = new NavPatchPlanner { radial = true };
            var r = p.Plan(A((new Vector3(0, 0, 1000), 40), (new Vector3(0, 1000, 0), 40)));
            Assert.That(r.Count, Is.EqualTo(2));
        }

        [Test]
        public void RadialCloseAnchorsShareOnePatch()
        {
            var p = new NavPatchPlanner { radial = true };
            var r = p.Plan(A((new Vector3(0, 0, 1000), 40), (new Vector3(30, 0, 1000), 40)));
            Assert.That(r.Count, Is.EqualTo(1));
        }

        [Test]
        public void RadialPatchKeepsIdUntilAnchorNearsEdge()
        {
            var p = new NavPatchPlanner { radial = true, patchSize = 256, edgeMargin = 32 };
            int id = p.Plan(A((new Vector3(0, 0, 1000), 40)))[0].id;
            Assert.That(p.Plan(A((new Vector3(20, 0, 1000), 40)))[0].id, Is.EqualTo(id));
            Assert.That(p.Plan(A((new Vector3(110, 0, 995), 40)))[0].id, Is.Not.EqualTo(id));
        }
    }
}
