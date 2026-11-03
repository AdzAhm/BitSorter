using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>What a look is, and the one conversion every new look's alphas go through.</summary>
    public class LookTests
    {
        /// <summary>
        /// Classic changes nothing: its interface is the colours the game shipped with, and its canvas is made from them.
        /// </summary>
        [Test]
        public void Classic_LeavesTheSceneAsItIs()
        {
            Look classic = Look.Classic;

            Assert.AreSame(Palette.Classic, classic.Colours);
            Assert.AreSame(SchematicPalette.Classic, classic.Canvas);
            Assert.AreEqual(PanelStyle.Filled, classic.Panels, "its panels are the filled slabs it shipped with");
        }

        /// <summary>Every look can be found by its name, and no two share one.</summary>
        [Test]
        public void EveryLook_IsNamedOnce()
        {
            foreach (Look look in Look.All)
            {
                Assert.IsNotNull(look.Colours, $"{look.Name} has no palette");
                Assert.AreSame(look, Look.Named(look.Name), $"{look.Name} is not the look its name finds");
            }

            Assert.IsNull(Look.Named("no such look"));
        }

        /// <summary>
        /// The player's choice is kept as a number of the look's own, so no two looks share one and a
        /// look added or moved in the list cannot change anyone's choice.
        /// </summary>
        [Test]
        public void EveryLook_IsKeptAsANumberOfItsOwn_AndOffersItselfByName()
        {
            var codes = new System.Collections.Generic.HashSet<int>();
            foreach (Look look in Look.All)
            {
                Assert.IsTrue(codes.Add(look.Code), $"{look.Name} shares its number with another look");
                Assert.IsFalse(string.IsNullOrEmpty(look.Title), $"{look.Name} has nothing for Settings to call it");
            }

            CollectionAssert.AreEqual(new[] { 3, 4, 2, 1 }, System.Linq.Enumerable.Select(Look.All, l => l.Code),
                "the numbers are what players' choices were kept as; never renumber one");
        }

        [Test]
        public void TheChosenLook_IsTheOneStored_OrTheDefault()
        {
            Preferences.Redirected = new System.Collections.Generic.Dictionary<string, int>();
            try
            {
                Assert.AreSame(Look.Default, Look.Chosen, "nothing chosen yet");

                Preferences.SetInt(Look.PreferenceKey, Looks.SchematicLight.Code);
                Assert.AreSame(Looks.SchematicLight, Look.Chosen);

                Preferences.SetInt(Look.PreferenceKey, 99);
                Assert.AreSame(Look.Default, Look.Chosen, "a look this build does not have is the default, never nothing");
            }
            finally
            {
                Preferences.Redirected = null;
            }
        }

        /// <summary>
        /// The default is the workbench as Ahmad chose it -- its canvas, at his glow -- on Neon
        /// Board's interface, so making it the default moved no pixel of the game.
        /// </summary>
        [Test]
        public void TheDefault_IsSchematic_OnNeonBoardsInterface()
        {
            Assert.AreSame(Looks.Schematic, Look.Default);
            Assert.AreSame(SchematicPalette.Dark, Looks.Schematic.Canvas);
            Assert.AreEqual(SchematicStyle.GlowStrength, Looks.Schematic.CanvasGlow);

            foreach (Look schematic in new[] { Looks.Schematic, Looks.SchematicLight })
            {
                Assert.AreSame(Looks.NeonBoard.Colours, schematic.Colours, $"{schematic.Name} should keep Neon Board's interface");
                Assert.AreEqual(Looks.NeonBoard.Panels, schematic.Panels);
            }
        }

        /// <summary>Nothing and everything are the same written as seen.</summary>
        [Test]
        public void Seen_KeepsTheEnds()
        {
            Assert.AreEqual(0f, Palette.Seen(0f), 1e-6f);
            Assert.AreEqual(1f, Palette.Seen(1f), 1e-6f);
        }

        /// <summary>
        /// The measured case: a black scrim written at 0.88 was seen as about 62% opaque, so asking
        /// for 62% has to give 0.88 back.
        /// </summary>
        [Test]
        public void Seen_UndoesTheLinearBlend()
        {
            Assert.AreEqual(0.88f, Palette.Seen(0.62f), 0.005f);
        }

        /// <summary>Asking for more opacity never gives less.</summary>
        [Test]
        public void Seen_Rises()
        {
            float previous = 0f;

            for (int step = 1; step <= 20; step++)
            {
                float alpha = Palette.Seen(step / 20f);
                Assert.GreaterOrEqual(alpha, previous);
                previous = alpha;
            }
        }
    }
}
