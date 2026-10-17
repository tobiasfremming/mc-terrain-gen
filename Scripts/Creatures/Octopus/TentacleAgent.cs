using UnityEngine;

// One arm's own mind (tree octopus design 2.2, user decision 2). It reads the brain's intent, the blackboard
// and its own memory, and decides for itself where to go; it moves the arm only through the chain's muscles
// (target shape, lengths, tone, sinus) and pins - and every pin is a probe answer on real geometry.
//
//   Curl     resting curl; free arms idle here when enough others hold on
//   Feel     an exploratory sweep in the arm's own cone; a tactile cast from the tip every frame, a cast from
//            the root every ExploreSeconds (alternately along a branch hint when there are hints); hits enter
//            a memory of 4 scored candidates; a touch grips at once
//   Reach    tip-led to the best candidate (FABRIK intent + sinus, stretching if needed); a miss times out
//   Grip     WrapMarch plans the pins (wrap a thin branch, spread on a thick trunk); they zip on tip -> base
//   Anchored holds; with Travel it shortens to pull the body toward an anchor ahead, lengthens and wants to
//            let go of one behind
//   Release  peels the pins off base -> tip, only while the remaining support meets the minimum
//   Strike   shoots straight at the predicted intercept (stretch to max, full tone)
//   Hold     wraps the prey's grab volume and follows it (pins re-placed every frame)
public sealed class TentacleAgent
{
    public enum State { Curl, Feel, Reach, Grip, Anchored, Release, Strike, Hold }

    public const int MemorySize = 4;
    public const float ExploreSeconds = 0.25f;
    public const float ReachTimeout = 2.2f;
    public const float ZipSeconds = 0.32f;
    public const float PeelStep = 0.03f;
    public const float DrapeSeconds = 0.5f;

    struct Candidate { public Vector3 point, normal; public int source; public float time; public bool valid, hinted; }

    public readonly int Index;
    public readonly TentacleChain Arm;
    public State Mode { get; private set; }
    public float StateTime { get; private set; }
    public int Grips, GripPlans, Releases, ReleasesStarted, Strikes, Misses;      // cumulative, for tests and stats
    public Vector3 TargetPoint { get; private set; }  // what it is reaching for (debug view)
    public bool HasTarget { get; private set; }

    readonly Candidate[] memory = new Candidate[MemorySize];
    readonly WrapMarch wrap;
    readonly Vector3 restDirLocal;                     // the arm's rest direction in body space
    readonly BranchHint[] hintScratch = new BranchHint[4];
    OctopusController owner;
    readonly float[] drapeUntil;
    uint rng;
    float nextExplore, zipStart, anchoredAt, peelNext, holdUntil;
    int zipDone, best = -1, exploreCount;
    Vector3 reachGoal;
    bool reachGoalFresh = true;
    bool wantsRelease;
    IPreyTarget heldPrey;

    public TentacleAgent(int index, TentacleChain arm, Vector3 restDirLocal, uint seed)
    {
        Index = index; Arm = arm; this.restDirLocal = restDirLocal;
        wrap = new WrapMarch(arm.N);
        drapeUntil = new float[arm.N];
        rng = seed * 2654435761u + 0x9E3779B9u + (uint)index * 0x85EBCA6Bu;
        if (rng == 0) rng = 1;
        Mode = State.Curl;
    }

    public bool Holding => Mode == State.Hold;
    public IPreyTarget HeldPrey => heldPrey;

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

    void Enter(State s)
    {
        Mode = s; StateTime = 0f;
        // the positional pull belongs to reaching and striking only
        if (s != State.Reach && s != State.Strike) Arm.ShapePull = s == State.Feel ? 0.01f : 0f;
    }

    // ------------------------------------------------------------------ the tick

    public void Tick(float dt, OctopusController c)
    {
        StateTime += dt;
        owner = c;
        ArmBlackboard board = c.Board;
        UpdateBoard(board);
        ExpireDrapes(c.Time);
        float ts = c.Body.TimeScale;

        // strikes pre-empt everything (the arm qualifies itself)
        if (c.Intent.mode == OctoMode.Strike && Mode != State.Strike && Mode != State.Hold && QualifiesForStrike(c)) BeginStrike(c);

        switch (Mode)
        {
            case State.Curl: TickCurl(c); break;
            case State.Feel: TickFeel(dt, c); break;
            case State.Reach: TickReach(c, ts); break;
            case State.Grip: TickGrip(c, ts); break;
            case State.Anchored: TickAnchored(c, ts); break;
            case State.Release: TickRelease(c, ts); break;
            case State.Strike: TickStrike(c, ts); break;
            case State.Hold: TickHold(c); break;
        }
        UpdateBoard(board);
    }

