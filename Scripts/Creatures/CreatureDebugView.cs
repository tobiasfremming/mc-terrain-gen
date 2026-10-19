using CreatureKit;
using UnityEngine;

// F3 debug overlay for every creature of the CreatureWorld on this object, in the video's colours:
// the spine chain (grey, head segment orange #FFB347), each joint's body ellipse in its joint frame (blue
// #5AA9E6), the feet's rest points (small crosses), shoulder-elbow-foot (green #6FBF7C) and the
// enclosure's safe disc. Lines are built into preallocated arrays (grown only when a creature is added)
// and drawn with GL.LINES in OnRenderObject, on top of the scene. The creature kit's part runtimes (eyes, antenna
// strands, new moving parts) draw themselves through ICreatureDebugDraw (counted first, then emitted).
[RequireComponent(typeof(CreatureWorld))]
public sealed class CreatureDebugView : MonoBehaviour
{
    public bool show;
    public KeyCode toggleKey = KeyCode.F3;

    const int kEllipseSegments = 12, kDiscSegments = 64;
    const float kCross = 0.02f;   // rest-point cross half size at size scale 1

    static readonly Color32 kChain = new Color32(0xB0, 0xB0, 0xB0, 0xFF);
    static readonly Color32 kHead = new Color32(0xFF, 0xB3, 0x47, 0xFF);
    static readonly Color32 kJoint = new Color32(0x5A, 0xA9, 0xE6, 0xFF);
    static readonly Color32 kLeg = new Color32(0x6F, 0xBF, 0x7C, 0xFF);
    static readonly Color32 kRest = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
    static readonly Color32 kDisc = new Color32(0xFF, 0xB3, 0x47, 0x90);
    // climbing: each joint's up (white), joints on a collider (magenta ellipse), the anticipation fan's forward
    // and wrap casts (yellow / cyan; red when Blocked), the climb goal (green cross), surface AABBs (grey)
    static readonly Color32 kUp = new Color32(0xFF, 0xFF, 0xFF, 0xC0);
    static readonly Color32 kOnSurface = new Color32(0xE0, 0x5A, 0xD0, 0xFF);
    static readonly Color32 kCast = new Color32(0xFF, 0xE0, 0x40, 0xFF);
    static readonly Color32 kWrap = new Color32(0x40, 0xE0, 0xFF, 0xFF);
    static readonly Color32 kBlocked = new Color32(0xFF, 0x30, 0x30, 0xFF);
    static readonly Color32 kGoal = new Color32(0x40, 0xFF, 0x60, 0xFF);
    static readonly Color32 kAabb = new Color32(0x90, 0x90, 0x90, 0x90);

    CreatureWorld world;
    Material material;
    Vector3[] verts = new Vector3[0];
    Color32[] colors = new Color32[0];
    int count;

    internal int VertexCount => count;

