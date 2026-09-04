using System;

namespace LSystems
{
    // Compiled arithmetic expression, evaluated against a flat float[] of bound
    // parameters. Built once by the parser, evaluated millions of times by the
    // rewriter, so: no dictionaries at eval time (names are resolved to slot
    // indices at parse time), no boxing, no allocation.
    //
    // Constant subexpressions are folded at construction, which is why a
    // #define costs nothing at runtime and why a non-parametric grammar's
    // successors evaluate straight to literals.
    public abstract class LExpr
    {
        public abstract float Eval(float[] args, ref LRandom rng);

        // True when the value cannot depend on parameters or on the RNG.
        public virtual bool IsConstant => false;

        public static readonly LExpr Zero = new LConst(0f);

        public static LExpr Constant(float v) => new LConst(v);

        public static LExpr Arg(int slot) => new LArg(slot);

        // All construction goes through these so folding happens in one place.
        public static LExpr Unary(LUnaryOp op, LExpr a)
        {
            var e = new LUnary(op, a);
            return a.IsConstant ? Fold(e) : e;
        }

        public static LExpr Binary(LBinaryOp op, LExpr a, LExpr b)
        {
            var e = new LBinary(op, a, b);
            return a.IsConstant && b.IsConstant ? Fold(e) : e;
        }

        public static LExpr Call(LFunc func, LExpr[] args)
        {
            var e = new LCall(func, args);
            if (func == LFunc.Rand) return e;              // never fold randomness
            for (int i = 0; i < args.Length; i++)
                if (!args[i].IsConstant) return e;
            return Fold(e);
        }

        static LExpr Fold(LExpr e)
        {
            var rng = new LRandom(0);
            return new LConst(e.Eval(null, ref rng));
        }
    }

    public enum LUnaryOp { Negate, Not }

    public enum LBinaryOp
    {
        Add, Sub, Mul, Div, Mod, Pow,
        Less, LessEqual, Greater, GreaterEqual, Equal, NotEqual,
        And, Or
    }

    public enum LFunc
    {
        Sin, Cos, Tan, Asin, Acos, Atan, Atan2,
        Sqrt, Abs, Floor, Ceil, Round, Sign, Exp, Log,
        Min, Max, Pow, Clamp, Lerp, Step, Rand
    }

    sealed class LConst : LExpr
    {
        readonly float _v;
        public LConst(float v) { _v = v; }
        public override bool IsConstant => true;
        public override float Eval(float[] args, ref LRandom rng) => _v;
        public override string ToString() => _v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    sealed class LArg : LExpr
    {
        readonly int _slot;
        public LArg(int slot) { _slot = slot; }
        public override float Eval(float[] args, ref LRandom rng) => args[_slot];
        public override string ToString() => "$" + _slot;
    }

    sealed class LUnary : LExpr
    {
        readonly LUnaryOp _op;
        readonly LExpr _a;
        public LUnary(LUnaryOp op, LExpr a) { _op = op; _a = a; }
        public override float Eval(float[] args, ref LRandom rng)
        {
            float a = _a.Eval(args, ref rng);
            return _op == LUnaryOp.Negate ? -a : (a != 0f ? 0f : 1f);
        }
    }

    sealed class LBinary : LExpr
    {
        readonly LBinaryOp _op;
        readonly LExpr _a, _b;
        public LBinary(LBinaryOp op, LExpr a, LExpr b) { _op = op; _a = a; _b = b; }

        public override float Eval(float[] args, ref LRandom rng)
        {
            // Short-circuit before evaluating the right side. Two reasons: a
            // guard like "x != 0 && 1/x < 5" has to be safe, and a
            // short-circuited rand() must not consume a draw -- otherwise the
            // RNG stream depends on branch outcomes and determinism gets subtle.
            if (_op == LBinaryOp.And)
                return _a.Eval(args, ref rng) != 0f && _b.Eval(args, ref rng) != 0f ? 1f : 0f;
            if (_op == LBinaryOp.Or)
                return _a.Eval(args, ref rng) != 0f || _b.Eval(args, ref rng) != 0f ? 1f : 0f;

            float a = _a.Eval(args, ref rng);
            float b = _b.Eval(args, ref rng);
            switch (_op)
            {
                case LBinaryOp.Add: return a + b;
                case LBinaryOp.Sub: return a - b;
                case LBinaryOp.Mul: return a * b;
                case LBinaryOp.Div: return b == 0f ? 0f : a / b;
                case LBinaryOp.Mod: return b == 0f ? 0f : a % b;
                case LBinaryOp.Pow: return (float)Math.Pow(a, b);
                case LBinaryOp.Less: return a < b ? 1f : 0f;
                case LBinaryOp.LessEqual: return a <= b ? 1f : 0f;
                case LBinaryOp.Greater: return a > b ? 1f : 0f;
                case LBinaryOp.GreaterEqual: return a >= b ? 1f : 0f;
                case LBinaryOp.Equal: return a == b ? 1f : 0f;
                case LBinaryOp.NotEqual: return a != b ? 1f : 0f;
                default: return 0f;
            }
        }
    }

