using UnityEngine;

// The octopus decides what each arm is for (user decision 5; plan 2026-10-18 T2-T4). Each arm still controls
// itself - its own target search, contact, wrap and release under the support rule - but the coordinator
// tells it what it is FOR, from the brain's intent, the support the body needs and where the arm is:
//
//   Strike   the arms nearest the intercept (pre-empts everything)
//   Grip     travelling: the 2-4 arms facing the route; they find holds ahead and, once anchored ahead,
//            pull the body along the route
//   Anchor   anchored arms kept for support (>= MinAnchors + 1 reserve); free arms sent to find support when
//            it is short; they hold but never pull
//   Feel     one arm exploring the far side (grips only when support is low)
//   Hang / Tuck / Rest / Groom   the others: no hold, no pull (spare holds behind are let go)
//
// Hysteresis: decisions at ~4 Hz (x TimeScale); an arm keeps a role for at least MinHoldSeconds unless
// pre-empted (strike, support emergency, its own failure report, a new intent mode); the current role gets
// a Stickiness bonus in the ranking; at most MaxChangesPerDecision ordinary changes per decision. A Failed
// report puts that role on a cool-down for the arm.
//
// Motion (the root of the old tug-of-war: every anchored arm shortened or lengthened on its own, and pairs of
// taut arms pulled the body toward two different places through their tethers, hundreds of times its weight):
// the coordinator plans ONE body velocity per tick (DesiredVelocity: along the route while a grip arm holds
// ahead, scaled down to what the paying-out holds still allow, zero otherwise), and every anchored arm sets its
// run's length from the same wanted root position. Grip arms ahead shorten to it; every other hold only pays out
// (it never shortens), so all the arms ask for one body position.
//
// Pure C#, no allocation after construction, no queries.
public sealed class ArmCoordinator
{
    public const float DecideSeconds = 0.25f;       // x TimeScale
    public const float MinHoldSeconds = 1.5f;       // x TimeScale
    public const float FailCooldownSeconds = 3f;    // x TimeScale
    public const float DoneCooldownSeconds = 1.5f;  // x TimeScale
    public const int MaxChangesPerDecision = 2;
    public const float RetakeBelow = 0.3f, RetakeAbove = 0.5f;   // facing: a free grip arm below gives Grip up, a free arm above takes it (early)
    const int RoleCount = 8;

    public float Stickiness = 0.25f;                // tests: 0 shows what hysteresis prevents
    public float MinHold = MinHoldSeconds;          // tests: 0 shows what hysteresis prevents
    public int Decisions, Changes, FailedReports, DoneReports;   // cumulative
    // A role forced on an arm (tests, the lab's role buttons): (int)ArmRole, or -1 = the coordinator decides.
    public readonly int[] Forced;
    public Vector3 RouteDir { get; private set; }

    readonly int arms;
    readonly Vector3[] restDirLocal;
    readonly ArmRole[] want;
    readonly bool[] pre;
    readonly float[] gScore, sScore, cooldown;
    readonly int[] order;
    readonly float[] facing, facingAnchor, lateral;
    float nextDecision;
    OctoMode lastMode;
    bool lastClimb;
    bool started;
    uint rng;
    // rest variety (T6): idle slots re-rolled every 8-20 s (x TimeScale), with quiet windows
    float nextIdleRoll, restSince;
    bool wasRest;
    int idleHang = -1, idleGroom = -1, idleGroomArm = -1;

    // Strain: the force an arm puts on the body, in body weights, smoothed over StrainSeconds. A hold straining
    // past StrainLimit for StrainHoldSeconds is fighting the others (a run that cannot bend from the body to its
    // hold, two short runs on either side): the arm feels it and lets go, if the support allows.
    public const float StrainLimit = 4f, StrainSeconds = 0.15f, StrainHoldSeconds = 0.4f;
    public readonly float[] Strain;
    readonly float[] strainSince;
    readonly float weight;
    public int StrainReleases;                      // cumulative

    public ArmCoordinator(OctopusBody body, uint seed = 1)
    {
        arms = body.ArmCount;
        Strain = new float[arms]; strainSince = new float[arms];
        swingGroup = new int[arms]; swingBase = new float[arms]; pump = new float[arms]; strainLetGo = new bool[arms]; heldSince = new float[arms];
        for (int a = 0; a < arms; a++) swingGroup[a] = -1;
        Forced = new int[arms];
        for (int a = 0; a < arms; a++) Forced[a] = -1;
        weight = body.TotalMass * OctopusController.Gravity;
        restDirLocal = new Vector3[arms];
        for (int a = 0; a < arms; a++) restDirLocal[a] = OctopusShape.ArmDir(body.Spec, a);
        want = new ArmRole[arms]; pre = new bool[arms];
        gScore = new float[arms]; sScore = new float[arms]; order = new int[arms];
        facing = new float[arms]; facingAnchor = new float[arms]; lateral = new float[arms];
        cooldown = new float[arms * RoleCount];
        rng = seed * 1103515245u + 12345u; if (rng == 0) rng = 3;
    }

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

    // v without its component along up, unit (zero if v is along up)
    public static Vector3 Level(Vector3 v, Vector3 up) => OctoMath.Norm(v - up * OctoMath.Dot(v, up), Vector3.zero);

    public static bool Travelling(OctoMode m) => m == OctoMode.Travel || m == OctoMode.Stalk || m == OctoMode.Retreat;

    public static bool Holds(ArmRole r) => r == ArmRole.Anchor || r == ArmRole.Grip;

    public bool CoolingDown(int arm, ArmRole r, float time) => time < cooldown[arm * RoleCount + (int)r];

    // ------------------------------------------------------------------ tick

