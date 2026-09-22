using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>The bins lighting up when a level is solved.</summary>
    public class SinkCelebrationTests
    {
        private const float Rate = 2.6f;

        /// <summary>
        /// A win lands at the celebration's brightest, whatever the game clock says.
        /// </summary>
        /// <remarks>
        /// It pulsed on the game clock, so each celebration began at a random point in its swell --
        /// sometimes the bottom of it. It is counted from the pass now, and the pass is the peak.
        /// </remarks>
        [Test]
        public void AWin_BeginsAtTheTopOfItsPulse()
        {
            Assert.AreEqual(1f, SinkCelebration.PulseAt(0f, Rate), 1e-5f,
                "the moment a level is solved should be the brightest the bins get");

            for (float t = 0.01f; t < 1f / Rate; t += 0.01f)
            {
                Assert.LessOrEqual(SinkCelebration.PulseAt(t, Rate), 1f + 1e-5f,
                    "nothing later in the first beat may outshine the moment of the win");
            }
        }

        /// <summary>It still pulses: half a beat on, it is at its lowest.</summary>
        [Test]
        public void HalfABeatLater_ItIsAtItsLowest()
        {
            Assert.AreEqual(0f, SinkCelebration.PulseAt(0.5f / Rate, Rate), 1e-5f);
        }
    }
}
