using System;
using UnityEngine;

// One octopus's behaviour on top of its body (pure C#; OctopusCreature wraps it for the world and renderer):
// the soft body and its arms, one TentacleAgent per arm, the blackboard, the brain's intent, and contact with
// the real world - every query goes through an ISurfaceProbe (terrain + real colliders) and is counted.
//
// Per frame: gravity from upAt; the brain (if any) publishes the intent; each arm agent decides and acts;
// the body ticks; then contact:
//   motion     one ray along the head's motion this frame and one along each moving free arm tip's: what
//              they meet becomes a contact plane (point, normal, local radius) for PlaneSeconds (catches falls)
//   body       2 lattice particles per frame (round-robin) probed outward from the body centre; a surface within
//              the skin becomes a contact plane too
//   planes     every lattice particle (skin depth) and free arm particle (its radius) near a contact plane is
//              kept on its outer side with its velocity into it removed: only the parts that meet the surface
//              stop, so an impact squashes the soft body
//   free arms  a sweep of SweepSegments segment casts per frame (round-robin over free arms); a segment that
//              would pass through a surface drapes its far particle on it for a moment
public sealed class OctopusController
{
    public const int SweepSegments = 8;       // per frame, shared by the arms' unpinned runs
    public const int SweepSegmentsFast = 8;   // extra while the body or an arm tip moves fast
    public const int MaxPlanes = 20;
    public const float PlaneSeconds = 0.5f;
    public const int BodyProbesPerFrame = 3;
    public const float Gravity = 9.81f;

    public readonly OctopusBody Body;
    public readonly TentacleAgent[] Agents;
    public readonly ArmBlackboard Board;
    public readonly OctopusEvents Events = new OctopusEvents();
    public ISurfaceProbe SurfaceProbe;
    public IBranchHints Hints;
    public OctopusBrain Brain;
    public OctoIntent Intent;                 // set by the brain, or by a test directly
    public Func<Vector3, Vector3> UpAt;
    public int RestAnchors = 4;               // at rest, free arms keep feeling until this many hold on
    // The enclosure (horizontal disc about EnclosureCentre, up = Up): arms take no hold farther out than
    // EnclosureRadius - a margin, so with the arm's reach the whole animal stays inside.
    public Vector3 EnclosureCentre;
    public float EnclosureRadius = float.PositiveInfinity;

    public float HorizontalDistance(Vector3 p)
    {
        Vector3 d = p - EnclosureCentre;
        d -= Up * OctoMath.Dot(d, Up);
        return d.magnitude;
    }

    // Holds must lie this far inside the enclosure edge (an arm's reach beyond a hold stays inside).
    public bool HoldInside(Vector3 p) => float.IsInfinity(EnclosureRadius) || HorizontalDistance(p) <= EnclosureRadius - 1.3f * Body.Arms[0].Length;
    public float HoldSeconds = 1.5f;
    public float Time { get; private set; }
    public float LastDt { get; private set; } = 1f / 60f;
    public Vector3 Up { get; private set; } = Vector3.up;

    // stats
    public int QueriesThisFrame { get; private set; }
    public int MaxQueriesPerFrame { get; private set; }
    public long TotalQueries { get; private set; }
    public int BodyPushes, Drapes;

    int queries, bodyCursor, sweepArm, sweepSeg;
    bool gripPlanned;   // one grip plan (~25-30 probes) per frame keeps the per-frame query count bounded
    readonly int slotBase;

    public OctopusController(OctopusBody body, ISurfaceProbe probe, Func<Vector3, Vector3> upAt = null, uint seed = 1, int slotBase = 0)
    {
        Body = body; SurfaceProbe = probe; UpAt = upAt; this.slotBase = slotBase;
        Board = new ArmBlackboard(body.ArmCount);
        Agents = new TentacleAgent[body.ArmCount];
        for (int a = 0; a < body.ArmCount; a++)
            Agents[a] = new TentacleAgent(a, body.Arms[a], OctopusShape.ArmDir(body.Spec, a), seed);
        Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
    }

    public int SlotOf(int arm) => slotBase + arm;
    public int SlotCount => Body.ArmCount + SoftLattice.Count;

    // ------------------------------------------------------------------ queries (counted)

    public bool Cast(Vector3 origin, Vector3 dir, float maxDist, out SurfaceHit hit)
    {
        queries++;
        if (SurfaceProbe == null) { hit = default; return false; }
        float m = dir.magnitude;
        if (!(m > 1e-9f)) { hit = default; return false; }
        return SurfaceProbe.Cast(origin, dir / m, maxDist, out hit);
    }

    public bool Probe(Vector3 near, Vector3 up, float window, int prefer, int slot, out SurfaceHit hit)
    {
        queries++;
        if (SurfaceProbe == null) { hit = default; return false; }
        return SurfaceProbe.Probe(near, up, window, prefer, slot, out hit);
    }