    public void Tick(float dt, OctopusController c)
    {
        ArmBlackboard b = c.Board;
        float ts = c.Body.TimeScale, now = c.Time;
        float k = 1f - Mathf.Exp(-dt / (StrainSeconds * ts));
        bool strained = false;
        for (int a = 0; a < arms; a++)
        {
            Strain[a] += (c.Body.ArmForce[a].magnitude / weight - Strain[a]) * k;
            if (!b.Anchored[a] || Strain[a] < StrainLimit) strainSince[a] = now;
            else if (now - strainSince[a] > StrainHoldSeconds * ts && b.CanRelease(a) && b.Role[a] != ArmRole.Strike
                     && (!Holds(b.Role[a]) || b.CommittedSupport > b.MinAnchors))
            {
                // let go of it (no grip or support role for a while), at once; a swinging arm keeps its group and
                // only lets go (a role change per strain churned the roles)
                if (SwingTravel(c.Intent) && grouped && swingGroup[a] >= 0) strainLetGo[a] = true;
                else
                {
                    cooldown[a * RoleCount + (int)ArmRole.Anchor] = cooldown[a * RoleCount + (int)ArmRole.Grip] = now + FailCooldownSeconds * ts;
                    SetRole(b, a, ArmRole.Hang, now);
                }
                strainSince[a] = now;
                StrainReleases++;
                strained = true;
            }
        }
        bool failed = strained, emergency = !b.Grounded && b.AnchoredCount < b.MinAnchors;
        for (int a = 0; a < arms; a++)
            if (b.Report[a] == ArmReport.Failed)
            {
                // a fruitless search at rest with support to spare ends that arm's search until the intent changes
                // or support drops (user report: an octopus that re-searched every few seconds never came to rest)
                bool rest = !Travelling(c.Intent.mode) && b.Role[a] == ArmRole.Anchor && b.AnchoredCount > b.MinAnchors;
                FailedReports++;
                // swinging: the arm keeps its group and searches again (the body's swing brings new holds into
                // reach; reassigning it churned roles for nothing)
                if (SwingTravel(c.Intent) && grouped && swingGroup[a] >= 0) continue;
                cooldown[a * RoleCount + (int)b.Role[a]] = rest ? float.PositiveInfinity : now + FailCooldownSeconds * ts;
                failed = true;
            }
        // a grip arm done pulling (its hold passed or its run at its shortest) is not ranked for Grip for a while:
        // it becomes support or lets go, and another arm goes ahead (hand over hand)
        for (int a = 0; a < arms; a++)
        {
            if (b.Report[a] != ArmReport.Done) continue;
            DoneReports++;
            if (b.Role[a] == ArmRole.Grip) { cooldown[a * RoleCount + (int)ArmRole.Grip] = now + DoneCooldownSeconds * ts; failed = true; }
            // a groom finished: that idle slot ends (the next burst picks again)
            if (b.Role[a] == ArmRole.Groom) { if (idleGroom == a) idleGroom = -1; cooldown[a * RoleCount + (int)ArmRole.Groom] = now + FailCooldownSeconds * ts; failed = true; }
        }
        bool modeChanged = !started || c.Intent.mode != lastMode || c.Intent.climb != lastClimb;
        bool restNow = !Travelling(c.Intent.mode) && c.Intent.mode != OctoMode.Strike;
        if (!restNow || !wasRest) restSince = now;
        wasRest = restNow;
        // a new intent, or support running short, lifts the open-ended cool-downs
        if (modeChanged || b.AnchoredCount <= b.MinAnchors)
            for (int i = 0; i < cooldown.Length; i++) if (float.IsPositiveInfinity(cooldown[i])) cooldown[i] = 0f;
        bool strikeNew = c.Intent.mode == OctoMode.Strike && b.StrikeClaims < Mathf.Max(1, c.Intent.strikers) && AnyStrikeCandidate(c);
        // an arm that took a hold under a role that does not keep it (a feeling arm allowed to grip) is taken into
        // support at once when the support is short
        bool supportShort = b.CommittedSupport < Mathf.Min(b.AnchoredCount, b.MinAnchors);
        if (modeChanged || failed || emergency || strikeNew || supportShort || now >= nextDecision)
        {
            Decide(c, modeChanged, failed, emergency);
            nextDecision = now + DecideSeconds * ts;
        }
        lastMode = c.Intent.mode; lastClimb = c.Intent.climb;
        started = true;
        for (int a = 0; a < arms; a++) if (b.Report[a] == ArmReport.Failed || b.Report[a] == ArmReport.Done) b.Report[a] = ArmReport.None;
        if ((SwingTravel(c.Intent) || c.Intent.mode == OctoMode.Rest) && grouped) SwingTick(c, dt);
        else EndSwing(b);
        PlanMotion(c);
    }

    bool AnyStrikeCandidate(OctopusController c)
    {
        for (int a = 0; a < arms; a++) if (c.Board.Role[a] != ArmRole.Strike && StrikeReach(c, a) >= 0f) return true;
        return false;
    }

    // >= 0: the arm's root distance to the intercept (it can reach it); -1: it cannot
    float StrikeReach(OctopusController c, int a)
    {
        OctoIntent it = c.Intent;
        if (it.prey == null || !it.prey.CanBeGrabbed) return -1f;
        TentacleChain ch = c.Body.Arms[a];
        float d = (it.intercept - ch.X[0]).magnitude;
        return d <= ch.Spec.MaxStretch * ch.Length * 0.95f ? d : -1f;
    }

    // ------------------------------------------------------------------ assignment

