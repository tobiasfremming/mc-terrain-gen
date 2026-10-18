using UnityEngine;

// What the renderer and the world need from any creature body, whatever its plan (tree octopus design §1):
// the spine creatures (CreatureSim, through SpineBody) and the cephalopods (OctopusCreature).
public interface ICreatureBody
{
    CreatureSpecies Species { get; }
    float SizeScale { get; }
    // Bone-world matrices (bone order and conventions of the species' body plan) into dst[offset..].
    void WriteBoneWorld(Matrix4x4[] dst, int offset);
    // Grows [lo, hi] by the body's simulated points (the renderer adds the species' reach x SizeScale).
    void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi);
    // Bodies that run themselves (an octopus with its brain inside); spine sims are ticked by the world
    // toward their brain's target instead.
    void Tick(float dt);
    // True when UpdateLook changes the per-instance shading data (cloak, chromatophores, flash): the
    // renderer then re-uploads the instance buffer every frame. False: uploaded on change only.
    bool DynamicLook { get; }
    void UpdateLook(ref CreatureInstance inst);
}

// A spine CreatureSim seen as an ICreatureBody. The sim is ticked by CreatureWorld (brain target), so Tick
// here only holds still; bone writing and bounds are exactly the renderer's former spine path.
public sealed class SpineBody : ICreatureBody
{
    public readonly CreatureSim Sim;
    public SpineBody(CreatureSim sim) { Sim = sim; }

    public CreatureSpecies Species => Sim.Species;
    public float SizeScale => Sim.SizeScale;
    public void WriteBoneWorld(Matrix4x4[] dst, int offset) => Sim.WriteBoneWorld(dst, offset);

    public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
    {
        Vector3[] j = Sim.Joints;
        for (int k = 0; k < j.Length; k++) { lo = Vector3.Min(lo, j[k]); hi = Vector3.Max(hi, j[k]); }
        LegState[] legs = Sim.Legs;
        for (int k = 0; k < legs.Length; k++)
        {
            lo = Vector3.Min(lo, legs[k].foot); hi = Vector3.Max(hi, legs[k].foot);
            lo = Vector3.Min(lo, legs[k].elbow); hi = Vector3.Max(hi, legs[k].elbow); // an arthropod's knee peaks high above the body
        }
    }

    public void Tick(float dt) => Sim.Tick(dt, Sim.Joints[0]);
    public bool DynamicLook => false;
    public void UpdateLook(ref CreatureInstance inst) { }
}