    public void CountProbes(int n) => queries += n;

    // The per-frame query budget (design §4: <= 40). Optional work (exploration casts, the collision sweep,
    // the brain's line-of-sight checks) asks first and waits for a later frame when the budget is spent;
    // grip plans, tactile, body and motion contact always run.
    public int QueryBudget = 40;
    public bool Spare(int n) => queries + n <= QueryBudget;

    // An arm may plan a grip this frame (the first to ask); the others keep reaching and ask again next frame.
    public bool TryTakeGripPlan()
    {
        if (gripPlanned) return false;
        gripPlanned = true;
        return true;
    }

    // ------------------------------------------------------------------ tick

    // Distance LOD (design §4): far from the viewer the octopus ticks at half rate (two frames' time in one
    // step) without the motion rays and the collision sweep.
    public bool Far;
    float lodAccum;
    int lodFrame;
    public int TicksRun { get; private set; }

    public void Tick(float dt)
    {
        if (!(dt > 0f)) return;
        if (Far)
        {
            lodAccum += dt;
            if ((++lodFrame & 1) != 0) return;
            dt = lodAccum; lodAccum = 0f;
        }
        else lodAccum = 0f;
        TicksRun++;
        dt = Mathf.Min(dt, 0.1f);
        Time += dt;
        LastDt = dt;
        queries = 0;
        Vector3 c = Body.HeadCentre;
        Up = UpAt != null ? OctoMath.Norm(UpAt(c), Vector3.up) : Vector3.up;
        Vector3 g = -Up * Gravity;
        Vector3 bg = Body.Gravity;
        if (g.x != bg.x || g.y != bg.y || g.z != bg.z) Body.Gravity = g;

        gripPlanned = false;
        Brain?.Tick(dt, this);
        for (int a = 0; a < Agents.Length; a++) Agents[a].Tick(dt, this);
        Body.Tick(dt);
        if (!Body.Asleep)
        {
            if (!Far) MotionRays();
            HeldSurfaces();
            CollideBody();
            if (!Far) SweepFreeArms();
            ApplyPlanes();
        }
        RememberMotion();

        QueriesThisFrame = queries;
        MaxQueriesPerFrame = Mathf.Max(MaxQueriesPerFrame, queries);
        TotalQueries += queries;
    }

    // Spawn / settle: every arm casts around its own cone (up and out first) and grips the nearest surface
    // in reach. Returns how many arms took hold. Meant for placing an octopus in a tree at spawn time.
    public int AnchorNearby(int maxArms = 8)
    {
        int held = 0;
        float L = Body.Arms[0].Length;
        for (int a = 0; a < Agents.Length && held < maxArms; a++)
        {
            TentacleChain ch = Body.Arms[a];
            Vector3 root = ch.X[0], outDir = OctoMath.Rotate(Body.Orientation, OctopusShape.ArmDir(Body.Spec, a));
            float bestD = float.MaxValue; SurfaceHit best = default; bool found = false;
            for (int k = 0; k < 24; k++)
            {
                // a fixed fan: elevation from straight up to below horizontal, azimuth around the arm's direction
                float el = Mathf.Lerp(80f, -40f, (k / 6) / 3f) * Mathf.Deg2Rad, az = ((k % 6) - 2.5f) * 18f * Mathf.Deg2Rad;
                Vector3 h = OctoMath.Rotate(outDir, Up, az);
                Vector3 dir = OctoMath.Norm(h * Mathf.Cos(el) + Up * Mathf.Sin(el), Up);
                if (Cast(root, dir, 0.95f * L, out SurfaceHit hit) && hit.distance > 0.25f * L && hit.distance < bestD && HoldInside(hit.point))
                {
                    float crowd = Board.Crowding(a, hit.point, 0.2f * L);
                    if (crowd > 0.5f) continue;
                    bestD = hit.distance; best = hit; found = true;
                }
            }
            if (found && Agents[a].GripAt(this, best)) held++;
        }
        return held;
    }

    internal void NotifyGrab(IPreyTarget prey) { Brain?.OnGrab(prey); Events.RaiseGrabbed(prey); prey.OnGrabbed(); }
    internal void NotifyRelease(IPreyTarget prey) { Brain?.OnRelease(prey); Events.RaiseReleased(prey); prey.OnReleased(); }

    // ------------------------------------------------------------------ contact

    struct Plane { public Vector3 point, normal; public float until, radius; }
    readonly Plane[] planes = new Plane[MaxPlanes];
    int planeCount;
    Vector3 prevHead;
    readonly Vector3[] prevTip = new Vector3[16];
    bool havePrev;

    public int ContactPlanes => planeCount;

