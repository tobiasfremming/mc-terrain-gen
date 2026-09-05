using System.Collections.Generic;
using LSystems;
using UnityEngine;

// LSkeleton -> one Mesh.
//
// This is the step the L-system README always pointed at: "a generalized-
// cylinder builder is the obvious next consumer". PlantBuilder's one-primitive-
// per-segment path is fine for a lab and useless for a world -- FractalA at 6
// iterations is 51,934 GameObjects, each with a MeshFilter and a MeshRenderer,
// and that is one plant.
//
// What comes out is a single mesh with two submeshes: bark (swept tubes) and
// leaves (cards). Everything the mesh needs is already in the skeleton, which
// is the payoff for having kept LSkeleton as the pivot rather than letting the
// turtle emit geometry directly:
//
//     Node.Position / Node.Width   ->  ring centre and radius
//     Node.Parent                  ->  which ring connects to which
//     Node.Depth                   ->  wind stiffness, baked into UV2
//     Marker.Position/Orientation  ->  where a leaf card sits and which way
//
// RING TOPOLOGY. One ring per NODE, not per segment. A fork is a node with two
// children, and both children's tubes start from that one ring -- so a branch
// point is genuinely connected instead of two tubes overlapping. It also means
// vertex count is O(nodes), not O(segments * 2).
//
// FRAMES. Each ring is oriented by rotation-minimizing transport from its
// parent: take the parent's reference vector and rotate it by the minimal
// rotation carrying the parent's tangent onto this node's. Computing each frame
// independently instead (say, FromToRotation from world up) makes the tube
// twist between rings, which reads as a barber-pole once bark is on it.
public struct PlantMeshLod
{
    [Tooltip("Vertices around a branch. Uniform per LOD: rings only stitch cleanly to rings of the same size, and paying for a couple of extra verts on a twig is cheaper than the seam-fixing a varying count needs.")]
    public int ringSegments;
    [Tooltip("Absolute floor, metres. Kept as a backstop; keepFraction is the lever that actually scales.")]
    public float minRadius;
    // Tuned gently on purpose, and LOD1 especially. Unlike ringSegments, which
    // thins a branch's cross-section and barely reads, keepFraction deletes
    // whole branches -- it changes the SILHOUETTE. At 0.55 a tree shed 63% of
    // its bark triangles the instant it crossed lod1Distance, which looks like
    // the plant being rebuilt in front of you rather than a level of detail.
    // The distances in PlantWorld move the swap further out; these numbers make
    // the swap itself smaller. Both are needed: no amount of distance hides a
    // tree losing most of its branches in one frame.
    [Tooltip("Fraction of nodes to keep, thickest first. 1 keeps the whole skeleton. This is the main LOD lever: it is a quantile of THIS plant's own widths, so one number works on a 10 m tree and a 0.3 m herb alike. Deletes whole branches, so it changes the silhouette -- move it in small steps.")]
    [Range(0f, 1f)] public float keepFraction;
    [Tooltip("Fraction of leaf cards to keep, spread evenly. Leaves never shrank with LOD before, so they grew from 14% of a bramble at LOD0 to 40% of it at LOD2.")]
    [Range(0f, 1f)] public float leafKeep;
    public bool leaves;

    public static PlantMeshLod Lod0 => new PlantMeshLod { ringSegments = 6, minRadius = 0f,     keepFraction = 1f,    leafKeep = 1f,   leaves = true };
    public static PlantMeshLod Lod1 => new PlantMeshLod { ringSegments = 4, minRadius = 0.008f, keepFraction = 0.75f, leafKeep = 0.95f, leaves = true };
    public static PlantMeshLod Lod2 => new PlantMeshLod { ringSegments = 3, minRadius = 0.02f,  keepFraction = 0.35f, leafKeep = 0.8f, leaves = true };
}

public static class PlantMeshBuilder
{
    // Scratch, reused across builds so baking a hundred prototypes does not
    // allocate a hundred sets of arrays.
    static readonly List<Vector3> _verts = new List<Vector3>();
    static readonly List<Vector3> _normals = new List<Vector3>();
    static readonly List<Vector2> _uv = new List<Vector2>();
    static readonly List<Vector2> _uv2 = new List<Vector2>();
    static readonly List<Color32> _colors = new List<Color32>();
    static readonly List<int> _barkTris = new List<int>();
    static readonly List<int> _leafTris = new List<int>();

