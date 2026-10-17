using UnityEngine;

// Climbing for one CreatureSim (design 2026-10-17-creature-climbing): the per-joint up field, head anticipation
// of walls and edges, steepness gait factors and the pitch-fold limiter. Exists only for a species that can
// climb on an ISurfaceProbe ground; the sim then grounds every joint along Up[i] instead of gravity's up.
//
// Up field: each joint's low-passed surface normal is Gaussian-smoothed along the chain, given a climb weight
// w = smoothstep(25, 50 deg) of its angle to gravity's up, and its target up is slerp(gravity, normal, w): level
// ground and dunes keep gravity's up, a wall or a trunk gets the surface's. Up turns toward the target at most
// upRateDeg per second (Froude-scaled). The head also looks ahead: a forward cast finds a wall (concave turn,
// e.g. ground -> trunk), a probe ahead that finds no surface triggers a wrap cast back under the edge (convex
// turn, e.g. a wall's top); the anticipated normal pulls the head's up so it turns onto the new surface before
// reaching it. A surface steeper than maxSurfaceAngle ahead sets Blocked (the sim brakes).
//
// Active is false while the whole body stands on gentle terrain with nothing near: the sim then runs its
// walking code unchanged (bit-identical), and this class only watches for a reason to switch on.
sealed class CreatureClimb
{
    const float kWeightLo = 25f, kWeightHi = 50f;   // climb weight from the surface angle to gravity's up
    const float kSteepLo = 30f, kSteepHi = 80f;     // gait steepness from the up's angle to gravity's up
    const float kWallDeg = 35f;                      // a surface ahead this far from the head's up is a turn
    const float kAheadSegments = 4f, kAheadTime = 0.3f; // lookahead: segments + seconds of travel
    const float kRampDeg = 50f;                      // terrain ahead rising steeper than this switches climbing on
    const float kTerrainWallDeg = 50f;               // terrain this steep under a joint is a wall (dunes stay below ~35)
    const float kReliefBodies = 0.5f;                // a terrain wall is climbed only over a relief this many body lengths tall
    const int kReliefSamples = 6;
    const int kGateEvery = 4;                        // walking: sub-steps between the surface gate / terrain wall checks
    const float kStayDeg = 40f;                      // climbing stays on while something this steep is under or ahead of the body
    const float kOffDelay = 1.5f;                    // seconds of nothing to climb before the ups relax (covers the walk from the activating cast to the fan's reach)
    const float kOffDeg = 0.5f;                      // every joint's up within this of gravity's up to switch off
    const float kGateReach = 1.5f;                   // gate bounds: joints grown by this x (lateral reach + window)

    readonly CreatureSpecies sp;
    readonly ClimbSpec spec;
    readonly CompositeSurfaceProbe composite;        // null when the ground is another ISurfaceProbe
    public readonly ISurfaceProbe Surface;           // exact (feet, casts)
    public readonly ISurfaceProbe Spine;             // the spine's (the terrain's smooth view)
    readonly IGroundProbe terrainSmooth;             // composite terrain smooth view: the ramp check
    readonly CachedGroundProbe terrainCache;         // the same, read-only (Peek) when it is a cache
    readonly float scale, timeScale;
    readonly int n;

    public readonly Vector3[] Up;       // per joint: the up the joint grounds along (gravity's while inactive)
    public readonly float[] Weight;     // per joint: climb weight 0..1
    public readonly float[] Steep;      // per joint: gait steepness 0..1
    public readonly int[] Source;       // per joint: what it stands on (0 = terrain)
    readonly Vector3[] grav;            // per joint: gravity's up
    readonly Vector3[] smoothN;
    readonly float[] kernel;
    readonly int K;