    void UpdateBoard(ArmBlackboard b)
    {
        int pins = 0; Vector3 sum = Vector3.zero;
        for (int k = 1; k < Arm.N; k++) if (Arm.Pinned[k] && drapeUntil[k] <= 0f) { pins++; sum += Arm.PinPoint[k]; }
        // an arm that is letting go is no longer support: the next arm to decide sees one anchor fewer
        b.Anchored[Index] = pins > 0 && (Mode == State.Anchored || Mode == State.Grip);
        if (pins > 0) b.Anchor[Index] = sum / pins;
        b.Load[Index] = Arm.Overload;
        b.Claiming[Index] = Mode == State.Reach && HasTarget;
        b.ClaimPoint[Index] = TargetPoint;
        b.Striking[Index] = Mode == State.Strike;
        b.Recount();
    }

    // ------------------------------------------------------------------ states

    void TickCurl(OctopusController c)
    {
        Arm.ClearTargetShape(); Arm.SinusAmount = 0f; Arm.Tone = 1f;
        if (StateTime < 0.3f) return;
        // idle while enough others hold on and the brain only wants rest; else start feeling
        bool rest = c.Intent.mode == OctoMode.Rest || c.Intent.mode == OctoMode.Hold;
        if (!rest || c.Board.AnchoredCount < c.RestAnchors) Enter(State.Feel);
    }

    void TickFeel(float dt, OctopusController c)
    {
        Arm.Tone = 1f; Arm.SinusAmount = 0.6f;
        Vector3 root = Arm.X[0];
        Vector3 axis = ConeAxis(c);
        float L = Arm.Length;
        // the exploratory curl path: a slowly wandering point in the cone at 0.7 L
        float ph = c.Time * 0.6f / c.Body.TimeScale + Index * 1.7f;
        Vector3 wander = Turn(axis, 0.55f * Mathf.Sin(ph), 0.45f * Mathf.Sin(1.3f * ph + 0.7f));
        Arm.SetTargetShape(root + wander * (0.7f * L), 0.55f, false);

        // touch: the tip feels what it brushes against, and grips it
        if (TactileTouch(c, out SurfaceHit touch) && Acceptable(c, touch.point))
        {
            Remember(touch.point, touch.normal, touch.source, c.Time, false);
            BeginGrip(c, touch);
            return;
        }

        if (c.Time >= nextExplore && c.Spare(2))
        {
            nextExplore = c.Time + ExploreSeconds * c.Body.TimeScale * (0.85f + 0.3f * Rand());
            exploreCount++;
            if (c.Hints != null && (exploreCount & 1) == 0) ExploreHint(c, root, axis, L);
            else ExploreCast(c, root, axis, L);
        }

        best = BestCandidate(c);
        if (best >= 0 && StateTime > 0.15f)
        {
            TargetPoint = memory[best].point; HasTarget = true;
            reachGoalFresh = true;
            Enter(State.Reach);
        }
    }

    void TickReach(OctopusController c, float ts)
    {
        if (best < 0 || !memory[best].valid) { HasTarget = false; Enter(State.Feel); return; }
        Candidate m = memory[best];
        float tipR = Arm.Spec.TipRadius * Arm.SizeScale;
        Vector3 target = m.point + m.normal * (2f * tipR);
        float w = Mathf.Clamp01(StateTime / (0.35f * ts));
        // the reach is tip-led at the arm's reach speed: the goal the tip is pulled to advances from where the
        // tip was toward the target (an arm does not whip to its hold)
        if (StateTime <= 0f || reachGoalFresh) { reachGoal = Arm.X[Arm.N - 1]; reachGoalFresh = false; }
        float step = Arm.Spec.ReachSpeed * Mathf.Sqrt(Arm.SizeScale) * c.LastDt;
        Vector3 toT = target - reachGoal;
        float tl = toT.magnitude;
        reachGoal = tl <= step ? target : reachGoal + toT * (step / tl);
        bool far = (target - Arm.X[0]).magnitude > Arm.Length;
        Arm.SetTargetShape(reachGoal, w, far);
        Arm.Tone = 1.6f; Arm.SinusAmount = 0.3f * (1f - w);
        Arm.ShapePull = 0.04f * w;
        float capture = 0.06f * Arm.SizeScale + 3f * tipR;
        Vector3 tip = Arm.X[Arm.N - 1];
        if ((tip - target).sqrMagnitude < capture * capture || (TactileTouch(c, out SurfaceHit touch) && (touch.point - m.point).magnitude < 0.25f * Arm.Length))
        {
            // the real contact under the tip, preferring the remembered surface
            if (!c.Probe(tip + m.normal * (0.1f * Arm.SizeScale), m.normal, 0.3f * Arm.SizeScale, m.source, Index, out SurfaceHit contact))
                contact = new SurfaceHit { point = m.point, normal = m.normal, source = m.source };
            BeginGrip(c, contact);
            return;
        }
        if (StateTime > ReachTimeout * ts)
        {
            memory[best].valid = false; Misses++;
            HasTarget = false;
            Enter(State.Feel);
        }
    }

