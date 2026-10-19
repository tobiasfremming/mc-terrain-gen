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
//
// What the arm is FOR comes from the octopus (ArmCoordinator, user decision 5): its role on the blackboard.
// Grip feels and reaches toward the route and, anchored ahead, pulls; Anchor finds or keeps a support hold and
// never pulls; Feel explores and grips only when allowed; Hang / Tuck / Rest / Groom hold nothing (a hold the
// role does not need is let go, under the support rule). Every anchored run takes its length from the one
// body motion the octopus wants (DesiredVelocity): only a Grip arm holding ahead shortens, every other hold
// only pays out - no two arms pull the body toward different places. The arm reports Failed (a reach that
// keeps missing, a search that finds nothing) so the coordinator can reassign it.
public sealed class TentacleAgent
{
    public const float RestRelaxRate = 4f;   // 1/s at scale 1: a curled arm's extra damping at rest
    public enum State { Curl, Feel, Reach, Grip, Anchored, Release, Strike, Hold, Groom }

    public const int MemorySize = 4;
    public const float ExploreSeconds = 0.25f;
    public const float ReachTimeout = 2.2f;
    public const float ZipSeconds = 0.32f;
    public const float PeelStep = 0.03f;
    public const float DrapeSeconds = 0.5f;

    struct Candidate { public Vector3 point, normal; public int source; public float time; public bool valid, hinted, stem; }

    public readonly int Index;
    public readonly TentacleChain Arm;
    public State Mode { get; private set; }
    public float StateTime { get; private set; }
    public int ExploreCasts, ExploreHits;   // cumulative (swing casts)
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
    int zipDone, best = -1, exploreCount, missesInRow;
    Vector3 reachGoal;
    bool reachGoalFresh = true;
    bool wantsRelease;
    IPreyTarget heldPrey;

