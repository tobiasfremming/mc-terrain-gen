using System.Collections.Generic;

namespace LSystems
{
    // Applies a grammar's productions to a word, N times. This is the whole
    // "L" of L-system, and it is deliberately the only part that knows what a
    // production is: everything downstream sees a word and nothing else.
    //
    // Reusable: it ping-pongs between two buffers, so running the same
    // rewriter for many plants allocates once, not once per plant. Not
    // thread-safe -- give each worker its own.
    public sealed class LSystemRewriter
    {
        // Hard ceiling on word length. A grammar with an average branching
        // factor of 3 hits 700k modules by iteration 12, and the difference
        // between "my plant has too many iterations" and "Unity is frozen" is
        // this number existing. Raise it deliberately, not reflexively.
        public int MaxModules = 250000;

        // True when the last run stopped early because MaxModules was reached.
        // Callers that care (the inspector preview) surface this; callers that
        // do not still get a valid, merely truncated, word.
        public bool Truncated { get; private set; }

        // Iterations actually completed, which is less than requested when
        // truncation kicked in.
        public int CompletedIterations { get; private set; }

        LModuleString _front = new LModuleString(256, 256);
        LModuleString _back = new LModuleString(256, 256);
        float[] _args = new float[8];
        readonly List<LProduction> _candidates = new List<LProduction>(4);

        public static LModuleString Run(LSystemGrammar grammar, int iterations, uint seed = 0u, uint sequence = 0u)
            => new LSystemRewriter().Rewrite(grammar, iterations, seed, sequence);

        // Returns a word owned by this rewriter -- valid until the next
        // Rewrite call on the same instance. Interpreters consume it
        // immediately, so copying by default would be pure waste; call
        // CopyResult() if you need to keep it.
        public LModuleString Rewrite(LSystemGrammar grammar, int iterations, uint seed = 0u, uint sequence = 0u)
        {
            Truncated = false;
            CompletedIterations = 0;

            if (grammar == null) { _front.Clear(); return _front; }

            int maxSlots = 0;
            for (int i = 0; i < grammar.Productions.Length; i++)
                if (grammar.Productions[i].SlotCount > maxSlots) maxSlots = grammar.Productions[i].SlotCount;
            if (_args.Length < maxSlots) _args = new float[maxSlots];

            _front.Clear();
            for (int i = 0; i < grammar.Axiom.Count; i++) _front.CopyModuleFrom(grammar.Axiom, i);

            var rng = new LRandom(seed, sequence);
            for (int it = 0; it < iterations; it++)
            {
                if (!Step(grammar, ref rng))
                {
                    Truncated = true;
                    break;
                }
                CompletedIterations++;
            }
            return _front;
        }

        public LModuleString CopyResult()
        {
            var copy = new LModuleString(_front.Count + 1, _front.ParamCount + 1);
            for (int i = 0; i < _front.Count; i++) copy.CopyModuleFrom(_front, i);
            return copy;
        }

        // One derivation step. Returns false if it would exceed MaxModules, in
        // which case _front is left untouched (the previous, complete
        // generation) rather than half-rewritten -- a partially derived word
        // is not a thing any interpreter should ever see.
        bool Step(LSystemGrammar grammar, ref LRandom rng)
        {
            LModuleString src = _front, dst = _back;
            dst.Clear();

            // Only needed for context-sensitive grammars, and it is an O(n)
            // pass plus an int per module, so it is worth not paying for it.
            int[] match = grammar.HasContext ? src.BracketMatch() : null;

            for (int i = 0; i < src.Count; i++)
            {
                LModule m = src[i];
                LProduction chosen = Select(grammar, src, i, m, match, ref rng, out bool needsRebind);

                if (chosen == null)
                {
                    // No rule fired: the module carries through unchanged.
                    // (This is what makes turtle symbols like F and + work
                    // without every grammar having to write F -> F.)
                    dst.CopyModuleFrom(src, i);
                }
                else
                {
                    if (needsRebind) Bind(grammar, src, i, chosen, match);
                    Emit(dst, chosen, ref rng);
                }

                if (dst.Count > MaxModules) return false;
            }

            _front = dst;
            _back = src;
            return true;
        }