    sealed class LCall : LExpr
    {
        readonly LFunc _f;
        readonly LExpr[] _args;
        public LCall(LFunc f, LExpr[] args) { _f = f; _args = args; }

        public override float Eval(float[] args, ref LRandom rng)
        {
            if (_f == LFunc.Rand)
            {
                // rand() -> [0,1)   rand(hi) -> [0,hi)   rand(lo,hi) -> [lo,hi)
                float r = rng.NextFloat();
                if (_args.Length == 0) return r;
                if (_args.Length == 1) return r * _args[0].Eval(args, ref rng);
                float lo = _args[0].Eval(args, ref rng);
                float hi = _args[1].Eval(args, ref rng);
                return lo + (hi - lo) * r;
            }

            float a = _args.Length > 0 ? _args[0].Eval(args, ref rng) : 0f;
            float b = _args.Length > 1 ? _args[1].Eval(args, ref rng) : 0f;
            float c = _args.Length > 2 ? _args[2].Eval(args, ref rng) : 0f;
            switch (_f)
            {
                case LFunc.Sin: return (float)Math.Sin(a);
                case LFunc.Cos: return (float)Math.Cos(a);
                case LFunc.Tan: return (float)Math.Tan(a);
                case LFunc.Asin: return (float)Math.Asin(a < -1f ? -1f : a > 1f ? 1f : a);
                case LFunc.Acos: return (float)Math.Acos(a < -1f ? -1f : a > 1f ? 1f : a);
                case LFunc.Atan: return (float)Math.Atan(a);
                case LFunc.Atan2: return (float)Math.Atan2(a, b);
                case LFunc.Sqrt: return a <= 0f ? 0f : (float)Math.Sqrt(a);
                case LFunc.Abs: return Math.Abs(a);
                case LFunc.Floor: return (float)Math.Floor(a);
                case LFunc.Ceil: return (float)Math.Ceiling(a);
                case LFunc.Round: return (float)Math.Round(a, MidpointRounding.AwayFromZero);
                case LFunc.Sign: return a > 0f ? 1f : a < 0f ? -1f : 0f;
                case LFunc.Exp: return (float)Math.Exp(a);
                case LFunc.Log: return a <= 0f ? 0f : (float)Math.Log(a);
                case LFunc.Min: return Math.Min(a, b);
                case LFunc.Max: return Math.Max(a, b);
                case LFunc.Pow: return (float)Math.Pow(a, b);
                case LFunc.Clamp: return a < b ? b : a > c ? c : a;
                case LFunc.Lerp: return a + (b - a) * (c < 0f ? 0f : c > 1f ? 1f : c);
                case LFunc.Step: return a >= b ? 1f : 0f;
                default: return 0f;
            }
        }
    }

    // Name/arity table, shared by the parser (to resolve calls) and by the
    // syntax reference in README.md. Keep the two in step.
    public static class LFuncTable
    {
        public static bool TryResolve(string name, out LFunc func, out int minArgs, out int maxArgs)
        {
            switch (name)
            {
                case "sin": func = LFunc.Sin; minArgs = maxArgs = 1; return true;
                case "cos": func = LFunc.Cos; minArgs = maxArgs = 1; return true;
                case "tan": func = LFunc.Tan; minArgs = maxArgs = 1; return true;
                case "asin": func = LFunc.Asin; minArgs = maxArgs = 1; return true;
                case "acos": func = LFunc.Acos; minArgs = maxArgs = 1; return true;
                case "atan": func = LFunc.Atan; minArgs = maxArgs = 1; return true;
                case "atan2": func = LFunc.Atan2; minArgs = maxArgs = 2; return true;
                case "sqrt": func = LFunc.Sqrt; minArgs = maxArgs = 1; return true;
                case "abs": func = LFunc.Abs; minArgs = maxArgs = 1; return true;
                case "floor": func = LFunc.Floor; minArgs = maxArgs = 1; return true;
                case "ceil": func = LFunc.Ceil; minArgs = maxArgs = 1; return true;
                case "round": func = LFunc.Round; minArgs = maxArgs = 1; return true;
                case "sign": func = LFunc.Sign; minArgs = maxArgs = 1; return true;
                case "exp": func = LFunc.Exp; minArgs = maxArgs = 1; return true;
                case "log": func = LFunc.Log; minArgs = maxArgs = 1; return true;
                case "min": func = LFunc.Min; minArgs = maxArgs = 2; return true;
                case "max": func = LFunc.Max; minArgs = maxArgs = 2; return true;
                case "pow": func = LFunc.Pow; minArgs = maxArgs = 2; return true;
                case "clamp": func = LFunc.Clamp; minArgs = maxArgs = 3; return true;
                case "lerp": func = LFunc.Lerp; minArgs = maxArgs = 3; return true;
                case "step": func = LFunc.Step; minArgs = maxArgs = 2; return true;
                case "rand": func = LFunc.Rand; minArgs = 0; maxArgs = 2; return true;
                default: func = default; minArgs = maxArgs = 0; return false;
            }
        }
    }
}