    // The head's trail: (position, up) every quarter segment of head travel, with the arc length travelled.
    // A joint's up follows the up the head had where the joint now is (follow the leader), so the body makes
    // the head's turn onto a wall at the place the head made it - a joint at a wall's foot that still probes
    // the ground under it would otherwise never turn.
    const float kTrailWeight = 0.7f;
    readonly Vector3[] trailPos, trailUp;
    readonly float[] trailArc;
    int trailCount, trailHead; // ring: trailHead = index of the newest entry
    float headArc;

    public readonly int SlotCount, AheadSlot;
    public bool Active { get; private set; }
    public bool Blocked { get; private set; }
    public bool ClimbAllowed = true;
    float offTimer;
    int tick;

    // head anticipation (also for the debug view)
    public Vector3 AheadNormal;
    public float AheadWeight;            // low-passed
    Vector3 rawNormal;
    float rawWeight;
    const float kAheadTau = 0.08f;       // release of the anticipation, seconds
    const float kFanClearances = 3f;     // fan reach: head clearances plus one segment
    const float kTurnSlow = 0.6f;        // speed lost while the head still has the whole anticipated turn to make
    float turnLeft;
    public Vector3 CastFrom, CastTo;     // the last anticipation ray
    public bool CastHit;
    public Vector3 WrapFrom, WrapTo;
    public bool WrapHit;
    public int Casts, WrapCasts;         // cumulative

    public CreatureClimb(CreatureSpecies sp, ISurfaceProbe ground, float scale, float timeScale)
    {
        this.sp = sp; spec = sp.climb; this.scale = scale; this.timeScale = timeScale;
        n = sp.JointCount;
        composite = ground as CompositeSurfaceProbe;
        Surface = ground;
        Spine = composite != null ? composite.Smooth : ground;
        terrainSmooth = composite != null ? (composite.Terrain is CachedGroundProbe c ? c.Smooth : composite.Terrain) : null;
        terrainCache = composite != null ? composite.Terrain as CachedGroundProbe : null;
        int legs = sp.legs != null ? sp.legs.Length : 0;
        AheadSlot = n + 3 * legs;
        SlotCount = AheadSlot + 1;
        if (ground is ISurfaceSlots slots) slots.EnsureSlots(SlotCount);
        Up = new Vector3[n]; Weight = new float[n]; Steep = new float[n]; Source = new int[n];
        grav = new Vector3[n]; smoothN = new Vector3[n];
        K = Mathf.Max(1, spec.UpSmoothJoints);
        kernel = new float[2 * K + 1];
        for (int k = -K; k <= K; k++) { float x = k / (float)K; kernel[k + K] = Mathf.Exp(-2f * x * x); }
        int cap = 4 * n + 16;
        trailPos = new Vector3[cap]; trailUp = new Vector3[cap]; trailArc = new float[cap];
    }

    // ---- the head's trail ----

    void Record(Vector3 head, Vector3 up)
    {
        float step = 0.25f * sp.spacing * scale;
        if (trailCount > 0)
        {
            float d = (head - trailPos[trailHead]).magnitude;
            if (d < step) return;
            headArc += d;
        }
        trailHead = (trailHead + 1) % trailPos.Length;
        trailPos[trailHead] = head; trailUp[trailHead] = up; trailArc[trailHead] = headArc;
        if (trailCount < trailPos.Length) trailCount++;
    }

    // The head's up where it was `behind` metres of travel ago (interpolated); false past the trail's end.
    bool TrailUp(Vector3 head, float behind, out Vector3 up, out Vector3 at)
    {
        up = default; at = default;
        if (trailCount < 2) return false;
        float s = headArc + (head - trailPos[trailHead]).magnitude - behind;
        int L = trailPos.Length;
        for (int k = 0; k < trailCount - 1; k++)
        {
            int a = (trailHead - k + L) % L, b = (a - 1 + L) % L;
            if (trailArc[b] <= s)
            {
                float span = trailArc[a] - trailArc[b];
                float t = span > 1e-6f ? Mathf.Clamp01((s - trailArc[b]) / span) : 0f;
                if (s > trailArc[a]) { up = trailUp[a]; at = trailPos[a]; return true; } // between the newest entry and the head
                up = SurfaceMath.Slerp(trailUp[b], trailUp[a], t);
                at = Vector3.Lerp(trailPos[b], trailPos[a], t);
                return true;
            }
        }
        return false;
    }

