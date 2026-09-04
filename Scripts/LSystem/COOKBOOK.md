# L-system cookbook

`README.md` in this folder is the syntax reference. This is the other half:
how to actually build something with it, and the specific things that cost time
when you don't know them.

## The three calls

```csharp
using LSystems;

// Everything, from an asset:
LSkeleton skel = grammarAsset.BuildSkeleton(TurtleSettings.Default, seed: 1234u);

// Or the layers, when you want the word:
LSystemParser.TryParse(text, out LSystemGrammar g, out var errors);
LModuleString word = new LSystemRewriter().Rewrite(g, iterations: 8, seed: 1234u);
LSkeleton skel2 = TurtleInterpreter.Build(word, TurtleSettings.Default);
```

Then walk the skeleton:

```csharp
foreach (var seg in skel.Segments)
{
    Vector3 a = skel.Nodes[seg.From].Position;
    Vector3 b = skel.Nodes[seg.To].Position;
    float ra = seg.WidthFrom * 0.5f, rb = seg.WidthTo * 0.5f;
    // sweep a cylinder / union a capsule SDF / spawn a prop
}
foreach (var mk in skel.Markers)
{
    // mk.Symbol is whatever the grammar invented: 'L' for leaf, 'K' for tip...
    // mk.Position, mk.Orientation, mk.Width, and skel.GetMarkerParam(mk, i)
}
```

## Writing a grammar: the checklist

Work through these before wondering why it looks wrong.

**Does it terminate, and does it terminate one iteration early enough?**
`Rewrite(g, N)` applies N passes, so the *N-th generation is emitted but never
rewritten*. A grammar whose apex needs 7 shrink steps to fall under its
threshold needs `#iterations: 8`, or the last generation of apex symbols
survives into the word — and since the turtle doesn't recognise them, they turn
into stray markers instead of branch tips. Cheap check: derive it and count
leftover apex symbols; it should be zero.

**Is the arity right?** A module's identity is its symbol *and* its parameter
count. `A -> B` does not fire on `A(1)`. Silent no-op, looks like the rule was
ignored.

**Do the stochastic variants all guard the same way?** If two variants have no
condition and one has `l < MIN`, the terminal one never gets to win — the
others match too and the weighted draw includes them. Every variant in a set
needs its own guard: `l >= MIN` on the growing ones, `l < MIN` on the terminal
one.

**`!(w)` SETS the width, it does not scale it.** So `!(0.7)` in a rule makes
every branch 0.7 wide, not 30% thinner than its parent. To taper, carry the
width as a module parameter and compute it: `A(l,w) -> F(l) !(w*TAPER) A(l*SH,
w*TAPER)`. Same for `"(s)` and the step length.

**One rule per line**, unless a `[` is open. There is no continuation
character — `\` is the roll-left turtle symbol. Break long rules inside a
branch, or let them be long.

**`//` is a comment but a lone `/` is the roll symbol.** `/(137.5)` is fine;
two rolls in a row need `/ /` or `/(90)/(90)`.

**Golden angle.** `/(137.5)` between siblings stops branches stacking into a
plane. It is the single cheapest thing that makes output stop looking
procedural.

## Determinism, threading, allocation

- Seed everything: `Rewrite(g, n, seed, sequence)`. `sequence` gives each
  instance its own stream from one world seed, so `Rewrite(g, n, worldSeed,
  instanceId)` is the pattern. Never `UnityEngine.Random` anywhere near this.
- **The returned `LModuleString` is owned by the rewriter** and is overwritten
  by its next `Rewrite`. Consume it immediately, or call `CopyResult()`.
- **`LSystemRewriter` is not thread-safe.** Chunk meshing runs on `Task.Run`
  workers, so anything sampled during meshing needs `[ThreadStatic]` scratch:

  ```csharp
  [System.ThreadStatic] static LSystemRewriter _rewriter;
  [System.ThreadStatic] static LSkeleton _skeleton;
  ...
  var rw = _rewriter ??= new LSystemRewriter();
  _skeleton = TurtleInterpreter.Build(rw.Rewrite(g, n, seed, seq), turtle, _skeleton);
  ```

  `TurtleInterpreter.Build` takes a skeleton to reuse; pass the same one back
  each time and the whole pipeline stops allocating after warmup.
- `MaxModules` (250k) caps word length. On overflow you get the last *complete*
  generation and `Truncated == true`, never a half-derived word.

## Adding a new decoration without touching this folder

