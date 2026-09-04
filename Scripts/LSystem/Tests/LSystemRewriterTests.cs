using NUnit.Framework;

namespace LSystems.Tests
{
    public class LSystemRewriterTests
    {
        static LSystemGrammar Parse(string source)
        {
            bool ok = LSystemParser.TryParse(source, out LSystemGrammar g, out var errors);
            if (!ok)
            {
                var sb = new System.Text.StringBuilder("grammar did not parse:");
                for (int i = 0; i < errors.Count; i++) sb.Append('\n').Append(errors[i]);
                Assert.Fail(sb.ToString());
            }
            return g;
        }

        static string Symbols(string source, int iterations, uint seed = 0u)
            => LSystemRewriter.Run(Parse(source), iterations, seed).ToSymbolString();

        static string Display(string source, int iterations, uint seed = 0u)
            => LSystemRewriter.Run(Parse(source), iterations, seed).ToDisplayString();

        // ---- derivation ----------------------------------------------------

        [Test]
        public void AlgaeGrowsThroughTheTextbookSequence()
        {
            // Lindenmayer's original 1968 algae model. If this ever breaks, the
            // rewriter is wrong in a way no amount of pretty plants will hide.
            const string src = "#axiom: A\nA -> AB\nB -> A\n";
            Assert.AreEqual("A", Symbols(src, 0));
            Assert.AreEqual("AB", Symbols(src, 1));
            Assert.AreEqual("ABA", Symbols(src, 2));
            Assert.AreEqual("ABAAB", Symbols(src, 3));
            Assert.AreEqual("ABAABABA", Symbols(src, 4));
            Assert.AreEqual("ABAABABAABAAB", Symbols(src, 5));
        }

        [Test]
        public void SymbolsWithNoRuleCarryThroughUnchanged()
        {
            // Turtle commands survive derivation without every grammar having
            // to write F -> F.
            Assert.AreEqual("F+F+F", Symbols("#axiom: F+F+F\nA -> B\n", 3));
        }

        [Test]
        public void ParametersAreRecomputedEachStep()
        {
            Assert.AreEqual("A(8)", Display("#axiom: A(1)\nA(x) -> A(x*2)\n", 3));
        }

        [Test]
        public void ArityIsPartOfModuleIdentity()
        {
            // "A -> B" must not fire on "A(1)": in a parametric system a module
            // is its symbol AND its arity, or grammars silently cross-talk.
            Assert.AreEqual("A(1) B", Display("#axiom: A(1) A\nA -> B\n", 1));
            Assert.AreEqual("CA", Symbols("#axiom: A(1) A\nA(x) -> C\n", 1));
        }

        [Test]
        public void ConditionStopsTheRewrite()
        {
            Assert.AreEqual("A(3)", Display("#axiom: A(0)\nA(x) : x < 3 -> A(x+1)\n", 10));
        }

        [Test]
        public void ConditionCanSelectBetweenRules()
        {
            const string src =
                "#axiom: A(0)\n" +
                "A(x) : x < 2 -> A(x+1)\n" +
                "A(x) : x >= 2 -> F\n";
            Assert.AreEqual("A(1)", Display(src, 1));
            Assert.AreEqual("A(2)", Display(src, 2));
            Assert.AreEqual("F", Display(src, 3));
        }

        // ---- stochastic ----------------------------------------------------

        [Test]
        public void SameSeedGivesTheSameResult()
        {
            const string src = "#axiom: A\nA : 1 -> AB\nA : 1 -> AC\n";
            Assert.AreEqual(Symbols(src, 12, 4242u), Symbols(src, 12, 4242u));
        }

        [Test]
        public void DifferentSeedsGiveDifferentResults()
        {
            // 12 independent coin flips: a collision is a 1-in-4096 event, so
            // this is a real signal rather than a flaky one.
            const string src = "#axiom: A\nA : 1 -> AB\nA : 1 -> AC\n";
            Assert.AreNotEqual(Symbols(src, 12, 1u), Symbols(src, 12, 2u));
        }

        [Test]
        public void WeightsSelectInProportion()
        {
            // 3:1 over 2000 draws from one stream. Binomial sd here is ~1%, so
            // a 4-point window is loose enough never to flake and tight enough
            // to catch the weights being ignored or inverted.
            LSystemGrammar g = Parse("#axiom: A\nA : 3 -> AB\nA : 1 -> AC\n");
            string word = LSystemRewriter.Run(g, 2000, 7u).ToSymbolString();

            int b = 0, c = 0;
            foreach (char ch in word)
            {
                if (ch == 'B') b++;
                else if (ch == 'C') c++;
            }
            Assert.AreEqual(2000, b + c);
            float ratio = b / (float)(b + c);
            Assert.That(ratio, Is.InRange(0.71f, 0.79f), "expected about 75% B, got " + ratio);
        }