    public int LegSlot(int leg, int k) => n + 3 * leg + k;

    // ---- per sub-step, before the head moves ----

    // Gate the surfaces to the body's surroundings and decide whether climbing is on this sub-step.
    // pathUp: gravity's up per joint from the last grounding (kept in Up while inactive).
    public void Begin(float dt, Vector3[] path, Vector3[] pathUp, Vector3[] groundNormals, Vector3 heading, float speed, float window)
    {
        tick++;
        // the colliders' gate every kGateEvery sub-steps while walking (a body moves ~1 cm a sub-step; the gate's
        // bounds are grown by metres), every sub-step while climbing
        if (composite != null && (Active || tick % kGateEvery == 1 || composite.SurfacesEnabled != ClimbAllowed))
        {
            composite.SurfacesEnabled = ClimbAllowed;
            Vector3 lo = path[0], hi = path[0];
            for (int i = 1; i < n; i++) { lo = Vector3.Min(lo, path[i]); hi = Vector3.Max(hi, path[i]); }
            float grow = kGateReach * (sp.MaxLateralReach * scale + window);
            var b = new Bounds(0.5f * (lo + hi), hi - lo);
            b.Expand(2f * grow);
            composite.SetActiveBounds(b);
        }
        if (!Active)
        {
            Record(path[0], pathUp[0]);
            for (int i = 0; i < n; i++) { Up[i] = pathUp[i]; grav[i] = pathUp[i]; }
            if (WantsOn(path, pathUp, groundNormals, heading, speed, window)) { Active = true; offTimer = 0f; }
        }
        if (!Active) { Blocked = false; AheadWeight = 0f; return; }

        float seg = sp.spacing * scale;
        float clear0 = sp.joints[0].clearance * scale * ClearanceScale(0);
        if (composite != null) composite.CasterOnCollider = Source[0] != 0;
        Anticipate(path[0], heading, Up[0], grav[0], clear0, seg);
        // low-passed: the fan's hits come and go as the head passes over a mesh's triangles and edges
        float k = 1f - Mathf.Exp(-dt / (kAheadTau * timeScale));
        if (rawWeight > 0f) AheadNormal = AheadWeight > 1e-3f ? SurfaceMath.Slerp(AheadNormal, rawNormal, k) : rawNormal;
        AheadWeight += (rawWeight - AheadWeight) * k;
        if (AheadWeight < 1e-3f) AheadWeight = 0f;
        // how much of the turn the head has still to make: it slows for it (a corner is taken carefully)
        turnLeft = AheadWeight > 0f ? AheadWeight * Mathf.Clamp01(SurfaceMath.AngleDeg(Up[0], AheadNormal) / 60f) : 0f;
    }

