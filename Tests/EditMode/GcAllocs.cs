using System;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace TerrainGen.Tests
{
    // Counts managed (GC) allocations made on this thread while an action runs, from the profiler's GC.Alloc
    // marker (the mechanism of the Test Framework's Is.Not.AllocatingGCMemory()). GC.GetAllocatedBytesForCurrentThread
    // is not a counter on Unity's Mono: it read 0 around 100 kB of byte arrays (measured in the Editor), so the
    // no-alloc tests built on it could not fail. Every no-alloc test calls AssertCounterWorks as its positive control.
    static class GcAllocs
    {
        internal static int Count(Action action)
        {
            Recorder r = Recorder.Get("GC.Alloc");
            r.enabled = false;
            r.FilterToCurrentThread();
            r.enabled = true;
            try { action(); }
            finally
            {
                r.enabled = false;
                r.CollectFromAllThreads();
            }
            return r.sampleBlockCount;
        }

        static object sink;

        // Positive control: ten known allocations inside the window must be counted, and none when there are none.
        internal static void AssertCounterWorks()
        {
            int some = Count(() => { for (int i = 0; i < 10; i++) sink = new byte[64]; });
            Assert.GreaterOrEqual(some, 10, "the GC allocation counter missed 10 known allocations: a no-alloc assert would be vacuous");
            int none = Count(() => { int x = 0; for (int i = 0; i < 100; i++) x += i; sink = null; });
            Assert.AreEqual(0, none, "the GC allocation counter counts allocations that did not happen");
        }
    }

    public class CreatureGcAllocTests
    {
        // The counter the no-alloc tests rely on, and the reason it replaced GC.GetAllocatedBytesForCurrentThread.
        [Test] public void GcAllocs_PositiveControl()
        {
            GcAllocs.AssertCounterWorks();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var keep = new byte[100][];
            for (int i = 0; i < keep.Length; i++) keep[i] = new byte[1000];
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.Out.WriteLine($"GC.GetAllocatedBytesForCurrentThread over 100 kB of allocations: {delta} B");
            GC.KeepAlive(keep);
        }
    }
}
