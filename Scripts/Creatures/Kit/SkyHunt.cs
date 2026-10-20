// Creature kit: SkyHunt - a soaring predator's hunt (the Pterodactyl): it climbs high, circles there in long glides broken
// by bursts of slow deep beats, picks prey below (HuntPrey: another creature in its area; with none, a practice
// mark on the ground), lines up on it and stoops - wings folded, the body pitched down its path, the neck reaching -
// then pulls out just over the target, the head striking with the beak agape, and climbs away to circle again. Hunts
// come in bouts; between bouts the species' other flight behaviour (FlightExcursion: patrols, landings, ground time)
// has the brain. Drives the flight only through its inputs (SetGoals, DiveFold, Model.DemandCap, Chain.Level) and
// the neck through IHeadAim. A ModularBrain behaviour (BrainFactory): allocation-free after construction.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // The creatures a hunter may take: the world's sims (CreatureWorld sets Source each tick; tests set their own).
    public static class HuntPrey
    {
        public static IReadOnlyList<CreatureSim> Source;

        // The nearest (planar) creature of another species inside the hunter's area. False: none.
        public static bool Nearest(CreatureSim hunter, in EnclosureArea area, out CreatureSim prey)
        {
            prey = null;
            IReadOnlyList<CreatureSim> src = Source;
            if (src == null) return false;
            float best = float.MaxValue;
            Vector3 at = hunter.Joints[0];
            for (int i = 0; i < src.Count; i++)
            {
                CreatureSim s = src[i];
                if (s == null || s == hunter || s.Species == hunter.Species) continue;
                if (area.PlanarDistance(s.Joints[0]) > area.safeRadius) continue;
                Vector3 d = s.Joints[0] - at; d -= area.up * Vector3.Dot(d, area.up);
                float m = d.sqrMagnitude;
                if (m < best) { best = m; prey = s; }
            }
            return prey != null;
        }
    }

    [Serializable]
    [Module("SkyHunt", CreatureModule.Behaviour, Summary = "A soaring predator's hunt: climb to soarAltitude, circle in long glides and bursts of slow deep beats, stoop on prey below (HuntPrey; with none, a practice mark on the ground) with the wings folded and the neck reaching, pull out over it with a strike, climb away. In bouts; the species' FlightExcursion flies between them. Gives the species a ModularBrain.",
            Needs = "Fly (+ a LongNeck: the strike)", KitOnly = true)]
    public sealed class SkyHunt : CreatureModule
    {
        [Param(ParamUnit.Metres, "circling height over the ground (the aviary is made 1.25 x this tall)", Example = "pterodactyl 9")] public float soarAltitude = 9f;
        [Param(ParamUnit.Metres, "the soaring band: glides down this far below soarAltitude, beats back up", Example = "pterodactyl 1.2")] public float soarBand = 1.2f;
        [Param(ParamUnit.Fraction, "circle radius, x the area's safe radius", Min = 0.2f, Max = 0.9f)] public float circleRadius = 0.55f;
        [Param(ParamUnit.Seconds, "a hunting bout's length (then the other flight behaviour has the brain)")] public float boutSeconds = 50f;
        [Param(ParamUnit.Seconds, "rest between bouts")] public float restSeconds = 25f;
        [Param(ParamUnit.Seconds, "mean time between stoops within a bout")] public float stoopInterval = 12f;
        [Param(ParamUnit.Degrees, "the stoop is at least this steep (the line from where it folds to the pull-out over the mark)", Min = 15, Max = 85)] public float stoopDeg = 38f;
        [Param(ParamUnit.Metres, "pulls out this high over the target (the head reaches below it)", Example = "pterodactyl 0.9")] public float pullUpHeight = 0.9f;
        [Param(ParamUnit.Fraction, "the wings' fold in the stoop at its steepest", Min = 0.3f, Max = 1)] public float stoopFold = 0.9f;
        [Param(ParamUnit.Flag, "with no prey in its area it stoops at a practice mark on the ground")] public bool practice = true;
        [Param(ParamUnit.Fraction, "utility while hunting (FlightExcursion 0.2, Wander 0.1)", Min = 0.25f, Max = 2)] public float utility = 0.4f;
        [Param(ParamUnit.Flag, "when it gives up the brain (a bout ends mid-glide, a landing starts) it hands the flight back: the wings free to beat (Model.DemandCap), no dive fold, the trunk's level. Off = shipped: a bout that ended in a held glide left the wings held, and the next landing glided round its point at floor height without a beat until it timed out")]
        public bool handBack;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Module<Fly>() == null) return "SkyHunt needs Fly";
            if (!(soarAltitude > 0f) || soarBand < 0f || !(boutSeconds > 0f) || restSeconds < 0f || !(stoopInterval > 0f) || !(pullUpHeight > 0f))
                return "SkyHunt: soarAltitude, boutSeconds, stoopInterval, pullUpHeight > 0; soarBand, restSeconds >= 0";
            return null;
        }

        public override void Configure(CreatureSpecies runtime) { if (runtime.flight != null) runtime.flight.soarCeiling = soarAltitude + soarBand; }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => c.Species.CanFly ? new SkyHuntBehaviour(this, c.Seed) : null;
    }

    public sealed class SkyHuntBehaviour : IBehaviour
    {
        public enum Phase { Circle, Stalk, Stoop, PullUp }

        readonly SkyHunt h;
        uint rng;
        IHeadAim neck; bool lookedNeck;
        float boutUntil = -1f, restUntil = -1f, stoopIn, phaseTime;
        bool beating;   // the soaring band: beating back up (else gliding down)
        CreatureSim prey;
        float lastHm = float.MaxValue;   // the beak's distance to the mark last tick (stoop)
        Vector3 mark;   // the target point (prey: its head, updated; practice: fixed)
        float defaultLevel = -1f;
        float stalkSpeed;

        public Phase Current { get; private set; } = Phase.Circle;
        public int Stoops { get; private set; }
        public int Aborts { get; private set; }             // stoops broken off high after overshooting the mark (not counted in Stoops)
        public int Strikes { get; private set; }            // stoops whose head came within reach of the target
        public float LastMiss { get; private set; }          // closest head-to-target distance of the last stoop (m)
        public float LastLowest { get; private set; }        // lowest girdle height over the ground in the last stoop (m)
        public Vector3 Target => mark;
        public int LastPull { get; private set; } = -1;      // why the last stoop pulled out: 0 height, 1 past the mark, 2 time, 3 broken off high, 4 struck
        public float PullAbove { get; private set; }         // its height over the mark then (m)
        public bool HasPrey => prey != null;
        float miss, lowest;

        public SkyHuntBehaviour(SkyHunt hunt, int seed)
        {
            h = hunt;
            rng = (uint)seed * 1103515245u + 12345u; if (rng == 0) rng = 7;
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        IHeadAim Neck(CreatureSim sim)
        {
            if (lookedNeck) return neck;
            lookedNeck = true;
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is IHeadAim a) { neck = a; break; }
            return neck;
        }

        public float Utility(in BrainCtx c)
        {
            CreatureFlight fl = c.Sim.Flight;
            if (fl == null) return -1f;
            float T = c.Sim.TimeScale;
            // a stoop or its pull-out is never interrupted
            if (Current == Phase.Stoop || Current == Phase.PullUp) return h.utility + 1f;
            if (!fl.Airborne || fl.Landing) return HandBack(fl);
            // bouts on the brain's clock: a bout, then a rest the other flight behaviour flies
            if (c.Time < restUntil) return HandBack(fl);
            if (boutUntil < 0f) boutUntil = c.Time + h.boutSeconds * T;
            if (c.Time > boutUntil) { boutUntil = -1f; restUntil = c.Time + h.restSeconds * T; return HandBack(fl); }
            holding = true;
            return h.utility;
        }

        // SkyHunt.handBack: out of the hunt, the flight's inputs it held are released once (-1: no utility)
        bool holding;
        float HandBack(CreatureFlight fl)
        {
            if (h.handBack && holding)
            {
                holding = false;
                fl.Model.DemandCap = float.MaxValue; fl.DiveFold = 0f;
                if (defaultLevel >= 0f) fl.Chain.Level = defaultLevel;
            }
            return -1f;
        }

        public void Enter(in BrainCtx c)
        {
            Current = Phase.Circle; phaseTime = 0f;
            stoopIn = h.stoopInterval * c.Sim.TimeScale * (0.4f + 0.6f * Rand());
            if (defaultLevel < 0f && c.Sim.Flight != null) defaultLevel = c.Sim.Flight.Chain.Level;
        }

        public BrainOutput Tick(in BrainCtx c, float dt)
        {
            CreatureSim sim = c.Sim;
            CreatureFlight fl = sim.Flight;
            Vector3 head = sim.Joints[0];
            if (fl == null) return BrainOutput.Toward(head);
            if (defaultLevel < 0f) defaultLevel = fl.Chain.Level;
            float T = sim.TimeScale, s = sim.SizeScale;
            phaseTime += dt;
            preyStep = dt; preyT = T;
            FlightModel m = fl.Model;
            EnclosureArea a = c.Area;
            Vector3 up = a.up.sqrMagnitude > 0.5f ? a.up : Vector3.up;
            float ground = Vector3.Dot(a.center, up);
            float ceiling = ground + (a.maxHeight > 0f ? 0.9f * a.maxHeight : float.MaxValue);
            float soar = Mathf.Min(ground + h.soarAltitude * s, ceiling - h.soarBand * s);
            float cruise = sim.Species.flight.cruiseSpeed * T;
            IHeadAim aim = Neck(sim);
            switch (Current)
            {
                case Phase.Circle:
                case Phase.Stalk:
                {
                    fl.DiveFold = 0f; fl.Chain.Level = defaultLevel;
                    // the soaring band: glide down to its bottom (the wings held spread: no beat), beat back up to its top
                    float alt = m.Altitude;
                    if (alt < soar - h.soarBand * s) beating = true;
                    else if (alt > soar + 0.5f * h.soarBand * s) beating = false;
                    m.DemandCap = beating || Current == Phase.Stalk ? float.MaxValue : 0f;
                    float goal = beating ? soar + h.soarBand * s : soar - 1.5f * h.soarBand * s;
                    float turn;
                    Vector3? toward = null;
                    if (Current == Phase.Circle)
                    {
                        turn = CircleTurn(fl, a, up, T);
                        stoopIn -= dt;
                        if (stoopIn <= 0f && alt > soar - h.soarBand * s && PickTarget(sim, a, up, ground)) { Current = Phase.Stalk; phaseTime = 0f; }
                        aim?.Release();
                    }
                    else
                    {
                        // line up on the mark; stoop when a folded-wing fall from here would carry it onto the mark (its
                        // horizontal speed x the time to fall to the pull-out height), the line at least stoopDeg steep
                        UpdateMark(a, up, ground);
                        turn = fl.TurnToward(mark); toward = mark;
                        Vector3 to = mark - m.Position;
                        Vector3 toH = to - up * Vector3.Dot(to, up);
                        float above = -Vector3.Dot(to, up) - h.pullUpHeight * s;
                        float dGuide = toH.magnitude - kStrikeLead * NeckReach(sim);
                        float dPred = m.HorizontalSpeed * Mathf.Sqrt(2f * Mathf.Max(0f, above) / FlightModel.G);
                        float dep = Mathf.Atan2(Mathf.Max(0f, above), Mathf.Max(1e-4f, dGuide)) * Mathf.Rad2Deg;
                        float off = Vector3.Angle(m.Forward, toH);
                        aim?.Aim(mark, 0f);
                        if (above > 0f && off < 20f && dGuide <= kFallShare * dPred && dGuide > 0.45f * dPred && dep >= h.stoopDeg) { Current = Phase.Stoop; phaseTime = 0f; Stoops++; miss = float.MaxValue; lowest = float.MaxValue; lastHm = float.MaxValue; }
                        else if (phaseTime > 20f * T || dGuide < 0.4f * dPred || alt < soar - 3f * h.soarBand * s) { Current = Phase.Circle; stoopIn = 2f * T; aim?.Release(); }   // overflew it or sank: go round
                        // far off it cruises up to the mark, slowing to its minimum speed for the last stretch (a slow
                        // fall carries it onto the mark inside the aviary)
                        float minV = sim.Species.flight.minSpeed * T;
                        float dMin = minV * Mathf.Sqrt(2f * Mathf.Max(0f, above) / FlightModel.G);
                        stalkSpeed = Mathf.Lerp(minV, cruise, Mathf.Clamp01((dGuide - dMin) / Mathf.Max(1e-3f, dMin)));
                        goal = soar;
                    }
                    if (fl.WallTurn(out float wall)) { turn = wall; toward = null; }
                    // gliding asks for the speed it has (no thrust demand: the wings stay spread); beating, for cruise
                    // stalking it slows to its minimum speed (a short fall then carries it onto the mark: a stoop
                    // from cruise overshot a mark inside the aviary)
                    float speed = Current == Phase.Stalk ? stalkSpeed : beating ? cruise : Mathf.Max(m.ForwardSpeed, sim.Species.flight.minSpeed * T);
                    fl.SetGoals(Mathf.Min(goal, ceiling), speed, turn, toward);
                    break;
                }
                case Phase.Stoop:
                {
                    UpdateMark(a, up, ground);
                    // the body is guided to a point a neck short of the mark (the head, carried ahead, strikes the mark)
                    float neckReach = NeckReach(sim);
                    Vector3 fH = m.Forward - up * Vector3.Dot(m.Forward, up);
                    fH = fH.sqrMagnitude > 1e-8f ? fH.normalized : m.Forward;
                    // the pull-out swoops along a circle of radius v^2 / (kPullG g): it loses r (1 - cos gamma) of height
                    // and runs r sin gamma on - the guide point leads the mark by that run, the pull starts that much higher
                    Vector3 v0 = m.Velocity;
                    float sp0 = v0.magnitude, gam0 = Mathf.Asin(Mathf.Clamp(-Vector3.Dot(v0, up) / Mathf.Max(1e-4f, sp0), -1f, 1f));
                    float rPull = sp0 * sp0 / (kPullG * FlightModel.G);
                    // (x kPullShare: the wings' own grip stops most of the fall at once - measured, the full circle overshot 4-5 x)
                    float drop = gam0 > 0f ? kPullShare * rPull * (1f - Mathf.Cos(gam0)) : 0f, run = gam0 > 0f ? kPullShare * rPull * Mathf.Sin(gam0) : 0f;
                    Vector3 guide = mark - fH * (kStrikeLead * neckReach + run);
                    Vector3 to = guide - m.Position;
                    Vector3 toH = to - up * Vector3.Dot(to, up);
                    float above = Vector3.Dot(m.Position - mark, up);
                    // fold to steepen the path onto the line of sight (a shallower path folds more, a steeper one opens)
                    float los = Mathf.Atan2(-Vector3.Dot(to, up), Mathf.Max(1e-4f, toH.magnitude));
                    Vector3 v = m.Velocity;
                    float gam = Mathf.Atan2(-Vector3.Dot(v, up), Mathf.Max(1e-4f, (v - up * Vector3.Dot(v, up)).magnitude));
                    float ease = Mathf.Clamp01(phaseTime / (0.35f * T));
                    float fold = Mathf.Clamp(0.55f + 2.2f * (los - gam), 0.2f, h.stoopFold) * ease;
                    fl.DiveFold = fold;
                    m.DemandCap = 0f;
                    // overshooting (its run to the pull-out height longer than the way to the guide point): brake the
                    // run at up to kBrakeG g - the half-folded wings' drag, as a stooping bird checks its line
                    {
                        float vd = Mathf.Max(0f, -Vector3.Dot(v, up)), hgt = Mathf.Max(0f, above - h.pullUpHeight * s - drop);
                        float tRem = (-vd + Mathf.Sqrt(vd * vd + 2f * FlightModel.G * hgt)) / FlightModel.G;
                        Vector3 hDir = toH.sqrMagnitude > 1e-8f ? toH.normalized : fH;
                        float vh = Vector3.Dot(v, hDir), want = toH.magnitude / Mathf.Max(0.05f * T, tRem);
                        if (vh > want) m.Velocity -= hDir * Mathf.Min(vh - want, kBrakeG * FlightModel.G * dt);
                    }
                    fl.Chain.Level = 0f;   // the body dives along its path
                    fl.SetGoals(Vector3.Dot(mark, up), m.HorizontalSpeed, fl.TurnToward(guide), guide);
                    // the strike: the head stretches for the mark once it is within reach and a half (it lands as the
                    // swoop carries the body past)
                    aim?.Aim(mark, (mark - m.Position).magnitude < 2.5f * neckReach ? 1f : 0f);
                    Track(sim, up, ground);
                    // pull out: low enough over the target for the falling speed (opening wings take the fall out in
                    // about 0.06 s per unit of size: their grip and lift grow with the speed), or past it, or too long
                    float pull = h.pullUpHeight * s + drop;
                    Vector3 toMark = mark - m.Position; toMark -= up * Vector3.Dot(toMark, up);
                    // or struck: the beak's tip is on the mark (the neck carried ahead strikes down the dive's line, then
                    // the swoop takes it away - a mark left to pass under the body is beyond a forward neck's reach)
                    float hm = aim != null ? (aim.StrikePoint - mark).magnitude : float.MaxValue;
                    bool struck = hm < kHit * NeckLength(sim);
                    // the beak closing on the mark: the stoop holds on a little lower for the strike to land
                    bool closing = hm < kClosing * NeckLength(sim) && hm < lastHm;
                    lastHm = hm;
                    int why = struck ? 4 : above < pull ? 0 : Vector3.Dot(toMark, fH) < -1.5f * neckReach ? 1 : phaseTime > 8f * T ? 2 : -1;
                    if (why >= 0)
                    {
                        // overshot while still high: it breaks the stoop off (pulls out and circles again), not a stoop at the mark
                        if (why == 1 && above > 3f * pull) { Stoops--; Aborts++; why = 3; }
                        Current = Phase.PullUp; phaseTime = 0f; LastPull = why; PullAbove = above;
                    }
                    break;
                }
                case Phase.PullUp:
                {
                    UpdateMark(a, up, ground);
                    float ease = Mathf.Clamp01(phaseTime / (0.2f * T));
                    fl.DiveFold *= 1f - ease;
                    // the swoop: the opening wings' lift turns the falling path onto the level at kPullG g, keeping the
                    // speed (a stoop's speed carried out into the climb; the model's lift alone acts along up, not
                    // across the path, and dropped a slow dive onto the ground)
                    {
                        Vector3 vel = m.Velocity;
                        float spd = vel.magnitude, down = -Vector3.Dot(vel, up);
                        if (down > 0f && spd > 1e-3f && phaseTime < 2f * T)
                        {
                            Vector3 fH2 = vel - up * Vector3.Dot(vel, up);
                            fH2 = fH2.sqrMagnitude > 1e-8f ? fH2.normalized : m.Forward;
                            m.Velocity = Vector3.RotateTowards(vel, fH2 * spd, kPullG * FlightModel.G / spd * dt, 0f);
                        }
                    }
                    m.DemandCap = float.MaxValue;
                    fl.Chain.Level = Mathf.Lerp(0f, defaultLevel, ease);
                    float turn = CircleTurn(fl, a, up, T);
                    if (fl.WallTurn(out float wall)) turn = wall;
                    // climb away fast: hold the speed the stoop gave (no stall onto the ground), beating hard
                    fl.SetGoals(soar, Mathf.Max(cruise, m.ForwardSpeed), phaseTime < 0.6f * T ? 0f : turn);
                    bool striking = phaseTime < 0.8f * T && (mark - m.Position).magnitude < 1.6f * NeckReach(sim);
                    if (striking) aim?.Aim(mark, 1f); else aim?.Release();
                    Track(sim, up, ground);
                    if (phaseTime > 2.5f * T && Vector3.Dot(m.Velocity, up) > 0f)
                    {
                        LastMiss = miss; LastLowest = lowest;
                        if (LastPull != 3 && miss < 0.25f * NeckLength(sim) + 0.2f * s) Strikes++;   // the beak tip within about a beak length
                        fl.DiveFold = 0f; fl.Chain.Level = defaultLevel;
                        Current = Phase.Circle; phaseTime = 0f; prey = null;
                        stoopIn = h.stoopInterval * T * (0.6f + 0.8f * Rand());
                        aim?.Release();
                    }
                    break;
                }
            }
            return BrainOutput.Toward(head);
        }

        float NeckLength(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is LongNeckRuntime n) return n.Part.length * sim.SizeScale;
            return 0.5f * sim.SizeScale;
        }

        float NeckReach(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is LongNeckRuntime n) return (n.Part.length * n.Part.strikeReach + n.Part.tipReach) * sim.SizeScale;
            return 0.5f * sim.SizeScale;
        }

        void Track(CreatureSim sim, Vector3 up, float ground)
        {
            IHeadAim aim = Neck(sim);
            Vector3 hp = aim != null ? aim.StrikePoint : sim.Joints[0];
            miss = Mathf.Min(miss, (hp - mark).magnitude);
            lowest = Mathf.Min(lowest, Vector3.Dot(sim.Flight.Model.Position, up) - ground);
        }

        // a level circle round the area's middle that fits the safe disc
        float CircleTurn(CreatureFlight fl, in EnclosureArea a, Vector3 up, float T)
        {
            FlightModel m = fl.Model;
            float R = fl.TurnRadius;
            float rc = Mathf.Clamp(h.circleRadius * a.safeRadius, 1.2f * R, Mathf.Max(1.2f * R, a.safeRadius - 1.4f * R));
            Vector3 rel = m.Position - a.center; rel -= up * Vector3.Dot(rel, up);
            float side = Vector3.Dot(Vector3.Cross(up, rel), m.Forward) >= 0f ? 1f : -1f;
            float v = Mathf.Max(m.HorizontalSpeed, 1e-3f);
            return side * (v / rc + 0.3f / T * (rel.magnitude - rc) / rc);
        }

        // prey in the area, else (practice) a mark on the ground ahead where the stoop's line will reach it
        bool PickTarget(CreatureSim sim, in EnclosureArea a, Vector3 up, float ground)
        {
            if (HuntPrey.Nearest(sim, a, out CreatureSim p)) { prey = p; mark = preyPrev = p.Joints[0]; preyVel = Vector3.zero; return true; }
            prey = null;
            if (!h.practice) return false;
            // a practice mark near the middle (the circle's centre): the circling flyer turns in toward it and stoops when
            // its fall would carry it there - the whole stoop and its pull-out stay well inside the aviary
            Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized, t1 = Vector3.Cross(up, t0);
            float r0 = 0.2f * a.safeRadius * Mathf.Sqrt(Rand()), a0 = 2f * Mathf.PI * Rand();
            Vector3 p0 = a.center + (t0 * Mathf.Cos(a0) + t1 * Mathf.Sin(a0)) * r0;
            p0 += up * (ground - Vector3.Dot(p0, up));
            if (sim.Flight.ProbeDown(p0 + up * (2f * sim.SizeScale), out Vector3 g, out _, out _)) p0 = g;
            mark = p0 + up * (kMarkHeight * sim.SizeScale);   // a small animal's head height over the ground
            return true;
        }
        const float kMarkHeight = 0.12f;   // m at scale 1
        const float kPullG = 3f;          // the pull-out's turn, x g (size-free)
        const float kFallShare = 0.75f;     // the stoop's fall is slower than a free fall (the folding wings still lift): it starts at this share of the free-fall run
        const float kBrakeG = 1.2f;         // the stoop's line check, x g
        const float kPullShare = 0.25f;     // the share of that circle the pull-out really loses / runs on
        // the stoop aims the swoop's bottom this share of the neck's reach short of the mark: the mark lies ahead and
        // below there, inside the neck's strike cone (a mark passed under the body is out of the forward-carried neck's reach)
        const float kStrikeLead = 0.15f;
        const float kHit = 0.12f;           // a strike lands: the beak's tip this close to the mark, x the neck's length
        const float kClosing = 0.8f;        // the beak closing within this (x the neck's length): the pull-out waits ...
        const float kHoldShare = 0.75f;     // ... down to this share of its height

        // prey: its head, led by its own speed (smoothed) over the time the stoop still takes
        void UpdateMark(in EnclosureArea a, Vector3 up, float ground)
        {
            if (prey == null) return;
            Vector3 p = prey.Joints[0];
            if (preyStep > 0f) preyVel = Vector3.Lerp(preyVel, (p - preyPrev) / preyStep, 0.2f);
            preyPrev = p;
            float lead = Current == Phase.Stoop || Current == Phase.Stalk ? 1.2f * preyT : 0f;   // one lead through the stalk and the stoop (a jump at the stoop put it past the mark at once)
            Vector3 v = preyVel - up * Vector3.Dot(preyVel, up);
            mark = p + v * lead;
        }
        Vector3 preyPrev, preyVel; float preyStep, preyT = 1f;
    }
}