    void Decide(OctopusController c, bool modeChanged, bool failed, bool emergency)
    {
        Decisions++;
        ArmBlackboard b = c.Board;
        OctopusBody body = c.Body;
        float L = body.Arms[0].Length, now = c.Time;
        Vector3 head = body.HeadCentre, up = c.Up;
        OctoIntent it = c.Intent;
        bool travel = Travelling(it.mode);
        Vector3 g = it.goal - head;
        Vector3 R = travel && g.sqrMagnitude > 1e-8f ? g.normalized : Vector3.zero;
        RouteDir = R;
        Vector3 lat = OctoMath.Norm(OctoMath.Cross(up, R), Vector3.zero);
        Vector3 Rl = Level(R, up);
        Quaternion q = body.Orientation;

        for (int a = 0; a < arms; a++)
        {
            want[a] = ArmRole.Rest; pre[a] = false;
            // the side of the body the arm leaves from, level (a body hanging tilted still has a front)
            Vector3 d = Level(OctoMath.Rotate(q, restDirLocal[a]), up);
            facing[a] = OctoMath.Dot(d, Rl);
            lateral[a] = OctoMath.Dot(d, lat);
            facingAnchor[a] = b.Anchored[a] ? OctoMath.Dot(OctoMath.Norm(b.Anchor[a] - head, Vector3.zero), R) : -1f;
        }

        // 1. strike: arms already striking or holding prey stay; then the nearest that can reach
        int strikers = 0;
        for (int a = 0; a < arms; a++)
        {
            TentacleAgent.State m = c.Agents[a].Mode;
            if (m == TentacleAgent.State.Strike || m == TentacleAgent.State.Hold) { want[a] = ArmRole.Strike; pre[a] = true; strikers++; }
        }
        if (it.mode == OctoMode.Strike)
        {
            int quota = Mathf.Max(1, it.strikers);
            while (strikers < quota)
            {
                int bi = -1; float bd = float.MaxValue;
                for (int a = 0; a < arms; a++)
                {
                    if (want[a] == ArmRole.Strike || CoolingDown(a, ArmRole.Strike, now)) continue;
                    float d = StrikeReach(c, a);
                    if (d >= 0f && d < bd) { bd = d; bi = a; }
                }
                if (bi < 0) break;
                want[bi] = ArmRole.Strike; pre[bi] = true; strikers++;
            }
        }

        // (never every arm: one must stay free to grip ahead - a two-armed animal with both arms kept as support
        // could not move hand over hand)
        int support = Mathf.Min(b.MinAnchors + 1, arms - 1);
        int anchoredFree = 0;   // anchored arms not striking
        for (int a = 0; a < arms; a++) if (b.Anchored[a] && want[a] != ArmRole.Strike) anchoredFree++;

        if (travel && SwingTravel(it))
        {
            DecideSwing(c, R, modeChanged);
        }
        else if (grouped && it.mode == OctoMode.Rest)
        {
            RestGrouped(c);
        }
        else if (travel)
        {
            // 2. grip: the arms facing the route (an arm already holding ahead counts double)
            int quota = it.mode == OctoMode.Stalk ? 2 : it.mode == OctoMode.Retreat ? 4 : it.urgency > 0.75f ? 4 : 3;
            for (int a = 0; a < arms; a++)
            {
                float s = facing[a];
                if (b.Anchored[a])
                {
                    // an arm holding ahead counts double; one holding behind would only let go (a free arm is
                    // better); one holding ahead at its shortest that cannot haul has nothing to pull with
                    bool ahead = OctoMath.Dot(b.Anchor[a] - head, R) > AheadFraction * L;
                    if (!ahead) s -= 1f;
                    else if (c.Agents[a].Spent() && body.Arms[a].BasePin() <= TentacleAgent.MinRun) s -= 10f;
                    else s += 0.8f * facingAnchor[a] + 0.3f;
                }
                if (b.Role[a] == ArmRole.Grip) s += Stickiness;
                if (CoolingDown(a, ArmRole.Grip, now)) s -= 10f;
                gScore[a] = s;
            }
            SortBy(gScore);
            int grips = 0, anchoredGrips = 0;
            for (int i = 0; i < arms && grips < quota; i++)
            {
                int a = order[i];
                if (want[a] == ArmRole.Strike || gScore[a] < -0.3f) continue;
                want[a] = ArmRole.Grip; grips++;
                // a grip holding behind the body is about to let go: it does not count as support
                if (b.Anchored[a] && OctoMath.Dot(b.Anchor[a] - head, R) > AheadFraction * L) anchoredGrips++;
            }
            // 3. support: the anchored non-grip arms with the most useful holds (ahead, above) stay Anchor
            int need = support - anchoredGrips;
            for (int a = 0; a < arms; a++)
            {
                sScore[a] = -100f;
                if (!b.Anchored[a] || want[a] != ArmRole.Rest) continue;
                Vector3 r = b.Anchor[a] - head;
                sScore[a] = (OctoMath.Dot(r, R) + 0.5f * OctoMath.Dot(r, up)) / L + (b.Role[a] == ArmRole.Anchor ? Stickiness : 0f)
                            - 10f * Mathf.Max(0f, b.Load[a] - 0.85f);   // a hold paid out to its limit goes first
            }
            SortBy(sScore);
            for (int i = 0; i < arms && need > 0; i++)
            {
                int a = order[i];
                if (sScore[a] <= -100f) break;
                want[a] = ArmRole.Anchor; need--;
                if (emergency) pre[a] = true;
            }
            // support short: free arms go and find holds (the ones facing the route first)
            if (need > 0)
            {
                SortBy(gScore);
                for (int i = 0; i < arms && need > 0; i++)
                {
                    int a = order[i];
                    if (want[a] != ArmRole.Rest || b.Anchored[a] || CoolingDown(a, ArmRole.Anchor, now)) continue;
                    want[a] = ArmRole.Anchor; need--;
                    if (emergency) pre[a] = true;
                }
            }
            // 4. the rest: the rearmost hang, the others tuck in (no arm feels about without a job: a free arm sweeping
            // the far side read as aimless flapping, user 2026-10-19 - support that runs short sends arms out above).
            // A free arm keeps the free role it has (these only change what an idle arm looks like; re-ranking them as
            // the body turns churned roles for nothing)
            for (int a = 0; a < arms; a++)
            {
                if (want[a] != ArmRole.Rest || b.Anchored[a]) continue;
                ArmRole cur = b.Role[a];
                if (cur == ArmRole.Hang || cur == ArmRole.Tuck && !b.Grounded) want[a] = cur;
            }
            for (int a = 0; a < arms; a++)
                if (want[a] == ArmRole.Rest) want[a] = facing[a] < -0.5f ? ArmRole.Hang : b.Grounded ? ArmRole.Rest : ArmRole.Tuck;
        }
        else
        {
            // at rest (and for the arms not striking): every hold stays; free arms find holds until RestAnchors
            // hold on (support emergency first), then idle
            int rest = Mathf.Max(c.RestAnchors, support);
            int have = 0;
            // (a hold kept from a swing keeps its Grip role: both hold, neither pulls at rest - relabelling every
            // hold at every rest and trip was most of the role churn)
            for (int a = 0; a < arms; a++)
                if (want[a] != ArmRole.Strike && b.Anchored[a]) { want[a] = b.Role[a] == ArmRole.Grip ? ArmRole.Grip : ArmRole.Anchor; have++; if (emergency) pre[a] = true; }
            // only as many go looking as holds are missing (the arms already looking first, then those whose
            // side faces up - branches are above): the rest stay curled, so the body settles
            // (falling or hanging on too little - a support emergency - every free arm grabs for a hold)
            int seek = emergency || b.AnchoredCount < b.MinAnchors && !b.Grounded ? arms : rest - have;
            if (have >= rest) seek = 0;
            for (int a = 0; a < arms; a++)
            {
                sScore[a] = -100f;
                if (want[a] != ArmRole.Rest || CoolingDown(a, ArmRole.Anchor, now)) continue;
                sScore[a] = OctoMath.Dot(OctoMath.Rotate(q, restDirLocal[a]), up) + (b.Role[a] == ArmRole.Anchor ? 1f : 0f);
            }
            SortBy(sScore);
            for (int i = 0; i < arms && seek > 0; i++)
            {
                int a = order[i];
                if (sScore[a] <= -100f) break;
                want[a] = ArmRole.Anchor; seek--;
                if (emergency || have < b.MinAnchors) pre[a] = true;
            }
            for (int a = 0; a < arms; a++)
                if (want[a] == ArmRole.Rest && b.Role[a] == ArmRole.Hang && !b.Anchored[a]) want[a] = ArmRole.Hang;   // a calm drape stays
            IdleRoles(c, have >= rest);
        }

        // forced roles (tests, the lab): pre-empt everything but a running strike
        for (int a = 0; a < arms; a++)
            if (Forced[a] >= 0 && want[a] != ArmRole.Strike) { want[a] = (ArmRole)Forced[a]; pre[a] = true; }

        // Apply: pre-empts at once; ordinary changes only after the minimum hold, at most MaxChangesPerDecision
        // (a new intent mode waives the hold and the limit for this decision).
        float hold = MinHold * c.Body.TimeScale;
        int budget = modeChanged ? arms : MaxChangesPerDecision;
        // holds are given up one per decision (the body settles onto the others between): several let go at
        // once - a new intent that needs fewer holds - dropped and swung the body into the branches
        int letGo = 1;
        // changes that add support go first
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < arms; a++)
            {
                ArmRole cur = b.Role[a], w = want[a];
                if (w == cur) continue;
                bool supportFirst = Holds(w) && !Holds(cur);
                if ((pass == 0) != supportFirst && !pre[a]) continue;
                // the strike role ends when the arm has recovered; never hold an arm in Strike past it
                // (an anchored arm taken back into support is never held back: support first)
                bool forced = pre[a] || b.Report[a] == ArmReport.Failed || b.Report[a] == ArmReport.Done || cur == ArmRole.Strike && w != ArmRole.Strike
                              || supportFirst && b.Anchored[a];
                bool releases = b.Anchored[a] && Holds(cur) && !Holds(w) && w != ArmRole.Strike;
                if (releases && !pre[a] && !(b.Report[a] == ArmReport.Failed))
                {
                    if (letGo <= 0) continue;
                    letGo--;
                }
                if (!forced)
                {
                    if (budget <= 0) continue;
                    // (a free grip arm still looking about - not reaching, not holding - whose side the body has
                    // turned away from the route, and a free arm the body has turned toward it, trade early: the
                    // body rocks on its holds and turns as the arms reach - a facing kept for MinHold went stale
                    // within half a second)
                    bool turned = travel && !SwingTravel(it) && !b.Anchored[a] && c.Agents[a].IsLooking
                                  && (cur == ArmRole.Grip ? facing[a] < RetakeBelow : w == ArmRole.Grip && facing[a] > RetakeAbove);
                    // (after half the minimum hold: a role is never flipped straight back)
                    if (!modeChanged && now - b.RoleSince[a] < (turned ? 0.5f * hold : hold)) continue;
                    budget--;
                }
                SetRole(b, a, w, now);
            }
        // commands
        for (int a = 0; a < arms; a++)
        {
            ArmCommand cmd = b.Command[a];
            cmd.role = b.Role[a];
            cmd.dir = b.Role[a] == ArmRole.Grip ? R : Vector3.zero;
            cmd.urgency = Mathf.Clamp01(it.urgency);
            cmd.allowGrip = b.AnchoredCount < support;
            cmd.groomArm = a == idleGroom ? idleGroomArm : -1;
            b.Command[a] = cmd;
        }
        b.Recount();
    }

    void SetRole(ArmBlackboard b, int a, ArmRole r, float now)
    {
        b.Role[a] = r; b.RoleSince[a] = now; Changes++;
        ArmCommand cmd = b.Command[a]; cmd.role = r; b.Command[a] = cmd;
    }

    // Rest variety (plan T6): with the support met, the spare free arms idle - mostly curled; now and then one
    // hangs and one grooms (bursts of 6-12 s x TimeScale between quiet windows of 15-30 s, all curled, so the body
    // sleeps most of the time). A new rest first settles: no burst for FirstIdleSeconds. (No arm feels about at rest
    // any more: an idle arm sweeping the air read as aimless, user 2026-10-19.)
    public const float FirstIdleSeconds = 40f;   // x TimeScale
    public float FirstIdle = FirstIdleSeconds;
    void IdleRoles(OctopusController c, bool supportMet)
    {
        ArmBlackboard b = c.Board;
        float now = c.Time, ts = c.Body.TimeScale;
        if (now < restSince + FirstIdle * ts) { idleHang = idleGroom = idleGroomArm = -1; nextIdleRoll = now; return; }
        if (now >= nextIdleRoll)
        {
            bool quiet = idleHang < 0 && idleGroom < 0 ? false : true;   // bursts and quiet windows alternate
            idleHang = idleGroom = idleGroomArm = -1;
            if (!quiet)
            {
                idleHang = PickFree(c);
                float r = Rand();
                idleGroom = PickFree(c, idleHang);
                if (idleGroom >= 0) idleGroomArm = r < 0.5f ? -1 : NeighbourOf(c, idleGroom);
            }
            nextIdleRoll = now + (quiet ? 15f + 15f * Rand() : 6f + 6f * Rand()) * ts;
        }
        for (int a = 0; a < arms; a++)
        {
            if (want[a] != ArmRole.Rest) continue;
            // a free arm hanging from the swing keeps hanging at rest (a calm drape; no relabelling)
            if (c.Board.Role[a] == ArmRole.Hang && !c.Board.Anchored[a] && a != idleGroom) { want[a] = ArmRole.Hang; continue; }
            if (!supportMet) continue;
            if (a == idleHang && !CoolingDown(a, ArmRole.Hang, now)) want[a] = ArmRole.Hang;
            else if (a == idleGroom && !CoolingDown(a, ArmRole.Groom, now)) want[a] = ArmRole.Groom;
        }
    }

    int PickFree(OctopusController c, int not = -1)
    {
        int n = 0;
        for (int a = 0; a < arms; a++) if (a != not && want[a] == ArmRole.Rest && !c.Board.Anchored[a]) n++;
        if (n == 0) return -1;
        int k = (int)(Rand() * n) % n;
        for (int a = 0; a < arms; a++) if (a != not && want[a] == ArmRole.Rest && !c.Board.Anchored[a] && k-- == 0) return a;
        return -1;
    }

    int NeighbourOf(OctopusController c, int a)
    {
        int l = (a + arms - 1) % arms, r = (a + 1) % arms;
        if (!c.Board.Anchored[l] && want[l] != ArmRole.Strike) return l;
        if (!c.Board.Anchored[r] && want[r] != ArmRole.Strike) return r;
        return -1;
    }

    // order[] = arm indices by score, highest first (insertion sort; 8 arms)
    void SortBy(float[] s)
    {
        for (int i = 0; i < arms; i++) order[i] = i;
        for (int i = 1; i < arms; i++)
        {
            int v = order[i], j = i - 1;
            while (j >= 0 && s[order[j]] < s[v]) { order[j + 1] = order[j]; j--; }
            order[j + 1] = v;
        }
    }

    // ------------------------------------------------------------------ swinging (plan T7-T10)
    // Travel and Retreat swing (user, 2026-10-18: "traverse the branches, letting the body swing under them";
    // brachiation is the preferred locomotion). Six arms work as two grip groups - "hands" of three on either
    // side of the route - and the two rearmost hang. The body hangs from one group (the pivot) and swings
    // under its holds; the other group catches holds ahead; at the forward apex of the swing (the body stopped
    // ahead, nearest the new holds) the pivot group lets go and the body swings on under the new holds. The
    // groups then trade places. Roles stay put through all of it (Grip for the groups, Hang for the rear): only
    // the group the body hangs from changes, so the roles do not churn.
    // Lengths (blackboard SwingLen): the pivot group holds the length it had when the body swung onto it, and
    // pumps around it while the catch is out of reach (shorter passing under the holds, longer at the apexes,
    // and the holds ahead shorter while the body swings forward): energy from the arms, motion from gravity.
    // The catching group follows the body without tension (its runs take in slack and pay out as the body
    // swings) so a caught hold never yanks the body; at the transfer it holds that length.
    // (a climb - back up from the ground, intent.climb - and a stalk keep the hand-over-hand pull: the body moves
    // along the holds, not under them)
    public static bool SwingTravel(in OctoIntent it) => (it.mode == OctoMode.Travel || it.mode == OctoMode.Retreat) && !it.climb;

    public const float CatchAhead = 0.25f;            // x arm length: a hold this far ahead of the pivot catches
    public const float PumpAfterSeconds = 0.8f;       // x TimeScale: no catch this long after a transfer -> pump
    public const float PumpRadial = 0.07f, PumpDrive = 0.06f;
    public const float PumpCapDeg = 45f, SwingDampRate = 1.4f;
    // The energy bound (by construction, not by tuning the pump): above GovernFromDeg of energy amplitude the
    // whole animal is damped (OctopusBody.SwingGovernor), ramping to GovernRate (x 1/TimeScale) at GovernFullDeg.
    // The pump stops at PumpCapDeg below it, so pumping alone never reaches the governor; a transfer onto holds
    // far ahead (the body starts well behind its new pivot) does, and the governor takes that energy back out.
    public const float GovernFromDeg = 40f, GovernFullDeg = 60f, GovernRate = 4f;   // deg, deg, 1/s (x 1/TimeScale)
    public const float FollowSlack = 1.02f;
    public const float MaxPumpRate = 0.6f;            // LengthTarget change per second (x 1/TimeScale)
    public const float HangChord = 0.75f, HangReelRate = 0.12f;   // x arm length; LengthTarget per second (x 1/TimeScale)

    public readonly SwingPhase Swing = new SwingPhase();
    public readonly BranchRoute Route = new BranchRoute();   // the planned waypoints to the goal (with hints)
    public const float ReplanSeconds = 6f;            // x TimeScale: no transfer this long -> plan again from here
    public const float StuckSeconds = 3f;             // x TimeScale: no transfer this long -> catches short of the waypoint
    public const float HorizonReplanSeconds = 0.5f;   // x TimeScale: the soonest a route that ended short is planned on
    public const float WaypointReach = 0.45f;         // x arm length: holds this near a waypoint have reached it
    public const float CatchNear = 0.6f;              // x arm length: a catch this near the waypoint counts
    public int Replans, WaypointsPassed;              // cumulative
    public int Swings, BranchSwings, Regroups, PivotLost;   // cumulative: transfers, transfers onto another surface, group assignments, pivots that lost every hold
    public float SwingDamping { get; private set; }   // extra body damping while the swing settles at its goal (1/s)
    public float SwingGovernor { get; private set; }  // whole-animal damping while the swing is too wide (1/s)
    public Vector3 SwingPush { get; private set; }    // the pump's push on the whole animal (m/s^2, OctopusBody.SwingPush)
    public const float PumpAccel = 0.15f, PumpTaperBoost = 2f;   // x g; the taper (1 - A/cap) x boost, at most PumpAccel
    public bool Pumping { get; private set; }
    public float PlayPump;                            // > 0: pump even without a catch waiting (idle swinging)
    readonly int[] swingGroup;
    readonly float[] swingBase, pump, heldSince;
    readonly bool[] strainLetGo;
    bool grouped;
    Vector3 groupRoute;
    float transferAt, caughtSince = -1f, plannedAt = -100f;
    bool needPlan;
    int pivotSource = -1;

    public int GroupOf(int arm) => swingGroup[arm];
    readonly int[] hangPair = { -1, -1 };

    // Resting after a trip: the groups stay (their arms keep their Grip / Hang roles - relabelling them at every
    // rest and trip was most of the role churn); every hold stays, the swing settles (SwingTick, arrived), free
    // group arms take holds only until RestAnchors hold on; the hanging pair idles - now and then one grooms.
    void RestGrouped(OctopusController c)
    {
        ArmBlackboard b = c.Board;
        for (int a = 0; a < arms; a++)
        {
            if (want[a] == ArmRole.Strike) continue;
            want[a] = swingGroup[a] >= 0 ? ArmRole.Grip : b.Anchored[a] ? ArmRole.Anchor : ArmRole.Rest;
        }
        IdleRoles(c, true);
        for (int a = 0; a < arms; a++)
        {
            if (swingGroup[a] >= 0 || want[a] == ArmRole.Strike) continue;
            if (want[a] == ArmRole.Feel || want[a] == ArmRole.Rest) want[a] = ArmRole.Hang;   // calm: no idle feeling
        }
    }

    public const float ArriveLengths = 0.5f;   // x arm length, level distance: the swing has arrived
    public bool SwingArrived { get; private set; }

    static bool Arrived(OctopusController c)
    {
        Vector3 d = c.Intent.goal - c.Body.HeadCentre;
        d -= c.Up * OctoMath.Dot(d, c.Up);
        return d.magnitude < ArriveLengths * c.Body.Arms[0].Length;
    }

    void DecideSwing(OctopusController c, Vector3 R, bool modeChanged)
    {
        Vector3 Rl = Level(R, c.Up);
        // (near the goal the route swings round as the body passes it: no regrouping there)
        bool regroup = modeChanged || !grouped || OctoMath.Dot(Rl, groupRoute) < 0.5f && !Arrived(c);
        if (regroup && Rl.sqrMagnitude > 0.5f) AssignGroups(c, Rl);
        ArmBlackboard b = c.Board;
        int groupHolds = 0;
        for (int a = 0; a < arms; a++) if (b.Anchored[a] && swingGroup[a] >= 0 && !b.Command[a].letGo) groupHolds++;
        for (int a = 0; a < arms; a++)
        {
            if (want[a] == ArmRole.Strike) continue;
            want[a] = swingGroup[a] >= 0 ? ArmRole.Grip : ArmRole.Hang;
            // the groups hold too little: a rear arm that still holds keeps holding, as support
            if (swingGroup[a] < 0 && b.Anchored[a] && groupHolds < b.MinAnchors) { want[a] = ArmRole.Anchor; pre[a] = true; }
        }
    }

    void AssignGroups(OctopusController c, Vector3 Rl)
    {
        ArmBlackboard b = c.Board;
        Regroups++;
        grouped = true;
        groupRoute = Rl;
        // the two arms facing most away from the route hang (the first time; afterwards the same two keep hanging -
        // a new direction only re-splits the groups by side, so no role changes); the others split by side into
        // two groups of three
        int hang = Mathf.Min(2, Mathf.Max(0, arms - 4));
        bool keep = hangPair[0] >= 0;
        for (int a = 0; a < arms; a++) { sScore[a] = -facing[a]; swingGroup[a] = 0; }
        if (keep) { for (int i = 0; i < hang; i++) swingGroup[hangPair[i]] = -1; }
        else
        {
            SortBy(sScore);
            for (int i = 0; i < hang; i++) { swingGroup[order[i]] = -1; hangPair[i] = order[i]; }
        }
        for (int a = 0; a < arms; a++) sScore[a] = swingGroup[a] < 0 ? -100f : lateral[a];
        SortBy(sScore);
        int n = arms - hang;
        for (int i = 0; i < n; i++) swingGroup[order[i]] = i < (n + 1) / 2 ? 0 : 1;
        // the body hangs from the group holding the most (ties: the one holding farther back, so the other
        // reaches ahead)
        int h0 = 0, h1 = 0; float x0 = 0f, x1 = 0f;
        Vector3 head = c.Body.HeadCentre;
        for (int a = 0; a < arms; a++)
        {
            if (!b.Anchored[a] || swingGroup[a] < 0) continue;
            float x = OctoMath.Dot(b.Anchor[a] - head, Rl);
            if (swingGroup[a] == 0) { h0++; x0 += x; } else { h1++; x1 += x; }
        }
        b.PivotGroup = h0 != h1 ? (h0 > h1 ? 0 : 1) : (h0 == 0 || x0 / h0 <= x1 / h1 ? 0 : 1);
        for (int a = 0; a < arms; a++) { swingBase[a] = 0f; pump[a] = 0f; strainLetGo[a] = false; heldSince[a] = -1f; }
        transferAt = c.Time; caughtSince = -1f; pivotSource = -1;
        needPlan = true;
        Swing.Reset();
    }

    void EndSwing(ArmBlackboard b)
    {
        if (!grouped && b.PivotGroup < 0) return;
        grouped = false;
        b.PivotGroup = -1;
        for (int a = 0; a < arms; a++)
        {
            b.SwingLen[a] = 0f; swingGroup[a] = -1;
            ArmCommand cmd = b.Command[a]; cmd.swing = false; cmd.letGo = false; cmd.group = -1; b.Command[a] = cmd; b.Group[a] = -1;
        }
        SwingDamping = SwingGovernor = 0f; SwingPush = Vector3.zero; Pumping = false;
        Route.Clear(); b.HasSwingTarget = false;
    }

    // The run's length factor (as LengthTarget) at which its tether holds the base pin exactly where it is.
    static float RunScale(TentacleChain ch)
    {
        int p = ch.BasePin();
        if (p <= 0) return 0f;
        return (ch.PinGoal[p] - ch.X[0]).magnitude / (TentacleChain.Taut * ch.Spacing * p);   // (an easing pin: where it goes)
    }

    // Every tick while swinging: the pivot, the phase, the transfer, the lengths, who may seek a hold.
    void SwingTick(OctopusController c, float dt)
    {
        ArmBlackboard b = c.Board;
        OctopusBody body = c.Body;
        float L = body.Arms[0].Length, ts = body.TimeScale, now = c.Time;
        Vector3 up = c.Up, Rl = Level(c.Intent.goal - body.HeadCentre, up);
        if (Rl.sqrMagnitude < 0.5f) Rl = groupRoute;
        float tipR = body.Spec.arm.TipRadius * body.SizeScale;
        int pg = b.PivotGroup, fg = 1 - pg;
        for (int a = 0; a < arms; a++)
        {
            if (!b.Anchored[a]) heldSince[a] = -1f;
            else if (heldSince[a] < 0f) heldSince[a] = now;
        }
        int pivotN = PivotCentroid(b, pg, out Vector3 P);
        // nothing to hang from in the pivot group, but the other group holds: the body hangs from that one
        if (pivotN == 0 && PivotCentroid(b, fg, out _) > 0)
        {
            PivotLost++;
            b.PivotGroup = pg = fg; fg = 1 - pg;
            for (int a = 0; a < arms; a++) if (swingGroup[a] == pg) swingBase[a] = 0f;
            pivotN = PivotCentroid(b, pg, out P);
        }
        if (pivotN == 0)
        {
            // hanging from nothing of the groups (the rear arms, or falling): every group arm looks for a hold
            for (int a = 0; a < arms; a++) SetSwingCommand(b, a, swingGroup[a] >= 0, false, 0f);
            Swing.Reset(); Pumping = false; SwingDamping = SwingGovernor = 0f; SwingPush = Vector3.zero;
            return;
        }
        // the route: planned over the branch hints from the holds to the goal (again for a new goal, or when the
        // swing has made no transfer for ReplanSeconds); the swing heads for its next waypoint
        bool restingNow = c.Intent.mode == OctoMode.Rest;
        if (restingNow && Route.Valid) Route.Clear();
        bool newGoal = !restingNow && (needPlan || (Route.Goal - c.Intent.goal).sqrMagnitude > 0.09f * L * L);
        needPlan = false;
        // (a plan that found no route is tried again soon: the holds have moved)
        float again = (Route.Valid ? ReplanSeconds : 1f) * ts;
        // a route that ended short of the goal (the plan's horizon: its query budget, or the hints ran out there) is
        // planned on from the holds it reached at once - swinging on without a waypoint, any hold ahead was a catch,
        // a trunk's too, and the body swung down a stem out of the crowns
        bool horizon = Route.Valid && Route.Done && !Route.ReachesGoal && now - plannedAt > HorizonReplanSeconds * ts;
        if (c.Hints != null && !restingNow && (newGoal || !SwingArrived && (horizon || now - transferAt > again && now - plannedAt > again)))
        {
            if (!newGoal) Replans++;
            Route.Plan(c.Hints, P, c.Intent.goal, up, L, 1.5f * tipR);
            plannedAt = now;
        }
        if (c.Hints == null && Route.Valid) Route.Clear();
        if (Route.Valid)
        {
            WaypointsPassed += Route.Advance(P, up, L, WaypointReach);
            if (!Route.Done)
            {
                Vector3 wl = Level(Route.Next - P, up);
                if (wl.sqrMagnitude > 0.5f) Rl = wl;
            }
        }
        b.HasSwingTarget = Route.Valid && !Route.Done;
        b.SwingTarget = b.HasSwingTarget ? Route.Next : c.Intent.goal;
        // no transfer for StuckSeconds: the waypoint is out of reach from this hang (and the pump has not brought
        // it into reach) - a hold ahead on the way to it is a catch too, and the swing moves up to it
        b.SwingTargetStrict = now - transferAt < StuckSeconds * ts;
        // the pivot's front: catches count as ahead of its foremost hold (a pivot spread over two branches would
        // otherwise take a catch on the forward one of them as a swing ahead - and hang between them, not swing)
        float front = FrontOf(b, pg, P, Rl, out int rear, out float back);
        b.SwingPivot = P + Rl * front; b.SwingRoute = Rl; b.SwingCentre = P;
        // spread along the route (holds on two branches): the rearmost lets go, if the support allows - the body
        // swings under the forward ones. With no hold to spare (one rear and one front hold: the body hung between
        // them, held back from the swing for good) the pivot group first takes another hold beside its front one
        // (letting the rear go at once left the body on a single hold, and the arms fought: 270 body weights)
        bool spread = front - back > SpreadLengths * L && rear >= 0;
        if (spread && b.CanRelease(rear) && pivotN > b.MinAnchorsHanging) SetLetGo(b, rear, true);
        bool spreadShort = spread && pivotN <= b.MinAnchorsHanging;
        b.SupportCentre = spreadShort ? b.SwingPivot : P;
        Vector3 com = body.Lattice.CentreOfMass();
        Swing.Update(com, P, up, Rl, OctopusController.Gravity, dt, 0.06f * ts);

        int frontAhead = 0;
        for (int a = 0; a < arms; a++)
            if (b.Anchored[a] && swingGroup[a] == fg && !b.Command[a].letGo && IsCatch(b, a, P, Rl, front, L)) frontAhead++;
        int need = Mathf.Min(b.MinAnchorsHanging, CountGroup(fg));
        bool caught = need > 0 && frontAhead >= need;
        if (caught && caughtSince < 0f) caughtSince = now;
        if (!caught) caughtSince = -1f;

        // arrived (the body is over the goal): no more transfers or pumping; every group arm may take a hold
        // and the swing dies out (until the brain picks the next goal)
        bool resting = c.Intent.mode == OctoMode.Rest;
        SwingArrived = resting || Arrived(c);
        // the transfer: at the forward apex (or with no swing to wait for, or once a whole swing has been missed)
        float period = 2f * Mathf.PI / Mathf.Max(Swing.Natural, 1e-3f);
        bool apex = !Swing.Valid || Swing.Amplitude < 4f * Mathf.Deg2Rad || Swing.Theta > 0f && Swing.Omega <= 0f;
        // ... and not while the body would start its swing on the new holds far behind them (a catch far ahead of a
        // short swing): the swing that follows would start past TransferMaxDeg - the energy of a drop from there,
        // not the pump's. It waits for a nearer apex, at most two periods
        if (caught && !SwingArrived && (apex || now - caughtSince > period) && (now - caughtSince > 2f * period || CatchAngle(b, fg, P, Rl, front, L, up, com) < TransferMaxDeg * Mathf.Deg2Rad))
        {
            for (int a = 0; a < arms; a++)
            {
                if (swingGroup[a] == pg && b.Anchored[a]) SetLetGo(b, a, true);
                // the new pivot is the catch ahead only: a hold of that group beside or behind (an old hold) would
                // keep the body from swinging on under the catch
                if (swingGroup[a] == fg && b.Anchored[a] && !IsCatch(b, a, P, Rl, front, L)) SetLetGo(b, a, true);
                if (swingGroup[a] >= 0) swingBase[a] = 0f;   // the new pivot locks the length it has now (below)
            }
            int src = MajoritySource(c, fg);
            if (pivotSource >= 0 && src >= 0 && src != pivotSource) BranchSwings++;
            pivotSource = src;
            b.PivotGroup = pg = fg; fg = 1 - pg;
            Swings++;
            transferAt = now; caughtSince = -1f;
            pivotN = PivotCentroid(b, pg, out P);
            front = FrontOf(b, pg, P, Rl, out _, out _);
            b.SwingPivot = P + Rl * front; b.SwingCentre = b.SupportCentre = P; spreadShort = false;
        }
        else if (pivotSource < 0) pivotSource = MajoritySource(c, pg);

        // pumping: while no catch is waiting (the holds ahead are out of reach) or for play, below the cap
        float amp = Swing.Amplitude * Mathf.Rad2Deg;
        bool pumpWanted = (!caught && now - transferAt > PumpAfterSeconds * ts) || PlayPump > 0f;
        Pumping = pumpWanted && amp < PumpCapDeg && !SwingArrived;
        SwingDamping = SwingArrived ? SwingDampRate / ts : 0f;
        // (only for a body hanging below its holds: one above them - holds taken below at spawn - has no swing)
        SwingGovernor = Swing.Valid && Mathf.Abs(Swing.Theta) < 0.5f * Mathf.PI
            ? GovernRate / ts * Mathf.Clamp01((amp - GovernFromDeg) / (GovernFullDeg - GovernFromDeg)) : 0f;
        bool swinging = Swing.Valid && Swing.Amplitude > 1f * Mathf.Deg2Rad;
        float w = Swing.Valid ? Swing.Omega / Mathf.Max(Swing.Natural, 1e-3f) : 0f;
        // the drive pulls the body the way it swings; from rest it drives at the natural frequency to seed it
        bool still = !swinging || Mathf.Abs(w) < 0.15f * Mathf.Max(Swing.Amplitude, 0.05f);
        float drive = !still ? Mathf.Clamp(w / Mathf.Max(Swing.Amplitude, 0.05f), -1f, 1f) : Mathf.Sin(Swing.Natural * now);
        float radial = swinging ? 2f * Swing.Kinetic - 1f : 0f;
        float maxStep = MaxPumpRate / ts * dt;
        // the pump's energy: a push on the whole animal along the swing's tangent, the way it swings (from stillness
        // at the natural frequency), PumpAccel x g, tapering to nothing at the cap - an explicit, bounded energy input.
        // (The holds' length pumping alone could not even hold a swing: soft arms stretch at the bottom of every
        // swing, where the pump shortens them, and the swing died out under it - 46 to 5 deg in 15 s of pumping.)
        // (only while waiting: not while an arm reaches for or closes on a catch - the push swung the body into
        // the catch at speed and the holds fought it, 250 body weights)
        SwingPush = Vector3.zero;
        bool catching = false;
        for (int a = 0; a < arms; a++)
            if (swingGroup[a] == fg && (c.Agents[a].Mode == TentacleAgent.State.Reach || c.Agents[a].Mode == TentacleAgent.State.Grip)) catching = true;
        if (Pumping && !catching && Swing.Valid && Mathf.Abs(Swing.Theta) < 0.5f * Mathf.PI)
        {
            Vector3 tangent = Rl * Mathf.Cos(Swing.Theta) + up * Mathf.Sin(Swing.Theta);
            SwingPush = tangent * (PumpAccel * OctopusController.Gravity * drive * Mathf.Min(1f, PumpTaperBoost * Mathf.Max(0f, 1f - amp / PumpCapDeg)));
        }

        for (int a = 0; a < arms; a++)
        {
            int grp = swingGroup[a];
            if (grp < 0) { SetSwingCommand(b, a, false, false, 0f); continue; }
            TentacleChain ch = body.Arms[a];
            bool anchored = b.Anchored[a];
            bool letGo = b.Command[a].letGo;
            if (letGo && !anchored && c.Agents[a].Mode != TentacleAgent.State.Release) letGo = false;
            if (strainLetGo[a]) { if (anchored) letGo = true; strainLetGo[a] = false; }
            // a hold on a very short run (taken at spawn, close under a branch) fights the others: let it go while
            // the support allows (the swing's own catches take longer runs, TentacleAgent.SwingMinRun)
            if (anchored && !letGo && ch.BasePin() > 0 && ch.BasePin() < TentacleAgent.MinRun && b.CanRelease(a) && b.AnchoredCount > b.MinAnchorsHanging) letGo = true;
            float len = 0f;
            if (!anchored) swingBase[a] = 0f;   // (a new hold takes the length it has when it is caught)
            if (anchored)
            {
                float cur = RunScale(ch);
                if (resting && !letGo)
                {
                    // resting: every hold keeps one length (a follow length re-read every tick kept the body awake)
                    if (swingBase[a] <= 0f) swingBase[a] = Mathf.Clamp(cur, ch.Spec.MinStretch, ch.Spec.MaxStretch);
                    len = swingBase[a];
                    pump[a] = 0f;
                }
                else if (grp == pg && !letGo)
                {
                    // the pivot holds the length it has when the body swings onto it (a run shortened or lengthened
                    // at that moment snatched or dropped the body); the pump works within the room the run has
                    if (swingBase[a] <= 0f) swingBase[a] = Mathf.Clamp(cur, ch.Spec.MinStretch, ch.Spec.MaxStretch);
                    // and then draws the body up toward a comfortable hang (HangChord x the arm's length below its
                    // holds), slowly: a body hanging at full stretch cannot reach the next branch
                    int bp = ch.BasePin();
                    if (bp > 0 && !SwingArrived)
                    {
                        float hang = Mathf.Clamp(HangChord * L / (TentacleChain.Taut * ch.Spacing * bp), ch.Spec.MinStretch, ch.Spec.MaxStretch);
                        if (swingBase[a] > hang) swingBase[a] = Mathf.MoveTowards(swingBase[a], hang, HangReelRate / ts * dt);
                    }
                    float along = Mathf.Clamp(OctoMath.Dot(b.Anchor[a] - P, Rl) / (0.15f * L), -1f, 1f);
                    float target = Pumping ? -PumpRadial * radial - PumpDrive * drive * along : 0f;
                    target = Mathf.Clamp(target, ch.Spec.MinStretch / swingBase[a] - 1f, ch.Spec.MaxStretch / swingBase[a] - 1f);
                    pump[a] = Mathf.MoveTowards(pump[a], target, maxStep);
                    len = swingBase[a] * (1f + pump[a]);
                }
                else
                {
                    // catching (or letting go): follow the body, no tension
                    len = cur * FollowSlack;
                    pump[a] = 0f;
                    // a catch that does not lie ahead (beside or behind the pivot) is let go after a while
                    if (grp == fg && !letGo && !SwingArrived && !IsCatch(b, a, P, Rl, front, L) && heldSince[a] >= 0f && now - heldSince[a] > 1.5f * ts && b.CanRelease(a))
                        letGo = true;
                }
                len = Mathf.Clamp(len, ch.Spec.MinStretch, ch.Spec.MaxStretch);
            }
            // who seeks a hold: the catching group always; the pivot group only to make up the support
            bool seek = !letGo && (resting ? b.AnchoredCount < c.RestAnchors && !anchored
                                           : grp == fg || SwingArrived || pivotN < b.MinAnchorsHanging || spreadShort || b.AnchoredCount < b.MinAnchors);
            SetSwingCommand(b, a, seek, letGo, len);
        }
    }

    // A catch: a hold ahead of the pivot's front, or (with a route) one near the next waypoint.
    bool IsCatch(ArmBlackboard b, int a, Vector3 P, Vector3 Rl, float front, float L)
    {
        Vector3 h = b.Anchor[a];
        if (b.HasSwingTarget && b.SwingTargetStrict && (h - b.SwingTarget).magnitude < CatchNear * L && OctoMath.Dot(h - P, Rl) > front) return true;
        return OctoMath.Dot(h - P, Rl) > front + CatchAhead * L;
    }

    public const float TransferMaxDeg = 70f;

    // The body's angle from straight down under the catch holds of group g (rad, unsigned).
    float CatchAngle(ArmBlackboard b, int g, Vector3 P, Vector3 Rl, float front, float L, Vector3 up, Vector3 com)
    {
        Vector3 c = Vector3.zero; int n = 0;
        for (int a = 0; a < arms; a++)
            if (b.Anchored[a] && swingGroup[a] == g && !b.Command[a].letGo && IsCatch(b, a, P, Rl, front, L)) { c += b.Anchor[a]; n++; }
        if (n == 0) return 0f;
        Vector3 d = com - c * (1f / n);
        return Mathf.Abs(Mathf.Atan2(OctoMath.Dot(d, Rl), Mathf.Max(-OctoMath.Dot(d, up), 1e-4f)));
    }

    public const float SpreadLengths = 0.35f;   // x arm length: a pivot spread farther along the route lets its rearmost go

    // The foremost and rearmost of group g's holds along the route, relative to P (and the rearmost's arm).
    float FrontOf(ArmBlackboard b, int g, Vector3 P, Vector3 Rl, out int rear, out float back)
    {
        float f = float.NegativeInfinity; back = float.PositiveInfinity; rear = -1;
        for (int a = 0; a < arms; a++)
        {
            if (!b.Anchored[a] || swingGroup[a] != g || b.Command[a].letGo) continue;
            float x = OctoMath.Dot(b.Anchor[a] - P, Rl);
            if (x > f) f = x;
            if (x < back) { back = x; rear = a; }
        }
        if (rear < 0) { back = 0f; return 0f; }
        return f;
    }

    int PivotCentroid(ArmBlackboard b, int g, out Vector3 P)
    {
        P = Vector3.zero; int n = 0;
        for (int a = 0; a < arms; a++)
            if (b.Anchored[a] && swingGroup[a] == g && !b.Command[a].letGo) { P += b.Anchor[a]; n++; }
        if (n > 0) P *= 1f / n;
        return n;
    }

    int CountGroup(int g) { int n = 0; for (int a = 0; a < arms; a++) if (swingGroup[a] == g) n++; return n; }

    int MajoritySource(OctopusController c, int g)
    {
        int best = -1, bestN = 0;
        for (int a = 0; a < arms; a++)
        {
            if (swingGroup[a] != g || !c.Board.Anchored[a]) continue;
            TentacleChain ch = c.Body.Arms[a];
            int p = ch.BasePin();
            if (p <= 0) continue;
            int src = ch.PinSource[p], n = 0;
            for (int o = 0; o < arms; o++)
            {
                if (swingGroup[o] != g || !c.Board.Anchored[o]) continue;
                int q = c.Body.Arms[o].BasePin();
                if (q > 0 && c.Body.Arms[o].PinSource[q] == src) n++;
            }
            if (n > bestN) { bestN = n; best = src; }
        }
        return best;
    }

    void SetLetGo(ArmBlackboard b, int a, bool v) { ArmCommand cmd = b.Command[a]; cmd.letGo = v; b.Command[a] = cmd; }

    void SetSwingCommand(ArmBlackboard b, int a, bool seek, bool letGo, float len)
    {
        ArmCommand cmd = b.Command[a];
        cmd.swing = swingGroup[a] >= 0;
        cmd.group = swingGroup[a];
        cmd.allowGrip = seek;
        cmd.letGo = letGo;
        if (cmd.swing) cmd.dir = b.SwingRoute;
        b.Command[a] = cmd;
        b.Group[a] = swingGroup[a];
        b.SwingLen[a] = len;
    }

    // ------------------------------------------------------------------ motion

    public const float TravelSpeed = 0.3f, TravelSpeedUrgent = 0.4f;   // m/s at scale 1 (+ urgency x the second)
    public const float AheadFraction = 0.05f;                           // x arm length: a hold this far ahead pulls

    // One body motion for this tick: along the route, while at least one grip arm holds ahead of the body with
    // run left to shorten (the lead: the best aligned, kept while it stays ahead). Every grip arm holding ahead
    // shortens toward the SAME next root position (root + DesiredVelocity dt); every other hold pays out what that
    // motion asks of it and never shortens. Arms that each shortened or lengthened on their own asked for
    // different body positions through their tethers: the old tug-of-war, at hundreds of times the body's
    // weight. The speed is scaled down so that no paying-out hold must stretch past its maximum (it would stop
    // the body and fight the grips), and it rises at most MaxAccel x g: a pull that started at full speed in one
    // tick snatched the body at up to 25 g. Slowing down is immediate (it only means pulling less).
    void PlanMotion(OctopusController c)
    {
        ArmBlackboard b = c.Board;
        OctopusBody body = c.Body;
        Vector3 want = PlanVelocity(c);
        float wl = want.magnitude, pl = planned.magnitude;
        if (wl <= pl) planned = pl > 1e-9f ? want : Vector3.zero;   // slower or a new direction at no more speed: at once
        else
        {
            // faster: the speed rises at MaxAccel (scale-free), in the wanted direction
            float sp = Mathf.Min(wl, pl + MaxAccel * OctopusController.Gravity * Mathf.Max(c.LastDt, 1e-4f));
            planned = want * (sp / wl);
        }
        // with nobody pulling, no motion is planned at all (the holds would only pay out); with no motion planned
        // (blocked: a hold paid out to its limit) nobody pulls - a pull toward the root where it already is
        // would follow every small swing toward the hold and ratchet the run shorter against the blocked hold
        if (b.Puller < 0) planned = Vector3.zero;
        b.DesiredVelocity = planned;
        if (planned.sqrMagnitude < 1e-12f) { b.Puller = -1; for (int a = 0; a < arms; a++) b.Pulling[a] = false; }
    }

    public const float MaxAccel = 0.15f;   // x g
    Vector3 planned;

    Vector3 PlanVelocity(OctopusController c)
    {
        ArmBlackboard b = c.Board;
        OctopusBody body = c.Body;
        OctoIntent it = c.Intent;
        int keep = b.Puller;
        b.Puller = -1;
        for (int a = 0; a < arms; a++) b.Pulling[a] = false;
        if (!Travelling(it.mode) || SwingTravel(it)) return Vector3.zero;   // swinging: gravity moves the body
        float L = body.Arms[0].Length;
        Vector3 head = body.HeadCentre, g = it.goal - head;
        float gl = g.magnitude;
        if (gl < 0.15f * L) return Vector3.zero;
        Vector3 R = g / gl;
        RouteDir = R;
        int lead = -1; float best = -2f;
        for (int a = 0; a < arms; a++)
        {
            if (!b.Anchored[a] || b.Role[a] != ArmRole.Grip) continue;
            Vector3 r = b.Anchor[a] - head;
            if (OctoMath.Dot(r, R) <= AheadFraction * L) continue;
            // a run already at its shortest has nothing left to pull with
            if (c.Agents[a].Spent()) continue;
            float s = OctoMath.Dot(OctoMath.Norm(r, Vector3.zero), R) + (a == keep ? Stickiness : 0f);
            b.Pulling[a] = true;
            if (s > best) { best = s; lead = a; }
        }
        TentacleChain lc = lead >= 0 ? body.Arms[lead] : null;
        int lp = lc != null ? lc.BasePin() : -1;
        Vector3 toPin = lp > 0 ? lc.X[lp] - lc.X[0] : Vector3.zero;
        float tl = toPin.magnitude;
        if (lead < 0 || lp <= 0 || tl < 1e-5f)
        {
            for (int a = 0; a < arms; a++) b.Pulling[a] = false;
            return Vector3.zero;
        }
        b.Puller = lead;
        b.Pulling[lead] = true;
        float speed = (TravelSpeed + TravelSpeedUrgent * Mathf.Clamp01(it.urgency)) * Mathf.Sqrt(body.SizeScale);
        if (it.mode == OctoMode.Stalk) speed *= 0.5f;
        float dt = Mathf.Max(c.LastDt, 1e-4f);
        // never past the goal, never past the run's shortest (the lead then is done)
        speed = Mathf.Min(speed, 0.5f * gl / dt);
        Vector3 v = R * speed;
        float scale = 1f;
        for (int a = 0; a < arms; a++)
        {
            if (!b.Anchored[a] || b.Pulling[a]) continue;
            TentacleChain ch = body.Arms[a];
            int p = ch.BasePin();
            if (p <= 0) continue;
            Vector3 d = ch.X[0] - ch.X[p];
            float chord = d.magnitude;
            if (chord < 1e-6f) continue;
            float rate = OctoMath.Dot(v, d) / chord;        // how fast this hold must pay out
            if (rate <= 0f) continue;
            float room = (TentacleChain.Taut * ch.Spec.MaxStretch * ch.Spacing * p - chord) / dt;
            if (room < rate) scale = Mathf.Min(scale, Mathf.Max(0f, room / rate));
        }
        return v * scale;
    }
}
