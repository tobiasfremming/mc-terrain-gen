using System.Collections.Generic;
using LSystems;
using UnityEngine;

// Grows one plant from a grammar and a profile, as real GameObjects.
//
// grammar text -> word -> LSkeleton -> primitives
//
// Only the last arrow is new. Everything before it is the existing L-system
// engine, which is why this file is short: LSkeleton already hands over nodes,
// segments and markers in world-ish space, and all that remains is deciding
// what to put there -- which is the profile's job, not this class's.
//
// EDIT-TIME LIVE. It rebuilds when the profile changes, when the grammar's
// SOURCE TEXT changes (polled, because editing another asset does not call
// this component's OnValidate), and when the transform is scaled. Type a rule
// into the grammar and the plant reshapes in the scene view.
[ExecuteAlways]
[DisallowMultipleComponent]
public class PlantBuilder : MonoBehaviour
{
    public PlantProfile profile;

    [Tooltip("Rebuild automatically in the editor. Off if a huge grammar makes typing sluggish; use the Rebuild context menu instead.")]
    public bool liveRebuild = true;

    [Tooltip("Overrides the profile's seed when >= 0, so several builders can share one profile and still differ.")]
    public int seedOverride = -1;

    [Header("Last build")]
    [SerializeField] int _segments;
    [SerializeField] int _parts;
    [SerializeField] int _wordModules;
    [Tooltip("Marker symbols the grammar produced that the profile has no row for. These are the knobs the grammar is offering you.")]
    [SerializeField] List<string> _unmappedSymbols = new List<string>();
    [SerializeField] string _status = "";

    const string kContainerName = "(generated)";

    Transform _container;
    readonly List<Transform> _pool = new List<Transform>();
    int _used;

    // Reused across rebuilds so typing in the grammar does not allocate a
    // skeleton and a rewriter per keystroke.
    LSystemRewriter _rewriter;
    LSkeleton _skeleton;

    readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

    bool _dirty = true;
    int _seenProfileVersion = -1;
    string _seenSource;
    int _seenIterations = int.MinValue;
    uint _seenSeed;

    void OnEnable() { _dirty = true; }
    void OnValidate() { _dirty = true; }        // deferred: mutating the hierarchy inside OnValidate is not allowed
    void OnDisable() { ReleaseMaterials(); }
    void OnDestroy() { ReleaseMaterials(); }

    void Update()
    {
        if (!liveRebuild && !_dirty) return;

        // Poll the things that change without notifying us: the grammar asset
        // is a separate object, so editing its text never reaches our
        // OnValidate.
        if (profile != null)
        {
            uint seed = seedOverride >= 0 ? (uint)seedOverride : profile.seed;
            string src = profile.grammar != null ? profile.grammar.source : null;
            if (profile.Version != _seenProfileVersion || src != _seenSource ||
                profile.iterations != _seenIterations || seed != _seenSeed)
            {
                _seenProfileVersion = profile.Version;
                _seenSource = src;
                _seenIterations = profile.iterations;
                _seenSeed = seed;
                _dirty = true;
            }
        }

        if (!_dirty) return;
        _dirty = false;
        Rebuild();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        EnsureContainer();
        _used = 0;
        _segments = 0;
        _parts = 0;
        _wordModules = 0;
        _unmappedSymbols.Clear();

        if (profile == null) { Finish("No profile assigned."); return; }
        if (profile.grammar == null) { Finish("Profile has no grammar."); return; }
        if (!profile.grammar.IsValid)
        {
            var errs = profile.grammar.Errors;
            Finish(errs != null && errs.Count > 0 ? "Grammar error: " + errs[0] : "Grammar does not parse.");
            return;
        }

        LSystemGrammar g = profile.grammar.Grammar;
        if (g == null) { Finish("Grammar did not parse."); return; }

        uint seed = seedOverride >= 0 ? (uint)seedOverride : profile.seed;
        int iters = profile.iterations >= 0 ? profile.iterations : profile.grammar.EffectiveIterations;

        _rewriter = _rewriter ?? new LSystemRewriter();
        LModuleString word = _rewriter.Rewrite(g, iters, seed);
        _wordModules = word.Count;
        _skeleton = TurtleInterpreter.Build(word, profile.turtle, _skeleton);

        BuildStems();
        BuildMarkers();

        // Anything the pool still holds from a bigger previous plant.
        for (int i = _used; i < _pool.Count; i++)
            if (_pool[i] != null) _pool[i].gameObject.SetActive(false);

        Finish(_rewriter.Truncated
            ? "Truncated at " + _rewriter.MaxModules + " modules -- lower iterations."
            : "OK");
    }

    [ContextMenu("Reroll seed")]
    public void RerollSeed()
    {
        seedOverride = Random.Range(0, int.MaxValue);
        _dirty = true;
        Rebuild();
    }

    void Finish(string status)
    {
        _status = status;
        if (status != "OK" && status != "") { /* left visible in the inspector */ }
    }