    static int[] _ringStart;      // per node: first vertex of its ring, or -1
    static Vector3[] _tangent;    // per node
    static Vector3[] _refDir;     // per node: the transported reference vector
    static float[] _alongLength;  // per node: distance from root, for bark V
    static bool[] _nodeUsed;
    static bool[] _nodeKept;      // per node: survives this LOD's pruning
    static float[] _radiiScratch; // sorted copy of node radii, for the quantile

    public static Mesh Build(LSkeleton skel, PlantProfile profile, PlantMeshLod lod, Mesh reuse = null)
    {
        Mesh mesh = reuse != null ? reuse : new Mesh();
        mesh.Clear();
        // A plant is derived data. Not marking it cost this project a 203 MB
        // scene once already.
        mesh.hideFlags = HideFlags.DontSaveInEditor;
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.name = "Plant";

        _verts.Clear(); _normals.Clear(); _uv.Clear(); _uv2.Clear(); _colors.Clear();
        _barkTris.Clear(); _leafTris.Clear();

        if (skel == null || profile == null || skel.Nodes.Count == 0) { mesh.subMeshCount = 0; return mesh; }

        int ring = Mathf.Clamp(lod.ringSegments, 3, 24);
        BuildFrames(skel, profile, lod, ring);
        BuildTubes(skel, profile, lod, ring);
        if (lod.leaves) BuildLeaves(skel, profile, lod);

        mesh.SetVertices(_verts);
        mesh.SetNormals(_normals);
        mesh.SetUVs(0, _uv);
        mesh.SetUVs(1, _uv2);
        mesh.SetColors(_colors);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(_barkTris, 0, true);
        mesh.SetTriangles(_leafTris, 1, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    static float NodeRadius(LSkeleton skel, PlantProfile p, int i)
        => Mathf.Max(p.minStemRadius, 0.5f * skel.Nodes[i].Width * p.stemRadiusScale);

    // Pass 1: which nodes actually carry geometry, their tangents, their
    // transported frames, and their distance from the root.
    static void BuildFrames(LSkeleton skel, PlantProfile profile, PlantMeshLod lod, int ring)
    {
        var nodes = skel.Nodes;
        var segs = skel.Segments;
        int n = nodes.Count;

        if (_ringStart == null || _ringStart.Length < n)
        {
            _ringStart = new int[n]; _tangent = new Vector3[n]; _refDir = new Vector3[n];
            _alongLength = new float[n]; _nodeUsed = new bool[n];
            _nodeKept = new bool[n]; _radiiScratch = new float[n];
        }
        for (int i = 0; i < n; i++) { _ringStart[i] = -1; _nodeUsed[i] = false; _alongLength[i] = 0f; }

        // WHICH NODES SURVIVE THIS LOD.
        //
        // minRadius alone did not work, and the baked assets said so plainly:
        // TreePlant kept all 1,922 of its rings at every LOD, and its bark went
        // 11,532 -> 7,688 -> 5,766 triangles purely from ringSegments 6:4:3. An
        // absolute metre threshold prunes a bramble twig and nothing at all on
        // a tree, whose thinnest branch is still thicker than 2 cm. A LOD2 at
        // 51% of LOD0 is not a LOD.
        //
        // keepFraction is a QUANTILE instead -- keep the thickest X% of nodes,
        // whatever this particular plant's widths happen to be. One number then
        // means the same thing on a 10 m tree and a 0.3 m herb, and the LOD
        // budget is predictable instead of a per-species guess.
        float cut = lod.minRadius;
        if (lod.keepFraction < 1f)
        {
            for (int i = 0; i < n; i++) _radiiScratch[i] = NodeRadius(skel, profile, i);
            System.Array.Sort(_radiiScratch, 0, n);
            int q = Mathf.Clamp(Mathf.FloorToInt((1f - Mathf.Clamp01(lod.keepFraction)) * n), 0, n - 1);
            cut = Mathf.Max(cut, _radiiScratch[q]);
        }

        // A node survives only if it is thick enough AND its parent survived, so
        // a pruned branch takes its whole subtree with it rather than leaving
        // the far end floating unattached. Parent index < child index, so the
        // same single forward pass below is enough. The root is always kept:
        // pruning it would delete the plant.
        for (int i = 0; i < n; i++)
        {
            int par = nodes[i].Parent;
            _nodeKept[i] = par < 0 || (_nodeKept[par] && NodeRadius(skel, profile, i) >= cut);
        }
        // Only nodes touched by a surviving segment get a ring. Emitting rings
        // for pruned twigs would leave orphan vertices in the buffer.
        for (int s = 0; s < segs.Count; s++)
        {
            var seg = segs[s];
            if (!_nodeKept[seg.From] || !_nodeKept[seg.To]) continue;
            _nodeUsed[seg.From] = true;
            _nodeUsed[seg.To] = true;
        }

        // Nodes are appended in creation order, and a branch's nodes always
        // follow their branch point, so parent index < child index. One
        // forward pass is enough; no recursion, no sorting.
        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            int parent = node.Parent;

            Vector3 tan;
            if (parent >= 0)
            {
                Vector3 d = node.Position - nodes[parent].Position;
                float len = d.magnitude;
                tan = len > 1e-6f ? d / len : _tangent[parent];
                _alongLength[i] = _alongLength[parent] + len;
            }
            else
            {
                // A root, or the start of a detached run after `f`. Point it at
                // whatever leaves it, so the first ring is not arbitrary.
                tan = Vector3.up;
                for (int s = 0; s < segs.Count; s++)
                    if (segs[s].From == i)
                    {
                        Vector3 d = nodes[segs[s].To].Position - node.Position;
                        if (d.sqrMagnitude > 1e-12f) { tan = d.normalized; }
                        break;
                    }
            }
            _tangent[i] = tan;

            if (parent >= 0)
            {
                // Rotation-minimizing transport: carry the parent's reference
                // vector through the smallest rotation that lines the parent's
                // tangent up with ours. This is what keeps the tube from
                // twisting along a branch.
                Quaternion swing = Quaternion.FromToRotation(_tangent[parent], tan);
                _refDir[i] = Vector3.ProjectOnPlane(swing * _refDir[parent], tan).normalized;
            }
            else
            {
                _refDir[i] = Vector3.ProjectOnPlane(
                    Mathf.Abs(Vector3.Dot(tan, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up, tan).normalized;
            }
            if (_refDir[i].sqrMagnitude < 1e-8f)
                _refDir[i] = Vector3.ProjectOnPlane(Vector3.right, tan).normalized;
        }

        // Pass 2: emit a ring per used node.
        Color32 bark = profile.stemColor;
        for (int i = 0; i < n; i++)
        {
            if (!_nodeUsed[i]) continue;
            _ringStart[i] = _verts.Count;

            Vector3 c = nodes[i].Position, t = _tangent[i], u = _refDir[i];
            Vector3 v = Vector3.Cross(t, u);
            float radius = NodeRadius(skel, profile, i);

            // Wind stiffness: 1 at the trunk, 0 at the tips. A vertex shader
            // multiplies its sway by this, so the trunk barely moves and twigs
            // whip. Depth is already exactly this quantity.
            float stiff = skel.MaxDepth > 0 ? 1f - Mathf.Clamp01(nodes[i].Depth / (float)skel.MaxDepth) : 1f;

            for (int k = 0; k < ring; k++)
            {
                float a = (k / (float)ring) * Mathf.PI * 2f;
                Vector3 dir = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                _verts.Add(c + dir * radius);
                _normals.Add(dir);
                _uv.Add(new Vector2(k / (float)ring, _alongLength[i]));
                _uv2.Add(new Vector2(stiff, _alongLength[i]));
                _colors.Add(bark);
            }
        }
    }

    static void BuildTubes(LSkeleton skel, PlantProfile profile, PlantMeshLod lod, int ring)
    {
        var segs = skel.Segments;
        for (int s = 0; s < segs.Count; s++)
        {
            var seg = segs[s];
            if (!_nodeKept[seg.From] || !_nodeKept[seg.To]) continue;

            int a = _ringStart[seg.From], b = _ringStart[seg.To];
            if (a < 0 || b < 0) continue;

            // Wound so the front face is the OUTSIDE. Getting this backwards is
            // not subtly wrong: backface culling then hides the surface you are
            // looking at and shows you the far interior wall instead, which
            // reads as a hollow tree. Vertex normals point along +dir (outward),
            // so the geometric normal from Cross(v1-v0, v2-v0) must agree.
            for (int k = 0; k < ring; k++)
            {
                int k2 = (k + 1) % ring;
                int a0 = a + k, a1 = a + k2, b0 = b + k, b1 = b + k2;
                _barkTris.Add(a0); _barkTris.Add(b1); _barkTris.Add(b0);
                _barkTris.Add(a0); _barkTris.Add(a1); _barkTris.Add(b1);
            }
        }
    }

    // Leaves are cards, two triangles each, laid out to match Unity's Quad
    // primitive (1x1 in local XY, normal -Z) so every eulerOffset already tuned
    // in a PlantProfile carries over unchanged from the primitive path.
    static void BuildLeaves(LSkeleton skel, PlantProfile profile, PlantMeshLod lod)
    {
        var markers = skel.Markers;
        // Leaves are NOT pruned along with the bark they hang on, deliberately.
        // The canopy IS the silhouette at distance, so culling the cards whose
        // twigs keepFraction removed would thin every tree at the LOD boundary
        // -- which is the pop that fadeBand was just added to kill. A card left
        // where its twig used to be still sits inside the canopy volume and
        // costs two triangles. Bark carries the cost reduction; leafKeep only
        // trims the species where cards dominate (a bramble at LOD2 was 40%
        // leaf by triangle, a tree only 3%).
        //
        // Error-diffusion stride rather than "every Nth": it keeps exactly
        // leafKeep of the cards and spreads them evenly, so thinning a canopy
        // does not carve a stripe out of it. Deterministic, which matters --
        // a prototype must bake identically every time.
        float acc = 0f;
        for (int i = 0; i < markers.Count; i++)
        {
            LSkeletonMarker mk = markers[i];
            if (!profile.TryGetPart(mk.Symbol, out PlantPart part)) continue;
            if (part.shape == PlantPartShape.None) continue;

            if (lod.leafKeep < 1f)
            {
                acc += lod.leafKeep;
                if (acc < 1f) continue;
                acc -= 1f;
            }

            float size = part.sizeMultiplier;
            switch (part.sizeFrom)
            {
                case PlantPartSizeSource.MarkerParam: size *= skel.GetMarkerParam(mk, part.paramIndex, 1f); break;
                case PlantPartSizeSource.TurtleWidth: size *= mk.Width; break;
            }
            Vector3 half = new Vector3(part.scale.x, part.scale.y, 1f) * size * 0.5f;
            if (half.x <= 1e-6f || half.y <= 1e-6f) continue;

            Quaternion rot = (part.alignToHeading ? mk.Orientation : Quaternion.identity) * Quaternion.Euler(part.eulerOffset);
            Vector3 c = mk.Position + (part.alignToHeading ? mk.Orientation : Quaternion.identity) * part.localOffset;
            Vector3 ex = rot * Vector3.right * half.x;
            Vector3 ey = rot * Vector3.up * half.y;
            Vector3 nrm = rot * Vector3.back;

            int v0 = _verts.Count;
            Color32 col = part.color;
            // Tips sway most; a leaf inherits the stiffness of the node it hangs on.
            float stiff = skel.MaxDepth > 0 ? 1f - Mathf.Clamp01(mk.Depth / (float)skel.MaxDepth) : 1f;

            _verts.Add(c - ex - ey); _verts.Add(c + ex - ey); _verts.Add(c + ex + ey); _verts.Add(c - ex + ey);
            for (int k = 0; k < 4; k++) { _normals.Add(nrm); _colors.Add(col); _uv2.Add(new Vector2(stiff, 0f)); }
            _uv.Add(new Vector2(0, 0)); _uv.Add(new Vector2(1, 0)); _uv.Add(new Vector2(1, 1)); _uv.Add(new Vector2(0, 1));

            // Same rule as the bark: winding must agree with the normal, which
            // is rot * back to match Unity's Quad primitive.
            _leafTris.Add(v0); _leafTris.Add(v0 + 2); _leafTris.Add(v0 + 1);
            _leafTris.Add(v0); _leafTris.Add(v0 + 3); _leafTris.Add(v0 + 2);
        }
    }
}