        // Picks among the rules whose predecessor, context and condition all
        // match, in proportion to their weights.
        LProduction Select(LSystemGrammar grammar, LModuleString src, int i, LModule m, int[] match,
                           ref LRandom rng, out bool needsRebind)
        {
            needsRebind = false;
            List<LProduction> list = grammar.Match(m.Symbol, m.ParamCount);
            if (list == null || list.Count == 0) return null;

            if (list.Count == 1)
            {
                LProduction only = list[0];
                if (only.Weight <= 0f) return null;
                if (!Bind(grammar, src, i, only, match)) return null;
                if (only.Condition != null && only.Condition.Eval(_args, ref rng) == 0f) return null;
                return only;                       // _args already holds its bindings
            }

            _candidates.Clear();
            float total = 0f;
            for (int k = 0; k < list.Count; k++)
            {
                LProduction p = list[k];
                if (!Bind(grammar, src, i, p, match)) continue;
                if (p.Condition != null && p.Condition.Eval(_args, ref rng) == 0f) continue;
                if (p.Weight <= 0f) continue;
                _candidates.Add(p);
                total += p.Weight;
            }

            if (_candidates.Count == 0 || total <= 0f) return null;

            // Weights are relative, not probabilities: they need not sum to 1,
            // which means adding a variant to a rule set does not force you to
            // renumber the others.
            LProduction picked = _candidates[_candidates.Count - 1];
            if (_candidates.Count > 1)
            {
                float r = rng.NextFloat() * total;
                for (int k = 0; k < _candidates.Count; k++)
                {
                    r -= _candidates[k].Weight;
                    if (r <= 0f) { picked = _candidates[k]; break; }
                }
            }

            // Every candidate wrote into the shared args buffer while being
            // tested, so the winner's bindings have to be restored.
            needsRebind = true;
            return picked;
        }

        void Emit(LModuleString dst, LProduction p, ref LRandom rng)
        {
            LSuccessorModule[] succ = p.Successor;
            for (int k = 0; k < succ.Length; k++)
            {
                dst.StartModule(succ[k].Symbol);
                LExpr[] ps = succ[k].Params;
                if (ps != null)
                    for (int q = 0; q < ps.Length; q++)
                        dst.PushParam(ps[q].Eval(_args, ref rng));
                dst.EndModule();
            }
        }

        // ---- matching ------------------------------------------------------

        bool Bind(LSystemGrammar grammar, LModuleString src, int i, LProduction p, int[] match)
        {
            if (!MatchLeft(grammar, src, i, p.LeftContext, match)) return false;

            for (int q = 0; q < p.ParamCount; q++)
                _args[p.StrictSlotStart + q] = src.GetParam(i, q);

            if (p.RightContext != null && p.RightContext.Length > 0 &&
                !MatchRight(grammar, src, i + 1, p.RightContext, 0, match)) return false;

            return true;
        }

        // Walks left from the module being rewritten. Brackets make the word a
        // tree, so this is a walk toward the root: a ']' means an entire
        // sibling branch to skip over, and a '[' means stepping out of the
        // current branch to its parent.
        bool MatchLeft(LSystemGrammar grammar, LModuleString src, int i, LPattern[] ctx, int[] match)
        {
            if (ctx == null || ctx.Length == 0) return true;

            int k = ctx.Length - 1;                 // nearest neighbour is last in source order
            int j = i - 1;
            while (k >= 0)
            {
                if (j < 0) return false;
                char c = src[j].Symbol;

                if (c == ']')
                {
                    int open = match[j];
                    if (open < 0) return false;
                    j = open - 1;                   // skip the whole sibling branch
                    continue;
                }
                if (c == '[') { j--; continue; }    // step out to the parent
                if (grammar.IsIgnored(c)) { j--; continue; }

                LPattern pat = ctx[k];
                if (c != pat.Symbol || src[j].ParamCount != pat.ParamCount) return false;
                for (int q = 0; q < pat.ParamCount; q++)
                    _args[pat.SlotStart + q] = src.GetParam(j, q);
                k--;
                j--;
            }
            return true;
        }

        // Walks right, i.e. down the tree. At a branch point the context may
        // continue either into the branch or past it, so both are tried --
        // branch first, matching cpfg. This is why right context is recursive
        // and left context is not.
        bool MatchRight(LSystemGrammar grammar, LModuleString src, int j, LPattern[] ctx, int k, int[] match)
        {
            while (true)
            {
                if (k >= ctx.Length) return true;
                if (j >= src.Count) return false;

                char c = src[j].Symbol;
                if (c == ']') return false;         // branch ended before the context was satisfied

                if (c == '[')
                {
                    if (MatchRight(grammar, src, j + 1, ctx, k, match)) return true;
                    int close = match[j];
                    if (close < 0) return false;
                    j = close + 1;
                    continue;
                }
                if (grammar.IsIgnored(c)) { j++; continue; }

                LPattern pat = ctx[k];
                if (c != pat.Symbol || src[j].ParamCount != pat.ParamCount) return false;
                for (int q = 0; q < pat.ParamCount; q++)
                    _args[pat.SlotStart + q] = src.GetParam(j, q);
                k++;
                j++;
            }
        }
    }
}
