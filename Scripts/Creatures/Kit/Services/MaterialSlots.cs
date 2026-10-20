// Creature kit service (2026-10-20, kit services): material slots per species. The shader's look-layer table has
// PartLayers.MaxParts rows and the global part-id registry filled it; a NEW part kind no longer takes a global id.
//   - A kind with a legacy registry id (body 0, limb 1, ..., teeth 13, tongue 14, ray-skin 15) keeps that slot in
//     every species: shipped meshes and tables are unchanged by construction.
//   - A kind without one (PartLayers.NamedKinds; a part type's [Part(MaterialKinds = ...)]; or a species' own kind,
//     named by a Layers row) gets a slot of this species: the lowest kit slot (LegacyIds .. MaxParts - 1) the species
//     does not already use. Used = the core ids 0..9, every legacy id a part declares ([Part(MaterialLayers)]), every
//     int field of a part named like "layer" (ArmourPlates.layer, Jaw.toothLayer, ...), and the Layers rows' part ids.
// The part emits with PartMeshEmitter.Slot(kind) (the vertex's uv1.x); PartLayers.Rows puts the kind's row in that
// slot, then the species' Layers rows (by part id or by kind). Built once per species (cached), allocation then.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CreatureKit
{
    // A part whose kinds depend on its data (beyond its [Part(MaterialKinds)]).
    public interface IMaterialKinds { void Kinds(List<string> into); }

    public sealed class MaterialSlots
    {
        public readonly string[] KindOfSlot = new string[PartLayers.MaxParts];   // a named kind's slot; null = legacy row
        public readonly bool[] Used = new bool[PartLayers.MaxParts];              // slots this species' mesh may use
        public readonly string Problem;                                           // the table is full: kinds left over
        readonly Dictionary<string, int> slot = new Dictionary<string, int>();

        static readonly ConditionalWeakTable<CreatureSpecies, MaterialSlots> cache = new ConditionalWeakTable<CreatureSpecies, MaterialSlots>();

        public static MaterialSlots For(CreatureSpecies sp)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            return cache.GetValue(rt, s => new MaterialSlots(s));
        }

        // Forget a species' slots (its parts or Layers changed: the Inspector).
        public static void Invalidate(CreatureSpecies sp) { if (sp != null) cache.Remove(sp.Compiled.Runtime); }

        // The slot of a kind in this species (-1: none - not a registry kind and not named by the species).
        public int SlotOf(string kind) => kind != null && slot.TryGetValue(kind, out int s) ? s : -1;

        MaterialSlots(CreatureSpecies sp)
        {
            for (int i = 0; i < PartLayers.LegacyIds && i < Used.Length; i++) Used[i] = true;
            var wanted = new List<string>();
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            if (parts != null)
                foreach (CreaturePart p in parts)
                {
                    if (p == null) continue;
                    PartAttribute a = p.Meta;
                    if (a != null)
                    {
                        foreach (int id in a.MaterialLayers) Use(id);
                        foreach (string k in a.MaterialKinds) wanted.Add(k);
                    }
                    foreach (FieldInfo f in p.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (f.FieldType == typeof(int) && f.Name.IndexOf("layer", StringComparison.OrdinalIgnoreCase) >= 0) Use((int)f.GetValue(p));
                    if (p is IMaterialKinds mk) mk.Kinds(wanted);
                }
            IReadOnlyList<CreatureModule> modules = KitView.Modules(sp);
            if (modules != null)
                foreach (CreatureModule m in modules)
                    if (m is Layers l && l.rows != null)
                        foreach (LayerDef d in l.rows)
                        {
                            if (string.IsNullOrEmpty(d.kind)) Use(d.partId);
                            else wanted.Add(d.kind);
                        }
            // legacy kinds first (their own slot), then the named ones in order of appearance
            var left = new List<string>();
            foreach (string k in wanted)
            {
                if (string.IsNullOrEmpty(k) || slot.ContainsKey(k)) continue;
                if (PartLayers.Find(k, out PartLayers.Entry e) && e.Id >= 0) { slot[k] = e.Id; Use(e.Id); }
            }
            foreach (string k in wanted)
            {
                if (string.IsNullOrEmpty(k) || slot.ContainsKey(k)) continue;
                int free = -1;
                for (int i = PartLayers.LegacyIds; i < PartLayers.MaxParts; i++) if (!Used[i]) { free = i; break; }
                if (free < 0) { left.Add(k); continue; }
                slot[k] = free; Used[free] = true; KindOfSlot[free] = k;
            }
            // the registry's legacy kinds answer SlotOf too (a part may emit with a legacy kind's name)
            foreach (PartLayers.Entry e in PartLayers.Registry) if (!slot.ContainsKey(e.Kind)) slot[e.Kind] = e.Id;
            if (left.Count > 0) Problem = $"material table full: no slot left for {string.Join(", ", left)} (slots {PartLayers.LegacyIds}..{PartLayers.MaxParts - 1} in use)";
        }

        void Use(int id) { if (id >= 0 && id < Used.Length) Used[id] = true; }
    }
}
