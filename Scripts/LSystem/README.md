# L-systems

A grammar engine for anything in this project whose shape is *branching* rather
than *field-based*: plants, cave and tunnel networks, river and canyon systems,
coral, crystals, lightning.

## The shape of it

Three layers, each usable without the ones after it:

```
grammar text  --LSystemParser-->  LSystemGrammar
LSystemGrammar --LSystemRewriter-> LModuleString   (a word: symbols + params)
LModuleString --TurtleInterpreter-> LSkeleton      (nodes, segments, markers)
LSkeleton     --> your consumer                    (mesh, SDF carve, spawner...)
```

The **rewriter is domain-blind**: it knows productions and nothing else. The
**interpreter decides what symbols mean**, and the turtle is only the first one.
The pivot is `LSkeleton` — a branching structure in space, which a plant mesher
and a cave carver want equally and neither should re-derive.

`LSystemParser`, `LSystemRewriter` and everything they touch are plain C# with
no `UnityEngine` dependency, so they can move into a job or be unit-tested
without an editor. Only the turtle, the skeleton and the asset need Unity.

### Why this folder has an .asmdef

It is the only assembly definition in the project, and it exists because a test
assembly cannot reference the predefined `Assembly-CSharp` — without an asmdef
here there is nowhere to put EditMode tests. The dependency runs one way:
`Assembly-CSharp` auto-references `TerrainGen.LSystem`, so the rest of the
project can use these types freely, but nothing in here can reach back out to
`DensityField` and friends. That is the right direction anyway — a grammar
engine that knows about terrain is not a grammar engine — and it means a future
skeleton-to-SDF carver belongs in `Scripts/Terrain`, not in this folder.

### Where the terrain fits

L-systems produce skeletons — curves and branch points. That makes them a good
fit for tunnels, rivers and crevasses (union capsule SDFs along the segments
into a `ModifiedDensityField`), and a poor fit for general terrain *surfaces*,
which the existing noise + biome-blend stack already does better. Reach for a
grammar when the thing you want has a topology, not when it has a height.

## Syntax

cpfg notation, from Prusinkiewicz & Lindenmayer's *The Algorithmic Beauty of
Plants*. Grammars from the book and the literature paste in and run.

```
// line comment              /* block comment */

#define R 1.456              // constant, folded at parse time; may use earlier defines
#axiom: A(1)                 // the starting word (required)
#ignore: + - [ ]             // symbols context matching looks straight through
#iterations: 5               // default derivation depth; the caller may override

A(s) -> F(s) [ +(25.7) A(s/R) ] [ -(25.7) A(s/R) ]
B(w) : 0.6 -> F(w) B(w*0.9)              // stochastic: relative weight
C(x) : x > 0.1 -> F(x) C(x*0.8)          // conditional
D(x) : x > 0.1 : 0.4 -> F(x)             // both; several ':' conditions AND together
A(x) < B(y) > C(z) -> B(y+x)             // context sensitive (left < pred > right)
```

**Symbols are single characters.** `FF` is two modules, not one named `FF`.
That is what makes textbook grammars work verbatim; it is not an oversight.

**A module's identity is its symbol *and* its arity.** `A -> B` does not fire on
`A(1)`, and vice versa.

**A symbol with no matching rule carries through unchanged**, which is why
turtle commands need no `F -> F`.

**A `:` clause that is nothing but a number is a probability weight**; anything
else is a condition. Weights are relative and need not sum to 1, so adding a
variant does not mean renumbering the others.

**A rule ends at the end of the line**, unless a `[` is still open — so
multi-line rules work as long as the break falls inside a branch. (There is no
line-continuation character: `\` is already the roll-left turtle symbol.)

**`#` `(` `)` `,` `:` `<` `>` cannot be module symbols.** Everything else can,
including `+ - * / \ & ^ | ! " $ [ ]`.

### Expressions

Available in module parameters, conditions and `#define`.

- `+ - * / % ^` (`^` is power, right-associative and tighter than unary minus)
- `< <= > >= == !=`, `&& || !` — comparisons yield 1 or 0
- `sin cos tan asin acos atan atan2 sqrt abs floor ceil round sign exp log`
- `min max pow clamp lerp step`
- `rand()` → `[0,1)`, `rand(hi)` → `[0,hi)`, `rand(lo,hi)` → `[lo,hi)`
- `pi`, `e`
- Division and `log`/`sqrt` of non-positive numbers yield 0 rather than NaN: a
  grammar is authored text, and a typo should give a wrong plant, not a
  corrupt mesh.

### Turtle commands

`n` below means the module's first parameter, or the `TurtleSettings` default
when it has none.

| | |
|---|---|
| `F(n)` `G(n)` | forward `n`, laying down a segment |
| `f(n)` `g(n)` | forward `n` without drawing (starts a detached run) |
| `+(a)` `-(a)` | turn left / right, about the turtle's up |
| `&(a)` `^(a)` | pitch down / up, about the turtle's left |
| `\(a)` `/(a)` | roll left / right, about the heading |
| `\|` | turn 180 degrees |
| `$` | roll upright: keep the heading, put the turtle's up as close to world up as it can go |
| `[` `]` | push / pop turtle state |
| `!(w)` | set width; bare `!` multiplies by `widthFactor` |
| `"(s)` | set step length; bare `"` multiplies by `lengthFactor` |

**Every other symbol becomes a marker** in the skeleton, carrying its
parameters, at the turtle's current position and orientation. That is the
extension point: `L(0.4)` for a leaf, `K` for a crystal seed, `W(2)` for a
tunnel widening. Adding a decoration type costs a line in a grammar and a case
in your consumer — nothing in this folder changes.

## Using it

```csharp
// The whole pipeline, from an asset:
LSkeleton skeleton = grammarAsset.BuildSkeleton(TurtleSettings.Default, seed: 1234u);

foreach (var seg in skeleton.Segments)
{
    Vector3 a = skeleton.Nodes[seg.From].Position;
    Vector3 b = skeleton.Nodes[seg.To].Position;
    // sweep a cylinder, union a capsule SDF, spawn a prop...
}
```

```csharp
// Or the layers separately, when you want the word itself:
LSystemParser.TryParse(text, out LSystemGrammar grammar, out var errors);
LModuleString word = new LSystemRewriter().Rewrite(grammar, iterations: 6, seed: 1234u);
```

Drop an `LSystemPreview` component on an empty GameObject to see a grammar in
the scene view while you edit it.

## Determinism and cost

The RNG is a seeded PCG32 owned by the rewriter, never `UnityEngine.Random`:
the same `(grammar, iterations, seed, sequence)` always gives the same word, so
a plant that streams out and back in comes back identical. `sequence` gives each
plant its own stream from one world seed — `new LRandom(worldSeed, plantId)`.

A rewriter reuses its buffers across runs, so generating many plants allocates
once, not once per plant. It is not thread-safe: give each worker its own.

`LSystemRewriter.MaxModules` (default 250k) caps the word length. On overflow
the rewriter returns the last *complete* generation and sets `Truncated` — it
never hands out a half-derived word. Branching grammars grow exponentially;
raise the cap deliberately.

## Not here yet

- Mesh generation. `LSkeleton` is the handoff point; a generalized-cylinder
  builder is the obvious next consumer.
- The SDF carver that would let a grammar cut caves into a `DensityField`.
- Polygon modules (`{ . . . }` in cpfg) for leaf blades. Markers plus a prefab
  or a quad cover most of what those are used for.
