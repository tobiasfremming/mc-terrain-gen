// Deep copy of an object's serialized fields (Object.Instantiate in the stubbed UnityEngine).
using System;
using System.Collections;
using System.Reflection;

public static class HarnessClone
{
    public static object Deep(object o)
    {
        if (o == null) return null;
        Type t = o.GetType();
        if (t.IsPrimitive || t.IsEnum || o is string) return o;
        if (o is Array a)
        {
            var c = Array.CreateInstance(t.GetElementType(), a.Length);
            for (int i = 0; i < a.Length; i++) c.SetValue(Deep(a.GetValue(i)), i);
            return c;
        }
        if (o is IList l && t.IsGenericType)
        {
            var c = (IList)Activator.CreateInstance(t);
            foreach (object x in l) c.Add(Deep(x));
            return c;
        }
        object copy = HarnessYaml.New(t);
        for (Type k = t; k != null && k != typeof(object); k = k.BaseType)
            foreach (FieldInfo f in k.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsInitOnly) continue;
                bool ser = !f.IsDefined(typeof(NonSerializedAttribute), false) && (f.IsPublic || f.IsDefined(typeof(UnityEngine.SerializeField), false));
                object v = f.GetValue(o);
                // serialized fields copy deep; the rest (caches) start fresh, as after a Unity serialization round trip
                if (ser) f.SetValue(copy, typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType) ? v : Deep(v));
            }
        if (o is UnityEngine.Object uo) ((UnityEngine.Object)copy).name = uo.name + "(Clone)";
        return copy;
    }
}