    void BuildStems()
    {
        if (profile.stemShape == PlantPartShape.None) return;

        var nodes = _skeleton.Nodes;
        var segs = _skeleton.Segments;
        for (int i = 0; i < segs.Count; i++)
        {
            LSkeletonSegment s = segs[i];
            Vector3 a = nodes[s.From].Position;
            Vector3 b = nodes[s.To].Position;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) continue;

            float ra = Mathf.Max(profile.minStemRadius, 0.5f * s.WidthFrom * profile.stemRadiusScale);
            float rb = Mathf.Max(profile.minStemRadius, 0.5f * s.WidthTo * profile.stemRadiusScale);
            float r = profile.taperStems ? 0.5f * (ra + rb) : ra;
            if (r < profile.hideStemsBelowRadius) continue;

            Transform t = Take(profile.stemShape, null, profile.stemColor);
            if (t == null) continue;

            t.localPosition = a + d * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            // Unity's Capsule and Cylinder primitives are 2 units tall, so
            // localScale.y is a HALF height. Sphere/Cube/Quad are 1 unit.
            bool halfHeight = profile.stemShape == PlantPartShape.Capsule || profile.stemShape == PlantPartShape.Cylinder;
            t.localScale = new Vector3(r * 2f, halfHeight ? len * 0.5f : len, r * 2f);
            _segments++;
        }
    }

    void BuildMarkers()
    {
        var markers = _skeleton.Markers;
        for (int i = 0; i < markers.Count; i++)
        {
            LSkeletonMarker mk = markers[i];

            if (!profile.TryGetPart(mk.Symbol, out PlantPart part))
            {
                string s = mk.Symbol.ToString();
                if (!_unmappedSymbols.Contains(s)) _unmappedSymbols.Add(s);
                continue;
            }
            if (part.shape == PlantPartShape.None) continue;

            float size = part.sizeMultiplier;
            switch (part.sizeFrom)
            {
                case PlantPartSizeSource.MarkerParam:
                    size *= _skeleton.GetMarkerParam(mk, part.paramIndex, 1f);
                    break;
                case PlantPartSizeSource.TurtleWidth:
                    size *= mk.Width;
                    break;
            }

            Transform t = Take(part.shape, part.prefab, part.color);
            if (t == null) continue;

            Quaternion rot = part.alignToHeading ? mk.Orientation : Quaternion.identity;
            t.localPosition = mk.Position + rot * part.localOffset;
            t.localRotation = rot * Quaternion.Euler(part.eulerOffset);
            t.localScale = part.scale * size;
            _parts++;
        }
    }

    // ---- pooled children -------------------------------------------------

    void EnsureContainer()
    {
        if (_container != null) return;

        Transform existing = transform.Find(kContainerName);
        if (existing != null)
        {
            _container = existing;
            _pool.Clear();
            for (int i = 0; i < _container.childCount; i++) _pool.Add(_container.GetChild(i));
            return;
        }

        var go = new GameObject(kContainerName);
        go.transform.SetParent(transform, false);
        _container = go.transform;
        MarkGenerated(go);
    }

    // The whole subtree is derived data, rebuilt from the grammar on load. Not
    // marking it cost this project 203 MB of serialized terrain chunks once
    // already; a plant regenerating hundreds of primitives per keystroke would
    // do the same to any scene it is saved in.
    static void MarkGenerated(GameObject go)
    {
        go.hideFlags = HideFlags.DontSaveInEditor;
    }

    Transform Take(PlantPartShape shape, GameObject prefab, Color color)
    {
        Transform t = _used < _pool.Count ? _pool[_used] : null;

        // A pooled object is only reusable if it is the same kind of thing.
        if (t != null && t.gameObject.name != KeyFor(shape, prefab))
        {
            DestroyImmediateSafe(t.gameObject);
            _pool[_used] = null;
            t = null;
        }

        if (t == null)
        {
            GameObject go = Create(shape, prefab);
            if (go == null) return null;
            go.name = KeyFor(shape, prefab);
            go.transform.SetParent(_container, false);
            MarkGenerated(go);
            t = go.transform;
            if (_used < _pool.Count) _pool[_used] = t; else _pool.Add(t);
        }

        t.gameObject.SetActive(true);
        ApplyColor(t.gameObject, color);
        _used++;
        return t;
    }

    static string KeyFor(PlantPartShape shape, GameObject prefab)
        => shape == PlantPartShape.Prefab ? "prefab:" + (prefab != null ? prefab.name : "none") : shape.ToString();

    GameObject Create(PlantPartShape shape, GameObject prefab)
    {
        if (shape == PlantPartShape.Prefab)
            return prefab != null ? Instantiate(prefab) : null;

        PrimitiveType type;
        switch (shape)
        {
            case PlantPartShape.Capsule:  type = PrimitiveType.Capsule; break;
            case PlantPartShape.Sphere:   type = PrimitiveType.Sphere; break;
            case PlantPartShape.Cube:     type = PrimitiveType.Cube; break;
            case PlantPartShape.Cylinder: type = PrimitiveType.Cylinder; break;
            case PlantPartShape.Quad:     type = PrimitiveType.Quad; break;
            default: return null;
        }

        GameObject go = GameObject.CreatePrimitive(type);
        // Primitives ship with a collider. A plant with a thousand of them is
        // a thousand colliders in the physics scene for no reason.
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediateSafe(col);
        return go;
    }

    void ApplyColor(GameObject go, Color color)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (r == null) return;
        r.sharedMaterial = MaterialFor(color);
    }

    // One material per distinct colour, shared by every part using it, and
    // never serialized. sharedMaterial (not material) so the editor does not
    // leak a clone per renderer per rebuild.
    Material MaterialFor(Color color)
    {
        if (_materials.TryGetValue(color, out Material m) && m != null) return m;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(shader) { name = "Plant " + ColorUtility.ToHtmlStringRGB(color), hideFlags = HideFlags.DontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        _materials[color] = m;
        return m;
    }

    void ReleaseMaterials()
    {
        foreach (var kv in _materials) if (kv.Value != null) DestroyImmediateSafe(kv.Value);
        _materials.Clear();
    }

    static void DestroyImmediateSafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
