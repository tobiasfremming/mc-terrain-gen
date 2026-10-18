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

        // Regression: a naive midpoint-of-origins merge can recentre a patch
        // away from a member it already held near its edge, stranding that
        // anchor outside every returned patch. The kept patch here holds an
        // anchor at local x=+80; a second anchor on the opposite side forms
        // its own cluster close enough (in origin space) to attempt a merge.
        // The merge must only commit if it keeps every member inside with
        // edgeMargin headroom -- otherwise both patches must stay separate.
        [Test]
        public void RadialMergeNeverStrandsAMemberAnchor()
        {
            var p = new NavPatchPlanner { radial = true };
            p.Plan(A((new Vector3(0, 0, 1000), 40)));
            var anchors = A((new Vector3(80, 0, 1000), 40), (new Vector3(-200, 0, 1000), 40));
            var r = p.Plan(anchors);
            foreach (var a in anchors)
            {
                bool contained = false;
                foreach (var patch in r)
                {
                    Vector3 local = Quaternion.Inverse(patch.rotation) * (a.position - patch.origin);
                    if (patch.localBounds.Contains(local)) { contained = true; break; }
                }
                Assert.That(contained, Is.True);
            }
        }

        // Regression: the flat patch's vertical range must cover every
        // included anchor, not just the primary -- a grounded anchor far
        // above/below the primary (e.g. an NPC on a cliff) must still fit.
        [Test]
        public void FlatVerticalRangeCoversAllIncludedAnchorsNotJustPrimary()
        {
            var p = new NavPatchPlanner();
            var b = p.Plan(A((Vector3.zero, 40), (new Vector3(100, 400, 0), 40)))[0].localBounds;
            Assert.That(b.Contains(Vector3.zero), Is.True);
            Assert.That(b.Contains(new Vector3(100, 400, 0)), Is.True);
        }

        // Regression: a stale patch from the previous mode must not leak
        // into the output after radial/flat is flipped -- PlanRadial's
        // keep/merge logic only makes sense for patches PlanRadial produced.
        [Test]
        public void ModeFlipDoesNotLeakThePreviousModesPatch()
        {
            var p = new NavPatchPlanner();
            p.Plan(A((new Vector3(100, 0, 0), 40)));
            p.radial = true;
            Vector3 pos = new Vector3(0, 1000, 0);
            var r = p.Plan(A((pos, 40)));
            Vector3 radialDir = pos.normalized;
            foreach (var patch in r)
            {
                Assert.That(patch.id, Is.Not.EqualTo(1));
                Assert.That(Vector3.Dot(patch.rotation * Vector3.up, radialDir), Is.GreaterThan(0.999f));
            }
        }

        // Regression (minor): the primary anchor must always ground, even if
        // its own sphere pokes outside the flat cap square -- Task 11's guard
        // assumes the primary always has a patch.
        [Test]
        public void FlatPrimaryIsNeverDroppedByTheCap()
        {
            var p = new NavPatchPlanner { maxFlatExtent = 64 };
            var r = p.Plan(A((Vector3.zero, 40)));
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r[0].localBounds.Contains(Vector3.zero), Is.True);
        }
    }
}