    // A reason to climb: a joint on a steep surface, a surface near the head (gate), or terrain ahead that rises
    // or drops steeply. Cheap: one AABB test and at most one terrain read.
    bool WantsOn(Vector3[] path, Vector3[] pathUp, Vector3[] groundNormals, Vector3 heading, float speed, float window)
    {
        float seg = sp.spacing * scale;
        // terrain is climbed only where it is a wall - steeper than any dune or slope the creature walks - over a
        // relief taller than kReliefBodies body lengths; smaller relief (a dug hole under a big creature, a
        // brink) is walked as before (draped over, R25), and gentle ground never switches climbing on
        float cosWall = Mathf.Cos(kTerrainWallDeg * Mathf.Deg2Rad);
        bool steep = false;
        for (int i = 0; i < n && !steep; i++)
            if (SurfaceMath.Dot(groundNormals[i], pathUp[i]) < cosWall) steep = true;
        if (steep && TerrainRelief(path, pathUp[0], heading, window) >= kReliefBodies * sp.BodyLength * scale) return true;
        if (composite != null && composite.SurfacesNear)
        {
            // a wall (a collider, not the terrain) ahead of the head?
            float L = kAheadSegments * seg + kAheadTime * speed;
            Casts++;
            if (composite.Surfaces.Cast(path[0], heading, L, out SurfaceHit hit) && SurfaceMath.AngleDeg(hit.normal, pathUp[0]) > kWallDeg)
                return true;
        }
        if (terrainSmooth != null && tick % kGateEvery == 3)
        {
            // A terrain wall ahead: one sample of the field (stateless - the creature's ground cache is not touched)
            // at a point the lookahead ahead and as high above the head's ground as a kRampDeg slope rises over
            // that distance: solid there means a wall steeper than any dune within reach, and only then is its
            // relief measured.
            DensityField f = composite.Field;
            Vector3 g = pathUp[0];
            float clear0 = sp.joints[0].clearance * scale;
            Vector3 here = path[0] - g * clear0;
            float d = kAheadSegments * seg + kAheadTime * speed;
            if (f != null)
            {
                Vector3 q = here + heading * d + g * (clear0 + d * Mathf.Tan(kRampDeg * Mathf.Deg2Rad));
                if (f.Sample(q) > 0f && TerrainRelief(path, g, heading, window) >= kReliefBodies * sp.BodyLength * scale) return true;
            }
            else if (TerrainHeight(here + heading * d, g, window, out Vector3 q)
                     && SurfaceMath.Dot(q - here, g) > d * Mathf.Tan(kRampDeg * Mathf.Deg2Rad)
                     && TerrainRelief(path, g, heading, window) >= kReliefBodies * sp.BodyLength * scale) return true;
        }
        return false;
    }

    // The terrain under q for a gating decision, read-only: a cached terrain answers from the nodes it already
    // has (CachedGroundProbe.Peek; probing here would change what the creature's own queries return later), a
    // stateless one is probed. Flat worlds only for the cache (its lattice is world XZ).
    bool TerrainHeight(Vector3 q, Vector3 g, float window, out Vector3 p)
    {
        if (terrainCache != null)
        {
            p = q;
            if (g.y < 0.99999f || !terrainCache.Peek(q.x, q.z, out float y)) return false;
            p = new Vector3(q.x, y, q.z);
            return true;
        }
        return terrainSmooth.Probe(q, g, window, out p, out _);
    }

    // For the relief (asked only once the ramp or the steepness check fired): the stateless field probe when
    // there is a field (reads the real terrain ahead, changes nothing), else the terrain's own read-only view.
    FieldGroundProbe reliefProbe;
    bool ReliefHeight(Vector3 q, Vector3 g, float window, out Vector3 p)
    {
        DensityField f = composite != null ? composite.Field : null;
        if (f != null)
        {
            if (reliefProbe == null) reliefProbe = new FieldGroundProbe(f);
            reliefProbe.Field = f;
            return reliefProbe.Probe(q, g, window, out p, out _);
        }
        return TerrainHeight(q, g, window, out p);
    }

    // Height range of the terrain (the cache's smooth patch) along the heading, from a quarter body behind
    // the head to a body length ahead, through the window around the head's own ground: kReliefSamples reads.
    float TerrainRelief(Vector3[] path, Vector3 g, Vector3 heading, float window)
    {
        if (terrainSmooth == null) return float.PositiveInfinity; // no terrain view: do not filter
        float bl = sp.BodyLength * scale, clear0 = sp.joints[0].clearance * scale;
        Vector3 here = path[0] - g * clear0;
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        Vector3 from = here;
        for (int k = 0; k < kReliefSamples; k++)
        {
            float d = Mathf.Lerp(-0.25f, 1f, k / (kReliefSamples - 1f)) * bl;
            Vector3 q = here + heading * d;
            // probe from the last height found (a wall ahead rises out of a narrow window)
            q += g * SurfaceMath.Dot(from - q, g);
            if (!ReliefHeight(q, g, Mathf.Max(window, bl), out Vector3 p)) continue;
            float h = SurfaceMath.Dot(p - here, g);
            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            from = p;
        }
        return hi > lo ? hi - lo : 0f;
    }

