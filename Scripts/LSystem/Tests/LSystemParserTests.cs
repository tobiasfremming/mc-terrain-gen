using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace LSystems.Tests
{
    public class LSystemParserTests
    {
        static LSystemGrammar Parse(string source)
        {
            bool ok = LSystemParser.TryParse(source, out LSystemGrammar g, out var errors);
            Assert.IsTrue(ok, "expected a clean parse but got:\n" + Describe(errors));
            return g;
        }

        static IReadOnlyList<LSystemParseError> ParseErrors(string source)
        {
            LSystemParser.TryParse(source, out _, out var errors);
            return errors;
        }

        static string Describe(IReadOnlyList<LSystemParseError> errors)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < errors.Count; i++) sb.AppendLine(errors[i].ToString());
            return sb.ToString();
        }

        // Evaluates a grammar's axiom to a string, which is the cheapest way to
        // check that expressions were parsed and folded correctly.
        static string Axiom(string source) => Parse(source).Axiom.ToDisplayString();

        [Test]
        public void SingleCharactersAreSeparateModules()
        {
            // The whole reason symbols are one character: textbook grammars
            // write FF meaning two forward steps, not a module named FF.
            LSystemGrammar g = Parse("#axiom: FF+F\n");
            Assert.AreEqual(4, g.Axiom.Count);
            Assert.AreEqual("FF+F", g.Axiom.ToSymbolString());
        }

        [Test]
        public void AxiomKeepsParameters()
        {
            LSystemGrammar g = Parse("#axiom: A(1, 2.5) B\n");
            Assert.AreEqual(2, g.Axiom.Count);
            Assert.AreEqual(2, g.Axiom[0].ParamCount);
            Assert.AreEqual(2.5f, g.Axiom.GetParam(0, 1), 1e-5f);
            Assert.AreEqual(0, g.Axiom[1].ParamCount);
        }

        [Test]
        public void CommentsAreStripped()
        {
            LSystemGrammar g = Parse("// leading\n#axiom: F  // trailing\n/* block\n   comment */\nF -> FF\n");
            Assert.AreEqual("F", g.Axiom.ToSymbolString());
            Assert.AreEqual(1, g.Productions.Length);
        }

        [Test]
        public void SlashIsOnlyACommentWhenDoubled()
        {
            // '/' is the roll-right turtle symbol, so a lone one must survive.
            LSystemGrammar g = Parse("#axiom: /(90) F\n");
            Assert.AreEqual("/F", g.Axiom.ToSymbolString());
        }

        [Test]
        public void DefinesAreFoldedIntoConstants()
        {
            Assert.AreEqual("F(2.912)", Axiom("#define R 1.456\n#axiom: F(R*2)\n"));
            Assert.AreEqual("F(3)", Axiom("#define A 1\n#define B A+2\n#axiom: F(B)\n"));
        }

        [Test]
        public void ExpressionPrecedenceMatchesArithmetic()
        {
            Assert.AreEqual("F(14)", Axiom("#axiom: F(2+3*4)\n"));
            Assert.AreEqual("F(20)", Axiom("#axiom: F((2+3)*4)\n"));
            Assert.AreEqual("F(-4)", Axiom("#axiom: F(-2^2)\n"), "unary minus must bind looser than ^");
            Assert.AreEqual("F(512)", Axiom("#axiom: F(2^3^2)\n"), "^ must be right associative");
            Assert.AreEqual("F(1)", Axiom("#axiom: F(3 > 2)\n"));
            Assert.AreEqual("F(0)", Axiom("#axiom: F(3 > 2 && 1 > 2)\n"));
        }

        [Test]
        public void FunctionsResolveAndValidateArity()
        {
            Assert.AreEqual("F(2)", Axiom("#axiom: F(sqrt(4))\n"));
            Assert.AreEqual("F(3)", Axiom("#axiom: F(clamp(9, 1, 3))\n"));

            var errors = ParseErrors("#axiom: F(sqrt(4, 5))\n");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("takes 1 argument", errors[0].Message);
        }

        [Test]
        public void ArrowIsNotSubtraction()
        {
            LSystemGrammar g = Parse("#axiom: A(5)\nA(x) -> F(x-1)\n");
            Assert.AreEqual(1, g.Productions.Length);
            Assert.AreEqual(1, g.Productions[0].Successor.Length);
        }

        [Test]
        public void NumericClauseIsAWeightAndAnythingElseIsACondition()
        {
            // The one ambiguity in the notation: ": 0.6" is a probability,
            // ": x > 1" is a guard. Resolved by lookahead, not by a keyword.
            LSystemGrammar g = Parse(
                "#axiom: A(1)\n" +
                "A(x) : 0.6 -> F\n" +
                "A(x) : x > 1 -> G\n" +
                "A(x) : x > 1 : 0.25 -> H\n");

            Assert.AreEqual(3, g.Productions.Length);

            Assert.AreEqual(0.6f, g.Productions[0].Weight, 1e-5f);
            Assert.IsNull(g.Productions[0].Condition);

            Assert.AreEqual(1f, g.Productions[1].Weight, 1e-5f);
            Assert.IsNotNull(g.Productions[1].Condition);

            Assert.AreEqual(0.25f, g.Productions[2].Weight, 1e-5f);
            Assert.IsNotNull(g.Productions[2].Condition);
        }

        [Test]
        public void ContextIsParsedOnBothSides()
        {
            LSystemGrammar g = Parse("#axiom: B\nA(x) < B(y) > C(z) -> B(y+x)\n");
            LProduction p = g.Productions[0];

            Assert.AreEqual('B', p.Symbol);
            Assert.AreEqual(1, p.ParamCount);
            Assert.AreEqual(1, p.LeftContext.Length);
            Assert.AreEqual('A', p.LeftContext[0].Symbol);
            Assert.AreEqual(1, p.RightContext.Length);
            Assert.AreEqual('C', p.RightContext[0].Symbol);
            Assert.AreEqual(3, p.SlotCount);
            Assert.IsTrue(g.HasContext);
        }

        [Test]
        public void IgnoreDirectiveCollectsSymbols()
        {
            LSystemGrammar g = Parse("#ignore: + - [ ]\n#axiom: F\n");
            Assert.IsTrue(g.IsIgnored('+'));
            Assert.IsTrue(g.IsIgnored(']'));
            Assert.IsFalse(g.IsIgnored('F'));
        }

        [Test]
        public void OpenBracketContinuesTheRuleOntoTheNextLine()
        {
            LSystemGrammar g = Parse(
                "#axiom: A\n" +
                "A -> F [\n" +
                "       +A\n" +
                "     ] [ -A ]\n");
            Assert.AreEqual(1, g.Productions.Length);
            Assert.AreEqual(9, g.Productions[0].Successor.Length, "F [ + A ] [ - A ]");
        }

        [Test]
        public void MissingAxiomIsAnError()
        {
            var errors = ParseErrors("A -> AB\n");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("#axiom", errors[0].Message);
        }

        [Test]
        public void UnclosedBracketIsReportedAtTheBracket()
        {
            var errors = ParseErrors("#axiom: A\nA -> F [ +A\n");
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(2, errors[0].Line);
            Assert.AreEqual(8, errors[0].Column);
            StringAssert.Contains("never closed", errors[0].Message);
        }

        [Test]
        public void UnmatchedCloseBracketIsReported()
        {
            var errors = ParseErrors("#axiom: A\nA -> F ] G\n");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("no matching", errors[0].Message);
        }

        [Test]
        public void UnknownParameterNameIsReported()
        {
            var errors = ParseErrors("#axiom: A(1)\nA(x) -> F(y)\n");
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(2, errors[0].Line);
            StringAssert.Contains("not a parameter", errors[0].Message);
        }

        [Test]
        public void RewritingMoreThanOneModuleIsReported()
        {
            var errors = ParseErrors("#axiom: A\nA B -> C\n");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("one module", errors[0].Message);
        }

        [Test]
        public void DuplicateParameterNameIsReported()
        {
            var errors = ParseErrors("#axiom: A\nA(x) < B(x) -> C\n");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("bound twice", errors[0].Message);
        }

        [Test]
        public void UnknownDirectiveIsReported()
        {
            var errors = ParseErrors("#axiom: A\n#wobble: 3\n");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("unknown directive", errors[0].Message);
        }

        [Test]
        public void OneBadRuleDoesNotSwallowTheNextOne()
        {
            // Error recovery is per line, so a half-typed rule still leaves the
            // rest of the grammar usable in the inspector.
            LSystemParser.TryParse("#axiom: A\nA -> F(y)\nA -> G\n", out LSystemGrammar g, out var errors);
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(1, g.Productions.Length);
            Assert.AreEqual('G', g.Productions[0].Successor[0].Symbol);
        }

        [Test]
        public void IterationsDirectiveIsCaptured()
        {
            Assert.AreEqual(7, Parse("#iterations: 7\n#axiom: A\n").DefaultIterations);
            Assert.AreEqual(-1, Parse("#axiom: A\n").DefaultIterations, "absent means 'caller decides'");
        }

        [Test]
        public void ShippedDefaultGrammarParses()
        {
            // The text in the asset's CreateAssetMenu default is the first
            // thing anyone sees; it must not be broken.
            bool ok = LSystemParser.TryParse(LSystemGrammarAsset.DefaultSource, out _, out var errors);
            Assert.IsTrue(ok, Describe(errors));
        }
    }
}
