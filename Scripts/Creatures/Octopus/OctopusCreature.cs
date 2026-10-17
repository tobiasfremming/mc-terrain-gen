using System;
using UnityEngine;

// A tree octopus in the world (ICreatureBody): the controller (body, arm agents, brain), its surfaces and
// hints, and its look for the renderer. CreatureWorld ticks it inside the back-face scope; the renderer draws
// it with its species' octopus mesh and reads the brain's cloak / chroma / flash / cloud phase every frame.
public sealed class OctopusCreature : ICreatureBody
{
    public readonly OctopusController Controller;
    public readonly OctopusBrain Brain;
    readonly CompositeSurfaceProbe composite;
    readonly BranchHints hints;
    float hintTime;

    public CreatureSpecies Species { get; }
    public OctopusBody Body => Controller.Body;
    public float SizeScale => Controller.Body.SizeScale;
    public double LastTickMs { get; private set; }
    readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

    // The player (or any viewer) the brain freezes for; null = none.
    public Transform Viewer;
    public float LodDistance = 30f;

    public OctopusCreature(CreatureSpecies sp, ISurfaceProbe probe, Func<Vector3, Vector3> upAt, Vector3 position, Quaternion rotation,
                           float sizeScale, int seed, BranchHints hints = null)
    {
        if (sp == null) throw new ArgumentNullException(nameof(sp));
        Species = sp;
        var body = new OctopusBody(sp.cephalopod, position, rotation, sizeScale);
        Controller = new OctopusController(body, probe, upAt, (uint)seed);
        Brain = new OctopusBrain((uint)seed);
        Controller.Brain = Brain;
        this.hints = hints;
        Controller.Hints = hints;
        composite = probe as CompositeSurfaceProbe;
        if (probe is ISurfaceSlots slots) slots.EnsureSlots(Controller.SlotCount);
    }

    // Takes hold of whatever is around at spawn (a tree): see OctopusController.AnchorNearby.
    public int AnchorNearby() { GateSurfaces(); return Controller.AnchorNearby(); }

    void GateSurfaces()
    {
        if (composite == null) return;
        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
        Controller.Body.Encapsulate(ref lo, ref hi);
        var b = new Bounds(); b.SetMinMax(lo, hi);
        b.Expand(2.6f * Controller.Body.Arms[0].Length);   // strike reach (1.3 L) each way
        composite.SetActiveBounds(b);
    }

    public void Tick(float dt)
    {
        watch.Restart();
        if (Viewer != null)
        {
            Brain.ViewerValid = true;
            Brain.ViewerPosition = Viewer.position;
            Brain.ViewerForward = Viewer.forward;
            Controller.Far = (Viewer.position - Controller.Body.HeadCentre).sqrMagnitude > LodDistance * LodDistance;
        }
        else Brain.ViewerValid = false;
        for (int i = 0; i < Brain.Prey.Count; i++) if (Brain.Prey[i] is IPreySampled ps) ps.Sample(dt);
        if (hints != null)
        {
            hintTime += dt;
            Vector3 c = Controller.Body.HeadCentre;
            float r = 2f * Controller.Body.Arms[0].Length;
            hints.Refresh(new Bounds(c, Vector3.one * (2f * r)), hintTime);
        }
        GateSurfaces();
        Controller.Tick(dt);
        watch.Stop();
        LastTickMs = watch.Elapsed.TotalMilliseconds;
    }

    public void WriteBoneWorld(Matrix4x4[] dst, int offset) => Controller.Body.WriteBoneWorld(dst, offset);
    public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) => Controller.Body.Encapsulate(ref lo, ref hi);

    public bool DynamicLook => true;
    public void UpdateLook(ref CreatureInstance inst)
    {
        inst.cloak = Brain.Cloak;
        inst.chroma = Brain.Chroma;
        inst.flash = Brain.Flash;
        inst.cloudPhase = Brain.CloudPhase;
    }
}
