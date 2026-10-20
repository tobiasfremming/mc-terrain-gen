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
//   Play      a move for fun in place (OctoIntent.play, the brain's Play state): BigSwing / HighSwing pump to the move's
//             amplitude in a direction of its own; Circle drives the body round under the hold in a cone; Drop finds a
//             lower branch within reach, calms, reaches down and catches it, then lets go above - the body drops on
//             the slack new arm and is caught softly (PlannedSwing's soft rope). A Run (Travel + play) pumps higher
//             than the catch needs (up to the highest apex still within reach) and reaches only near that amplitude.
//             Damping, the lateral calming and the free arm's pose change eased (RampSeconds), never in a step.
// The arms (ArmUse): Free (passive, damped follow-through toward a hanging pose - no tone fighting gravity), Reach
// (an eased gesture), Zip, Hold (the run between the body and the hold stretched to the rope, the coil on the bark),
// Peel. The soft lattice is OctopusBody's secondary motion only.
// Every contact comes from the ISurfaceProbe (the controller counts the queries); hints only plan. Lengths x size,
// times x sqrt(size). Deterministic, no allocation after construction.
//
// Many arms (Sling: more than two - the tree octopus, 2026-10-20). The same steps, plus:
//   rest      a multi-hold sling: resting, the body takes support holds (RestHolds in all) - a fan of casts from the
//             crown, a slow reach by the free arm whose root lies nearest, a catch whose rope is the distance it has
//             (no reel: several ropes can never pull against each other) - and sleeps only once it has them (or none
//             is within reach)
//   travel    a pendulum on one hold: before the pump (or a play move) every support but the hold toward the next one
//             peels off; brachiation is then the swinger's, the reach and the catch by the free arm nearest the hold
//   idle arms display only (OctopusBody posed arms): curled up under the web or trailing down past the mantle, each
//             arm its own shape, following the body with a lag; at rest now and then one grooms (a slow stroke over
//             the mantle). Never simulated, so never flapping
//   hunt      the brain's Strike: up to intent.strikers free arms nearest the intercept strike (an eased fast reach); the
//             first tip at the prey's grab volume takes it (Prey: the arm wraps the capsule, re-planned every tick)
//             for the controller's HoldSeconds, then lets go; the others draw back
public sealed class SwingSequencer
{
    public enum Step { Hang, Pump, Reach, Catch, Creep, Perch, Fall, Play }
    public enum ArmUse { Free, Reach, Zip, Hold, Peel, Strike, Prey, Groom }

    // ---- tuning (lengths x arm length L, times x TimeScale)
    // A hold's coil: a hook - over the top and down the near side (half a turn; 0.8 turns ate a fifth of the reach, so
    // every catch needed a big swing). A branch too thick for a 0.8-turn coil within 0.45 L is still no hold.
    public const float WrapTurns = 0.5f, MaxCoilTurns = 0.8f;
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

    // play (debug view, lab readout, tests)
    public PlayMove Play { get; private set; }            // the move being swung (None: no play)
    public int Drops, DropFailures;                        // cumulative
    public float FreeOpen { get; private set; }            // 0..1: the free arm flung out (eased)
    public const float RampSeconds = 0.6f;                 // x TimeScale: damping and lateral calming change this smoothly
    public const float PlayRestDamping = 0.7f;             // 1/s (x 1/sqrt size): a pause in play lets the swing die slowly
    public const float RunReachSlackDeg = 3f;              // a run reaches once the swing is within this of its amplitude
    public const float DropMaxAmplitudeDeg = 48f, DropMinLengths = 0.08f;   // the drop's swing at most; its fall at least x L
    public const float DropCalmDeg = 8f, DropBelowLengths = 0.15f;   // x L: the lower hold at least this far under the held one
    public bool PlayDone(int id) => playDoneId == id;
    public bool DropFailedRecently => time < dropFailedUntil;
    int playSeen = -1, playDoneId = -1, dropPhase;
    float playAmp, playSense = 1f, dropFailedUntil, dropRope, restWant, lateralWant;
    Vector3 playDir;
    bool dropping;
    SurfaceHit dropHit; float dropRadius;
    uint rng;

    SurfaceHit next;
    float nextRadius, nextSearch, time, calmFrames;
    int missesHere;
    Vector3 goal, facing = Vector3.forward, facingWant = Vector3.forward, oral = Vector3.up, oralVel;
    bool haveGoal, creeping;
    readonly Vector3[] failed = new Vector3[4];
    readonly float[] failedUntil = new float[4];
    int failedHead;
    readonly BranchHint[] hintBuf = new BranchHint[4];

    // seed: the play moves' variety (direction, sense); everything else follows from the state alone
    public SwingSequencer(OctopusBody body, uint seed)
    {
        this.body = body;
        rng = seed * 2246822519u + 3266489917u; if (rng == 0) rng = 11;
        int n = body.ArmCount;
        wrap = new WrapMarch[n]; Use = new ArmUse[n];
        slot = new int[n]; keep = new int[n]; zipDone = new int[n];
        zipStart = new float[n]; peelNext = new float[n]; reachStart = new float[n]; reachDur = new float[n];
        reachFrom = new Vector3[n]; holdPivot = new Vector3[n]; reachHit = new SurfaceHit[n]; aim = new Quaternion[n];
        for (int a = 0; a < n; a++) { wrap[a] = new WrapMarch(body.Arms[a].N); slot[a] = -1; aim[a] = Quaternion.identity; }
        Swing.Reset(body.PlanOrigin, Vector3.zero);   // (the controller enabled the body's planned mode first)
        Swing.SubSteps = 4;
        Swing.LateralDamping = lateralWant = 2f / TS;
        for (int a = 0; a < n; a++) { body.Arms[a].Tone = FreeTone; body.Arms[a].CalmDamping = FreeCalm; }
        Sling = n > 2;
        if (Sling)
        {
            body.DrawHandle = SlingDrawHandle; body.DrawEndHandle = SlingDrawEndHandle; body.DrawSinglePin = true;
            strikeStart = new float[n]; poseBase = new Vector3[n][]; poseNear = new Vector3[n][]; posedSet = new bool[n]; poseIsNear = new bool[n];
            for (int a = 0; a < n; a++)
            {
                poseBase[a] = new Vector3[body.Arms[a].N]; BuildPose(a, poseBase[a], false);
                poseNear[a] = new Vector3[body.Arms[a].N]; BuildPose(a, poseNear[a], true);
            }
        }
    }