    // The leader's surface: a fan of short casts from the head's centre - down along its up, forward-down,
    // forward, and from a point ahead back down under it (the face beyond an edge) - whose normals, weighted by
    // how close each hit is, give the surface the head is about to be on. On open ground only the down cast
    // hits (the ground's normal); in a concave corner the down and forward casts meet both faces (their bisector,
    // then the wall's as the head comes up it); past a convex edge the back-down cast finds the far face. A hit
    // steeper than maxSurfaceAngle close ahead (an overhang) sets Blocked.
    void Anticipate(Vector3 head, Vector3 heading, Vector3 up0, Vector3 g0, float clear0, float seg)
    {
        rawWeight = 0f; Blocked = false; CastHit = false; WrapHit = false;
        float R = kFanClearances * clear0 + seg;
        Vector3 sumN = Vector3.zero;
        float sumW = 0f;
        Fan(head, -up0, R, head, ref sumN, ref sumW, g0, false);
        Fan(head, SurfaceMath.Norm(heading - up0, -up0), R, head, ref sumN, ref sumW, g0, false);
        CastFrom = head; CastTo = head + heading * R;
        CastHit = Fan(head, heading, R, head, ref sumN, ref sumW, g0, true);
        Vector3 q = head + heading * R;
        WrapFrom = q; WrapTo = q + SurfaceMath.Norm(-heading - up0, -up0) * (1.5f * R);
        WrapHit = Fan(q, SurfaceMath.Norm(-heading - up0, -up0), 1.5f * R, head, ref sumN, ref sumW, g0, true);
        if (!(sumW > 0f)) return;
        rawNormal = SurfaceMath.Norm(sumN, up0);
        rawWeight = 1f;
    }

    // One fan cast; its hit's weight falls off quadratically with its distance from the head.
    bool Fan(Vector3 from, Vector3 dir, float len, Vector3 head, ref Vector3 sumN, ref float sumW, Vector3 g0, bool canBlock)
    {
        Casts++;
        if (!Surface.Cast(from, dir, len, out SurfaceHit h)) return false;
        float d = (h.point - head).magnitude, reach = 1.5f * len;
        float w = 1f - d / reach;
        if (!(w > 0f)) return true;
        w *= w;
        sumN += h.normal * w; sumW += w;
        if (canBlock && SurfaceMath.AngleDeg(h.normal, g0) > spec.MaxSurfaceAngle && w > 0.1f) Blocked = true;
        return true;
    }

    // ---- per joint, during grounding ----

    public float ClearanceScale(int i) => 1f + (spec.ClearanceFactor - 1f) * Steep[i];

    // A joint whose up has turned away from gravity probes only a few clearances along it: across a pit or
    // between a trunk and a branch the walking window (metres) would reach the surface opposite.
    const float kTiltWindowClearances = 3f;
    public float Window(int i, float window, float clear)
        => Steep[i] > 0f || Weight[i] > 0f ? Mathf.Min(window, kTiltWindowClearances * clear) : window;

