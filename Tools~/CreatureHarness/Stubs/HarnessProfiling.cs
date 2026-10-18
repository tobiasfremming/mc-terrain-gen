// UnityEngine.Profiling.Recorder for Tests/EditMode/GcAllocs.cs (both modes; in RealUnity mode it shadows the
// native one on purpose, CS0436). CoreCLR's per-thread allocation counter is exact, so sampleBlockCount here
// is the number of bytes allocated while enabled: 0 when nothing allocated, >= 10 for ten byte[64], which
// is all GcAllocs.Count and its positive control rely on.
using System;

namespace UnityEngine.Profiling
{
    public sealed class Recorder
    {
        long start, total; bool on;
        [ThreadStatic] static Recorder gc;
        public static Recorder Get(string name) => gc ?? (gc = new Recorder());
        public bool enabled
        {
            get => on;
            set
            {
                if (value == on) return;
                if (value) { total = 0; start = GC.GetAllocatedBytesForCurrentThread(); }
                else total += GC.GetAllocatedBytesForCurrentThread() - start;
                on = value;
            }
        }
        public void FilterToCurrentThread() { }
        public void CollectFromAllThreads() { }
        public int sampleBlockCount => (int)Math.Min(int.MaxValue, total);
    }
}
