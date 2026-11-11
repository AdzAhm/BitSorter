using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// <see cref="IdleRendering"/> (5.1): every frame drawn while anything happens and for a second after, else one in four --
    /// each kind of happening alone enough to draw every frame again at once.
    /// </summary>
    public class IdleRenderingTests
    {
        private static readonly IdleFacts Nothing = default;

        [Test]
        public void TheFirstFrame_IsDrawn_AndEveryFrameForASecondAfter()
        {
            var idle = new IdleRendering();
            Assert.AreEqual(1, idle.Frame(100.0, Nothing), "a launch is something happening");
            Assert.AreEqual(1, idle.Frame(100.5, Nothing));
            Assert.AreEqual(1, idle.Frame(100.999, Nothing), "still inside the second");
            Assert.AreEqual(IdleRendering.IdleInterval, idle.Frame(101.0, Nothing), "a second of nothing: one frame in four");
            Assert.AreEqual(IdleRendering.IdleInterval, idle.Frame(500.0, Nothing), "and so on while nothing happens");
            Assert.AreEqual(IdleRendering.IdleInterval, idle.Interval);
        }

        [TestCase(true, false, false, false, false, false, false, TestName = "Input")]
        [TestCase(false, true, false, false, false, false, false, TestName = "Resized")]
        [TestCase(false, false, true, false, false, false, false, TestName = "Playing")]
        [TestCase(false, false, false, true, false, false, false, TestName = "Making")]
        [TestCase(false, false, false, false, true, false, false, TestName = "Working")]
        [TestCase(false, false, false, false, false, true, false, TestName = "Moving")]
        [TestCase(false, false, false, false, false, false, true, TestName = "Redrawn")]
        public void EachHappening_DrawsEveryFrameAtOnce_ThenLingersASecond(bool input, bool resized, bool playing, bool making,
            bool working, bool moving, bool redrawn)
        {
            var idle = new IdleRendering();
            idle.Frame(0.0, Nothing);
            Assert.AreEqual(IdleRendering.IdleInterval, idle.Frame(5.0, Nothing), "sanity: idle");

            var facts = new IdleFacts(input, resized, playing, making, working, moving, redrawn);
            Assert.IsTrue(facts.Any);
            Assert.AreEqual(1, idle.Frame(5.01, facts), "the frame it happens in is drawn, and every one after");
            Assert.AreEqual(1, idle.Frame(5.9, Nothing), "for a second after");
            Assert.AreEqual(IdleRendering.IdleInterval, idle.Frame(6.01, Nothing), "then one in four again");
        }

        /// <summary>The pair to the above: with nothing happening, nothing makes it draw every frame.</summary>
        [Test]
        public void Nothing_IsNothing()
        {
            Assert.IsFalse(Nothing.Any);
            Assert.IsFalse(new IdleFacts().Any);
        }

        [Test]
        public void SomethingGoingOn_KeepsEveryFrameDrawn_HoweverLong()
        {
            var idle = new IdleRendering();
            var playing = new IdleFacts(playing: true);
            for (double t = 0; t < 30.0; t += 0.25)
                Assert.AreEqual(1, idle.Frame(t, playing), $"playing at {t} s");
        }
    }
}
