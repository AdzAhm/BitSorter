using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// <see cref="Allocations.Least"/>, which every test that holds work to allocating nothing measures with: it must still
    /// see work that allocates every time, or every one of those tests would pass by construction, and pass over work that
    /// allocated once, the runtime's one-time work being what it is there to leave out.
    /// </summary>
    public class AllocationsTests
    {
        private static object _kept;

        [Test]
        public void WorkThatAllocatesEveryTime_IsSeen()
        {
            long least = Allocations.Least(() => _kept = new byte[1000]);
            Assert.GreaterOrEqual(least, 1000, "an array made every call is counted in every measure");
        }

        [Test]
        public void WorkThatAllocatedOnlyOnce_IsNot()
        {
            int calls = 0;
            long least = Allocations.Least(() =>
            {
                if (calls++ == 1)
                    _kept = new byte[1000];   // once, in the first measure: as the runtime's one-time work lands
            });
            Assert.AreEqual(0, least, "the least of three measures leaves out what one of them alone made");
            Assert.AreEqual(1 + Allocations.Measures, calls, "warmed once, then measured three times");
        }
    }
}
