using System;
using System.Runtime.CompilerServices;
using CreatureKit;
using UnityEngine;

// Procedural creature spine: a steered head drags a bend-limited chain that rides the ground.
// Port of the 2D LizardSim; legs live in CreatureLegs.
// Creature kit (Kit/PartRuntime.cs): the species' locomotion modes (flight) are asked first each sub-step and
// walking runs when none claims it; parts that move on their own (antennae) and the eyes are part runtimes,
// stepped at their stage (AfterChain, AfterGround, AfterLimbs, Late) and writing their own bones.
public sealed class CreatureSim
{
    const float kMaxDt = 0.1f;
    const float kGroundWindow = 0.5f;
    const float kGroundTau = 0.06f;
    const float kMaxSink = 0.005f;
    const float kSwayFull = 0.75f;  // girdle reach difference (leg lengths) that gives full sway
    const float kSwayTau = 0.08f;   // smoothing of the sway and roll signals, seconds
    const int kRollSpread = 3;      // a girdle's roll fades out over this many joints each way
    // Swing guard: the legs assume the spine only moves by travelling (follow-the-leader). A chain coiled against
    // its bend cones and turned through a rigid neck amplifies the turn down the body instead (a 1 deg head turn
    // in place swung the winged serpent's hips 25-50 mm and 10-15 deg in a tick: its short hind legs could not
    // step while the other gait group was in the air, so the IK dragged a planted foot and folded the leg root).
    // Walking, a sub-step that would move a girdle further than (kSwingTravel x max(speed, top speed) +
    // kSwingCreep x size) x dt is retried with its turn halved, then none, then (the last try) held still.
    // Every legacy species walks within 0.75 of the bound in the golden runs (bit-identical); the monitor's
    // tight-turn wander reaches it on ~10 sub-steps a minute (its own, milder whip).
    const float kSwingTravel = 2f, kSwingCreep = 0.1f;
    const int kSwingTries = 4;
    const float kRunLo = 0.25f, kRunHi = 0.6f; // climbing: segment run/length below which its plane direction fades out
    const int kPitchPasses = 4;     // pitch-fold limiter passes (each followed by a length restore)

    public readonly CreatureSpecies Species;
    public readonly float SizeScale;
    // Gait time scale, sqrt(SizeScale) (Froude similarity: a bigger animal of the same build walks with
    // slower steps at a speed ~ sqrt(size)). Speed caps and step times scale with it, turn and
    // undulation rates inversely; accelerations do not.
    public readonly float TimeScale;
    public readonly Vector3[] Joints;
    public readonly Quaternion[] Rotations;
    public readonly Vector3[] GroundNormals;
    public EnclosureArea? Bounds;

    public readonly LegState[] Legs;
    public int StepsTaken;     // cumulative, for tests/stats

    // Actual head speed (after the Bounds clamp), not the commanded one.
    public float Speed { get; private set; }
    public float Time { get; private set; }

    readonly IGroundProbe ground;
    readonly IGroundProbe spineGround; // the ground the spine rests on: smooth over edges (see CachedGroundProbe.Smooth)
    readonly Func<Vector3, Vector3> upAt;
    readonly Vector3[] groundPoint;
    readonly Vector3[] pathUp;       // up at each path joint, from the grounding pass (the next chain step reads it)
    // The follow-the-leader body path (chain + ground). Joints is this path with the gait sway applied.
    internal readonly Vector3[] Path;
    readonly Vector3[] pathStart;    // swing guard: the path before this sub-step's chain step
    readonly int[] girdles;          // attach joints with legs on both sides, ascending
    readonly float[] swaySignal, rollSignal, swayAmp; // per girdle: smoothed -1..1 signals, amplitude (m)
    readonly float[] swayY, rollDeg; // per joint: lateral offset (m, +right) and roll (deg)
    readonly float[] pitchLift;      // pitch-fold limiter scratch
    readonly CreatureLegs legs;
    readonly CreatureClimb climb;    // null: this species cannot climb, or the ground is not an ISurfaceProbe
    readonly int n;
    Vector3 heading;
    Vector3 headUp;                  // the head's up the heading was last made tangent to (parallel transport)
    float commanded;
    float bobY, bobV;                // gait bob (species gaitBob): the body's height offset (m, + up) and its rate

    // Climbing (CreatureClimb): only for a species with climb.canClimb on an ISurfaceProbe ground. Up is the up
    // each joint grounds along (gravity's while the creature walks), ClimbWeight how far it follows the
    // surface instead of gravity, Source what it stands on (0 = terrain, else a collider's instance id).
    public readonly Vector3[] Up;
    public readonly float[] ClimbWeight;
    public readonly int[] Source;
    public bool CanClimb => climb != null;
    public bool Climbing => climb != null && climb.Active;
    // A surface ahead is past the species' maxSurfaceAngle (an overhang): the creature brakes to a stop.
    public bool Blocked => climb != null && climb.Blocked;
    // Off: colliders are ignored (the creature walks on terrain only, through plants as before); terrain walls
    // are still climbed.
    public bool ClimbAllowed = true;
    public Vector3 Heading => heading;
    // The surfaces the creature probes (null when it cannot climb): casts for climb goals.
    public ISurfaceProbe Surface => climb != null ? climb.Surface : null;
    internal CreatureClimb Climb => climb;
    internal float LegThrottle => legs.Throttle; // stats/tests: the gait's speed throttle (1 = free)
    public float Steepness(int joint) => climb != null && climb.Active ? climb.Steep[joint] : 0f;

