using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Switching level must leave nothing of the previous one behind: not the board, not the parts
    /// count, not the delay spend, not the palette selection.
    /// </summary>
    /// <remarks>
    /// These drive the real LevelSession against the real shipped level files, and load more than
    /// one level per test on purpose. Every other level test loads exactly one, which is precisely
    /// the blind spot that let carried-over state through.
    ///
    /// The session runs with no SimulationRunner. Edit Mode never calls Awake, so the runner is
    /// never looked up and stays null -- which LoadLevel handles: it falls back to the default board
    /// size and skips the rebuild. Everything asserted here is upstream of the runner, so its
    /// absence changes no answer. Anything that needs a built graph belongs in a Play Mode test.
    /// </remarks>
    internal sealed class LevelSwitchTests
    {
        private GameObject _object;
        private LevelSession _session;

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("level session");
            _session = _object.AddComponent<LevelSession>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_object);

        [Test]
        public void SwitchingLevel_DiscardsWhatThePlayerBuilt()
        {
            Assert.IsTrue(_session.LoadLevel("half-adder"), "half-adder should load");

            _session.Blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Xor);
            LevelTestFixtures.Wire(
                _session.Blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell, delay: 3);

            Assert.IsTrue(_session.LoadLevel("route-the-bit"), "route-the-bit should load");

            Assert.IsTrue(_session.Blueprint.IsEmpty, "the previous level's gates and wires survived");
            Assert.AreEqual(0, _session.Blueprint.ExtraDelay(), "spent delay must go back to zero");
        }

        /// <summary>
        /// The parts rows read as spent of total, so an untouched board is 0 of 1. This is the exact
        /// figure the HUD prints, which is why it is asserted through the session rather than the
        /// blueprint.
        /// </summary>
        [Test]
        public void SwitchingLevel_ReportsNothingPlacedOnTheNewBoard()
        {
            Assert.IsTrue(_session.LoadLevel("route-the-bit"));
            _session.Blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            Assert.AreEqual(1, _session.PlacedCountOf(GateKind.Not), "one NOT is on the old board");

            Assert.IsTrue(_session.LoadLevel("balance-the-paths"));

            Assert.AreEqual(0, _session.PlacedCountOf(GateKind.Xor), "XOR row on an empty board");
            Assert.AreEqual(0, _session.PlacedCountOf(GateKind.And), "AND row on an empty board");
            Assert.AreEqual(0, _session.PlacedCountOf(GateKind.Not), "the old level's NOT lingered");
        }

        [Test]
        public void SwitchingLevel_SwapsInTheNewLevelsPartsList()
        {
            Assert.IsTrue(_session.LoadLevel("route-the-bit"));
            Assert.AreEqual(1, _session.Level.BudgetFor(GateKind.Not), "route-the-bit stocks a NOT");

            Assert.IsTrue(_session.LoadLevel("balance-the-paths"));

            Assert.AreEqual(0, _session.Level.BudgetFor(GateKind.Not), "the NOT came along with us");
            Assert.AreEqual(1, _session.Level.BudgetFor(GateKind.Xor));
            Assert.AreEqual(1, _session.Level.BudgetFor(GateKind.And));
        }

        [Test]
        public void CyclingThroughEveryLevelAndBack_LeavesTheBoardEmpty()
        {
            int count = _session.AvailableLevels.Count;
            Assert.Greater(count, 1, "a switch needs at least two levels to switch between");

            Assert.IsTrue(_session.LoadLevel(_session.AvailableLevels[0]));
            string first = _session.LevelName;

            _session.Blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            for (int i = 0; i < count; i++)
                Assert.IsTrue(_session.CycleLevel(1), $"cycle step {i}");

            Assert.AreEqual(first, _session.LevelName, "a full cycle should return to where it began");
            Assert.IsTrue(_session.Blueprint.IsEmpty, "coming back must not restore the old board");
        }

        // -----------------------------------------------------------------
        // What the palette is told
        // -----------------------------------------------------------------

        [Test]
        public void LoadingALevel_AnnouncesItSoDerivedStateCanFollow()
        {
            LevelDefinition announced = null;
            _session.LevelLoaded += level => announced = level;

            Assert.IsTrue(_session.LoadLevel("balance-the-paths"));

            Assert.IsNotNull(announced, "nothing told the palette the level had changed");
            Assert.AreEqual("Balance the paths", announced.Name);
        }

        [Test]
        public void TheAnnouncedLevel_HasAlreadyBeenClearedDown()
        {
            Assert.IsTrue(_session.LoadLevel("half-adder"));
            _session.Blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Xor);

            bool boardWasEmpty = false;
            _session.LevelLoaded += _ => boardWasEmpty = _session.Blueprint.IsEmpty;

            Assert.IsTrue(_session.LoadLevel("route-the-bit"));

            Assert.IsTrue(boardWasEmpty, "a subscriber must not see the previous level's board");
        }

        [Test]
        public void ALevelThatDoesNotStockAKind_DoesNotOfferItToThePalette()
        {
            Assert.IsTrue(_session.LoadLevel("balance-the-paths"));
            LevelDefinition level = _session.Level;

            Assert.IsFalse(level.Offers(GateKind.Not), "NOT is not on this level's parts list");
            Assert.IsTrue(level.Offers(GateKind.Xor));
            Assert.IsTrue(level.Offers(GateKind.And));
        }

        [Test]
        public void TheFirstBudgetRow_IsWhereThePaletteLands()
        {
            Assert.IsTrue(_session.LoadLevel("balance-the-paths"));

            Assert.IsTrue(_session.Level.TryFirstBudgetKind(out GateKind first));
            Assert.AreEqual(GateKind.Xor, first, "XOR is the first row of this level's budget");
        }

        /// <summary>
        /// An exhausted kind stays selectable: the player may still remove one and place it again.
        /// Offers asks what the level stocks, not what is left.
        /// </summary>
        [Test]
        public void AKindWithNoneLeft_IsStillOffered()
        {
            Assert.IsTrue(_session.LoadLevel("route-the-bit"));
            _session.Blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            Assert.AreEqual(1, _session.PlacedCountOf(GateKind.Not), "the budget is spent");
            Assert.IsTrue(_session.Level.Offers(GateKind.Not), "spent is not the same as unstocked");
        }

        [Test]
        public void AWiresOnlyLevel_HasNoPaletteSelectionToMake()
        {
            LevelDefinition wiresOnly = LevelTestFixtures.Parse(@"{
                ""name"": ""Wires only"",
                ""hint"": ""no gates, just routing"",
                ""tickLimit"": 40,
                ""fixtures"": [
                    { ""id"": ""in"",  ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""1"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"": 0 } }
                ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""1"" } ]
            }");

            Assert.IsFalse(wiresOnly.TryFirstBudgetKind(out _), "there is no part to select");
            Assert.IsFalse(wiresOnly.Offers(GateKind.Not));
        }
    }
}
