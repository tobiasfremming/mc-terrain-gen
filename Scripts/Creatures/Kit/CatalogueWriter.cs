// Creature kit T2.6 (design §3a): the catalogue an agent reads before making an animal - every part and module
// type (from their [Part]/[Module]/[Param] attributes), which species use each, every parameter with its unit,
// size rule, meaning, range and example, the species to copy, and the material-layer (part id) registry.
// Generated, never edited: Config/Creatures/CATALOGUE.md (for agents) and catalogue.json (for tools). The
// Editor menu (Editor/CreatureKitCatalogue.cs) and the offline harness (`CreatureHarness catalogue --write`)
// both call Build; Catalogue_IsFresh fails when the checked-in files are stale. Output is deterministic
// (ordinal sorting, no dates, fixed number formats), byte-identical from Unity and from the harness.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CreatureKit
{
    public static class CatalogueWriter
    {
        public const string MarkdownPath = "Assets/TerrainGen/Config/Creatures/CATALOGUE.md";
        public const string JsonPath = "Assets/TerrainGen/Config/Creatures/catalogue.json";
        // the part-id registry (mesh part ids = material layers, creature-module rule 9): Kit/PartLayers.cs (T4.1)
        public static int NextFreeMaterialLayer => PartLayers.NextFree;

        static string LayerCell(PartLayers.Entry e)
        {
            if (e.Octopus) return "octopus shader family (CreatureOctopus.shader)";
            PartLayer r = e.Row;
            var bits = new List<string>();
            foreach (LayerFlags f in Enum.GetValues(typeof(LayerFlags))) if (f != LayerFlags.None && (r.flags & f) != 0) bits.Add(f.ToString());
            string tint = r.tintAmount > 0f ? $", tint {r.tintSource} {r.tintAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}" : "";
            return $"{r.model} ({string.Join(", ", bits)}{tint})";
        }

        public sealed class Param
        {
            public string name, type, unit, size, meaning, range, example;
        }

        public static List<Param> Params(Type t) { var rows = new List<Param>(); Collect(t, "", rows, 0); return rows; }

        static void Collect(Type t, string prefix, List<Param> rows, int depth)
        {
            foreach (FieldInfo f in KitTypes.SerializedFields(t))
            {
                var a = (ParamAttribute)Attribute.GetCustomAttribute(f, typeof(ParamAttribute), true);
                if (a == null) continue;   // KitAttributes_EveryPublicFieldHasParam keeps this from happening
                string meaning = a.Meaning;
                if (a.Unit == ParamUnit.Spec) meaning += " - fields: " + string.Join(", ", FieldNames(f.FieldType));
                string example = a.Default.Length > 0 ? "default " + a.Default + (a.Example.Length > 0 ? "; " + a.Example : "") : a.Example;
                rows.Add(new Param
                {
                    name = prefix + f.Name, type = TypeName(f.FieldType), unit = a.Unit.ToString(), size = UnitRules.Rule(a.Unit),
                    meaning = meaning, range = a.Range, example = example,
                });
                if (a.Unit == ParamUnit.Struct && depth < 3 && !typeof(CreaturePart).IsAssignableFrom(KitTypes.ElementType(f.FieldType)))
                {
                    Type e = KitTypes.ElementType(f.FieldType);
                    Collect(e, prefix + f.Name + (e != f.FieldType ? "[]." : "."), rows, depth + 1);
                }
            }
        }

        static List<string> FieldNames(Type t)
        {
            var n = new List<string>();
            foreach (FieldInfo f in KitTypes.SerializedFields(t)) n.Add(f.Name);
            return n;
        }

        static string TypeName(Type t)
        {
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return TypeName(t.GetGenericArguments()[0]) + "[]";
            if (t.IsEnum) return string.Join(" / ", Enum.GetNames(t));
            if (t == typeof(float)) return "float";
            if (t == typeof(int)) return "int";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            return t.Name;
        }

        // ------------------------------------------------------------ build

        public static void Build(IList<CreatureSpecies> species, out string markdown, out string json)
        {
            var usedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var compiled = new List<KeyValuePair<CreatureSpecies, CompiledSpecies>>();
            foreach (CreatureSpecies sp in species)
            {
                CompiledSpecies c = sp.Compiled;
                compiled.Add(new KeyValuePair<CreatureSpecies, CompiledSpecies>(sp, c));
                // the authored parts too (Repeat is used even though compose expands it)
                if (sp.kitVersion > 0) foreach (CreaturePart p in sp.parts) if (p != null) Use(usedBy, p.Meta.Name, sp.name);
                foreach (CreaturePart p in c.Parts) Use(usedBy, p.Meta.Name, sp.name);
                foreach (CreatureModule m in c.Modules) Use(usedBy, m.Meta.Name, sp.name);
            }

            var md = new StringBuilder();
            md.Append("# Creature kit catalogue\n\n");
            md.Append("GENERATED - do not edit. Regenerate: Unity menu *Marching Cubes > Creature Kit > Regenerate Catalogue*, or offline\n");
            md.Append("`dotnet Tools~/CreatureHarness/bin/Release/net8.0/CreatureHarness.dll catalogue --write` (the `creature-kit` skill).\n");
            md.Append("`Catalogue_IsFresh` fails when this file is stale.\n\n");
            md.Append("Read this first. **Reuse before you invent**: every row says which species already use it. To make an animal, copy the\n");
            md.Append("nearest species below (`CreatureHarness kit-convert <Legacy> <New>` writes a legacy one in the kit format) and edit\n");
            md.Append("its parts and modules; add a new part or module type only when nothing here fits.\n\n");
            md.Append("Units: lengths in metres at scale 1. `size` is how a value follows the individual's sizeScale s (Froude): lengths x s,\n");
            md.Append("times and speeds x sqrt(s), rates / sqrt(s); the rest is size-free. Authoring sugar, expanded at load: `mirror` on any\n");
            md.Append("part (<id>.L then <id>.R), `Repeat` (rows of parts), `SpineBody.profile` (keys instead of every joint), `Walk.pattern`\n");
            md.Append("(gait groups of limbs left at -1).\n\n");

            md.Append("## Part types\n\n| part | kind | status | bones | material layers | use when | used by |\n|---|---|---|---|---|---|---|\n");
            var partTypes = new List<Type>(KitTypes.Parts);
            partTypes.Sort((x, y) => { int k = CreaturePart.MetaOf(x).Kind.CompareTo(CreaturePart.MetaOf(y).Kind); return k != 0 ? k : string.CompareOrdinal(KitTypes.NameOf(x), KitTypes.NameOf(y)); });
            foreach (Type t in partTypes)
            {
                PartAttribute a = CreaturePart.MetaOf(t);
                md.Append($"| **{a.Name}** | {a.Kind} | {Status(a.Status, a.Branch)} | {Cell(a.Bones)} | {Join(a.MaterialLayers)} | {Cell(a.UseWhen)} | {UsedBy(usedBy, a.Name)} |\n");
            }

            md.Append("\n## Modules\n\n| module | category | status | needs | used by | what |\n|---|---|---|---|---|---|\n");
            var moduleTypes = new List<Type>(KitTypes.Modules);
            moduleTypes.Sort((x, y) => { int k = string.CompareOrdinal(CreatureModule.MetaOf(x).Category, CreatureModule.MetaOf(y).Category); return k != 0 ? k : string.CompareOrdinal(KitTypes.NameOf(x), KitTypes.NameOf(y)); });
            foreach (Type t in moduleTypes)
            {
                ModuleAttribute a = CreatureModule.MetaOf(t);
                md.Append($"| **{a.Name}** | {a.Category} | {Status(a.Status, a.Branch)} | {Cell(a.Needs)} | {UsedBy(usedBy, a.Name)} | {Cell(a.Summary)} |\n");
            }

            md.Append("\n## Species (examples to copy)\n\n| species | format | plan | parts | modules | bones |\n|---|---|---|---|---|---|\n");
            foreach (var kv in compiled)
            {
                CompiledSpecies c = kv.Value;
                md.Append($"| {kv.Key.name} | {(kv.Key.kitVersion > 0 ? "kit" : "legacy")} | {(c.Soft ? "Soft" : "Spine")} | {Cell(PartSummary(c))} | {Cell(ModuleSummary(c))} | {(c.Layout != null ? c.Layout.BoneCount.ToString() : "-")} |\n");
            }

            md.Append("\n## Parameters\n");
            var all = new List<Type>(partTypes); all.AddRange(moduleTypes);
            foreach (Type t in all)
            {
                bool part = typeof(CreaturePart).IsAssignableFrom(t);
                string name = KitTypes.NameOf(t);
                string summary = part ? CreaturePart.MetaOf(t).Summary : CreatureModule.MetaOf(t).Summary;
                md.Append($"\n### {name}\n\n{summary}");
                if (part) md.Append($" System: {CreaturePart.MetaOf(t).System}.");
                List<Param> rows = Params(t);
                if (rows.Count == 0) { md.Append("\n\nNo parameters.\n"); continue; }
                md.Append("\n\n| field | type | unit | size | meaning | range | example |\n|---|---|---|---|---|---|---|\n");
                foreach (Param r in rows) md.Append($"| `{r.name}` | {Cell(r.type)} | {r.unit} | {r.size} | {Cell(r.meaning)} | {r.range} | {Cell(r.example)} |\n");
            }

            md.Append($"\n## Material layers (mesh part ids; next free: {NextFreeMaterialLayer})\n\n");
            md.Append("Creature.shader shades a part id by its row of the species' look-layer table (Kit/PartLayers.cs): a shading model ");
            md.Append("(" + string.Join(", ", Enum.GetNames(typeof(ShadingModel))) + ") plus layers (pattern, scale grain, sheen, shimmer) and a tint. ");
            md.Append("A new part takes the next free id and adds its row to PartLayers.Registry - no shader edit; a species restyles rows with the Layers module. ");
            md.Append("Never reuse an id. A new shading MODEL is a shader edit: add it to CreatureLayers.hlsl + ShadingModel, with a CPU proof and the Unity pixel guard (CreaturePartLayerTests, CreatureShaderLayerTests).\n\n");
            md.Append("| id | use | row (model, layers, tint) |\n|---|---|---|\n");
            foreach (PartLayers.Entry e in PartLayers.Registry) md.Append($"| {e.Id} | {Cell(e.Use)} | {Cell(LayerCell(e))} |\n");
            markdown = md.ToString();

            // ------------------------------------------------------------ json
            var j = new Json();
            j.Open('{');
            j.Key("version").Num(1);
            j.Key("parts").Open('[');
            foreach (Type t in partTypes)
            {
                PartAttribute a = CreaturePart.MetaOf(t);
                j.Open('{');
                j.Key("name").Str(a.Name).Key("kind").Str(a.Kind.ToString()).Key("status").Str(a.Status).Key("bones").Str(a.Bones);
                j.Key("materialLayers").Open('['); foreach (int l in a.MaterialLayers) j.Num(l); j.Close(']');
                j.Key("system").Str(a.System).Key("useWhen").Str(a.UseWhen).Key("summary").Str(a.Summary);
                j.Key("usedBy").Strs(usedBy.TryGetValue(a.Name, out var u) ? u : new SortedSet<string>());
                ParamsJson(j, t);
                j.Close('}');
            }
            j.Close(']');
            j.Key("modules").Open('[');
            foreach (Type t in moduleTypes)
            {
                ModuleAttribute a = CreatureModule.MetaOf(t);
                j.Open('{');
                j.Key("name").Str(a.Name).Key("category").Str(a.Category).Key("status").Str(a.Status).Key("needs").Str(a.Needs).Key("summary").Str(a.Summary);
                j.Key("usedBy").Strs(usedBy.TryGetValue(a.Name, out var u) ? u : new SortedSet<string>());
                ParamsJson(j, t);
                j.Close('}');
            }
            j.Close(']');
            j.Key("species").Open('[');
            foreach (var kv in compiled)
            {
                CompiledSpecies c = kv.Value;
                j.Open('{');
                j.Key("name").Str(kv.Key.name).Key("kitVersion").Num(kv.Key.kitVersion).Key("plan").Str(c.Soft ? "Soft" : "Spine");
                j.Key("bones").Num(c.Layout != null ? c.Layout.BoneCount : -1);
                var pn = new List<string>(); foreach (CreaturePart p in c.Parts) pn.Add(p.Meta.Name + ":" + p.id);
                var mn = new List<string>(); foreach (CreatureModule m in c.Modules) mn.Add(m.Meta.Name);
                j.Key("parts").Strs(pn).Key("modules").Strs(mn);
                j.Close('}');
            }
            j.Close(']');
            j.Key("materialLayers").Open('[');
            foreach (PartLayers.Entry e in PartLayers.Registry)
            {
                j.Open('{'); j.Key("id").Num(e.Id).Key("use").Str(e.Use).Key("model").Str(e.Octopus ? "Octopus" : e.Row.model.ToString()).Key("layers").Str(LayerCell(e)); j.Close('}');
            }
            j.Close(']');
            j.Key("nextFreeMaterialLayer").Num(NextFreeMaterialLayer);
            j.Close('}');
            json = j.ToString() + "\n";
        }

        static void ParamsJson(Json j, Type t)
        {
            j.Key("params").Open('[');
            foreach (Param r in Params(t))
            {
                j.Open('{');
                j.Key("name").Str(r.name).Key("type").Str(r.type).Key("unit").Str(r.unit).Key("size").Str(r.size)
                 .Key("meaning").Str(r.meaning).Key("range").Str(r.range).Key("example").Str(r.example);
                j.Close('}');
            }
            j.Close(']');
        }

        static string PartSummary(CompiledSpecies c)
        {
            var order = new List<string>(); var count = new Dictionary<string, int>();
            foreach (CreaturePart p in c.Parts)
            {
                string n = p.Meta.Name;
                if (!count.ContainsKey(n)) { order.Add(n); count[n] = 0; }
                count[n]++;
            }
            var s = new List<string>();
            foreach (string n in order) s.Add(count[n] > 1 ? count[n] + "x " + n : n);
            return string.Join(", ", s);
        }

        static string ModuleSummary(CompiledSpecies c)
        {
            var s = new List<string>();
            foreach (CreatureModule m in c.Modules)
                s.Add(m is Walk w && w.pattern != GaitPattern.Explicit ? "Walk (" + w.pattern + ")" : m.Meta.Name);
            return string.Join(", ", s);
        }

        static void Use(Dictionary<string, SortedSet<string>> d, string k, string v) { if (!d.TryGetValue(k, out var s)) d[k] = s = new SortedSet<string>(StringComparer.Ordinal); s.Add(v); }
        static string UsedBy(Dictionary<string, SortedSet<string>> d, string k) => d.TryGetValue(k, out var s) ? string.Join(", ", s) : "-";
        static string Status(string status, string branch) => branch.Length > 0 ? status + " (" + branch + ")" : status;
        static string Join(int[] a) { var s = new List<string>(); foreach (int i in a) s.Add(i.ToString()); return string.Join(", ", s); }
        static string Cell(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");

        // a minimal deterministic JSON writer (Unity has no System.Text.Json; JsonUtility cannot write a dictionary)
        sealed class Json
        {
            readonly StringBuilder sb = new StringBuilder();
            readonly List<bool> first = new List<bool>();
            int Depth => first.Count;

            void Item()
            {
                if (Depth == 0) return;
                if (pendingKey) { pendingKey = false; return; }
                if (!first[Depth - 1]) sb.Append(',');
                first[Depth - 1] = false;
                sb.Append('\n').Append(' ', 2 * Depth);
            }
            bool pendingKey;

            public Json Open(char c) { Item(); sb.Append(c); first.Add(true); return this; }
            public Json Close(char c)
            {
                bool empty = first[Depth - 1];
                first.RemoveAt(Depth - 1);
                if (!empty) sb.Append('\n').Append(' ', 2 * Depth);
                sb.Append(c);
                return this;
            }
            public Json Key(string k) { Item(); Quote(k); sb.Append(": "); pendingKey = true; return this; }
            public Json Str(string s) { Item(); Quote(s ?? ""); return this; }
            public Json Num(int n) { Item(); sb.Append(n); return this; }
            public Json Strs(IEnumerable<string> list) { Open('['); foreach (string s in list) Str(s); return Close(']'); }

            void Quote(string s)
            {
                sb.Append('"');
                foreach (char ch in s)
                {
                    switch (ch)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                            else sb.Append(ch);
                            break;
                    }
                }
                sb.Append('"');
            }

            public override string ToString() => sb.ToString();
        }
    }
}
