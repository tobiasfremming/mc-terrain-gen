// Creature kit (2026-10-20, the Delver): Burrow - a digger's postures and its trips underground.
//
// Activities (requested every tick by a behaviour - Kit/DiggerBehaviours.cs - and dropped when no one asks):
//   Scratch   halted; the head lowered (sniffDeg), the digging limbs (DigLeg.digs) scoop at the ground in turn
//   Rest      halted and crouched (restCrouch); `curl` 1 tucks the head down under the hood, 0 lies flat in the sun
// A trip (BeginTrip(goal)), planned through the enclosure's TunnelNetwork: walk to an existing hole when the network
// gets it nearer its goal for less than digging would cost (digCost per metre dug), else dig in where it stands:
//   ToHole    walk on the surface to the entry hole
//   DigIn     halted; the forelimbs scoop, the head goes down (digSeconds; slipSeconds into an existing hole)
//   Dive      walking again: each joint sinks as it passes the entry point (head first, over diveLength, to `depth`)
//   Under     out of sight (IConcealment): the sim walks the tunnel's line on the surface above it; new tunnel is
//             carved as the head goes (CreatureDigging requests every carveStep: real density edits, inside the
//             enclosure only), existing tunnel is ridden without carving
//   Lunge     halted at the exit: the head and neck burst up out of the ground pitched up, CreatureSim.Display -> 1
//             (a Maw gapes wide), the exit crater carved (a new hole)
//   Rise      walking out: each joint rises as it passes the exit point; the display fades
// The trip's new tunnel and holes join the network (reused by every individual of the enclosure afterwards).
// The postures are drawn, not simulated: the sim walks on the surface all along (its gait, containment and feet are
// untouched - the generic kit checks hold); this part, the LAST one, moves every bone with its joint (the spine's
// own, the limbs' by their attach joint, other parts' by theirs) down by the joint's sink and pitches it to the sink's
// slope. At rest weight 0 it writes nothing (bit-exact pass-through). Froude: lengths x s, times x sqrt s.
// Allocation-free after the first step (the enclosure's network is fetched then); main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public enum DigActivity { None = 0, Scratch = 1, Rest = 2 }

    [Serializable]
    [Part("Burrow", PartKind.Rigid, Bones = "0 (poses the others' bones)", Summary = "A digger's postures and trips underground: scratch and sniff, rest curled or flat; dig in (forelimbs scooping, head first), travel hidden along a real carved tunnel (or ride the enclosure's existing tunnels), burst out jaws first (CreatureSim.Display) and climb out. Shared TunnelNetwork per enclosure; carves through CreatureDigging, inside the enclosure only.",
          System = "BurrowRuntime (part runtime, stage Late; the LAST part: moves the other parts' bones) + CreatureSim Halt / Crouch / Display + CreatureDigging / TunnelNetwork", KitOnly = true,
          UseWhen = "burrowers: moles, armadillos, sandworms, burrowing lizards and amphisbaenians, anything that digs in and bursts out")]
    public sealed class Burrow : CreaturePart
    {
        [Param(ParamUnit.Metres, "how far the body sinks under the surface (every joint, at the full dive)", Example = "delver 0.6")] public float depth = 0.3f;
        [Param(ParamUnit.Metres, "the dive's (and the climb-out's) run along the path: a joint sinks over this much travel past the hole")] public float diveLength = 0.6f;
        [Param(ParamUnit.Seconds, "scooping before the dive into new ground")] public float digSeconds = 2.5f;
        [Param(ParamUnit.Seconds, "the pause before slipping into an existing hole")] public float slipSeconds = 0.8f;
        [Param(ParamUnit.Seconds, "the burst: the head rises out of the ground")] public float lungeSeconds = 0.45f;
        [Param(ParamUnit.Seconds, "held agape after the burst")] public float roarSeconds = 1.2f;
        [Param(ParamUnit.Count, "joints the burst lifts (head first, fading)", Min = 1, Max = 16, Default = "6")] public int lungeJoints = 6;
        [Param(ParamUnit.Metres, "how high above its walking height the head bursts")] public float lungeLift = 0.15f;
        [Param(ParamUnit.Degrees, "the head's pitch up at the burst", Min = 0, Max = 80, Default = "40")] public float lungePitchDeg = 40f;
        [Param(ParamUnit.Metres, "tunnel radius (the carve spheres')")] public float tunnelRadius = 0.2f;
        [Param(ParamUnit.Fraction, "a carve every this x the radius of the head's travel underground", Min = 0.3f, Max = 2f, Default = "0.7")] public float carveStep = 0.7f;
        [Param(ParamUnit.Flag, "carve into the terrain's physical layer (colliders and creatures feel the holes); off = rendered only")] public bool carvePhysics = true;
        [Param(ParamUnit.Metres, "a trip starts or ends at an existing hole within this distance")] public float holeSnap = 0.6f;
        [Param(ParamUnit.Fraction, "planning: a metre dug costs this many metres walked or ridden (> 1 favours existing tunnels)", Min = 1, Max = 20, Default = "4")] public float digCost = 4f;
        [Param(ParamUnit.Degrees, "the digging limbs' scoop stroke", Min = 0, Max = 90, Default = "45")] public float scoopDeg = 45f;
        [Param(ParamUnit.Hz, "scoop strokes a second")] public float scoopHz = 1.6f;
        [Param(ParamUnit.Degrees, "the head lowered to sniff and dig", Min = 0, Max = 60, Default = "22")] public float sniffDeg = 22f;
        [Param(ParamUnit.Metres, "crouch at rest")] public float restCrouch = 0.08f;
        [Param(ParamUnit.Degrees, "the head tucked down under the hood at a curled rest", Min = 0, Max = 70, Default = "35")] public float restCurlDeg = 35f;
        [Param(ParamUnit.Seconds, "postures blend in and out over this")] public float blendSeconds = 0.6f;
        [Param(ParamUnit.Seconds, "out of sight at least this long: a trip that reaches its exit sooner tunnels on (turning off the wall) until then", Example = "delver 8")] public float minUnderSeconds = 6f;
        [Param(ParamUnit.Count, "clods of earth flung by the digging and the burst (one bone each; 0 = none)", Min = 0, Max = 48)] public int debris;
        [Param(ParamUnit.Metres, "a clod's size")] public float debrisSize = 0.03f;
        [Param(ParamUnit.Count, "material layer of the clods (0 the body's skin: the palette's sandy colours)", Min = 0, Max = 15)] public int debrisLayer;
        [Param(ParamUnit.Joint, "a rigid carapace: joints from here to carapaceTo dive and climb as one (their sinks on a line), so the plates on them never turn against each other; -1 = none", Min = -1)] public int carapaceFrom = -1;
        [Param(ParamUnit.Joint, "the carapace's last joint")] public int carapaceTo = -1;
        [Param(ParamUnit.Fraction, "out of sight (Under) once this share of the joints, head first, is at the full depth (a tail tip may still show)", Min = 0.3f, Max = 1, Default = "0.8")] public float hiddenShare = 0.8f;

        public override int BoneCount(in SpeciesCtx c) => debris;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: Burrow needs a SpineBody";
            if (c.IndexOf(this) != c.Parts.Count - 1) return $"{id}: Burrow must be the last part (it moves the other parts' bones after they wrote them)";
            for (int i = 0; i < c.Parts.Count; i++) if ((c.Parts[i] is Burrow || c.Parts[i] is Tuck) && !ReferenceEquals(c.Parts[i], this)) return $"{id}: only one posture part (Burrow / Tuck)";
            if (!(depth > 0f) || !(diveLength > 0f) || !(tunnelRadius > 0f)) return $"{id}: depth, diveLength, tunnelRadius > 0";
            if (!(digSeconds > 0f) || !(slipSeconds > 0f) || !(lungeSeconds > 0f) || roarSeconds < 0f || !(blendSeconds > 0f)) return $"{id}: digSeconds, slipSeconds, lungeSeconds, blendSeconds > 0; roarSeconds >= 0";
            if (lungeJoints < 1 || lungeJoints > 16 || lungeLift < 0f || lungePitchDeg < 0f || lungePitchDeg > 80f) return $"{id}: lungeJoints 1..16, lungeLift >= 0, lungePitchDeg 0..80";
            if (carveStep < 0.3f || carveStep > 2f || holeSnap < 0f || digCost < 1f) return $"{id}: carveStep 0.3..2, holeSnap >= 0, digCost >= 1";
            if (scoopDeg < 0f || !(scoopHz > 0f) || sniffDeg < 0f || restCrouch < 0f || restCurlDeg < 0f) return $"{id}: scoopDeg, sniffDeg, restCrouch, restCurlDeg >= 0; scoopHz > 0";
            if (carapaceFrom >= 0 && (carapaceTo <= carapaceFrom || carapaceTo >= c.Joints)) return $"{id}: carapace joints {carapaceFrom}..{carapaceTo} must lie in the spine, first < last";
            if (hiddenShare < 0.3f || hiddenShare > 1f) return $"{id}: hiddenShare 0.3..1";
            if (minUnderSeconds < 0f) return $"{id}: minUnderSeconds >= 0";
            if (debris < 0 || debris > 48 || (debris > 0 && !(debrisSize > 0f))) return $"{id}: debris 0..48 with debrisSize > 0";
            if (debrisLayer < 0 || debrisLayer >= PartLayers.MaxParts || (debrisLayer >= 4 && debrisLayer <= 7)) return $"{id}: debrisLayer must be a spine-shader part id";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new BurrowRuntime(this, c.Sim, c.Species.Layout.Start(c.Index)) : null;

        // The clods: small lumpy ellipsoids, one rigid to each of this part's bones (parked inside the body at rest).
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (debris <= 0 || m.Rest == null) return;
            int b0 = m.Species.Layout.Start(c.Index);
            for (int k = 0; k < debris; k++)
            {
                float a = debrisSize * (0.7f + 0.6f * BurrowRuntime.Hash01(k * 3 + 1)), b = debrisSize * (0.6f + 0.5f * BurrowRuntime.Hash01(k * 3 + 2));
                float d = debrisSize * (0.7f + 0.6f * BurrowRuntime.Hash01(k * 3 + 3));
                m.Ellipsoid(m.Bones[b0 + k], Vector3.zero, a, b, d, 6, 4, debrisLayer, b0 + k);
            }
        }
    }

    public sealed class BurrowRuntime : IPartRuntime, IConcealment
    {
        public enum Phase { Idle = 0, ToHole = 1, DigIn = 2, Dive = 3, Under = 4, Lunge = 5, Rise = 6 }

        const int kMaxWay = 128, kMaxCarve = 256;
        const float kEps = 1e-4f;

        readonly Burrow p;
        readonly CreatureSim sim;
        readonly float L, T;
        readonly int n, headK, pivot, hiddenJoint;
        readonly float[] arc, sink, pitch;
        readonly int[] boneJoint;            // every bone's joint (-1: not moved)
        readonly int[] digBones;             // first bone of each digging limb (3 bones each)
        readonly int[] digSide, digJoint;
        readonly Matrix4x4[] jointM;
        readonly Vector3[] way = new Vector3[kMaxWay];
        readonly Vector3[] carved = new Vector3[kMaxCarve];

        TunnelNetwork net;
        EnclosureArea area;
        bool hasArea;

        // the trip
        int wayCount, wayAt, newFrom, entryHole, carvedCount, startHole;
        bool entryNew, exitNew;
        Vector3 entry, exit, outTarget, lastHead, lastCarve, beyond;
        bool beyondSet;
        float underClock;
        bool ownsDisplay;
        float odo, odoIn, odoOut, clock, stuck;
        // postures
        DigActivity request, activity;
        float curlReq, curl, wScratch, wRest, wLunge, wHead, display;
        float scoopClock;

        public Phase State { get; private set; }
        public bool TripActive => State != Phase.Idle;
        public bool Concealed => State == Phase.Under;
        public bool Away => State != Phase.Idle;
        public int Trips { get; private set; }
        public int TripsReusing { get; private set; }
        public float Depth => p.depth * L;
        public Burrow Part => p;
        public TunnelNetwork Network => net;
        public Vector3 Entry => entry;
        public Vector3 Exit => exit;
        public bool EntryWasNew => entryNew;
        public bool ExitWasNew => exitNew;
        public DigActivity Activity => activity;
        public float Sink(int joint) => sink[joint];
        public float Pitch(int joint) => pitch[joint];
        // where the behaviour should steer now (the sim's own head while halted)
        public Vector3 Target { get; private set; }

        // the clods
        readonly int debrisBone;
        readonly Vector3[] clodPos, clodVel;
        readonly float[] clodLife, clodGround, clodSpin;
        int nextClod;
        float flingClock;

        internal static float Hash01(int k)
        {
            uint x = (uint)k * 0x9E3779B1u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        public int ClodsFlying { get { int c = 0; for (int i = 0; i < clodLife.Length; i++) if (clodLife[i] > 0f) c++; return c; } }
        public float ClodHeight(int i) => clodLife[i] > 0f ? Vector3.Dot(clodPos[i], Up) - clodGround[i] : 0f;
        public int Clods => clodLife.Length;

        public BurrowRuntime(Burrow burrow, CreatureSim sim, int firstBone = -1)
        {
            debrisBone = firstBone;
            int nd = firstBone >= 0 ? burrow.debris : 0;
            clodPos = new Vector3[nd]; clodVel = new Vector3[nd]; clodLife = new float[nd]; clodGround = new float[nd]; clodSpin = new float[nd];
            p = burrow; this.sim = sim; L = sim.SizeScale; T = sim.TimeScale;
            CreatureSpecies sp = sim.Species;
            n = sp.JointCount;
            arc = new float[n]; sink = new float[n]; pitch = new float[n]; jointM = new Matrix4x4[n];
            for (int j = 0; j < n; j++) arc[j] = j * sp.spacing * L;
            headK = sp.RigidHead >= 2 ? sp.RigidHead : 1;
            hiddenJoint = Mathf.Clamp(Mathf.CeilToInt(burrow.hiddenShare * (n - 1)), 0, n - 1);
            pivot = Mathf.Min(n - 1, Mathf.Max(headK, 1));
            // bones -> joints
            boneJoint = new int[sp.BoneCount];
            for (int b = 0; b < boneJoint.Length; b++) boneJoint[b] = -1;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            BoneRange[] ranges = sp.Layout.Ranges;
            var dig = new List<int>(); var dside = new List<int>(); var djoint = new List<int>();
            int[] ordinal = KitView.Ordinals(parts);
            foreach (BoneRange r in ranges)
            {
                CreaturePart q = r.Part >= 0 && r.Part < parts.Count ? parts[r.Part] : null;
                for (int k = 0; k < r.Count; k++)
                {
                    int b = r.Start + k;
                    if (b >= boneJoint.Length) continue;
                    if (q is Burrow) continue;   // the clods fly on their own
                    if (r.Kind == PartKind.Body) boneJoint[b] = Mathf.Min(k, n - 1);
                    else if (q != null) boneJoint[b] = Mathf.Clamp(q is LimbPart ? sp.legs[ordinal[r.Part]].attachJoint : q.at.joint, 0, n - 1);
                }
                if (q is DigLeg d && d.digs) { dig.Add(sp.LegBone(ordinal[r.Part], 0)); dside.Add(sp.legs[ordinal[r.Part]].side); djoint.Add(sp.legs[ordinal[r.Part]].attachJoint); }
            }
            // eyes (a Sense block) ride their joint
            digBones = dig.ToArray(); digSide = dside.ToArray(); digJoint = djoint.ToArray();
            State = Phase.Idle;
            lastHead = sim.Joints[0];
            Target = sim.Joints[0];
        }

        public PartStage Stage => PartStage.Late;

        // ------------------------------------------------------------ requests

        // Asked every tick by a behaviour; dropped after the next step when no one asks again.
        public void Request(DigActivity a, float curl01 = 0f) { request = a; curlReq = Mathf.Clamp01(curl01); }

        Vector3 Up => sim.Up != null && sim.Up.Length > 0 ? sim.Up[0] : Vector3.up;

        static float Planar(Vector3 a, Vector3 b, Vector3 up) { Vector3 d = a - b; d -= up * Vector3.Dot(d, up); return d.magnitude; }

        void EnsureNetwork()
        {
            if (net != null || !sim.Bounds.HasValue) return;
            area = sim.Bounds.Value;
            if (area.up.sqrMagnitude < 0.5f) area.up = Vector3.up;
            hasArea = true;
            net = TunnelNetwork.For(area);
        }

        // Plans and starts a trip to `goal` (a surface point inside the enclosure). False while one is under way.
        public bool BeginTrip(Vector3 goal)
        {
            if (State != Phase.Idle) return false;
            EnsureNetwork();
            Vector3 up = Up, head = sim.Joints[0];
            float bodyLen = sim.Species.BodyLength * L;
            float snap = p.holeSnap * L, dc = p.digCost;
            // a: dig from here straight to the goal
            float best = dc * Planar(head, goal, up);
            int bestIn = -1, bestOut = -1;
            bool bestExtend = false;
            if (net != null && net.Holes > 1)
                for (int i = 0; i < net.Holes; i++)
                {
                    float walkIn = Planar(head, net.Hole(i), up);
                    net.Paths(i);
                    for (int k = 0; k < net.Holes; k++)
                    {
                        if (k == i || float.IsPositiveInfinity(net.PathLength(k))) continue;
                        float tail = Planar(net.Hole(k), goal, up);
                        bool extend = tail > 2f * snap + 0.5f * bodyLen;
                        float c = walkIn + net.PathLength(k) + (extend ? dc * tail : tail);
                        if (c < best) { best = c; bestIn = i; bestOut = k; bestExtend = extend; }
                    }
                }
            wayCount = 0; wayAt = 0; carvedCount = 0;
            if (bestIn >= 0)
            {
                net.Paths(bestIn);
                wayCount = net.PathPoints(bestOut, way);
                newFrom = wayCount;   // the network part is ridden, not carved
                if (bestExtend && wayCount < kMaxWay) way[wayCount++] = goal;
                entry = net.Hole(bestIn); entryNew = false; entryHole = bestIn;
                startHole = bestExtend ? bestOut : -1;
                exit = way[wayCount - 1]; exitNew = bestExtend;
                TripsReusing++;
                State = Phase.ToHole;
            }
            else
            {
                way[0] = goal; wayCount = 1; newFrom = 0;
                entry = head; entryNew = true; entryHole = -1; startHole = -1;
                exit = goal; exitNew = true;
                State = Phase.DigIn;
            }
            Trips++;
            clock = 0f; stuck = 0f; beyondSet = false; underClock = 0f;
            return true;
        }

        // ------------------------------------------------------------ the step

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        // the dive's profile: a half cosine (its curvature peaks at pi^2/2 = 4.9, a smoothstep's at 6: neighbouring plates
        // turn less against each other)
        static float Ramp(float x) { x = Mathf.Clamp01(x); return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * x); }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            EnsureNetwork();
            if (hasArea) CreatureGroup.Join(sim, area);
            float h = dt / T;   // Froude time
            Vector3 up = Up, head = sim.Joints[0];
            float travel = Planar(head, lastHead, up);
            lastHead = head;
            odo += travel;
            clock += h;
            scoopClock += h;
            float bodyLen = sim.Species.BodyLength * L, arrive = 0.2f * bodyLen;

            // activities (only on the surface, between trips)
            activity = State == Phase.Idle ? request : DigActivity.None;
            request = DigActivity.None;
            float blend = h / p.blendSeconds;
            wScratch = Mathf.MoveTowards(wScratch, activity == DigActivity.Scratch ? 1f : 0f, blend);
            wRest = Mathf.MoveTowards(wRest, activity == DigActivity.Rest ? 1f : 0f, blend);
            if (activity == DigActivity.Rest) curl = Mathf.MoveTowards(curl, curlReq, blend);

            switch (State)
            {
                case Phase.Idle:
                    Target = head;
                    break;
                case Phase.ToHole:
                    Target = entry;
                    if (Planar(head, entry, up) < arrive || clock > 40f) { State = Phase.DigIn; clock = 0f; }
                    break;
                case Phase.DigIn:
                    Target = head;
                    // earth thrown back over the shoulders while it scoops (new ground)
                    flingClock += h;
                    if (entryNew && flingClock >= 0.22f) { flingClock = 0f; Fling(head, up, false, 2); }
                    if (clock >= (entryNew ? p.digSeconds : p.slipSeconds) - kEps)
                    {
                        State = Phase.Dive; clock = 0f; odoIn = odo; entry = head; lastCarve = head; stuck = 0f;
                        if (entryNew && net != null && hasArea) entryHole = net.AddHole(Ground(head), p.holeSnap * L, up);
                    }
                    break;
                case Phase.Dive:
                case Phase.Under:
                    // follow the waypoints; the last one is the exit
                    while (wayAt < wayCount - 1 && Planar(head, way[wayAt], up) < arrive) wayAt++;
                    Target = way[Mathf.Min(wayAt, wayCount - 1)];
                    // under once the tail is down too (every joint at the full depth)
                    if (State == Phase.Dive && odo - odoIn >= p.diveLength * L + arc[hiddenJoint]) State = Phase.Under;
                    stuck = travel > 0.02f * dt * L ? 0f : stuck + h;
                    bool there = wayAt >= wayCount - 1 && (Planar(head, exit, up) < arrive || stuck > 6f);
                    if (State == Phase.Under) underClock += h;
                    // a trip shorter than the dive plus the body (or than minUnderSeconds under): keep tunnelling ahead
                    bool early = State == Phase.Dive || underClock < p.minUnderSeconds - kEps;
                    if (there && early)
                    {
                        if (!beyondSet) { beyond = hasArea ? ClampToArea(head + sim.Heading * bodyLen, bodyLen) : head + sim.Heading * bodyLen; beyondSet = true; }
                        Target = beyond;
                        if (Planar(head, beyond, up) < arrive) beyond = hasArea ? ClampToArea(head + Quaternion.AngleAxis(60f, up) * sim.Heading * bodyLen, bodyLen) : head + sim.Heading * bodyLen;
                    }
                    if (there && !early)
                    {
                        State = Phase.Lunge; clock = 0f; exit = head;
                    }
                    if (wayAt >= newFrom) CarveAlong(head, up, false);
                    break;
                case Phase.Lunge:
                    Target = head;
                    if (clock < kEps + h)
                    {
                        if (exitNew) Carve(Ground(head) - up * (0.25f * p.tunnelRadius * L), 1.15f * p.tunnelRadius * L);
                        Fling(head, up, true, Mathf.Max(1, clodLife.Length * 2 / 3));   // the burst: earth flies round the head
                    }
                    if (clock >= p.lungeSeconds + p.roarSeconds - kEps)
                    {
                        State = Phase.Rise; clock = 0f; odoOut = odo;
                        outTarget = head + sim.Heading * (1.5f * bodyLen);
                        if (hasArea) outTarget = ClampToArea(outTarget, bodyLen);
                    }
                    break;
                case Phase.Rise:
                    Target = outTarget;
                    stuck = travel > 0.02f * dt * L ? 0f : stuck + h;
                    if (odo - odoOut > p.diveLength * L + arc[n - 1] || stuck > 8f) Finish(up);
                    break;
            }

            StepClods(dt, up);
            // postures into the sim's inputs
            sim.Halt = State == Phase.DigIn || State == Phase.Lunge || activity != DigActivity.None || wScratch > 0.05f || wRest > 0.05f;
            sim.Crouch = Ease(wRest) * p.restCrouch * L;
            float lungeT = State == Phase.Lunge ? Ease(clock / p.lungeSeconds) : State == Phase.Rise ? 1f : 0f;
            wLunge = lungeT;
            display = State == Phase.Lunge ? lungeT : State == Phase.Rise ? Mathf.Max(0f, 1f - clock / Mathf.Max(0.1f, 0.8f)) : Mathf.MoveTowards(display, wScratch > 0.5f ? 0.15f : 0f, h);
            // the display channel is shared (a ThreatDisplay, a test): drive it only while a pose wants it, release it once
            if (State != Phase.Idle || display > 0f) { sim.Display = display; ownsDisplay = true; }
            else if (ownsDisplay) { sim.Display = 0f; ownsDisplay = false; }
            Sinks();
        }

        // n clods from the ground at the head: thrown back and up (digging) or up and out all round (the burst). Froude:
        // speeds x sqrt s, gravity size-free.
        void Fling(Vector3 head, Vector3 up, bool burst, int count)
        {
            if (clodLife.Length == 0) return;
            Vector3 g = Ground(head);
            Vector3 fwd = sim.Heading, side = Vector3.Cross(up, fwd);
            float v = Mathf.Sqrt(L);
            for (int c = 0; c < count; c++)
            {
                int i = nextClod; nextClod = (nextClod + 1) % clodLife.Length;
                int k = Trips * 131 + i * 17 + (int)(clock * 60f) * 7 + sim.Seed * 3;
                float a = Hash01(k), b = Hash01(k + 1), e = Hash01(k + 2);
                Vector3 dir;
                if (burst)
                {
                    float ang = 2f * Mathf.PI * a;
                    dir = (up * (2.2f + 1.5f * b) + (fwd * Mathf.Cos(ang) + side * Mathf.Sin(ang)) * (0.8f + 0.8f * e)).normalized;
                    clodPos[i] = g + up * (0.15f * L) + fwd * (0.1f * L);
                    clodVel[i] = dir * v * (2.4f + 1.4f * b);
                }
                else
                {
                    float s = (a - 0.5f) * 1.6f;
                    dir = (-fwd * 1.2f + up * (1f + 0.6f * b) + side * s).normalized;
                    clodPos[i] = g + fwd * (0.25f * sim.Species.BodyLength * L) + up * (0.05f * L) + side * (s * 0.15f * L);
                    clodVel[i] = dir * v * (1.6f + 1.1f * e);
                }
                clodGround[i] = Vector3.Dot(g, up);
                clodLife[i] = 3.5f;
                clodSpin[i] = 360f * (b - 0.5f) * 3f;
            }
        }

        void StepClods(float dt, Vector3 up)
        {
            for (int i = 0; i < clodLife.Length; i++)
            {
                if (clodLife[i] <= 0f) continue;
                float hgt = Vector3.Dot(clodPos[i], up) - clodGround[i];
                if (hgt > 0f || Vector3.Dot(clodVel[i], up) > 0f)
                {
                    clodVel[i] -= up * (9.81f * dt);
                    clodPos[i] += clodVel[i] * dt;
                }
                else { clodVel[i] = Vector3.zero; clodPos[i] -= up * hgt; }   // landed: it lies there, then crumbles
                clodLife[i] -= dt / T;
                if (clodLife[i] <= 0f) clodLife[i] = 0f;
            }
        }

        Vector3 ClampToArea(Vector3 p0, float bodyLen)
        {
            Vector3 d = p0 - area.center; Vector3 upA = area.up;
            float y = Vector3.Dot(d, upA); Vector3 pl = d - upA * y;
            float r = Mathf.Max(0f, area.safeRadius - 0.6f * bodyLen);
            if (pl.magnitude > r) pl = pl.normalized * r;
            return area.center + pl + upA * y;
        }

        Vector3 Ground(Vector3 headJoint) => headJoint - Up * (sim.Species.joints[0].clearance * L);

        void Carve(Vector3 c, float r)
        {
            if (!hasArea) return;
            CreatureDigging.Request(area, c, r, p.carvePhysics);
        }

        // New tunnel: a carve every carveStep x radius of the head's travel, centred on the drawn head (its sink).
        void CarveAlong(Vector3 head, Vector3 up, bool force)
        {
            float r = p.tunnelRadius * L;
            if (!force && Planar(head, lastCarve, up) < p.carveStep * r) return;
            lastCarve = head;
            Vector3 c = head - up * (sink[0] + 0.1f * r);
            if (carvedCount < kMaxCarve) carved[carvedCount++] = c;
            Carve(c, r);
        }

        void Finish(Vector3 up)
        {
            if (net != null && hasArea)
            {
                int exitHole = exitNew ? net.AddHole(Ground(exit), p.holeSnap * L, up) : net.NearestHole(exit, up, out _);
                int from = startHole >= 0 ? startHole : entryHole;
                if (carvedCount > 0 && from >= 0 && exitHole >= 0 && from != exitHole) net.AddTunnel(from, exitHole, carved, carvedCount);
            }
            State = Phase.Idle; clock = 0f; carvedCount = 0;
        }

        // ------------------------------------------------------------ the pose

        // Each joint's sink below its walking height and its pitch (nose down +) from the sink's slope; the burst lifts
        // the head joints and pitches them up.
        void Sinks()
        {
            float D = p.depth * L, R = Mathf.Max(1e-4f, p.diveLength * L), lift = p.lungeLift * L;
            bool tripPose = State >= Phase.Dive;
            for (int j = 0; j < n; j++)
            {
                if (!tripPose) { sink[j] = 0f; continue; }
                float sIn = Ramp((odo - arc[j] - odoIn) / R);
                float sOut = State == Phase.Rise ? Ramp((odo - arc[j] - odoOut) / R) : 0f;
                float s = D * Mathf.Max(0f, sIn - sOut);
                if (State == Phase.Lunge || State == Phase.Rise)
                {
                    float share = j >= p.lungeJoints ? 0f : 0.5f * (1f + Mathf.Cos(Mathf.PI * j / p.lungeJoints));
                    // the burst's lift settles over the first half of the climb out (the head comes down as the shell rises)
                    float settle = State == Phase.Rise ? Ramp((odo - arc[j] - odoOut) / (0.5f * R)) : 0f;
                    s -= (D + lift) * wLunge * share * (1f - Mathf.Max(sOut, settle)) * sIn;
                }
                sink[j] = s;
            }
            // a rigid carapace: its joints' sinks on their least-squares line (it tips as one plate)
            int c0 = p.carapaceFrom, c1 = p.carapaceTo;
            if (tripPose && c0 >= 0 && c1 > c0 && c1 < n)
            {
                float sx = 0f, sy = 0f, sxx = 0f, sxy = 0f; int m = c1 - c0 + 1;
                for (int j = c0; j <= c1; j++) { sx += arc[j]; sy += sink[j]; sxx += arc[j] * arc[j]; sxy += arc[j] * sink[j]; }
                float den = m * sxx - sx * sx;
                float k = Mathf.Abs(den) > 1e-12f ? (m * sxy - sx * sy) / den : 0f, b0 = (sy - k * sx) / m;
                for (int j = c0; j <= c1; j++) sink[j] = b0 + k * arc[j];
            }
            float sp = sim.Species.spacing * L;
            // the slope over two joints either side: neighbouring plates turn by a fraction of the curvature
            // the burst lifts only the first lungeJoints: the joints behind keep their own slope (the lifted neck does not
            // tip the shell's first band against its neighbour)
            int lungeEnd = wLunge > 0f ? Mathf.Min(n - 1, p.lungeJoints) : 0;
            for (int j = 0; j < n; j++)
            {
                int ja = Mathf.Max(j >= lungeEnd ? lungeEnd : 0, j - 2), jb = Mathf.Min(n - 1, j + 2);
                pitch[j] = Mathf.Atan2(sink[ja] - sink[jb], Mathf.Max(1, jb - ja) * sp) * Mathf.Rad2Deg;
            }
            if (c0 >= 0 && c1 > c0 && c1 < n)
            {
                float slope = Mathf.Atan2(sink[c0] - sink[c1], arc[c1] - arc[c0]) * Mathf.Rad2Deg;
                for (int j = c0; j <= c1; j++) pitch[j] = slope;
            }
            // the rigid head pitches as one (its pivot joint's slope) plus the poses: down to sniff / dig / curl, up at
            // the burst
            float headDeg = pitch[Mathf.Min(n - 1, headK)];
            float sniff = p.sniffDeg * Mathf.Max(wScratch, State == Phase.DigIn ? Ease(clock / 0.5f) : 0f);
            float rest = p.restCurlDeg * Ease(wRest) * curl;
            float burst = -p.lungePitchDeg * wLunge * (State == Phase.Rise ? 1f - Ramp((odo - odoOut) / (0.5f * R)) : 1f);
            wHead = sniff + rest + burst;
            for (int j = 0; j < headK && j < n; j++) pitch[j] = headDeg + wHead;
        }

        bool Posed => State >= Phase.DigIn || wScratch > 0f || wRest > 0f;

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            WriteClods(dst, offset);
            if (!Posed) return;   // walking: the other parts' bones exactly as they wrote them
            Vector3[] J = sim.Joints;
            Quaternion[] R = sim.Rotations;
            Vector3 up = Up;
            for (int j = 0; j < n; j++)
            {
                Vector3 right = R[j] * Vector3.right;
                Vector3 pj = J[j];
                // the head group turns about its pivot joint; others about themselves
                Quaternion q = Quaternion.AngleAxis(pitch[j], right);
                Vector3 pivotPt = j < headK ? J[pivot] : pj;
                Vector3 moved = pivotPt + q * (pj - pivotPt) - up * sink[j];
                // M = translate so the joint lands at `moved`, rotated by q about the joint
                jointM[j] = Matrix4x4.TRS(moved, q, Vector3.one) * Matrix4x4.TRS(-pj, Quaternion.identity, Vector3.one);
            }
            for (int b = 0; b < boneJoint.Length; b++)
            {
                int j = boneJoint[b];
                if (j < 0) continue;
                dst[offset + b] = jointM[j] * dst[offset + b];
            }
            // the digging limbs scoop (digging in, scratching): each turns about its shoulder, the sides in turn
            float scoop = Mathf.Max(wScratch, State == Phase.DigIn ? Ease(clock / 0.4f) * Ease((DigLeft()) / 0.4f) : 0f);
            if (scoop <= 0f || digBones.Length == 0) return;
            for (int k = 0; k < digBones.Length; k++)
            {
                float ph = 2f * Mathf.PI * p.scoopHz * scoopClock + (digSide[k] < 0 ? 0f : Mathf.PI);
                // reach forward (-) then pull back (+): a stroke from -scoopDeg to +0.35 scoopDeg
                float ang = scoop * p.scoopDeg * (-0.33f + 0.67f * Mathf.Sin(ph));
                Matrix4x4 sh = dst[offset + digBones[k]];
                Vector3 shoulder = sh.MultiplyPoint3x4(Vector3.zero);
                Vector3 right = R[digJoint[k]] * Vector3.right;
                Matrix4x4 rot = Matrix4x4.TRS(shoulder, Quaternion.AngleAxis(ang, right), Vector3.one) * Matrix4x4.TRS(-shoulder, Quaternion.identity, Vector3.one);
                for (int s = 0; s < 3; s++) dst[offset + digBones[k] + s] = rot * dst[offset + digBones[k] + s];
            }
        }

        // flying or lying clods at their place (spinning, crumbling away over their last second); idle ones parked, tiny,
        // inside the body at a third of its length
        void WriteClods(Matrix4x4[] dst, int offset)
        {
            if (clodLife.Length == 0) return;
            Vector3 park = sim.Joints[n / 3];
            for (int i = 0; i < clodLife.Length; i++)
            {
                float life = clodLife[i];
                if (life <= 0f) { dst[offset + debrisBone + i] = Matrix4x4.TRS(park, Quaternion.identity, new Vector3(0.02f * L, 0.02f * L, 0.02f * L)); continue; }
                float sc = L * Mathf.Clamp01(life / 1f);
                Quaternion q = Quaternion.AngleAxis(clodSpin[i] * (3.5f - life), new Vector3(0.6f, 0.3f, 0.74f));
                dst[offset + debrisBone + i] = Matrix4x4.TRS(clodPos[i], q, new Vector3(sc, sc, sc));
            }
        }

        float DigLeft() => (entryNew ? p.digSeconds : p.slipSeconds) - clock;

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float pad = 2f * p.debrisSize * L;
            for (int i = 0; i < clodLife.Length; i++)
                if (clodLife[i] > 0f) { lo = Vector3.Min(lo, clodPos[i] - new Vector3(pad, pad, pad)); hi = Vector3.Max(hi, clodPos[i] + new Vector3(pad, pad, pad)); }
            if (!Posed) return;
            float D = p.depth * L, lift = (p.lungeLift + 0.3f) * L;
            Vector3 up = Up;
            lo = Vector3.Min(lo, lo - up * D); hi = Vector3.Max(hi, hi + up * lift);
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            if (State == Phase.Idle) return;
            d.Point(entry, 0.1f * L, new Color(1f, 0.9f, 0.1f, 1f));
            d.Point(exit, 0.1f * L, new Color(1f, 0.2f, 0.1f, 1f));
            for (int i = 1; i < wayCount; i++) d.Line(way[i - 1], way[i], new Color(0.1f, 0.9f, 1f, 1f));
        }
    }
}