    // Spawning or settling: lay the arm straight from its root onto a probed contact (within reach) and grip
    // it at once. False if out of reach.
    public bool GripAt(OctopusController c, SurfaceHit contact)
    {
        Vector3 root = Arm.X[0], d = contact.point + contact.normal * (Arm.Spec.TipRadius * Arm.SizeScale) - root;
        float dist = d.magnitude;
        if (dist > Arm.Length * 1.02f || dist < 1e-3f) return false;
        Vector3 dir = d / dist;
        // a gentle arc of the arm's length ending at the contact: straight when taut, bowed when not
        Vector3 side = OctoMath.Norm(OctoMath.Cross(dir, c.Up), Vector3.right), bow = OctoMath.Cross(side, dir);
        float slack = Mathf.Sqrt(Mathf.Max(0f, Arm.Length * Arm.Length - dist * dist)) * 0.35f;
        for (int k = 0; k < Arm.N; k++)
        {
            float t = (float)k / (Arm.N - 1);
            Arm.X[k] = root + d * t + bow * (slack * 4f * t * (1f - t));
            Arm.Prev[k] = Arm.X[k];
        }
        Arm.UpdateFrames(0f);
        Remember(contact.point, contact.normal, contact.source, c.Time, false);
        BeginGrip(c, contact, true);
        if (Mode != State.Grip) return false;
        for (; zipDone < wrap.Count; zipDone++) Arm.Pin(wrap.Particle[zipDone], wrap.Point[zipDone], wrap.Normal[zipDone], wrap.Source[zipDone]);
        Arm.ClearTargetShape();
        Grips++; anchoredAt = c.Time; wantsRelease = false; HasTarget = false;
        Enter(State.Anchored);
        UpdateBoard(c.Board);   // the next arm to settle sees this hold
        return true;
    }

    void BeginGrip(OctopusController c, SurfaceHit contact, bool force = false)
    {
        if (!force && !c.TryTakeGripPlan()) { if (Mode != State.Reach) { TargetPoint = contact.point; HasTarget = true; Remember(contact.point, contact.normal, contact.source, c.Time, false); best = BestCandidate(c); if (best >= 0) Enter(State.Reach); } return; }
        int tip = Arm.N - 1;
        Vector3 tipDir = (Arm.X[tip] - Arm.X[tip - 1]).normalized;
        GripPlans++;
        wrap.Plan(c.SurfaceProbe, Arm, contact, tip, tipDir, Arm.X[0], 6, c.SlotOf(Index));
        c.CountProbes(wrap.Probes); wrap.Probes = 0;
        if (wrap.Count == 0) { Enter(State.Feel); return; }
        zipStart = c.Time; zipDone = 0;
        HasTarget = true; TargetPoint = contact.point;
        Enter(State.Grip);
    }

    void TickGrip(OctopusController c, float ts)
    {
        Arm.Tone = 1f; Arm.SinusAmount = 0f;
        float t = (c.Time - zipStart) / (ZipSeconds * ts);
        int due = Mathf.Min(wrap.Count, 1 + (int)(t * wrap.Count));
        for (; zipDone < due; zipDone++)
            Arm.Pin(wrap.Particle[zipDone], wrap.Point[zipDone], wrap.Normal[zipDone], wrap.Source[zipDone]);
        if (zipDone >= wrap.Count)
        {
            Arm.ClearTargetShape();
            Grips++; anchoredAt = c.Time; wantsRelease = false;
            HasTarget = false;
            Enter(State.Anchored);
        }
    }

