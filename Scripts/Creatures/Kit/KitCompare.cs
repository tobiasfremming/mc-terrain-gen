// Creature kit: deep equality of serialized data by Unity's rules (fields, nested [Serializable] classes and
// structs, arrays; floats compared bit for bit; a null array equals an empty one, as Unity reads them). The
// adapter folds L/R pairs with it, the tests compare a round-tripped species with the asset.
using System;
using System.Collections;
using System.Reflection;

namespace CreatureKit
{
    public static class KitCompare
    {
        // `ignore(path)` true skips a field ("legs[2].pole"); null = compare everything.
        public static string FirstDifference(object a, object b, Func<string, bool> ignore = null, string path = "")
        {
            if (ignore != null && path.Length > 0 && ignore(path)) return null;
            if (a is IList || b is IList || (a == null && b == null)) return Lists(a as IList, b as IList, ignore, path);
            if (a == null || b == null) return $"{path}: {(a == null ? "null" : "set")} vs {(b == null ? "null" : "set")}";
            Type t = a.GetType();
            if (t != b.GetType()) return $"{path}: {t.Name} vs {b.GetType().Name}";
            if (t == typeof(float)) return BitConverter.SingleToInt32Bits((float)a) == BitConverter.SingleToInt32Bits((float)b) ? null : $"{path}: {(float)a:R} vs {(float)b:R}";
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return Equals(a, b) || (t == typeof(string) && (string)a == (string)b) ? null : $"{path}: {a} vs {b}";
            // a reference to another asset compares by identity; the two objects compared themselves by their fields
            if (path.Length > 0 && typeof(UnityEngine.Object).IsAssignableFrom(t)) return ReferenceEquals(a, b) ? null : $"{path}: different objects";
            foreach (FieldInfo f in KitTypes.SerializedFields(t))
            {
                string d = FirstDifference(f.GetValue(a), f.GetValue(b), ignore, path.Length == 0 ? f.Name : path + "." + f.Name);
                if (d != null) return d;
            }
            return null;
        }

        static string Lists(IList a, IList b, Func<string, bool> ignore, string path)
        {
            int na = a != null ? a.Count : 0, nb = b != null ? b.Count : 0;
            if (na != nb) return $"{path}: {na} vs {nb} items";
            for (int i = 0; i < na; i++)
            {
                string d = FirstDifference(a[i], b[i], ignore, $"{path}[{i}]");
                if (d != null) return d;
            }
            return null;
        }
    }
}