    void Awake() => world = GetComponent<CreatureWorld>();

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) show = !show;
    }

    void OnRenderObject()
    {
        if (!show) return;
        if (world == null) world = GetComponent<CreatureWorld>();
        int n = BuildLines();
        if (n == 0 || !EnsureMaterial()) return;
        material.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);
        GL.Begin(GL.LINES);
        for (int i = 0; i < n; i++) { GL.Color(colors[i]); GL.Vertex(verts[i]); }
        GL.End();
        GL.PopMatrix();
    }

    bool EnsureMaterial()
    {
        if (material != null) return true;
        Shader sh = Shader.Find("Hidden/Internal-Colored");
        if (sh == null) return false;
        material = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); // an overlay, as in the video
        return true;
    }

    // Vertex pairs (one line each) for every creature into verts/colors; returns the vertex count.
    internal int BuildLines()
    {
        count = 0;
        if (world == null) world = GetComponent<CreatureWorld>();
        if (world == null) return 0;
        var sims = world.Sims;
        int need = 0;
        for (int s = 0; s < sims.Count; s++) need += VerticesFor(sims[s]) + PartVertices(sims[s]);
        CreatureSurfaces reg = world.SurfacesIfAny;
        if (reg != null) need += 24 * reg.Count;
        var bodies = world.Bodies;
        for (int b = 0; b < bodies.Count; b++) if (bodies[b] is OctopusCreature) need += OctopusVertices;
        if (need > verts.Length)
        {
            int cap = Mathf.Max(need, 2 * verts.Length);
            verts = new Vector3[cap]; colors = new Color32[cap];
        }
        for (int s = 0; s < sims.Count; s++)
        {
            AddCreature(sims[s]);
            AddParts(sims[s]);
            if (world.BrainOf(s) is ClimbingBrain cb && cb.Mode != ClimbingBrain.State.Wander)
            {
                float c = 0.1f * sims[s].SizeScale; Vector3 g = cb.Goal;
                Line(g - Vector3.right * c, g + Vector3.right * c, kGoal);
                Line(g - Vector3.up * c, g + Vector3.up * c, kGoal);
                Line(g - Vector3.forward * c, g + Vector3.forward * c, kGoal);
            }
        }
        for (int b = 0; b < bodies.Count; b++) if (bodies[b] is OctopusCreature oc) AddOctopus(oc);
        if (reg != null) for (int i = 0; i < reg.Count; i++) Box(reg[i].bounds, kAabb);
        return count;
    }

    // An octopus (F3): its intent - the goal (green cross), the planned route over the branches from the holds it
    // hangs from (yellow; done waypoints grey), the waypoint it reaches for now (magenta cross) - and the swing:
    // the pivot (cyan cross) with a line to the body, each hold line root -> hold (green: the group it hangs
    // from, orange: the catching group, grey: others).
    static readonly Color32 kRoute = new Color32(0xFF, 0xE0, 0x40, 0xFF), kRouteDone = new Color32(0x80, 0x80, 0x80, 0xC0);
    static readonly Color32 kWaypoint = new Color32(0xFF, 0x40, 0xE0, 0xFF), kPivot = new Color32(0x40, 0xE0, 0xFF, 0xFF);
    static readonly Color32 kCatch = new Color32(0xFF, 0x9A, 0x30, 0xFF);
    static readonly Color32 kArc = new Color32(0xF0, 0xF0, 0xF0, 0xFF);
    const int OctopusVertices = 2 * (3 + 3 + 3 + 1 + BranchRoute.MaxWaypoints + 1 + 16 + 4 + 7);

    void Cross(Vector3 p, float r, Color32 c)
    {
        Line(p - Vector3.right * r, p + Vector3.right * r, c);
        Line(p - Vector3.up * r, p + Vector3.up * r, c);
        Line(p - Vector3.forward * r, p + Vector3.forward * r, c);
    }

    void AddOctopus(OctopusCreature oc)
    {
        OctopusController c = oc.Controller;
        float r = 0.15f * oc.SizeScale;
        Vector3 head = c.Body.HeadCentre;
        bool travel = ArmCoordinator.Travelling(c.Intent.mode);
        if (travel) Cross(c.Brain != null ? c.Brain.Goal : c.Intent.goal, r, kGoal);
        ArmBlackboard b = c.Board;
        BranchRoute route = c.Route;
        if (route.Valid)
        {
            Vector3 prev = b.PivotGroup >= 0 ? b.SwingCentre : head;
            for (int i = 0; i < route.Count; i++)
            {
                Line(prev, route.Waypoints[i], i < route.Current ? kRouteDone : kRoute);
                prev = route.Waypoints[i];
            }
            Line(prev, route.Goal, kRoute);
            if (!route.Done) Cross(route.Next, 1.5f * r, kWaypoint);
        }
        SwingSequencer q = c.Sequencer;
        if (q != null)
        {
            // the planned body (the swinger): each rope pivot -> the crown (cyan), the next hold (magenta), the predicted
            // arc to the forward apex (white) and the apex (white cross), where the last hold was let go (grey) and caught (orange)
            PlannedSwing sw = q.Swing;
            for (int i = 0; i < PlannedSwing.MaxHolds; i++)
                if (sw.Active[i]) { Cross(sw.Pivot[i], r, kPivot); Line(sw.Pivot[i], sw.Position, kPivot); }
            if (q.HasNext) Cross(q.NextHold, 1.5f * r, kWaypoint);
            if (q.Mode == SwingSequencer.Step.Pump || q.Mode == SwingSequencer.Step.Reach)
            {
                for (int i = 1; i < q.ArcCount; i++) Line(q.Arc[i - 1], q.Arc[i], kArc);
                if (q.ArcCount > 0) Cross(q.PredictedApex, r, kArc);
            }
            if (q.Transfers > 0) { Cross(q.LastRelease, 0.6f * r, kRouteDone); Cross(q.LastCatch, 0.6f * r, kCatch); }
            // the hunt: the head -> the spider it hunts (green: reachable, orange: its last check found it out of reach),
            // and where the last spider it gave up was when it gave up (grey cross; the lab readout says why)
            OctopusBrain br = c.Brain;
            if (br != null)
            {
                if (br.Target != null && (br.Mode == OctopusBrain.State.Stalk || br.Mode == OctopusBrain.State.Feed || br.Mode == OctopusBrain.State.Eat))
                {
                    Vector3 t = br.Target.Position;
                    Line(head, t, br.TargetReachable ? kGoal : kCatch);
                    Cross(t, 0.7f * r, br.TargetReachable ? kGoal : kCatch);
                }
                if (br.GiveUps > 0) Cross(br.LastGiveUpAt, 0.7f * r, kRouteDone);
            }
        }
        else if (b.PivotGroup >= 0)
        {
            Cross(b.SwingCentre, r, kPivot);
            Line(b.SwingCentre, head, kPivot);
        }
        for (int a = 0; a < b.Arms && a < 8; a++)
        {
            if (!b.Anchored[a]) continue;
            int g = q != null ? (q.Use[a] == SwingSequencer.ArmUse.Hold ? b.PivotGroup : 1) : c.Coordinator.GroupOf(a);
            Color32 col = g < 0 ? kChain : g == b.PivotGroup ? kGoal : kCatch;
            Line(c.Body.Arms[a].X[0], b.Anchor[a], col);
        }
        // mouth tentacles feeding (the swinger): the beak to the prey they reach for or eat (magenta)
        MouthFeeder fd = c.Feeder;
        if (fd != null && fd.Feeding && fd.Prey != null)
        {
            Vector3 p = fd.Prey.Position;
            Line(fd.Centre, p, kWaypoint);
            Cross(p, 0.5f * r, kWaypoint);
        }
    }

    static int VerticesFor(CreatureSim sim)
    {
        int n = sim.Joints.Length, legs = sim.Legs.Length;
        return 2 * (n - 1) + 2 * kEllipseSegments * n + legs * (4 + 4) + (sim.Bounds.HasValue ? 2 * kDiscSegments : 0)
             + (sim.CanClimb ? 2 * n + 4 : 0) + 6
             + (sim.Flight != null ? 2 * sim.Flight.Wings.Count * 8 + 2 * n + 12 : 0);
    }

    // The part runtimes' own lines (ICreatureDebugDraw): counted, then emitted, without allocating.
    sealed class PartLines : ICreatureDebugDraw
    {
        public CreatureDebugView view;
        public bool counting;
        public int counted;
        public void Line(Vector3 a, Vector3 b, Color c) { if (counting) counted += 2; else view.Line(a, b, c); }
        public void Point(Vector3 p, float r, Color c)
        {
            if (counting) { counted += 6; return; }
            view.Line(p - Vector3.right * r, p + Vector3.right * r, c);
            view.Line(p - Vector3.up * r, p + Vector3.up * r, c);
            view.Line(p - Vector3.forward * r, p + Vector3.forward * r, c);
        }
    }
    readonly PartLines partLines = new PartLines();

    int PartVertices(CreatureSim sim)
    {
        partLines.view = this; partLines.counting = true; partLines.counted = 0;
        IPartRuntime[] rs = sim.PartRuntimes;
        for (int i = 0; i < rs.Length; i++) rs[i].DrawDebug(partLines);
        return partLines.counted;
    }

    void AddParts(CreatureSim sim)
    {
        partLines.view = this; partLines.counting = false;
        IPartRuntime[] rs = sim.PartRuntimes;
        for (int i = 0; i < rs.Length; i++) rs[i].DrawDebug(partLines);
    }

    void Line(Vector3 a, Vector3 b, Color32 c)
    {
        // VerticesFor is an estimate (arthropod segments, wings, climb goals, surfaces grow it); never trust it to
        // be exact - grow instead of overrunning. Debug view only, and the buffer keeps its size once grown.
        if (count + 2 > verts.Length)
        {
            int cap = Mathf.Max(64, 2 * verts.Length);
            System.Array.Resize(ref verts, cap); System.Array.Resize(ref colors, cap);
        }
        verts[count] = a; colors[count++] = c;
        verts[count] = b; colors[count++] = c;
    }

    static readonly Color32 kBand = new Color32(0x9C, 0xE6, 0x5A, 0xFF), kWrist = new Color32(0xFF, 0x66, 0xCC, 0xFF);
    static readonly Color32 kAir = new Color32(0x66, 0xD9, 0xFF, 0xFF), kLand = new Color32(0xFF, 0xE0, 0x40, 0xFF);

    // A flyer (F3): each wing's comfort band (green, vertical through the rest wrist), its wrist (magenta
    // cross), the air weight per joint (cyan, its length), the landing point and the approach's entry (yellow).
    void AddFlight(CreatureSim sim)
    {
        CreatureFlight fl = sim.Flight;
        float s = sim.SizeScale;
        CreatureWings w = fl.Wings;
        for (int k = 0; k < w.Count; k++)
        {
            WingState st = w.State[k];
            WingSpec ws = w.Spec(k);
            Quaternion gr = sim.Rotations[fl.Girdle];
            Vector3 up = gr * Vector3.up;
            Vector3 restWrist = sim.Joints[fl.Girdle] + gr * new Vector3(ws.restTip.x * w.Side(k), ws.restTip.y, ws.restTip.z) * s;
            Line(restWrist - up * (ws.comfortDown * s), restWrist + up * (ws.comfortUp * s), kBand);
            float c = 0.03f * s;
            Line(restWrist - gr * Vector3.forward * c, restWrist + gr * Vector3.forward * c, kBand);
            Line(st.wrist - up * c, st.wrist + up * c, kWrist);
            Line(st.wrist - gr * Vector3.right * c, st.wrist + gr * Vector3.right * c, kWrist);
            Line(st.wrist - gr * Vector3.forward * c, st.wrist + gr * Vector3.forward * c, kWrist);
        }
        for (int i = 0; i < sim.Joints.Length; i++)
            if (fl.AirWeight[i] > 0f) Line(sim.Joints[i], sim.Joints[i] + (sim.Rotations[i] * Vector3.up) * (0.15f * s * fl.AirWeight[i]), kAir);
        if (fl.Landing)
        {
            Vector3 p = fl.LandPoint, e = fl.EntryPoint; float c = 0.2f * s;
            Line(p - Vector3.right * c, p + Vector3.right * c, kLand);
            Line(p - Vector3.forward * c, p + Vector3.forward * c, kLand);
            Line(p, p + Vector3.up * (4f * c), kLand);
            Line(fl.Model.Position, p, kLand);
            Line(e - Vector3.up * c, e + Vector3.up * c, kLand);
        }
    }

    void AddCreature(CreatureSim sim)
    {
        CreatureSpecies sp = sim.Species;
        Vector3[] j = sim.Joints;
        Quaternion[] r = sim.Rotations;
        float s = sim.SizeScale;
        for (int i = 1; i < j.Length; i++) Line(j[i - 1], j[i], i == 1 ? kHead : kChain);
        if (sim.Flight != null) AddFlight(sim);

        for (int i = 0; i < j.Length; i++)
        {
            Vector3 right = r[i] * Vector3.right, up = r[i] * Vector3.up;
            float a = sp.joints[i].halfWidth * s, b = sp.joints[i].halfHeight * s;
            Color32 jc = sim.Source[i] != 0 ? kOnSurface : kJoint;
            Vector3 prev = j[i] + right * a;
            for (int k = 1; k <= kEllipseSegments; k++)
            {
                float t = k * 2f * Mathf.PI / kEllipseSegments;
                Vector3 p = j[i] + right * (a * Mathf.Cos(t)) + up * (b * Mathf.Sin(t));
                Line(prev, p, jc);
                prev = p;
            }
            if (sim.CanClimb) Line(j[i], j[i] + sim.Up[i] * (2f * b), kUp);
        }

        float c = kCross * s;
        for (int l = 0; l < sim.Legs.Length; l++)
        {
            LegState st = sim.Legs[l];
            LegSpec ls = sim.Species.legs[l];
            if (ls.IsArthropod)
            {
                // every segment of an arthropod leg (coxa base .. foot)
                Vector3[] pts = sim.LegPoints;
                int o = sim.LegPointOffset(l);
                for (int k = 0; k < ls.segments.Length; k++) Line(pts[o + k], pts[o + k + 1], kLeg);
            }
            else
            {
                Line(st.shoulder, st.elbow, kLeg);
                Line(st.elbow, st.ankle, kLeg); Line(st.ankle, st.foot, kLeg);
            }
            Vector3 q = sim.LegRestPoint(l);
            Line(q - Vector3.right * c, q + Vector3.right * c, kRest);
            Line(q - Vector3.forward * c, q + Vector3.forward * c, kRest);
        }

        CreatureClimb cl = sim.Climb;
        if (cl != null && cl.Active)
        {
            Line(cl.CastFrom, cl.CastTo, cl.Blocked ? kBlocked : kCast);
            Line(cl.WrapFrom, cl.WrapTo, kWrap);
        }

        if (sim.Bounds.HasValue)
        {
            EnclosureArea area = sim.Bounds.Value;
            Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 t1 = Vector3.Cross(area.up, t0);
            // the disc at the creature's height so it reads on dune slopes near the animal
            Vector3 centre = area.center + area.up * Vector3.Dot(j[0] - area.center, area.up);
            Vector3 prev = centre + t0 * area.safeRadius;
            for (int k = 1; k <= kDiscSegments; k++)
            {
                float t = k * 2f * Mathf.PI / kDiscSegments;
                Vector3 p = centre + (t0 * Mathf.Cos(t) + t1 * Mathf.Sin(t)) * area.safeRadius;
                Line(prev, p, kDisc);
                prev = p;
            }
        }
    }

    void Box(Bounds b, Color32 c)
    {
        Vector3 lo = b.min, hi = b.max;
        for (int e = 0; e < 4; e++)
        {
            float x0 = (e & 1) == 0 ? lo.x : hi.x, z0 = (e & 2) == 0 ? lo.z : hi.z;
            Line(new Vector3(x0, lo.y, z0), new Vector3(x0, hi.y, z0), c);                 // verticals
            Line(new Vector3(lo.x, (e & 1) == 0 ? lo.y : hi.y, (e & 2) == 0 ? lo.z : hi.z),
                 new Vector3(hi.x, (e & 1) == 0 ? lo.y : hi.y, (e & 2) == 0 ? lo.z : hi.z), c); // along x
            Line(new Vector3((e & 1) == 0 ? lo.x : hi.x, (e & 2) == 0 ? lo.y : hi.y, lo.z),
                 new Vector3((e & 1) == 0 ? lo.x : hi.x, (e & 2) == 0 ? lo.y : hi.y, hi.z), c); // along z
        }
    }

    void OnDestroy()
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
    }
}
