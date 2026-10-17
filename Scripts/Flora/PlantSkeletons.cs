using System.Collections.Generic;
using LSystems;
using UnityEngine;

// The L-system skeleton of a baked plant variant, regenerated exactly as PlantPrototypeBaker derived it (the
// set's source profile: grammar, iterations, turtle, and the variant's stored seed) and cached. Plant-local
// space, the same space as the variant's meshes (PlantMeshBuilder sweeps the skeleton's nodes as they are).
// For perception and planning only (the tree octopus's branch hints): contacts always come from the real
// mesh. Main thread; the first request per variant derives it (milliseconds), later ones are a lookup.
public static class PlantSkeletons
{
    static readonly Dictionary<(PlantPrototypeSet, int), LSkeleton> cache = new Dictionary<(PlantPrototypeSet, int), LSkeleton>();
    static LSystemRewriter rewriter;

    // Null when the set has no usable source profile (a hand-made or stripped set).
    public static LSkeleton For(PlantPrototypeSet set, int variant)
    {
        if (set == null || set.variants == null || variant < 0 || variant >= set.variants.Length) return null;
        if (cache.TryGetValue((set, variant), out LSkeleton cached)) return cached;
        LSkeleton skel = null;
        PlantProfile profile = set.source;
        PlantVariant pv = set.variants[variant];
        if (profile != null && profile.grammar != null && profile.grammar.IsValid && pv != null && profile.grammar.Grammar != null)
        {
            if (rewriter == null) rewriter = new LSystemRewriter();
            rewriter.MaxModules = Mathf.Max(1000, profile.maxModules);
            int iters = profile.iterations >= 0 ? profile.iterations : profile.grammar.EffectiveIterations;
            LModuleString word = rewriter.Rewrite(profile.grammar.Grammar, iters, pv.seed);
            skel = TurtleInterpreter.Build(word, profile.turtle, new LSkeleton());
        }
        cache[(set, variant)] = skel;
        return skel;
    }

    public static void Clear() => cache.Clear();
}
