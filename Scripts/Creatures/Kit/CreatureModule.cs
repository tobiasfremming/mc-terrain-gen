// Creature kit (design §2.3): parts say what the animal IS; modules say what it DOES (Locomotion, Behaviour)
// and how it LOOKS (Look). A species lists the modules it has, held in CreatureSpecies.modules
// ([SerializeReference] List<CreatureModule>). A module type is a [Serializable] class in namespace CreatureKit
// with [Module] and a [Param] on every field.
using System;
using System.Collections.Generic;

namespace CreatureKit
{
    [Serializable]
    public abstract class CreatureModule
    {
        public const string Locomotion = "Locomotion", Behaviour = "Behaviour", Look = "Look";

        public ModuleAttribute Meta => MetaOf(GetType());

        // null when valid (e.g. a module that needs another module or a part), else the first problem
        public virtual string Validate(in SpeciesCtx c) => null;

        public CreatureModule Clone() => (CreatureModule)MemberwiseClone();

        // A locomotion module's mode for one individual (Kit/PartRuntime.cs: ILocomotionMode), built in the sim's
        // constructor; null = none (Walk itself, Slither and Climb are the walking sub-step).
        public virtual ILocomotionMode CreateMode(CreatureSim sim, in ModeCtx c) => null;

        // A behaviour module's behaviour for one individual's ModularBrain (Kit/Brains.cs); null = none here.
        public virtual IBehaviour CreateBehaviour(in BehaviourCtx c) => null;

        static readonly Dictionary<Type, ModuleAttribute> metas = new Dictionary<Type, ModuleAttribute>();
        public static ModuleAttribute MetaOf(Type t)
        {
            if (!metas.TryGetValue(t, out ModuleAttribute a))
            {
                a = (ModuleAttribute)Attribute.GetCustomAttribute(t, typeof(ModuleAttribute), false)
                    ?? throw new InvalidOperationException(t.Name + " has no [Module] attribute");
                metas[t] = a;
            }
            return a;
        }

        public override string ToString() => Meta.Name;
    }
}
