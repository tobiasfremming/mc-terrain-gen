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
        DbgPhase?.Invoke("start");
        for (int a = 0; a < Agents.Length; a++) Agents[a].Tick(dt, this);
        DbgPhase?.Invoke("agents");
        RestRelax();
        bool wasAsleep = Body.Asleep;
        Body.Tick(dt);
        DbgPhase?.Invoke("body");
        if (Body.Asleep && wasAsleep)
        {
            // asleep: nothing moves, so contact neither changes nor ages - the planes holding the parts that
            // rest on a surface must still be there when it wakes, or those parts drop into the bark
            for (int i = 0; i < planeCount; i++) planes[i].until += dt;
        }
        else
        {
            if (!Far) MotionRays();
            HeldSurfaces();
            CollideBody();
            if (!Far) SweepFreeArms();
            ApplyPlanes();
            // fell asleep this tick: the pose to hold is the one after contact (the body's sleep snapshot is
            // taken before it; holding that one would leave a part resting on a surface where the solver had it,
            // inside the surface, and show as a jump the moment the octopus falls asleep)
            if (Body.Asleep) Body.ResnapSleep();
        }
        DbgPhase?.Invoke("planes");
        RememberMotion();

        QueriesThisFrame = queries;
        MaxQueriesPerFrame = Mathf.Max(MaxQueriesPerFrame, queries);
        TotalQueries += queries;
    }

    // A settled rest - the brain wants rest, every arm holds on or lies curled, none is searching, reaching or
    // letting go - relaxes the whole animal: the body and its holding arms get the same absolute damping the
    // curled arms have (TentacleAgent.RestRelaxRate), so the last swing on the holds dies out in a second or
    // two and the body can fall asleep. Off again the moment any arm does anything else.
    public bool Settled { get; private set; }
    public static float BendMax = 0.12f;       // x arm length: thicker surfaces keep flat contact planes
    public float NextRestRelease;              // a settled rest lets go of a taut spare hold at most this often
    public static float RestHoldTone = 0.5f;
    public static float RestSlack = 1.06f;      // a holding arm's free run's length target in a settled rest   // a holding arm's muscle tone in a settled rest (relaxed)

    void RestRelax()
    {
        bool rest = Intent.mode == OctoMode.Rest || Intent.mode == OctoMode.Hold;
        bool settled = rest && Board.AnchoredCount > Board.MinAnchors;
        for (int a = 0; a < Agents.Length && settled; a++)
        {
            TentacleAgent.State m = Agents[a].Mode;
            if (m != TentacleAgent.State.Anchored && m != TentacleAgent.State.Curl && m != TentacleAgent.State.Hold) settled = false;
        }
        Settled = settled;
        Body.Settled = settled;
        float want = settled ? TentacleAgent.RestRelaxRate / Body.TimeScale : 0f;
        if (Body.Lattice.ExtraDamping != want) Body.Lattice.ExtraDamping = want;
        for (int a = 0; a < Agents.Length; a++)
            if (Agents[a].Mode == TentacleAgent.State.Anchored)
            {
                if (Body.Arms[a].ExtraDamping != want) Body.Arms[a].ExtraDamping = want;
                if (settled && (Dbg & 64) == 0) Body.Arms[a].Tone = RestHoldTone;
                if (settled && (Dbg & 128) == 0)
                {
                    // and lets its run go a little slack: holds taken one by one never agree exactly, and eight
                    // taut runs pulling against each other set the solver ringing (a 2-tick flicker at the roots)
                    TentacleChain ch = Body.Arms[a];
                    for (int j = 0; j < ch.N - 1; j++) if (!ch.Pinned[j + 1]) ch.LengthTarget[j] = RestSlack;
                }
            }
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

    struct Plane { public Vector3 point, normal, axis; public float until, radius, bend; public int owner; }
    readonly Plane[] planes = new Plane[MaxPlanes];
    int planeCount;
    Vector3 prevHead;
    readonly Vector3[] prevTip = new Vector3[16];
    bool havePrev;
    readonly Vector3[] frameLattice = new Vector3[SoftLattice.Count];   // the lattice at the end of the last tick
    Vector3[] frameArms = new Vector3[0];                               // the arms' particles at the end of the last tick
    int frameStride;

    public int ContactPlanes => planeCount;

    // owner: the lattice particle whose own probe found the surface (-1: none)
    void AddPlane(SurfaceHit h, float radius, int owner = -1)
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
        Vector3 nn = OctoMath.Norm(h.normal, Up);
        // the surface's bend radius from the planes already around it: two planes on one branch, their points
        // d apart across the branch (the part of d along n x n', the branch axis, removed), fit one circle. A flat plane is only true within about that radius of its
        // point: further out it would claim the air beside and below a thin branch as solid, push an arm
        // hanging there sideways out of it every frame, and the arm's muscles would pull it straight back.
        float bend = float.PositiveInfinity;
        Vector3 axis = Vector3.zero;
        for (int i = 0; i < planeCount; i++)
        {
            if (i == slot) continue;
            float c = OctoMath.Dot(planes[i].normal, nn);
            if (c > 0.98f || c < -0.95f) continue;     // nearly parallel: no curvature to read (or two faces)
            Vector3 d = h.point - planes[i].point, ax = Vector3.Cross(planes[i].normal, nn);
            float al2 = ax.sqrMagnitude;
            if (al2 < 1e-8f) continue;
            d -= ax * (OctoMath.Dot(d, ax) / al2);
            if (d.sqrMagnitude > 0.25f * Body.Arms[0].Length * Body.Arms[0].Length) continue;   // far apart: different surfaces
            // on one convex circle p = centre + r n, so d = r (n - n'): r from the projection, and only when
            // d really is that (a concave corner - ground and a trunk - or two separate surfaces are not)
            Vector3 dn = nn - planes[i].normal;
            float r = OctoMath.Dot(d, dn) / dn.sqrMagnitude;
            if (!(r > 0f) || r > BendMax * Body.Arms[0].Length || (d - dn * r).sqrMagnitude > 0.09f * r * r + 1e-4f * Body.SizeScale * Body.SizeScale) continue;
            ax *= 1f / Mathf.Sqrt(al2);
            if (r < bend) { bend = r; axis = ax; }
            if ((Dbg & 1) == 0 && r < planes[i].bend) { planes[i].bend = r; planes[i].axis = ax; }
        }
        if ((Dbg & 1) != 0) bend = float.PositiveInfinity;
        planes[slot] = new Plane { point = h.point, normal = nn, axis = axis, until = Time + PlaneSeconds, radius = radius, bend = bend, owner = owner };
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
        for (int i = 0; i < SoftLattice.Count; i++) frameLattice[i] = Body.Lattice.X[i];
        for (int a = 0; a < Agents.Length && a < prevTip.Length; a++) prevTip[a] = Body.Arms[a].X[Body.Arms[a].N - 1];
        if (frameStride == 0)
        {
            for (int a = 0; a < Body.ArmCount; a++) frameStride = Mathf.Max(frameStride, Body.Arms[a].N);
            frameArms = new Vector3[frameStride * Body.ArmCount];
        }
        for (int a = 0; a < Body.ArmCount; a++)
        {
            TentacleChain ch = Body.Arms[a];
            for (int k = 0; k < ch.N; k++) frameArms[a * frameStride + k] = ch.X[k];
        }
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
            if (skin - OctoMath.Dot(x - h.point, h.normal) > 0f) AddPlane(h, 0.6f * Body.Arms[0].Length, p);
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
        for (int i = 0; i < planeCount; i++) planeTouched[i] = false;
        SoftLattice lat = Body.Lattice;
        float skin = Skin;
        for (int i = 0; i < SoftLattice.Count; i++)
            if (Project(ref lat.X[i], ref lat.Prev[i], skin, i)) BodyPushes++;
        for (int a = 0; a < Agents.Length; a++)
        {
            TentacleChain ch = Body.Arms[a];
            for (int k = 1; k < ch.N; k++)
            {
                if (ch.Pinned[k]) continue;
                float rk = ch.Spec.RadiusAt(ch.Spec.Spacing * k) * ch.SizeScale;
                Project(ref ch.X[k], ref ch.Prev[k], rk, -1, a * frameStride + k);
            }
        }
        float until = Time + PlaneSeconds;
        for (int i = 0; i < planeCount; i++) if ((Dbg & 2) == 0 && planeTouched[i] && planes[i].until < until) planes[i].until = until;
    }

    // lattice: the particle's index; it is pushed out of another particle's plane only if it was in front of
    // that plane when the frame began (it crossed it this frame). One already behind it at the start is on the
    // far side of a thin branch - the body hanging beneath the bar whose top the plane is - and pushing it
    // through would throw the body across the bar. Its own probe's plane (the exit of a solid it is inside)
    // always applies.
    // arm: the arm particle's slot in frameArms (-1: none). The same crossing rule holds for arm particles: one
    // that began the frame behind a plane is wrapped around the far side of the branch the plane was taken
    // from (a held arm's run, a draped arm), and pushing it out of the plane would throw it through the branch
    // every frame while its muscles pull it back. That rule is only needed where the bend is unknown: a plane
    // of known bend radius acts as the cylinder it fits (see Project), which ignores whatever lies beside or
    // behind the branch and does push out anything left inside it after an impact.
    // A plane something rests against (within kTouch x its clearance) stays alive while touched: contact lasts
    // as long as the touch does. Letting a plane in use lapse every PlaneSeconds dropped the particle on it
    // into the bark until a probe or the sweep found the surface again, then threw it back out - a perched
    // octopus twitched forever and never slept.
    public static int Dbg;
    public static Action<string> DbgPhase;
    const float kTouch = 1.5f, kStatic = 1f;
    readonly bool[] planeTouched = new bool[MaxPlanes];

    bool Project(ref Vector3 x, ref Vector3 prev, float clearance, int lattice = -1, int arm = -1)
    {
        bool moved = false;
        for (int p = 0; p < planeCount; p++)
        {
            Plane pl = planes[p];
            Vector3 n;
            float h;
            if (pl.bend < float.PositiveInfinity)
            {
                // a curved surface of known bend: the contact is the cylinder it fits (centre line through
                // point - r normal, along the axis), not the tangent plane. A plane would claim the air beside
                // and behind a thin branch as solid and throw an arm hanging there through or around it every
                // frame; the cylinder pushes only what is really inside, outward from the branch's centre.
                Vector3 cen = pl.point - pl.normal * pl.bend, rc = x - cen;
                float along = OctoMath.Dot(rc, pl.axis);
                if (along > pl.radius || along < -pl.radius) continue;          // beyond the patch along the branch
                Vector3 radial = rc - pl.axis * along;
                float dist = radial.magnitude;
                h = dist - pl.bend;
                if (h >= kTouch * clearance) continue;
                if (dist > 1e-6f) n = radial * (1f / dist); else n = pl.normal;
                if (OctoMath.Dot(n, pl.normal) < -0.2f) continue;   // the far side: only the near half was seen
            }
            else
            {
                n = pl.normal;
                Vector3 rel = x - pl.point;
                h = OctoMath.Dot(rel, n);
                // well clear, or far behind the plane (the other side of a branch): not this plane's business
                if (h >= kTouch * clearance || h < -4f * clearance - 0.3f * Body.SizeScale) continue;
                if ((rel - n * h).sqrMagnitude > pl.radius * pl.radius) continue;   // outside the plane's patch
                if ((Dbg & 16) == 0 && arm >= 0 && havePrev && arm < frameArms.Length && OctoMath.Dot(frameArms[arm] - pl.point, n) < -clearance) continue;
            }
            if (lattice >= 0 && pl.owner != lattice && havePrev && OctoMath.Dot(frameLattice[lattice] - pl.point, pl.normal) < -clearance) continue;
            // a resting touch keeps the plane alive; one that has to shove the particle hard every tick is fighting
            // the arm's shape - a flat plane claiming air beside a branch - and is left to lapse (a real surface
            // is found again by the probes and sweeps)
            if (h > 0.5f * clearance) planeTouched[p] = true;
            if (h >= clearance) continue;
            float push = clearance - h;
            Vector3 v = x - prev;
            x += n * push;
            // remove the velocity into the surface, keep the tangential part (sliding; static friction below)
            float vn = OctoMath.Dot(v, n);
            prev += n * (push + (vn < 0f ? vn : 0f));
            // static friction (Coulomb, mu = kStatic): a particle pressed onto the surface does not creep along
            // it - its slide over this tick, if smaller than kStatic x the press, is taken back. Without it a
            // loaded hold crept a few mm/s for ever under the body's weight and the octopus never slept.
            if ((Dbg & 32) == 0 && havePrev && (lattice >= 0 || arm >= 0) && (arm < 0 || arm < frameArms.Length))
            {
                Vector3 start = lattice >= 0 ? frameLattice[lattice] : frameArms[arm];
                Vector3 slide = x - start;
                slide -= n * OctoMath.Dot(slide, n);
                float sl = slide.magnitude;
                if (sl > 1e-9f)
                {
                    float back = Mathf.Min(sl, kStatic * push);
                    Vector3 b = slide * (back / sl);
                    x -= b; prev -= b;
                }
            }
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
