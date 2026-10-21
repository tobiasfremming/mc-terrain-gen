using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // RestMeshCache (Apex redesign, task 10): FlowBody, PitRow, Dewlap, CrestBeakHead and FleshWing record the rest mesh they emit and
    // replay it on the next build of the same data (the Apex's mesh build fell from ~10 to ~2 ms offline; CreatureKitTests 4
    // allows 5). Invariants: a replay is the uncached mesh bit for bit (every vertex attribute and index), the cache is hit,
    // and an edit to any part's data builds afresh (the keys follow the data). The RED collapses every key into one: an
    // edited pit row replays the old mesh.
    public class CreatureRestMeshCacheTests
    {
        static PitRow Pits(float radius = 0.012f) => new PitRow
        {
            id = "spiracles", mirror = true, at = new PartSocket(0, 0, Vector3.zero), fromU = 0.18f, toU = 0.4f, count = 5, radius = radius,
        };

        // the CrestBeakHead fixture (steep neck, head at joint 0) with a dewlap and a pit row: every caching part
        static CreatureSpecies Species(float pitRadius = 0.012f, float flapDepth = 0.24f)
        {
            Dewlap d = CreatureDewlapTests.Flap(); d.depthAt[2] = flapDepth;
            var parts = new List<CreaturePart> { CreatureCrestBeakHeadTests.Neck(), CreatureFlowBodyTests.HindLeg(), CreatureKitComposeTests.TwoEyes(), CreatureCrestBeakHeadTests.Head(), d, Pits(pitRadius) };
            return FlowKit.Build("RestMeshCacheFixture", 1f, parts, new CreatureModule[] { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } });
        }

        sealed class Bytes
        {
            public Vector3[] V, N; public List<Vector2> U0 = new List<Vector2>(), U1 = new List<Vector2>(); public List<Vector4> U2 = new List<Vector4>(); public int[] T;
        }

        static Bytes Read(CreatureSpecies sp)
        {
            Mesh m = FlowKit.Mesh(sp);
            var b = new Bytes { V = m.vertices, N = m.normals, T = m.triangles };
            m.GetUVs(0, b.U0); m.GetUVs(1, b.U1); m.GetUVs(2, b.U2);
            Object.DestroyImmediate(m);
            return b;
        }

        static string Diff(Bytes a, Bytes b)
        {
            if (a.V.Length != b.V.Length) return $"{a.V.Length} vs {b.V.Length} vertices";
            if (a.T.Length != b.T.Length) return $"{a.T.Length} vs {b.T.Length} indices";
            for (int i = 0; i < a.V.Length; i++)
                if (!a.V[i].Equals(b.V[i]) || !a.N[i].Equals(b.N[i]) || !a.U0[i].Equals(b.U0[i]) || !a.U1[i].Equals(b.U1[i]) || !a.U2[i].Equals(b.U2[i])) return $"vertex {i} differs";
            for (int i = 0; i < a.T.Length; i++) if (a.T[i] != b.T[i]) return $"index {i} differs";
            return null;
        }

        static Bytes Uncached(CreatureSpecies sp)
        {
            RestMeshCache.OffForTest = true;
            try { return Read(sp); }
            finally { RestMeshCache.OffForTest = false; }
        }

        [Test] public void Replay_IsTheUncachedMesh_BitForBit()
        {
            RestMeshCache.ClearForTest();
            CreatureSpecies sp = Species();
            Bytes truth = Uncached(sp);
            Bytes first = Read(sp);                       // builds and records
            int hits = RestMeshCache.Hits;
            Bytes again = Read(sp);                       // replays
            Assert.IsNull(Diff(truth, first), "recorded build");
            Assert.IsNull(Diff(truth, again), "replayed build");
            Assert.AreEqual(5, RestMeshCache.Hits - hits, "the body, the head, the dewlap and both pit rows replayed");
        }

        [Test] public void EditedData_BuildsAfresh()
        {
            RestMeshCache.ClearForTest();
            Read(Species());                              // the cache holds the plain species
            string e = EditProblem();
            Assert.IsNull(e, e);
        }

        static string EditProblem()
        {
            foreach (var edited in new[] { Species(pitRadius: 0.016f), Species(flapDepth: 0.3f) })
            {
                string d = Diff(Uncached(edited), Read(edited));
                if (d != null) return $"{edited.name}: an edited part replayed a stale mesh ({d})";
            }
            FlowBody body = CreatureCrestBeakHeadTests.Neck(); body.profile[2].hump = 0.03f;
            var parts = new List<CreaturePart> { body, CreatureFlowBodyTests.HindLeg(), CreatureKitComposeTests.TwoEyes(), CreatureCrestBeakHeadTests.Head(), CreatureDewlapTests.Flap(), Pits() };
            CreatureSpecies sp = FlowKit.Build("RestMeshCacheHump", 1f, parts, new CreatureModule[] { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } });
            string db = Diff(Uncached(sp), Read(sp));
            return db != null ? $"edited body: stale mesh ({db})" : null;
        }

        // The wings too (CreatureFleshWingTests' flyer: a FlowBody and two FleshWings), and an edit of the wing, of its arm
        // or of the wing's shared flight spec builds afresh.
        [Test] public void FleshWing_Replay_IsTheUncachedMesh_EditsBuildAfresh()
        {
            RestMeshCache.ClearForTest();
            CreatureSpecies sp = CreatureFleshWingTests.Species();
            Bytes truth = Uncached(sp);
            Read(sp);
            int hits = RestMeshCache.Hits;
            Assert.IsNull(Diff(truth, Read(sp)), "replayed build");
            Assert.AreEqual(3, RestMeshCache.Hits - hits, "the body and both wings replayed");
            foreach (CreatureSpecies edited in new[]
            {
                CreatureFleshWingTests.Species(w => w.rootThickness = 0.09f, name: "EditedWing"),
                CreatureFleshWingTests.Species(arm: a => a.lowerLength = 0.52f, name: "EditedArm"),
                CreatureFleshWingTests.Species(w => w.spec.fingerSpreadDeg = new[] { 18f, 34f, 50f, 66f, 82f }, name: "EditedSpec"),
            })
                Assert.IsNull(Diff(Uncached(edited), Read(edited)), edited.name + ": stale mesh");
        }

        // RED: one key for everything - the edited pit row replays the plain one's mesh (or another part's)
        [Test] public void Red_OneKey_StaleMesh()
        {
            RestMeshCache.ClearForTest();
            RestMeshCache.OneKeyForTest = true;
            try
            {
                Read(Species());
                string e = EditProblem();
                TestContext.Out.WriteLine("RED: " + e);
                Assert.IsNotNull(e);
            }
            finally { RestMeshCache.OneKeyForTest = false; RestMeshCache.ClearForTest(); }
        }
    }
}
