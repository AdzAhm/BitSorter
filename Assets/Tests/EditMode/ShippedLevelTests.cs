using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Every level the game ships loads, validates and is solvable. A typo in a level file otherwise
    /// only shows up when someone presses Play, and an unsolvable level not at all until a player gives
    /// up on it.
    /// </summary>
    /// <remarks>
    /// The only tests here that touch Resources. Everything else drives LevelLoader.Parse with inline
    /// JSON, which keeps the rule matrix independent of what happens to be in Assets/Resources/Levels.
    /// </remarks>
    public class ShippedLevelTests
    {
        [TestCase("route-the-bit")]
        [TestCase("half-adder")]
        [TestCase("balance-the-paths")]
        public void AShippedLevel_LoadsAndValidates(string levelName)
        {
            LevelLoadResult result = LevelLoader.Load(levelName, LevelTestFixtures.Board);

            Assert.IsTrue(result.IsValid, $"{levelName}.json: {result.Error}");
            Assert.IsNotEmpty(result.Level.Name, "a level needs a name for the HUD");
            Assert.IsNotEmpty(result.Level.Hint, "a level needs a hint the player can read");
            Assert.Greater(result.Level.VectorCount, 0, "at least one test vector");
        }

        [Test]
        public void EveryShippedLevel_IsDiscoverableForCycling()
        {
            // Page Up and Page Down walk whatever Resources.LoadAll finds, rather than a list written
            // down somewhere, so a level that fails to turn up here would be unreachable in game.
            var found = new List<string>();

            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                if (asset != null)
                    found.Add(asset.name);
            }

            foreach (string expected in new[] { "route-the-bit", "half-adder", "balance-the-paths" })
            {
                Assert.Contains(expected, found,
                    $"'{expected}' is not discoverable under Resources/{LevelLoader.ResourcePath}");
            }
        }

        [TestCase(0, 1, 3, ExpectedResult = 1, TestName = "CyclingForward_Advances")]
        [TestCase(2, 1, 3, ExpectedResult = 0, TestName = "CyclingPastTheEnd_WrapsToTheStart")]
        [TestCase(0, -1, 3, ExpectedResult = 2, TestName = "CyclingBackFromTheFirst_WrapsToTheEnd")]
        [TestCase(1, -1, 3, ExpectedResult = 0, TestName = "CyclingBack_Retreats")]
        [TestCase(-1, 1, 3, ExpectedResult = 0, TestName = "AnUnknownLevel_StartsAtTheFirst")]
        [TestCase(-1, -1, 3, ExpectedResult = 2, TestName = "AnUnknownLevelSteppingBack_StartsAtTheLast")]
        [TestCase(0, 1, 1, ExpectedResult = 0, TestName = "ASingleLevel_StaysPut")]
        [TestCase(0, 1, 0, ExpectedResult = -1, TestName = "NoLevelsAtAll_GoesNowhere")]
        public int CyclingWrapsInBothDirections(int current, int step, int count) =>
            LevelSession.NextIndex(current, step, count);

        [Test]
        public void AMissingLevel_IsRefusedRatherThanThrowing()
        {
            LevelLoadResult result = default;

            Assert.DoesNotThrow(() =>
                result = LevelLoader.Load("no-such-level", LevelTestFixtures.Board));

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("no-such-level", result.Error, "the reason should name the file");
        }

        [Test]
        public void RouteTheBit_IsSolvedByASingleNotGate()
        {
            // Level 1's intended solution, against the real file rather than a copy of it. If the level
            // is ever retuned, this is where an unsolvable version gets caught.
            LevelLoadResult result = LevelLoader.Load("route-the-bit", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);

            LevelDefinition level = result.Level;
            var blueprint = new CircuitBlueprint();
            var gate = new Vector2Int(0, 0);

            Assert.AreEqual(1, level.BudgetFor(GateKind.Not), "the budget the solution relies on");

            blueprint.Place(gate, GateKind.Not);
            LevelTestFixtures.Wire(blueprint, new Vector2Int(-3, 0), gate);
            LevelTestFixtures.Wire(blueprint, gate, new Vector2Int(3, 1));

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void RouteTheBit_IsNotSolvedByWiringStraightToABin()
        {
            // A level solvable without using the part it hands you is not teaching anything.
            LevelLoadResult result = LevelLoader.Load("route-the-bit", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);

            var blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(blueprint, new Vector2Int(-3, 0), new Vector2Int(3, 1));

            Assert.IsFalse(LevelTestFixtures.RunAndGrade(result.Level, blueprint).IsPass,
                "the ONE bin wants a 1 and the source emits 0");
        }

        [TestCase("route-the-bit")]
        [TestCase("half-adder")]
        [TestCase("balance-the-paths")]
        public void AShippedLevel_PinsEveryFixtureInsideTheBoard(string levelName)
        {
            // The loader already refuses an off-board fixture, so this pins the levels against a future
            // change to the grid's extents rather than against a typo.
            LevelLoadResult result = LevelLoader.Load(levelName, LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);

            for (int i = 0; i < result.Level.Fixtures.Count; i++)
            {
                LevelFixture fixture = result.Level.Fixtures[i];

                Assert.LessOrEqual(Mathf.Abs(fixture.Cell.x), LevelTestFixtures.Board.x,
                    $"'{fixture.Id}' x");
                Assert.LessOrEqual(Mathf.Abs(fixture.Cell.y), LevelTestFixtures.Board.y,
                    $"'{fixture.Id}' y");
            }
        }

        [TestCase("route-the-bit")]
        [TestCase("half-adder")]
        [TestCase("balance-the-paths")]
        public void AShippedLevel_LeavesRoomForItsOwnBudget(string levelName)
        {
            // A level that hands out more gates than there are free cells cannot be finished. Cheap to
            // check, and impossible to see by reading the JSON.
            LevelLoadResult result = LevelLoader.Load(levelName, LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);

            LevelDefinition level = result.Level;

            int cells = (LevelTestFixtures.Board.x * 2 + 1) * (LevelTestFixtures.Board.y * 2 + 1);
            int free = cells - level.Fixtures.Count;

            int budgeted = 0;
            for (int i = 0; i < level.Budget.Count; i++)
                budgeted += level.Budget[i].Count;

            Assert.LessOrEqual(budgeted, free,
                $"{levelName} budgets {budgeted} gates but only {free} cells are free");
        }

        [TestCase("route-the-bit")]
        [TestCase("half-adder")]
        [TestCase("balance-the-paths")]
        public void AShippedLevel_HasACoherentDelayAllowance(string levelName)
        {
            // A budget the per-wire cap cannot absorb is a level that hands out delay the player has no
            // way to place. Cheap to check, and invisible reading the JSON.
            LevelLoadResult result = LevelLoader.Load(levelName, LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);

            LevelDefinition level = result.Level;

            Assert.GreaterOrEqual(level.MaxWireDelay, 1, "a wire has to carry at least one tick");

            if (!level.HasDelayBudget)
                return;

            Assert.Greater(level.MaxWireDelay, 1,
                $"{levelName} budgets delay but caps every wire at 1, so none of it can be spent");
        }
    }
}
