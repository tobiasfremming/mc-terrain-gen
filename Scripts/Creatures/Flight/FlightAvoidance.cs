using UnityEngine;

// Predictive obstacle avoidance for a flyer (user request 2026-10-18: "the wyvern should be able to avoid
// flying into trees"). Real geometry only: the casts go to the creature's surfaces (ISurfaceProbe.Cast - bark
// and rock colliders; terrain hits are ignored, the altitude floor handles the ground). On by FlightSpec.
// avoidLookahead > 0 (0 = off: the goals pass through untouched).
//
// Every kEvery seconds (x sqrt scale) the path the flyer will actually fly for a turn-rate command is
// predicted - the roll toward that command's coordinated bank at a typical roll rate, the coordinated turn,
// the current climb rate - over avoidLookahead seconds, and swept by rays along its chords at the body's
// centre line and at both wing tips (the banked span plus avoidMargin; the wing tips' vertical offset
// alternates between evaluations, so the downstroke's sweep and the dangling tail are covered over a few).
// The command the brain or the landing autopilot asked for is checked first (3 chords x 5 rays: the centre
// line, both tips and a pair in between whose offset rotates per evaluation); only when
// it is blocked are the alternatives checked: five turn rates from hard left to hard right. The command
// with the longest clear run wins (ties: the nearest to the asked one, then the one chosen last time), the
// flyer slows toward minSpeed while the way ahead is short (a slower flyer turns tighter: R = v^2 / g tan
// bank), and the choice is held kHold seconds after the last blocked check so the bank does not chatter.
// The turn is flown by FlightModel's own roll dynamics: banked, rate-limited, never a jerk.
// Paths that leave the enclosure's safe disc count as blocked there (the wall turn is not undone). Landing:
// hits within kLandIgnore of the landing point are its perch, not obstacles; the flare is not steered.
// Allocation-free, main thread.
public sealed class FlightAvoidance
{
    const float kEvery = 0.1f;          // s at scale 1 between evaluations
    const float kHold = 0.5f;           // s at scale 1 a choice is held after the last blocked evaluation
    const int kSteps = 6;               // prediction steps over the horizon
    const int kChords = 6;              // chords cast per path (kSteps / kChords steps each): one per step - a chord over two steps of a tight turn cut 0.9 m inside the outer wing tip's arc
    const float kRollLead = 0.2f;       // s at scale 1 before the roll starts (CreatureFlight's)
    const float kRollRate = 30f;        // deg/s at scale 1 (CreatureFlight's typical roll rate)
    const float kClimbRateMax = 1.5f;   // m/s at scale 1: the predicted climb rate is clamped to this
    const float kSlowBelow = 0.75f;     // slow down while the best clear run is below this fraction of the horizon
    const float kSlowFloor = 0.88f;     // of cruise: the slowest an avoidance turn flies (see Evaluate)
    const float kLandIgnore = 0.9f;     // m at scale 1: hits this near the landing point are the perch
    const float kPerchTube = 0.4f, kPerchBehind = 3f, kPerchAhead = 0.35f; // m at scale 1: the perch branch (SetPerch)
    const float kMaxTurnDeg = 150f;     // a predicted turn flies straight on after this much heading change (no loops)

    readonly FlightSpec f;
    readonly float scale, T, span;
    static readonly float[] kCandidates = { -1f, -0.5f, 0f, 0.5f, 1f };
    const float kGolden = 137.50776f;   // deg: the ray ring's turn per evaluation
    const int kRays = 7;                // per chord: the centre line, both wing tips, a turning ring of kRays - 3
    const float kSwitch = 0.7f;
    const float kClimbRate = 1.2f;      // m/s at scale 1: a climbing candidate's predicted climb
    const float kClimbBy = 2f;          // m at scale 1: ... and how far its altitude goal is raised
    const float kClimbCost = 0.5f;      // level turns win a tie
    const float kCeiling = 0.9f;        // of the enclosure's maxHeight: no climbing candidates above this
    const int kMemory = 16;             // remembered obstacle points (ring)
    const float kMemoryTime = 3f;       // s at scale 1 a hit point is remembered         // a held turn's other side counts at this share of its clear run