    void AddPlane(SurfaceHit h, float radius)
    {
        // refresh a plane at nearly the same place, else take a free or the oldest slot
        float s2 = 0.01f * Body.SizeScale * Body.SizeScale;
        int slot = -1;
        for (int i = 0; i < planeCount; i++)
            if ((planes[i].point - h.point).sqrMagnitude < s2 && OctoMath.Dot(planes[i].normal, h.normal) > 0.9f) { slot = i; break; }
        if (slot < 0 && planeCount < MaxPlanes) slot = planeCount++;
        if (slot < 0)
        {
            slot = 0;
            for (int i = 1; i < planeCount; i++) if (planes[i].until < planes[slot].until) slot = i;
        }
        planes[slot] = new Plane { point = h.point, normal = OctoMath.Norm(h.normal, Up), until = Time + PlaneSeconds, radius = radius };
        if (OctoMath.Dot(h.normal, Up) > 0.5f) groundedUntil = Time + 0.5f;
    }

    float Skin => 0.3f * Body.Spec.HeadRadius * Body.SizeScale;   // the lattice sits inside the skin

    bool fastMotion;
    int frameParity;

    void MotionRays()
    {
        fastMotion = false;
        if (!havePrev) return;
        float reach = 0.6f * Body.Arms[0].Length;
        Vector3 head = Body.HeadCentre, d = head - prevHead;
        float len = d.magnitude, fast = 0.025f * Mathf.Sqrt(Body.SizeScale);   // 1.5 m/s at 60 Hz
        if (len > fast) fastMotion = true;
        if (len > 1e-4f)
        {
            float lead = Skin + 0.5f * Body.Spec.MantleRadius * Body.SizeScale;
            if (Cast(prevHead, d, len + lead, out SurfaceHit h)) AddPlane(h, reach);
        }
        for (int a = 0; a < Agents.Length && a < prevTip.Length; a++)
        {
            if (!Agents[a].IsFreeArm) continue;
            TentacleChain ch = Body.Arms[a];
            Vector3 tip = ch.X[ch.N - 1], m = tip - prevTip[a];
            float ml = m.magnitude;
            if (ml < 0.02f * Body.SizeScale) continue;     // only a moving tip can strike through something
            if (ml > 2f * fast) fastMotion = true;
            if (Probe(tip, -m / ml, ml + 0.02f * Body.SizeScale, SurfaceHit.NoSource, -1, out SurfaceHit h)) AddPlane(h, 0.4f * reach);
        }
        // and along the motion of each free arm's fastest particle (where a sweeping arm meets something)
        frameParity ^= 1;
        for (int a = frameParity; a < Agents.Length; a += 2)   // each arm every other frame
        {
            if (!Agents[a].IsFreeArm || !Spare(1)) continue;
            TentacleChain ch = Body.Arms[a];
            int fk = -1; float fv = 0f;
            for (int k = 1; k < ch.N - 1; k++)
            {
                if (ch.Pinned[k]) continue;
                float v = (ch.X[k] - ch.Prev[k]).sqrMagnitude;
                if (v > fv) { fv = v; fk = k; }
            }
            if (fk < 0) continue;
            // the last sub-step's motion, stretched to the whole frame
            Vector3 mv = (ch.X[fk] - ch.Prev[fk]) * Body.Spec.SubSteps;
            float ml = mv.magnitude, rk = ch.Spec.RadiusAt(ch.Spec.Spacing * fk) * ch.SizeScale;
            if (ml < 0.5f * Mathf.Sqrt(Body.SizeScale) * LastDt) continue;   // slower than 0.5 m/s: nothing to catch
            // a probe, not a cast: from air it finds the surface ahead along the motion, from inside a solid the
            // exit back the way the particle came (a cast from inside sees nothing)
            if (Probe(ch.X[fk], -mv / ml, ml + rk, SurfaceHit.NoSource, -1, out SurfaceHit h)) AddPlane(h, 0.3f * reach);
        }
    }

    // The surfaces the arms hold are known without a query: each anchored arm's base-most pin (its surface
    // point and normal) keeps a contact plane alive, so the run leading into a hold never sags through it.
    void HeldSurfaces()
    {
        float patch = 0.3f * Body.Arms[0].Length;
        for (int a = 0; a < Agents.Length; a++)
        {
            if (!Board.Anchored[a]) continue;
            TentacleChain ch = Body.Arms[a];
            for (int k = 1; k < ch.N; k++)
            {
                if (!ch.Pinned[k]) continue;
                float rk = ch.Spec.RadiusAt(ch.Spec.Spacing * k) * ch.SizeScale;
                Vector3 n = ch.PinNormal[k];
                AddPlane(new SurfaceHit { point = ch.PinPoint[k] - n * rk, normal = n, source = ch.PinSource[k] }, patch);
                break;
            }
        }
    }

