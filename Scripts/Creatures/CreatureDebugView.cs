using UnityEngine;

// F3 debug overlay for every creature of the CreatureWorld on this object, in the video's colours:
// the spine chain (grey, head segment orange #FFB347), each joint's body ellipse in its joint frame (blue
// #5AA9E6), the feet's rest points (small crosses), shoulder-elbow-foot (green #6FBF7C) and the
// enclosure's safe disc. Lines are built into preallocated arrays (grown only when a creature is added)
// and drawn with GL.LINES in OnRenderObject, on top of the scene.
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
        for (int s = 0; s < sims.Count; s++) need += VerticesFor(sims[s]);
        CreatureSurfaces reg = world.SurfacesIfAny;
        if (reg != null) need += 24 * reg.Count;
        if (need > verts.Length)
        {
            int cap = Mathf.Max(need, 2 * verts.Length);
            verts = new Vector3[cap]; colors = new Color32[cap];
        }
        for (int s = 0; s < sims.Count; s++)
        {
            AddCreature(sims[s]);
            if (world.BrainOf(s) is ClimbingBrain cb && cb.Mode != ClimbingBrain.State.Wander)
            {
                float c = 0.1f * sims[s].SizeScale; Vector3 g = cb.Goal;
                Line(g - Vector3.right * c, g + Vector3.right * c, kGoal);
                Line(g - Vector3.up * c, g + Vector3.up * c, kGoal);
                Line(g - Vector3.forward * c, g + Vector3.forward * c, kGoal);
            }
        }
        if (reg != null) for (int i = 0; i < reg.Count; i++) Box(reg[i].bounds, kAabb);
        return count;
    }

    static int VerticesFor(CreatureSim sim)
    {
        int n = sim.Joints.Length, legs = sim.Legs.Length;
        return 2 * (n - 1) + 2 * kEllipseSegments * n + legs * (4 + 4) + (sim.Bounds.HasValue ? 2 * kDiscSegments : 0)
             + (sim.CanClimb ? 2 * n + 4 : 0) + 6;
    }

    void Line(Vector3 a, Vector3 b, Color32 c)
    {
        verts[count] = a; colors[count++] = c;
        verts[count] = b; colors[count++] = c;
    }

    void AddCreature(CreatureSim sim)
    {
        CreatureSpecies sp = sim.Species;
        Vector3[] j = sim.Joints;
        Quaternion[] r = sim.Rotations;
        float s = sim.SizeScale;
        for (int i = 1; i < j.Length; i++) Line(j[i - 1], j[i], i == 1 ? kHead : kChain);

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
            Line(st.shoulder, st.elbow, kLeg);
            Line(st.elbow, st.foot, kLeg);
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