Any symbol the turtle doesn't recognise becomes a marker carrying its
parameters. That is the extension point — use it instead of adding turtle
commands:

```
A(l,w) : l < MIN -> F(l) K(w*1.1)      // K is not a turtle command
```

```csharp
foreach (var mk in skel.Markers)
    if (mk.Symbol == 'K')
        AddBlob(mk.Position, skel.GetMarkerParam(mk, 0, mk.Width) * 0.5f);
```

New decoration type = one line of grammar + one case in your consumer.

## Worked example: the Spire Grove biome

`Scripts/Terrain/LSystemGroveField.cs` is the reference consumer — grammar to
`DensityField`. Read it before writing a second one; the shape of the problem
repeats.

| | |
|---|---|
| `Config/GroveGrammar.asset` | the grammar text |
| `Config/GroveField.asset` | `LSystemGroveField`, ground + spires |
| `Config/GroveBiome.asset` | the `Biome` wrapping that field |

What it has to solve, and how — these are the parts that generalise:

1. **A density field is a pure function of world position at every LOD.** So
   the world is cut into plots, each plot's contents are
   `f(seed, plotX, plotZ)`, and a sample reads its own plot plus the eight
   around it.
2. **That 3×3 neighbourhood is only valid if a structure can't reach further
   than one plot.** Enforced, not assumed: `Fit()` uniformly scales a skeleton
   down if it exceeds half a plot horizontally or `maxHeight` vertically. The
   same clamp is what makes `TryGetHeightBounds` honest — and losing that
   costs the mesher its empty-chunk skip for the *whole world*.
3. **A sample must not test every capsule.** Each plot carries a CSR bucket
   grid over XZ; capsules are inserted into every cell their footprint touches
   *inflated by radius + blend*, which is the range beyond which a capsule
   can't move a nearby surface — so bucketing never shifts the isosurface.
4. **Per-column work beats per-voxel work.** Overriding `AddDensityColumn`
   resolves the ground height and the candidate capsule set once per `(x,z)`
   instead of once per voxel. A 32-tall column goes from 32 grid lookups to 1.
5. **Segments become tapered capsules (round cones), unioned with a smooth
   max.** `SMax` returns the *exact* max once its arguments are more than `k`
   apart, which is why folding in hundreds of distant capsules can't inflate
   the ground.

## Traps specific to this project

**Adding a grammar-driven field to a BiomeWorld drops that world to CPU
meshing.** `BiomeDensityField.TryBuildGpuLeaves` requires *every* biome's field
to resolve to a fixed `LeafGpuParams` struct; one that can't takes the whole
world with it (no per-chunk GPU/CPU mixing — that would break watertightness).
A grove is a variable-length capsule buffer, so it can never be a GPU leaf as
that interface stands. This is why `GroveBiome` is **not** wired into
`Config/BiomeWorld.asset` by default.

**The dependency runs one way.** `Scripts/LSystem` has an `.asmdef` (the only
one in the project) so that EditMode tests have something to reference — a test
assembly cannot reference the predefined `Assembly-CSharp`. `Assembly-CSharp`
auto-references `TerrainGen.LSystem`, so terrain code can use grammars freely,
but **nothing in `Scripts/LSystem` can reach `DensityField`**. A new
skeleton-to-terrain adapter belongs in `Scripts/Terrain`, never here.

**Band-limiting is globally off.** `TerrainNoise.kBandLimit` is `const false`,
so `DetailFade` always returns 1. Follow the convention and call it anyway, but
don't expect it to save thin geometry from aliasing at coarse LODs — it won't,
and it isn't currently supposed to.

**Density convention is positive = solid**, the opposite of the raymarching
SDFs you'll copy formulas from. Negate at the very end, like `AlienVolumeField`
does.

## Verifying changes

- `Scripts/LSystem/Tests/` — EditMode tests for parser, rewriter and turtle.
  Run them from the Test Runner; they cover the derivation rules, all the
  context-matching tree cases, seed determinism and the turtle's handedness.
- The parser and rewriter have no `UnityEngine` dependency, so they can be
  compiled and run under plain `dotnet` for a fast loop without opening Unity.
  The turtle can't — `Quaternion.AngleAxis` is native and throws outside the
  editor.
- `LSystemGroveField` exposes `DistanceToCone` / `SmoothMax` as `internal` for
  exactly that reason: its geometry math can be checked offline, but it lives
  in `Assembly-CSharp` and the EditMode tests can't reach it.
