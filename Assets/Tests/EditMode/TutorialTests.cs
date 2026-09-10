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
            bool passed = false,
            bool failed = false) =>
            new BoardFacts(selected, gate, wiredIn, wiredOut, running, passed, failed);

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

        // -----------------------------------------------------------------
        // A run that fails
        // -----------------------------------------------------------------

        [Test]
        public void Normally_ThereIsNoRecoveryLine()
        {
            Assert.IsNull(TutorialScript.RecoveryText(Facts()));
            Assert.IsNull(TutorialScript.RecoveryText(Wired()));
        }

        [Test]
        public void AFailedRun_SendsThePlayerBackToRun_AndSaysWhatToPress()
        {
            // Reachable: a second wire into the bin's port collides, and the run fails. Without the
            // recovery line the watch step could never finish, run would be asked for again, and
            // pressing it would fail again -- while the panel still said the circuit works.
            BoardFacts failed = Facts(TutorialLevel.Part, true, true, true, failed: true);

            Assert.AreEqual(4, TutorialScript.CurrentStep(failed), "back to the run step");
            Assert.IsNotNull(TutorialScript.RecoveryText(failed), "and told what to press");
        }

        [Test]
        public void TheRecoveryLineGoesAwayOnceRunningAgain()
        {
            BoardFacts rerunning = Facts(TutorialLevel.Part, true, true, true,
                running: true, failed: true);

            Assert.IsNull(TutorialScript.RecoveryText(rerunning));
        }

        [Test]
        public void TheWatchStep_CompletesOnTheRunSettling_NotOnAKeyPress()
        {
            // Nothing the player might not press can be required here. Space and the right arrow are
            // mentioned in the text as things they may do, never as things they must.
            Assert.IsFalse(TutorialScript.IsComplete(5,
                Facts(TutorialLevel.Part, true, true, true, running: true)), "still in flight");

            Assert.IsTrue(TutorialScript.IsComplete(5,
                Facts(TutorialLevel.Part, true, true, true, passed: true)), "settled");
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
        public void TheBoardIsOneSourceAndOneBin()
        {
            LevelDefinition level = TutorialLevel.Build(Board);

            Assert.AreEqual(2, level.Fixtures.Count, "one source and one bin");
        }

        [Test]
        public void ThePaletteOffersThePartAndOneOther()
        {
            LevelDefinition level = TutorialLevel.Build(Board);

            Assert.AreEqual(2, level.Budget.Count, "the part asked for, and the decoy");

            bool offersPart = false;

            foreach (LevelBudgetEntry entry in level.Budget)
            {
                if (entry.Kind == TutorialLevel.Part)
                {
                    offersPart = true;
                    Assert.AreEqual(1, entry.Count, "one of the part, not a supply of them");
                }
            }

            Assert.IsTrue(offersPart, "the tutorial asks for a part it does not stock");
        }

        /// <summary>
        /// The part the tutorial asks for must not be the palette's first row.
        /// </summary>
        /// <remarks>
        /// PlacementController.SelectFirstOffered puts the selection on a level's first budget row
        /// every time a level loads. If that were the part the first step asks for, the step would
        /// already be complete before the player touched anything -- the tutorial would open on step
        /// two and nobody would learn the palette is clickable. Found in play mode, not here, which
        /// is why the guard exists now.
        /// </remarks>
        [Test]
        public void TheFirstStep_IsNotAlreadyDoneByTheAutoSelection()
        {
            LevelDefinition level = TutorialLevel.Build(Board);

            Assert.AreNotEqual(TutorialLevel.Part, level.Budget[0].Kind,
                "the part asked for is auto-selected on load, so the select step would be free");

            Assert.IsTrue(level.TryFirstBudgetKind(out GateKind first));
            Assert.AreEqual(TutorialLevel.Decoy, first, "the decoy is what arrives selected");

            // And the step really is unfinished in that state.
            Assert.IsFalse(TutorialScript.IsComplete(0, Facts(first)),
                "arriving with the decoy selected must leave the select step to do");
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