    void RememberMotion()
    {
        prevHead = Body.HeadCentre;
        for (int a = 0; a < Agents.Length && a < prevTip.Length; a++) prevTip[a] = Body.Arms[a].X[Body.Arms[a].N - 1];
        havePrev = true;
    }

    void CollideBody()
    {
        SoftLattice lat = Body.Lattice;
        Vector3 centre = lat.ClusterCentre[OctopusShape.ClusterHead];
        float skin = Skin;
        for (int i = 0; i < BodyProbesPerFrame; i++)
        {
            int p = bodyCursor; bodyCursor = (bodyCursor + 1) % SoftLattice.Count;
            Vector3 x = lat.X[p];
            Vector3 up = OctoMath.Norm(x - centre, Up);
            if (!Probe(x + up * skin, up, 2f * skin + 0.2f * Body.SizeScale, SurfaceHit.NoSource, slotBase + Body.ArmCount + p, out SurfaceHit h)) continue;
            // a surface within the skin (above the particle, or the exit of the solid it is in)
            if (skin - OctoMath.Dot(x - h.point, h.normal) > 0f) AddPlane(h, 0.6f * Body.Arms[0].Length);
        }
        Board.Grounded = Time < groundedUntil;
    }

    // Keep every particle near a contact plane on its outer side (skin / arm radius), velocity into it removed.
    void ApplyPlanes()
    {
        int live = 0;
        for (int i = 0; i < planeCount; i++) if (planes[i].until >= Time) planes[live++] = planes[i];
        planeCount = live;
        if (planeCount == 0) return;
        SoftLattice lat = Body.Lattice;
        float skin = Skin;
        for (int i = 0; i < SoftLattice.Count; i++)
            if (Project(ref lat.X[i], ref lat.Prev[i], skin)) BodyPushes++;
        for (int a = 0; a < Agents.Length; a++)
        {
            TentacleChain ch = Body.Arms[a];
            for (int k = 1; k < ch.N; k++)
            {
                if (ch.Pinned[k]) continue;
                float rk = ch.Spec.RadiusAt(ch.Spec.Spacing * k) * ch.SizeScale;
                Project(ref ch.X[k], ref ch.Prev[k], rk);
            }
        }
    }

    bool Project(ref Vector3 x, ref Vector3 prev, float clearance)
    {
        bool moved = false;
        for (int p = 0; p < planeCount; p++)
        {
            Plane pl = planes[p];
            Vector3 rel = x - pl.point;
            float h = OctoMath.Dot(rel, pl.normal);
            // well clear, or far behind the plane (the other side of a branch): not this plane's business
            if (h >= clearance || h < -4f * clearance - 0.3f * Body.SizeScale) continue;
            if ((rel - pl.normal * h).sqrMagnitude > pl.radius * pl.radius) continue;   // outside the plane's patch
            float push = clearance - h;
            Vector3 v = x - prev;
            x += pl.normal * push;
            // remove the velocity into the plane, keep the tangential part (friction-free sliding)
            float vn = OctoMath.Dot(v, pl.normal);
            prev += pl.normal * (push + (vn < 0f ? vn : 0f));
            moved = true;
        }
        return moved;
    }

    float groundedUntil;

    void SweepFreeArms()
    {
        // fast motion (a fall, an impact, a whipping arm) gets a bigger sweep for as long as it lasts
        int arms = Agents.Length, budget = SweepSegments + (fastMotion ? SweepSegmentsFast : 0);
        float patch = 0.25f * Body.Arms[0].Length;
        for (int tries = 0; tries < arms && budget > 0; tries++)
        {
            TentacleAgent ag = Agents[sweepArm];
            if (ag.Mode == TentacleAgent.State.Strike || ag.Mode == TentacleAgent.State.Hold) { sweepArm = (sweepArm + 1) % arms; sweepSeg = 0; continue; }
            TentacleChain ch = ag.Arm;
            while (budget > 0 && sweepSeg < ch.N - 1)
            {
                if (!Spare(1)) return;
                int k = sweepSeg++;
                budget--;
                if (ch.Pinned[k + 1]) continue;
                Vector3 a = ch.X[k], d = ch.X[k + 1] - a;
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                // a segment that would pass through a surface: the surface becomes a contact plane and the arm's
                // particles near it are kept outside (it drapes and slides over the branch; no pins, so the
                // arm's own decisions about holds are untouched)
                if (Cast(a, d, len, out SurfaceHit h)) { AddPlane(h, patch); Drapes++; }
            }
            if (sweepSeg >= ch.N - 1) { sweepSeg = 0; sweepArm = (sweepArm + 1) % arms; }
        }
    }
}