    void TickAnchored(OctopusController c, float ts)
    {
        Arm.SinusAmount = 0f; Arm.Tone = 1f;
        if (PinCount() == 0) { Enter(State.Feel); return; }   // the body let this grip go (overload)
        OctoIntent it = c.Intent;
        Vector3 centre = c.Body.HeadCentre, anchor = c.Board.Anchor[Index];
        float lt = 1f;
        wantsRelease = false;
        if (it.mode == OctoMode.Travel || it.mode == OctoMode.Stalk || it.mode == OctoMode.Retreat)
        {
            Vector3 a = anchor - centre, g = it.goal - centre;
            float align = Vector3.Dot(a.normalized, g.normalized);
            float urgency = Mathf.Clamp01(it.urgency);
            if (align > 0.25f) lt = Mathf.Lerp(1f, Arm.Spec.MinStretch, 0.5f + 0.5f * urgency);   // pull toward it
            else if (align < -0.05f) { lt = 1.12f; wantsRelease = true; }                          // behind: let it go
            // a goal that is already reached needs no pulling
            if (g.magnitude < 0.15f * Arm.Length) { lt = 1f; wantsRelease = false; }
        }
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = Arm.Pinned[j + 1] ? 1f : lt;
        if (wantsRelease && c.Time - anchoredAt > 0.6f * ts && c.Board.CanRelease(Index)) BeginRelease(c);
    }

    void BeginRelease(OctopusController c) { peelNext = c.Time; ReleasesStarted++; Enter(State.Release); }

    void TickRelease(OctopusController c, float ts)
    {
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = 1f;
        if (c.Time < peelNext) return;
        peelNext = c.Time + PeelStep * ts;
        for (int k = 1; k < Arm.N; k++)
            if (Arm.Pinned[k]) { Arm.Unpin(k); drapeUntil[k] = 0f; break; }
        if (PinCount() == 0) { Releases++; ForgetNear(c.Board.Anchor[Index], 0.3f * Arm.Length); Enter(State.Curl); }
    }

    bool QualifiesForStrike(OctopusController c)
    {
        OctoIntent it = c.Intent;
        if (it.prey == null || !it.prey.CanBeGrabbed) return false;
        if (c.Board.StrikeClaims >= Mathf.Max(1, it.strikers)) return false;
        if ((it.intercept - Arm.X[0]).magnitude > Arm.Spec.MaxStretch * Arm.Length * 0.95f) return false;
        if (Mode == State.Anchored || Mode == State.Grip || Mode == State.Release) return c.Board.CanRelease(Index);
        return true;
    }

    void BeginStrike(OctopusController c)
    {
        Arm.UnpinAll();
        for (int k = 0; k < Arm.N; k++) drapeUntil[k] = 0f;
        Strikes++;
        Enter(State.Strike);
        c.Board.Striking[Index] = true; c.Board.Recount();
    }

    void TickStrike(OctopusController c, float ts)
    {
        OctoIntent it = c.Intent;
        IPreyTarget prey = it.prey;
        Arm.Tone = 3f; Arm.SinusAmount = 0f;
        Arm.SetTargetShape(it.intercept, 1f, true);
        Arm.ShapePull = 0.25f;
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = Arm.Spec.MaxStretch;
        if (prey != null && prey.CanBeGrabbed)
        {
            prey.GrabVolume(out Vector3 a, out Vector3 b, out float r);
            Vector3 tip = Arm.X[Arm.N - 1];
            if (DistanceToSegment(tip, a, b) < r + 0.08f * Arm.SizeScale)
            {
                heldPrey = prey;
                holdUntil = c.Time + c.HoldSeconds;
                c.NotifyGrab(prey);
                Enter(State.Hold);
                return;
            }
        }
        if (StateTime > 2.5f * Arm.Spec.StrikeTime * ts) { Misses++; Recover(); }
    }

    void TickHold(OctopusController c)
    {
        IPreyTarget prey = heldPrey;
        if (prey == null || c.Time > holdUntil || c.Intent.mode == OctoMode.Retreat)
        {
            if (prey != null) c.NotifyRelease(prey);
            heldPrey = null;
            Recover();
            return;
        }
        prey.GrabVolume(out Vector3 a, out Vector3 b, out float r);
        Vector3 tip = Arm.X[Arm.N - 1];
        Vector3 q = ClosestOnSegment(tip, a, b);
        Vector3 contact = OctoMath.Norm(tip - q, Vector3.up);
        int avail = Mathf.Min(Arm.N - 8, 14);
        wrap.PlanAroundCapsule(Arm, Arm.N - 1, a, b, r, contact, 1f, avail);
        for (int i = 0; i < wrap.Count; i++) Arm.Pin(wrap.Particle[i], wrap.Point[i], wrap.Normal[i], SurfaceHit.NoSource);
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = Arm.Pinned[j + 1] ? 1f : 0.95f;
    }

