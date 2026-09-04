using System.Collections.Generic;

namespace LSystems
{
    // One module pattern on the left of an arrow: a symbol plus the number of
    // formal parameters it binds, and where those parameters land in the
    // production's flat slot array.
    //
    // Slots are assigned at parse time in source order -- left context, then
    // the strict predecessor, then right context -- so binding at rewrite time
    // is a straight copy into args[SlotStart + i] with no name lookup.
    public readonly struct LPattern
    {
        public readonly char Symbol;
        public readonly int ParamCount;
        public readonly int SlotStart;

        public LPattern(char symbol, int paramCount, int slotStart)
        {
            Symbol = symbol;
            ParamCount = paramCount;
            SlotStart = slotStart;
        }
    }

    // One module on the right of an arrow. Params are expressions over the
    // production's bound slots; a module with no parentheses has Params == null.
    public readonly struct LSuccessorModule
    {
        public readonly char Symbol;
        public readonly LExpr[] Params;

        public LSuccessorModule(char symbol, LExpr[] parameters)
        {
            Symbol = symbol;
            Params = parameters;
        }
    }

    // A single production rule.
    //
    //   [left <] pred [> right] [: condition] [: weight] -> successor
    //
    // Weight and Condition are independent: several productions can share a
    // predecessor, and among those whose condition passes, one is drawn in
    // proportion to Weight. A grammar where every candidate has weight 1 and no
    // condition is a plain deterministic (D0L) system, so the simple case costs
    // nothing to express.
    public sealed class LProduction
    {
        public char Symbol;                 // strict predecessor symbol
        public int ParamCount;              // strict predecessor arity
        public LPattern[] LeftContext;      // source order; matched right-to-left
        public LPattern[] RightContext;     // source order; matched left-to-right
        public LExpr Condition;             // null == always true
        public float Weight = 1f;
        public LSuccessorModule[] Successor;
        public int StrictSlotStart;         // where the predecessor's own params land
        public int SlotCount;               // size of the args array this rule needs
        public int SourceLine;              // for diagnostics

        public bool HasContext => (LeftContext != null && LeftContext.Length > 0)
                               || (RightContext != null && RightContext.Length > 0);
    }

    // A parsed L-system: axiom, productions, and the handful of knobs the
    // grammar text can set. Immutable once the parser hands it over.
    //
    // This is the only thing LSystemRewriter needs, and it carries no Unity
    // types -- grammars can be built in code, loaded from a ScriptableObject,
    // or generated procedurally, and the rewriter cannot tell the difference.
    public sealed class LSystemGrammar
    {
        public LModuleString Axiom { get; }
        public LProduction[] Productions { get; }

        // Symbols skipped when matching context (classically "+ - [ ]", so that
        // a rule can see past turtle bookkeeping to the structural neighbours).
        public IReadOnlyCollection<char> IgnoredSymbols => _ignored;
        readonly HashSet<char> _ignored;

        // Defaults a grammar can declare for its callers. Advisory: the caller
        // may override them, they just save repeating numbers in the inspector.
        public int DefaultIterations { get; }
        public IReadOnlyDictionary<string, float> Defines { get; }

        public bool HasContext { get; }
        public bool IsStochastic { get; }

        // Productions bucketed by (symbol, arity). Arity is part of the key
        // because "A(x) -> ..." must not fire on a bare "A": in a parametric
        // system arity is part of a module's identity.
        readonly Dictionary<int, List<LProduction>> _byKey;

        public LSystemGrammar(
            LModuleString axiom,
            LProduction[] productions,
            HashSet<char> ignored,
            int defaultIterations,
            Dictionary<string, float> defines)
        {
            Axiom = axiom;
            Productions = productions ?? new LProduction[0];
            _ignored = ignored ?? new HashSet<char>();
            DefaultIterations = defaultIterations;
            Defines = defines ?? new Dictionary<string, float>();

            _byKey = new Dictionary<int, List<LProduction>>(Productions.Length);
            for (int i = 0; i < Productions.Length; i++)
            {
                LProduction p = Productions[i];
                int key = Key(p.Symbol, p.ParamCount);
                if (!_byKey.TryGetValue(key, out var list))
                    _byKey[key] = list = new List<LProduction>(2);
                list.Add(p);
                if (p.HasContext) HasContext = true;
            }

            foreach (var kv in _byKey)
                if (kv.Value.Count > 1) { IsStochastic = true; break; }
        }

        static int Key(char symbol, int arity) => (symbol << 8) | (arity & 0xFF);

        public bool IsIgnored(char symbol) => _ignored.Contains(symbol);

        // Candidate rules for a module, or null. The rewriter still has to
        // check context and condition -- this only narrows by identity.
        public List<LProduction> Match(char symbol, int arity)
        {
            _byKey.TryGetValue(Key(symbol, arity), out var list);
            return list;
        }
    }
}