    // Flight (Flight/CreatureFlight): only for a species that CanFly - the Fly module's locomotion mode. Ground mode
    // is the walking code above, untouched; the other modes run the flight model, wings and air chain (see
    // CreatureFlight), which also hooks the walking step (drape, launch / touchdown blends, arm bones).
    readonly CreatureFlight flight;
    // Creature kit: the locomotion modes (highest priority first) and the part runtimes, all and by stage.
    readonly ILocomotionMode[] modes;
    readonly IPartRuntime[] runtimes, afterChain, afterGround, afterLimbs, late;
    public IPartRuntime[] PartRuntimes => runtimes;
    public CreatureFlight Flight => flight;
    public FlightMode Mode => flight != null ? flight.Mode : FlightMode.Ground;
    // Per joint: the last grounding found ground within its window (a perched tail over a branch end does not).
    public readonly bool[] Supported;
    // A flyer's per-joint probed ground (the raw probe answer, before smoothing), for its drape; null otherwise.
    internal readonly Vector3[] ProbedGround;
    internal Vector3[] PathUp => pathUp;
    internal CreatureLegs LegsInternal => legs;
    internal float Commanded => commanded;

    public CreatureSim(CreatureSpecies sp, IGroundProbe ground, Func<Vector3, Vector3> upAt,
                       Vector3 position, Vector3 forward, float sizeScale = 1f, int seed = 0)
    {
        sp = sp.Compiled.Runtime;   // creature kit: the runtime view (a legacy asset is its own)
        Species = sp; this.ground = ground; this.upAt = upAt; SizeScale = sizeScale;
        spineGround = ground is CachedGroundProbe cache ? cache.Smooth
                    : ground is CompositeSurfaceProbe comp ? comp.Smooth : ground;
        TimeScale = Mathf.Sqrt(Mathf.Max(1e-4f, sizeScale));
        n = sp.JointCount;
        Joints = new Vector3[n]; Rotations = new Quaternion[n];
        GroundNormals = new Vector3[n]; groundPoint = new Vector3[n]; pathUp = new Vector3[n];

        Vector3 up = Norm(upAt(position), Vector3.up);
        heading = Tangent(forward, up);
        headUp = up;
        float step = sp.spacing * sizeScale;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = position - heading * (step * i);
            Vector3 u = Norm(upAt(p), up);
            float clear = sp.joints[i].clearance * sizeScale;
            if (!ground.Probe(p, u, kGroundWindow * sizeScale, out Vector3 g, out Vector3 nrm)) { g = p - u * clear; nrm = u; }
            groundPoint[i] = g; GroundNormals[i] = Norm(nrm, u); pathUp[i] = u;
            Joints[i] = g + u * clear;
        }
        Path = (Vector3[])Joints.Clone();
        pathStart = new Vector3[n];
        pitchLift = new float[n];
        swayY = new float[n]; rollDeg = new float[n];
        girdles = FindGirdles(sp);
        swaySignal = new float[girdles.Length]; rollSignal = new float[girdles.Length]; swayAmp = new float[girdles.Length];
        float tanSway = Mathf.Tan(Mathf.Clamp(sp.gaitSwayDeg, 0f, 45f) * Mathf.Deg2Rad);
        for (int g = 0; g < girdles.Length; g++)
        {
            // half the trunk to the nearest node: the neighbouring girdle, or the head for a lone girdle
            int d = girdles[g];
            if (g > 0) d = Mathf.Min(d, girdles[g] - girdles[g - 1]);
            if (g + 1 < girdles.Length) d = Mathf.Min(d, girdles[g + 1] - girdles[g]);
            // smoothstep between nodes peaks at slope 1.5 * 2A / D = tan(gaitSwayDeg)
            swayAmp[g] = d * step * tanSway / 3f;
        }
        if (sp.climb.canClimb && ground is ISurfaceProbe surface) climb = new CreatureClimb(sp, surface, sizeScale, TimeScale);
        if (climb != null)
        {
            Up = climb.Up; ClimbWeight = climb.Weight; Source = climb.Source;
            Array.Copy(pathUp, Up, n);
        }
        else { Up = pathUp; ClimbWeight = new float[n]; Source = new int[n]; }
        UpdateFrames();
        legs = new CreatureLegs(sp, ground, upAt, Joints, Rotations, sizeScale, TimeScale, climb);
        Legs = legs.State;
        Supported = new bool[n];
        if (sp.CanFly) ProbedGround = new Vector3[n];
        for (int i = 0; i < n; i++) Supported[i] = true;
        // creature kit: modes from the species' modules (Fly -> CreatureFlight), runtimes from its parts (Eyes, the
        // Antenna strands), in part order
        modes = KitRuntime.Modes(this, KitView.Modules(sp), new ModeCtx(ground, upAt));
        for (int m = 0; m < modes.Length && flight == null; m++) flight = modes[m] as CreatureFlight;
        runtimes = KitRuntime.Create(this, KitView.Parts(sp));
        afterChain = KitRuntime.OfStage(runtimes, PartStage.AfterChain);
        afterGround = KitRuntime.OfStage(runtimes, PartStage.AfterGround);
        afterLimbs = KitRuntime.OfStage(runtimes, PartStage.AfterLimbs);
        late = KitRuntime.OfStage(runtimes, PartStage.Late);
        for (int r = 0; r < runtimes.Length && antennae == null; r++) antennae = runtimes[r] as CreatureAntennae;
    }

    // Antennae (the kit's Antenna: whiskers, barbels): spring chains stepped after the body each sub-step (a part
    // runtime, stage AfterLimbs); null for a species without any.
    readonly CreatureAntennae antennae;
    public CreatureAntennae Antennae => antennae;

    void RunStage(IPartRuntime[] stage, float dt)
    {
        var c = new StepCtx(this);
        for (int i = 0; i < stage.Length; i++) stage[i].Step(c, dt);
    }

    // Grows [lo, hi] by every part runtime's points (antenna strands).
    public void EncapsulatePartBounds(ref Vector3 lo, ref Vector3 hi)
    {
        for (int i = 0; i < runtimes.Length; i++) runtimes[i].EncapsulateBounds(ref lo, ref hi);
    }

    static int[] FindGirdles(CreatureSpecies sp)
    {
        int c = sp.legs != null ? sp.legs.Length : 0, count = 0;
        var tmp = new int[c];
        for (int j = 0; j < sp.JointCount; j++)
        {
            bool left = false, right = false;
            for (int l = 0; l < c; l++)
                if (sp.legs[l].attachJoint == j && !sp.legs[l].feeler) { if (sp.legs[l].side < 0) left = true; else right = true; }
            if (left && right) tmp[count++] = j;
        }
        var r = new int[count];
        Array.Copy(tmp, r, count);
        return r;
    }

    public void Tick(float dt, Vector3 target)
    {
        if (!(dt > 0f)) dt = 0f;
        dt = Mathf.Clamp(dt, 0f, kMaxDt);
        int sub = Mathf.Max(1, Species.subSteps);
        float h = dt / sub;
        // every sub-step ends with fresh frames (the legs read them); only a zero dt needs them here
        if (h > 0f)
        {
            for (int s = 0; s < sub; s++) SubStep(h, target);
            if (late.Length != 0) RunStage(late, dt);
        }
        else UpdateFrames();
    }

    void SubStep(float dt, Vector3 target)
    {
        if (!(dt > 0f)) return;
        Time += dt;
        bool claimed = false;
        for (int m = 0; m < modes.Length && !claimed; m++) claimed = modes[m].SubStep(this, dt, target);
        if (!claimed) GroundSubStep(dt, target);
        if (afterLimbs.Length != 0) RunStage(afterLimbs, dt);
    }

    // The walking sub-step (every mode but the air ones; launch and touchdown blend it with the air chain).
    internal void GroundSubStep(float dt, Vector3 target)
    {
        var sp = Species;
        float maxSpeed = sp.speed * TimeScale;
        float window = kGroundWindow * SizeScale;
        if (climb != null)
        {
            climb.ClimbAllowed = ClimbAllowed;
            climb.Begin(dt, Path, pathUp, GroundNormals, heading, Speed, window);
        }
        bool climbing = climb != null && climb.Active;

        // 1. head steering (climbing: in the head's surface plane, the heading carried over the up's turn)
        Vector3 head = Path[0];
        Vector3 up;
        if (climbing)
        {
            up = Norm(climb.Up[0], Vector3.up);
            heading = SurfaceMath.Transport(heading, headUp, up);
            maxSpeed *= climb.SpeedFactor(heading);
        }
        else up = Norm(upAt(head), Vector3.up);
        headUp = up;
        heading = Tangent(heading, up);
        Vector3 to = target - head;
        float dist = to.magnitude;
        Vector3 desired = Vector3.ProjectOnPlane(to, up);
        float turnAngle = 0f, turned = 0f;
        Vector3 heading0 = heading;
        if (desired.sqrMagnitude > 1e-10f)
        {
            desired.Normalize();
            // a standing creature does not wiggle: fade the sway in with travel speed
            float sway = maxSpeed > 1e-4f ? Mathf.Clamp01(commanded / (0.3f * maxSpeed)) : 1f;
            float und = sway * sp.undulationDeg * Mathf.Sin(2f * Mathf.PI * sp.undulationHz / TimeScale * Time);
            if (und != 0f) desired = Quaternion.AngleAxis(und, up) * desired;
            float ang = Vector3.SignedAngle(heading, desired, up);
            turnAngle = Mathf.Abs(ang);
            // Turning in place swings the rigid head and drags the front of the body, so a leg at its
            // stretch or joint-range limit throttles the turn as well as the speed.
            float maxTurn = sp.turnRateDeg / TimeScale * dt * legs.Throttle;
            turned = Mathf.Clamp(ang, -maxTurn, maxTurn);
            heading = Quaternion.AngleAxis(turned, up) * heading;
        }
        float want = Mathf.Min(maxSpeed, 2.5f * dist) * (1f - sp.turnSlowdown * turnAngle / 180f);
        // A creature cannot outrun its legs: the gait caps the speed, and slows it when a planted foot
        // nears full stretch. The cap is approached with accel-limited easing (braking 3x harder).
        float cap = Mathf.Min(maxSpeed * legs.Throttle, legs.MaxSpeed);
        float goal = Mathf.Max(0f, want);
        float rate = sp.accel;
        if (cap < goal) { goal = cap; if (goal < commanded) rate = 3f * sp.accel; }
        // an overhang ahead, or the enclosure's height cap reached going up: brake to a stop (turning is free)
        if (climbing && (climb.Blocked || AboveCap(head, heading))) { goal = 0f; rate = 3f * sp.accel; }
        commanded = Mathf.MoveTowards(commanded, goal, rate * dt);
        Vector3 start = head;
        head += heading * (commanded * dt);
        if (Bounds.HasValue) head = Bounds.Value.ClampToSafe(head);
        Speed = (head - start).magnitude / dt;
        Path[0] = head;

        // 2. chain (Chain), then the swing guard (kSwingTravel)
        float seg = sp.spacing * SizeScale;
        SpineJoint[] js = sp.joints;
        bool guard = !climbing && girdles.Length > 0;
        if (guard) Array.Copy(Path, pathStart, n);
        Chain(seg, climbing);
        if (guard)
        {
            float bound = (kSwingTravel * Mathf.Max(Speed, sp.speed * TimeScale) + kSwingCreep * SizeScale) * dt;
            float f = 1f;
            for (int tryNo = 0; tryNo < kSwingTries && GirdleSwing() > bound; tryNo++)
            {
                // the turn is halved first (it is what whips the coil), the advance only on the last try
                bool last = tryNo + 1 == kSwingTries;
                f = last || tryNo + 2 == kSwingTries ? 0f : 0.5f * f;
                Array.Copy(pathStart, Path, n);
                heading = Quaternion.AngleAxis(turned * f, up) * heading0;
                head = start + heading * (commanded * dt * (last ? 0f : 1f));
                if (Bounds.HasValue) head = Bounds.Value.ClampToSafe(head);
                Speed = (head - start).magnitude / dt;
                Path[0] = head;
                Chain(seg, climbing);
            }
        }
        if (afterChain.Length != 0) RunStage(afterChain, dt);

        // 3. ground: smooth height offset and normal; hold on probe miss. Climbing: along each joint's own up,
        // at the steepness-scaled clearance, with a wrap cast past an edge.
        float k = 1f - Mathf.Exp(-dt / (kGroundTau * TimeScale));
        float[] droop = legs.DroopSpread;
        bool bobOn = sp.gaitBob.amplitude > 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = Path[i];
            Vector3 u = climbing ? climb.Up[i] : Norm(upAt(p), up);
            pathUp[i] = u;
            float clear = js[i].clearance * SizeScale;
            if (climbing) clear *= climb.ClearanceScale(i);
            var near = new Vector3(p.x - u.x * clear, p.y - u.y * clear, p.z - u.z * clear);
            Vector3 g0 = groundPoint[i];
            float hPrev = (g0.x - near.x) * u.x + (g0.y - near.y) * u.y + (g0.z - near.z) * u.z;
            float hs = hPrev;
            Vector3 nrm = GroundNormals[i];
            Vector3 gp, gn;
            bool got = climbing
                ? climb.ProbeJoint(i, near, u, climb.Window(i, window, clear), p, i == 0 ? heading : Norm(Path[i - 1] - p, heading), clear, out gp, out gn)
                : climb != null ? climb.ProbeWalking(i, near, u, window, out gp, out gn) // the same terrain answer; colliders cached per slot
                : spineGround.Probe(near, u, window, out gp, out gn);
            Supported[i] = got;
            if (got && ProbedGround != null) ProbedGround[i] = gp;
            if (got)
            {
                float hProbe = (gp.x - near.x) * u.x + (gp.y - near.y) * u.y + (gp.z - near.z) * u.z;
                hs = hPrev + (hProbe - hPrev) * k;
                // smoothing may lag a rising slope, but never let the body sink more than 5 mm into it
                if (hs < hProbe - kMaxSink * SizeScale) hs = hProbe - kMaxSink * SizeScale;
                nrm = Norm(new Vector3(nrm.x + (gn.x - nrm.x) * k, nrm.y + (gn.y - nrm.y) * k, nrm.z + (gn.z - nrm.z) * k), u);
            }
            var g = new Vector3(near.x + u.x * hs, near.y + u.y * hs, near.z + u.z * hs);
            groundPoint[i] = g;
            GroundNormals[i] = nrm;
            float lift = clear - droop[i];
            if (bobOn) lift += bobY;
            Path[i] = new Vector3(g.x + u.x * lift, g.y + u.y * lift, g.z + u.z * lift);
        }
        RestoreLengths(seg);
        // a flyer perched on a branch or a rock: joints past the end of their support hang
        if (flight != null && flight.Drape(dt, Path, pathUp, groundPoint, seg)) RestoreLengths(seg);
        // climbing, the body never folds sharply in pitch (ground -> wall, over an edge): lift the fold (only while
        // climbing: walking stays exactly the walking code - the limiter's on/off at its threshold jittered R25's holes)
        if (climbing)
            for (int pass = 0; pass < kPitchPasses && CreatureClimb.LimitPitch(Path, pathUp, seg, sp.climb.PitchLimitDeg, pitchLift); pass++) RestoreLengths(seg);
        // the up field follows the new ground normals (the next sub-step's chain step and grounding use it)
        if (climbing) climb.UpdateUpField(dt, Path, GroundNormals, pathUp, upAt);
        if (afterGround.Length != 0) RunStage(afterGround, dt);

        // 4. gait sway: the displayed body is the path bent toward the reaching legs
        ApplySway(dt);
        // a flyer launching or touching down: the displayed body blends with the air chain
        if (flight != null) flight.BeforeLegs(dt);

        // 5. legs (use the final joint positions; frames are refreshed first)
        UpdateFrames();
        if (flight != null) flight.AfterFrames(); // a twisting flyer's launch keeps its air chain's frames
        int started = legs.Update(dt, Speed);
        StepsTaken += started;
        if (sp.gaitBob.amplitude > 0f) Bob(dt, started > 0);
    }

    // The gait bob: a damped spring on the body's height, kicked downward when legs lift off (the body sags
    // onto the legs that stay planted, then bounces back). Frequency Froude-scaled like the gait.
    void Bob(float dt, bool liftOff)
    {
        GaitBob b = Species.gaitBob;
        float w = 2f * Mathf.PI * Mathf.Max(0.1f, b.hz) / TimeScale;
        // a walking bounce: fades in with the speed like the sway (an idle tidy-up step does not bob)
        float maxSpeed = Species.speed * TimeScale;
        float fade = maxSpeed > 1e-4f ? Mathf.Clamp01(Speed / (0.3f * maxSpeed)) : 0f;
        if (liftOff) bobV -= b.amplitude * SizeScale * w * fade;
        bobV += (-w * w * bobY - 2f * Mathf.Clamp01(b.damping) * w * bobV) * dt;   // semi-implicit Euler
        bobY += bobV * dt;
        float cap = 3f * b.amplitude * SizeScale;
        bobY = Mathf.Clamp(bobY, -cap, cap);
    }

    // The gait bob's current height offset of the body (m, + up); 0 without gaitBob.
    public float BobOffset => bobY;

    // 2. chain. The bend cone and the rigid joints act on the segments' directions along the ground
    // (the lizard's lateral bending); each segment keeps its own pitch from the last sub-step, so the
    // chain never flattens what the ground set (R25: a rigid neck copied the level heading, and the
    // grounding then stretched it over any drop). Lengths are exact.
    void Chain(float seg, bool climbing)
    {
        Vector3 prevBack = -heading;
        SpineJoint[] js = Species.joints;
        for (int i = 1; i < n; i++)
        {
            Vector3 a = Path[i - 1], u = pathUp[i];
            Vector3 v = Path[i] - a;
            float rise = u.x * v.x + u.y * v.y + u.z * v.z;
            var hv = new Vector3(v.x - u.x * rise, v.y - u.y * rise, v.z - u.z * rise);
            float run = Mathf.Sqrt(hv.x * hv.x + hv.y * hv.y + hv.z * hv.z);
            // the previous segment's ground direction, in this joint's tangent plane (it already is on a flat world)
            Vector3 pb;
            if (climbing)
            {
                // the previous direction lies in joint i-1's tangent plane: carry it over to joint i's (a plain
                // projection is ill-conditioned where the body turns up a wall and loses its lateral sense)
                pb = SurfaceMath.Transport(prevBack, pathUp[i - 1], u);
                float q = pb.x * u.x + pb.y * u.y + pb.z * u.z;
                pb = Norm(new Vector3(pb.x - u.x * q, pb.y - u.y * q, pb.z - u.z * q), prevBack);
            }
            else
            {
                float pu = prevBack.x * u.x + prevBack.y * u.y + prevBack.z * u.z;
                pb = pu * pu < 1e-10f ? prevBack : Norm(new Vector3(prevBack.x - u.x * pu, prevBack.y - u.y * pu, prevBack.z - u.z * pu), prevBack);
            }
            Vector3 d;
            if (js[i].rigid || run < 1e-6f) d = pb;
            else
            {
                d = hv / run;
                // climbing: a segment nearly along this joint's up (over an edge the up turns faster than the body
                // can bend) has no reliable direction in the joint's plane - keep the previous one there
                if (climbing)
                {
                    float steepness = run / Mathf.Max(1e-6f, Mathf.Sqrt(run * run + rise * rise));
                    float trust = SurfaceMath.SmoothStep(kRunLo, kRunHi, steepness);
                    if (trust < 1f) d = Norm(pb + (d - pb) * trust, pb);
                }
                // the bend cone: only a direction past the limit is turned back onto it
                float lim = js[i].bendLimitDeg * Mathf.Deg2Rad;
                if (pb.x * d.x + pb.y * d.y + pb.z * d.z < Mathf.Cos(lim))
                    d = Vector3.RotateTowards(pb, d, lim, 0f);
            }
            float len = Mathf.Sqrt(run * run + rise * rise);
            float sp1 = len > 1e-6f ? rise / len : 0f, cp = len > 1e-6f ? run / len : 1f;
            Path[i] = new Vector3(a.x + (d.x * cp + u.x * sp1) * seg, a.y + (d.y * cp + u.y * sp1) * seg, a.z + (d.z * cp + u.z * sp1) * seg);
            prevBack = d;
        }
    }

    // Swing guard: the largest distance a girdle's path joint moved in this sub-step's chain step.
    float GirdleSwing()
    {
        float worst = 0f;
        for (int g = 0; g < girdles.Length; g++)
        {
            int j = girdles[g];
            worst = Mathf.Max(worst, (Path[j] - pathStart[j]).sqrMagnitude);
        }
        return Mathf.Sqrt(worst);
    }

    void UpdateFrames()
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 f;
            if (i == 0) f = Norm(heading, Vector3.forward);
            else
            {
                Vector3 a = Joints[i - 1], b = Joints[i];
                f = Norm(new Vector3(a.x - b.x, a.y - b.y, a.z - b.z), Vector3.forward);
            }
            Vector3 g = GroundNormals[i];
            if (climb != null && climb.Active) g = SurfaceMath.Slerp(g, climb.Up[i], climb.Weight[i]);
            float gf = g.x * f.x + g.y * f.y + g.z * f.z;
            var u = new Vector3(g.x - f.x * gf, g.y - f.y * gf, g.z - f.z * gf);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.up, f);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.right, f);
            u = Norm(u, u);
            if (rollDeg != null && rollDeg[i] != 0f)
            {
                // u is perpendicular to f: rotating it about f is u cos + (f x u) sin
                float r = rollDeg[i] * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
                u = new Vector3(u.x * c + (f.y * u.z - f.z * u.y) * sn,
                                u.y * c + (f.z * u.x - f.x * u.z) * sn,
                                u.z * c + (f.x * u.y - f.y * u.x) * sn);
            }
            Rotations[i] = Basis(f, u);
        }
    }

    // Gait-coupled spine motion (sprawling-lizard pattern), driven by the feet themselves: each girdle
    // swings toward the leg that reaches furthest forward and rolls over its planted side while the other
    // side steps. Diagonal gait groups then give a standing S-wave with the head near a node. Fades out
    // with the walking speed like the head undulation. Joints = the path with each segment turned about
    // up by the sway's slope, within the bend limits; segment lengths are the path's own, exactly.
    void ApplySway(float dt)
    {
        var sp = Species;
        int G = girdles.Length;
        if (G == 0 || (sp.gaitSwayDeg <= 0f && sp.gaitRollDeg <= 0f))
        {
            Array.Copy(Path, Joints, n);
            return;
        }
        float maxSpeed = sp.speed * TimeScale;
        float fade = maxSpeed > 1e-4f ? Mathf.Clamp01(commanded / (0.3f * maxSpeed)) : 0f;
        float k = 1f - Mathf.Exp(-dt / (kSwayTau * TimeScale));
        bool climbing = climb != null && climb.Active;
        LegSpec[] specs = sp.legs;
        for (int g = 0; g < G; g++)
        {
            int j = girdles[g];
            Vector3 fwd = j == 0 ? heading : Path[j - 1] - Path[j];
            fwd = fwd.sqrMagnitude > 1e-12f ? fwd.normalized : heading;
            float sumR = 0f, sumL = 0f, roll = 0f; int cR = 0, cL = 0;
            for (int l = 0; l < specs.Length; l++)
            {
                LegSpec s = specs[l];
                if (s.attachJoint != j || s.feeler) continue;
                float reach = Vector3.Dot(Legs[l].foot - Path[j], fwd) / (sp.LegReach(l) * SizeScale);
                if (s.side > 0) { sumR += reach; cR++; } else { sumL += reach; cL++; }
                if (Legs[l].stepping) roll += s.side * Mathf.Sin(Mathf.PI * Legs[l].stepT);
            }
            float sway = Mathf.Clamp((sumR / cR - sumL / cL) / kSwayFull, -1f, 1f) * fade;
            if (climbing) sway *= 1f - 0.5f * climb.Steep[j]; // half the sway on a vertical surface
            swaySignal[g] = Mathf.Lerp(swaySignal[g], sway, k);
            rollSignal[g] = Mathf.Lerp(rollSignal[g], Mathf.Clamp(roll, -1f, 1f) * fade, k);
        }

        // lateral offset profile: node at the head, the girdles' offsets, a node one trunk behind the last
        // girdle; smoothstep between control points
        for (int i = 0; i < n; i++) { swayY[i] = 0f; rollDeg[i] = 0f; }
        int prevJ = 0; float prevY = 0f;
        for (int g = 0; g <= G; g++)
        {
            int j; float y;
            if (g < G) { j = girdles[g]; y = swayAmp[g] * swaySignal[g]; }
            else { j = Mathf.Min(n - 1, girdles[G - 1] + Mathf.Max(1, G > 1 ? girdles[G - 1] - girdles[G - 2] : girdles[G - 1])); y = 0f; }
            for (int i = prevJ + 1; i <= j; i++)
            {
                float t = (i - prevJ) / (float)(j - prevJ);
                swayY[i] = Mathf.Lerp(prevY, y, t * t * (3f - 2f * t));
            }
            prevJ = j; prevY = y;
            if (j >= n - 1) break;
        }
        for (int g = 0; g < G; g++)
            for (int d = -kRollSpread; d <= kRollSpread; d++)
            {
                int i = girdles[g] + d;
                if (i < 0 || i >= n) continue;
                float w = 0.5f * (1f + Mathf.Cos(Mathf.PI * d / (kRollSpread + 1f)));
                rollDeg[i] += sp.gaitRollDeg * rollSignal[g] * w;
            }

        // Each path segment turns about its ground normal by the sway's slope, so the swayed joints stay on
        // the ground's tangent plane (no dipping on a cross slope) and keep the path's own segment lengths
        // and height profile. The bend limit is applied to the turn alone: a segment's turn may differ from
        // the previous one only by the bend the path leaves free at that joint (none at a rigid joint).
        float seg = sp.spacing * SizeScale;
        Joints[0] = Path[0];
        Vector3 prevPath = -heading;
        float prevTurn = 0f;
        for (int i = 1; i < n; i++)
        {
            Vector3 v = Path[i] - Path[i - 1];
            float len = v.magnitude;
            Vector3 d = len > 1e-9f ? v / len : prevPath;
            // moving joint i to the right of joint i-1 turns the backward segment toward -right
            float turn = -Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp((swayY[i] - swayY[i - 1]) / seg, -0.5f, 0.5f));
            float slack = 0f;
            if (!sp.joints[i].rigid)
            {
                float cd;
                if (climbing)
                {
                    // the lateral bend only: a corner's pitch (ground -> wall) must not use up the bend limit
                    Vector3 uu = climb.Up[i];
                    Vector3 pp = Norm(prevPath - uu * Vector3.Dot(prevPath, uu), prevPath), dd = Norm(d - uu * Vector3.Dot(d, uu), d);
                    cd = Mathf.Clamp(Vector3.Dot(pp, dd), -1f, 1f);
                }
                else cd = Mathf.Clamp(prevPath.x * d.x + prevPath.y * d.y + prevPath.z * d.z, -1f, 1f);
                slack = Mathf.Max(0f, sp.joints[i].bendLimitDeg - Mathf.Acos(cd) * Mathf.Rad2Deg);
            }
            turn = Mathf.Clamp(turn, prevTurn - slack, prevTurn + slack);
            prevPath = d; prevTurn = turn;
            // climbing: about the smooth up field (a corner's probe normals jump between its two surfaces)
            if (turn != 0f) d = Rotate(d, climbing ? climb.Up[i] : GroundNormals[i], turn * Mathf.Deg2Rad);
            Vector3 jp = Joints[i - 1];
            Joints[i] = new Vector3(jp.x + d.x * len, jp.y + d.y * len, jp.z + d.z * len);
        }
    }

    // Grounding sets each joint's height on its own, along up, so wherever the ground's height changes
    // along the body faster than it did a sub-step ago (a dug hole's rim, a slip-face brink) a segment
    // comes out longer or shorter than the spacing (R25: up to twice its length at a hole's wall, and the
    // skinned body with it). Each segment is put back to its length along its new direction, from the head
    // back. The chain step of the next sub-step keeps these pitches, so in steady motion this moves
    // nothing; only the change of one sub-step is redistributed. A stretched segment is pulled in along
    // itself (follow the leader once more: a joint the ground dropped away from stays nearer its
    // predecessor, the body drapes); a shortened one is let out along the ground, keeping the joint's
    // grounded height (along itself it would push a down-sloping tail into the sand).
    void RestoreLengths(float seg)
    {
        for (int i = 1; i < n; i++)
        {
            Vector3 a = Path[i - 1], v = Path[i] - a;
            float m2 = v.x * v.x + v.y * v.y + v.z * v.z;
            if (!(m2 > 1e-18f)) continue;
            if (m2 >= seg * seg)
            {
                float f = seg / Mathf.Sqrt(m2);
                Path[i] = new Vector3(a.x + v.x * f, a.y + v.y * f, a.z + v.z * f);
                continue;
            }
            Vector3 u = pathUp[i];
            float rise = v.x * u.x + v.y * u.y + v.z * u.z;
            var hv = new Vector3(v.x - u.x * rise, v.y - u.y * rise, v.z - u.z * rise);
            float run2 = hv.x * hv.x + hv.y * hv.y + hv.z * hv.z;
            if (!(run2 > 1e-18f)) continue;
            float g = Mathf.Sqrt(Mathf.Max(0f, seg * seg - rise * rise) / run2);
            Path[i] = new Vector3(a.x + hv.x * g + u.x * rise, a.y + hv.y * g + u.y * rise, a.z + hv.z * g + u.z * rise);
        }
    }

    // The enclosure's height cap (EnclosureArea.maxHeight) reached by a head that is still heading up.
    bool AboveCap(Vector3 head, Vector3 dir)
    {
        if (!Bounds.HasValue) return false;
        EnclosureArea a = Bounds.Value;
        if (!(a.maxHeight > 0f)) return false;
        return Vector3.Dot(head - a.center, a.up) >= a.maxHeight && Vector3.Dot(dir, a.up) > 0.1f;
    }

    // Touchdown: the walking state restarts from the body as it is (Joints) - path, ground points and normals
    // re-probed along gravity's up, the heading the head's, the walking speed `speed`, every foot put down.
    internal void ResetGround(float speed, float reach = 0f)
    {
        // the body may still be hovering up to `reach` above the ground it settles onto
        float window = Mathf.Max(kGroundWindow * SizeScale, reach);
        Vector3 hu = Norm(upAt(Joints[0]), Vector3.up);
        Vector3 f = n > 1 ? Joints[0] - Joints[1] : heading;
        heading = Tangent(f.sqrMagnitude > 1e-12f ? f : heading, hu);
        headUp = hu;
        commanded = Mathf.Clamp(speed, 0f, Species.speed * TimeScale);
        Speed = commanded;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = Joints[i];
            Vector3 u = Norm(upAt(p), hu);
            float clear = Species.joints[i].clearance * SizeScale;
            Vector3 near = p - u * clear;
            if (spineGround.Probe(near, u, window, out Vector3 g, out Vector3 nrm) || ground.Probe(near, u, window, out g, out nrm)) { groundPoint[i] = g; GroundNormals[i] = Norm(nrm, u); Supported[i] = true; }
            else { groundPoint[i] = near; GroundNormals[i] = u; Supported[i] = false; }
            pathUp[i] = u;
            Path[i] = Joints[i];
            swayY[i] = 0f; rollDeg[i] = 0f;
        }
        for (int g = 0; g < swaySignal.Length; g++) { swaySignal[g] = 0f; rollSignal[g] = 0f; }
        if (climb != null) Array.Copy(pathUp, Up, n);
        UpdateFrames();
        legs.Replant();
    }

    // The air modes report the flight speed.
    internal void SetFlightSpeed(float speed) { Speed = speed; commanded = 0f; }
    internal void RefreshFrames() => UpdateFrames();

    // The leg's current ground-projected rest point (debug view).
    public Vector3 LegRestPoint(int leg) => legs.RestPoint(leg);

    // Arthropod legs (LegSpec.segments): every joint point (coxa base .. foot) from LegPointOffset(leg), and the
    // leg plane's normal (the segment bones' hinge reference).
    public Vector3[] LegPoints => legs.Points;
    public int LegPointOffset(int leg) => legs.PointOffset[leg];
    public Vector3 LegPlaneNormal(int leg) => legs.PlaneNormal[leg];

    // Spine, leg, wing, eye and part bones in world space, in the species bone order.
    public void WriteBoneWorld(Matrix4x4[] dst, int offset)
    {
        Vector3 s = Vector3.one * SizeScale;
        for (int i = 0; i < n; i++) dst[offset + i] = Matrix4x4.TRS(Joints[i], Rotations[i], s);
        legs.WriteBones(dst, offset);
        if (flight != null) flight.WriteBones(dst, offset); // wings; tucked or blended legs in the air
        // eyes, antenna strands, any part runtime with bones (in part order)
        for (int r = 0; r < runtimes.Length; r++) runtimes[r].WriteBones(dst, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector3 Norm(Vector3 v, Vector3 fallback)
    {
        float m = v.x * v.x + v.y * v.y + v.z * v.z;
        if (!(m > 1e-12f)) return fallback;
        float inv = 1f / Mathf.Sqrt(m);
        return new Vector3(v.x * inv, v.y * inv, v.z * inv);
    }

    // v rotated about the unit axis a by rad (Rodrigues).
    static Vector3 Rotate(Vector3 v, Vector3 a, float rad)
    {
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad), ad = (a.x * v.x + a.y * v.y + a.z * v.z) * (1f - c);
        return new Vector3(v.x * c + (a.y * v.z - a.z * v.y) * s + a.x * ad,
                           v.y * c + (a.z * v.x - a.x * v.z) * s + a.y * ad,
                           v.z * c + (a.x * v.y - a.y * v.x) * s + a.z * ad);
    }

    // Quaternion.LookRotation(f, u) for a unit f and a unit u perpendicular to it, built from the rotation
    // matrix with columns (u x f, u, f): the frame convention's right, up and forward (cheaper than the
    // engine call, which the sim makes for every joint twice a frame).
    // f and u are only near-orthonormal after the roll and climb slerps, so the extracted quaternion can drift
    // from unit length by ~1e-5; Matrix4x4.TRS rejects that ("input Quaternion is invalid"). Renormalise only
    // then, so exact frames keep their bits.
    internal static Quaternion Basis(Vector3 f, Vector3 u)
    {
        Quaternion q = BasisRaw(f, u);
        float l2 = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        if (Mathf.Abs(l2 - 1f) > 1e-6f)
        {
            float inv = 1f / Mathf.Sqrt(l2);
            q = new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }
        return q;
    }

    static Quaternion BasisRaw(Vector3 f, Vector3 u)
    {
        float rx = u.y * f.z - u.z * f.y, ry = u.z * f.x - u.x * f.z, rz = u.x * f.y - u.y * f.x;
        float m00 = rx, m01 = u.x, m02 = f.x, m10 = ry, m11 = u.y, m12 = f.y, m20 = rz, m21 = u.z, m22 = f.z;
        float tr = m00 + m11 + m22;
        if (tr > 0f)
        {
            float s = 0.5f / Mathf.Sqrt(tr + 1f);
            return new Quaternion((m21 - m12) * s, (m02 - m20) * s, (m10 - m01) * s, 0.25f / s);
        }
        if (m00 > m11 && m00 > m22)
        {
            float s = 2f * Mathf.Sqrt(1f + m00 - m11 - m22), inv = 1f / s;
            return new Quaternion(0.25f * s, (m01 + m10) * inv, (m02 + m20) * inv, (m21 - m12) * inv);
        }
        if (m11 > m22)
        {
            float s = 2f * Mathf.Sqrt(1f + m11 - m00 - m22), inv = 1f / s;
            return new Quaternion((m01 + m10) * inv, 0.25f * s, (m12 + m21) * inv, (m02 - m20) * inv);
        }
        {
            float s = 2f * Mathf.Sqrt(1f + m22 - m00 - m11), inv = 1f / s;
            return new Quaternion((m02 + m20) * inv, (m12 + m21) * inv, 0.25f * s, (m10 - m01) * inv);
        }
    }

    static Vector3 Tangent(Vector3 v, Vector3 up)
    {
        Vector3 t = Vector3.ProjectOnPlane(v, up);
        if (t.sqrMagnitude < 1e-10f) t = Vector3.ProjectOnPlane(Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.forward, up);
        return t.normalized;
    }
}
