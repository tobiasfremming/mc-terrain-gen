// Creature kit: writes a kit-format species asset (kitVersion 1, [SerializeReference] parts + modules) as
// Unity 6000.2 serialises it (references version 2), so an agent can convert or author a species offline
// (`CreatureHarness kit-convert <Species> <NewName>`) and Unity imports it unchanged. Unity rewrites the file in
// its own layout on the next save (it adds the empty legacy fields); the data is the same.
// Floats are written round-trip ("R"), as Unity does; bools 0/1, enums as ints, Vector3/Color in flow style.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using CreatureKit;

public static class HarnessKitYaml
{
    public static string Emit(string name, float scale, string scriptGuid, IList<CreaturePart> parts, IList<CreatureModule> modules)
    {
        var w = new Writer();
        var sb = w.sb;
        sb.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n");
        sb.Append("  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n");
        sb.Append("  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n");
        sb.Append($"  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}\n");
        sb.Append($"  m_Name: {name}\n  m_EditorClassIdentifier: TerrainGen.Runtime::CreatureSpecies\n");
        sb.Append($"  scale: {F(scale)}\n  kitVersion: 1\n");
        sb.Append("  parts:\n");
        foreach (CreaturePart p in parts) sb.Append($"  - rid: {w.Rid(p)}\n");
        sb.Append("  modules:\n");
        foreach (CreatureModule m in modules) sb.Append($"  - rid: {w.Rid(m)}\n");
        sb.Append("  references:\n    version: 2\n    RefIds:\n");
        for (int i = 0; i < w.objects.Count; i++)   // nested references (Repeat.template) append as they are met
        {
            object o = w.objects[i];
            sb.Append($"    - rid: {1000 + i}\n      type: {{class: {o.GetType().Name}, ns: {o.GetType().Namespace}, asm: TerrainGen.Runtime}}\n      data:\n");
            w.Fields(o, 8);
        }
        return sb.ToString();
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    sealed class Writer
    {
        public readonly StringBuilder sb = new StringBuilder();
        public readonly List<object> objects = new List<object>();

        public long Rid(object o)
        {
            int i = objects.IndexOf(o);
            if (i < 0) { objects.Add(o); i = objects.Count - 1; }
            return 1000 + i;
        }

        public void Fields(object o, int ind)
        {
            foreach (FieldInfo f in KitTypes.SerializedFields(o.GetType()))
            {
                if (f.IsDefined(typeof(UnityEngine.SerializeReference), false))
                {
                    object r = f.GetValue(o);
                    sb.Append(' ', ind).Append(f.Name).Append(":\n");
                    sb.Append(' ', ind + 2).Append("rid: ").Append(r == null ? -2 : Rid(r)).Append('\n');
                    continue;
                }
                Value(f.Name, f.FieldType, f.GetValue(o), ind);
            }
        }

        static bool IsFlow(Type t) => t.IsValueType && !t.IsPrimitive && !t.IsEnum && t.Namespace == "UnityEngine";

        static string Scalar(object v)
        {
            switch (v)
            {
                case float f: return F(f);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case bool b: return b ? "1" : "0";
                case string s: return s;
                case Enum e: return Convert.ToInt64(e).ToString(CultureInfo.InvariantCulture);
                case IFormattable n: return n.ToString(null, CultureInfo.InvariantCulture);
            }
            throw new NotSupportedException(v.GetType().Name);
        }

        static string Flow(object v)
        {
            var parts = new List<string>();
            foreach (FieldInfo f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) parts.Add($"{f.Name}: {Scalar(f.GetValue(v))}");
            return "{" + string.Join(", ", parts) + "}";
        }

        static bool IsScalar(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string);

        void Value(string name, Type t, object v, int ind)
        {
            string pad = new string(' ', ind);
            if (IsScalar(t)) { sb.Append(pad).Append(name).Append(": ").Append(v == null ? "" : Scalar(v)).Append('\n'); return; }
            if (IsFlow(t)) { sb.Append(pad).Append(name).Append(": ").Append(Flow(v)).Append('\n'); return; }
            if (v == null && KitTypes.ElementType(t) != t) { sb.Append(pad).Append(name).Append(": []\n"); return; }
            if (v is IList list)
            {
                Type e = KitTypes.ElementType(t);
                if (list.Count == 0) { sb.Append(pad).Append(name).Append(": []\n"); return; }
                sb.Append(pad).Append(name).Append(":\n");
                foreach (object it in list)
                {
                    if (IsScalar(e)) { sb.Append(pad).Append("- ").Append(Scalar(it)).Append('\n'); continue; }
                    if (IsFlow(e)) { sb.Append(pad).Append("- ").Append(Flow(it)).Append('\n'); continue; }
                    int at = sb.Length;
                    Fields(it, ind + 2);
                    sb.Remove(at, ind + 2).Insert(at, pad + "- ");   // Unity: list items at the key's indent
                }
                return;
            }
            if (v == null) { sb.Append(pad).Append(name).Append(":\n"); return; }
            sb.Append(pad).Append(name).Append(":\n");
            Fields(v, ind + 2);
        }
    }
}
