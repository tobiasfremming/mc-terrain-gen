// Creature kit (design §2.1, §2.3): a part is DATA, held polymorphically in CreatureSpecies.parts
// ([SerializeReference] List<CreaturePart>, the YAML names the class), plus small contracts: how many bones it
// needs, how it validates, which other parts it names. Runtime behaviour (mesh emitters, IPartRuntime) joins
// in Phase 3; until then CompiledSpecies turns parts into the legacy runtime fields the systems read.
// A part type is a [Serializable] class in namespace CreatureKit with [Part] and a [Param] on every field.
using System;
using System.Collections.Generic;

namespace CreatureKit
{
    // What validation and bone counts may look at: the species after authoring sugar is expanded.
    public readonly struct SpeciesCtx
    {
        public readonly IReadOnlyList<CreaturePart> Parts;
        public readonly IReadOnlyList<CreatureModule> Modules;
        public readonly int Joints;          // spine joints of the body (0 for a soft body)
        public readonly bool Soft;           // a soft (cephalopod) body plan

        public SpeciesCtx(IReadOnlyList<CreaturePart> parts, IReadOnlyList<CreatureModule> modules, int joints, bool soft)
        {
            Parts = parts ?? new CreaturePart[0]; Modules = modules ?? new CreatureModule[0]; Joints = joints; Soft = soft;
        }

        public CreaturePart Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Parts.Count; i++) if (Parts[i] != null && Parts[i].id == id) return Parts[i];
            return null;
        }

        public int IndexOf(CreaturePart p)
        {
            for (int i = 0; i < Parts.Count; i++) if (ReferenceEquals(Parts[i], p)) return i;
            return -1;
        }

        public T Module<T>() where T : CreatureModule
        {
            for (int i = 0; i < Modules.Count; i++) if (Modules[i] is T m) return m;
            return null;
        }
    }

    [Serializable]
    public abstract class CreaturePart
    {
        [Param(ParamUnit.Text, "unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index)")]
        public string id = "";
        [Param(ParamUnit.Struct, "where it attaches: spine joint, side, offset in the joint frame")]
        public PartSocket at;
        [Param(ParamUnit.Flag, "authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order")]
        public bool mirror;

        public PartAttribute Meta => MetaOf(GetType());
        public PartKind Kind => Meta.Kind;

        public virtual int BoneCount(in SpeciesCtx c) => 0;
        // null when valid, else the first problem, naming the part's id
        public virtual string Validate(in SpeciesCtx c) => null;
        // ids of other parts this one names (checked by compose: each must exist)
        public virtual void CollectReferences(List<string> into) { }

        // A copy that shares no arrays with this one (compose clones before it edits).
        public CreaturePart Clone()
        {
            var c = (CreaturePart)MemberwiseClone();
            c.CopyArrays();
            return c;
        }
        protected virtual void CopyArrays() { }

        static readonly Dictionary<Type, PartAttribute> metas = new Dictionary<Type, PartAttribute>();
        public static PartAttribute MetaOf(Type t)
        {
            if (!metas.TryGetValue(t, out PartAttribute a))
            {
                a = (PartAttribute)Attribute.GetCustomAttribute(t, typeof(PartAttribute), false)
                    ?? throw new InvalidOperationException(t.Name + " has no [Part] attribute");
                metas[t] = a;
            }
            return a;
        }

        public override string ToString() => $"{Meta.Name} '{id}'";
    }
}