    // The joint's ground along its up (slot i, preferring what it stood on); past an edge, where nothing is
    // under it, a wrap cast from the joint's centre back under the edge (behind and into the surface).
    public bool ProbeJoint(int i, Vector3 near, Vector3 u, float window, Vector3 centre, Vector3 fwd, float clear, out Vector3 point, out Vector3 normal)
    {
        if (Spine.Probe(near, u, window, Source[i], i, out SurfaceHit h))
        {
            Source[i] = h.source; point = h.point; normal = h.normal;
            return true;
        }
        Vector3 dir = SurfaceMath.Norm(-u - fwd, -u);
        WrapCasts++;
        if (Surface.Cast(centre, dir, 1.5f * (clear + window), out h) && SurfaceMath.Dot(h.normal, u) > -0.2f)
        {
            Source[i] = h.source; point = h.point; normal = h.normal;
            return true;
        }
        point = near; normal = u;
        return false;
    }

    // The walking spine's probe (climbing off): exactly the terrain answer the plain IGroundProbe call gives,
    // but with the joint's slot, so a body walking under a tree reuses its collider raycasts' misses.
    public bool ProbeWalking(int i, Vector3 near, Vector3 u, float window, out Vector3 point, out Vector3 normal)
    {
        bool ok = Spine.Probe(near, u, window, SurfaceHit.NoSource, i, out SurfaceHit h);
        point = h.point; normal = h.normal;
        return ok;
    }

    // ---- per sub-step, after grounding ----

