using UnityEngine;

// The locomotion sequencer of a planned body (planned-body rework, 2026-10-19; the swinger first): ONE layer that
// decides what the body and its arms do, replacing the brain -> ArmCoordinator -> TentacleAgent -> muscle timer stack
// for locomotion. The body's main motion is the reduced-order PlannedSwing; the arms keep their local work (the exact
// grip point from a probe, the wrap on the real bark, the peel) and otherwise only follow.
//
// Steps (Mode):
//   Hang      rest on the holds: the swing is damped out, the free arms hang passively; still -> the body sleeps
//   Pump      a next hold is chosen (BranchRoute over the branch hints, or a fan of casts toward the goal) and
//             the swing is pumped to the amplitude whose forward apex brings that hold within the free arm's reach
//   Reach     the free arm reaches for the hold in one eased move, timed to arrive at the forward apex
//   Catch     the hold is confirmed by a probe on the real surface, the arm wraps it (WrapMarch, zipped tip ->
//             base) and a rope joins the swing model: the body hangs in a two-hold sling for a moment
//   (transfer) the zip done, the old hold's rope goes and its arm peels off base -> tip; the body swings on under
//             the new hold (gibbon: never a spare hold, never none)
//   Creep     stalking: the hold near the prey is within reach without a swing - a slow reach, catch, transfer
//   Perch     at the hold near the prey: the rope is reeled in (the grip slides down the arm: a haul) until the mouth
//             is under the prey; still
//   Fall      no hold: ballistic; the arms grab whatever real surface comes within reach; landed -> wait there
// The arms (ArmUse): Free (passive, damped follow-through toward a hanging pose - no tone fighting gravity), Reach
// (an eased gesture), Zip, Hold (the run between the body and the hold stretched to the rope, the coil on the bark),
// Peel. The soft lattice is OctopusBody's secondary motion only.
// Every contact comes from the ISurfaceProbe (the controller counts the queries); hints only plan. Lengths x size,
// times x sqrt(size). Deterministic, no allocation after construction.
public sealed class SwingSequencer
{
    public enum Step { Hang, Pump, Reach, Catch, Creep, Perch, Fall }
    public enum ArmUse { Free, Reach, Zip, Hold, Peel }

    // ---- tuning (lengths x arm length L, times x TimeScale)
    public const float WrapTurns = 0.8f;           // a hold's coil (a hook: more left for the reach)
    public const int MinRun = 11;                  // particles: the run to the base pin never gets shorter (a haul stops there)
    public const float HangRun = 0.92f;            // hanging: rope / run arc
    public const float CatchStretch = 1.12f;       // a catch farther than this x the run arc misses
    public const float ZipSeconds = 0.3f, PeelStep = 0.035f;
    public const float ReachLead = 1.0f;           // a reach starts at most this long before the apex it aims for
    public const float MinReachSeconds = 0.45f, CreepReachSeconds = 1.3f;
    public const float MinAmplitudeDeg = 12f, MaxAmplitudeDeg = 62f, AmplitudeMarginDeg = 4f;
    public const float RestDamping = 2f;           // 1/s amplitude decay at rest (x 1/sqrt size)
    public const float SleepSpeed = 0.008f, SleepArmSpeed = 0.015f;   // m/s x sqrt size
    public const int SleepFrames = 30;
    public const float ReelSpeed = 0.25f;          // m/s x sqrt size
    public const float PerchRope = 0.36f;          // x L: perched under the prey
    public const float HaulSeconds = 0.08f;        // one particle of grip slid down the arm at most this often
    public const float ArriveLengths = 0.6f;
    public const float FreeSpeedCap = 3f;          // m/s x sqrt size: a free arm relative to the body (it trails, never whips)
    public const float HoldTone = 0.45f, ReachTone = 1.2f, ReachPull = 0.3f;
    public static float FreeTone = 0.35f, FreeCalm = 4f;   // a free arm: light tone, damped toward the body's motion (trails, no flutter)
    public const float OralOmega = 6f;             // rad/s: how fast the body turns its arm crown to the holds
    public const float FacingRateDeg = 70f;        // deg/s
    // The shoulder turn toward where the arm works (deg). Off: a turned arm root sheared the arm base against the
    // web bone it blends with (inverted arm-base triangles) and moved the drawn arm off its particles more, not less.
    public static float MaxAimDeg = 0f;

    public readonly PlannedSwing Swing = new PlannedSwing();
    public readonly BranchRoute Route = new BranchRoute();
    readonly OctopusBody body;
    readonly WrapMarch[] wrap;
    public readonly ArmUse[] Use;
    readonly int[] slot, keep, zipDone;
    readonly float[] zipStart, peelNext, reachStart, reachDur;
    readonly Vector3[] reachFrom, holdPivot;
    readonly SurfaceHit[] reachHit;
    readonly Quaternion[] aim;

    public Step Mode { get; private set; } = Step.Hang;
    public float StepTime { get; private set; }
    public int Transfers, Catches, CatchAttempts, Misses, Hauls, Falls, Sleeps;
    public bool Arrived { get; private set; }
    public bool Grounded { get; private set; }

    // plan (debug view, lab readout, tests)
    public bool HasNext { get; private set; }
    public Vector3 NextHold => next.point;
    public float TargetAmplitude { get; private set; }
    public readonly Vector3[] Arc = new Vector3[24];
    public int ArcCount { get; private set; }
    public Vector3 PredictedApex { get; private set; }
    public float PredictedApexTime { get; private set; }
    public Vector3 LastRelease { get; private set; }
    public Vector3 LastCatch { get; private set; }
    public int Reacher { get; private set; } = -1;
    public bool DebugTrace; public System.Action<string> DebugLog;   // a trace of the hold search (tests, investigations)