    // ---- many arms (the tree octopus)
    public readonly bool Sling;
    public const float SlingCatchSlack = 1.06f;      // x CatchStretch: the catch limit of a hand that is there
    public const float SlingDrawHandle = 0.5f, SlingDrawEndHandle = 0.33f;      // x chord: a drawn run leaves the crown straighter (the web)
    public const float HoldLeanDeg = 35f;
    public const int RestHolds = 3;                 // holds of a settled rest (the first + supports)
    public const float SupportSpacing = 0.18f;      // x L: a support at least this far from every other hold
    public const float SupportRetrySeconds = 6f;    // x TimeScale: none found - look again after this long
    public const float GroomChance = 0.35f;         // a rest grooms once with this chance
    public const float GroomStrokeSeconds = 1.3f, GroomEaseSeconds = 0.6f;   // x TimeScale
    public const float StrikeEaseSeconds = 0.06f;   // x TimeScale: the strike's pull eases in this fast
    public int Supports, Strikes, StrikeMisses, Grooms;   // cumulative
    bool supporting, groomed;
    float supportNext, groomStart, groomEnd;
    int groomArm = -1, groomStrokes;
    Vector3 groomFrom, groomTo;
    float[] strikeStart;
    Vector3[][] poseBase, poseNear;
    bool[] posedSet, poseIsNear;
    bool strikeLaunched;
    IPreyTarget heldPrey;
    float holdUntil;
    public Vector3 StrikeAt { get; private set; }
    public bool HoldingPrey { get { for (int a = 0; a < Use.Length; a++) if (Use[a] == ArmUse.Prey) return true; return false; } }
    public IPreyTarget HeldPrey => heldPrey;

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

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
        if (Sling) arm = NearestFree(bestTop.point);
        if (!GripNow(c, arm, bestTop)) { Enter(Step.Fall); return 0; }
        Enter(Step.Hang);
        int held = 1;
        if (Sling)
        {
            // the sling at once: support holds around (each by the free arm nearest it), the idle arms on their poses
            for (int k = 0; k < RestHolds - 1; k++)
            {
                supportIdx = -1;
                if (ScanSupport(c, true) <= 0) break;
                int s = NearestFree(supportHit.point);
                if (s < 0 || !GripNow(c, s, supportHit, true)) break;
                held++; Supports++;
            }
            for (int a = 0; a < body.ArmCount; a++)
                if (Use[a] == ArmUse.Free) { ApplyPose(a); body.ArmPosed[a] = true; body.SnapPosed(a); }
        }
        UpdateBoard(c);
        return held;
    }

    // Lays arm a straight from its root to the contact, wraps and pins it at once, and hangs a rope from it (a support:
    // the rope is the distance it has and stays so).
    bool GripNow(OctopusController c, int a, SurfaceHit contact, bool support = false)
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
        Swing.RopeTarget[slot[a]] = support ? Swing.Rope[slot[a]] : HangRun * RunArc(a);
        if (support) FreezeRopes();
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
        Swing.ReelSpeed = ReelSpeed * TS; Swing.ReelAccel = 1.2f * S / (TS * TS) * 1f; Swing.ReelGain = 3f / TS;
        Swing.Circling = false; lateralWant = 2f / TS;
        OctoIntent it = c.Intent;
        bool locomote = it.mode == OctoMode.Travel || it.mode == OctoMode.Retreat || it.mode == OctoMode.Stalk;
        bool perch = it.mode == OctoMode.Stalk || it.feed;
        // play: a new move starts (its own direction and sense); in place (Rest + a move) or a run (Travel + Run)
        PlayMove move = it.play;
        inPlace = it.mode == OctoMode.Rest && (move == PlayMove.BigSwing || move == PlayMove.HighSwing || move == PlayMove.Circle || move == PlayMove.Drop);
        playRun = it.mode == OctoMode.Travel && move == PlayMove.Run;
        playing = move != PlayMove.None;
        if (playing && it.playId != playSeen) { playSeen = it.playId; StartMove(c, move, it.playAmplitude); }
        Play = inPlace || playRun || move == PlayMove.Pause ? move : PlayMove.None;
        if (locomote || it.mode == OctoMode.Stalk)
        {
            if (!haveGoal || (it.goal - goal).sqrMagnitude > 0.09f * L * L) { goal = it.goal; haveGoal = true; Arrived = false; HasNext = false; missesHere = 0; nextSearch = 0f; Route.Clear(); }
        }
        creeping = it.mode == OctoMode.Stalk;

        if (body.Asleep)
        {
            bool stay = (Mode == Step.Hang || Mode == Step.Perch) && (!locomote || Arrived) && !inPlace && (c.Feeder == null || !c.Feeder.Feeding) && it.mode != OctoMode.Strike;
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
            case Step.Play: TickPlay(c); break;
        }
        if (Sling) { TickHunt(c, it); TickGroom(c); }
        // eased: the damping, the lateral calming, the free arm's gesture (a step in any of them is a jolt)
        float ramp = dt / (RampSeconds * TS);
        // (down 4x faster: a coasting reach must not be braked by the rest that came before it)
        Swing.RestDamping = Mathf.MoveTowards(Swing.RestDamping, restWant, RestDamping / TS * ramp * (restWant < Swing.RestDamping ? 4f : 1f));
        Swing.LateralDamping = Mathf.MoveTowards(Swing.LateralDamping, lateralWant, 2f / TS * ramp);
        bool open = Mode == Step.Play && (Play == PlayMove.BigSwing || Play == PlayMove.HighSwing || Play == PlayMove.Circle) || playRun && Mode == Step.Pump;
        FreeOpen = Mathf.MoveTowards(FreeOpen, open ? 1f : 0f, dt / (0.8f * TS));

        for (int a = 0; a < body.ArmCount; a++) if (Use[a] == ArmUse.Zip) TickZip(c, a);
        for (int a = 0; a < body.ArmCount; a++) if (Use[a] == ArmUse.Peel) TickPeel(c, a);
        if (Sling)
        {
            // resting in a sling of holds (or perched over prey): braced on them, the body comes to a stop
            Swing.Braced = Swing.HoldCount >= 2 && (Mode == Step.Hang || Mode == Step.Perch || Mode == Step.Creep && supporting) && !Swing.Pumping;
            Swing.BraceRate = 5f / TS;
        }
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
        if (Sling) return NearestFree(HasNext || dropping ? next.point : dropPhase == 1 ? dropHit.point : Swing.Position + Swing.Up * L);
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
        restWant = locomote && !Arrived ? 0f : (playing ? PlayRestDamping : RestDamping) / TS;
        if (inPlace && Swing.HoldCount > 0 && !Grounded) { Enter(Step.Play); return; }
        if (perch && (creeping ? NearPreyHold(c) : !locomote)) { Enter(Step.Perch); return; }
        if (locomote && !Arrived && PlanNext(c)) { Enter(Step.Pump); return; }
        if (Sling && !playing && (c.Intent.mode == OctoMode.Rest || locomote && Arrived && !creeping)) TrySupport(c);
    }

    void TickPump(OctopusController c, bool locomote)
    {
        restWant = 0f;   // pumping (or coasting into the reach): the prediction assumes no damping
        if (!locomote) { Enter(Step.Hang); HasNext = false; return; }
        if (!HasNext && !PlanNext(c))
        {
            // still scanning for it: coast on (a swing braked while the next hold is chosen lost its energy with a jolt)
            Swing.Pumping = false; restWant = 0f;
            if (!Scanning) Enter(Step.Hang);
            return;
        }
        if (Arrived) { Enter(Step.Hang); return; }
        // many arms: off the sling onto the one hold toward the next (the supports peel off; the body starts to swing)
        if (Sling && Swing.HoldCount > 1) { if (!AnyZip()) KeepOne(c, Swing.Position); return; }
        int hold = Swing.Single;
        if (hold < 0) return;   // still in a sling (transfer under way)
        Vector3 up = c.Up, P = Swing.Pivot[hold];
        Vector3 f = Level(next.point - P, up);
        Swing.SwingDir = OctoMath.Norm(f, facing);
        int a = FreeArm();
        // stalking, and the hold is within reach of the body hanging still: calm the swing, then creep over to it
        if (creeping && a >= 0 && Reachable(P - up * Swing.Rope[hold], a, 0.85f) && Swing.Amplitude >= 6f * Mathf.Deg2Rad)
        {
            Swing.Pumping = false; restWant = RestDamping / TS;
            return;
        }
        // a hold within reach of a calm body: no swing needed (a creep, a short step)
        if (a >= 0 && Swing.Amplitude < 6f * Mathf.Deg2Rad && Reachable(Swing.Position, a, 0.85f))
        {
            Swing.Pumping = false; restWant = RestDamping / TS;
            BeginReach(c, a, creeping ? CreepReachSeconds * TS : 0.8f * TS, Step.Creep);
            return;
        }
        TargetAmplitude = NeededAmplitude(P, Swing.Rope[hold], up, a < 0 ? 0 : a);
        if (TargetAmplitude < 0f) { Fail(c, "out of reach"); return; }
        if (playRun) TargetAmplitude = RunAmplitude(P, Swing.Rope[hold], up, a < 0 ? 0 : a, TargetAmplitude);
        Swing.TargetAmplitude = TargetAmplitude * (creeping ? 0.9f : 1f);
        Swing.Pumping = true;
        ArcCount = 0;
        bool apex = Swing.PredictApex(2.5f * TS, Arc, out int n, out Vector3 at, out float t);
        ArcCount = n; PredictedApex = at; PredictedApexTime = t;
        if (a < 0 || !apex) return;
        float vf = OctoMath.Dot(Swing.Velocity, Swing.SwingDir);
        // one eased move that arrives with the apex: started inside [MinReachSeconds, ReachLead] before it (later
        // than that, the next swing)
        bool big = !playRun || Swing.Amplitude >= TargetAmplitude - RunReachSlackDeg * Mathf.Deg2Rad;
        if (vf > 0f && t >= MinReachSeconds * TS && t <= ReachLead * TS && big && Reachable(at, a, 1f))
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
        if (Mode == Step.Creep) { Swing.Pumping = false; restWant = RestDamping / TS; }
        else { Swing.Pumping = false; restWant = 0f; }   // coasting: the apex comes where it was predicted
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
        // (many arms: a hand on the bark a little beyond the planned stretch still takes it - the drawn run stretches)
        if (ok && (hit.point - Swing.Position).magnitude > (Sling ? SlingCatchSlack : 1f) * CatchStretch * ExpectedRun(nextRadius, a)) ok = false;
        if (ok) ok = PlanWrap(c, a, hit);
        if (!ok)
        {
            if (DebugTrace) DebugLog?.Invoke($"miss: probe {c.Probe(h.point + up * (0.5f * win), up, win, h.source, -1, out SurfaceHit hh)} d {(hh.point - Swing.Position).magnitude:F3} limit {CatchStretch * ExpectedRun(nextRadius, a):F3} tip {(ch.X[ch.N - 1] - h.point).magnitude:F3} v {Swing.Velocity}");
            Misses++; missesHere++;
            if (dropping) { dropping = false; DropFailed(); }
            Use[a] = ArmUse.Free; Reacher = -1;
            ch.ShapeRate = TentacleChain.DefaultShapeRate;
            if (supporting) { supporting = false; missesHere = 0; supportNext = time + SupportRetrySeconds * TS; Enter(Step.Hang); return; }
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
        if (supporting)
        {
            // a support: one more rope of the length it has; every rope keeps its length (the sling stays feasible)
            slot[a] = Swing.AddHold(piv, dist);
            FreezeRopes();
            holdPivot[a] = piv;
            Swing.Pumping = false;
            Enter(Step.Catch);
            return;
        }
        if (dropping)
        {
            // a drop: the old hold keeps the body until the zip is done; the new arm hangs slack, then catches it
            dropRope = Mathf.Max(DropRope(Swing.Position, piv, up, HangRun * ExpectedRun(nextRadius, a)), dist);
            slot[a] = Swing.AddHold(piv, dropRope, dropRope > dist + 1e-3f);
            holdPivot[a] = piv;
            Swing.Pumping = false;
            HasNext = false;
            Enter(Step.Catch);
            return;
        }
        for (int o = 0; o < Use.Length; o++)
            if (o != a && slot[o] >= 0) { LastRelease = Swing.Pivot[slot[o]]; Swing.Release(slot[o]); slot[o] = -1; }
        slot[a] = Swing.AddHold(piv, dist);
        holdPivot[a] = piv;
        Swing.Pumping = false; restWant = 0f;
        HasNext = false;
        Enter(Step.Catch);
    }

    void TickCatch(OctopusController c, bool locomote)
    {
        Swing.Pumping = false; restWant = 0f;
        if (AnyZip()) return;
        if (supporting) { supporting = false; Supports++; Enter(Step.Hang); return; }
        // the zip is done: transfer - the old hold goes, the body swings on under the new one
        for (int a = 0; a < Use.Length; a++)
        {
            if (Use[a] != ArmUse.Hold || a == lastZipped) continue;
            ReleaseArm(a);
        }
        Transfers++;
        if (dropping) { dropping = false; Drops++; playDoneId = playSeen; dropPhase = 3; Enter(Step.Hang); return; }
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
        if (s >= 0) Swing.RopeTarget[s] = creeping || supporting ? Swing.Rope[s] : dropping ? dropRope : HangRun * RunArc(a);
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

    // ------------------------------------------------------------------ play

    bool inPlace, playRun, playing, playDirChecked;

    // Many arms (a big body): a play swing goes where its arc is clear - the body's path (the rope extended by half the
    // body) at a half and the full amplitude, both ways (a circle: round the cone), cast as a chain from where the body
    // is; the move's own direction first, then turned by 60 deg steps. None clear: the clearest (fewest blocked links).
    Vector3 ClearSwingDir(OctopusController c, Vector3 dir, float amp, bool circle)
    {
        int one = Swing.Single;
        if (one < 0) return dir;
        Vector3 up = c.Up, P = Swing.Pivot[one];
        float l = (Swing.Position - P).magnitude + 0.5f * (body.Spec.MantleLength + body.Spec.HeadRadius) * S;
        Vector3 best = dir; int bestBlocked = int.MaxValue;
        for (int k = 0; k < 6 && bestBlocked > 0; k++)
        {
            float turn = (k + 1) / 2 * (k % 2 == 0 ? 1f : -1f) * 60f * Mathf.Deg2Rad;
            Vector3 f = OctoMath.Norm(Level(OctoMath.Rotate(dir, up, turn), up), Perp(up)), side = OctoMath.Cross(up, f);
            int blocked = 0;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 from = P - up * l;
                for (int j = 1; j <= 2; j++)
                {
                    float A = amp * j / 2f;
                    Vector3 h = circle ? (f * (sgn * Mathf.Cos(Mathf.PI * j / 4f)) + side * Mathf.Sin(Mathf.PI * j / 4f)) : f * sgn;
                    Vector3 to = P + (OctoMath.Norm(h, f) * Mathf.Sin(circle ? amp : A) - up * Mathf.Cos(circle ? amp : A)) * l;
                    Vector3 d = to - from;
                    float len = d.magnitude;
                    if (len > 1e-4f && c.Cast(from, d, len, out _)) blocked++;
                    from = to;
                }
            }
            if (blocked < bestBlocked) { bestBlocked = blocked; best = f; }
        }
        return best;
    }

    void StartMove(OctopusController c, PlayMove move, float amplitude)
    {
        Vector3 up = c.Up;
        playAmp = amplitude;
        float turn = (Rand() * 2f - 1f) * 70f * Mathf.Deg2Rad;
        playDir = OctoMath.Norm(OctoMath.Rotate(OctoMath.Norm(Level(facing, up), Perp(up)), up, turn), Perp(up));
        playSense = Rand() < 0.5f ? -1f : 1f;
        if (move == PlayMove.BigSwing || move == PlayMove.HighSwing) facingWant = playDir;
        if (move == PlayMove.Drop) { dropPhase = 0; dropping = false; lowIdx = -1; }
        playDirChecked = false;
    }

    void TickPlay(OctopusController c)
    {
        if (!inPlace) { Enter(Step.Hang); return; }
        if (Sling && Swing.HoldCount > 1 && !AnyZip() && !dropping) KeepOne(c, Swing.Position);
        if (Swing.Single < 0) { Swing.Pumping = false; restWant = PlayRestDamping / TS; return; }   // a sling: wait for one hold
        if (Sling && !playDirChecked && (Play == PlayMove.BigSwing || Play == PlayMove.HighSwing || Play == PlayMove.Circle))
        {
            playDirChecked = true;
            playDir = ClearSwingDir(c, playDir, playAmp, Play == PlayMove.Circle);
            if (Play != PlayMove.Circle) facingWant = playDir;
        }
        switch (Play)
        {
            case PlayMove.BigSwing:
            case PlayMove.HighSwing:
                Swing.SwingDir = playDir; Swing.TargetAmplitude = TargetAmplitude = playAmp; Swing.Pumping = true; restWant = 0f;
                break;
            case PlayMove.Circle:
                Swing.SwingDir = playDir; Swing.ConeAngle = playAmp; Swing.CircleSense = playSense;
                Swing.Circling = true; Swing.Pumping = false; restWant = 0f; lateralWant = 0f;
                break;
            case PlayMove.Drop:
                TickDrop(c);
                break;
        }
    }

    // A drop: a lower branch within reach (ScanLower) -> calm the swing -> reach down and catch it (Creep, TryCatch
    // with `dropping`: the new rope slack, the old hold kept until the zip is done) -> let go above (TickCatch) -> the
    // body falls on the slack arm and the soft rope catches it.
    void TickDrop(OctopusController c)
    {
        Swing.Pumping = false;
        restWant = (dropPhase >= 3 ? PlayRestDamping : RestDamping) / TS;
        if (dropPhase >= 2) return;
        int a = FreeArm();
        if (dropPhase == 0)
        {
            int r = ScanLower(c);
            if (r == 0) return;
            if (r < 0 || a < 0) { DropFailed(); Enter(Step.Hang); return; }
            dropPhase = 1;
        }
        if (a >= 0 && Swing.Amplitude < DropCalmDeg * Mathf.Deg2Rad)
        {
            next = dropHit; nextRadius = dropRadius;
            facingWant = OctoMath.Norm(Level(next.point - Pivot, c.Up), facingWant);
            dropping = true; dropPhase = 2;
            BeginReach(c, a, 0.9f * TS, Step.Creep);
        }
    }

    void DropFailed()
    {
        DropFailures++;
        dropFailedUntil = time + 20f * TS;
        playDoneId = playSeen;
        dropPhase = 3;
    }

    // A lower branch for a drop: a fan of casts from the crown (up, level and a little down), over frames. The hold must lie at least
    // DropBelowLengths L below the one held, within reach now, and give a fall of DropMinLengths L or more on a rope whose
    // swing stays under DropMaxAmplitudeDeg; nothing under it where the body will hang. 1 found, 0 scanning, -1 none.
    const int LowCasts = 32;
    int lowIdx = -1;
    float lowBest, lowRope;
    SurfaceHit lowHit; float lowR;
    bool lowFound;

    int ScanLower(OctopusController c)
    {
        int a = FreeArm();
        if (a < 0) { lowIdx = -1; return -1; }
        if (lowIdx < 0) { lowIdx = 0; lowBest = float.NegativeInfinity; lowFound = false; }
        Vector3 up = c.Up, crown = Swing.Position, P = Pivot;
        Vector3 t0 = Perp(up), t1 = OctoMath.Cross(up, t0);
        for (int n = 0; n < ScanPerFrame && lowIdx < LowCasts; n++, lowIdx++)
        {
            if (!c.Spare(5)) return 0;
            int e = lowIdx / 8, k = lowIdx % 8;
            float el = (60f - 25f * e) * Mathf.Deg2Rad, az = k * Mathf.PI / 4f + 0.3f;   // 60, 35, 10, -15 deg: the body hangs low, a lower branch is mostly above it
            Vector3 dir = (t0 * Mathf.Cos(az) + t1 * Mathf.Sin(az)) * Mathf.Cos(el) + up * Mathf.Sin(el);
            if (!c.Cast(crown, dir, 0.95f * L, out SurfaceHit h) || !c.HoldInside(h.point)) continue;
            if (OctoMath.Dot(P - h.point, up) < 0.1f * L) continue;   // (the branch held, or one as high)
            if (!ContactFromHit(c, h, out SurfaceHit top, out float r)) continue;
            if (OctoMath.Dot(P - top.point, up) < DropBelowLengths * L || Level(top.point - P, up).magnitude < 0.1f * L) continue;
            float cd = (top.point - crown).magnitude;
            if (cd > 0.85f * CatchStretch * 0.97f * ExpectedRun(r, a)) continue;
            float rope = DropRope(crown, top.point, up, HangRun * ExpectedRun(r, a)), fall = rope - cd;
            if (DebugTrace) DebugLog?.Invoke($"drop candidate {top.point} below {OctoMath.Dot(P - top.point, up):F2} dist {cd:F2} rope {rope:F2} fall {fall:F2}");
            if (fall < DropMinLengths * L) continue;
            if (fall > lowBest) { lowBest = fall; lowHit = top; lowR = r; lowRope = rope; lowFound = true; }
        }
        if (lowIdx < LowCasts) return 0;
        lowIdx = -1;
        if (!lowFound) return -1;
        // the way down is clear (nothing where the body will swing under the new hold)
        Vector3 from = lowHit.point - up * (2f * lowR + 0.05f * L);
        if (c.Cast(from, -up, lowRope + 0.3f * L, out _)) return -1;
        dropHit = lowHit; dropRadius = lowR;
        return 1;
    }

    // The swing a drop from rest at `crown` on a rope of `rope` from `piv` leaves: a free fall straight down until the
    // rope goes taut, its part along the rope braked away (the soft catch), the part across it swinging on.
    float DropAmplitude(Vector3 crown, Vector3 piv, Vector3 up, float rope)
    {
        Vector3 r = crown - piv;
        float y0 = OctoMath.Dot(r, up), dx = Level(r, up).magnitude;
        if (dx >= rope) return Mathf.PI;
        float y1 = -Mathf.Sqrt(rope * rope - dx * dx);
        if (y1 >= y0) return Mathf.Atan2(dx, -y0);
        float g = OctopusController.Gravity, vt2 = 2f * g * (y0 - y1) * dx * dx / (rope * rope);
        float e = 0.5f * vt2 + g * (rope + y1);
        return Mathf.Acos(Mathf.Clamp(1f - e / (g * rope), -1f, 1f));
    }

    // The longest rope up to `natural` whose drop swings at most DropMaxAmplitudeDeg.
    float DropRope(Vector3 crown, Vector3 piv, Vector3 up, float natural)
    {
        float d = (crown - piv).magnitude, max = DropMaxAmplitudeDeg * Mathf.Deg2Rad;
        for (float rope = natural; rope > d; rope *= 0.95f) if (DropAmplitude(crown, piv, up, rope) <= max) return rope;
        return d;
    }

    // A run: higher than the catch needs - up to the move's amplitude, but never past the highest apex still in reach.
    float RunAmplitude(Vector3 P, float l, Vector3 up, int a, float need)
    {
        Vector3 f = OctoMath.Norm(Level(next.point - P, up), facing);
        float want = Mathf.Max(need, playAmp), best = need;
        for (float deg = need * Mathf.Rad2Deg + 1f; deg <= MaxAmplitudeDeg - AmplitudeMarginDeg && best < want; deg += 1f)
        {
            float A = deg * Mathf.Deg2Rad;
            Vector3 at = P + (f * Mathf.Sin(A) - up * Mathf.Cos(A)) * l;
            if (!Reachable(at, a, 0.92f)) break;
            best = A;
        }
        return Mathf.Min(want, best);
    }

    // A surface hit the swinger could hang from (a branch it can wrap; not the ground, a wall or a thick trunk).
    public bool Holdable(OctopusController c, SurfaceHit h) => ContactFromHit(c, h, out _, out _);

    // ------------------------------------------------------------------ perch (stalking, feeding)

    // The hold over the prey: the stalk goal lies below it (OctopusBrain.CreepBelow).
    bool NearPreyHold(OctopusController c)
    {
        Vector3 piv = Pivot;
        Vector3 d = Level(goal - piv, c.Up);
        return d.magnitude < 0.35f * L;
    }

    void TickPerch(OctopusController c, bool locomote, bool perch)
    {
        Swing.Pumping = false; restWant = RestDamping / TS;
        if (!perch)
        {
            // done (eaten, or gave up): pay the rope back out and rest
            int h0 = HoldingArm();
            if (h0 >= 0 && slot[h0] >= 0 && !Sling) Swing.RopeTarget[slot[h0]] = HangRun * RunArc(h0);   // (a sling keeps its ropes: one paid out alone only went slack)
            Enter(Step.Hang);
            return;
        }
        if (locomote && creeping && !NearPreyHold(c) && StepTime > 1f * TS) { Enter(Step.Hang); return; }
        if (Sling) return;   // the octopus hangs still over its prey at its natural length (its arms strike from there)
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
        Swing.Pumping = false; restWant = 0f;
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
        if (2f * Mathf.PI * r * MaxCoilTurns > 0.45f * L) return false;
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
        // the tip arrives along the reach, from the crown: the coil's sense follows from the plan, not from where the
        // simulated arm happens to be (the arms are for show - nothing of theirs feeds back into the body's motion)
        Vector3 tipDir = OctoMath.Norm(Level(contact.point - Swing.Position, c.Up), Perp(c.Up));
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
        if (Sling && Swing.HoldCount > 0)
        {
            // many arms: the body hangs leaning off its holding arms (their side of the crown turned up toward the
            // holds, HoldLeanDeg): from a crown facing the hold square on, every holding arm turned ~90-110 deg at its
            // base inside the web, and the web there folded through itself (2026-10-20)
            Vector3 lean = Vector3.zero; int nh = 0;
            for (int a = 0; a < Use.Length; a++)
                if (Use[a] == ArmUse.Hold || Use[a] == ArmUse.Zip || Use[a] == ArmUse.Reach) { lean += OctoMath.Rotate(body.PlanRotation, OctopusShape.ArmDir(body.Spec, a)); nh++; }
            if (nh > 0)
            {
                lean -= want * OctoMath.Dot(lean, want);
                float ll = lean.magnitude;
                if (ll > 1e-3f) want = OctoMath.Norm(want - lean * (Mathf.Tan(HoldLeanDeg * Mathf.Deg2Rad) / ll), want);
            }
        }
        if (dt > 0f)
        {
            float w = OralOmega / TS;
            oralVel += ((want - oral) * (w * w) - oralVel * (2f * w)) * dt;
            oral = OctoMath.Norm(oral + oralVel * dt, want);
        }
        else { oral = want; oralVel = Vector3.zero; }
        // (many arms braced on two or more holds do not turn on them: a yaw twisted every holding arm about the crown
        // and folded the web between them - the facing waits for the next single-hold swing)
        if (dt > 0f && !(Sling && Swing.HoldCount >= 2))
        {
            float ang = Mathf.Acos(Mathf.Clamp(OctoMath.Dot(facing, facingWant), -1f, 1f)), step = FacingRateDeg * Mathf.Deg2Rad * dt / TS;
            if (ang > 1e-4f)
            {
                Vector3 ax = OctoMath.Cross(facing, facingWant);
                ax = ax.sqrMagnitude > 1e-10f ? ax.normalized : up;
                facing = OctoMath.Norm(OctoMath.Rotate(facing, ax, Mathf.Min(ang, step)), facing);
            }
        }
        else if (!(dt > 0f)) facing = facingWant;
        Vector3 bodyUp = -oral;
        Vector3 fw = facing - bodyUp * OctoMath.Dot(facing, bodyUp);
        return OctoMath.Look(OctoMath.Norm(fw, Perp(bodyUp)), bodyUp);
    }

    void ArmControl(OctopusController c, int a, Quaternion rot, float dt)
    {
        TentacleChain ch = body.Arms[a];
        Vector3 up = c.Up;
        // a holding arm is drawn from the body to its hold (OctopusBody.DrawHeld), sagging by the rope's slack
        body.ArmDrawn[a] = Use[a] == ArmUse.Hold;
        if (Sling)
        {
            // many arms: the reach and the zip are drawn too (curves from the root; the web between the arms stays smooth)
            body.ArmDrawn[a] = Use[a] == ArmUse.Hold || Use[a] == ArmUse.Zip || Use[a] == ArmUse.Reach;
            body.ArmReach[a] = Use[a] == ArmUse.Reach;
            body.ArmPosed[a] = Use[a] == ArmUse.Free || Use[a] == ArmUse.Groom;
            if (Use[a] != ArmUse.Free) posedSet[a] = false;   // (its base pose is rewritten when it comes back)
        }
        body.ArmSlack[a] = slot[a] >= 0 && Swing.Active[slot[a]] ? Mathf.Max(0f, Swing.Rope[slot[a]] - (Swing.Position - Swing.Pivot[slot[a]]).magnitude) : 0f;
        // (many arms: a few mm of slack are no sag - the sag goes as its square root, and a sling's ropes sit at a hair's
        // slack that came and went every tick, a flicker of the drawn runs)
        if (Sling) body.ArmSlack[a] = Mathf.Max(0f, body.ArmSlack[a] - 0.004f * S);
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
                if (Sling) body.ArmReachTarget[a] = tgt;
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
            case ArmUse.Strike:
            {
                // an eased fast reach at the intercept: the pull comes in over StrikeEaseSeconds, stretched to its limit
                float u = Mathf.Clamp01((time - strikeStart[a]) / (StrikeEaseSeconds * TS));
                ch.SetTargetShape(StrikeAt, 1f, true);
                ch.ShapePull = 0.25f * (0.3f + 0.7f * u * u * (3f - 2f * u)); ch.PullSpeed = 0f;
                ch.Tone = 3f; ch.CalmDamping = 0f; ch.SpeedCap = 0f;
                for (int j = 0; j < ch.N - 1; j++) ch.LengthTarget[j] = ch.Spec.MaxStretch;
                aimAt = StrikeAt;
                break;
            }
            case ArmUse.Prey:
                ch.ShapePull = 0f; ch.Tone = 1f; ch.CalmDamping = 0f; ch.SpeedCap = 0f;
                aimAt = ch.X[ch.N - 1];
                break;
            case ArmUse.Groom:
                aimAt = ch.X[ch.N - 1];
                break;
            case ArmUse.Free when Sling:
                // display only: on its pose (OctopusBody posed arm); the chain settings matter only while it eases in
                if (!posedSet[a]) ch.ClearTargetShape();
                if (!posedSet[a] || poseIsNear[a] != NeighbourBusy(a)) ApplyPose(a);
                ch.ShapePull = 0f; ch.Tone = FreeTone; ch.CalmDamping = FreeCalm; ch.SpeedCap = FreeSpeedCap * TS;
                aimAt = ch.X[ch.N - 1];
                break;
            default:
            {
                // hanging loosely beside and below the body, damped relative to it: it trails the swing
                ch.ShapePull = 0f; ch.Tone = FreeTone; ch.CalmDamping = FreeCalm; ch.SpeedCap = FreeSpeedCap * TS;
                ResetLengths(ch);
                Vector3 root = ch.X[0], side = OctoMath.Norm(Level(root - Swing.Position, up), OctoMath.Rotate(rot, OctopusShape.ArmDir(body.Spec, a)));
                // playing, it is flung out to the side and a little ahead (eased in and out: FreeOpen)
                float o = FreeOpen;
                Vector3 tgt = root + (side * (0.45f + 0.35f * o) - up * (0.8f - 0.55f * o) - facing * (0.1f - 0.3f * o)) * (0.8f * L);
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
            b.Role[a] = Use[a] == ArmUse.Hold ? ArmRole.Anchor : Use[a] == ArmUse.Zip || Use[a] == ArmUse.Reach ? ArmRole.Grip : Use[a] == ArmUse.Peel ? ArmRole.Rest
                      : Use[a] == ArmUse.Strike || Use[a] == ArmUse.Prey ? ArmRole.Strike : Use[a] == ArmUse.Groom ? ArmRole.Groom
                      : Sling && Curled(a) ? ArmRole.Tuck : ArmRole.Hang;
            b.Striking[a] = Use[a] == ArmUse.Strike;
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
        for (int a = 0; a < Use.Length && rest; a++) if (Use[a] == ArmUse.Reach || Use[a] == ArmUse.Peel || Use[a] >= ArmUse.Strike) rest = false;
        // many arms: not before the sling is taken (or none is within reach for now)
        if (Sling && rest && !playing && Swing.HoldCount < RestHolds && time >= supportNext && (c.Intent.mode == OctoMode.Rest || locomote && Arrived && !creeping)) rest = false;
        if (Sling && rest && c.Intent.mode == OctoMode.Strike) rest = false;
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

    // ------------------------------------------------------------------ many arms: the sling

    // The free arm whose root lies nearest p (-1: none); one beside a busy arm counts AdjacentPenalty x L farther
    // (two neighbours working in different directions fold the web between them). (Choosing by the arm's facing
    // instead - the least turn at its base - was tried: it changed which arms hold the sling and folded the web more.)
    public const float AdjacentPenalty = 0.15f;
    int NearestFree(Vector3 p)
    {
        int best = -1; float bd = float.MaxValue;
        for (int a = 0; a < Use.Length; a++)
        {
            if (Use[a] != ArmUse.Free) continue;
            float d = (body.Arms[a].X[0] - p).magnitude + (NeighbourBusy(a) ? AdjacentPenalty * L : 0f);
            if (d < bd) { bd = d; best = a; }
        }
        return best;
    }

    bool AnyBusy()
    {
        for (int a = 0; a < Use.Length; a++)
            if (Use[a] == ArmUse.Reach || Use[a] == ArmUse.Zip || Use[a] == ArmUse.Peel || Use[a] == ArmUse.Strike || Use[a] == ArmUse.Prey) return true;
        return false;
    }

    // Every rope keeps the length it has (a sling of ropes that each reel toward their own length can be infeasible).
    void FreezeRopes()
    {
        for (int i = 0; i < PlannedSwing.MaxHolds; i++) if (Swing.Active[i]) Swing.RopeTarget[i] = Swing.Rope[i];
    }

    // Off the sling onto one hold: the hold whose pivot lies nearest p (level; the crown: the one most nearly overhead,
    // the gentlest start) stays, every other one peels off.
    void KeepOne(OctopusController c, Vector3 p)
    {
        Vector3 up = c.Up;
        int keepArm = -1; float bd = float.MaxValue;
        for (int a = 0; a < Use.Length; a++)
        {
            if (Use[a] != ArmUse.Hold || slot[a] < 0) continue;
            float d = Level(Swing.Pivot[slot[a]] - p, up).sqrMagnitude;
            if (d < bd) { bd = d; keepArm = a; }
        }
        if (keepArm < 0) return;
        for (int a = 0; a < Use.Length; a++) if (a != keepArm && Use[a] == ArmUse.Hold) ReleaseArm(a);
        // the kept rope taut at the distance there is and kept so (no slack to fall into, no reel: the body swings
        // off from where it hung; a rope paid out under a reach sank the body out of it)
        int s = slot[keepArm];
        Swing.Rope[s] = Mathf.Min(Swing.Rope[s], (Swing.Position - Swing.Pivot[s]).magnitude);
        Swing.RopeTarget[s] = Swing.Rope[s];
    }

    // Resting with fewer than RestHolds holds: a support hold is looked for (over frames) and reached for slowly.
    void TrySupport(OctopusController c)
    {
        if (Swing.HoldCount == 0 || Swing.HoldCount >= RestHolds || time < supportNext || Grounded || AnyBusy()) { supportIdx = -1; return; }
        if (Swing.Amplitude > 6f * Mathf.Deg2Rad) return;
        int r = ScanSupport(c, false);
        if (r == 0) return;
        int a = r > 0 ? NearestFree(supportHit.point) : -1;
        if (a < 0) { supportNext = time + SupportRetrySeconds * TS; return; }
        next = supportHit; nextRadius = supportR; supporting = true; HasNext = false;
        BeginReach(c, a, CreepReachSeconds * TS, Step.Creep);
    }

    // A support hold. With branch hints: the hinted branches around the crown first (one probe each for the top). Then
    // (or probe-only) a ring of casts from the crown at points around it - SupportAzimuths round, at two level
    // distances, at the held pivot's height and a little above and below (aimed at points, not directions: a thin bar
    // slips between the rays of a fan) - all at once or ScanPerFrame per frame within the query budget. A branch the
    // arm can wrap, within reach, above the crown, not near a hold already taken; best: the one farthest round from
    // the holds there are (a spread sling), then the nearer. 1 found, 0 scanning, -1 none.
    const int SupportAzimuths = 16, SupportCasts = SupportAzimuths * 6;
    static readonly float[] supportRing = { 0.35f, 0.6f }, supportRise = { 0f, 0.12f, -0.12f };
    readonly BranchHint[] supportHints = new BranchHint[8];
    int supportIdx = -1;
    float supportBest, supportR;
    SurfaceHit supportHit;
    bool supportFound;

    int ScanSupport(OctopusController c, bool all)
    {
        Vector3 up = c.Up, crown = Swing.Position, P = Pivot;
        if (supportIdx < 0)
        {
            supportIdx = 0; supportBest = float.NegativeInfinity; supportFound = false;
            if (c.Hints != null)
            {
                int n = c.Hints.Query(crown, up, -1f, 0.3f * L, CatchStretch * L, body.Spec.arm.TipRadius * S * 1.5f, 2f * L, supportHints);
                for (int i = 0; i < n; i++)
                {
                    if (NearHold(supportHints[i].point, 0.7f)) continue;
                    if (Mathf.Abs(OctoMath.Dot(supportHints[i].axis, up)) > BranchRoute.MaxAxisRise) continue;
                    if (TopContact(c, supportHints[i].point, supportHints[i].radius, out SurfaceHit top)) Candidate(c, top, supportHints[i].radius);
                }
                if (supportFound) { supportIdx = -1; return 1; }
                if (!all) return 0;   // (the casts from the next frame)
            }
        }
        Vector3 t0 = Perp(up), t1 = OctoMath.Cross(up, t0);
        for (int n = 0; (all || n < ScanPerFrame) && supportIdx < SupportCasts; n++, supportIdx++)
        {
            if (!all && !c.Spare(5)) return 0;
            int k = supportIdx % SupportAzimuths, j = supportIdx / SupportAzimuths, ri = j % 2, hi = j / 2;
            float az = k * 2f * Mathf.PI / SupportAzimuths + 0.1f;
            Vector3 aim = crown + (t0 * Mathf.Cos(az) + t1 * Mathf.Sin(az)) * (supportRing[ri] * L) + up * (OctoMath.Dot(P - crown, up) + supportRise[hi] * L);
            Vector3 d = aim - crown;
            float len = d.magnitude;
            if (!c.Cast(crown, d, len + 0.15f * L, out SurfaceHit h) || !c.HoldInside(h.point)) continue;
            if (NearHold(h.point, 0.8f) || supportFound && (h.point - supportHit.point).magnitude < SupportSpacing * L) continue;   // (cheap: before the axis probes)
            if (!ContactFromHit(c, h, out SurfaceHit top, out float r)) continue;
            Candidate(c, top, r);
        }
        if (supportIdx < SupportCasts) return 0;
        supportIdx = -1;
        return supportFound ? 1 : -1;
    }

    bool NearHold(Vector3 p, float f)
    {
        for (int i = 0; i < PlannedSwing.MaxHolds; i++) if (Swing.Active[i] && (Swing.Pivot[i] - p).magnitude < f * SupportSpacing * L) return true;
        return false;
    }

    void Candidate(OctopusController c, SurfaceHit top, float r)
    {
        Vector3 up = c.Up, crown = Swing.Position;
        float cd = (top.point - crown).magnitude;
        if (DebugTrace) DebugLog?.Invoke($"support candidate {top.point} cd {cd / L:F2} L rise {OctoMath.Dot(top.point - crown, up) / L:F2} L reach {CatchStretch * 0.97f * ExpectedRun(r, 0) / L:F2} L");
        if (cd < 0.3f * L || cd > CatchStretch * 0.97f * ExpectedRun(r, 0)) return;
        if (OctoMath.Dot(top.point - crown, up) < 0.1f * L || !c.HoldInside(top.point) || NearHold(top.point, 1f)) return;
        Vector3 held = OctoMath.Norm(Level(Pivot - crown, up), Vector3.zero);
        float score = -OctoMath.Dot(OctoMath.Norm(Level(top.point - crown, up), Vector3.zero), held) - 0.3f * cd / L;
        if (score > supportBest) { supportBest = score; supportHit = top; supportR = r; supportFound = true; }
    }

    // ------------------------------------------------------------------ many arms: the hunt

    void TickHunt(OctopusController c, in OctoIntent it)
    {
        IPreyTarget prey = it.prey;
        bool strike = it.mode == OctoMode.Strike && prey != null;
        if (strike) StrikeAt = it.intercept;
        if (strike && !strikeLaunched && heldPrey == null && prey.CanBeGrabbed) { strikeLaunched = true; Launch(Mathf.Max(1, it.strikers)); }
        if (it.mode != OctoMode.Strike) strikeLaunched = false;
        for (int a = 0; a < Use.Length; a++)
        {
            TentacleChain ch = body.Arms[a];
            if (Use[a] == ArmUse.Strike)
            {
                if (strike && heldPrey == null && prey.CanBeGrabbed)
                {
                    prey.GrabVolume(out Vector3 pa, out Vector3 pb, out float r);
                    Vector3 tip = ch.X[ch.N - 1];
                    if ((tip - ClosestOnSegment(tip, pa, pb)).magnitude < r + 0.08f * S)
                    {
                        heldPrey = prey; holdUntil = c.Time + c.HoldSeconds;
                        Use[a] = ArmUse.Prey;
                        c.NotifyGrab(prey);
                        HoldPrey(a);   // wrapped at once: the strike's momentum must not carry the tip off for a frame
                        continue;
                    }
                }
                if (!strike || heldPrey != null || time - strikeStart[a] > 2.5f * ch.Spec.StrikeTime * TS)
                {
                    if (heldPrey == null) StrikeMisses++;
                    Recover(a);
                }
            }
            else if (Use[a] == ArmUse.Prey)
            {
                if (heldPrey == null || c.Time > holdUntil || it.mode == OctoMode.Retreat)
                {
                    IPreyTarget p = heldPrey; heldPrey = null;
                    Recover(a);
                    if (p != null) c.NotifyRelease(p);
                }
                else HoldPrey(a);
            }
        }
    }

    // Up to n free (or grooming) arms whose roots lie nearest the intercept, within a stretched arm's reach of it.
    void Launch(int n)
    {
        for (int i = 0; i < n; i++)
        {
            int best = -1; float bd = float.MaxValue;
            for (int a = 0; a < Use.Length; a++)
            {
                if (Use[a] != ArmUse.Free && Use[a] != ArmUse.Groom) continue;
                TentacleChain ch = body.Arms[a];
                float d = (StrikeAt - ch.X[0]).magnitude;
                if (d <= 0.95f * ch.Spec.MaxStretch * ch.Length && d < bd) { bd = d; best = a; }
            }
            if (best < 0) return;
            if (Use[best] == ArmUse.Groom) groomArm = -1;
            TentacleChain sc = body.Arms[best];
            sc.UnpinAll();
            sc.ShapeRate = TentacleChain.FastShapeRate;
            Use[best] = ArmUse.Strike; strikeStart[best] = time;
            Strikes++;
        }
    }

    void Recover(int a)
    {
        TentacleChain ch = body.Arms[a];
        ch.UnpinAll();
        ch.ClearTargetShape();
        ResetLengths(ch);
        ch.ShapeRate = TentacleChain.DefaultShapeRate;
        ch.Tone = FreeTone;
        Use[a] = ArmUse.Free;
    }

    // The holding arm's tip coils round the prey's grab volume (the wrap re-planned on the capsule every tick).
    void HoldPrey(int a)
    {
        TentacleChain ch = body.Arms[a];
        heldPrey.GrabVolume(out Vector3 pa, out Vector3 pb, out float r);
        Vector3 tip = ch.X[ch.N - 1];
        Vector3 contact = OctoMath.Norm(tip - ClosestOnSegment(tip, pa, pb), Vector3.up);
        int avail = Mathf.Min(ch.N - 8, 14);
        WrapMarch w = wrap[a];
        w.PlanAroundCapsule(ch, ch.N - 1, pa, pb, r, contact, 1f, avail);
        for (int i = 0; i < w.Count; i++) ch.Pin(w.Particle[i], w.Point[i], w.Normal[i], SurfaceHit.NoSource);
        for (int j = 0; j < ch.N - 1; j++) ch.LengthTarget[j] = ch.Pinned[j + 1] ? 1f : 0.95f;
    }

    static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float l2 = ab.sqrMagnitude;
        return l2 > 1e-12f ? a + ab * Mathf.Clamp01(OctoMath.Dot(p - a, ab) / l2) : a;
    }

    // ------------------------------------------------------------------ many arms: idle poses, grooming

    // Arm a's idle pose in body space (x size; the mantle is +Y, hanging down): it leaves the crown along its rest
    // direction, bends down past the mantle and hangs there, its tip curled in - a third of the arms (1, 4, 7) curl up
    // tightly instead. Every arm takes the same bend over its first RootBend of length (the web skirt spans neighbours
    // there: two neighbours bending differently inverted it); past it each arm's droop, splay and curl differ a little
    // (by its index). Kept off the shell.
    // Beside a busy arm (holding, reaching, striking: it leaves the crown toward its work, the opposite way) the idle
    // arm stays out along its rest direction a little longer (near: RootStraightNear .. RootBendNear) so the web
    // between them is not folded through itself.
    const float RootStraight = 0.1f, RootBend = 0.4f, RootStraightNear = 0.25f, RootBendNear = 0.55f, RootBendDeg = 80f;
    void BuildPose(int a, Vector3[] p, bool near)
    {
        CephalopodSpec sp = body.Spec;
        TentacleChain ch = body.Arms[a];
        float r0 = near ? RootStraightNear : RootStraight, r1 = near ? RootBendNear : RootBend;
        int n = ch.N;
        float h = (Mathf.Sin(a * 12.9898f + 1.7f) * 43758.5453f) % 1f; if (h < 0f) h += 1f;   // a fixed per-arm variety
        bool curl = a % 3 == 1;
        float hangDeg = curl ? 30f : 4f + 12f * h;   // past the root bend
        float curlFrom = curl ? (near ? 0.6f : 0.45f) : 0.66f + 0.08f * h, curlDeg = curl ? 300f : 70f + 60f * h;
        float splay = (h - 0.5f) * 24f * Mathf.Deg2Rad;
        Vector3 d = OctopusShape.ArmDir(sp, a), up = Vector3.up;
        p[0] = OctopusShape.ArmRoot(sp, a) * S;
        float sp1 = ch.Spacing;
        for (int k = 0; k < n - 1; k++)
        {
            float s = (k + 0.5f) / (n - 1);
            float u = Mathf.Clamp01((s - r0) / (r1 - r0)), th = RootBendDeg * u * u * (3f - 2f * u);
            float w = Mathf.Clamp01((s - r1) / 0.15f);
            w = w * w * (3f - 2f * w);
            th += hangDeg * w;
            float v = Mathf.Clamp01((s - curlFrom) / (1f - curlFrom));
            th += curlDeg * v * v;
            float t = th * Mathf.Deg2Rad;
            Vector3 dd = OctoMath.Rotate(d, up, splay * w);
            Vector3 q = p[k] + (dd * Mathf.Cos(t) + up * Mathf.Sin(t)) * sp1;
            // off the mantle and head (a little more than the arm's radius there)
            float clear = (ch.Spec.RadiusAt(ch.Spec.Spacing * (k + 1)) * 1.6f + 0.01f);
            float dist = OctopusShape.ShellDistance(sp, q / S, out Vector3 outward);
            if (dist < clear) q += outward * ((clear - dist) * S);
            p[k + 1] = q;
        }
    }

    bool Curled(int a) => a % 3 == 1;

    // A neighbour around the crown is holding, reaching, striking or letting go.
    bool NeighbourBusy(int a)
    {
        int n = Use.Length, l = (a + n - 1) % n, r = (a + 1) % n;
        return Busy(Use[l]) || Busy(Use[r]);
    }

    static bool Busy(ArmUse u) => u != ArmUse.Free && u != ArmUse.Groom;

    void ApplyPose(int a)
    {
        bool near = NeighbourBusy(a);
        SetPose(a, near ? poseNear[a] : poseBase[a]);
        poseIsNear[a] = near; posedSet[a] = true;
    }

    void SetPose(int a, Vector3[] src)
    {
        Vector3[] dst = body.ArmPoseLocal[a];
        for (int k = 0; k < dst.Length; k++) dst[k] = src[k];
    }

    // At rest (once per rest, by chance): one idle arm strokes its tip slowly over the mantle beside it, two or three
    // strokes, eased in and out, then hangs again. Display only (the pose moves; nothing is simulated).
    void TickGroom(OctopusController c)
    {
        bool rest = Mode == Step.Hang && c.Intent.mode == OctoMode.Rest && !playing && !body.Asleep;
        if (c.Intent.mode != OctoMode.Rest) groomed = false;
        if (groomArm >= 0)
        {
            int a = groomArm;
            if (Use[a] != ArmUse.Groom) { groomArm = -1; return; }
            if (!rest || time >= groomEnd) { EndGroom(); return; }
            float t = time - groomStart, total = groomEnd - groomStart, ease = GroomEaseSeconds * TS;
            float amt = Mathf.Clamp01(Mathf.Min(t, total - t) / ease);
            amt = amt * amt * (3f - 2f * amt);
            float stroke = Mathf.Max(0f, t - ease) / (GroomStrokeSeconds * TS);
            float tri = 1f - Mathf.Abs(2f * (stroke - Mathf.Floor(stroke)) - 1f);
            tri = tri * tri * (3f - 2f * tri);
            Vector3 g = groomFrom + (groomTo - groomFrom) * tri;
            Vector3[] src = poseBase[a], dst = body.ArmPoseLocal[a];
            int n = dst.Length;
            Vector3 off = g - src[n - 1];
            for (int k = 0; k < n; k++)
            {
                float s = (float)k / (n - 1), w = Mathf.Clamp01((s - 0.45f) / 0.5f);
                dst[k] = src[k] + off * (amt * w * w * (3f - 2f * w));
            }
            return;
        }
        if (!rest || groomed || AnyBusy() || Swing.Velocity.magnitude > 2f * SleepSpeed * TS) return;
        if (Swing.HoldCount < RestHolds && time >= supportNext) return;   // the sling first
        groomed = true;
        if (Rand() >= GroomChance) return;
        int pick = (int)(Rand() * Use.Length), arm = -1;
        for (int i = 0; i < Use.Length && arm < 0; i++) { int k = (pick + i) % Use.Length; if (Use[k] == ArmUse.Free && body.ArmPose[k] >= 1f && !Curled(k) && !NeighbourBusy(k)) arm = k; }
        if (arm < 0) return;
        // the stroke: along the mantle beside the arm's hanging part, a little off the skin
        CephalopodSpec sp = body.Spec;
        Vector3 mid = poseBase[arm][(int)(0.6f * (body.Arms[arm].N - 1))] / S;
        float bt = 0.5f, bphi = 0f, bd = float.MaxValue;
        for (int i = 0; i <= 6; i++)
            for (int j = 0; j < 16; j++)
            {
                float tt = 0.3f + 0.08f * i, ph = j * Mathf.PI / 8f;
                float dd = (OctopusShape.ShellPoint(sp, tt, ph) - mid).sqrMagnitude;
                if (dd < bd) { bd = dd; bt = tt; bphi = ph; }
            }
        float lift = 1f + 2.5f * body.Arms[arm].Spec.TipRadius / Mathf.Max(sp.MantleRadius, 1e-3f);
        groomFrom = OctopusShape.ShellPoint(sp, Mathf.Max(0.2f, bt - 0.15f), bphi, lift) * S;
        groomTo = OctopusShape.ShellPoint(sp, Mathf.Min(0.85f, bt + 0.15f), bphi, lift) * S;
        groomStrokes = 2 + (int)(Rand() * 2f);
        groomArm = arm; groomStart = time;
        groomEnd = time + (2f * GroomEaseSeconds + groomStrokes * GroomStrokeSeconds) * TS;
        Use[arm] = ArmUse.Groom; Grooms++;
    }

    void EndGroom()
    {
        int a = groomArm;
        groomArm = -1;
        if (a >= 0 && Use[a] == ArmUse.Groom) { Use[a] = ArmUse.Free; ApplyPose(a); }
    }

    // ------------------------------------------------------------------ helpers

    static Vector3 Level(Vector3 v, Vector3 up) => v - up * OctoMath.Dot(v, up);
    static Vector3 Perp(Vector3 up) => OctoMath.Norm(OctoMath.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.forward);
}
