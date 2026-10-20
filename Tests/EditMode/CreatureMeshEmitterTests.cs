using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit T3.1 (design §2.3, §3e): the species mesh is emitted part by part (CreaturePart.EmitMesh through
    // PartMeshEmitter). Every existing species must come out byte for byte as the frozen pre-kit builders
    // (CreatureMeshReference.cs) built it: positions, normals, the three uv channels, triangles and the bind poses.
    // Both builds run in the same runtime, so the check holds in Unity and offline alike.
    [TestFixtureSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
    public class CreatureMeshEmitterTests
    {
        readonly string name;
        public CreatureMeshEmitterTests(string species) { name = species; }

        [Test] public void Mesh_Bytes_EqualLegacy()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            Assert.IsNotNull(sp, name);
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) Assert.Ignore("cephalopod: OctopusMeshBuilder (octopus internals stay until its roles plan lands)");
            string changed = MeshBytes.ChangedSinceReference(sp);
            if (changed != null) Assert.Ignore(changed);
            // a kit-only BODY (RayBody: ISpineChainBody) draws its own trunk: the legacy builder's spine tube is not a prefix of it
            if (sp.Compiled != null && sp.Compiled.Body is CreatureKit.ISpineChainBody) Assert.Ignore("kit-only body " + sp.Compiled.Body + ": no legacy mesh to compare");
            // a kit-only part (MeshOrder 100) appends after every legacy vertex: the legacy mesh is then a byte prefix
            bool prefix = CreatureKitAssets.KitOnlyReason(sp) != null;
            string diff = MeshBytes.Compare(MeshBytes.Legacy(sp), MeshBytes.Current(sp), prefix);
            Assert.IsNull(diff, name + ": the emitted mesh differs from the legacy builder" + (prefix ? " (prefix)" : "") + ": " + diff);
        }

        [Test] public void BindPoses_Bytes_EqualLegacy()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) Assert.Ignore("cephalopod: OctopusRestPose");
            string changed = MeshBytes.ChangedSinceReference(sp);
            if (changed != null) Assert.Ignore(changed);
            string diff = MeshBytes.CompareMatrices(LegacyRestPose.BoneWorld(sp), CreatureRestPose.BoneWorld(sp));
            Assert.IsNull(diff, name + ": rest bone worlds differ: " + diff);
        }
    }

    // Code-built species (test factories) and the explicit-wings overload.
    public class CreatureMeshEmitterFactoryTests
    {
        static void Same(CreatureSpecies sp)
        {
            string changed = MeshBytes.ChangedSinceReference(sp);
            if (changed != null) { Object.DestroyImmediate(sp); Assert.Ignore(changed); }
            try { Assert.IsNull(MeshBytes.Compare(MeshBytes.Legacy(sp), MeshBytes.Current(sp)), sp.name); }
            finally { Object.DestroyImmediate(sp); }
        }

        [Test] public void Mesh_Bytes_EqualLegacy_Lizardish() => Same(CreatureTestSpecies.Lizardish());
        [Test] public void Mesh_Bytes_EqualLegacy_Snake() => Same(CreatureTestSpecies.Snake());
        [Test] public void Mesh_Bytes_EqualLegacy_Hexapod() => Same(CreatureTestSpecies.Hexapod());

        [Test] public void Mesh_Bytes_EqualLegacy_SerpentFactoryWings()
        {
            WingedSerpentTestSpecies.Serpent s = WingedSerpentTestSpecies.Create();
            // the wing features added after the reference (MeshBytes.ChangedSinceReference) off: the rest of the
            // winged path must still be the reference's byte for byte
            foreach (WingSpec ws in s.wings) { ws.groundTallStance = false; ws.flankPatches = 0; }
            s.species.InvalidateCompiled();
            string changed = MeshBytes.ChangedSinceReference(s.species);
            if (changed != null) { Object.DestroyImmediate(s.species); Assert.Ignore(changed); }
            try
            {
                var a = MeshBytes.Of(LegacyWingedMeshBuilder.Build(s.species, s.wings));
                var b = MeshBytes.Of(WingedMeshBuilder.Build(s.species, s.wings));
                Assert.IsNull(MeshBytes.Compare(a, b));
                Assert.IsNull(MeshBytes.CompareMatrices(LegacyWingedMeshBuilder.BindPoses(s.species, s.wings), WingedMeshBuilder.BindPoses(s.species, s.wings)));
            }
            finally { Object.DestroyImmediate(s.species); }
        }
    }

    // RED: the byte check bites. The same parts emitted in another order (the emitter's test hook) are not the
    // legacy mesh; neither is a mesh with one vertex nudged by one ulp.
    public class CreatureMeshEmitterRedTests
    {
        [Test] public void Mesh_EmitOrderReversed_IsCaught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("DesertSnake");   // legless: still the frozen reference's mesh
            Assert.IsNull(MeshBytes.ChangedSinceReference(sp));
            MeshBytes legacy = MeshBytes.Legacy(sp), reversed;
            CreatureKit.PartMeshEmitter.ReverseOrderForTest = true;
            try { reversed = MeshBytes.Current(sp); }
            finally { CreatureKit.PartMeshEmitter.ReverseOrderForTest = false; }
            string diff = MeshBytes.Compare(legacy, reversed);
            TestContext.WriteLine("reversed emit order: " + diff);
            Assert.IsNotNull(diff, "a different emit order must be reported");
            Assert.IsNull(MeshBytes.Compare(legacy, MeshBytes.Current(sp)), "and the shipped order is equal again");
        }

        [Test] public void Mesh_OneUlp_IsCaught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Tarantula");
            MeshBytes a = MeshBytes.Legacy(sp), b = MeshBytes.Current(sp);
            int i = b.pos.Length / 2;
            b.pos[i].y = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(b.pos[i].y) + 1);
            Assert.IsNotNull(MeshBytes.Compare(a, b));
        }
    }

    // A mesh as plain arrays, compared bit for bit (float bits, not ==: -0 and NaN count).
    internal sealed class MeshBytes
    {
        public Vector3[] pos, nrm;
        public List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
        public List<Vector4> uv2 = new List<Vector4>();
        public int[] tris;
        public string indexFormat;

        public static MeshBytes Of(Mesh m)
        {
            var b = new MeshBytes { pos = m.vertices, nrm = m.normals, tris = m.triangles, indexFormat = m.indexFormat.ToString() };
            m.GetUVs(0, b.uv0); m.GetUVs(1, b.uv1); m.GetUVs(2, b.uv2);
            Object.DestroyImmediate(m);
            return b;
        }

        // Why a species' mesh differs from the frozen reference on purpose (null: it must not). The wing mesh changed
        // after the kit move (feat/wyvern-trees 2026-10-19, user requests): a tall stance's straight-spine bind
        // (WingSpec.groundTallStance) and the membrane's flank edge split into patches along the body
        // (WingSpec.flankPatches, more bones per wing). A species using them is compared with the current builder
        // where a test needs a mesh (Reference); the kit move stays pinned by every other species (SkyDragon's
        // wings, the factory serpent with the features off) and by the RED tests.
        public static string ChangedSinceReference(CreatureSpecies sp)
        {
            CreatureSpecies r = sp.Compiled.Runtime;
            // The legs changed on purpose too (feat/leg-look 2026-10-19, "basically all legs look weird"): the
            // two-bone limb's haunch, muscle profile, raised ankle and lofted foot with toes and claws, the arthropod
            // segments' spindle and neck. Every legged species is compared with the current builder; the kit move
            // stays pinned by the legless species and by the RED tests.
            if (r.legs != null && r.legs.Length > 0) return sp.name + ": leg look after the frozen reference (2026-10-19 Leg2Bone / ArthropodLeg mesh)";
            if (r.wings == null) return null;
            foreach (WingSpec ws in r.wings)
                if (ws != null && (ws.groundTallStance || WingBoneLayout.FlankPatchCount(ws) > 1))
                    return sp.name + ": wing features after the frozen reference (groundTallStance / flankPatches)";
            return null;
        }

        // The mesh a species must come out as: the frozen reference, or the current builder for a species that
        // changed on purpose (ChangedSinceReference).
        public static MeshBytes Reference(CreatureSpecies sp) => ChangedSinceReference(sp) != null ? Current(sp) : Legacy(sp);
        public static Matrix4x4[] ReferenceBoneWorld(CreatureSpecies sp) => ChangedSinceReference(sp) != null ? CreatureRestPose.BoneWorld(sp) : LegacyRestPose.BoneWorld(sp);

        public static MeshBytes Legacy(CreatureSpecies sp)
        {
            CreatureSpecies r = sp.Compiled.Runtime;
            return Of(r.WingCount > 0 ? LegacyWingedMeshBuilder.Build(r) : LegacyCreatureMeshBuilder.Build(r));
        }

        // What the renderer draws today.
        public static MeshBytes Current(CreatureSpecies sp)
        {
            CreatureSpecies r = sp.Compiled.Runtime;
            return Of(r.WingCount > 0 ? WingedMeshBuilder.Build(r) : CreatureMeshBuilder.Build(r));
        }

        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        // prefix: b may carry more vertices and indices after a's (kit-only parts append, MeshOrder 100), its extra
        // indices only into its extra vertices; everything a has must be b's, bit for bit
        public static string Compare(MeshBytes a, MeshBytes b, bool prefix = false)
        {
            if (a.pos.Length == 0) return "the reference mesh is empty";
            if (prefix)
            {
                if (b.pos.Length < a.pos.Length || b.tris.Length < a.tris.Length) return $"prefix: {b.pos.Length} vertices / {b.tris.Length} indices, fewer than {a.pos.Length} / {a.tris.Length}";
                for (int i = a.tris.Length; i < b.tris.Length; i++)
                    if (b.tris[i] < a.pos.Length) return $"prefix: appended index {i} reaches back into the legacy vertices ({b.tris[i]})";
            }
            else
            {
                if (a.indexFormat != b.indexFormat) return $"index format {a.indexFormat} vs {b.indexFormat}";
                if (a.pos.Length != b.pos.Length) return $"vertex count {a.pos.Length} vs {b.pos.Length}";
                if (a.tris.Length != b.tris.Length) return $"index count {a.tris.Length} vs {b.tris.Length}";
            }
            for (int i = 0; i < a.pos.Length; i++)
            {
                if (!Same(a.pos[i], b.pos[i])) return $"position {i}: {a.pos[i]:F6} vs {b.pos[i]:F6}";
                if (!Same(a.nrm[i], b.nrm[i])) return $"normal {i}: {a.nrm[i]:F6} vs {b.nrm[i]:F6}";
                if (Bits(a.uv0[i].x) != Bits(b.uv0[i].x) || Bits(a.uv0[i].y) != Bits(b.uv0[i].y)) return $"uv0 {i}: {a.uv0[i]} vs {b.uv0[i]}";
                if (Bits(a.uv1[i].x) != Bits(b.uv1[i].x) || Bits(a.uv1[i].y) != Bits(b.uv1[i].y)) return $"uv1 {i}: {a.uv1[i]} vs {b.uv1[i]}";
                Vector4 p = a.uv2[i], q = b.uv2[i];
                if (Bits(p.x) != Bits(q.x) || Bits(p.y) != Bits(q.y) || Bits(p.z) != Bits(q.z) || Bits(p.w) != Bits(q.w)) return $"uv2 {i}: {p} vs {q}";
            }
            for (int i = 0; i < a.tris.Length; i++) if (a.tris[i] != b.tris[i]) return $"index {i}: {a.tris[i]} vs {b.tris[i]}";
            return null;
        }

        static bool Same(Vector3 a, Vector3 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z);

        public static string CompareMatrices(Matrix4x4[] a, Matrix4x4[] b)
        {
            if (a.Length != b.Length) return $"bone count {a.Length} vs {b.Length}";
            for (int i = 0; i < a.Length; i++)
                for (int k = 0; k < 16; k++)
                    if (Bits(a[i][k]) != Bits(b[i][k])) return $"bone {i} element {k}: {a[i][k]} vs {b[i][k]}";
            return null;
        }
    }
}