    public TentacleAgent(int index, TentacleChain arm, Vector3 restDirLocal, uint seed)
    {
        Index = index; Arm = arm; this.restDirLocal = restDirLocal;
        wrap = new WrapMarch(arm.N);
        drapeUntil = new float[arm.N];
        haulWas = new bool[arm.N];
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
        Arm.ExtraDamping = 0f;
        // the muscles ease into a new shape, except a strike and a hold, which follow their prey at once
        Arm.ShapeRate = s == State.Strike || s == State.Hold ? TentacleChain.FastShapeRate : TentacleChain.DefaultShapeRate;
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
        if (c.Intent.mode == OctoMode.Strike && board.Role[Index] == ArmRole.Strike && Mode != State.Strike && Mode != State.Hold && QualifiesForStrike(c)) BeginStrike(c);

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
            case State.Groom: TickGroom(c, ts); break;
        }
        UpdateBoard(board);
    }

    void UpdateBoard(ArmBlackboard b)
    {
        int pins = 0; Vector3 sum = Vector3.zero;
        for (int k = 1; k < Arm.N; k++) if (Arm.Pinned[k] && drapeUntil[k] <= 0f) { pins++; sum += Arm.PinGoal[k]; }
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

    ArmRole RoleOf(OctopusController c) => c.Board.Role[Index];

    // roles that look for a hold to commit to
    bool SeeksHold(OctopusController c)
    {
        ArmRole r = RoleOf(c);
        if (Swinging(c)) return c.Board.Command[Index].allowGrip;   // the group arm seeks when the octopus says
        return r == ArmRole.Grip || r == ArmRole.Anchor || r == ArmRole.Feel && c.Board.Command[Index].allowGrip;
    }

    void Report(OctopusController c, ArmReport r) => c.Board.Report[Index] = r;

    // a grip-group arm of a swinging traversal (ArmCoordinator): its holds' lengths and its letting go come from
    // the octopus (the swing), the search for a hold ahead is its own
    bool Swinging(OctopusController c) => c.Board.Command[Index].swing && RoleOf(c) == ArmRole.Grip;

    // a swinging arm of the group the body hangs from, seeking a hold: support beside the pivot, not a catch ahead
    // (a pivot arm that caught ahead spread the pivot over two branches and the body hung between them)
    bool PivotSupport(OctopusController c) => Swinging(c) && c.Board.Command[Index].group == c.Board.PivotGroup;

    public const float HangTone = 0.25f, TuckPull = 0.03f;

    void TickCurl(OctopusController c)
    {
        ArmRole role = RoleOf(c);
        bool resting = c.Intent.mode == OctoMode.Rest || c.Intent.mode == OctoMode.Hold;
        Arm.SinusAmount = 0f;
        switch (role)
        {
            case ArmRole.Hang:
                if (c.Coordinator.Pumping)
                {
                    // pumping a swing: the hanging arms kick fore and aft with it (a child's legs on a swing) - the
                    // shift of their weight seeds the swing that the holds' pumping then builds
                    SwingPhase sw = c.Coordinator.Swing;
                    // in phase with the swing once there is one; from stillness (or a body held off to one side) at
                    // the swing's natural frequency, which starts one
                    bool moving = sw.Valid && Mathf.Abs(sw.Omega) > 0.15f * sw.Natural * Mathf.Max(sw.Amplitude, 0.05f) && sw.Amplitude > 3f * Mathf.Deg2Rad;
                    float k = moving ? Mathf.Clamp(sw.Theta / sw.Amplitude, -1f, 1f) : Mathf.Sin(sw.Natural * c.Time);
                    Vector3 kick = OctoMath.Norm(-c.Up * 0.8f + c.Board.SwingRoute * (KickReach * k), -c.Up);
                    Arm.SetTargetShape(Arm.X[0] + kick * (0.8f * Arm.Length), 0.7f, false);
                    Arm.Tone = 1f;
                    kicking = true;
                    break;
                }
                // limp: the arm drapes down under its own weight
                if (kicking || Arm.Tone != HangTone) { Arm.ClearTargetShape(); kicking = false; }
                Arm.Tone = HangTone;
                break;
            case ArmRole.Tuck:
                // curled compactly under the body: the arm coils toward a point just below the crown on its own side
                // (the muscle shape plus a light positional pull, as a reach has)
                {
                    Vector3 down = -c.Up, side = OctoMath.Rotate(c.Body.Orientation, restDirLocal);
                    Vector3 t = Arm.X[0] + down * (0.22f * Arm.Length) + side * (0.1f * Arm.Length);
                    // re-aimed a few times a second (it follows the body; a shape set every tick never lets it sleep)
                    if (!tuckSet || c.Time >= tuckNext) { Arm.SetTargetShape(t, 1f, false); tuckSet = true; tuckNext = c.Time + 0.3f * c.Body.TimeScale; }
                    Arm.Tone = 1.6f; Arm.ShapePull = TuckPull;
                }
                break;
            default:
                Arm.ClearTargetShape(); Arm.Tone = 1f;
                break;
        }
        if (role != ArmRole.Tuck) { if (tuckSet) Arm.ShapePull = 0f; tuckSet = false; }
        if (StateTime < 0.3f) return;
        // a curled arm at rest relaxes: its swing dies out in about a second instead of ringing on for 20 s
        // (absolute damping is safe here - a resting octopus hangs still; Froude-scaled like every rate)
        float want = resting ? RestRelaxRate / c.Body.TimeScale : 0f;
        if (Arm.ExtraDamping != want) Arm.ExtraDamping = want;
        if (role == ArmRole.Groom) { BeginGroom(c); return; }
        if (SeeksHold(c) || role == ArmRole.Feel) Enter(State.Feel);
    }

    bool tuckSet, kicking;
    public const float KickReach = 1.2f;   // fore-aft reach of a pumping kick (x down)
    float tuckNext;

    public const float RestSearchSeconds = 6f, GripSearchSeconds = 5f;   // x TimeScale: a search that finds nothing fails

    void TickFeel(float dt, OctopusController c)
    {
        ArmRole role = RoleOf(c);
        if (role != ArmRole.Grip && role != ArmRole.Anchor && role != ArmRole.Feel) { HasTarget = false; Enter(State.Curl); return; }
        if (Swinging(c) && !SeeksHold(c)) { HasTarget = false; Enter(State.Curl); return; }
        bool commit = SeeksHold(c);
        Arm.Tone = 1f; Arm.SinusAmount = 0.6f;
        Vector3 root = Arm.X[0];
        Vector3 axis = ConeAxis(c);
        float L = Arm.Length;
        if (Swinging(c) && c.Board.HasSwingTarget)
        {
            // a swing's catch: the arm reaches out toward the route's next waypoint (purposeful, no wandering)
            Vector3 toW = c.Board.SwingTarget - root;
            float tw = toW.magnitude;
            Arm.SetTargetShape(root + toW * (Mathf.Min(tw, 0.85f * L) / Mathf.Max(tw, 1e-4f)), 0.6f, false);
            Arm.SinusAmount = 0.15f;
        }
        else
        {
            // the exploratory curl path: a slowly wandering point in the cone at 0.7 L
            float ph = c.Time * 0.6f / c.Body.TimeScale + Index * 1.7f;
            Vector3 wander = Turn(axis, 0.55f * Mathf.Sin(ph), 0.45f * Mathf.Sin(1.3f * ph + 0.7f));
            Arm.SetTargetShape(root + wander * (0.7f * L), 0.55f, false);
        }

        // touch: the tip feels what it brushes against, and grips it (if its role lets it commit)
        if (TactileTouch(c, out SurfaceHit touch) && Acceptable(c, touch.point))
        {
            Remember(touch.point, touch.normal, touch.source, c.Time, false);
            if (commit) { BeginGrip(c, touch); return; }
        }

        if (c.Time >= nextExplore && c.Spare(2))
        {
            nextExplore = c.Time + ExploreSeconds * c.Body.TimeScale * (0.85f + 0.3f * Rand());
            exploreCount++;
            if (c.Hints != null && (exploreCount & 1) == 0) ExploreHint(c, root, axis, L);
            else if (Swinging(c) && (exploreCount % 3) != 0) ExploreSwing(c, root, L);
            else if (Climbing(c) && (exploreCount % 3) != 0) ExploreClimb(c, root, L);
            else ExploreCast(c, root, axis, L);
        }

        // a search that finds nothing fails: at rest after RestSearchSeconds (an octopus does not sweep forever),
        // a grip arm in the void after GripSearchSeconds; the coordinator reassigns it
        bool resting = c.Intent.mode == OctoMode.Rest || c.Intent.mode == OctoMode.Hold;
        float limit = (resting ? RestSearchSeconds : GripSearchSeconds) * c.Body.TimeScale;
        if (commit && StateTime > limit && (resting || role == ArmRole.Grip))
        {
            Report(c, ArmReport.Failed);
            Enter(State.Curl);
            return;
        }

        if (!commit) return;
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
        if (!SeeksHold(c)) { HasTarget = false; Enter(State.Curl); return; }
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
            // two misses running: this arm cannot do its job from here
            if (++missesInRow >= 2) { missesInRow = 0; Report(c, ArmReport.Failed); Enter(State.Curl); }
            else Enter(State.Feel);
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
        for (; zipDone < wrap.Count; zipDone++)
        {
            if (zipDone > 0 && !PinReachable(wrap.Particle[zipDone], wrap.Point[zipDone])) break;
            Arm.Pin(wrap.Particle[zipDone], wrap.Point[zipDone], wrap.Normal[zipDone], wrap.Source[zipDone]);
        }
        zipDone = wrap.Count;
        Arm.ClearTargetShape();
        HoldRun(c, false, true);
        Grips++; anchoredAt = c.Time; wantsRelease = false; HasTarget = false; missesInRow = 0;
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
        // a zip is compliant: at full tone the arm bent toward its reach shape against the pins it had laid, and
        // the reaction yanked the body at the catch (up to 250 body weights)
        Arm.Tone = ZipTone; Arm.SinusAmount = 0f;
        float t = (c.Time - zipStart) / (ZipSeconds * ts);
        int due = Mathf.Min(wrap.Count, 1 + (int)(t * wrap.Count));
        for (; zipDone < due; zipDone++)
        {
            // the zip stops where the run from the root could not reach its next pin even at full stretch: pinned
            // anyway, the tether would snatch the body toward the catch (a catch at the edge of reach)
            if (zipDone > 0 && !PinReachable(wrap.Particle[zipDone], wrap.Point[zipDone])) { zipDone = wrap.Count; break; }
            // nor nearer the root than MinRun: a run that short cannot bend from the body to its hold within its
            // joint cones - two of them on either side fought at hundreds of body weights (a catch close to the body)
            if (zipDone > 0 && wrap.Particle[zipDone] < (Swinging(c) ? SwingMinRun : MinRun)) { zipDone = wrap.Count; break; }
            Arm.Pin(wrap.Particle[zipDone], wrap.Point[zipDone], wrap.Normal[zipDone], wrap.Source[zipDone], true);
        }
        // the run between the root and the zip front keeps the length it has (a zip toward the root at the run's
        // target length yanked the body toward the new hold)
        HoldRun(c, false, true);
        if (zipDone >= wrap.Count)
        {
            Arm.ClearTargetShape();
            HoldRun(c, false, true);   // no yank at the catch: the run keeps the length it reached with
            Grips++; anchoredAt = c.Time; wantsRelease = false; missesInRow = 0;
            HasTarget = false;
            Enter(State.Anchored);
        }
    }

    // A pin of particle k at p keeps the run from the root feasible (within its tether at full stretch, with a
    // little room to pay out).
    bool PinReachable(int k, Vector3 p) => (p - Arm.X[0]).magnitude <= 0.95f * TentacleChain.Taut * Arm.Spec.MaxStretch * Arm.Spacing * k;

    // the run to the base pin is at (or near) its shortest: nothing left to pull with
    public bool Spent()
    {
        int p = Arm.BasePin();
        return p > 0 && (Arm.X[p] - Arm.X[0]).magnitude <= (Arm.Spec.MinStretch + 0.03f) * TentacleChain.Taut * Arm.Spacing * p;
    }

    // Hauling (a rope drawn through a hand): a grip arm ahead whose run is at its shortest slides its grip one
    // particle toward the root - every pin moves to the next particle down the arm, at the same surface point
    // (offset by the thicker radius there), the tip-most pin lets go - so the run to the hold is one segment
    // shorter and can pull again. No queries: the points are the ones the wrap found. Climbing a trunk is this,
    // over and over.
    public const float HaulSeconds = 0.1f;     // x TimeScale: at most one step this often
    public const float ZipTone = HangTone;        // muscle tone while a grip zips onto its hold
    // a swing's hold is a rope, not a lever: at full tone a slack held run bent toward its rest curl and the
    // reaction yanked the body (tug spikes of 35-70 body weights at 4x)
    public const float SwingHoldTone = HangTone;
    public const int SwingMinRun = 11;         // a swing's hold: the run the body swings on (shorter ones fight the others)
    public const int MinRun = 8;               // the run to the base pin never gets shorter than this: a shorter run cannot bend from the body to its hold within its joint cones, and fights it
    float nextHaul;
    readonly bool[] haulWas;
    public int Hauls;                          // cumulative

    bool Haul(OctopusController c, float ts)
    {
        int p = Arm.BasePin();
        if (p <= MinRun) return false;
        if (c.Time < nextHaul) return true;   // hauling; the next step comes
        nextHaul = c.Time + HaulSeconds * ts;
        for (int k = 0; k < Arm.N; k++) haulWas[k] = Arm.Pinned[k] && drapeUntil[k] <= 0f;
        for (int k = p; k < Arm.N; k++)
        {
            if (!haulWas[k]) continue;
            float dr = (Arm.Spec.RadiusAt(Arm.Spec.Spacing * (k - 1)) - Arm.Spec.RadiusAt(Arm.Spec.Spacing * k)) * Arm.SizeScale;
            Arm.Pin(k - 1, Arm.PinGoal[k] + Arm.PinNormal[k] * dr, Arm.PinNormal[k], Arm.PinSource[k], true);
        }
        // every run of pins moved down one: the tip-most pin of each run is free now
        for (int k = p; k < Arm.N; k++) if (haulWas[k] && (k + 1 >= Arm.N || !haulWas[k + 1])) Arm.Unpin(k);
        // the shorter run's length from where things are now: it holds the body where it is (no snatch), and the
        // pull goes on from there next tick
        HoldRun(c, false, true);
        Hauls++;
        return true;
    }

    void TickAnchored(OctopusController c, float ts)
    {
        // (the muscle's size scaling - its force relative to the weight it carries - is in TentacleChain.SizeFree,
        // for every arm and every role; a held run used to take Tone 1 / size here)
        Arm.SinusAmount = 0f; Arm.Tone = 1f;
        if (PinCount() == 0) { Enter(State.Feel); return; }   // the body let this grip go (overload)
        ArmRole role = RoleOf(c);
        if (Swinging(c))
        {
            // swinging: hold at the length the octopus gives (pivot: locked and pumped; catching: no tension); let
            // go when the body has swung over to the other group's holds
            Arm.Tone = SwingHoldTone;
            SwingRun(c);
            if (c.Board.Command[Index].letGo) { if (c.Time - anchoredAt > 0.2f * ts && c.Board.CanRelease(Index)) BeginRelease(c); return; }
            // a hold whose run is at its shortest and still slack (caught close to the body) hauls itself in, a
            // particle at a time, so it shares the body's weight with the other holds instead of hanging idle
            float sl = c.Board.SwingLen[Index];
            int bp = Arm.BasePin();
            if (sl > 0f && sl <= Arm.Spec.MinStretch + 0.005f && bp > SwingMinRun
                && (Arm.PinGoal[bp] - Arm.X[0]).magnitude < 0.97f * sl * TentacleChain.Taut * Arm.Spacing * bp)
                Haul(c, ts);
            return;
        }
        OctoIntent it = c.Intent;
        Vector3 head = c.Body.HeadCentre, anchor = c.Board.Anchor[Index];
        bool travel = ArmCoordinator.Travelling(it.mode);
        Vector3 R = c.Coordinator.RouteDir;
        bool arrived = !travel || (it.goal - head).magnitude < 0.15f * Arm.Length;
        bool ahead = travel && !arrived && OctoMath.Dot(anchor - head, R) > ArmCoordinator.AheadFraction * Arm.Length;
        // the lead grip pulls the body along the route; every other hold only pays out
        HoldRun(c, c.Board.Pulling[Index]);
        wantsRelease = false;
        // the body passed it: let go and grip again ahead. Came so close that the run cannot shorten any more and
        // cannot haul either: done pulling - the octopus makes it a support hold (it keeps what was gained) and
        // sends another arm ahead
        if (role == ArmRole.Grip && !arrived && !ahead) wantsRelease = true;   // (it stays a grip arm and reaches ahead again)
        else if (role == ArmRole.Grip && !arrived && Spent() && !Haul(c, ts)) Report(c, ArmReport.Done);
        else if (!ArmCoordinator.Holds(role) && role != ArmRole.Strike) wantsRelease = true;                // a hold the octopus does not need
        if (wantsRelease && c.Time - anchoredAt > 0.6f * ts && c.Board.CanRelease(Index)) BeginRelease(c);
        // a settled rest lets a spare hold go that is pulled taut (beyond the arm's rest length): holds taken
        // one by one rarely agree, and taut runs pulling against each other set the body flickering. One at a
        // time, the tautest first, never below RestAnchors.
        else if (c.Settled && Arm.Overload > TautHold && c.Board.AnchoredCount > c.RestAnchors && c.Time >= c.NextRestRelease
                 && c.Board.CanRelease(Index) && TautestHold(c))
        {
            c.NextRestRelease = c.Time + 1f * ts;
            BeginRelease(c);
        }
    }

    // The run from the root to the base-most pin takes its length from the body motion the octopus wants
    // (DesiredVelocity, the same for every arm). pull (the lead grip): the run is the distance from its hold to
    // the root's next position, so the tether draws the body there. Otherwise the run only pays out what that
    // motion asks of it (with PayOutMargin to spare: the body never follows a plan exactly) and never shortens -
    // a hold behind lets the body go forward, a hold beside never drags it sideways, and with no motion wanted
    // nothing changes (no creep under the body's weight).
    public const float PayOutMargin = 1.5f;

    void HoldRun(OctopusController c, bool pull, bool atCatch = false)
    {
        int p = Arm.BasePin();
        if (p <= 0) return;
        float arc = TentacleChain.Taut * Arm.Spacing * p;
        // (an easing pin - a zip or a haul sliding it onto the surface - counts where it goes: a run sized to where it
        // is would reel the body after it)
        Vector3 next = Arm.X[0] + c.Board.DesiredVelocity * c.LastDt, pin = Arm.PinGoal[p];
        float s;
        if (pull) s = (next - pin).magnitude / arc;
        else
        {
            float grow = (next - pin).magnitude - (Arm.X[0] - pin).magnitude;
            s = Arm.LengthTarget[0] + Mathf.Max(0f, grow) * PayOutMargin / arc;
            // at the catch, a run gripped longer than its target (a stretched reach) keeps the length it has: no
            // yank. Only then: afterwards a hold that something pushes the body away from (an impact, a contact
            // push) does not pay out after it - that would let every hold creep long and slack
            // while the body is being moved, a hold that pays out also follows where the body really went (the
            // pull draws it toward the grips' holds and gravity swings it, never exactly along the plan): it never
            // holds the body back against the pull
            bool moving = c.Board.DesiredVelocity.sqrMagnitude > 0f;
            if (atCatch || moving) s = Mathf.Max(s, Mathf.Min((Arm.X[0] - pin).magnitude / arc, Arm.Spec.MaxStretch));
        }
        // and long enough that the virtual particle behind the root (its tether reaches one spacing farther)
        // is not snatched toward the hold either: a hold off to the side of where the arm leaves the body would
        // otherwise twist the body through that tether, and two of them on either side twisted it against each
        // other (the tug-of-war that remained, at ~100 body weights each)
        float back = ((Arm.Back - pin).magnitude / TentacleChain.Taut - Arm.Spacing) / (Arm.Spacing * p);
        // (a pulling arm too: one that turned the body toward its hold through that tether twisted it against
        // the holds paying out - at 4x, hundreds of body weights)
        if (back > s) s = back;
        s = Mathf.Clamp(s, Arm.Spec.MinStretch, Arm.Spec.MaxStretch);
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = j < p ? s : 1f;
    }

    // The swing's run length (blackboard SwingLen, as LengthTarget), no shorter than the back tether needs (see
    // HoldRun: a hold off to the side would otherwise twist the body through the virtual back particle).
    void SwingRun(OctopusController c)
    {
        float s = c.Board.SwingLen[Index];
        if (!(s > 0f)) { HoldRun(c, false); return; }
        int p = Arm.BasePin();
        if (p <= 0) return;
        Vector3 pin = Arm.PinGoal[p];
        float back = ((Arm.Back - pin).magnitude / TentacleChain.Taut - Arm.Spacing) / (Arm.Spacing * p);
        if (back > s) s = back;
        s = Mathf.Clamp(s, Arm.Spec.MinStretch, Arm.Spec.MaxStretch);
        for (int j = 0; j < Arm.N - 1; j++) Arm.LengthTarget[j] = j < p ? s : 1f;
    }

    public const float TautHold = 0.8f;   // Overload above which a spare hold is let go in a settled rest

    bool TautestHold(OctopusController c)
    {
        for (int a = 0; a < c.Board.Arms; a++)
            if (a != Index && c.Board.Anchored[a] && c.Board.Load[a] > Arm.Overload) return false;
        return true;
    }

    ArmRole releaseRole;
    void BeginRelease(OctopusController c) { peelNext = c.Time; ReleasesStarted++; releaseRole = RoleOf(c); Enter(State.Release); }

    void TickRelease(OctopusController c, float ts)
    {
        if (Swinging(c)) SwingRun(c); else HoldRun(c, false);
        // the octopus wants this hold kept after all (support short): stop peeling, hold on with what is left
        // (a role changed to a holding one since the release began)
        if (RoleOf(c) != releaseRole && ArmCoordinator.Holds(RoleOf(c)) && PinCount() > 0) { anchoredAt = c.Time; Enter(State.Anchored); return; }
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
                TickHold(c);   // wrapped at once: a strike's momentum must not carry the tip off for a frame
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

    // ------------------------------------------------------------------ grooming (T6)

    // Grooming: the tip strokes up and down an analytic path - over its own side of the mantle shell, or along a
    // neighbouring arm - 2-4 strokes, then the arm reports Done and curls. The path stands a little off the
    // surface (GroomClearance x the tip radius beyond the skin), so the stroke brushes it and never goes in. No
    // queries.
    public const float StrokeSeconds = 1.3f, GroomClearance = 2.5f;
    float groomStart; int groomStrokes;
    public int GroomStrokes;   // cumulative

    void BeginGroom(OctopusController c)
    {
        groomStart = c.Time;
        groomStrokes = 2 + (int)(Rand() * 3f);
        Enter(State.Groom);
        Arm.ShapePull = 0.02f;
    }

    // Where the grooming tip should be at stroke phase u (0..strokes).
    public Vector3 GroomPoint(OctopusController c, float u, out int other)
    {
        OctopusBody b = c.Body;
        float tri = 1f - Mathf.Abs(2f * (u - Mathf.Floor(u)) - 1f);
        other = c.Board.Command[Index].groomArm;
        float tipR = Arm.Spec.TipRadius * Arm.SizeScale;
        if (other >= 0 && other < b.ArmCount && other != Index)
        {
            TentacleChain o = b.Arms[other];
            float kf = Mathf.Lerp(5f, o.N - 6f, tri);
            int k = Mathf.Clamp((int)kf, 1, o.N - 2);
            Vector3 p = Vector3.Lerp(o.X[k], o.X[k + 1], kf - k);
            Vector3 side = OctoMath.Norm(Arm.X[0] - o.X[0], c.Up);
            float rk = o.Spec.RadiusAt(o.Spec.Spacing * kf) * o.SizeScale;
            return p + side * (rk + GroomClearance * tipR);
        }
        Vector3 d = restDirLocal;
        float phi = Mathf.Atan2(d.x, d.z) + 0.35f * Mathf.Sin(Mathf.PI * u);
        float t = Mathf.Lerp(0.4f, 0.85f, tri);
        Vector3 local = OctopusShape.ShellPoint(b.Spec, t, phi) * b.SizeScale;
        Vector3 n = OctoMath.Norm(local - OctopusShape.Axis(b.Spec, t) * b.SizeScale, Vector3.up);
        local += n * (GroomClearance * tipR);
        return b.HeadCentre + OctoMath.Rotate(b.Orientation, local - b.Lattice.ClusterRestCentre[OctopusShape.ClusterHead]);
    }

    void TickGroom(OctopusController c, float ts)
    {
        if (RoleOf(c) != ArmRole.Groom) { Arm.ShapePull = 0f; Enter(State.Curl); return; }
        Arm.Tone = 1f; Arm.SinusAmount = 0f;
        float u = (c.Time - groomStart) / (StrokeSeconds * ts);
        if (u >= groomStrokes)
        {
            GroomStrokes += groomStrokes;
            Report(c, ArmReport.Done);
            Arm.ShapePull = 0f;
            Enter(State.Curl);
            return;
        }
        // the stroke eases in over the first half stroke (no whip toward the body)
        float w = Mathf.Clamp01(u * 2f) * 0.85f;
        Arm.SetTargetShape(GroomPoint(c, u, out _), w, false);
    }

    // The grooming arm's last particles touch the skin and stay outside it (the octopus does not collide with
    // itself otherwise; a stroke that reached into the mantle would read as the arm sinking into the body). The
    // analytic shell of the rest shape, in the head's frame; a particle inside is put back on it, its velocity
    // into it removed.
    internal void KeepOffShell(OctopusController c)
    {
        if (Mode != State.Groom) return;
        OctopusBody b = c.Body;
        Quaternion q = b.Orientation, inv = new Quaternion(-q.x, -q.y, -q.z, q.w);
        Vector3 rc = b.Lattice.ClusterRestCentre[OctopusShape.ClusterHead];
        float s = b.SizeScale;
        for (int k = Arm.N - 5; k < Arm.N; k++)
        {
            if (k < 1 || Arm.Pinned[k]) continue;
            Vector3 local = (OctoMath.Rotate(inv, Arm.X[k] - b.HeadCentre) + rc) * (1f / s);
            float rk = Arm.Spec.RadiusAt(Arm.Spec.Spacing * k) * Arm.SizeScale;
            float d = OctopusShape.ShellDistance(b.Spec, local, out Vector3 o) * s - rk;
            if (d >= 0f) continue;
            Vector3 push = OctoMath.Rotate(q, o) * (-d);
            Arm.X[k] += push; Arm.Prev[k] += push;
        }
    }

    // ------------------------------------------------------------------ perception

    // The arm's own reach cone: its rest direction (body-rotated), drawn toward the goal when travelling and
    // a little upward (trees).
    Vector3 ConeAxis(OctopusController c)
    {
        Vector3 rest = OctoMath.Rotate(c.Body.Orientation, restDirLocal);
        Vector3 up = c.Up;
        Vector3 axis = rest + up * 0.6f;
        // a swing's catch lies ahead along the route and up (the body hangs about an arm below its holds)
        if (Swinging(c) && c.Board.SwingRoute.sqrMagnitude > 0.5f) axis = rest * 0.4f + up * 0.8f + c.Board.SwingRoute * 1.4f;
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
        if (RoleOf(c) == ArmRole.Grip && ArmCoordinator.Travelling(it.mode))
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
        // a swing's catch may take the arm's stretch (the body swings toward it as it reaches)
        if (c.Cast(root, dir, (Swinging(c) ? 1.2f : 0.95f) * L, out SurfaceHit h) && h.distance > 0.3f * L)
            Remember(h.point, h.normal, h.source, c.Time, false);
    }

    // A swing's catch: a cast toward where the next hold would be - ahead of the pivot's front by about an arm's
    // length, level with it, a little to either side (two of three explore casts while swinging; the third
    // stays random in the cone, for whatever else is there)
    void ExploreSwing(OctopusController c, Vector3 root, float L)
    {
        ArmBlackboard b = c.Board;
        Vector3 R = b.SwingRoute, up = c.Up, side = OctoMath.Norm(OctoMath.Cross(up, R), Vector3.right);
        Vector3 from = b.PivotGroup >= 0 && b.SwingPivot != Vector3.zero ? b.SwingPivot : c.Body.HeadCentre + up * (0.7f * L);
        if (PivotSupport(c) && b.SupportCentre != Vector3.zero)
        {
            // support: beside the holds the body hangs from, along the branch
            Vector3 sa = b.SupportCentre + side * (L * (0.8f * Rand() - 0.4f)) + up * (L * (0.2f * Rand() - 0.1f));
            Vector3 sd = sa - root;
            float sl = sd.magnitude;
            ExploreCasts++;
            if (sl > 1e-4f && c.Cast(root, sd, Mathf.Min(sl + 0.3f * L, 1.3f * L), out SurfaceHit sh) && sh.distance > 0.3f * L)
            { ExploreHits++; Remember(sh.point, sh.normal, sh.source, c.Time, false); }
            return;
        }
        // with a route: at its next waypoint (a hint point on a branch), a little around it
        Vector3 aim = b.HasSwingTarget && b.SwingTargetStrict
            ? b.SwingTarget + R * (L * (0.3f * Rand() - 0.15f)) + up * (L * (0.2f * Rand() - 0.05f)) + side * (L * (0.5f * Rand() - 0.25f))
            : from + R * (L * (0.55f + 0.6f * Rand())) + up * (L * (0.5f * Rand() - 0.2f)) + side * (L * (0.8f * Rand() - 0.4f));
        Vector3 d = aim - root;
        float dl = d.magnitude;
        if (dl < 1e-4f) return;
        ExploreCasts++;
        if (c.Cast(root, d, Mathf.Min(dl + 0.3f * L, 1.3f * L), out SurfaceHit h) && h.distance > 0.3f * L)
        { ExploreHits++; Remember(h.point, h.normal, h.source, c.Time, false); }
    }

    // a grip arm of a climb up (hand over hand up a trunk, back from the ground: a route steeper than 45 deg): it
    // looks further along the surface the body already holds. (Along a level route the arm's own cone finds the
    // holds ahead on its side; aimed past the foremost hold, arms on the far side of the body took holds ahead and
    // kept the grip role reaching over the body.)
    bool Climbing(OctopusController c) => !Swinging(c) && RoleOf(c) == ArmRole.Grip && c.Intent.climb && ArmCoordinator.Travelling(c.Intent.mode)
                                          && OctoMath.Dot(c.Coordinator.RouteDir, c.Up) > ClimbRouteRise;
    public const float ClimbRouteRise = 0.7071f;

    // A climb's next hold: a cast at the surface beyond the foremost hold toward the goal (up a trunk: further up
    // it). Random casts in the arm's cone mostly missed a trunk the body hugs (the cone points at the goal, off to
    // the side of the bark), and the climb stalled with its holds spent below (two of three explore casts while
    // climbing; the third stays random in the cone).
    void ExploreClimb(OctopusController c, Vector3 root, float L)
    {
        ArmBlackboard b = c.Board;
        Vector3 head = c.Body.HeadCentre, R = c.Coordinator.RouteDir;
        if (R.sqrMagnitude < 0.5f) { ExploreCast(c, root, ConeAxis(c), L); return; }
        int top = -1; float tx = float.NegativeInfinity;
        for (int a = 0; a < b.Arms; a++)
        {
            if (!b.Anchored[a]) continue;
            float x = OctoMath.Dot(b.Anchor[a] - head, R);
            if (x > tx) { tx = x; top = a; }
        }
        if (top < 0) { ExploreCast(c, root, ConeAxis(c), L); return; }
        Vector3 side = OctoMath.Norm(OctoMath.Cross(R, c.Up), OctoMath.Norm(OctoMath.Cross(R, Vector3.right), Vector3.forward));
        Vector3 aim = b.Anchor[top] + R * (L * (0.35f + 0.45f * Rand())) + side * (L * (0.3f * Rand() - 0.15f));
        Vector3 d = aim - root;
        float dl = d.magnitude;
        if (dl < 1e-4f) return;
        if (c.Cast(root, d, Mathf.Min(dl + 0.4f * L, 1.3f * L), out SurfaceHit h) && h.distance > 0.3f * L)
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

    public const float GripMinReach = 0.6f;
    public const float SwingCatchDrop = 0.35f;   // x arm length: the lowest a catch without a waypoint may lie below the pivot
    public const float SwingCatchMin = 0.3f, SwingCatchBest = 0.9f, SwingAheadWeight = 1.2f;   // x arm length, ahead of the pivot   // x arm length: the nearest hold a grip arm on the route takes

    bool Acceptable(OctopusController c, Vector3 p)
    {
        if (c.Board.Crowding(Index, p, (Swinging(c) ? 0.07f : 0.12f) * Arm.Length) > 0.3f) return false;   // another arm holds there (a grip group's arms hold side by side)
        OctoIntent it = c.Intent;
        if (Swinging(c))
        {
            // a catch for the swing: ahead of the holds the body hangs from (or of the body, hanging from none)
            ArmBlackboard b = c.Board;
            Vector3 from = b.PivotGroup >= 0 && b.SwingPivot != Vector3.zero ? b.SwingPivot : c.Body.HeadCentre;
            if ((p - Arm.X[0]).magnitude <= 0.35f * Arm.Length) return false;
            if (PivotSupport(c)) return Mathf.Abs(OctoMath.Dot(p - b.SupportCentre, b.SwingRoute)) < 0.25f * Arm.Length;
            if (b.HasSwingTarget && b.SwingTargetStrict) return (p - b.SwingTarget).magnitude < ArmCoordinator.CatchNear * Arm.Length && !OnStem(c, p);
            // (and not far below the holds: catch after catch taken lower on a trunk swung the body down out of
            // the crowns, where no branch and no route was in reach any more)
            return OctoMath.Dot(p - from, b.SwingRoute) > SwingCatchMin * Arm.Length && OctoMath.Dot(p - from, c.Up) > -SwingCatchDrop * Arm.Length
                   && !OnStem(c, p);
        }
        if (RoleOf(c) != ArmRole.Grip || !ArmCoordinator.Travelling(it.mode)) return true;
        // a grip on the route is taken far enough out to pull with (a hold near the root leaves the run nothing
        // to shorten) and not clearly behind
        if ((p - Arm.X[0]).magnitude < GripMinReach * Arm.Length) return false;
        Vector3 g = it.goal - c.Body.HeadCentre, a = p - c.Body.HeadCentre;
        return Vector3.Dot(a, g) > -0.2f * a.magnitude * g.magnitude;
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
        memory[slot] = new Candidate { point = p, normal = n, source = source, time = time, valid = true, hinted = hinted, stem = owner != null && OnStem(owner, p) };
    }

    // p lies on a stem: the nearest skeleton hint within StemNear x the arm's length is steeper than a swing hold
    // (BranchRoute.MaxAxisRise). No hints: nothing is known to be a stem.
    public const float StemNear = 0.3f;
    bool OnStem(OctopusController c, Vector3 p)
    {
        if (c.Hints == null) return false;
        int n = c.Hints.Query(p, c.Up, -1f, 0f, StemNear * Arm.Length, 0f, float.PositiveInfinity, hintScratch);
        return n > 0 && Mathf.Abs(OctoMath.Dot(hintScratch[0].axis, c.Up)) > BranchRoute.MaxAxisRise;
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
        bool swing = Swinging(c);
        bool travel = !swing && RoleOf(c) == ArmRole.Grip && ArmCoordinator.Travelling(it.mode);
        Vector3 g = it.goal - centre;
        float gl = g.magnitude;
        ArmBlackboard bb = c.Board;
        Vector3 pivot = bb.PivotGroup >= 0 && bb.SwingPivot != Vector3.zero ? bb.SwingPivot : centre;
        for (int i = 0; i < MemorySize; i++)
        {
            if (!memory[i].valid) continue;
            Candidate m = memory[i];
            float age = c.Time - m.time;
            if (age > 6f * c.Body.TimeScale) { memory[i].valid = false; continue; }
            Vector3 d = m.point - root;
            float dist = d.magnitude / L;
            if (dist < (travel ? GripMinReach : swing ? 0.35f : 0.25f) || dist > 1.25f) continue;
            float s = 1f - Mathf.Abs(dist - 0.65f) / 0.45f;
            if (swing && PivotSupport(c))
            {
                float along = Mathf.Abs(OctoMath.Dot(m.point - bb.SupportCentre, bb.SwingRoute)) / L;
                if (along > 0.25f) continue;
                s = 0.5f * s + 1f - along;
            }
            else if (swing)
            {
                // the swing's catch: ahead of the pivot by about an arm's length, level with it (a catch far above
                // or below turns the swing into a climb or a drop), and up the route; never on a stem (a trunk is
                // no swing hold: caught, the body hung against it and slid down it out of the crowns)
                if (m.stem) continue;
                if (bb.HasSwingTarget && bb.SwingTargetStrict)
                {
                    // with a route: the hold at (or nearest) its next waypoint
                    float off = (m.point - bb.SwingTarget).magnitude / L;
                    if (off > ArmCoordinator.CatchNear) continue;
                    s = 0.5f * s + SwingAheadWeight * (1f - off / ArmCoordinator.CatchNear);
                }
                else
                {
                    float ahead = OctoMath.Dot(m.point - pivot, bb.SwingRoute) / L;
                    if (ahead < SwingCatchMin || OctoMath.Dot(m.point - pivot, c.Up) < -SwingCatchDrop * L) continue;
                    float level = Mathf.Abs(OctoMath.Dot(m.point - pivot, c.Up)) / L;
                    s = 0.5f * s + SwingAheadWeight * (1f - Mathf.Abs(ahead - SwingCatchBest) / SwingCatchBest) - 0.4f * level;
                }
            }
            else if (travel && gl > 1e-3f)
            {
                Vector3 a = m.point - centre;
                float prog = Vector3.Dot(a, g) / (gl * Mathf.Max(a.magnitude, 1e-3f));
                s += (0.6f + Mathf.Clamp01(it.urgency)) * prog;
            }
            else s += 0.3f * Vector3.Dot(m.point - centre, c.Up) / L;   // at rest: prefer holds above
            s -= 1.2f * c.Board.Crowding(Index, m.point, (swing ? 0.1f : 0.22f) * L);
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

    public bool IsLooking => Mode == State.Curl || Mode == State.Feel;   // free, not yet reaching for a hold
    public bool IsFreeArm => Mode == State.Curl || Mode == State.Feel || Mode == State.Reach || Mode == State.Strike || Mode == State.Groom;

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
