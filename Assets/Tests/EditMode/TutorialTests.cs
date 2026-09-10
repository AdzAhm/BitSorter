using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="TutorialScript"/> and <see cref="TutorialLevel"/>: the six steps, what finishes
    /// each, and the board they run on.
    /// </summary>
    /// <remarks>
    /// The property worth testing hardest is that a step un-finishes. Steps are read off board state
    /// rather than remembered, so deleting a wire has to send the tutorial back a step with nothing
    /// anywhere tracking the undo -- and a step that latched would leave the highlight pointing at
    /// something the player has already taken apart.
    /// </remarks>
    public class TutorialTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private static BoardFacts Facts(
            GateKind selected = GateKind.And,
            bool gate = false,
            bool wiredIn = false,
            bool wiredOut = false,
            bool running = false,
            bool passed = false) =>
            new BoardFacts(selected, gate, wiredIn, wiredOut, running, passed);

        /// <summary>Everything done up to and including the wiring.</summary>
        private static BoardFacts Wired() =>
            Facts(TutorialLevel.Part, gate: true, wiredIn: true, wiredOut: true);

        // -----------------------------------------------------------------
        // The steps themselves
        // -----------------------------------------------------------------

        [Test]
        public void ThereAreSixSteps_EachWithTextAndATarget()
        {
            Assert.AreEqual(6, TutorialScript.Count, "six steps, deliberately, not fifteen");

            foreach (TutorialStep step in TutorialScript.Steps)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.Text), $"'{step.Id}' says nothing");
                Assert.AreNotEqual(TutorialTarget.None, step.From, $"'{step.Id}' points at nothing");
            }
        }

        [Test]
        public void WiringStepsPointAtBothEnds()
        {
            // A drag needs somewhere to start and somewhere to land. Highlighting only the source
            // leaves the player holding a wire with nowhere to put it.
            foreach (TutorialStep step in TutorialScript.Steps)
            {
                if (step.Id != TutorialScript.WireInId && step.Id != TutorialScript.WireOutId)
                    continue;

                Assert.AreNotEqual(TutorialTarget.None, step.To, $"'{step.Id}' has no destination");
            }
        }

        // -----------------------------------------------------------------
        // Walking forwards
        // -----------------------------------------------------------------

        [Test]
        public void AnUntouchedBoard_IsOnTheFirstStep()
        {
            Assert.AreEqual(0, TutorialScript.CurrentStep(Facts()));
        }

        [Test]
        public void EachActionAdvancesExactlyOneStep()
        {
            BoardFacts f = Facts();
            Assert.AreEqual(0, TutorialScript.CurrentStep(f), "select the part");

            f = Facts(TutorialLevel.Part);
            Assert.AreEqual(1, TutorialScript.CurrentStep(f), "place it");

            f = Facts(TutorialLevel.Part, gate: true);
            Assert.AreEqual(2, TutorialScript.CurrentStep(f), "wire the source in");

            f = Facts(TutorialLevel.Part, gate: true, wiredIn: true);
            Assert.AreEqual(3, TutorialScript.CurrentStep(f), "wire the bin up");

            f = Wired();
            Assert.AreEqual(4, TutorialScript.CurrentStep(f), "press run");

            f = Facts(TutorialLevel.Part, true, true, true, running: true);
            Assert.AreEqual(5, TutorialScript.CurrentStep(f), "watch it land");

            f = Facts(TutorialLevel.Part, true, true, true, passed: true);
            Assert.AreEqual(TutorialScript.Count, TutorialScript.CurrentStep(f), "and done");
        }

        [Test]
        public void SelectingSomethingElse_DoesNotAdvance()
        {
            // Wandering is legal and simply does not count. Nothing is refused.
            Assert.AreEqual(0, TutorialScript.CurrentStep(Facts(GateKind.Xor)));
        }

        // -----------------------------------------------------------------
        // Walking backwards, which is the point of predicates
        // -----------------------------------------------------------------

        [Test]
        public void DeletingTheWire_SendsTheTutorialBack()
        {
            Assert.AreEqual(4, TutorialScript.CurrentStep(Wired()), "both wires made");

            BoardFacts undone = Facts(TutorialLevel.Part, gate: true, wiredIn: true);

            Assert.AreEqual(3, TutorialScript.CurrentStep(undone),
                "removing the second wire must ask for it again");
        }

        [Test]
        public void RemovingTheGate_SendsTheTutorialBackFurther()
        {
            // What Ctrl+Z on the placement looks like. Nothing tracks the undo; the step is simply
            // asked again because the board no longer satisfies it.
            BoardFacts undone = Facts(TutorialLevel.Part, gate: false, wiredIn: false);

            Assert.AreEqual(1, TutorialScript.CurrentStep(undone));
        }

        [Test]
        public void AFinishedRun_CountsAsHavingPressedRun()
        {
            // A run is over in a couple of seconds. If only Running counted, a director that looked
            // after it settled would ask the player to press a button they had already pressed.
            Assert.IsTrue(TutorialScript.IsComplete(4,
                Facts(TutorialLevel.Part, true, true, true, running: false, passed: true)));
        }

        [Test]
        public void AnIndexPastTheEnd_IsComplete()
        {
            Assert.IsTrue(TutorialScript.IsComplete(TutorialScript.Count, Wired()));
            Assert.IsTrue(TutorialScript.IsComplete(-1, Wired()));
        }

        // -----------------------------------------------------------------
        // The board it runs on
        // -----------------------------------------------------------------

        [Test]
        public void TheBoardIsOneSourceOneBinAndOnePart()
        {
            LevelDefinition level = TutorialLevel.Build(Board);

            Assert.AreEqual(2, level.Fixtures.Count, "one source and one bin");
            Assert.AreEqual(1, level.Budget.Count, "exactly one kind of part is offered");
            Assert.AreEqual(TutorialLevel.Part, level.Budget[0].Kind);
            Assert.AreEqual(1, level.Budget[0].Count, "and only one of it");
        }

        [Test]
        public void TheBoardIsGraded_SoItEndsOnARealPass()
        {
            LevelDefinition level = TutorialLevel.Build(Board);

            Assert.IsTrue(level.IsGraded, "the last step is a win, and a win needs grading");
            Assert.AreEqual(1, level.Expectations.Count);
            Assert.AreEqual(1, level.VectorCount, "one bit crosses the board, not a stream");
        }

        [Test]
        public void TheGateCellIsClearOfBothFixtures()
        {
            // The highlighted square has to be somewhere the player can actually drop a part.
            LevelDefinition level = TutorialLevel.Build(Board);

            foreach (LevelFixture fixture in level.Fixtures)
            {
                Assert.AreNotEqual(TutorialLevel.GateCell, fixture.Cell,
                    $"'{fixture.Id}' is sitting on the cell the tutorial highlights");
            }
        }

        [Test]
        public void TheFixturesAreOnTheBoard()
        {
            LevelDefinition level = TutorialLevel.Build(Board);

            foreach (LevelFixture fixture in level.Fixtures)
            {
                Assert.LessOrEqual(Mathf.Abs(fixture.Cell.x), Board.x, $"'{fixture.Id}' is off-board");
                Assert.LessOrEqual(Mathf.Abs(fixture.Cell.y), Board.y, $"'{fixture.Id}' is off-board");
            }
        }

        [Test]
        public void TheTutorialIsNotOneOfTheNineLevels()
        {
            // Its key is not a file name, and nothing in Resources/Levels answers to it -- which is
            // what keeps it out of the catalogue, out of Q and E, and out of the "1 / 9" count.
            Assert.AreNotEqual(TutorialLevel.Key, SandboxLevel.Key, "two keys, two different things");

            var asset = Resources.Load<TextAsset>($"{LevelLoader.ResourcePath}/{TutorialLevel.Key}");
            Assert.IsNull(asset, "the tutorial must not exist as a level file");
        }
    }
}
