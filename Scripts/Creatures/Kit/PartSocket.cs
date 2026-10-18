// Creature kit: where a part attaches. A spine joint, a side and an offset in that joint's frame (+Z forward,
// +Y up, +X right; x is mirrored by side, as LegSpec does). A soft body (octopus) uses the ring slot instead.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    public struct PartSocket
    {
        [Param(ParamUnit.Joint, "spine joint the part hangs from (soft bodies: unused)")]
        public int joint;
        [Param(ParamUnit.Count, "-1 left, +1 right, 0 on the midline; set by mirror", Min = -1, Max = 1)]
        public int side;
        [Param(ParamUnit.Metres, "offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre)")]
        public Vector3 offset;
        [Param(ParamUnit.Count, "ring slot on a soft body (octopus web), 0 = the first")]
        public int slot;

        public PartSocket(int joint, int side, Vector3 offset) { this.joint = joint; this.side = side; this.offset = offset; slot = 0; }
    }
}