    Vector3? toward;
    float pursuitGain;
    float evalIn, holdLeft, heldTurn, heldSpeed, clock, heldAltitude = float.MinValue;
    // Obstacle points seen recently: a path is blocked where it passes within the span of one. The rays sweep
    // the swath a part at a time (rotating offsets); the memory keeps what one evaluation saw for the next.
    readonly Vector3[] memory = new Vector3[kMemory];
    readonly float[] memoryTime = new float[kMemory];
    int memoryNext, memoryCount;
    int phase;
    readonly Vector3[] path = new Vector3[kSteps + 1];
    readonly Vector3[] right = new Vector3[kSteps + 1], bup = new Vector3[kSteps + 1]; // banked right and up

    public bool Enabled => f.avoidLookahead > 0f;
    public bool Active => holdLeft > 0f;     // steering round an obstacle
    public float ClearFraction { get; private set; } = 1f; // the chosen path's clear run / horizon, last evaluation
    public int Evaluations { get; private set; }
    public int Casts { get; private set; }
    public int Blocked { get; private set; } // evaluations whose asked-for path was blocked
    public Vector3 LastHit { get; private set; }
    public float LastAsked { get; private set; }  // the last evaluation's asked-for and chosen turn rates (rad/s)
    public float LastChosen { get; private set; }
    public float LastAskedClear { get; private set; } // the asked-for path's clear run (m)

    // span: the half span (shoulder line to wing tip) at scale 1.
    public FlightAvoidance(FlightSpec spec, float sizeScale, float halfSpan)
    {
        f = spec; scale = sizeScale; T = Mathf.Sqrt(sizeScale);
        span = (halfSpan + spec.avoidMargin) * sizeScale;
    }

    // The half span of a winged species at scale 1: the wrist's rest reach plus its longest finger.
    public static float HalfSpan(CreatureSpecies sp)
    {
        float s = 0f;
        for (int w = 0; w < sp.WingCount; w++)
        {
            WingSpec ws = sp.wings[w];
            float fl = 0f;
            if (ws.fingerLengths != null) for (int k = 0; k < ws.fingerLengths.Length; k++) fl = Mathf.Max(fl, ws.fingerLengths[k]);
            s = Mathf.Max(s, Mathf.Abs(ws.restTip.x) + fl);
        }
        return s;
    }

    public void Reset() { evalIn = 0f; holdLeft = 0f; ClearFraction = 1f; memoryCount = 0; heldAltitude = float.MinValue; }

    void Remember(Vector3 p)
    {
        memory[memoryNext] = p; memoryTime[memoryNext] = clock;
        memoryNext = (memoryNext + 1) % kMemory;
        if (memoryCount < kMemory) memoryCount++;
    }