    void Recover()
    {
        Arm.UnpinAll();
        Arm.ClearTargetShape();
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = 1f;
        Arm.Tone = 1f;
        Enter(State.Curl);
    }

    // ------------------------------------------------------------------ perception

    // The arm's own reach cone: its rest direction (body-rotated), drawn toward the goal when travelling and
    // a little upward (trees).
    Vector3 ConeAxis(OctopusController c)
    {
        Vector3 rest = OctoMath.Rotate(c.Body.Orientation, restDirLocal);
        Vector3 up = c.Up;
        Vector3 axis = rest + up * 0.6f;
        // near the enclosure edge the arms explore inward
        if (!float.IsInfinity(c.EnclosureRadius))
        {
            float edge = c.EnclosureRadius - c.HorizontalDistance(Arm.X[0]);
            if (edge < 1.5f * Arm.Length)
            {
                Vector3 inward = c.EnclosureCentre - Arm.X[0];
                inward -= up * OctoMath.Dot(inward, up);
                axis += OctoMath.Norm(inward, Vector3.zero) * (2f * (1f - Mathf.Clamp01(edge / (1.5f * Arm.Length))));
            }
        }
        OctoIntent it = c.Intent;
        if (it.mode == OctoMode.Travel || it.mode == OctoMode.Stalk || it.mode == OctoMode.Retreat)
        {
            Vector3 g = it.goal - c.Body.HeadCentre;
            if (g.sqrMagnitude > 1e-6f) axis += g.normalized * (0.8f + Mathf.Clamp01(it.urgency));
        }
        return OctoMath.Norm(axis, up);
    }

    // `axis` turned by yaw / pitch radians in a frame around it.
    static Vector3 Turn(Vector3 axis, float yaw, float pitch)
    {
        Vector3 x = OctoMath.Norm(OctoMath.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.right);
        Vector3 y = OctoMath.Cross(axis, x);
        return OctoMath.Norm(axis + x * Mathf.Tan(yaw) + y * Mathf.Tan(pitch), axis);
    }

    bool TactileTouch(OctopusController c, out SurfaceHit hit)
    {
        int n = Arm.N;
        Vector3 tip = Arm.X[n - 1], dir = Arm.X[n - 1] - Arm.X[n - 2];
        return c.Cast(Arm.X[n - 2], dir, 2.5f * Arm.Spacing, out hit);
    }

    void ExploreCast(OctopusController c, Vector3 root, Vector3 axis, float L)
    {
        Vector3 dir = Turn(axis, (Rand() * 2f - 1f) * 0.95f, (Rand() * 2f - 1f) * 0.8f);
        if (c.Cast(root, dir, 0.95f * L, out SurfaceHit h) && h.distance > 0.3f * L)
            Remember(h.point, h.normal, h.source, c.Time, false);
    }

    void ExploreHint(OctopusController c, Vector3 root, Vector3 axis, float L)
    {
        float tipR = Arm.Spec.TipRadius * Arm.SizeScale;
        int n = c.Hints.Query(root, axis, Mathf.Cos(75f * Mathf.Deg2Rad), 0.35f * L, 0.95f * L, 1.5f * tipR, 2f * L, hintScratch);
        if (n == 0) { ExploreCast(c, root, axis, L); return; }
        BranchHint hnt = hintScratch[(int)(Rand() * n) % n];
        Vector3 d = hnt.point - root;
        // confirm on the real surface: a cast at the hinted branch
        if (c.Cast(root, d, d.magnitude + hnt.radius + 0.2f * L, out SurfaceHit h) && h.distance > 0.3f * L)
            Remember(h.point, h.normal, h.source, c.Time, true);
    }

    bool Acceptable(OctopusController c, Vector3 p)
    {
        if (c.Board.Crowding(Index, p, 0.12f * Arm.Length) > 0.3f) return false;   // another arm holds there
        OctoIntent it = c.Intent;
        if (it.mode != OctoMode.Travel && it.mode != OctoMode.Retreat) return true;
        Vector3 g = it.goal - c.Body.HeadCentre, a = p - c.Body.HeadCentre;
        return Vector3.Dot(a, g) > -0.2f * a.magnitude * g.magnitude;   // not clearly behind
    }

