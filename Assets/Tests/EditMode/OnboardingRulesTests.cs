using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    public class OnboardingRulesTests
    {
        [Test]
        public void ItShowsOnlyForFirstLevelAndOnlyOnce()
        {
            Assert.IsTrue(OnboardingRules.ShouldShow("route-the-bit", false, false));
            Assert.IsFalse(OnboardingRules.ShouldShow("route-the-bit", true, false));
            Assert.IsFalse(OnboardingRules.ShouldShow("route-the-bit", false, true));
            Assert.IsFalse(OnboardingRules.ShouldShow("balance-the-paths", false, false));
        }

        [Test]
        public void StepBoundsAndButtonsStayConsistent()
        {
            Assert.AreEqual(0, OnboardingRules.ClampStep(-4));
            Assert.AreEqual(OnboardingRules.StepCount - 1, OnboardingRules.ClampStep(99));

            Assert.IsFalse(OnboardingRules.CanStepBack(0));
            Assert.IsTrue(OnboardingRules.CanStepForward(0));
            Assert.IsTrue(OnboardingRules.CanStepBack(OnboardingRules.StepCount - 1));
            Assert.IsFalse(OnboardingRules.CanStepForward(OnboardingRules.StepCount - 1));
        }

        [Test]
        public void EveryStepHasNonEmptyCopy()
        {
            for (int step = 0; step < OnboardingRules.StepCount; step++)
            {
                Assert.IsNotEmpty(OnboardingRules.TitleAt(step));
                Assert.IsNotEmpty(OnboardingRules.BodyAt(step));
            }
        }
    }
}
