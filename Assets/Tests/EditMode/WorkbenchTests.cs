using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The parts of the 5.0 workbench that need Unity: how its stage fits a window, its colours, and
    /// the claim the view-model's arithmetic rests on.
    /// </summary>
    /// <remarks>
    /// The view-model itself -- the run read at a moment, the routes, pan and zoom, pointing at a
    /// wire, the chunking -- is tested under <c>dotnet test</c> (<c>Tools/ci/ViewModel.Tests</c>),
    /// where twenty-three of these tests went when it left Unity in Part 9.
    /// </remarks>
    public class WorkbenchTests
    {
        // -----------------------------------------------------------------
        // The stage in a window
        // -----------------------------------------------------------------

        [Test]
        public void TheStage_FillsATallWindow_ButNeverShrinksItsWordsInAShortOne_AndScrollsInstead()
        {
            var stage = new Vector2(1000f, 900f);

            Assert.AreEqual(1.2f, WorkbenchPanel.StageScale(new Vector2(1800f, 1080f), stage), 1e-4f, "a tall window: as large as fits");
            Assert.AreEqual(1f, WorkbenchPanel.StageScale(new Vector2(1800f, 700f), stage), 1e-4f,
                "a short window keeps the stage at its own size, its words at theirs");
            Assert.AreEqual(200f, WorkbenchPanel.Overflow(700f, 900f, 1f), 1e-3f, "and scrolls what it cannot show");
            Assert.AreEqual(0f, WorkbenchPanel.Overflow(1080f, 900f, 1.2f), 1e-3f, "a stage that fits does not scroll");
        }

        // -----------------------------------------------------------------
        // Colours
        // -----------------------------------------------------------------

        // Every look's canvas colours answer the brief in LookBriefTests, which holds them all.

        [Test]
        public void AFaintColour_TakesLessAlphaInLinearSpace_ThanABrowserWould()
        {
            SchematicPalette p = SchematicPalette.Dark;

            Assert.AreEqual(0f, p.Over(p.One, 0f).a, 1e-4f);
            Assert.AreEqual(1f, p.Over(p.One, 1f).a, 1e-4f);
            Assert.Less(p.Over(p.One, 0.35f).a, 0.35f,
                "a light colour over a dark ground blends brighter in linear space, so it needs less alpha to look the same");
            Assert.Greater(p.Over(p.One, 0.35f).a, 0f);
        }

        // -----------------------------------------------------------------
        // The view-model's arithmetic is Unity's
        // -----------------------------------------------------------------

        /// <summary>
        /// <see cref="Vec2"/> computes what Unity's <c>Vector2</c> computes, to the last bit, for every
        /// operation the workbench uses -- the claim that lets the view-model's tests under
        /// <c>dotnet test</c> speak for what the editor and the browser draw, and the shots stay
        /// identical to the pixel.
        /// </summary>
        [Test]
        public void Vec2_ComputesWhatUnitysVector2Computes_ToTheBit()
        {
            var random = new System.Random(20261015);
            int differ = 0;

            for (int i = 0; i < 1000000; i++)
            {
                float ax = Next(random), ay = Next(random), bx = Next(random), by = Next(random);
                float d = (float)(random.NextDouble() * 50 + 0.01), t = (float)(random.NextDouble() * 1.4 - 0.2);
                var ua = new Vector2(ax, ay);
                var ub = new Vector2(bx, by);
                var va = new Vec2(ax, ay);
                var vb = new Vec2(bx, by);

                if (!Same(ua + ub, va + vb) || !Same(ua - ub, va - vb) || !Same(ua * d, va * d) || !Same(d * ua, d * va) ||
                    !Same(ua / d, va / d) || !Same(-ua, -va) || !Same(ua * ub, va * vb) ||
                    !Same(ua.normalized, va.normalized) ||
                    !Same(Vector2.Lerp(ua, ub, t), Vec2.Lerp(va, vb, t)) ||
                    !Same(Vector2.LerpUnclamped(ua, ub, t), Vec2.LerpUnclamped(va, vb, t)) ||
                    !Bits(Vector2.Distance(ua, ub), Vec2.Distance(va, vb)) || !Bits(Vector2.Dot(ua, ub), Vec2.Dot(va, vb)) ||
                    !Bits(ua.magnitude, va.magnitude) || !Bits(ua.sqrMagnitude, va.sqrMagnitude) ||
                    (ua == ub) != (va == vb) ||
                    !Bits(Mathf.Clamp(ax, -100f, 100f), Arith.Clamp(ax, -100f, 100f)) || !Bits(Mathf.Clamp01(t), Arith.Clamp01(t)) ||
                    !Bits(Mathf.Min(ax, bx), Arith.Min(ax, bx)) || !Bits(Mathf.Max(ax, bx), Arith.Max(ax, bx)) ||
                    !Bits(Mathf.Pow(d, t), Arith.Pow(d, t)))
                {
                    differ++;
                }
            }

            Assert.AreEqual(0, differ, "of a million random cases, these computed a different float from Unity's");

            // And the edges: no length at all, and two points a hair apart.
            Assert.IsTrue(Same(Vector2.zero.normalized, Vec2.zero.normalized));
            Assert.IsTrue(Same(new Vector2(1e-6f, 0f).normalized, new Vec2(1e-6f, 0f).normalized));
            Assert.AreEqual(new Vector2(1f, 1f) == new Vector2(1f + 5e-6f, 1f), new Vec2(1f, 1f) == new Vec2(1f + 5e-6f, 1f));
        }

        private static float Next(System.Random random) =>
            random.Next(10) == 0 ? random.Next(-3, 4) : (float)(random.NextDouble() * 2000 - 1000);

        private static bool Same(Vector2 u, Vec2 v) => Bits(u.x, v.x) && Bits(u.y, v.y);

        private static bool Bits(float a, float b) =>
            System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);
    }
}
