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
    bool started;
    uint rng;
    // rest variety (T6): idle slots re-rolled every 8-20 s (x TimeScale), with quiet windows
    float nextIdleRoll, restSince;
    bool wasRest;
    int idleHang = -1, idleFeel = -1, idleGroom = -1, idleGroomArm = -1;

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
                // let go of it (no grip or support role for a while), at once
                cooldown[a * RoleCount + (int)ArmRole.Anchor] = cooldown[a * RoleCount + (int)ArmRole.Grip] = now + FailCooldownSeconds * ts;
                SetRole(b, a, ArmRole.Hang, now);
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
                cooldown[a * RoleCount + (int)b.Role[a]] = rest ? float.PositiveInfinity : now + FailCooldownSeconds * ts;
                failed = true;
                FailedReports++;
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
        bool modeChanged = !started || c.Intent.mode != lastMode;
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
        lastMode = c.Intent.mode;
        started = true;
        for (int a = 0; a < arms; a++) if (b.Report[a] == ArmReport.Failed || b.Report[a] == ArmReport.Done) b.Report[a] = ArmReport.None;
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

        int support = b.MinAnchors + 1;
        int anchoredFree = 0;   // anchored arms not striking
        for (int a = 0; a < arms; a++) if (b.Anchored[a] && want[a] != ArmRole.Strike) anchoredFree++;

        if (travel)
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
            // 4. the rest: one feels the far side, the rearmost hang, the others tuck in. A free arm keeps the
            // free role it has (these only change what an idle arm looks like; re-ranking them as the body turns
            // churned roles for nothing)
            bool feelTaken = false;
            for (int a = 0; a < arms; a++)
            {
                if (want[a] != ArmRole.Rest || b.Anchored[a]) continue;
                ArmRole cur = b.Role[a];
                if (cur == ArmRole.Feel && !feelTaken && !CoolingDown(a, ArmRole.Feel, now)) { want[a] = ArmRole.Feel; feelTaken = true; }
                else if (cur == ArmRole.Hang || cur == ArmRole.Tuck && !b.Grounded) want[a] = cur;
            }
            int feel = -1; float fl = -1f;
            for (int a = 0; a < arms; a++)
                if (want[a] == ArmRole.Rest && !CoolingDown(a, ArmRole.Feel, now))
                {
                    float s = Mathf.Abs(lateral[a]) + (b.Role[a] == ArmRole.Feel ? Stickiness : 0f);
                    if (s > fl) { fl = s; feel = a; }
                }
            if (feel >= 0 && !feelTaken) want[feel] = ArmRole.Feel;
            for (int a = 0; a < arms; a++)
                if (want[a] == ArmRole.Rest) want[a] = facing[a] < -0.5f ? ArmRole.Hang : b.Grounded ? ArmRole.Rest : ArmRole.Tuck;
        }
        else
        {
            // at rest (and for the arms not striking): every hold stays; free arms find holds until RestAnchors
            // hold on (support emergency first), then idle
            int rest = Mathf.Max(c.RestAnchors, support);
            int have = 0;
            for (int a = 0; a < arms; a++)
                if (want[a] != ArmRole.Strike && b.Anchored[a]) { want[a] = ArmRole.Anchor; have++; if (emergency) pre[a] = true; }
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
                    if (!modeChanged && now - b.RoleSince[a] < hold) continue;
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
    // hangs, one feels about, one grooms (bursts of 6-12 s x TimeScale between quiet windows of 15-30 s, all
    // curled, so the body sleeps most of the time). A new rest first settles: no burst for FirstIdleSeconds.
    public const float FirstIdleSeconds = 40f;   // x TimeScale
    public float FirstIdle = FirstIdleSeconds;
    void IdleRoles(OctopusController c, bool supportMet)
    {
        ArmBlackboard b = c.Board;
        float now = c.Time, ts = c.Body.TimeScale;
        if (now < restSince + FirstIdle * ts) { idleHang = idleFeel = idleGroom = idleGroomArm = -1; nextIdleRoll = now; return; }
        if (now >= nextIdleRoll)
        {
            bool quiet = idleHang < 0 && idleFeel < 0 && idleGroom < 0 ? false : true;   // bursts and quiet windows alternate
            idleHang = idleFeel = idleGroom = idleGroomArm = -1;
            if (!quiet)
            {
                idleHang = PickFree(c);
                float r = Rand();
                if (r < 0.5f) { idleGroom = PickFree(c, idleHang); if (idleGroom >= 0) idleGroomArm = Rand() < 0.5f ? -1 : NeighbourOf(c, idleGroom); }
                else idleFeel = PickFree(c, idleHang);
            }
            nextIdleRoll = now + (quiet ? 15f + 15f * Rand() : 6f + 6f * Rand()) * ts;
        }
        for (int a = 0; a < arms; a++)
        {
            if (want[a] != ArmRole.Rest) continue;
            if (!supportMet) continue;
            if (a == idleHang && !CoolingDown(a, ArmRole.Hang, now)) want[a] = ArmRole.Hang;
            else if (a == idleFeel && !CoolingDown(a, ArmRole.Feel, now)) want[a] = ArmRole.Feel;
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
        if (!Travelling(it.mode)) return Vector3.zero;
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
