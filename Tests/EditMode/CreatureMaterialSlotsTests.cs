using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Kit services (2026-10-20): material slots per species (Kit/Services/MaterialSlots.cs). Legacy kinds keep their
    // registry id in every species (shipped meshes and tables identical); a kind with no global id gets a free kit slot
    // of the species that names it.
    public class CreatureMaterialSlotsTests
    {
        // A test-only part kind with no global id: a small patch on joint 0 drawn with the species' slot of "test glow".
        [Serializable]
        [Part("TestGlowPatch", PartKind.Rigid, Bones = "0", Summary = "test fixture", MaterialKinds = new[] { "test glow" })]
        public sealed class TestGlowPatch : CreaturePart
        {
            public override int BoneCount(in SpeciesCtx c) => 0;
            public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
                => m.Ellipsoid(m.Bones[0], new Vector3(0f, 0.02f, 0f), 0.01f, 0.01f, 0.01f, 8, 4, m.Slot("test glow"), 0);
        }

        // The table before slots: the registry's rows, then the Layers rows by part id.
        static PartLayer[] OldRows(CreatureSpecies sp)
        {
            var rows = new PartLayer[PartLayers.MaxParts];
            for (int i = 0; i < rows.Length; i++) rows[i] = PartLayers.LegacyRow(i);
            foreach (CreatureModule m in KitView.Modules(sp.Compiled.Runtime))
                if (m is Layers l && l.rows != null)
                    foreach (LayerDef d in l.rows) if (d.partId >= 0 && d.partId < rows.Length) rows[d.partId] = d.ToRow();
            return rows;
        }

        static bool Same(PartLayer a, PartLayer b) => a.Equals(b) && a.tint.a == b.tint.a;

        // Species built on the parts with named kinds (the Apex redesign's FlowBody, CrestBeakHead, PitRow, Dewlap,
        // FleshWing): their named kinds take kit slots by design; every other check below holds for them too, every slot
        // not holding a named kind keeps its legacy row, and every named slot has its kind's row (NamedSlotRowProblem).
        static readonly HashSet<string> kNamedKindSpecies = new HashSet<string> { "Apex" };

        // Every shipped species: no named kind (but kNamedKindSpecies), the table exactly as before (but the named kinds'
        // slots), and every part id its mesh emits is a slot the species is known to use (the scan behind the free-slot
        // choice is complete).
        [Test] public void Shipped_SlotsAreTheLegacyIds_TablesUnchanged()
        {
            foreach (string name in CreatureKitAssets.Species())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(name);
                MaterialSlots s = MaterialSlots.For(sp);
                Assert.IsNull(s.Problem, name);
                bool named = kNamedKindSpecies.Contains(name);
                for (int i = 0; i < PartLayers.MaxParts; i++) if (!named) Assert.IsNull(s.KindOfSlot[i], $"{name}: slot {i} holds a named kind");
                foreach (PartLayers.Entry e in PartLayers.Registry) Assert.AreEqual(e.Id, s.SlotOf(e.Kind), $"{name}: {e.Kind}");
                PartLayer[] now = PartLayers.Rows(sp), old = OldRows(sp);
                for (int i = 0; i < now.Length; i++) if (s.KindOfSlot[i] == null) Assert.IsTrue(Same(now[i], old[i]), $"{name}: row {i} {now[i]} vs {old[i]}");
                string named2 = NamedSlotRowProblem(sp, now);
                Assert.IsNull(named2, named2);
                if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) continue;   // the octopus mesh and shader: ids 4-7 (core)
                Mesh mesh = CreatureKitAssets.BuildMesh(sp);
                var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
                Object.DestroyImmediate(mesh);
                var ids = new HashSet<int>();
                foreach (Vector2 u in uv1) ids.Add(Mathf.RoundToInt(u.x));
                foreach (int id in ids) Assert.IsTrue(id >= 0 && id < PartLayers.MaxParts && s.Used[id], $"{name}: its mesh emits part id {id}, which the slot scan does not count as used");
            }
        }

        // Every slot holding a named kind has that kind's row: the species' Layers row naming the kind if it has one,
        // else the kind's PartLayers.NamedKinds default (a kind with neither keeps the slot's legacy row).
        internal static string NamedSlotRowProblem(CreatureSpecies sp, PartLayer[] rows)
        {
            MaterialSlots s = MaterialSlots.For(sp);
            for (int i = 0; i < PartLayers.MaxParts; i++)
            {
                string kind = s.KindOfSlot[i];
                if (kind == null) continue;
                PartLayer want = PartLayers.Find(kind, out PartLayers.Entry e) ? e.Row : PartLayers.LegacyRow(i);
                foreach (CreatureModule m in KitView.Modules(sp.Compiled.Runtime))
                    if (m is Layers l && l.rows != null)
                        foreach (LayerDef d in l.rows) if (d.kind == kind) want = d.ToRow();
                if (!Same(rows[i], want)) return $"{sp.name}: slot {i} ({kind}) is {rows[i]}, not its kind's row {want}";
            }
            return null;
        }

        // RED: a wrong row in a named slot (the Apex's wingfan slot given the body's row) is caught.
        [Test] public void Red_NamedSlot_WrongRow_IsCaught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Apex");
            PartLayer[] rows = PartLayers.Rows(sp);
            Assert.IsNull(NamedSlotRowProblem(sp, rows));
            int slot = MaterialSlots.For(sp).SlotOf("wingfan");
            Assert.GreaterOrEqual(slot, PartLayers.LegacyIds);
            rows[slot] = PartLayers.LegacyRow(0);
            string e = NamedSlotRowProblem(sp, rows);
            TestContext.Out.WriteLine("RED: " + e);
            StringAssert.Contains("wingfan", e);
        }

        static CreatureSpecies WithGlow(string kind, params LayerDef[] rows)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("KitMonitor");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "KitMonitor-glow"; sp.scale = a.scale; sp.kitVersion = 1;
            sp.parts = new List<CreaturePart>(a.parts) { new TestGlowPatch { id = "glow" } };
            sp.modules = new List<CreatureModule>(a.modules) { new Layers { rows = rows } };
            return sp;
        }

        // A new kind gets the lowest kit slot the species does not use; its vertices carry it; its Layers row (by kind)
        // styles that slot; the rest of the mesh and table are the base species'.
        [Test] public void NamedKind_GetsAFreeSlot_MeshAndTableUseIt()
        {
            var row = new LayerDef { kind = "test glow", model = ShadingModel.Membrane, noPattern = true, noGrain = true, tintSource = TintSource.Own, tint = new Color(0.2f, 0.9f, 0.9f, 1f), tintAmount = 0.8f };
            CreatureSpecies sp = WithGlow("test glow", row);
            Assert.IsNull(sp.Validate());
            CreatureSpecies rt = sp.Compiled.Runtime, baseSp = CreatureKitAssets.Load("KitMonitor");
            MaterialSlots s = MaterialSlots.For(rt);
            int slot = s.SlotOf("test glow");
            TestContext.Out.WriteLine($"test glow -> slot {slot}");
            Assert.GreaterOrEqual(slot, PartLayers.LegacyIds);
            Assert.IsFalse(MaterialSlots.For(baseSp).Used[slot], "a slot the base species does not use");
            Assert.AreEqual("test glow", s.KindOfSlot[slot]);
            Assert.IsTrue(Same(PartLayers.Rows(rt)[slot], row.ToRow()), "the Layers row by kind styles the slot");
            PartLayer[] b = PartLayers.Rows(baseSp), g = PartLayers.Rows(rt);
            for (int i = 0; i < g.Length; i++) if (i != slot) Assert.IsTrue(Same(b[i], g[i]), $"row {i} unchanged");

            Mesh m0 = CreatureKitAssets.BuildMesh(baseSp), m1 = CreatureKitAssets.BuildMesh(rt);
            var u0 = new List<Vector2>(); m0.GetUVs(1, u0);
            var u1 = new List<Vector2>(); m1.GetUVs(1, u1);
            Object.DestroyImmediate(m0); Object.DestroyImmediate(m1);
            Assert.Greater(u1.Count, u0.Count, "the patch adds vertices");
            for (int i = 0; i < u0.Count; i++) Assert.AreEqual(u0[i].x, u1[i].x, $"vertex {i}: the base mesh's ids unchanged");
            for (int i = u0.Count; i < u1.Count; i++) Assert.AreEqual(slot, Mathf.RoundToInt(u1[i].x), $"patch vertex {i} carries its slot");
        }

        // RED: a species that names more new kinds than free kit slots is reported, not silently overlapped.
        [Test] public void Red_TooManyKinds_TableFull()
        {
            var rows = new List<LayerDef>();
            for (int k = 0; k <= PartLayers.MaxParts; k++) rows.Add(new LayerDef { kind = "kind" + k });
            CreatureSpecies sp = WithGlow("test glow", rows.ToArray());
            string p = MaterialSlots.For(sp.Compiled.Runtime).Problem;
            TestContext.Out.WriteLine("RED: " + p);
            Assert.IsNotNull(p);
        }

        // The part's own declaration ([Part(MaterialKinds)]) is enough for a slot (no Layers row: the kind's default
        // row, here none in the registry -> the legacy row of that slot); a kind nobody names has none.
        [Test] public void DeclaredKind_HasASlot_UnknownHasNone()
        {
            MaterialSlots s = MaterialSlots.For(WithGlow("test glow").Compiled.Runtime);
            Assert.GreaterOrEqual(s.SlotOf("test glow"), PartLayers.LegacyIds);
            Assert.AreEqual(-1, s.SlotOf("never named"));
            Assert.AreEqual(13, s.SlotOf("teeth"), "a registry kind answers its own id");
        }
    }
}