    void Remember(Vector3 p, Vector3 n, int source, float time, bool hinted)
    {
        if (owner != null && !owner.HoldInside(p)) return;   // holds stay inside the enclosure
        // replace a duplicate, else an empty slot, else the oldest
        int slot = -1;
        for (int i = 0; i < MemorySize; i++) if (memory[i].valid && (memory[i].point - p).sqrMagnitude < 0.01f) { slot = i; break; }
        if (slot < 0) for (int i = 0; i < MemorySize; i++) if (!memory[i].valid) { slot = i; break; }
        if (slot < 0)
        {
            slot = 0;
            for (int i = 1; i < MemorySize; i++) if (memory[i].time < memory[slot].time) slot = i;
        }
        memory[slot] = new Candidate { point = p, normal = n, source = source, time = time, valid = true, hinted = hinted };
    }

    void ForgetNear(Vector3 p, float r)
    {
        for (int i = 0; i < MemorySize; i++) if (memory[i].valid && (memory[i].point - p).sqrMagnitude < r * r) memory[i].valid = false;
    }

    // Score (higher is better): reach fit, progress toward the goal, line of sight from the root, spacing
    // from the other arms, freshness, a small bonus for hint-confirmed branches. -1 = none acceptable.
    int BestCandidate(OctopusController c)
    {
        int bi = -1; float bs = 0.15f;
        Vector3 root = Arm.X[0], centre = c.Body.HeadCentre;
        float L = Arm.Length;
        OctoIntent it = c.Intent;
        bool travel = it.mode == OctoMode.Travel || it.mode == OctoMode.Stalk || it.mode == OctoMode.Retreat;
        Vector3 g = it.goal - centre;
        float gl = g.magnitude;
        for (int i = 0; i < MemorySize; i++)
        {
            if (!memory[i].valid) continue;
            Candidate m = memory[i];
            float age = c.Time - m.time;
            if (age > 6f) { memory[i].valid = false; continue; }
            Vector3 d = m.point - root;
            float dist = d.magnitude / L;
            if (dist < 0.25f || dist > 1.25f) continue;
            float s = 1f - Mathf.Abs(dist - 0.65f) / 0.45f;
            if (travel && gl > 1e-3f)
            {
                Vector3 a = m.point - centre;
                float prog = Vector3.Dot(a, g) / (gl * Mathf.Max(a.magnitude, 1e-3f));
                s += (0.6f + Mathf.Clamp01(it.urgency)) * prog;
            }
            else s += 0.3f * Vector3.Dot(m.point - centre, c.Up) / L;   // at rest: prefer holds above
            s -= 1.2f * c.Board.Crowding(Index, m.point, 0.22f * L);
            s -= 0.08f * age;
            if (m.hinted) s += 0.1f;
            if (s <= bs) continue;
            // line of sight from the root (one cast, only for the current front-runner)
            if (c.Cast(root, d, d.magnitude - 0.04f * L, out SurfaceHit blk) && (blk.point - m.point).magnitude > 0.1f * L) { memory[i].valid = false; continue; }
            bs = s; bi = i;
        }
        return bi;
    }

    // ------------------------------------------------------------------ collision drape

    // A free arm segment that would pass through a surface: its far particle is pinned on the surface for a
    // moment (the arm drapes over the branch instead of passing through it).
    public void Drape(int k, SurfaceHit hit, float time)
    {
        if (k <= 0 || k >= Arm.N || Mode == State.Strike || Mode == State.Hold) return;
        float rk = Arm.Spec.RadiusAt(Arm.Spec.Spacing * k) * Arm.SizeScale;
        Arm.Pin(k, hit.point + hit.normal * rk, hit.normal, hit.source);
        drapeUntil[k] = time + DrapeSeconds;
    }

    public bool IsFreeArm => Mode == State.Curl || Mode == State.Feel || Mode == State.Reach || Mode == State.Strike;

    void ExpireDrapes(float time)
    {
        for (int k = 1; k < Arm.N; k++)
            if (drapeUntil[k] > 0f && time > drapeUntil[k]) { drapeUntil[k] = 0f; if (Mode != State.Anchored && Mode != State.Grip && Mode != State.Release && Mode != State.Hold) Arm.Unpin(k); }
    }

    int PinCount()
    {
        int n = 0;
        for (int k = 1; k < Arm.N; k++) if (Arm.Pinned[k] && drapeUntil[k] <= 0f) n++;
        return n;
    }

    static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
        return a + ab * t;
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b) => (p - ClosestOnSegment(p, a, b)).magnitude;
}