    SurfaceHit next;
    float nextRadius, nextSearch, time, calmFrames;
    int missesHere;
    Vector3 goal, facing = Vector3.forward, facingWant = Vector3.forward, oral = Vector3.up, oralVel;
    bool haveGoal, creeping;
    readonly Vector3[] failed = new Vector3[4];
    readonly float[] failedUntil = new float[4];
    int failedHead;
    readonly BranchHint[] hintBuf = new BranchHint[4];

    // seed: reserved for idle variety (gestures); everything the sequencer does now follows from the state alone
    public SwingSequencer(OctopusBody body, uint seed)
    {
        this.body = body;
        int n = body.ArmCount;
        wrap = new WrapMarch[n]; Use = new ArmUse[n];
        slot = new int[n]; keep = new int[n]; zipDone = new int[n];
        zipStart = new float[n]; peelNext = new float[n]; reachStart = new float[n]; reachDur = new float[n];
        reachFrom = new Vector3[n]; holdPivot = new Vector3[n]; reachHit = new SurfaceHit[n]; aim = new Quaternion[n];
        for (int a = 0; a < n; a++) { wrap[a] = new WrapMarch(body.Arms[a].N); slot[a] = -1; aim[a] = Quaternion.identity; }
        Swing.Reset(body.PlanOrigin, Vector3.zero);   // (the controller enabled the body's planned mode first)
        Swing.SubSteps = 4;
        for (int a = 0; a < n; a++) { body.Arms[a].Tone = FreeTone; body.Arms[a].CalmDamping = FreeCalm; }
    }

    float L => body.Arms[0].Length;
    float S => body.SizeScale;
    float TS => body.TimeScale;
    public Vector3 Crown => Swing.Position;

    // The centroid of the holds the body hangs from (the crown when none).
    public Vector3 Pivot
    {
        get
        {
            Vector3 p = Vector3.zero; int n = 0;
            for (int i = 0; i < PlannedSwing.MaxHolds; i++) if (Swing.Active[i]) { p += Swing.Pivot[i]; n++; }
            return n > 0 ? p / n : Swing.Position;
        }
    }

    void Enter(Step s) { if (Mode != s) { Mode = s; StepTime = 0f; if (s == Step.Fall) Falls++; } }

    // ------------------------------------------------------------------ spawn

    // Takes hold of the best surface above (a fan of casts from the crown), hangs the body under it (a spawn-time
    // jump), lays the arm onto it and wraps it at once. Returns how many arms hold.
    public int AnchorNearby(OctopusController c)
    {
        Vector3 up = c.Up, crown = Swing.Position;
        float bestS = float.NegativeInfinity; SurfaceHit bestTop = default; float bestR = 0f; bool found = false;
        Vector3 t0 = Perp(up), t1 = OctoMath.Cross(up, t0);
        for (int e = 0; e < 4; e++)
            for (int k = 0; k < 8; k++)
            {
                float el = (85f - 18f * e) * Mathf.Deg2Rad, az = k * Mathf.PI / 4f;
                Vector3 dir = (t0 * Mathf.Cos(az) + t1 * Mathf.Sin(az)) * Mathf.Cos(el) + up * Mathf.Sin(el);
                if (!c.Cast(crown, dir, 1.1f * L, out SurfaceHit h) || !c.HoldInside(h.point)) continue;
                bool cf = ContactFromHit(c, h, out SurfaceHit top, out float r);
                if (DebugTrace) DebugLog?.Invoke($"anchor cast {dir} hit {h.point} n {h.normal} contact {cf} r {r}");
                if (!cf) continue;
                float score = OctoMath.Dot(top.point - crown, up) - 0.5f * h.distance;
                if (score > bestS) { bestS = score; bestTop = top; bestR = r; found = true; }
            }
        if (!found) { Enter(Step.Fall); return 0; }
        int arm = 0;
        float run = ExpectedRun(bestR, arm);
        Vector3 hang = bestTop.point - up * (HangRun * run);
        Swing.Reset(hang, Vector3.zero);
        oral = up; oralVel = Vector3.zero;
        facing = facingWant = haveGoal ? OctoMath.Norm(Level(goal - hang, up), Perp(up)) : Perp(up);
        Quaternion rot = Pose(0f, c);
        body.SetPlannedPose(hang, rot, true);
        if (!GripNow(c, arm, bestTop)) { Enter(Step.Fall); return 0; }
        Enter(Step.Hang);
        UpdateBoard(c);
        return 1;
    }

    // Lays arm a straight from its root to the contact, wraps and pins it at once, and hangs a rope from it.
    bool GripNow(OctopusController c, int a, SurfaceHit contact)
    {
        TentacleChain ch = body.Arms[a];
        // a smooth curve from the root (leaving along the root's own direction) to the contact
        Vector3 root = ch.X[0], end = contact.point + contact.normal * (ch.Spec.TipRadius * S);
        Vector3 c1 = root + OctoMath.Rotate(ch.RootFrame, 0f, 0f, 1f) * (0.4f * (end - root).magnitude);
        for (int k = 0; k < ch.N; k++)
        {
            float t = (float)k / (ch.N - 1), u = 1f - t;
            ch.X[k] = root * (u * u) + c1 * (2f * u * t) + end * (t * t);
            ch.Prev[k] = ch.X[k];
        }
        ch.UpdateFrames(0f);
        if (!PlanWrap(c, a, contact)) return false;
        for (int i = 0; i < keep[a]; i++) ch.Pin(wrap[a].Particle[i], wrap[a].Point[i], wrap[a].Normal[i], wrap[a].Source[i]);
        zipDone[a] = keep[a];
        Vector3 piv = wrap[a].Point[keep[a] - 1];
        holdPivot[a] = piv;
        slot[a] = Swing.AddHold(piv, (Swing.Position - piv).magnitude);
        Swing.RopeTarget[slot[a]] = HangRun * RunArc(a);
        Use[a] = ArmUse.Hold;
        ch.ClearTargetShape(); ch.ShapePull = 0f; ch.Tone = HoldTone; ch.CalmDamping = 0f;
        return true;
    }

