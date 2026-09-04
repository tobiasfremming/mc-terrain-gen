using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LSystems
{
    // One module in an L-system word: a symbol plus zero or more float
    // parameters. Parameters live in the owning LModuleString's flat float
    // buffer rather than in a per-module array, so a 100k-module word is two
    // allocations, not 100k.
    public readonly struct LModule
    {
        public readonly char Symbol;
        public readonly int ParamStart;
        public readonly int ParamCount;

        public LModule(char symbol, int paramStart, int paramCount)
        {
            Symbol = symbol;
            ParamStart = paramStart;
            ParamCount = paramCount;
        }

        public bool IsBranchOpen => Symbol == '[';
        public bool IsBranchClose => Symbol == ']';
    }

    // A word (module string). This is THE interchange format of the whole
    // library: the parser produces one (the axiom), the rewriter consumes and
    // produces one, and every interpreter -- turtle, and whatever else gets
    // written later -- consumes one. Nothing downstream of the rewriter knows
    // what a production is.
    //
    // Mutable and reusable on purpose: the rewriter ping-pongs between two of
    // these across iterations, so a 6-iteration run allocates twice, not
    // twelve times.
    public sealed class LModuleString
    {
        LModule[] _modules;
        float[] _params;
        int _count;
        int _paramCount;

        // Index of the module currently being built by StartModule, or -1.
        int _open = -1;

        // Bumped by every mutation so cached derived data (BracketMatch) can
        // tell "same length, different content" from "unchanged".
        int _version;

        public LModuleString(int moduleCapacity = 64, int paramCapacity = 64)
        {
            _modules = new LModule[moduleCapacity < 1 ? 1 : moduleCapacity];
            _params = new float[paramCapacity < 1 ? 1 : paramCapacity];
        }

        public int Count => _count;
        public int ParamCount => _paramCount;

        public LModule this[int i] => _modules[i];

        // Parameter p of module i. Out-of-range params read as 0 so that
        // interpreters can ask for an optional parameter without branching.
        public float GetParam(int moduleIndex, int p)
        {
            LModule m = _modules[moduleIndex];
            if (p < 0 || p >= m.ParamCount) return 0f;
            return _params[m.ParamStart + p];
        }

        public float GetParamRaw(int paramIndex) => _params[paramIndex];

        public void Clear()
        {
            _count = 0;
            _paramCount = 0;
            _open = -1;
            _version++;
        }

        public void Add(char symbol)
        {
            EnsureModules(_count + 1);
            _modules[_count++] = new LModule(symbol, _paramCount, 0);
            _version++;
        }

        public void Add(char symbol, float p0)
        {
            StartModule(symbol);
            PushParam(p0);
            EndModule();
        }

        // Streaming form, for when the parameter count is not known up front
        // (the rewriter evaluating a successor, the parser reading an axiom).
        public void StartModule(char symbol)
        {
            EnsureModules(_count + 1);
            _open = _count;
            _modules[_count++] = new LModule(symbol, _paramCount, 0);
            _version++;
        }

        public void PushParam(float value)
        {
            if (_paramCount == _params.Length)
                System.Array.Resize(ref _params, _params.Length * 2);
            _params[_paramCount++] = value;
            _version++;
        }

        public void EndModule()
        {
            if (_open < 0) return;
            LModule m = _modules[_open];
            _modules[_open] = new LModule(m.Symbol, m.ParamStart, _paramCount - m.ParamStart);
            _open = -1;
            _version++;
        }

        public void CopyModuleFrom(LModuleString src, int index)
        {
            LModule m = src._modules[index];
            StartModule(m.Symbol);
            for (int i = 0; i < m.ParamCount; i++)
                PushParam(src._params[m.ParamStart + i]);
            EndModule();
        }

        void EnsureModules(int needed)
        {
            if (needed <= _modules.Length) return;
            int cap = _modules.Length;
            while (cap < needed) cap *= 2;
            System.Array.Resize(ref _modules, cap);
        }

        // Bracket-matching table: for every '[' the index of its ']' and vice
        // versa, -1 elsewhere. Context-sensitive matching needs to skip whole
        // subtrees, and doing that by rescanning is quadratic on deep words.
        // Built on demand and invalidated by any mutation via _version.
        int[] _match;
        int _matchStamp = -1;

        public int[] BracketMatch()
        {
            if (_matchStamp == _version && _match != null) return _match;
            if (_match == null || _match.Length < _count) _match = new int[_count < 1 ? 1 : _count];
            var stack = _matchStack ??= new Stack<int>();
            stack.Clear();
            for (int i = 0; i < _count; i++)
            {
                _match[i] = -1;
                char c = _modules[i].Symbol;
                if (c == '[') stack.Push(i);
                else if (c == ']' && stack.Count > 0)
                {
                    int open = stack.Pop();
                    _match[open] = i;
                    _match[i] = open;
                }
            }
            _matchStamp = _version;
            return _match;
        }

        Stack<int> _matchStack;

        // "F(1) [ +(25.7) A(0.68) ]". Round-trips through the parser, which is
        // what makes the tests readable.
        public string ToDisplayString(int decimals = 3)
        {
            var sb = new StringBuilder();
            string fmt = "0." + new string('#', decimals < 0 ? 0 : decimals);
            for (int i = 0; i < _count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                LModule m = _modules[i];
                sb.Append(m.Symbol);
                if (m.ParamCount == 0) continue;
                sb.Append('(');
                for (int p = 0; p < m.ParamCount; p++)
                {
                    if (p > 0) sb.Append(',');
                    sb.Append(_params[m.ParamStart + p].ToString(fmt, CultureInfo.InvariantCulture));
                }
                sb.Append(')');
            }
            return sb.ToString();
        }

        // Symbols only, no parameters or spacing: "F[+A][-A]". The classic
        // textbook form -- handy for tests of non-parametric grammars.
        public string ToSymbolString()
        {
            var sb = new StringBuilder(_count);
            for (int i = 0; i < _count; i++) sb.Append(_modules[i].Symbol);
            return sb.ToString();
        }

        public override string ToString() => ToDisplayString();
    }
}
