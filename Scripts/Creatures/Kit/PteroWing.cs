// Creature kit: PteroWing - a pterosaur's wing. It flies exactly as a Wing does (a WingPart: CreatureFlight +
// CreatureWings beat it, pose its arm, its rays and its membrane frames), but it is built as a pterosaur's:
//   arm        a humerus with a deltopectoral crest, a ball elbow, a forearm of two bones to the carpus, a long
//              metacarpal to the big knuckle (the "wrist" of the flight system: what the wing-walk stands on);
//   digits     three small clawed fingers at the knuckle, on the thumb's curve (CreatureWings.ThumbTip): on the
//              ground they are the hand's print (WingSpec.groundWristLift stands the knuckle on them);
//   pteroid    a slender bone from the carpus toward the shoulder, the propatagium (the membrane in front of the
//              arm) spanned between it, the arm and the shoulder - two affine patches, exact on their triangles;
//   wing finger  finger 0 of the spec, as four phalanges on bones of their own: a static sweep back toward the
//              tip, a lag against the stroke (the tip whips up on the downstroke, down on the upstroke), and a curl
//              when folded on the ground (the folded blade arches back over the body);
//   membrane   the brachiopatagium: the flight system's fan from the knuckle (wing finger, two hidden actinofibril
//              rays - fingers 1 and 2 of the spec are never drawn as bones -, the trailing rib, the rear flank rib,
//              the forearm) and its flank patches to the body; its leading edge rides the phalanges, its trailing
//              edge runs smooth from the wing tip to the flank (trailingCurve), not scalloped finger to finger.
// Bones: the flight's (WingBoneLayout.Bones(spec)), then 4 phalanges, the pteroid, 2 propatagium patches. The
// bind pose is the spread wing (KitRuntime.AfterSpreadWings re-poses them on it). Off by default: a species without
// a PteroWing is unchanged. Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("PteroWing", PartKind.Wing, Bones = "as Wing + 6 (wing-finger phalanges 2-4, the pteroid, 2 propatagium patches)",
          Summary = "A pterosaur's wing on an arm limb (a Leg2Bone it names), flown like a Wing (CreatureFlight + CreatureWings): crested humerus, two-bone forearm, long metacarpal to the knuckle, three small clawed walking digits, a pteroid with the propatagium in front of the arm, a four-phalanx wing finger that sweeps, lags the stroke and curls when folded, and a narrow brachiopatagium with a smooth trailing edge to the flank.",
          System = "CreatureFlight + CreatureWings (the beat, the arm, the rays) + PteroWingRuntime (phalanges, pteroid, propatagium; stage Late) + PteroWingMesh",
          MaterialLayers = new[] { 8, 1, 2 }, KitOnly = true,
          UseWhen = "pterosaurs (azhdarchids, pteranodons): a long single wing finger, a pteroid and propatagium, walking on the folded wings' knuckles")]
    public sealed partial class PteroWing : WingPart
    {
        public const int Phalanges = 4, ExtraBones = Phalanges - 1 + 3;   // phalanx 0 rides the flight's finger-0 bone

        [Param(ParamUnit.Fraction, "the wing finger (fingerLengths[0]) split into its four phalanges, root to tip (sum 1)", Example = "azhdarchid 0.32 0.29 0.24 0.15")]
        public float[] phalanges = { 0.32f, 0.29f, 0.24f, 0.15f };
        [Param(ParamUnit.Metres, "wing finger radius at its root (it tapers to a fine tip)", Example = "apex 0.017")]
        public float fingerRoot = 0.016f;
        [Param(ParamUnit.Degrees, "static sweep back (toward the trailing edge) at each of the three interphalangeal joints", Min = -10, Max = 15)]
        public float sweepDeg = 3f;
        [Param(ParamUnit.Degrees, "the tip's lag against the stroke: each interphalangeal joint bends up to this much (up on the downstroke)", Min = 0, Max = 20)]
        public float flexDeg = 6f;
        [Param(ParamUnit.Seconds, "time constant of that lag (and of the curl's ease)", Min = 0.01f, Max = 1)]
        public float flexSeconds = 0.08f;
        [Param(ParamUnit.Degrees, "folded on the ground: each interphalangeal joint curls this much dorsally (the folded blade arches back)", Min = -20, Max = 30)]
        public float foldCurlDeg = 6f;
        [Param(ParamUnit.Fraction, "the carpus (the pteroid's root, the forearm's end) along the lower arm bone, 0 elbow .. 1 knuckle", Min = 0.3f, Max = 0.9f)]
        public float carpusAt = 0.6f;
        [Param(ParamUnit.Metres, "pteroid length", Example = "apex 0.13")]
        public float pteroidLength = 0.12f;
        [Param(ParamUnit.Degrees, "the pteroid turned from the carpus-shoulder line toward the leading edge, in the arm's plane", Min = 5, Max = 80)]
        public float pteroidDeg = 25f;
        [Param(ParamUnit.Fraction, "the deltopectoral crest's height on the humerus, x the upper arm radius (0 = none)", Min = 0, Max = 3)]
        public float crest = 1.2f;
        [Param(ParamUnit.Metres, "the three walking digits' length (0 = none)", Example = "apex 0.07")]
        public float digitLength = 0.06f;
        [Param(ParamUnit.Fraction, "the trailing edge's concavity between neighbouring rays, a fraction of the zone's reach", Min = 0, Max = 0.3f)]
        public float trailingCurve = 0.04f;
        [Param(ParamUnit.Metres, "the leading edge's thickness: the membrane rolls over the wing finger into a rounded edge this thick (0 = a flat sheet to the finger)", Example = "apex 0.012")]
        public float leadingEdge = 0.01f;
        [Param(ParamUnit.Fraction, "the membrane's camber: it bellies up between the leading edge and the trailing edge by this x the local chord", Min = 0, Max = 0.15f)]
        public float camber = 0.04f;
        [Param(ParamUnit.Count, "mesh: membrane columns per zone (across the rays)", Min = 4, Max = 32)]
        public int membraneColumns = 12;
        [Param(ParamUnit.Count, "mesh: membrane rows (out from the knuckle)", Min = 4, Max = 48)]
        public int membraneRows = 18;
        [Param(ParamUnit.Fraction, "the bind pose's fold (0 = the spread glide pose, as a Wing): the mesh is built part-folded, so the flight's spread opens the membrane's zones and the ground fold closes them each by a ratio two-ray skinning can follow (a pterosaur folds its finger along the arm: from the spread bind its zones close to a seventh and turn inside out)", Min = 0, Max = 0.8f)]
        public float bindFold;

        public override int BoneCount(in SpeciesCtx c) => FlightBones + ExtraBones;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        protected override void CopyArrays()
        {
            base.CopyArrays();
            phalanges = (float[])phalanges?.Clone();
        }

        public override string Validate(in SpeciesCtx c)
        {
            string e = base.Validate(c);
            if (e != null) return e;
            if (phalanges == null || phalanges.Length != Phalanges) return $"{id}: phalanges needs {Phalanges} entries";
            float sum = 0f;
            foreach (float p in phalanges) { if (!(p > 0f)) return $"{id}: every phalanx > 0"; sum += p; }
            if (Mathf.Abs(sum - 1f) > 0.02f) return $"{id}: phalanges must sum to 1 (they split fingerLengths[0])";
            if (!(fingerRoot > 0f) || !(pteroidLength > 0f) || digitLength < 0f) return $"{id}: fingerRoot and pteroidLength > 0, digitLength >= 0";
            if (!(flexSeconds > 0f)) return $"{id}: flexSeconds > 0";
            if (carpusAt < 0.3f || carpusAt > 0.9f) return $"{id}: carpusAt in 0.3..0.9";
            if (bindFold < 0f || bindFold > 0.8f) return $"{id}: bindFold in 0..0.8";
            if (membraneColumns < 4 || membraneRows < 4) return $"{id}: membraneColumns and membraneRows >= 4";
            // the spec is the flight's (stroke, fold, rays, membrane joints); a PteroWing draws its own wing - the Wing's
            // look fields would silently do nothing, so they must stay 0
            WingSpec w = spec;
            if (w.scallop != 0f || w.scallopByChord || w.membraneBillow != 0f || w.membraneDetail != 0 || w.fingerKnuckles != 0f
                || w.wristKnuckle != 0f || w.armMuscle != 0f || w.forearmTwin != 0f || w.thumbRadius != 0f)
                return $"{id}: spec.scallop / scallopByChord / membraneBillow / membraneDetail / fingerKnuckles / wristKnuckle / armMuscle / forearmTwin / thumbRadius are the Wing's look - a PteroWing draws its own (trailingCurve, camber, leadingEdge, crest, ...): set them to 0";
            return null;
        }

        // the wing's index among the species' wings (sp.wings order = WingParts in part order)
        internal static int WingIndex(IReadOnlyList<CreaturePart> parts, int partIndex)
        {
            int w = 0;
            for (int i = 0; i < partIndex && i < parts.Count; i++) if (parts[i] is WingPart) w++;
            return w;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null || c.Sim.Flight == null) return null;
            int w = WingIndex(KitView.Parts(c.Species), c.Index);
            return new PteroWingRuntime(this, c.Sim, w, c.Species.Layout.Start(c.Index) + FlightBones);
        }

        // ------------------------------------------------------------ shared geometry (runtime and mesh)

        // Phalanx k's frame from the wing-finger bone F (world, scaled): along +Z, rotated at each joint by the sweep
        // (about the finger's up, toward `backSign`) and the dorsal bend (flex + curl, + = dorsal). k = 0 is F itself.
        internal void Phalanx(Matrix4x4 F, int k, float backSign, float bendDeg, out Matrix4x4 m)
        {
            m = F;
            float L = spec.fingerLengths[0];
            for (int j = 1; j <= k; j++)
            {
                Quaternion r = Quaternion.AngleAxis(backSign * sweepDeg, Vector3.up) * Quaternion.AngleAxis(-bendDeg, Vector3.right);
                m = m * Matrix4x4.TRS(new Vector3(0f, 0f, phalanges[j - 1] * L), r, Vector3.one);
            }
        }

        // The pteroid's root (the carpus) and direction from the arm's points (world).
        internal void Pteroid(Vector3 shoulder, Vector3 elbow, Vector3 wrist, out Vector3 carpus, out Vector3 dir)
        {
            carpus = Vector3.Lerp(elbow, wrist, carpusAt);
            Vector3 toSh = shoulder - carpus, toEl = elbow - carpus;
            Vector3 n = Vector3.Cross(toEl, toSh);   // the arm's plane
            Vector3 a = PartMeshEmitter.Unit(toSh, Vector3.forward);
            // turned away from the elbow (toward the leading edge) about the plane's normal
            Vector3 lead = Vector3.Cross(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up, a);
            if (Vector3.Dot(lead, toEl) > 0f) lead = -lead;
            float t = pteroidDeg * Mathf.Deg2Rad;
            dir = PartMeshEmitter.Unit(a * Mathf.Cos(t) + lead * Mathf.Sin(t), a);
        }

        // The bind pose's wings: the spread rest pose with this wing part-folded (bindFold). Its own wing's arm and flight
        // bones are what the bind (and the mesh) use.
        internal CreatureWings BindWings(CreatureSim rest, int w)
        {
            CreatureSpecies sp = rest.Species;
            var cw = new CreatureWings(sp, sp.wings, rest.Joints, rest.Rotations, 1f);
            if (bindFold > 0f) { cw.SetPose(w, 0f, bindFold); cw.Pose(); }
            return cw;
        }

        // An affine patch frame: origin o, columns a - o, b - o and their unit normal x scale (exact on the triangle).
        internal static Matrix4x4 Patch(Vector3 o, Vector3 a, Vector3 b, float scale)
        {
            Vector3 c0 = a - o, c1 = b - o;
            Vector3 cn = Vector3.Cross(c0, c1);
            Vector3 c2 = (cn.sqrMagnitude > 1e-14f ? cn.normalized : Vector3.up) * scale;
            var m = Matrix4x4.identity;
            m.m00 = c0.x; m.m01 = c1.x; m.m02 = c2.x; m.m03 = o.x;
            m.m10 = c0.y; m.m11 = c1.y; m.m12 = c2.y; m.m13 = o.y;
            m.m20 = c0.z; m.m21 = c1.z; m.m22 = c2.z; m.m23 = o.z;
            return m;
        }
    }

    // One pterosaur wing of one individual: the phalanges, the pteroid and the propatagium patches, written on top
    // of the flight's bones (stage Late; WriteBones runs after the flight's).
    public sealed class PteroWingRuntime : IPartRuntime, ISpreadWingsRest
    {
        readonly PteroWing p;
        readonly CreatureSim sim;
        readonly int w, extra, finger0, finger1, upper, lower, leg;
        readonly float T;
        float flex, curl;
        readonly Vector3[] debug = new Vector3[PteroWing.Phalanges + 3];
        Matrix4x4[] bindScratch;   // the bind's bones (AfterSpreadWings, rest sims only: built on first use)

        public float Flex => flex;
        public float Curl => curl;
        public int Wing => w;
        public int FirstExtraBone => extra;

        public PteroWingRuntime(PteroWing part, CreatureSim sim, int wing, int firstExtra)
        {
            p = part; this.sim = sim; w = wing; extra = firstExtra;
            CreatureSpecies sp = sim.Species;
            finger0 = sp.WingBone(w, 0); finger1 = sp.WingBone(w, 1);
            leg = sp.wings[w].leg;
            upper = sp.LegBone(leg, 0); lower = sp.LegBone(leg, 1);
            T = sim.TimeScale;
        }

        public PartStage Stage => PartStage.Late;

        public void Step(in StepCtx c, float dt)
        {
            CreatureFlight fl = sim.Flight;
            if (fl == null || !(dt > 0f)) return;
            ref WingState st = ref fl.Wings.State[w];
            bool air = fl.Mode != FlightMode.Ground;
            float target = 0f, curlTarget = 0f;
            if (air)
            {
                float rateMax = Mathf.Max(1e-3f, sim.Species.flight.strokeRateMax / T);
                float load = Mathf.Clamp(-st.angleRate / rateMax, -1f, 1f);
                // gliding: the lift bows the spread finger up a little
                if (fl.Model.Gliding) load = 0.35f * (1f - st.fold);
                target = p.flexDeg * load;
            }
            curlTarget = p.foldCurlDeg * st.fold * (air ? 0.3f : 1f);
            float k = 1f - Mathf.Exp(-dt / (p.flexSeconds * T));
            flex += (target - flex) * k;
            curl += (curlTarget - curl) * k;
        }

        public void WriteBones(Matrix4x4[] dst, int offset) => Write(dst, offset, flex + curl);

        // the bind: the spread wing (part-folded by bindFold: its arm and flight bones rewritten), no lag, no curl
        public void AfterSpreadWings(Matrix4x4[] bones, int offset)
        {
            if (p.bindFold > 0f)
            {
                CreatureSpecies sp = sim.Species;
                if (bindScratch == null) bindScratch = new Matrix4x4[sp.BoneCount];
                p.BindWings(sim, w).WriteBones(bindScratch, 0);
                for (int k = 0; k < 3; k++) bones[offset + sp.LegBone(leg, k)] = bindScratch[sp.LegBone(leg, k)];
                for (int k = 0; k < p.FlightBones; k++) bones[offset + sp.WingBone(w, k)] = bindScratch[sp.WingBone(w, k)];
            }
            Write(bones, offset, 0f);
        }

        void Write(Matrix4x4[] dst, int o, float bend)
        {
            float s = sim.SizeScale;
            Matrix4x4 F = dst[o + finger0];
            Vector3 fx = F.GetColumn(0);
            Vector3 d1 = dst[o + finger1].GetColumn(2);
            float back = Vector3.Dot(fx, d1) >= 0f ? 1f : -1f;
            debug[0] = F.GetColumn(3);
            for (int k = 1; k < PteroWing.Phalanges; k++)
            {
                p.Phalanx(F, k, back, bend, out Matrix4x4 m);
                dst[o + extra + k - 1] = m;
                debug[k] = m.GetColumn(3);
            }
            Matrix4x4 U = dst[o + upper], Lw = dst[o + lower];
            Vector3 shoulder = U.GetColumn(3), elbow = Lw.GetColumn(3);
            Vector3 wrist = F.GetColumn(3);
            p.Pteroid(shoulder, elbow, wrist, out Vector3 carpus, out Vector3 dir);
            Vector3 up = Vector3.Cross(dir, wrist - elbow);
            if (up.sqrMagnitude < 1e-12f) up = Vector3.up;
            Vector3 tip = carpus + dir * (p.pteroidLength * s);
            dst[o + extra + PteroWing.Phalanges - 1] = Matrix4x4.TRS(carpus, Quaternion.LookRotation(dir, up.normalized), new Vector3(s, s, s));
            dst[o + extra + PteroWing.Phalanges] = PteroWing.Patch(elbow, carpus, tip, s);
            dst[o + extra + PteroWing.Phalanges + 1] = PteroWing.Patch(elbow, tip, shoulder, s);
            debug[PteroWing.Phalanges] = carpus; debug[PteroWing.Phalanges + 1] = tip; debug[PteroWing.Phalanges + 2] = shoulder;
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // within the wing's reach (the renderer's)

        public void DrawDebug(ICreatureDebugDraw d)
        {
            for (int k = 1; k < PteroWing.Phalanges; k++) d.Line(debug[k - 1], debug[k], new Color(0.2f, 0.9f, 0.8f));
            d.Line(debug[PteroWing.Phalanges], debug[PteroWing.Phalanges + 1], new Color(0.9f, 0.8f, 0.2f));
            d.Line(debug[PteroWing.Phalanges + 1], debug[PteroWing.Phalanges + 2], new Color(0.9f, 0.8f, 0.2f));
        }
    }
}