    // ------------------------------------------------------------------ tick

    public void Tick(float dt, OctopusController c)
    {
        time += dt; StepTime += dt;
        Vector3 up = c.Up;
        Swing.Up = up; Swing.Gravity = OctopusController.Gravity;
        Swing.ReelSpeed = ReelSpeed * TS; Swing.ReelAccel = 1.2f * S / (TS * TS) * 1f; Swing.ReelGain = 3f / TS; Swing.LateralDamping = 2f / TS;
        OctoIntent it = c.Intent;
        bool locomote = it.mode == OctoMode.Travel || it.mode == OctoMode.Retreat || it.mode == OctoMode.Stalk;
        bool perch = it.mode == OctoMode.Stalk || it.feed;
        if (locomote || it.mode == OctoMode.Stalk)
        {
            if (!haveGoal || (it.goal - goal).sqrMagnitude > 0.09f * L * L) { goal = it.goal; haveGoal = true; Arrived = false; HasNext = false; missesHere = 0; nextSearch = 0f; Route.Clear(); }
        }
        creeping = it.mode == OctoMode.Stalk;

        if (body.Asleep)
        {
            bool stay = (Mode == Step.Hang || Mode == Step.Perch) && (!locomote || Arrived) && (c.Feeder == null || !c.Feeder.Feeding);
            if (stay) { UpdateBoard(c); return; }
            body.Wake(); calmFrames = 0;
        }

        if (Swing.HoldCount == 0 && Mode != Step.Fall && !AnyZip()) Enter(Step.Fall);

        switch (Mode)
        {
            case Step.Hang: TickHang(c, locomote, perch); break;
            case Step.Pump: TickPump(c, locomote); break;
            case Step.Reach: TickReach(c); break;
            case Step.Catch: TickCatch(c, locomote); break;
            case Step.Creep: TickReach(c); break;
            case Step.Perch: TickPerch(c, locomote, perch); break;
            case Step.Fall: TickFall(c); break;
        }

        for (int a = 0; a < body.ArmCount; a++) if (Use[a] == ArmUse.Zip) TickZip(c, a);
        for (int a = 0; a < body.ArmCount; a++) if (Use[a] == ArmUse.Peel) TickPeel(c, a);
        if (Grounded) Swing.Velocity = Vector3.zero; else Swing.Step(dt);

        Quaternion rot = Pose(dt, c);
        body.SetPlannedPose(Swing.Position, rot);
        for (int a = 0; a < body.ArmCount; a++) ArmControl(c, a, rot, dt);
        UpdateBoard(c);
        Sleep(c, locomote);
    }

    bool AnyZip() { for (int a = 0; a < Use.Length; a++) if (Use[a] == ArmUse.Zip) return true; return false; }

    int FreeArm()
    {
        int best = -1;
        for (int a = 0; a < Use.Length; a++) if (Use[a] == ArmUse.Free) { best = a; break; }
        return best;
    }

    int HoldingArm()
    {
        for (int a = 0; a < Use.Length; a++) if (Use[a] == ArmUse.Hold) return a;
        return -1;
    }

    void TickHang(OctopusController c, bool locomote, bool perch)
    {
        Swing.Pumping = false;
        // resting: damped out; about to travel (choosing the next hold): coasting, whatever swing there is carries on
        Swing.RestDamping = locomote && !Arrived ? 0f : RestDamping / TS;
        if (perch && (creeping ? NearPreyHold(c) : !locomote)) { Enter(Step.Perch); return; }
        if (locomote && !Arrived && PlanNext(c)) Enter(Step.Pump);
    }

    void TickPump(OctopusController c, bool locomote)
    {
        if (!locomote) { Enter(Step.Hang); HasNext = false; return; }
        if (!HasNext && !PlanNext(c))
        {
            // still scanning for it: coast on (a swing braked while the next hold is chosen lost its energy with a jolt)
            Swing.Pumping = false; Swing.RestDamping = 0f;
            if (!Scanning) Enter(Step.Hang);
            return;
        }
        if (Arrived) { Enter(Step.Hang); return; }
        int hold = Swing.Single;
        if (hold < 0) return;   // still in a sling (transfer under way)
        Vector3 up = c.Up, P = Swing.Pivot[hold];
        Vector3 f = Level(next.point - P, up);
        Swing.SwingDir = OctoMath.Norm(f, facing);
        int a = FreeArm();
        // stalking, and the hold is within reach of the body hanging still: calm the swing, then creep over to it
        if (creeping && a >= 0 && Reachable(P - up * Swing.Rope[hold], a, 0.85f) && Swing.Amplitude >= 6f * Mathf.Deg2Rad)
        {
            Swing.Pumping = false; Swing.RestDamping = RestDamping / TS;
            return;
        }
        // a hold within reach of a calm body: no swing needed (a creep, a short step)
        if (a >= 0 && Swing.Amplitude < 6f * Mathf.Deg2Rad && Reachable(Swing.Position, a, 0.85f))
        {
            Swing.Pumping = false; Swing.RestDamping = RestDamping / TS;
            BeginReach(c, a, creeping ? CreepReachSeconds * TS : 0.8f * TS, Step.Creep);
            return;
        }
        TargetAmplitude = NeededAmplitude(P, Swing.Rope[hold], up, a < 0 ? 0 : a);
        if (TargetAmplitude < 0f) { Fail(c, "out of reach"); return; }
        Swing.TargetAmplitude = TargetAmplitude * (creeping ? 0.9f : 1f);
        Swing.Pumping = true;
        ArcCount = 0;
        bool apex = Swing.PredictApex(2.5f * TS, Arc, out int n, out Vector3 at, out float t);
        ArcCount = n; PredictedApex = at; PredictedApexTime = t;
        if (a < 0 || !apex) return;
        float vf = OctoMath.Dot(Swing.Velocity, Swing.SwingDir);
        // one eased move that arrives with the apex: started inside [MinReachSeconds, ReachLead] before it (later
        // than that, the next swing)
        if (vf > 0f && t >= MinReachSeconds * TS && t <= ReachLead * TS && Reachable(at, a, 1f))
            BeginReach(c, a, t, Step.Reach);
    }

