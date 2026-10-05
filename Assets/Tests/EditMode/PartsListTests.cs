using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The parts list down the left edge: how long it may grow, and that it always fits the window.
    /// </summary>
    public class PartsListTests
    {
        /// <summary>
        /// How tall a bottom-corner readout is at most, by <see cref="UiTheme.ClockDiagramCorner"/>'s
        /// remarks: the clock diagram in the left corner is what the list must stop above.
        /// </summary>
        private const float CornerReadout = 96f;

        /// <summary>
        /// The list is centred on the left edge, between the menu button's key at the top and the
        /// clock diagram's corner at the bottom, so it has twice the nearer of the two to the middle.
        /// </summary>
        private static float Room(float canvasHeight)
        {
            float top = UiTheme.Margin + UiTheme.ButtonHeight + 4f + UiTheme.KeyCaptionHeight;
            float bottom = UiTheme.Margin + CornerReadout;

            return canvasHeight - 2f * (Mathf.Max(top, bottom) + UiTheme.Gap);
        }

        /// <remarks>
        /// 930 is the canvas a 1920 by 800 browser tab gives, the shortest window the interface is
        /// held to anywhere.
        /// </remarks>
        [TestCase(1080f)]
        [TestCase(930f)]
        public void EveryLengthOfPartsList_FitsTheWindow(float canvasHeight)
        {
            for (int rows = 1; rows <= LevelDefinition.MaxPartsRows; rows++)
            {
                Assert.LessOrEqual(GatePaletteView.ListHeight(rows), Room(canvasHeight),
                    $"{rows} rows run past the window's {canvasHeight}");
            }
        }

        [Test]
        public void ARowIsCompactOnlyPastWhatFitsAtFullSize()
        {
            Assert.AreEqual(UiTheme.PaletteButton, GatePaletteView.RowHeightFor(GatePaletteView.FullRows));
            Assert.AreEqual(GatePaletteView.CompactRow, GatePaletteView.RowHeightFor(GatePaletteView.FullRows + 1));

            float fullAtOneMore = (GatePaletteView.FullRows + 1) * (UiTheme.PaletteButton + UiTheme.Gap);
            Assert.Greater(fullAtOneMore, Room(930f),
                "rows go compact while full ones would still fit -- FullRows is set too low");
        }

        [Test]
        public void FreePlaysLibrary_IsWhatIsLeftUnderItsGates()
        {
            LevelDefinition empty = SandboxLevel.Build(SandboxLevel.Default(SandboxLevel.Board), SandboxLevel.Board);

            Assert.AreEqual(LevelDefinition.MaxPartsRows, empty.Budget.Count + SandboxLevel.LibraryCapacity,
                "the library and the gates together are not the longest list there may be");
            Assert.Greater(SandboxLevel.LibraryCapacity, 0);
        }

        [Test]
        public void FreePlay_StocksItsLibraryWithoutLimit_AndTakesAnyBlockBack()
        {
            BlockDefinition parity = BlockTests.Parity();
            LevelDefinition level = SandboxLevel.Build(
                SandboxLevel.Default(SandboxLevel.Board), SandboxLevel.Board, new[] { parity });

            Assert.AreSame(parity, level.BlockNamed("PAR"));
            Assert.AreEqual(1, level.BlockBudget.Count, "the library's block has no row");
            Assert.AreEqual(LevelDefinition.UnlimitedBudget, level.BlockBudgetFor("PAR"));
            Assert.IsTrue(level.AnyBlock, "a board's copy of a block since deleted would be dropped");
            Assert.AreEqual(LevelDefinition.UnlimitedBudget, level.BlockBudgetFor("GONE"));
        }

        [Test]
        public void NoShippedLevel_NeedsItsRowsCompact()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                string name = asset.name;
                LevelLoadResult loaded = LevelLoader.Load(name, LevelTestFixtures.Board);
                Assert.IsTrue(loaded.IsValid, $"{name}: {loaded.Error}");

                int rows = loaded.Level.Budget.Count + loaded.Level.BlockBudget.Count;
                Assert.LessOrEqual(rows, GatePaletteView.FullRows, $"{name} has {rows} parts");
            }
        }
    }
}