        [Test]
        public void RandInSuccessorIsSeedDeterministic()
        {
            const string src = "#axiom: A\nA -> A F(rand(1,2))\n";
            Assert.AreEqual(Display(src, 6, 99u), Display(src, 6, 99u));
            Assert.AreNotEqual(Display(src, 6, 99u), Display(src, 6, 100u));
        }

        [Test]
        public void ZeroWeightRuleNeverFires()
        {
            Assert.AreEqual("A", Symbols("#axiom: A\nA : 0 -> B\n", 5));
        }

        // ---- context sensitivity -------------------------------------------

        [Test]
        public void LeftContextPropagatesASignal()
        {
            // The classic 1L signal: B walks rightwards one module per step.
            const string src = "#axiom: BAAAA\nB < A -> B\nB -> A\n";
            Assert.AreEqual("ABAAA", Symbols(src, 1));
            Assert.AreEqual("AABAA", Symbols(src, 2));
            Assert.AreEqual("AAABA", Symbols(src, 3));
        }

        [Test]
        public void IgnoredSymbolsAreInvisibleToContext()
        {
            Assert.AreEqual("B+A", Symbols("#axiom: B+A\nB < A -> C\n", 1),
                "without #ignore, '+' blocks the context");
            Assert.AreEqual("B+C", Symbols("#ignore: +\n#axiom: B+A\nB < A -> C\n", 1));
        }

        [Test]
        public void LeftContextSkipsWholeBranches()
        {
            // Brackets make the word a tree: A's left neighbour is B, not the
            // X sitting in a finished sibling branch.
            Assert.AreEqual("B[X]C", Symbols("#axiom: B[X]A\nB < A -> C\n", 1));
            Assert.AreEqual("B[X]A", Symbols("#axiom: B[X]A\nX < A -> C\n", 1));
        }

        [Test]
        public void LeftContextReachesOutOfABranch()
        {
            // Inside a branch, the left neighbour is the module before the '['.
            Assert.AreEqual("B[C]", Symbols("#axiom: B[A]\nB < A -> C\n", 1));
        }

        [Test]
        public void RightContextTriesBranchesAndTheTrunk()
        {
            Assert.AreEqual("X[B]C", Symbols("#axiom: A[B]C\nA > B -> X\n", 1), "into the branch");
            Assert.AreEqual("Y[B]C", Symbols("#axiom: A[B]C\nA > C -> Y\n", 1), "past the branch");
            Assert.AreEqual("A[B]C", Symbols("#axiom: A[B]C\nA > D -> Z\n", 1), "neither");
        }

        [Test]
        public void RightContextCannotEscapeUpwards()
        {
            // A inside a branch must not see the C that follows the ']'.
            Assert.AreEqual("[A]C", Symbols("#axiom: [A]C\nA > C -> Z\n", 1));
        }

        [Test]
        public void ContextParametersBindIntoTheSuccessor()
        {
            Assert.AreEqual("A(2) B(5) C(3)", Display("#axiom: A(2)B(0)C(3)\nA(x) < B(y) > C(z) -> B(x+z)\n", 1));
        }

        // ---- limits --------------------------------------------------------

        [Test]
        public void TruncationStopsAtTheLastCompleteGeneration()
        {
            // The point of the cap is that the caller still gets a *valid*
            // word, not a half-derived one.
            var rewriter = new LSystemRewriter { MaxModules = 100 };
            LModuleString word = rewriter.Rewrite(Parse("#axiom: A\nA -> AA\n"), 20);

            Assert.IsTrue(rewriter.Truncated);
            Assert.AreEqual(6, rewriter.CompletedIterations);
            Assert.AreEqual(64, word.Count, "2^6, the last generation that fit");
        }

        [Test]
        public void ReusingARewriterDoesNotLeakStateBetweenRuns()
        {
            var rewriter = new LSystemRewriter();
            LSystemGrammar algae = Parse("#axiom: A\nA -> AB\nB -> A\n");
            LSystemGrammar koch = Parse("#axiom: F\nF -> F+F-F\n");

            Assert.AreEqual("ABAAB", rewriter.Rewrite(algae, 3).ToSymbolString());
            Assert.AreEqual("F+F-F", rewriter.Rewrite(koch, 1).ToSymbolString());
            Assert.AreEqual("ABAAB", rewriter.Rewrite(algae, 3).ToSymbolString());
            Assert.IsFalse(rewriter.Truncated);
        }

        [Test]
        public void CopyResultSurvivesTheNextRun()
        {
            var rewriter = new LSystemRewriter();
            rewriter.Rewrite(Parse("#axiom: A\nA -> AB\nB -> A\n"), 3);
            LModuleString kept = rewriter.CopyResult();

            rewriter.Rewrite(Parse("#axiom: F\nF -> FF\n"), 4);
            Assert.AreEqual("ABAAB", kept.ToSymbolString());
        }
    }
}