    void BeginReach(OctopusController c, int a, float duration, Step step)
    {
        TentacleChain ch = body.Arms[a];
        Use[a] = ArmUse.Reach; Reacher = a;
        reachStart[a] = time; reachDur[a] = duration;
        reachFrom[a] = ch.X[ch.N - 1];
        reachHit[a] = next;
        ch.ShapeRate = TentacleChain.FastShapeRate;
        Enter(step);
    }

    // the eased reach: tip from where it was to just above the hold, lifted over in an arc
    Vector3 ReachTarget(int a, Vector3 up, out float u)
    {
        u = Mathf.Clamp01((time - reachStart[a]) / Mathf.Max(reachDur[a], 1e-3f));
        float e = u * u * u * (u * (6f * u - 15f) + 10f);
        SurfaceHit h = reachHit[a];
        float tipR = body.Arms[a].Spec.TipRadius * S;
        Vector3 to = h.point + h.normal * (tipR + 0.01f * S);
        return reachFrom[a] + (to - reachFrom[a]) * e + up * (0.12f * L * Mathf.Sin(Mathf.PI * e));
    }

    void TickReach(OctopusController c)
    {
        int a = Reacher;
        if (a < 0 || Use[a] != ArmUse.Reach) { Enter(Step.Hang); return; }
        if (Mode == Step.Creep) { Swing.Pumping = false; Swing.RestDamping = RestDamping / TS; }
        else { Swing.Pumping = false; Swing.RestDamping = 0f; }   // coasting: the apex comes where it was predicted
        float u = Mathf.Clamp01((time - reachStart[a]) / Mathf.Max(reachDur[a], 1e-3f));
        if (u < 1f) return;
        TryCatch(c, a);
    }

    void TryCatch(OctopusController c, int a)
    {
        CatchAttempts++;
        Vector3 up = c.Up;
        TentacleChain ch = body.Arms[a];
        SurfaceHit h = reachHit[a];
        float win = 0.1f * L;
        // the hold, confirmed on the real surface where the hand is now
        bool ok = c.Probe(h.point + up * (0.5f * win), up, win, h.source, c.SlotOf(a), out SurfaceHit hit) && OctoMath.Dot(hit.normal, up) > 0.3f;
        if (ok && (hit.point - Swing.Position).magnitude > CatchStretch * ExpectedRun(nextRadius, a)) ok = false;
        if (ok) ok = PlanWrap(c, a, hit);
        if (!ok)
        {
            if (DebugTrace) DebugLog?.Invoke($"miss: probe {c.Probe(h.point + up * (0.5f * win), up, win, h.source, -1, out SurfaceHit hh)} d {(hh.point - Swing.Position).magnitude:F3} limit {CatchStretch * ExpectedRun(nextRadius, a):F3} tip {(ch.X[ch.N - 1] - h.point).magnitude:F3} v {Swing.Velocity}");
            Misses++; missesHere++;
            Use[a] = ArmUse.Free; Reacher = -1;
            ch.ShapeRate = TentacleChain.DefaultShapeRate;
            if (missesHere >= 3) Fail(c, "missed");
            Enter(Swing.HoldCount > 0 ? Step.Pump : Step.Fall);
            return;
        }
        Catches++; missesHere = 0;
        LastCatch = hit.point;
        Use[a] = ArmUse.Zip; Reacher = -1;
        zipStart[a] = time; zipDone[a] = 0;
        ch.ShapeRate = TentacleChain.DefaultShapeRate;
        ch.ClearTargetShape(); ch.ShapePull = 0f; ch.Tone = HoldTone;
        // the body swings on from the new hold at once (the catch comes at the apex: the pivot changes where the
        // body is still - no snatch); the old arm keeps its grip until the zip is done, then peels
        // the pivot is where the run will leave the coil (the planned base pin): it does not move when the zip is done
        Vector3 piv = wrap[a].Point[keep[a] - 1];
        float dist = (Swing.Position - piv).magnitude;
        for (int o = 0; o < Use.Length; o++)
            if (o != a && slot[o] >= 0) { LastRelease = Swing.Pivot[slot[o]]; Swing.Release(slot[o]); slot[o] = -1; }
        slot[a] = Swing.AddHold(piv, dist);
        holdPivot[a] = piv;
        Swing.Pumping = false; Swing.RestDamping = 0f;
        HasNext = false;
        Enter(Step.Catch);
    }

    void TickCatch(OctopusController c, bool locomote)
    {
        Swing.Pumping = false; Swing.RestDamping = 0f;
        if (AnyZip()) return;
        // the zip is done: transfer - the old hold goes, the body swings on under the new one
        for (int a = 0; a < Use.Length; a++)
        {
            if (Use[a] != ArmUse.Hold || a == lastZipped) continue;
            ReleaseArm(a);
        }
        Transfers++;
        if (ReachedGoal(c)) { Arrived = true; Enter(Step.Hang); }
        else if (creeping && NearPreyHold(c)) Enter(Step.Perch);
        else Enter(locomote ? Step.Pump : Step.Hang);
    }

    int lastZipped = -1;

