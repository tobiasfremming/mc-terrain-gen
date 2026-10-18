// Creature kit: every part and module type, found by reflection (declaring the class registers it), and the
// documentation rule the catalogue depends on: every serialized field of a kit type carries [Param].
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CreatureKit
{
    public static class KitTypes
    {
        static Type[] parts, modules;

        // Concrete part / module types (namespace CreatureKit) of the runtime assembly, by catalogue name (ordinal).
        public static Type[] Parts => parts ?? (parts = Find(typeof(CreaturePart)));
        public static Type[] Modules => modules ?? (modules = Find(typeof(CreatureModule)));

        static Type[] Find(Type root)
        {
            var list = new List<Type>();
            foreach (Type t in root.Assembly.GetTypes())
                if (t.IsClass && !t.IsAbstract && root.IsAssignableFrom(t) && t.Namespace == "CreatureKit") list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(NameOf(a), NameOf(b)));
            return list.ToArray();
        }

        public static string NameOf(Type t)
        {
            var p = (PartAttribute)Attribute.GetCustomAttribute(t, typeof(PartAttribute), false);
            if (p != null) return p.Name;
            var m = (ModuleAttribute)Attribute.GetCustomAttribute(t, typeof(ModuleAttribute), false);
            return m != null ? m.Name : t.Name;
        }

        public static Type PartByName(string name) { foreach (Type t in Parts) if (NameOf(t) == name) return t; return null; }
        public static Type ModuleByName(string name) { foreach (Type t in Modules) if (NameOf(t) == name) return t; return null; }

        // Unity's serialized fields of a type, base class first, in declaration order.
        public static List<FieldInfo> SerializedFields(Type t)
        {
            var chain = new List<Type>();
            for (Type b = t; b != null && b != typeof(object); b = b.BaseType) chain.Insert(0, b);
            var list = new List<FieldInfo>();
            foreach (Type b in chain)
            {
                FieldInfo[] fs = b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Array.Sort(fs, (x, y) => x.MetadataToken.CompareTo(y.MetadataToken));
                foreach (FieldInfo f in fs)
                {
                    if (f.IsInitOnly || f.IsLiteral || f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    if (f.IsPublic || f.IsDefined(typeof(UnityEngine.SerializeField), false) || f.IsDefined(typeof(UnityEngine.SerializeReference), false)) list.Add(f);
                }
            }
            return list;
        }

        // A shallow copy of a runtime spec class that has no Clone of its own (WingSpec, FlightSpec): compose
        // and the adapter copy before they edit, so an asset's objects are never changed.
        static readonly MethodInfo memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        public static T Memberwise<T>(T o) where T : class => o == null ? null : (T)memberwise.Invoke(o, null);

        // T, T[] or List<T> -> T
        public static Type ElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];
            return t;
        }

        // Fields of a kit type (and of its Struct-unit fields' types) without [Param], and a missing [Part]/[Module].
        public static List<string> ParamProblems(Type t)
        {
            var problems = new List<string>();
            bool isPart = typeof(CreaturePart).IsAssignableFrom(t), isModule = typeof(CreatureModule).IsAssignableFrom(t);
            if (isPart && Attribute.GetCustomAttribute(t, typeof(PartAttribute), false) == null) problems.Add(t.Name + ": no [Part]");
            if (isModule && Attribute.GetCustomAttribute(t, typeof(ModuleAttribute), false) == null) problems.Add(t.Name + ": no [Module]");
            if ((isPart || isModule) && !t.IsDefined(typeof(SerializableAttribute), false)) problems.Add(t.Name + ": not [Serializable]");
            Walk(t, t.Name, problems, new HashSet<Type>());
            return problems;
        }

        static void Walk(Type t, string path, List<string> problems, HashSet<Type> seen)
        {
            if (!seen.Add(t)) return;
            foreach (FieldInfo f in SerializedFields(t))
            {
                var a = (ParamAttribute)Attribute.GetCustomAttribute(f, typeof(ParamAttribute), true);
                if (a == null) { problems.Add($"{path}.{f.Name}: no [Param]"); continue; }
                // a Struct field's own fields are documented too (Spec fields are runtime classes documented elsewhere)
                if (a.Unit == ParamUnit.Struct) Walk(ElementType(f.FieldType), path + "." + f.Name, problems, seen);
            }
        }
    }
}
