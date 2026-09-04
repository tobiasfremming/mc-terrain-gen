using System.Collections.Generic;
using UnityEngine;

namespace LSystems
{
    // A grammar as a designer-editable asset, matching how every other tunable
    // in this project is authored (Biome, WorldConfig, the density fields).
    //
    // The asset owns the *text*; the parsed grammar is a cache rebuilt on
    // demand and thrown away on any edit. That ordering matters: the text is
    // the source of truth, so a grammar survives a script recompile, diffs
    // usefully in git, and can be pasted in from a paper.
    [CreateAssetMenu(fileName = "LSystem", menuName = "Marching Cubes/L-System Grammar")]
    public class LSystemGrammarAsset : ScriptableObject
    {
        [TextArea(8, 40)]
        public string source = DefaultSource;

        [Tooltip("Used when a caller does not specify its own. A #iterations directive in the source overrides this.")]
        [Range(0, 20)] public int iterations = 7;

        LSystemGrammar _grammar;
        IReadOnlyList<LSystemParseError> _errors;
        bool _parsed;

        // Parsed form, or null if the source does not parse. Never throws:
        // callers check IsValid or handle null, and Errors says what is wrong.
        public LSystemGrammar Grammar
        {
            get { EnsureParsed(); return _grammar; }
        }

        public IReadOnlyList<LSystemParseError> Errors
        {
            get { EnsureParsed(); return _errors; }
        }

        public bool IsValid
        {
            get { EnsureParsed(); return _errors.Count == 0; }
        }

        // Effective iteration count: the source's #iterations wins, because a
        // grammar that only makes sense at 7 iterations should say so once
        // rather than in every scene that uses it.
        public int EffectiveIterations
        {
            get
            {
                EnsureParsed();
                if (_grammar != null && _grammar.DefaultIterations >= 0) return _grammar.DefaultIterations;
                return iterations;
            }
        }

        public void Invalidate()
        {
            _parsed = false;
            _grammar = null;
            _errors = null;
        }

        void EnsureParsed()
        {
            if (_parsed) return;
            _parsed = true;
            LSystemParser.TryParse(source, out _grammar, out _errors);
        }

        // Convenience end-to-end path, so the common case is one call:
        // text -> word -> skeleton. Anything that wants the intermediate word
        // (a non-turtle interpreter, say) uses LSystemRewriter directly.
        public LSkeleton BuildSkeleton(TurtleSettings turtle, int iterationsOverride = -1,
                                       uint seed = 0u, uint sequence = 0u,
                                       LSystemRewriter rewriter = null, LSkeleton reuse = null)
        {
            EnsureParsed();
            if (_grammar == null) return reuse ?? new LSkeleton();
            int n = iterationsOverride >= 0 ? iterationsOverride : EffectiveIterations;
            LModuleString word = (rewriter ?? new LSystemRewriter()).Rewrite(_grammar, n, seed, sequence);
            return TurtleInterpreter.Build(word, turtle, reuse);
        }

        void OnValidate() => Invalidate();

        // A stochastic parametric tree: shows off #define, weighted variants,
        // conditions, a leaf marker, and the golden-angle roll that keeps
        // branches from stacking into a plane. One rule per line, because a
        // rule ends at the newline unless a '[' is still open.
        public const string DefaultSource =
@"// Stochastic parametric tree. Try iterations 6-8; it stops growing on its
// own once every apex is shorter than MIN, so more iterations are harmless.
#define ANGLE 32
#define SHRINK 0.7
#define MIN 0.45      // apex length at which a branch stops and puts out a leaf
#define TAPER 0.72
#define PHI 137.5      // golden angle: successive branches never line up

#axiom: !(0.3) A(2.2, 0.3)

// A(length, width) is a growing apex. Two variants, drawn 65/35, so no two
// seeds give the same tree.
A(l,w) : l >= MIN : 0.65 -> F(l) !(w*TAPER) [ &(ANGLE) /(PHI) A(l*SHRINK, w*TAPER) ] [ ^(ANGLE*0.55) /(PHI*2) A(l*SHRINK*0.92, w*TAPER) ]
A(l,w) : l >= MIN : 0.35 -> F(l) !(w*TAPER) [ &(ANGLE*1.4) \(PHI) A(l*SHRINK*0.85, w*TAPER) ] A(l*SHRINK, w*TAPER)

// Too short to keep branching: finish the twig and hang a leaf on it.
// L is not a turtle command, so it lands in the skeleton as a marker.
A(l,w) : l < MIN -> F(l) $ L(l*4)
";
    }
}