    void TickZip(OctopusController c, int a)
    {
        TentacleChain ch = body.Arms[a];
        WrapMarch w = wrap[a];
        float t = (time - zipStart[a]) / (ZipSeconds * TS);
        int due = Mathf.Min(keep[a], 1 + (int)(t * keep[a]));
        for (; zipDone[a] < due; zipDone[a]++)
        {
            int i = zipDone[a];
            ch.Pin(w.Particle[i], w.Point[i], w.Normal[i], w.Source[i], true);
        }
        if (zipDone[a] < keep[a] || t < 1f) return;
        // the hold: its pivot is where the run leaves the coil
        Vector3 piv = w.Point[keep[a] - 1];
        holdPivot[a] = piv;
        int s = slot[a];
        if (s >= 0) Swing.RopeTarget[s] = creeping ? Swing.Rope[s] : HangRun * RunArc(a);
        Use[a] = ArmUse.Hold; lastZipped = a;
    }

    void ReleaseArm(int a)
    {
        if (slot[a] >= 0) { LastRelease = Swing.Pivot[slot[a]]; Swing.Release(slot[a]); }   // (a transfer released it already)
        slot[a] = -1;
        Use[a] = ArmUse.Peel;
        peelNext[a] = time;
    }

    void TickPeel(OctopusController c, int a)
    {
        TentacleChain ch = body.Arms[a];
        // the grip opens base -> tip; once what is left would have to hold the arm taut (the body swinging away), it
        // all lets go at once - a stretched arm held at its tip snapped back like a whip when the last pin went
        int bp = ch.BasePin();
        if (bp > 0 && (ch.PinGoal[bp] - ch.X[0]).magnitude > 0.9f * TentacleChain.Taut * bp * ch.Spacing)
        {
            ch.UnpinAll();
            Use[a] = ArmUse.Free;
            return;
        }
        if (time < peelNext[a]) return;
        peelNext[a] = time + PeelStep * TS;
        for (int k = 1; k < ch.N; k++) if (ch.Pinned[k]) { ch.Unpin(k); return; }
        Use[a] = ArmUse.Free;
    }

    // ------------------------------------------------------------------ perch (stalking, feeding)

    // The hold over the prey: the stalk goal lies below it (OctopusBrain.CreepBelow).
    bool NearPreyHold(OctopusController c)
    {
        Vector3 piv = Pivot;
        Vector3 d = Level(goal - piv, c.Up);
        return d.magnitude < 0.25f * L;
    }

    void TickPerch(OctopusController c, bool locomote, bool perch)
    {
        Swing.Pumping = false; Swing.RestDamping = RestDamping / TS;
        if (!perch)
        {
            // done (eaten, or gave up): pay the rope back out and rest
            int h0 = HoldingArm();
            if (h0 >= 0 && slot[h0] >= 0) Swing.RopeTarget[slot[h0]] = HangRun * RunArc(h0);
            Enter(Step.Hang);
            return;
        }
        if (locomote && creeping && !NearPreyHold(c) && StepTime > 1f * TS) { Enter(Step.Hang); return; }
        int h = HoldingArm();
        if (h < 0 || slot[h] < 0) return;
        Swing.RopeTarget[slot[h]] = PerchRope * L;
        Haul(c, h);
    }

    // The grip slides one particle down the arm (a rope drawn through the hand) while the rope wants the run shorter
    // than the arm can shorten itself; pivot and coil stay where they are on the bark.
    void Haul(OctopusController c, int a)
    {
        TentacleChain ch = body.Arms[a];
        int bp = ch.BasePin();
        if (bp <= MinRun - 4 || slot[a] < 0) return;
        float want = Swing.RopeTarget[slot[a]], arc = bp * ch.Spacing;
        if (want >= ch.Spec.MinStretch * 0.95f * arc) return;
        if (time < nextHaul) return;
        nextHaul = time + HaulSeconds * TS;
        for (int k = 1; k < ch.N; k++)
        {
            if (!ch.Pinned[k]) continue;
            Vector3 g = ch.PinGoal[k], n = ch.PinNormal[k]; int src = ch.PinSource[k];
            ch.Unpin(k);
            ch.Pin(k - 1, g, n, src, true);
        }
        Hauls++;
    }
    float nextHaul;

    // ------------------------------------------------------------------ fall

    void TickFall(OctopusController c)
    {
        Swing.Pumping = false; Swing.RestDamping = 0f;
        Vector3 up = c.Up, crown = Swing.Position;
        if (Swing.HoldCount > 0) { Enter(Step.Hang); Grounded = false; return; }
        int a = FreeArm();
        if (a < 0) return;
        // a fan of casts each frame (6): anything within reach is grabbed at once
        Vector3 t0 = Perp(up), t1 = OctoMath.Cross(up, t0);
        float spin = time * 2.1f;
        for (int k = 0; k < 6; k++)
        {
            float el = (k < 3 ? 70f : 35f) * Mathf.Deg2Rad, az = spin + k * 2.0944f;
            Vector3 dir = (t0 * Mathf.Cos(az) + t1 * Mathf.Sin(az)) * Mathf.Cos(el) + up * Mathf.Sin(el);
            if (!c.Cast(crown, dir, 0.85f * L, out SurfaceHit h)) continue;
            if (!ContactFromHit(c, h, out SurfaceHit top, out float r)) continue;
            if ((top.point - crown).magnitude > CatchStretch * ExpectedRun(r, a)) continue;
            if (GripNow(c, a, top)) { Grounded = false; Enter(Step.Hang); return; }
        }
        // the ground: land and wait there (the arms keep looking up)
        if (!Grounded && c.Probe(crown, up, 0.4f * L, SurfaceHit.NoSource, -1, out SurfaceHit g) && OctoMath.Dot(crown - g.point, up) < 0.3f * L)
        {
            Grounded = true;
            Swing.Velocity = Vector3.zero;
        }
    }

    // ------------------------------------------------------------------ planning