    // The first remembered point within the span of the chord a -> b: its distance along the chord, or -1.
    float RememberedOn(Vector3 a, Vector3 b, Vector3? landing)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude, best = -1f, oldest = clock - kMemoryTime * T;
        if (len2 < 1e-10f) return -1f;
        for (int k = 0; k < memoryCount; k++)
        {
            if (memoryTime[k] < oldest) continue;
            Vector3 q = memory[k];
            if (Ignored(q, landing)) continue;
            float t = Vector3.Dot(q - a, ab) / len2;
            if (t <= 0f || t > 1f) continue; // behind the chord's start (passing it now) or past its end
            if ((a + ab * t - q).sqrMagnitude > span * span) continue;
            float d = t * Mathf.Sqrt(len2);
            if (best < 0f || d < best) best = d;
        }
        return best;
    }

    // The perch being landed on (a branch): hits on it are where the feet go, not obstacles. The branch is
    // taken as a tube (kPerchTube) along the approach heading through the landing point, from kPerchBehind
    // behind it (the final comes down along the branch) to kPerchAhead past it; anything else there - the
    // trunk the branch grows from, another limb - stays an obstacle.
    public void SetPerch(Vector3 point, Vector3 heading)
    {
        perchSet = heading.sqrMagnitude > 1e-8f;
        perchPoint = point; perchDir = perchSet ? heading.normalized : Vector3.zero;
    }
    public void ClearPerch() => perchSet = false;
    bool perchSet;
    Vector3 perchPoint, perchDir;

    bool Ignored(Vector3 q, Vector3? landing)
    {
        if (landing.HasValue && (q - landing.Value).sqrMagnitude < kLandIgnore * kLandIgnore * scale * scale) return true;
        if (!perchSet) return false;
        Vector3 d = q - perchPoint;
        float t = Vector3.Dot(d, perchDir);
        if (t < -kPerchBehind * scale || t > kPerchAhead * scale) return false;
        return (d - perchDir * t).sqrMagnitude < kPerchTube * kPerchTube * scale * scale;
    }

    // Adjusts the model's goals for obstacles ahead (before Model.Control). landing: the landing point (null
    // when not landing); flare: the flare is never steered.
    // toward: the point the asked turn pursues (null: a constant turn), with the pursuit's gain (turn rate per
    // radian of heading error, / sqrt scale).
    public void Apply(float dt, FlightModel m, ISurfaceProbe probe, EnclosureArea? bounds, Vector3? landing, bool flare,
                      Vector3? toward = null, float pursuitGain = 0f)
    {
        this.toward = toward; this.pursuitGain = pursuitGain;
        if (!Enabled || probe == null || flare) { holdLeft = 0f; return; }
        evalIn -= dt; clock += dt;
        if (evalIn <= 0f)
        {
            evalIn = kEvery * T;
            Evaluate(m, probe, bounds, landing);
        }
        if (holdLeft > 0f)
        {
            holdLeft -= dt;
            m.TurnRateGoal = heldTurn;
            m.SpeedGoal = Mathf.Min(m.SpeedGoal, heldSpeed);
            m.AltitudeGoal = Mathf.Max(m.AltitudeGoal, heldAltitude);
        }
    }

    void Evaluate(FlightModel m, ISurfaceProbe probe, EnclosureArea? bounds, Vector3? landing)
    {
        Evaluations++;
        phase++;
        float v = Mathf.Max(m.HorizontalSpeed, f.minSpeed * T);
        float wMax = FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad) / v;
        float horizon = f.avoidLookahead * T;
        float asked = Mathf.Clamp(m.TurnRateGoal, -wMax, wMax);
        float full = v * horizon;
        float clear = Clear(m, probe, bounds, landing, asked, v, horizon, false, toward);
        float geometry = lastGeometry; // the wall alone never triggers an avoidance (the wall turn is the brain's)
        float askedWall = lastWall;
        LastAsked = asked; LastAskedClear = Mathf.Min(clear, full); LastChosen = holdLeft > 0f ? heldTurn : asked;
        if (geometry >= full)
        {
            ClearFraction = 1f;
            return; // the asked-for path is clear: keep holding a recent choice until it runs out
        }
        Blocked++;
        float bestTurn = asked, bestClear = Mathf.Min(geometry, full), bestCost = float.MaxValue;
        bool bestClimb = false;
        bool held = holdLeft > 0f && heldTurn != 0f;
        float ceiling = bounds.HasValue && bounds.Value.maxHeight > 0f ? Vector3.Dot(bounds.Value.center, m.Up) + kCeiling * bounds.Value.maxHeight : float.MaxValue;
        bool canClimb = m.Altitude + kClimbBy * scale < ceiling;
        for (int k = 0; k < kCandidates.Length + 2; k++)
        {
            // five turn rates level, then climbing straight on and climbing in the held (else asked) turn
            bool up = k >= kCandidates.Length;
            if (up && !canClimb) break;
            float w = !up ? kCandidates[k] * wMax : k == kCandidates.Length ? 0f : Mathf.Sign(held ? heldTurn : asked) * 0.5f * wMax;
            // scored on the run clear of geometry first, the run inside the safe disc second: counted as one, a
            // flyer near the wall saw every turn "blocked" by it and picked the one into the trunk (2026-10-19)
            float wallC;
            float c;
            if (!up && Mathf.Abs(w - asked) < 1e-4f) { c = geometry; wallC = askedWall; }
            else { Clear(m, probe, bounds, landing, w, v, horizon, up); c = lastGeometry; wallC = lastWall; }
            c = Mathf.Min(c, full); wallC = Mathf.Min(wallC, full);
            // longest clear run first (in steps of a tenth of the horizon), then nearest the asked turn, level
            // before climbing. Committed: while a turn round an obstacle is held, the other side must be clearly
            // better (kSwitch) to win - a flyer that changes its mind every evaluation rolls back and forth into
            // the obstacle.
            bool otherSide = held && w != 0f && Mathf.Sign(w) != Mathf.Sign(heldTurn);
            float score = otherSide ? c * kSwitch : c;
            float cost = -Mathf.Floor(10f * score / full) * 10f - Mathf.Floor(10f * wallC / full) * 2f
                         + Mathf.Abs(w - (held ? heldTurn : asked)) / wMax + (up ? kClimbCost : 0f);
            if (cost < bestCost) { bestCost = cost; bestTurn = w; bestClear = c; bestClimb = up; }
        }
        ClearFraction = Mathf.Min(bestClear, full) / full;
        heldTurn = bestTurn; LastChosen = bestTurn;
        if (bestClimb) heldAltitude = m.Altitude + kClimbBy * scale;
        else if (!(holdLeft > 0f)) heldAltitude = float.MinValue;
        // slower while the way is short: a tighter turn
        float t = Mathf.Clamp01(ClearFraction / kSlowBelow);
        // (never below kSlowFloor x cruise: slower, a flyer that cannot hold its height there sank into the bark it
        // was turning away from - the winged serpent below ~0.85 cruise)
        heldSpeed = Mathf.Lerp(Mathf.Max(f.minSpeed, kSlowFloor * f.cruiseSpeed) * T, f.maxSpeed * T, t);
        holdLeft = kHold * T;
    }

    // The clear run (m along the path) of the path flown for turn-rate command w.
    float Clear(FlightModel m, ISurfaceProbe probe, EnclosureArea? bounds, Vector3? landing, float w, float v, float horizon, bool climbing = false, Vector3? pursue = null)
    {
        Vector3 up = m.Up;
        Vector3 fwd = m.Forward;
        float bank = m.Bank;
        float target = Mathf.Clamp(Mathf.Atan(w * v / FlightModel.G), -f.maxBankDeg * Mathf.Deg2Rad, f.maxBankDeg * Mathf.Deg2Rad);
        float rate = kRollRate * Mathf.Deg2Rad / T, lead = kRollLead * T;
        float climb = climbing ? kClimbRate * T : Mathf.Clamp(m.VerticalSpeed, -kClimbRateMax * T, kClimbRateMax * T);
        float h = horizon / kSteps;
        Vector3 p = m.Position;
        path[0] = p; right[0] = Banked(up, fwd, bank); bup[0] = Vector3.Cross(fwd, right[0]);
        float tNow = 0f, turned = 0f;
        for (int i = 1; i <= kSteps; i++)
        {
            tNow += h;
            if (pursue.HasValue)
            {
                // the pursuit: the turn rate follows the heading error to the point
                Vector3 to = Vector3.ProjectOnPlane(pursue.Value - p, up);
                float err = to.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(fwd, to, up) * Mathf.Deg2Rad : 0f;
                float wMax = FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad) / v;
                float wp = Mathf.Clamp(pursuitGain / T * err, -wMax, wMax);
                target = Mathf.Atan(wp * v / FlightModel.G);
            }
            if (turned >= kMaxTurnDeg) bank = Mathf.MoveTowards(bank, 0f, rate * h);
            else if (tNow > lead) bank = Mathf.MoveTowards(bank, target, rate * h);
            float yaw = FlightModel.G * Mathf.Tan(bank) / v * h * Mathf.Rad2Deg;
            turned += Mathf.Abs(yaw);
            fwd = Quaternion.AngleAxis(yaw, up) * fwd;
            p += fwd * (v * h) + up * (climb * h);
            path[i] = p; right[i] = Banked(up, fwd, bank); bup[i] = Vector3.Cross(fwd, right[i]);
        }
        // the enclosure: where the path leaves the safe disc (the wall run). It counts in the scoring of
        // alternatives, never as an obstacle on its own (lastGeometry: the run against geometry alone)
        float wallRun = float.MaxValue, acc = 0f;
        if (bounds.HasValue)
            for (int i = 1; i <= kSteps; i++)
            {
                acc += (path[i] - path[i - 1]).magnitude;
                if (bounds.Value.PlanarDistance(path[i]) > bounds.Value.safeRadius) { wallRun = acc; break; }
            }
        lastGeometry = Geometry(probe, landing);
        lastWall = wallRun;
        return Mathf.Min(wallRun, lastGeometry);
    }

    float lastGeometry, lastWall;

    // The predicted path's clear run against real geometry (float.MaxValue: clear): kChords chords, kRays rays
    // each, and the remembered hits.
    float Geometry(ISurfaceProbe probe, Vector3? landing)
    {
        float run = 0f;
        int per = kSteps / kChords;
        for (int c = 0; c < kChords; c++)
        {
            int a = c * per, b = Mathf.Min(kSteps, a + per);
            float mem = RememberedOn(path[a], path[b], landing);
            for (int r = 0; r < kRays; r++)
            {
                // the centre line, both (banked) wing tips every time - a trunk passed within the span was missed
                // by the turning ring alone (2026-10-19) - and a ring round it (the wings' swept disc: as far below
                // and above the girdle as out to the side, ~1.6 m at scale 1), the ring's radius alternating between
                // the span and half of it and its angle turning by the golden angle per evaluation
                Vector3 off0 = Vector3.zero, off1 = Vector3.zero;
                if (r == 1 || r == 2)
                {
                    float sg = r == 1 ? span : -span;
                    off0 = right[a] * sg; off1 = right[b] * sg;
                }
                else if (r > 2)
                {
                    float rho = (((phase + r) & 1) == 0 ? 1f : 0.55f) * span;
                    float th = (phase * kGolden + 360f / (kRays - 3) * (r - 3)) * Mathf.Deg2Rad;
                    float cx = rho * Mathf.Cos(th), cy = rho * Mathf.Sin(th);
                    off0 = right[a] * cx + bup[a] * cy;
                    off1 = right[b] * cx + bup[b] * cy;
                }
                Vector3 o = path[a] + off0, e = path[b] + off1, d = e - o;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                Casts++;
                if (!probe.Cast(o, d / len, len, out SurfaceHit hit) || hit.source == 0) continue;
                if (Ignored(hit.point, landing)) continue;
                LastHit = hit.point;
                Remember(hit.point);
                float along = Vector3.Dot(hit.point - path[a], (path[b] - path[a]).normalized);
                return run + Mathf.Max(0f, mem >= 0f ? Mathf.Min(mem, along) : along);
            }
            if (mem >= 0f) return run + mem;
            run += (path[b] - path[a]).magnitude;
        }
        return float.MaxValue;
    }

    // A straight leg from a to b clear of obstacles for the wings' swept disc (level wings): the centre line,
    // both wing tips, half way out on either side, and the span below and above (7 rays). The brain's check
    // for a waypoint or a soar circle's chord.
    public bool SegmentClear(ISurfaceProbe probe, Vector3 a, Vector3 b, Vector3 up)
    {
        if (probe == null) return true;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 1e-5f) return true;
        d /= len;
        Vector3 r = Vector3.Cross(up, d);
        if (r.sqrMagnitude < 1e-8f) r = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right);
        r = r.normalized * span;
        Vector3 u = Vector3.Cross(d, r).normalized * span;
        for (int k = 0; k < 7; k++)
        {
            Vector3 o = a + (k == 0 ? Vector3.zero : k == 1 ? r : k == 2 ? -r : k == 3 ? 0.5f * r : k == 4 ? -0.5f * r : k == 5 ? -u : u);
            Casts++;
            if (probe.Cast(o, d, len, out SurfaceHit hit) && hit.source != 0 && !Ignored(hit.point, null)) { Remember(hit.point); return false; }
        }
        return true;
    }

    static Vector3 Banked(Vector3 up, Vector3 fwd, float bank)
    {
        Vector3 r = Vector3.Cross(up, fwd);
        return Mathf.Cos(bank) * r - Mathf.Sin(bank) * up;
    }
}
