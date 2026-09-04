using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LSystems
{
    public struct LSystemParseError
    {
        public int Line;        // 1-based
        public int Column;      // 1-based
        public string Message;

        public override string ToString() => "line " + Line + ", col " + Column + ": " + Message;
    }

    // Text -> LSystemGrammar, in the cpfg notation of Prusinkiewicz &
    // Lindenmayer's "The Algorithmic Beauty of Plants", so grammars can be
    // lifted from the literature unchanged. See README.md in this folder for
    // the full syntax; the short version:
    //
    //   #define R 1.456
    //   #ignore: + - [ ]
    //   #axiom: A(1)
    //   A(s) -> F(s) [ +(25.7) A(s/R) ] [ -(25.7) A(s/R) ]
    //   B(w) : 0.6 -> F(w) B(w*0.9)          // stochastic weight
    //   C(x) : x > 0.1 -> F(x) C(x*0.8)      // condition
    //   A(x) < B(y) > C(z) -> B(y+x)         // context
    //
    // Symbols are single characters, exactly as in the book: "FF" is two
    // modules, not one named FF. That is the whole reason the book's grammars
    // paste in and work, and it is why there is no identifier lexing here.
    //
    // Errors are collected rather than thrown: a grammar is authored text in an
    // inspector field, it is wrong most of the time it is being typed, and the
    // useful behaviour is to report every bad line at once and still hand back
    // whatever parsed.
    public static class LSystemParser
    {
        public static bool TryParse(string source, out LSystemGrammar grammar, out IReadOnlyList<LSystemParseError> errors)
        {
            var p = new Impl(source ?? string.Empty);
            bool ok = p.Parse(out grammar);
            errors = p.Errors;
            return ok;
        }

        // Parse a bare module string ("F[+F]F") with no productions around it.
        // Used for axioms authored in code and by the tests.
        public static bool TryParseModuleString(string source, out LModuleString word, out IReadOnlyList<LSystemParseError> errors)
        {
            var p = new Impl(source ?? string.Empty);
            bool ok = p.ParseStandaloneWord(out word);
            errors = p.Errors;
            return ok;
        }

        // Characters that can never be a module symbol because the grammar
        // syntax itself uses them. Everything else is fair game, including the
        // turtle operators + - / \ & ^ | ! and the brackets.
        public static bool IsReservedChar(char c) =>
            c == '(' || c == ')' || c == ',' || c == ':' || c == '<' || c == '>' || c == '#';

        public static bool IsSymbolChar(char c) => !char.IsWhiteSpace(c) && !IsReservedChar(c);

        sealed class Impl
        {
            readonly char[] _src;       // comments blanked out, newlines preserved
            int _pos;
            int _line = 1;              // tracked incrementally: Line/Column are
            int _lineStart;             // read once per token, not once per file

            readonly List<LSystemParseError> _errors = new List<LSystemParseError>();
            public IReadOnlyList<LSystemParseError> Errors => _errors;

            readonly Dictionary<string, float> _defines = new Dictionary<string, float>();
            readonly HashSet<char> _ignored = new HashSet<char>();
            readonly List<LProduction> _productions = new List<LProduction>();

            // Formal parameter name -> slot, rebuilt for each production.
            readonly Dictionary<string, int> _slots = new Dictionary<string, int>();

            LModuleString _axiom;
            int _iterations = -1;

            public Impl(string source)
            {
                _src = StripComments(source);
            }

            // ---- entry points ------------------------------------------------

            public bool Parse(out LSystemGrammar grammar)
            {
                while (true)
                {
                    SkipWhitespaceAndNewlines();
                    if (AtEnd) break;

                    if (Peek == '#') ParseDirective();
                    else ParseProduction();

                    // Whatever the statement did or failed to do, resume at the
                    // next line so one bad rule does not cascade. (Successors
                    // already stop on the newline; this only discards trailing
                    // junk after a directive or a failed rule.)
                    SkipToEndOfLine();
                }

                if (_axiom == null)
                {
                    Error(1, 1, "no #axiom directive: the grammar has nothing to rewrite");
                    _axiom = new LModuleString();
                }

                grammar = new LSystemGrammar(_axiom, _productions.ToArray(), _ignored, _iterations, _defines);
                return _errors.Count == 0;
            }

            public bool ParseStandaloneWord(out LModuleString word)
            {
                _slots.Clear();
                var modules = ParseSuccessor();
                word = Evaluate(modules);
                return _errors.Count == 0;
            }

            // ---- directives --------------------------------------------------

            void ParseDirective()
            {
                int dirLine = Line, dirCol = Column;
                Advance();                                   // '#'
                string name = ReadIdentifier();
                if (name.Length == 0)
                {
                    Error(dirLine, dirCol, "expected a directive name after '#'");
                    return;
                }

                SkipInlineWhitespace();
                if (!AtEnd && Peek == ':') Advance();        // "#axiom:" and "#axiom" both fine

                switch (name)
                {
                    case "define":
                    {
                        SkipInlineWhitespace();
                        int nameLine = Line, nameCol = Column;
                        string constName = ReadIdentifier();
                        if (constName.Length == 0)
                        {
                            Error(nameLine, nameCol, "expected a name after #define");
                            return;
                        }
                        // '=' is optional so both "#define R 1.456" and
                        // "#define R = 1.456" read naturally.
                        SkipInlineWhitespace();
                        if (!AtEnd && Peek == '=') Advance();

                        LExpr e = ParseExpression();
                        if (e == null) return;
                        if (!e.IsConstant)
                        {
                            Error(nameLine, nameCol, "#define " + constName + " must be a constant expression");
                            return;
                        }
                        var rng = new LRandom(0);
                        _defines[constName] = e.Eval(null, ref rng);
                        break;
                    }

                    case "axiom":
                    {
                        _slots.Clear();
                        var modules = ParseSuccessor();
                        if (_axiom != null) Error(dirLine, dirCol, "duplicate #axiom");
                        _axiom = Evaluate(modules);
                        break;
                    }

                    case "ignore":
                    {
                        SkipInlineWhitespace();
                        while (!AtEnd && Peek != '\n')
                        {
                            char c = Peek;
                            if (char.IsWhiteSpace(c) || c == ',') { Advance(); continue; }
                            _ignored.Add(c);
                            Advance();
                        }
                        break;
                    }

                    case "iterations":
                    {
                        LExpr e = ParseExpression();
                        if (e == null) return;
                        var rng = new LRandom(0);
                        _iterations = (int)System.Math.Round(e.Eval(null, ref rng));
                        break;
                    }

                    default:
                        Error(dirLine, dirCol, "unknown directive '#" + name + "' (expected define, axiom, ignore or iterations)");
                        break;
                }
            }

            // ---- productions -------------------------------------------------

            void ParseProduction()
            {
                int ruleLine = Line, ruleCol = Column;
                int errorsBefore = _errors.Count;

                var first = ParsePatterns();
                List<RawPattern> left = null, strict, right = null;

                SkipInlineWhitespace();
                if (!AtEnd && Peek == '<')
                {
                    Advance();
                    left = first;
                    strict = ParsePatterns();
                }
                else strict = first;

                SkipInlineWhitespace();
                if (!AtEnd && Peek == '>')
                {
                    Advance();
                    right = ParsePatterns();
                }

                if (strict.Count != 1)
                {
                    Error(ruleLine, ruleCol, strict.Count == 0
                        ? "a rule needs exactly one module before the arrow"
                        : "a rule can only rewrite one module at a time (got " + strict.Count +
                          "); use left/right context with '<' and '>' for the neighbours");
                    return;
                }

                // Slots: left context, strict predecessor, right context, in
                // source order. Assigned before any expression is parsed, so
                // conditions and successors can name any of them.
                _slots.Clear();
                int slot = 0;
                LPattern[] leftPat = BuildPatterns(left, ref slot);
                LPattern[] strictPat = BuildPatterns(strict, ref slot);
                LPattern[] rightPat = BuildPatterns(right, ref slot);

                LExpr condition = null;
                float weight = 1f;
                bool sawWeight = false;

                SkipInlineWhitespace();
                while (!AtEnd && Peek == ':')
                {
                    Advance();
                    int clauseLine = Line, clauseCol = Column;

                    if (TryReadWeightClause(out float w))
                    {
                        if (sawWeight) Error(clauseLine, clauseCol, "duplicate probability weight");
                        if (w < 0f) { Error(clauseLine, clauseCol, "probability weight cannot be negative"); w = 0f; }
                        weight = w;
                        sawWeight = true;
                    }
                    else
                    {
                        LExpr e = ParseExpression();
                        if (e == null) return;
                        // Several ':' clauses AND together, so
                        // "A(x) : x > 0 : x < 10 -> ..." reads the obvious way.
                        condition = condition == null ? e : LExpr.Binary(LBinaryOp.And, condition, e);
                    }
                    SkipInlineWhitespace();
                }

                SkipInlineWhitespace();
                if (!AtArrow)
                {
                    Error(Line, Column, "expected '->' after the rule's predecessor");
                    return;
                }
                Advance(); Advance();                        // '->'

                var successorModules = ParseSuccessor();
                if (successorModules == null) return;

                // A rule that produced any diagnostic is not added: half of a
                // rule is worse than none of it, and the caller still gets
                // every other rule in the file.
                if (_errors.Count != errorsBefore) return;

                _productions.Add(new LProduction
                {
                    Symbol = strictPat[0].Symbol,
                    ParamCount = strictPat[0].ParamCount,
                    LeftContext = leftPat,
                    RightContext = rightPat,
                    Condition = condition,
                    Weight = weight,
                    Successor = ToSuccessor(successorModules),
                    StrictSlotStart = strictPat[0].SlotStart,
                    SlotCount = slot,
                    SourceLine = ruleLine,
                });
            }

            struct RawPattern
            {
                public char Symbol;
                public List<string> Params;
                public int Line, Column;
            }

            // A predecessor module list: symbols whose parameters are formal
            // names, not expressions. Stops at '<', '>', ':', '->' or newline.
            List<RawPattern> ParsePatterns()
            {
                var result = new List<RawPattern>();
                while (true)
                {
                    SkipInlineWhitespace();
                    if (AtEnd || Peek == '\n') break;
                    char c = Peek;
                    if (c == '<' || c == '>' || c == ':') break;
                    if (AtArrow) break;
                    if (!IsSymbolChar(c))
                    {
                        Error(Line, Column, "'" + c + "' cannot be a module symbol");
                        Advance();
                        continue;
                    }

                    var pat = new RawPattern { Symbol = c, Line = Line, Column = Column };
                    Advance();

                    // As in successors, '(' must touch its symbol: "A (x)" is
                    // an error rather than a silent "A(x)".
                    if (!AtEnd && Peek == '(')
                    {
                        Advance();
                        pat.Params = new List<string>();
                        while (true)
                        {
                            SkipInlineWhitespace();
                            if (AtEnd || Peek == '\n')
                            {
                                Error(pat.Line, pat.Column, "unterminated parameter list on '" + pat.Symbol + "'");
                                break;
                            }
                            if (Peek == ')') { Advance(); break; }

                            int nLine = Line, nCol = Column;
                            string id = ReadIdentifier();
                            if (id.Length == 0)
                            {
                                Error(nLine, nCol, "a rule's predecessor takes parameter names, not expressions");
                                SkipToEndOfLine();
                                break;
                            }
                            pat.Params.Add(id);

                            SkipInlineWhitespace();
                            if (!AtEnd && Peek == ',') Advance();
                        }
                    }
                    result.Add(pat);
                }
                return result;
            }

            LPattern[] BuildPatterns(List<RawPattern> raw, ref int slot)
            {
                if (raw == null || raw.Count == 0) return System.Array.Empty<LPattern>();
                var result = new LPattern[raw.Count];
                for (int i = 0; i < raw.Count; i++)
                {
                    RawPattern p = raw[i];
                    int n = p.Params?.Count ?? 0;
                    result[i] = new LPattern(p.Symbol, n, slot);
                    for (int k = 0; k < n; k++)
                    {
                        string name = p.Params[k];
                        if (_slots.ContainsKey(name))
                            Error(p.Line, p.Column, "parameter '" + name + "' is bound twice in this rule");
                        else
                            _slots[name] = slot;
                        slot++;
                    }
                }
                return result;
            }

            // A ':' clause is a probability weight when it is nothing but a
            // number. "B : 0.6 -> ..." is a weight; "B(x) : x -> ..." is a
            // condition on x. This is the one ambiguity in the notation and it
            // is resolved by lookahead, not by a keyword, so book grammars work.
            bool TryReadWeightClause(out float weight)
            {
                int savePos = _pos, saveLine = _line, saveLineStart = _lineStart;
                SkipInlineWhitespace();
                if (TryReadNumber(out weight))
                {
                    SkipInlineWhitespace();
                    if (AtEnd || Peek == ':' || Peek == '\n' || AtArrow) return true;
                }
                _pos = savePos;
                _line = saveLine;
                _lineStart = saveLineStart;
                weight = 0f;
                return false;
            }

            struct RawSuccessor
            {
                public char Symbol;
                public LExpr[] Params;
            }

            static LSuccessorModule[] ToSuccessor(List<RawSuccessor> raw)
            {
                var result = new LSuccessorModule[raw.Count];
                for (int i = 0; i < raw.Count; i++)
                    result[i] = new LSuccessorModule(raw[i].Symbol, raw[i].Params);
                return result;
            }

            // The right-hand side. Runs to the end of the line, except that an
            // unclosed '[' keeps the statement open -- which is what makes
            // multi-line branching rules readable without a continuation char
            // (and '\' cannot be one: it is the roll-left turtle symbol).
            List<RawSuccessor> ParseSuccessor()
            {
                var result = new List<RawSuccessor>();
                int depth = 0;
                int openLine = Line, openCol = Column;

                while (true)
                {
                    // Newlines terminate only at depth 0.
                    while (!AtEnd && (Peek == ' ' || Peek == '\t' || Peek == '\r' || (Peek == '\n' && depth > 0)))
                        Advance();

                    if (AtEnd || Peek == '\n') break;

                    char c = Peek;
                    if (!IsSymbolChar(c))
                    {
                        Error(Line, Column, "'" + c + "' cannot appear in a successor");
                        Advance();
                        continue;
                    }

                    int symLine = Line, symCol = Column;
                    Advance();

                    if (c == '[') { if (depth++ == 0) { openLine = symLine; openCol = symCol; } }
                    else if (c == ']')
                    {
                        depth--;
                        if (depth < 0)
                        {
                            Error(symLine, symCol, "']' with no matching '['");
                            depth = 0;
                        }
                    }

                    var mod = new RawSuccessor { Symbol = c };

                    // Note: no SkipInlineWhitespace before '(' here. "F (x)"
                    // would otherwise silently mean "F(x)" while reading like
                    // two modules; requiring the paren to touch its symbol
                    // keeps that unambiguous.
                    if (!AtEnd && Peek == '(')
                    {
                        if (c == '[' || c == ']')
                            Error(symLine, symCol, "'" + c + "' cannot take parameters");

                        Advance();
                        var ps = new List<LExpr>();
                        while (true)
                        {
                            SkipInlineWhitespace();
                            if (AtEnd || Peek == '\n')
                            {
                                Error(symLine, symCol, "unterminated parameter list on '" + c + "'");
                                break;
                            }
                            if (Peek == ')') { Advance(); break; }

                            LExpr e = ParseExpression();
                            if (e == null) { SkipToEndOfLine(); break; }
                            ps.Add(e);

                            SkipInlineWhitespace();
                            if (!AtEnd && Peek == ',') Advance();
                        }
                        mod.Params = ps.ToArray();
                    }

                    result.Add(mod);
                }

                if (depth > 0) Error(openLine, openCol, "'[' is never closed");
                return result;
            }

            // Successor modules whose parameters are all constant (axioms) get
            // baked straight into a word.
            LModuleString Evaluate(List<RawSuccessor> modules)
            {
                var word = new LModuleString(modules.Count + 1, modules.Count + 1);
                var rng = new LRandom(0);
                for (int i = 0; i < modules.Count; i++)
                {
                    RawSuccessor m = modules[i];
                    word.StartModule(m.Symbol);
                    if (m.Params != null)
                        for (int p = 0; p < m.Params.Length; p++)
                            word.PushParam(m.Params[p].Eval(null, ref rng));
                    word.EndModule();
                }
                return word;
            }

            // ---- expressions -------------------------------------------------

            LExpr ParseExpression() => ParseOr();

            LExpr ParseOr()
            {
                LExpr a = ParseAnd();
                while (a != null)
                {
                    SkipInlineWhitespace();
                    if (Peek == '|' && Peek1 == '|') { Advance(); Advance(); }
                    else break;
                    LExpr b = ParseAnd();
                    if (b == null) return null;
                    a = LExpr.Binary(LBinaryOp.Or, a, b);
                }
                return a;
            }

            LExpr ParseAnd()
            {
                LExpr a = ParseEquality();
                while (a != null)
                {
                    SkipInlineWhitespace();
                    if (Peek == '&' && Peek1 == '&') { Advance(); Advance(); }
                    else break;
                    LExpr b = ParseEquality();
                    if (b == null) return null;
                    a = LExpr.Binary(LBinaryOp.And, a, b);
                }
                return a;
            }

            LExpr ParseEquality()
            {
                LExpr a = ParseComparison();
                while (a != null)
                {
                    SkipInlineWhitespace();
                    LBinaryOp op;
                    if (Peek == '=' && Peek1 == '=') op = LBinaryOp.Equal;
                    else if (Peek == '!' && Peek1 == '=') op = LBinaryOp.NotEqual;
                    else break;
                    Advance(); Advance();
                    LExpr b = ParseComparison();
                    if (b == null) return null;
                    a = LExpr.Binary(op, a, b);
                }
                return a;
            }

            LExpr ParseComparison()
            {
                LExpr a = ParseAdditive();
                while (a != null)
                {
                    SkipInlineWhitespace();
                    LBinaryOp op;
                    int width;
                    if (Peek == '<' && Peek1 == '=') { op = LBinaryOp.LessEqual; width = 2; }
                    else if (Peek == '>' && Peek1 == '=') { op = LBinaryOp.GreaterEqual; width = 2; }
                    else if (Peek == '<') { op = LBinaryOp.Less; width = 1; }
                    else if (Peek == '>') { op = LBinaryOp.Greater; width = 1; }
                    else break;
                    for (int i = 0; i < width; i++) Advance();
                    LExpr b = ParseAdditive();
                    if (b == null) return null;
                    a = LExpr.Binary(op, a, b);
                }
                return a;
            }

            LExpr ParseAdditive()
            {
                LExpr a = ParseMultiplicative();
                while (a != null)
                {
                    SkipInlineWhitespace();
                    // '->' ends the expression; it is not a subtraction.
                    if (AtArrow) break;
                    LBinaryOp op;
                    if (Peek == '+') op = LBinaryOp.Add;
                    else if (Peek == '-') op = LBinaryOp.Sub;
                    else break;
                    Advance();
                    LExpr b = ParseMultiplicative();
                    if (b == null) return null;
                    a = LExpr.Binary(op, a, b);
                }
                return a;
            }

            LExpr ParseMultiplicative()
            {
                LExpr a = ParseUnary();
                while (a != null)
                {
                    SkipInlineWhitespace();
                    LBinaryOp op;
                    if (Peek == '*') op = LBinaryOp.Mul;
                    else if (Peek == '/') op = LBinaryOp.Div;
                    else if (Peek == '%') op = LBinaryOp.Mod;
                    else break;
                    Advance();
                    LExpr b = ParseUnary();
                    if (b == null) return null;
                    a = LExpr.Binary(op, a, b);
                }
                return a;
            }

            LExpr ParseUnary()
            {
                SkipInlineWhitespace();
                if (Peek == '-')
                {
                    Advance();
                    LExpr a = ParseUnary();
                    return a == null ? null : LExpr.Unary(LUnaryOp.Negate, a);
                }
                if (Peek == '!' && Peek1 != '=')
                {
                    Advance();
                    LExpr a = ParseUnary();
                    return a == null ? null : LExpr.Unary(LUnaryOp.Not, a);
                }
                if (Peek == '+') { Advance(); return ParseUnary(); }
                return ParsePower();
            }

            // Right-associative, and binds tighter than unary minus so that
            // -x^2 is -(x^2), as everywhere else.
            LExpr ParsePower()
            {
                LExpr a = ParsePrimary();
                if (a == null) return null;
                SkipInlineWhitespace();
                if (Peek == '^')
                {
                    Advance();
                    LExpr b = ParseUnary();
                    if (b == null) return null;
                    return LExpr.Binary(LBinaryOp.Pow, a, b);
                }
                return a;
            }

            LExpr ParsePrimary()
            {
                SkipInlineWhitespace();
                int line = Line, col = Column;

                if (AtEnd || Peek == '\n')
                {
                    Error(line, col, "unexpected end of expression");
                    return null;
                }

                if (Peek == '(')
                {
                    Advance();
                    LExpr e = ParseExpression();
                    if (e == null) return null;
                    SkipInlineWhitespace();
                    if (Peek != ')') { Error(line, col, "missing ')'"); return null; }
                    Advance();
                    return e;
                }

                if (TryReadNumber(out float value)) return LExpr.Constant(value);

                string id = ReadIdentifier();
                if (id.Length == 0)
                {
                    Error(line, col, "'" + Peek + "' is not valid in an expression");
                    Advance();
                    return null;
                }

                SkipInlineWhitespace();
                if (Peek == '(')
                {
                    if (!LFuncTable.TryResolve(id, out LFunc func, out int minArgs, out int maxArgs))
                    {
                        Error(line, col, "unknown function '" + id + "'");
                        return null;
                    }
                    Advance();
                    var args = new List<LExpr>();
                    SkipInlineWhitespace();
                    if (Peek == ')') Advance();
                    else
                    {
                        while (true)
                        {
                            LExpr arg = ParseExpression();
                            if (arg == null) return null;
                            args.Add(arg);
                            SkipInlineWhitespace();
                            if (Peek == ',') { Advance(); continue; }
                            if (Peek == ')') { Advance(); break; }
                            Error(Line, Column, "expected ',' or ')' in call to '" + id + "'");
                            return null;
                        }
                    }
                    if (args.Count < minArgs || args.Count > maxArgs)
                    {
                        Error(line, col, "'" + id + "' takes " +
                            (minArgs == maxArgs ? minArgs.ToString() : minArgs + " to " + maxArgs) +
                            " argument(s), got " + args.Count);
                        return null;
                    }
                    return LExpr.Call(func, args.ToArray());
                }

                if (_slots.TryGetValue(id, out int slotIndex)) return LExpr.Arg(slotIndex);
                if (_defines.TryGetValue(id, out float defined)) return LExpr.Constant(defined);
                if (id == "pi") return LExpr.Constant((float)System.Math.PI);
                if (id == "e") return LExpr.Constant((float)System.Math.E);

                Error(line, col, "'" + id + "' is not a parameter of this rule, a #define, or a known function");
                return null;
            }

            // ---- lexing primitives -------------------------------------------

            bool AtEnd => _pos >= _src.Length;
            char Peek => _pos < _src.Length ? _src[_pos] : '\0';
            char Peek1 => _pos + 1 < _src.Length ? _src[_pos + 1] : '\0';
            bool AtArrow => Peek == '-' && Peek1 == '>';

            int Line => _line;
            int Column => _pos - _lineStart + 1;

            void Advance()
            {
                if (_pos >= _src.Length) return;
                if (_src[_pos] == '\n') { _line++; _lineStart = _pos + 1; }
                _pos++;
            }

            void SkipInlineWhitespace()
            {
                while (!AtEnd)
                {
                    char c = Peek;
                    if (c == ' ' || c == '\t' || c == '\r') Advance();
                    else break;
                }
            }

            void SkipWhitespaceAndNewlines()
            {
                while (!AtEnd && char.IsWhiteSpace(Peek)) Advance();
            }

            void SkipToEndOfLine()
            {
                while (!AtEnd && Peek != '\n') Advance();
            }

            string ReadIdentifier()
            {
                if (AtEnd) return string.Empty;
                char c = Peek;
                if (!char.IsLetter(c) && c != '_') return string.Empty;
                var sb = new StringBuilder();
                while (!AtEnd)
                {
                    c = Peek;
                    if (char.IsLetterOrDigit(c) || c == '_') { sb.Append(c); Advance(); }
                    else break;
                }
                return sb.ToString();
            }

            bool TryReadNumber(out float value)
            {
                value = 0f;
                int start = _pos;
                bool any = false;
                while (!AtEnd && char.IsDigit(Peek)) { Advance(); any = true; }
                if (!AtEnd && Peek == '.')
                {
                    int save = _pos;
                    Advance();
                    bool frac = false;
                    while (!AtEnd && char.IsDigit(Peek)) { Advance(); frac = true; }
                    // A trailing '.' is not part of the number ('.' is a legal
                    // module symbol, so "1." must lex as "1" then ".").
                    if (!frac && !any) { _pos = save; return false; }
                    if (!frac) _pos = save;
                    else any = true;
                }
                if (!any) { _pos = start; return false; }

                if (!AtEnd && (Peek == 'e' || Peek == 'E'))
                {
                    int save = _pos;
                    Advance();
                    if (!AtEnd && (Peek == '+' || Peek == '-')) Advance();
                    bool digits = false;
                    while (!AtEnd && char.IsDigit(Peek)) { Advance(); digits = true; }
                    if (!digits) _pos = save;
                }

                string text = new string(_src, start, _pos - start);
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    _pos = start;
                    return false;
                }
                return true;
            }

            void Error(int line, int column, string message)
            {
                // One error per position: cascades from a single bad token are
                // noise, and the first one is the one that is actually useful.
                for (int i = 0; i < _errors.Count; i++)
                    if (_errors[i].Line == line && _errors[i].Column == column) return;
                _errors.Add(new LSystemParseError { Line = line, Column = column, Message = message });
            }

            // Blank out // and /* */ comments in place, preserving newlines and
            // total length so every position still maps to the original text.
            // Doing this up front is why nothing below has to think about
            // comments -- and it is necessary anyway, because '/' is also the
            // roll-right turtle symbol and only "//" starts a comment.
            static char[] StripComments(string src)
            {
                var buf = src.ToCharArray();
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i] == '/' && i + 1 < buf.Length && buf[i + 1] == '/')
                    {
                        while (i < buf.Length && buf[i] != '\n') buf[i++] = ' ';
                        i--;
                    }
                    else if (buf[i] == '/' && i + 1 < buf.Length && buf[i + 1] == '*')
                    {
                        int j = i;
                        while (j < buf.Length && !(buf[j] == '*' && j + 1 < buf.Length && buf[j + 1] == '/'))
                        {
                            if (buf[j] != '\n') buf[j] = ' ';
                            j++;
                        }
                        if (j < buf.Length) { buf[j] = ' '; buf[j + 1] = ' '; j += 2; }
                        i = j - 1;
                    }
                }
                return buf;
            }
        }
    }
}
