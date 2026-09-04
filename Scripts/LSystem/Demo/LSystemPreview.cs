using System.Text;
using UnityEngine;

namespace LSystems
{
    // Drop this on an empty GameObject to see a grammar. Draws the skeleton
    // with gizmos in the scene view, rebuilding whenever anything in the
    // inspector changes -- which is the actual point: an L-system is unusable
    // until you can turn an angle by two degrees and immediately see it.
    //
    // Deliberately a preview and not a renderer. It produces no mesh, no
    // GameObjects and no allocations at runtime beyond the skeleton itself,
    // because the next consumer down (a plant mesher, an SDF carver) is what
    // should own that decision.
    [AddComponentMenu("Marching Cubes/L-System Preview")]
    [ExecuteAlways]
    public class LSystemPreview : MonoBehaviour
    {
        [Tooltip("Grammar asset to run. Leave empty to use the inline source below.")]
        public LSystemGrammarAsset grammar;

        [Tooltip("Used only when no grammar asset is assigned. Handy for scratch experiments.")]
        [TextArea(6, 24)]
        public string inlineSource = "#axiom: A\nA -> F [ +A ] [ -A ]\n";

        [Tooltip("-1 uses the grammar's own #iterations, or the asset's iterations field.")]
        [Range(-1, 16)] public int iterations = -1;

        [Tooltip("Changes the outcome of stochastic rules. Deterministic: the same seed always gives the same structure.")]
        public int seed = 0;

        public TurtleSettings turtle = TurtleSettings.Default;

        [Header("Display")]
        public bool drawSegments = true;
        public bool drawMarkers = true;
        public bool drawNodes = false;
        [Tooltip("Segment colour at the base of the structure.")]
        public Color baseColor = new Color(0.45f, 0.31f, 0.18f);
        [Tooltip("Segment colour at the deepest branch level.")]
        public Color tipColor = new Color(0.42f, 0.72f, 0.28f);
        public Color markerColor = new Color(0.95f, 0.80f, 0.25f);
        [Tooltip("Marker gizmo size, as a multiple of the marker's turtle width.")]
        public float markerScale = 1.5f;

        LSkeleton _skeleton;
        LSystemGrammarAsset _inlineAsset;      // wraps inlineSource so both paths share one code path
        readonly LSystemRewriter _rewriter = new LSystemRewriter();
        bool _dirty = true;
        string _status = "";

        // What the last build produced, for the inspector and the context menu.
        public string Status { get { Rebuild(false); return _status; } }
        public LSkeleton Skeleton { get { Rebuild(false); return _skeleton; } }

        void OnValidate()
        {
            if (_inlineAsset != null) _inlineAsset.Invalidate();
            _dirty = true;
        }

        void OnEnable() => _dirty = true;

        [ContextMenu("Rebuild")]
        void ForceRebuild()
        {
            if (grammar != null) grammar.Invalidate();
            if (_inlineAsset != null) _inlineAsset.Invalidate();
            _dirty = true;
            Rebuild(true);
            Debug.Log(_status, this);
        }

        [ContextMenu("Log word")]
        void LogWord()
        {
            LSystemGrammarAsset asset = ResolveAsset();
            if (asset == null || asset.Grammar == null) { Debug.LogWarning("No parsable grammar.", this); return; }
            int n = iterations >= 0 ? iterations : asset.EffectiveIterations;
            LModuleString word = _rewriter.Rewrite(asset.Grammar, n, unchecked((uint)seed));
            // Truncated so a 200k-module word cannot take the console with it.
            string text = word.ToDisplayString();
            if (text.Length > 8000) text = text.Substring(0, 8000) + " ... (" + word.Count + " modules)";
            Debug.Log(text, this);
            _dirty = true;                     // the shared rewriter buffer just moved
        }