    bool PlanNext(OctopusController c)
    {
        if (HasNext) return true;
        if (time < nextSearch) return false;
        nextSearch = time + 0.5f * TS;
        if (ReachedGoal(c)) { Arrived = true; return false; }
        Vector3 up = c.Up, P = Pivot;
        bool found = false;
        if (c.Hints != null)
        {
            if (!Route.Valid || Route.Done) Route.Plan(c.Hints, P, goal, up, L, body.Spec.arm.TipRadius * S * 1.5f);
            if (Route.Valid)
            {
                Route.Advance(P, up, L, 0.35f);
                if (!Route.Done)
                {
                    Vector3 w = Route.Waypoints[Route.Current];
                    int n = c.Hints.Query(w, up, -1f, 0f, 0.15f * L, 0f, 2f * L, hintBuf);
                    float r = n > 0 ? hintBuf[0].radius : 0.05f * S;
                    Vector3 centre = n > 0 ? hintBuf[0].point : w;
                    if (!Failed(centre) && TopContact(c, centre, r, out SurfaceHit top)) { next = top; nextRadius = r; found = true; }
                    else Route.Current++;
                }
            }
        }
        if (!found)
        {
            int r = Scan(c, P, up);
            if (r == 0) { nextSearch = time; return false; }   // scanning: on next frame
            found = r > 0;
        }
        HasNext = found;
        if (found) facingWant = OctoMath.Norm(Level(next.point - P, up), facingWant);
        return found;
    }

    // Probe-only (no hints): a scan of casts from the crown at points ahead along the travel direction - at the
    // pivot's height and a little above and below, spaced finer than a thin branch is thick - spread over frames
    // (ScanPerFrame casts each). The hit with the most progress toward the goal within one swing becomes the hold.
    public const int ScanPerFrame = 8, ScanDistances = 17, ScanHeights = 4;
    static readonly float[] scanRise = { 0f, 0.15f, -0.15f, 0.3f };
    int scanIdx = -1;
    bool Scanning => scanIdx >= 0;
    float scanBest;
    SurfaceHit scanHit;
    bool scanFound;

    // 1 found (next set), 0 still scanning, -1 nothing
    int Scan(OctopusController c, Vector3 P, Vector3 up)
    {
        if (scanIdx < 0) { scanIdx = 0; scanBest = float.NegativeInfinity; scanFound = false; }
        Vector3 crown = Swing.Position;
        Vector3 f = OctoMath.Norm(Level(goal - P, up), facing);
        float here = Level(goal - P, up).magnitude;
        int total = ScanDistances * ScanHeights;
        for (int n = 0; n < ScanPerFrame && scanIdx < total; n++, scanIdx++)
        {
            if (!c.Spare(1)) return 0;
            int hi = scanIdx / ScanDistances, di = scanIdx % ScanDistances;
            Vector3 aimAt = P + f * ((0.35f + 0.75f * di / (ScanDistances - 1)) * L) + up * (scanRise[hi] * L);
            Vector3 d = aimAt - crown;
            float len = d.magnitude;
            if (len < 1e-4f || !c.Cast(crown, d, len + 0.15f * L, out SurfaceHit h) || !c.HoldInside(h.point)) continue;
            float step = Level(h.point - P, up).magnitude, rise = OctoMath.Dot(h.point - P, up);
            if (step < 0.3f * L || step > 1.15f * L || Mathf.Abs(rise) > 0.5f * L) continue;
            float progress = here - Level(goal - h.point, up).magnitude;
            if (progress < 0.2f * L || Failed(h.point)) continue;
            float score = progress - 0.5f * Mathf.Abs(rise);
            if (score > scanBest) { scanBest = score; scanHit = h; scanFound = true; }
        }
        if (scanIdx < total) return 0;
        scanIdx = -1;
        if (scanFound && ContactFromHit(c, scanHit, out SurfaceHit top, out float r)) { next = top; nextRadius = r; return 1; }
        if (scanFound) { next = scanHit; Fail(c, "no hold there"); }
        return -1;
    }

    bool Failed(Vector3 p)
    {
        for (int i = 0; i < failed.Length; i++) if (time < failedUntil[i] && (failed[i] - p).sqrMagnitude < 0.04f * L * L) return true;
        return false;
    }

    void Fail(OctopusController c, string why)
    {
        failed[failedHead] = next.point; failedUntil[failedHead] = time + 6f * TS; failedHead = (failedHead + 1) % failed.Length;
        HasNext = false; missesHere = 0;
        Route.Clear();
        nextSearch = time + 0.3f * TS;
    }

    // arrived: under the goal (stalking: under the hold over the prey, much closer)
    bool ReachedGoal(OctopusController c) => haveGoal && Level(goal - Pivot, c.Up).magnitude < (creeping ? 0.25f : ArriveLengths) * L;

    // The top of the branch whose centre line passes through centre (radius r): the surface a probe from above finds.
    bool TopContact(OctopusController c, Vector3 centre, float r, out SurfaceHit top)
    {
        Vector3 up = c.Up;
        float lift = r + 0.1f * L;
        if (!c.Probe(centre + up * lift, up, lift + r, SurfaceHit.NoSource, -1, out top)) return false;
        return OctoMath.Dot(top.normal, up) > 0.5f && OctoMath.Dot(top.point - centre, up) > -0.5f * r;
    }

    // A cast hit on some branch: its local tube (3 probes) -> the top of that branch. No hold on a stem, a wall or a
    // trunk too thick to wrap.
    bool ContactFromHit(OctopusController c, SurfaceHit h, out SurfaceHit top, out float r)
    {
        top = default;
        float armR = 2f * body.Spec.arm.TipRadius * S;   // the probe offsets stay well inside a thin branch
        bool ok = wrap[0].EstimateAxis(c.SurfaceProbe, h, armR, -1, out Vector3 axis, out r, out float conf);
        c.CountProbes(3);
        if (!ok || float.IsInfinity(r) || conf < WrapMarch.MinConfidence) return false;
        if (Mathf.Abs(OctoMath.Dot(axis, c.Up)) > BranchRoute.MaxAxisRise) return false;
        if (2f * Mathf.PI * r * WrapTurns > 0.45f * L) return false;
        return TopContact(c, h.point - h.normal * r, r, out top);
    }

