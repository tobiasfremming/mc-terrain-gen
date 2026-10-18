// Creature kit (design docs/superpowers/specs/2026-10-18-creature-kit-design.md §2.2): the one generic bone
// layout. Every part asks for a number of bones; the registry hands out contiguous ranges in a fixed block
// order per body plan, so callers never compute bone indices themselves:
//   spine plan: Body (spine joints), Limb (legs, in part order), Wing, Sense (2 eyes)    <- the legacy order
//               then Chain, Fin, Rigid (own-bone cones)                                   <- new kinds
//   soft plan:  Chain (arms), Body (6 soft bones), Sense (eyes), Rigid (siphon)           <- OctopusBody's order
// The legacy blocks come first in their legacy order, so every shipped species keeps its exact bone indices
// (golden hashes). Built once per species (CreatureSpecies.Layout caches it); lookups are array reads.
using System;
using System.Collections.Generic;

namespace CreatureKit
{
    // What a part is, as far as the layout and the systems go: its block in the layout and the system that runs it.
    // Sugar (Repeat) is expanded by compose before anything lays bones out; the layout rejects it.
    public enum PartKind { Body = 0, Limb = 1, Wing = 2, Sense = 3, Chain = 4, Fin = 5, Rigid = 6, Sugar = 7 }

    // One part's request: its kind, its id (for messages and lookups) and how many bones it needs.
    public readonly struct BoneBlock
    {
        public readonly PartKind Kind; public readonly string Id; public readonly int Count;
        public BoneBlock(PartKind kind, string id, int count) { Kind = kind; Id = id; Count = count; }
    }

    // The bones a part got: [Start, Start + Count). Part = the index of its block in the request list.
    public readonly struct BoneRange
    {
        public readonly int Part, Start, Count; public readonly PartKind Kind; public readonly string Id;
        public BoneRange(int part, string id, PartKind kind, int start, int count) { Part = part; Id = id; Kind = kind; Start = start; Count = count; }
        public override string ToString() => $"{Id} ({Kind}) {Start}..{Start + Count - 1}";
    }

    public sealed class CreatureBoneLayout
    {
        public static readonly PartKind[] SpineOrder = { PartKind.Body, PartKind.Limb, PartKind.Wing, PartKind.Sense, PartKind.Chain, PartKind.Fin, PartKind.Rigid };
        public static readonly PartKind[] SoftOrder = { PartKind.Chain, PartKind.Body, PartKind.Sense, PartKind.Rigid, PartKind.Limb, PartKind.Wing, PartKind.Fin };

        public readonly BoneRange[] Ranges;   // in bone order, bone-less parts left out
        public readonly int BoneCount;
        readonly int[] startOfPart;           // by block index; a bone-less part gets the index it would start at

        // the legacy view (FromLegacy): first bone of each leg, of the wing block, of the eyes
        int[] legStart;
        int wingStart, eyeStart;

        public CreatureBoneLayout(IList<BoneBlock> blocks, PartKind[] order)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            order = order ?? SpineOrder;
            startOfPart = new int[blocks.Count];
            for (int i = 0; i < startOfPart.Length; i++) startOfPart[i] = -1;
            var list = new List<BoneRange>();
            int next = 0;
            for (int o = 0; o < order.Length; o++)
                for (int i = 0; i < blocks.Count; i++)
                {
                    BoneBlock b = blocks[i];
                    if (b.Kind != order[o]) continue;
                    if (b.Count < 0) throw new ArgumentException($"{b.Id}: negative bone count {b.Count}");
                    startOfPart[i] = next;
                    if (b.Count > 0) list.Add(new BoneRange(i, b.Id, b.Kind, next, b.Count));
                    next += b.Count;
                }
            for (int i = 0; i < startOfPart.Length; i++)
                if (startOfPart[i] < 0) throw new ArgumentException($"{blocks[i].Id}: kind {blocks[i].Kind} is not in the block order");
            Ranges = list.ToArray();
            BoneCount = next;
        }

        // First bone of a part (by its block index), and its k-th bone.
        public int Start(int part) => startOfPart[part];
        public int Bone(int part, int k) => startOfPart[part] + k;
        public int PartCount => startOfPart.Length;

        // ------------------------------------------------------------ legacy fields

        // The layout of a CreatureSpecies' legacy fields: spine joints, each leg's bones (3 for a two-bone leg,
        // one per arthropod segment), 6 per wing, 2 eyes. Blocks: 0 = spine, 1..legs, then wings, then eyes.
        public static CreatureBoneLayout FromLegacy(CreatureSpecies sp) => FromLegacy(sp, SpineOrder);

        // `order` exists for the RED test (a wrong block order must break the legacy equivalence).
        public static CreatureBoneLayout FromLegacy(CreatureSpecies sp, PartKind[] order)
        {
            int legs = sp.legs != null ? sp.legs.Length : 0, wings = sp.WingCount;
            var blocks = new List<BoneBlock>(legs + wings + 2) { new BoneBlock(PartKind.Body, "spine", sp.JointCount) };
            for (int l = 0; l < legs; l++) blocks.Add(new BoneBlock(PartKind.Limb, "leg" + l, sp.legs[l] != null ? sp.legs[l].SegmentCount : 3));
            for (int w = 0; w < wings; w++) blocks.Add(new BoneBlock(PartKind.Wing, "wing" + w, WingBoneLayout.BonesPerWing));
            blocks.Add(new BoneBlock(PartKind.Sense, "eyes", 2));
            var layout = new CreatureBoneLayout(blocks, order);
            layout.legStart = new int[legs];
            for (int l = 0; l < legs; l++) layout.legStart[l] = layout.Start(1 + l);
            layout.wingStart = layout.Start(1 + legs);
            layout.eyeStart = layout.Start(1 + legs + wings);
            if (wings == 0) layout.wingStart = layout.eyeStart;
            return layout;
        }

        // The legacy accessors for a layout built from parts (CompiledSpecies): leg l = the l-th limb part.
        internal void SetLegacyStarts(int[] legs, int wings, int eyes) { legStart = legs; wingStart = wings; eyeStart = eyes; }

        public int LegBone(int leg, int part) => legStart[leg] + part;
        public int WingBone(int wing, int k) => wingStart + WingBoneLayout.BonesPerWing * wing + k;
        public int EyeBone(int eye) => eyeStart + eye;
        public int LegCount => legStart != null ? legStart.Length : 0;

        // ------------------------------------------------------------ invariants

        // null when the ranges tile 0..BoneCount-1 without a gap or an overlap.
        public string Check()
        {
            int expect = 0;
            for (int i = 0; i < Ranges.Length; i++)
            {
                BoneRange r = Ranges[i];
                if (r.Start != expect) return $"{r.Id}: starts at bone {r.Start}, expected {expect} (gap or overlap)";
                if (startOfPart[r.Part] != r.Start) return $"{r.Id}: part start {startOfPart[r.Part]} != range start {r.Start}";
                expect = r.Start + r.Count;
            }
            return expect != BoneCount ? $"ranges end at {expect}, BoneCount {BoneCount}" : null;
        }

        public override string ToString() => $"{BoneCount} bones: " + string.Join(", ", Array.ConvertAll(Ranges, r => r.ToString()));
    }
}