        void Rebuild(bool force)
        {
            if (!_dirty && !force && _skeleton != null) return;
            _dirty = false;

            LSystemGrammarAsset asset = ResolveAsset();
            if (asset == null || asset.Grammar == null)
            {
                _skeleton = null;
                _status = Describe(asset);
                return;
            }

            int n = iterations >= 0 ? iterations : asset.EffectiveIterations;
            LModuleString word = _rewriter.Rewrite(asset.Grammar, n, unchecked((uint)seed));
            _skeleton = TurtleInterpreter.Build(word, turtle, _skeleton);

            var sb = new StringBuilder();
            sb.Append(word.Count).Append(" modules -> ")
              .Append(_skeleton.Segments.Count).Append(" segments, ")
              .Append(_skeleton.Markers.Count).Append(" markers, depth ")
              .Append(_skeleton.MaxDepth);
            if (_rewriter.Truncated)
                sb.Append("  [TRUNCATED at ").Append(_rewriter.CompletedIterations)
                  .Append('/').Append(n).Append(" iterations -- raise LSystemRewriter.MaxModules or lower iterations]");
            _status = sb.ToString();
        }

        string Describe(LSystemGrammarAsset asset)
        {
            if (asset == null) return "No grammar.";
            var sb = new StringBuilder("Grammar has errors:");
            var errors = asset.Errors;
            for (int i = 0; i < errors.Count && i < 10; i++) sb.Append('\n').Append(errors[i]);
            if (errors.Count > 10) sb.Append("\n... and ").Append(errors.Count - 10).Append(" more");
            return sb.ToString();
        }

        LSystemGrammarAsset ResolveAsset()
        {
            if (grammar != null) return grammar;
            if (string.IsNullOrWhiteSpace(inlineSource)) return null;

            // A throwaway in-memory asset, not saved anywhere: it exists only
            // so the inline path gets the same parse caching and error
            // reporting as a real asset instead of a second implementation.
            if (_inlineAsset == null)
            {
                _inlineAsset = ScriptableObject.CreateInstance<LSystemGrammarAsset>();
                _inlineAsset.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_inlineAsset.source != inlineSource)
            {
                _inlineAsset.source = inlineSource;
                _inlineAsset.Invalidate();
            }
            return _inlineAsset;
        }

        void OnDestroy()
        {
            if (_inlineAsset == null) return;
            if (Application.isPlaying) Destroy(_inlineAsset);
            else DestroyImmediate(_inlineAsset);
            _inlineAsset = null;
        }

        void OnDrawGizmos()
        {
            Rebuild(false);
            if (_skeleton == null) return;

            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            float depthScale = _skeleton.MaxDepth > 0 ? 1f / _skeleton.MaxDepth : 0f;

            if (drawSegments)
            {
                var segments = _skeleton.Segments;
                var nodes = _skeleton.Nodes;
                for (int i = 0; i < segments.Count; i++)
                {
                    LSkeletonSegment s = segments[i];
                    Gizmos.color = Color.Lerp(baseColor, tipColor, s.Depth * depthScale);
                    Gizmos.DrawLine(nodes[s.From].Position, nodes[s.To].Position);
                }
            }

            if (drawNodes)
            {
                var nodes = _skeleton.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    Gizmos.color = Color.Lerp(baseColor, tipColor, nodes[i].Depth * depthScale);
                    Gizmos.DrawWireSphere(nodes[i].Position, nodes[i].Width * 0.5f);
                }
            }

            if (drawMarkers)
            {
                Gizmos.color = markerColor;
                var markers = _skeleton.Markers;
                for (int i = 0; i < markers.Count; i++)
                {
                    LSkeletonMarker mk = markers[i];
                    // Markers carry a full frame, so show it: a leaf that is
                    // in the right place but facing the wrong way is a bug you
                    // will not see from a dot.
                    float size = Mathf.Max(mk.Width, 0.01f) * markerScale;
                    Vector3 forward = mk.Orientation * Vector3.forward * size;
                    Gizmos.DrawLine(mk.Position, mk.Position + forward);
                    Gizmos.DrawWireSphere(mk.Position + forward, size * 0.35f);
                }
            }

            Gizmos.matrix = prev;
        }
    }
}