    // The run a hold of radius r leaves (particles to the base pin) x spacing.
    float ExpectedRun(float r, int a)
    {
        TentacleChain ch = body.Arms[a];
        float coil = WrapTurns * 2f * Mathf.PI * (r + ch.Spec.TipRadius * S);
        int pins = Mathf.CeilToInt(coil / ch.Spacing) + 1;
        int bp = Mathf.Max(MinRun, ch.N - 1 - pins);
        return bp * ch.Spacing;
    }

    float RunArc(int a) { int bp = body.Arms[a].BasePin(); return (bp > 0 ? bp : MinRun) * body.Arms[a].Spacing; }

    bool Reachable(Vector3 crownAt, int a, float margin) =>
        (next.point - crownAt).magnitude <= margin * CatchStretch * 0.97f * ExpectedRun(nextRadius, a);

    // The smallest swing amplitude whose forward apex brings the next hold within reach (+ a margin), -1 if none.
    float NeededAmplitude(Vector3 P, float l, Vector3 up, int a)
    {
        Vector3 f = OctoMath.Norm(Level(next.point - P, up), facing);
        for (float deg = MinAmplitudeDeg - AmplitudeMarginDeg; deg <= MaxAmplitudeDeg; deg += 1f)
        {
            float A = deg * Mathf.Deg2Rad;
            Vector3 at = P + (f * Mathf.Sin(A) - up * Mathf.Cos(A)) * l;
            if (Reachable(at, a, 0.92f)) return Mathf.Max(MinAmplitudeDeg, deg + AmplitudeMarginDeg) * Mathf.Deg2Rad;
        }
        return -1f;
    }

    bool PlanWrap(OctopusController c, int a, SurfaceHit contact)
    {
        TentacleChain ch = body.Arms[a];
        WrapMarch w = wrap[a];
        int tip = ch.N - 1;
        Vector3 tipDir = OctoMath.Norm(ch.X[tip] - ch.X[tip - 1], c.Up);
        // (planned with the arm's whole length available, so a branch that a full arm wraps is wrapped - not spread -
        // then cut to the coil it needs and to MinRun)
        w.Plan(c.SurfaceProbe, ch, contact, tip, tipDir, ch.X[0], 6, c.SlotOf(a));
        c.CountProbes(w.Probes); w.Probes = 0;
        if (w.Count == 0) return false;
        float r = float.IsInfinity(w.Radius) ? 0f : w.Radius;
        float coil = WrapTurns * 2f * Mathf.PI * (r + ch.Spec.TipRadius * S);
        int pins = Mathf.Min(w.Count, Mathf.CeilToInt(coil / ch.Spacing) + 1);
        int n = 0;
        while (n < pins && w.Particle[n] >= MinRun) n++;
        keep[a] = Mathf.Max(1, n);
        if (DebugTrace)
        {
            string p = "";
            for (int i = 0; i < w.Count; i++) p += $" {w.Particle[i]}:{w.Point[i]}";
            DebugLog?.Invoke($"wrap arm {a} contact {contact.point} n {contact.normal} tipDir {tipDir} spread {w.Spread} r {w.Radius} axis {w.Axis} turns {w.WrapTurns} count {w.Count} keep {keep[a]}{p}");
        }
        return true;
    }

    // ------------------------------------------------------------------ pose and arms

    // The body's rotation: its arm crown (body -Y, the oral side) turned to the holds - a spring, so a catch or a
    // transfer swings the body round smoothly - and its front to the travel direction (rate-limited).
    Quaternion Pose(float dt, OctopusController c)
    {
        Vector3 up = c.Up;
        Vector3 want = up;
        if (Swing.HoldCount > 0) want = OctoMath.Norm(Pivot - Swing.Position, up);
        if (dt > 0f)
        {
            float w = OralOmega / TS;
            oralVel += ((want - oral) * (w * w) - oralVel * (2f * w)) * dt;
            oral = OctoMath.Norm(oral + oralVel * dt, want);
        }
        else { oral = want; oralVel = Vector3.zero; }
        if (dt > 0f)
        {
            float ang = Mathf.Acos(Mathf.Clamp(OctoMath.Dot(facing, facingWant), -1f, 1f)), step = FacingRateDeg * Mathf.Deg2Rad * dt / TS;
            if (ang > 1e-4f)
            {
                Vector3 ax = OctoMath.Cross(facing, facingWant);
                ax = ax.sqrMagnitude > 1e-10f ? ax.normalized : up;
                facing = OctoMath.Norm(OctoMath.Rotate(facing, ax, Mathf.Min(ang, step)), facing);
            }
        }
        else facing = facingWant;
        Vector3 bodyUp = -oral;
        Vector3 fw = facing - bodyUp * OctoMath.Dot(facing, bodyUp);
        return OctoMath.Look(OctoMath.Norm(fw, Perp(bodyUp)), bodyUp);
    }