    // New targets from the low-passed ground normals, turned toward at the up rate. Copies Up into pathUp (the
    // chain step and grounding of the next sub-step use it). Switches climbing off after kOffDelay of nothing.
    public void UpdateUpField(float dt, Vector3[] path, Vector3[] groundNormals, Vector3[] pathUp, System.Func<Vector3, Vector3> upAt)
    {
        if (!Active) return;
        Record(path[0], Up[0]);
        for (int i = 0; i < n; i++)
        {
            Vector3 g = upAt(path[i]);
            grav[i] = SurfaceMath.Norm(g, Vector3.up);
        }
        float maxRad = spec.UpRateDeg / timeScale * dt * Mathf.Deg2Rad;
        float seg = sp.spacing * scale;
        // Is there still something to climb? A joint on a collider, ground steeper than kStayDeg under a joint, or a
        // steep surface ahead (the fan always sees something, level ground too). Below kStayDeg - dunes, and the
        // ground a creature walks off a wall onto - every target is gravity's up: the ups turn back at the up rate
        // and climbing switches off once they are there (before, dune faces past the weight's 25 deg kept
        // climbing on for good, and every later dug hole was climbed into instead of walked over).
        float cosStay = Mathf.Cos(kStayDeg * Mathf.Deg2Rad);
        bool any = Blocked || (AheadWeight > 0f && SurfaceMath.Dot(AheadNormal, grav[0]) < cosStay);
        for (int i = 0; i < n && !any; i++)
            if (Source[i] != 0 || SurfaceMath.Dot(groundNormals[i], grav[i]) < cosStay) any = true;
        // nothing to climb for kOffDelay (not a passing moment, e.g. just switched on before the fan reaches the wall)
        if (any) offTimer = 0f; else offTimer += dt;
        bool relax = offTimer >= kOffDelay;
        float maxDev = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 s = Vector3.zero;
            for (int k = -K; k <= K; k++)
            {
                int j = i + k;
                if (j < 0 || j >= n) continue;
                s += groundNormals[j] * kernel[k + K];
            }
            Vector3 nn = SurfaceMath.Norm(s, groundNormals[i]);
            smoothN[i] = nn;
            float w = relax ? 0f : SurfaceMath.SmoothStep(kWeightLo, kWeightHi, SurfaceMath.AngleDeg(nn, grav[i]));
            Weight[i] = w;
            Vector3 target = SurfaceMath.Slerp(grav[i], nn, w);
            if (relax) { }
            else if (i <= 1 && AheadWeight > 0f) target = SurfaceMath.Slerp(target, AheadNormal, AheadWeight);
            else if (i > 1 && TrailUp(path[0], i * seg, out Vector3 tu, out _)) target = SurfaceMath.Slerp(target, tu, kTrailWeight);
            Up[i] = SurfaceMath.RotateTowards(Up[i], target, maxRad);
            float dev = SurfaceMath.AngleDeg(Up[i], grav[i]);
            maxDev = Mathf.Max(maxDev, dev);
            Steep[i] = SurfaceMath.SmoothStep(kSteepLo, kSteepHi, dev);
            pathUp[i] = Up[i];
        }
        if (!relax || maxDev > kOffDeg) return;
        // back to walking: gravity's up exactly, nothing remembered
        Active = false; Blocked = false; AheadWeight = 0f;
        for (int i = 0; i < n; i++) { Up[i] = grav[i]; pathUp[i] = grav[i]; Weight[i] = 0f; Steep[i] = 0f; Source[i] = 0; }
    }

    // Gravity's up at joint i as last measured (valid while active).
    public Vector3 Gravity(int i) => grav[i];

    // Speed factor: speedFactor going up a vertical surface, descentSpeedFactor head-first down, blended by
    // the head's steepness and how much the heading points up or down.
    public float SpeedFactor(Vector3 heading)
    {
        if (!Active) return 1f;
        float slow = 1f - kTurnSlow * turnLeft;
        float s = Steep[0];
        if (s <= 0f) return slow;
        float t = 0.5f + 0.5f * Mathf.Clamp(SurfaceMath.Dot(heading, grav[0]) * 2f, -1f, 1f);
        float f = Mathf.Lerp(spec.DescentSpeedFactor, spec.SpeedFactor, t);
        return slow * (1f + (f - 1f) * s);
    }

    // ---- the pitch-fold limiter ----

    const float kPitchRelax = 0.6f;      // share of the wanted lift applied per pass (the caller runs a few)

    // Where the chain folds sharply in pitch (about its joint's right axis) the fold is lifted away from the
    // surface: a concave fold (the body bending up, ground -> wall) lifts its apex joint along its up; a convex
    // fold (bending down, over an edge or a rim) lifts the two joints either side of it (lifting the apex would
    // sharpen it). The wanted lifts of the whole chain are gathered first, each joint taking the largest one
    // asked of it, then a relaxed share is applied - corrected one at a time, neighbouring folds made each
    // other worse in turn and the body zigzagged. Only ever lifts. Returns whether anything moved; the caller
    // restores the segment lengths after each pass.
    public static bool LimitPitch(Vector3[] path, Vector3[] up, float seg, float limitDeg, float[] lift)
    {
        int n = path.Length;
        float lim = limitDeg * Mathf.Deg2Rad;
        bool any = false;
        for (int i = 0; i < n; i++) lift[i] = 0f;
        for (int i = 1; i < n - 1; i++)
        {
            Vector3 a = path[i - 1] - path[i], b = path[i] - path[i + 1];
            Vector3 u = up[i];
            Vector3 f = a + b;
            f -= u * SurfaceMath.Dot(f, u);
            float fm = f.magnitude;
            if (fm < 1e-6f) continue;
            f /= fm;
            float pa = Mathf.Atan2(SurfaceMath.Dot(a, u), SurfaceMath.Dot(a, f));
            float pb = Mathf.Atan2(SurfaceMath.Dot(b, u), SurfaceMath.Dot(b, f));
            float bend = pa - pb;
            if (bend > lim)
            {
                lift[i] = Mathf.Max(lift[i], 0.5f * (bend - lim) * seg);
                any = true;
            }
            else if (bend < -lim)
            {
                float d = 0.5f * (-lim - bend) * seg;
                lift[i - 1] = Mathf.Max(lift[i - 1], d);
                lift[i + 1] = Mathf.Max(lift[i + 1], d);
                any = true;
            }
        }
        if (!any) return false;
        for (int i = 0; i < n; i++)
            if (lift[i] > 0f) path[i] += up[i] * (kPitchRelax * lift[i]);
        return true;
    }
}
