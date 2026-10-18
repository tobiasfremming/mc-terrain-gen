// The NUnit subset the creature tests use, for the offline harness (Program.cs discovers and runs them).
// Semantics follow NUnit 3 where the tests depend on them: Assert.Inconclusive / Ignore end a test without
// failing it, [TestCase] rows and [TestFixtureSource] fixtures expand into one test each.
using System;
using System.Collections;
using System.IO;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public class TestAttribute : Attribute { public string Description; }
    [AttributeUsage(AttributeTargets.Method)] public class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class OneTimeSetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class OneTimeTearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class CategoryAttribute : Attribute { public string Name; public CategoryAttribute(string name) { Name = name; } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class ExplicitAttribute : Attribute { public ExplicitAttribute() { } public ExplicitAttribute(string reason) { } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class IgnoreAttribute : Attribute { public string Reason; public IgnoreAttribute(string reason) { Reason = reason; } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class TimeoutAttribute : Attribute { public TimeoutAttribute(int ms) { } }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseAttribute : Attribute
    {
        public object[] Arguments;
        public string TestName;
        public TestCaseAttribute(params object[] args) { Arguments = args ?? new object[] { null }; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class TestFixtureSourceAttribute : Attribute
    {
        public Type SourceType; public string SourceName;
        public TestFixtureSourceAttribute(string sourceName) { SourceName = sourceName; }
        public TestFixtureSourceAttribute(Type sourceType, string sourceName) { SourceType = sourceType; SourceName = sourceName; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseSourceAttribute : Attribute
    {
        public Type SourceType; public string SourceName;
        public TestCaseSourceAttribute(string sourceName) { SourceName = sourceName; }
        public TestCaseSourceAttribute(Type sourceType, string sourceName) { SourceType = sourceType; SourceName = sourceName; }
    }

    public class AssertionException : Exception { public AssertionException(string m) : base(m) { } }
    public class InconclusiveException : Exception { public InconclusiveException(string m) : base(m) { } }
    public class IgnoreException : Exception { public IgnoreException(string m) : base(m) { } }

    public static class TestContext
    {
        public static TextWriter Out => Console.Out;
        public static TextWriter Progress => Console.Out;
        public static void WriteLine(string s) => Console.Out.WriteLine(s);
        public static void WriteLine(object o) => Console.Out.WriteLine(o);
    }

    public sealed class Constraint
    {
        internal Func<object, bool> ok; internal string desc;
        public Constraint(Func<object, bool> ok, string desc) { this.ok = ok; this.desc = desc; }
    }

    public static class Is
    {
        static double D(object o) => Convert.ToDouble(o);
        public static Constraint InRange(double a, double b) => new Constraint(o => D(o) >= a && D(o) <= b, $"in [{a}, {b}]");
        public static Constraint EqualTo(object e) => new Constraint(o => Equals(o, e) || Assert.NumEq(e, o), $"equal to {e}");
        public static Constraint GreaterThan(double e) => new Constraint(o => D(o) > e, $"> {e}");
        public static Constraint LessThan(double e) => new Constraint(o => D(o) < e, $"< {e}");
        public static Constraint GreaterThanOrEqualTo(double e) => new Constraint(o => D(o) >= e, $">= {e}");
        public static Constraint LessThanOrEqualTo(double e) => new Constraint(o => D(o) <= e, $"<= {e}");
        public static Constraint Null => new Constraint(o => o == null, "null");
        public static Constraint True => new Constraint(o => o is bool b && b, "true");
        public static Constraint False => new Constraint(o => o is bool b && !b, "false");
        public static Constraint Empty => new Constraint(o => o is ICollection c ? c.Count == 0 : o is string s && s.Length == 0, "empty");
        public static class Not
        {
            public static Constraint Null => new Constraint(o => o != null, "not null");
            public static Constraint Empty => new Constraint(o => !(o is ICollection c ? c.Count == 0 : o is string s && s.Length == 0), "not empty");
            public static Constraint EqualTo(object e) => new Constraint(o => !Equals(o, e), $"not equal to {e}");
        }
        static bool IsNum(object o) => o is float || o is double || o is int || o is long || o is uint || o is ulong || o is short || o is byte;
    }

    public static class Assert
    {
        static void F(string m) => throw new AssertionException(m);
        static string M(string m, object[] a) => a != null && a.Length > 0 ? string.Format(m, a) : m;
        public static void Fail(string m = "") => F(m);
        public static void Pass(string m = "") { }
        public static void Inconclusive(string m = "") => throw new InconclusiveException(m);
        public static void Ignore(string m = "") => throw new IgnoreException(m);
        public static void That(bool c, string m = "") { if (!c) F("expected true: " + m); }
        public static void That(object v, Constraint c, string m = "") { if (!c.ok(v)) F($"expected {c.desc} but was {v}: {m}"); }
        public static void IsTrue(bool c, string m = "", params object[] a) { if (!c) F("expected true: " + M(m, a)); }
        public static void IsFalse(bool c, string m = "", params object[] a) { if (c) F("expected false: " + M(m, a)); }
        public static void True(bool c, string m = "") => IsTrue(c, m);
        public static void False(bool c, string m = "") => IsFalse(c, m);
        public static void IsNull(object o, string m = "", params object[] a) { if (o != null) F($"expected null but was <{o}>: {M(m, a)}"); }
        public static void IsNotNull(object o, string m = "", params object[] a) { if (o == null) F("expected not null: " + M(m, a)); }
        public static void NotNull(object o, string m = "") => IsNotNull(o, m);
        public static void Null(object o, string m = "") => IsNull(o, m);
        public static void IsEmpty(ICollection c, string m = "") { if (c.Count != 0) F($"expected empty, {c.Count} items: {m}"); }
        public static void IsNotEmpty(ICollection c, string m = "") { if (c.Count == 0) F("expected not empty: " + m); }
        public static void IsEmpty(string s, string m = "") { if (!string.IsNullOrEmpty(s)) F($"expected empty but was <{s}>: {m}"); }
        public static void AreEqual(double e, double a, double tol, string m = "", params object[] x) { if (!(Math.Abs(e - a) <= tol)) F($"expected {e} +- {tol} but was {a}: {M(m, x)}"); }
        public static void AreEqual(object e, object a, string m = "", params object[] x)
        {
            bool eq = Equals(e, a) || NumEq(e, a);
            if (!eq) F($"expected <{e}> but was <{a}>: {M(m, x)}");
        }
        public static void AreNotEqual(object e, object a, string m = "", params object[] x) { if (Equals(e, a)) F($"expected not <{e}>: {M(m, x)}"); }
        public static void AreSame(object e, object a, string m = "") { if (!ReferenceEquals(e, a)) F("expected the same object: " + m); }
        public static void AreNotSame(object e, object a, string m = "") { if (ReferenceEquals(e, a)) F("expected different objects: " + m); }
        public static void Greater(double a, double b, string m = "", params object[] x) { if (!(a > b)) F($"expected greater than {b} but was {a}: {M(m, x)}"); }
        public static void GreaterOrEqual(double a, double b, string m = "", params object[] x) { if (!(a >= b)) F($"expected >= {b} but was {a}: {M(m, x)}"); }
        public static void Less(double a, double b, string m = "", params object[] x) { if (!(a < b)) F($"expected less than {b} but was {a}: {M(m, x)}"); }
        public static void LessOrEqual(double a, double b, string m = "", params object[] x) { if (!(a <= b)) F($"expected <= {b} but was {a}: {M(m, x)}"); }
        public static void Zero(double a, string m = "") { if (a != 0) F($"expected 0 but was {a}: {m}"); }
        public static void Positive(double a, string m = "") { if (!(a > 0)) F($"expected positive but was {a}: {m}"); }
        public static T Throws<T>(TestDelegate d, string m = "") where T : Exception
        {
            try { d(); }
            catch (T e) { return e; }
            catch (Exception e) { F($"expected {typeof(T).Name} but got {e.GetType().Name}: {m}"); }
            F($"expected {typeof(T).Name} but nothing was thrown: {m}");
            return null;
        }
        public static void DoesNotThrow(TestDelegate d, string m = "")
        {
            try { d(); } catch (Exception e) { F($"unexpected {e.GetType().Name} ({e.Message}): {m}"); }
        }
        static bool Num(object o) => o is float || o is double || o is int || o is long || o is uint || o is ulong || o is short || o is byte;
        static bool Real(object o) => o is float || o is double;
        // NUnit compares mixed numeric types by value. Integers compare exactly (decimal holds every ulong: a
        // golden hash off by one in the last bit must fail); a float or double on either side compares as double.
        internal static bool NumEq(object e, object a)
        {
            if (!Num(e) || !Num(a)) return false;
            if (Real(e) || Real(a)) return Convert.ToDouble(e) == Convert.ToDouble(a);
            return Convert.ToDecimal(e) == Convert.ToDecimal(a);
        }
    }

    public delegate void TestDelegate();
}

namespace NUnit.Framework
{
    public static class StringAssert
    {
        public static void Contains(string expected, string actual, string m = "") { if (actual == null || !actual.Contains(expected)) throw new AssertionException($"expected a string containing <{expected}> but was <{actual}>: {m}"); }
        public static void StartsWith(string expected, string actual, string m = "") { if (actual == null || !actual.StartsWith(expected)) throw new AssertionException($"expected a string starting with <{expected}> but was <{actual}>: {m}"); }
        public static void DoesNotContain(string expected, string actual, string m = "") { if (actual != null && actual.Contains(expected)) throw new AssertionException($"expected a string not containing <{expected}> but was <{actual}>: {m}"); }
    }
}