    void ArmControl(OctopusController c, int a, Quaternion rot, float dt)
    {
        TentacleChain ch = body.Arms[a];
        Vector3 up = c.Up;
        Vector3 aimAt;
        switch (Use[a])
        {
            case ArmUse.Hold:
            case ArmUse.Zip:
            {
                ch.Tone = HoldTone; ch.CalmDamping = 0f; ch.ShapePull = 0f; ch.SpeedCap = 0f;
                // the run: root -> the (planned) base pin - the zip's pins land tip first, the run stays the same
                int bp = Use[a] == ArmUse.Zip && keep[a] > 0 ? wrap[a].Particle[keep[a] - 1] : ch.BasePin();
                if (bp > 0)
                {
                    float d = (holdPivot[a] - ch.X[0]).magnitude, arc = bp * ch.Spacing;
                    float lt = Mathf.Clamp(d / (0.95f * TentacleChain.Taut * arc), ch.Spec.MinStretch, ch.Spec.MaxStretch);
                    for (int k = 0; k < bp; k++) ch.LengthTarget[k] = lt;
                    for (int k = bp; k < ch.N - 1; k++) ch.LengthTarget[k] = 1f;
                    aimAt = holdPivot[a];
                }
                else aimAt = holdPivot[a];
                break;
            }
            case ArmUse.Reach:
            {
                Vector3 tgt = ReachTarget(a, up, out float u);
                ch.SetTargetShape(tgt, 1f, true);
                ch.ShapePull = ReachPull * Mathf.Min(1f, 4f * u + 0.25f); ch.PullSpeed = 0f;
                ch.Tone = ReachTone; ch.CalmDamping = 0f; ch.SpeedCap = 0f;
                aimAt = tgt;
                break;
            }
            case ArmUse.Peel:
                ch.Tone = FreeTone; ch.CalmDamping = 0f; ch.ShapePull = 0f; ch.SpeedCap = FreeSpeedCap * TS;
                ResetLengths(ch);
                aimAt = ch.X[ch.N - 1];
                break;
            default:
            {
                // hanging loosely beside and below the body, damped relative to it: it trails the swing
                ch.ShapePull = 0f; ch.Tone = FreeTone; ch.CalmDamping = FreeCalm; ch.SpeedCap = FreeSpeedCap * TS;
                ResetLengths(ch);
                Vector3 root = ch.X[0], side = OctoMath.Norm(Level(root - Swing.Position, up), OctoMath.Rotate(rot, OctopusShape.ArmDir(body.Spec, a)));
                Vector3 tgt = root + (side * 0.45f - up * 0.8f - facing * 0.1f) * (0.8f * L);
                ch.SetTargetShape(tgt, 0.8f, false);
                aimAt = tgt;
                break;
            }
        }
        // the shoulder: the arm leaves the crown turned toward where it works (at most 60 deg off its rest direction)
        Vector3 rest = OctoMath.Rotate(rot, OctopusShape.ArmDir(body.Spec, a));
        Vector3 to = OctoMath.Norm(aimAt - ch.X[0], rest);
        float ang = Mathf.Acos(Mathf.Clamp(OctoMath.Dot(rest, to), -1f, 1f));
        Quaternion want = Quaternion.identity;
        if (ang > 1e-3f)
        {
            Vector3 axisW = OctoMath.Norm(OctoMath.Cross(rest, to), Vector3.right);
            Vector3 axisB = OctoMath.Rotate(OctoMath.Conj(rot), axisW);
            want = OctoMath.AxisAngle(axisB, Mathf.Min(ang, MaxAimDeg * Mathf.Deg2Rad));
        }
        float k2 = dt > 0f ? 1f - Mathf.Exp(-6f * dt / TS) : 1f;
        aim[a] = NLerp(aim[a], want, k2);
        body.ArmAim[a] = aim[a];
    }

    static void ResetLengths(TentacleChain ch) { for (int k = 0; k < ch.N - 1; k++) ch.LengthTarget[k] = 1f; }

    static Quaternion NLerp(Quaternion a, Quaternion b, float t)
    {
        float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w, sb = d < 0f ? -t : t, sa = 1f - t;
        return OctoMath.Normalize(OctoMath.Q(a.x * sa + b.x * sb, a.y * sa + b.y * sb, a.z * sa + b.z * sb, a.w * sa + b.w * sb));
    }

    // ------------------------------------------------------------------ board, sleep

    void UpdateBoard(OctopusController c)
    {
        ArmBlackboard b = c.Board;
        for (int a = 0; a < body.ArmCount; a++)
        {
            bool held = Use[a] == ArmUse.Hold || Use[a] == ArmUse.Zip;
            b.Anchored[a] = held;
            if (held) b.Anchor[a] = holdPivot[a];
            b.Role[a] = Use[a] == ArmUse.Hold ? ArmRole.Anchor : Use[a] == ArmUse.Zip || Use[a] == ArmUse.Reach ? ArmRole.Grip : Use[a] == ArmUse.Peel ? ArmRole.Rest : ArmRole.Hang;
            b.Claiming[a] = Use[a] == ArmUse.Reach;
            if (b.Claiming[a]) b.ClaimPoint[a] = reachHit[a].point;
        }
        b.Grounded = Grounded;
        b.SwingCentre = Pivot;
        b.PivotGroup = Swing.HoldCount > 0 ? 0 : -1;
        b.HasSwingTarget = HasNext; b.SwingTarget = next.point;
        b.Recount();
    }

    void Sleep(OctopusController c, bool locomote)
    {
        bool rest = (Mode == Step.Hang || Mode == Step.Perch) && (!locomote || Arrived) && !AnyZip() && (c.Feeder == null || !c.Feeder.Feeding);
        for (int a = 0; a < Use.Length && rest; a++) if (Use[a] == ArmUse.Reach || Use[a] == ArmUse.Peel) rest = false;
        if (rest && Swing.HoldCount > 0)
        {
            for (int i = 0; i < PlannedSwing.MaxHolds; i++) if (Swing.Active[i] && Mathf.Abs(Swing.Rope[i] - Swing.RopeTarget[i]) > 1e-4f) rest = false;
        }
        if (!rest || Swing.Velocity.magnitude > SleepSpeed * TS || body.MaxArmSpeed > SleepArmSpeed * TS) { calmFrames = 0; return; }
        if (++calmFrames < SleepFrames) return;
        Swing.Velocity = Vector3.zero;
        body.SleepPlanned();
        Sleeps++;
    }

    // ------------------------------------------------------------------ helpers

    static Vector3 Level(Vector3 v, Vector3 up) => v - up * OctoMath.Dot(v, up);
    static Vector3 Perp(Vector3 up) => OctoMath.Norm(OctoMath.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.forward);
}
