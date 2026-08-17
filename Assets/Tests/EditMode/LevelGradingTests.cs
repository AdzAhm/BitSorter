using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// What counts as a pass, and what each kind of failure reports. Every case here runs the real
    /// simulation -- there is no separate oracle anywhere in the game, because the whole subject is
    /// consume semantics, collisions and delay arithmetic, and a second implementation of those would
    /// eventually disagree with the one the player is watching.
    /// </summary>
    public class LevelGradingTests
    {
        private LevelDefinition _level;
        private CircuitBlueprint _blueprint;

        [SetUp]
        public void SetUp()
        {
            _level = LevelTestFixtures.Routing();
            _blueprint = new CircuitBlueprint();
        }

        private RunVerdict Grade() => LevelTestFixtures.RunAndGrade(_level, _blueprint);

        /// <summary>The intended solution: source -> NOT -> the ONE bin, with the ZERO bin left alone.</summary>
        private void SolveRouting()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinOneCell);
        }

        // -----------------------------------------------------------------
        // Pass
        // -----------------------------------------------------------------

        [Test]
        public void TheIntendedSolution_Passes()
        {
            SolveRouting();

            RunVerdict verdict = Grade();

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.AreEqual(RunOutcome.Pass, verdict.Outcome);
            Assert.IsNotNull(verdict.Reason, "a pass should say what it verified");
        }

        [Test]
        public void APassIgnoresHowLongThePathWas()
        {
            // Arrival ticks are not graded, deliberately: how long a route the player took is their
            // choice. Wiring the same solution through a longer wire must still pass.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(
                _blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell, delay: 5);
            LevelTestFixtures.Wire(
                _blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinOneCell, delay: 4);

            RunVerdict verdict = Grade();

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // Failures
        // -----------------------------------------------------------------

        [Test]
        public void NothingWired_FailsAsMissingOutput()
        {
            RunVerdict verdict = Grade();

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.MissingOutput, verdict.Outcome);
            Assert.AreEqual("binOne", verdict.SinkId, "the bin that came up short");
            Assert.AreEqual(0, verdict.Vector, "the level has one vector, so it must be vector 0");
        }

        [Test]
        public void SkippingTheGate_FailsAsWrongOutput()
        {
            // The source emits 0 and the ONE bin wants a 1, so a direct wire delivers the wrong value
            // rather than no value. That distinction is the difference between "you forgot something"
            // and "the thing you built computes the wrong answer".
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.BinOneCell);

            RunVerdict verdict = Grade();

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual("binOne", verdict.SinkId);
            Assert.AreEqual(0, verdict.Vector);
        }

        [Test]
        public void WiringIntoBothBins_FailsAsExtraOutput()
        {
            // The rule that stops the cheese. Without grading the bin that is meant to stay empty, a
            // player could wire the answer into every bin and let the grader find the one that fits.
            SolveRouting();
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinZeroCell);

            RunVerdict verdict = Grade();

            Assert.IsFalse(verdict.IsPass, "the ONE bin being right must not excuse the ZERO bin");
            Assert.AreEqual(RunOutcome.ExtraOutput, verdict.Outcome);
            Assert.AreEqual("binZero", verdict.SinkId);
            StringAssert.Contains("empty", verdict.Reason, "an empty bin deserves its own wording");
        }

        [Test]
        public void AFedBackLoop_FailsAsNeverSettled()
        {
            // WiringRules allows self-loops on purpose, and a NOT fed by both a source and its own
            // output never stops flipping. Without a tick limit the run would simply hang.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.MiddleCell);

            RunVerdict verdict = Grade();

            Assert.AreEqual(RunOutcome.NeverSettled, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(-1, verdict.Vector, "a runaway loop cannot be pinned on one vector");
        }

        [Test]
        public void UnbalancedPathsIntoOnePort_FailAsCorrupted()
        {
            // Two routes of different length into the same port: the early bit latches, and the next
            // arrival collides with it. This is the hazard CorruptedCount exists to make visible.
            LevelDefinition level = LevelTestFixtures.FourVectors("0011");
            var blueprint = new CircuitBlueprint();
            var sink = new Vector2Int(3, 0);

            blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, sink);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.MiddleCell, sink);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void CorruptionIsReportedBeforeASequenceMismatch()
        {
            // A collision usually breaks the sequence too. "Bits were destroyed" tells the player far
            // more about what to fix than "expected 1, received nothing" does, so it is checked first.
            LevelDefinition level = LevelTestFixtures.FourVectors("0011");
            var blueprint = new CircuitBlueprint();
            var sink = new Vector2Int(3, 0);

            blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, sink);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.MiddleCell, sink);

            BuiltCircuit built = CircuitBuilder.Build(level, blueprint);
            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, level, built.FixtureNodeIds);

            Assert.Greater(built.Simulation.CorruptedCount, 0, "the circuit really does corrupt");
            Assert.AreNotEqual(RunOutcome.MissingOutput, verdict.Outcome,
                "the sequence is broken too, but corruption is the more useful answer");
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome);
        }

        // -----------------------------------------------------------------
        // Which vector failed
        // -----------------------------------------------------------------

        [Test]
        public void AMismatchNamesTheVectorThatProducedIt()
        {
            // Pass-through delivers 0,0,1,1 but the level asks for 0,0,1,0, so the first three match
            // and the fourth does not. Reporting "vector 0" here would send the player looking in the
            // wrong place.
            LevelDefinition level = LevelTestFixtures.FourVectors("0010");
            var blueprint = new CircuitBlueprint();

            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 0));

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(3, verdict.Vector, "the fourth vector is the one that differs");
            StringAssert.Contains("vector 3", verdict.Reason);
        }

        [Test]
        public void GapsInAnExpectation_DoNotShiftTheVectorNumbering()
        {
            // "--10" means vectors 0 and 1 produce nothing here, so the sink's first expected bit
            // belongs to vector 2. Counting receptions instead of vectors would have blamed vector 0.
            LevelDefinition level = LevelTestFixtures.FourVectors("--10");
            var blueprint = new CircuitBlueprint();

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.AreEqual(RunOutcome.MissingOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(2, verdict.Vector, "the first expected bit belongs to vector 2, not vector 0");
        }

        // -----------------------------------------------------------------
        // Don't-care outputs
        // -----------------------------------------------------------------

        /// <summary>The source stream is 0011, so a bare wire delivers 0, 0, 1, 1.</summary>
        private static CircuitBlueprint PassThroughToSink()
        {
            var blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 0));
            return blueprint;
        }

        [Test]
        public void ADontCare_AcceptsTheValueALiteralWouldReject()
        {
            // The pass-through delivers 0 on vector 1. Spelled as a literal 1 that is a failure;
            // spelled as 'x' it is fine. Same circuit, same run, only the expectation differs -- which
            // is the only way to show that 'x' is not just a differently written literal.
            CircuitBlueprint blueprint = PassThroughToSink();

            RunVerdict asLiteral = LevelTestFixtures.RunAndGrade(
                LevelTestFixtures.FourVectors("0111"), blueprint);

            Assert.AreEqual(RunOutcome.WrongOutput, asLiteral.Outcome,
                "sanity: vector 1 really does deliver a 0");
            Assert.AreEqual(1, asLiteral.Vector);

            RunVerdict asDontCare = LevelTestFixtures.RunAndGrade(
                LevelTestFixtures.FourVectors("0x11"), blueprint);

            Assert.IsTrue(asDontCare.IsPass, asDontCare.ToString());
        }

        [Test]
        public void ADontCare_AlsoAcceptsTheValueALiteralWouldHaveMatched()
        {
            // The other half of "either value passes". Vector 1 delivers a 0, and a literal 0 passes
            // there too -- so 'x' must not have narrowed anything.
            CircuitBlueprint blueprint = PassThroughToSink();

            Assert.IsTrue(LevelTestFixtures.RunAndGrade(
                LevelTestFixtures.FourVectors("0011"), blueprint).IsPass, "literal 0 passes");

            Assert.IsTrue(LevelTestFixtures.RunAndGrade(
                LevelTestFixtures.FourVectors("0x11"), blueprint).IsPass, "so must the don't-care");
        }

        [Test]
        public void ADontCare_StillRequiresABitToArrive()
        {
            // 'x' constrains the value, never the arrival. Four don't-cares must not turn an unwired
            // board into a pass -- that is what '-' is for, and conflating the two would make an
            // empty-bin level unenforceable.
            LevelDefinition level = LevelTestFixtures.FourVectors("xxxx");
            var blueprint = new CircuitBlueprint();

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.IsFalse(verdict.IsPass, "nothing arrived, so there is nothing to excuse");
            Assert.AreEqual(RunOutcome.MissingOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(0, verdict.Vector);
        }

        // -----------------------------------------------------------------
        // Silent vectors
        // -----------------------------------------------------------------

        [Test]
        public void ASpuriousBitOnASilentVector_NamesThatVector()
        {
            // "0-11" says vector 1 produces nothing at this sink. A bare pass-through emits on every
            // vector, so it puts a bit exactly where the level asked for silence.
            //
            // The defect this guards: '-' vectors are dropped from the expected list, so the value
            // comparison runs positionally over a list with holes in it. The intruder shifts every
            // later reception by one and the first mismatch is blamed on vector 2 -- whose wiring is
            // perfectly correct. That sends the player to the wrong end of the board.
            LevelDefinition level = LevelTestFixtures.FourVectors("0-11");
            CircuitBlueprint blueprint = PassThroughToSink();

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.IsFalse(verdict.IsPass, "a bit arrived where the level asked for silence");
            Assert.AreEqual(RunOutcome.ExtraOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(1, verdict.Vector, "vector 1 is the one that should have stayed silent");
            StringAssert.Contains("vector 1", verdict.Reason);
        }

        [Test]
        public void ASpuriousBitThatHappensToMatch_StillNamesItsVector()
        {
            // The quieter half of the same defect. With "0-01" the intruder's value coincidentally
            // matches what the following slot wanted, so every positional comparison passes and the
            // failure degrades to a bare count mismatch naming no vector at all. Same mistake, same
            // sink, and the player is given even less to go on.
            LevelDefinition level = LevelTestFixtures.FourVectors("0-01");
            CircuitBlueprint blueprint = PassThroughToSink();

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.ExtraOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(1, verdict.Vector, "the intruder still belongs to vector 1");
        }

        [Test]
        public void ASinkExpectedToStayEmpty_KeepsItsOwnWording()
        {
            // The all-'-' case must not be swallowed by the fix above. There is no expected bit to
            // measure latency against, so the vector cannot be named -- but "should have stayed
            // empty" already says everything the player needs, and it stays.
            LevelDefinition level = LevelTestFixtures.FourVectors("----");
            CircuitBlueprint blueprint = PassThroughToSink();

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, blueprint);

            Assert.AreEqual(RunOutcome.ExtraOutput, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("empty", verdict.Reason);
        }

        // -----------------------------------------------------------------
        // Latency
        // -----------------------------------------------------------------

        /// <summary>A bare wire to the sink, at whatever delay the test needs.</summary>
        private static CircuitBlueprint PassThroughAtDelay(int delay)
        {
            var blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(
                blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 0), delay: delay);
            return blueprint;
        }

        [Test]
        public void ACircuitOverTheLatencyCeiling_Fails()
        {
            // Vector v leaves on tick v and is consumed on tick v + 5, so the latency is 5 against a
            // ceiling of 2. Values and order are perfect and only the arrival ticks differ, which
            // makes this the one failure that is purely about time.
            LevelDefinition level = LevelTestFixtures.FourVectors("0011", maxLatency: 2);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, PassThroughAtDelay(5));

            Assert.IsFalse(verdict.IsPass, "5 ticks of latency against a ceiling of 2");
            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            Assert.AreEqual("out", verdict.SinkId);
            Assert.AreEqual(-1, verdict.Vector, "a long critical path is not one vector's fault");
            StringAssert.Contains("5", verdict.Reason, "the measured latency belongs in the message");
        }

        [Test]
        public void ACircuitInsideTheLatencyCeiling_Passes()
        {
            LevelDefinition level = LevelTestFixtures.FourVectors("0011", maxLatency: 4);

            Assert.IsTrue(LevelTestFixtures.RunAndGrade(level, PassThroughAtDelay(1)).IsPass);
        }

        [Test]
        public void TheLatencyCeilingIsInclusive()
        {
            // A level author sets this to the critical path of the intended solution, so an
            // off-by-one here would make the intended solution the one thing that cannot be built.
            LevelDefinition level = LevelTestFixtures.FourVectors("0011", maxLatency: 3);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, PassThroughAtDelay(3));

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void ALevelWithNoLatencyCeiling_DoesNotGradeOnTime()
        {
            // The default, and how every level written before this one behaves. Grading on arrival
            // ticks by accident would fail a correct circuit for taking the scenic route.
            LevelDefinition level = LevelTestFixtures.FourVectors("0011");

            Assert.IsTrue(LevelTestFixtures.RunAndGrade(level, PassThroughAtDelay(9)).IsPass);
        }

        [Test]
        public void AWrongAnswerIsReportedBeforeASlowOne()
        {
            // Order of complaint. A circuit that is both wrong and slow should hear that it is wrong
            // first -- shortening a path that computes the wrong thing is wasted work.
            LevelDefinition level = LevelTestFixtures.FourVectors("0010", maxLatency: 1);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, PassThroughAtDelay(5));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(3, verdict.Vector, "vector 3 is the value mismatch");
        }

        // -----------------------------------------------------------------
        // Settling
        // -----------------------------------------------------------------

        [Test]
        public void AnUnbuiltCircuit_SettlesOnceTheSourcesAreSpent()
        {
            // A source with nowhere to emit still runs dry, so an empty board grades immediately rather
            // than sitting at RUNNING until the tick limit.
            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);

            Assert.IsFalse(LevelGrader.IsSettled(built.Simulation.View), "the source has a bit to emit");

            built.Simulation.Tick();

            Assert.IsTrue(LevelGrader.IsSettled(built.Simulation.View), "nothing left to happen");
        }

        [Test]
        public void ABitStrandedInAPort_StillCountsAsSettled()
        {
            // An AND given one input and never the other waits forever. That is terminal, not busy:
            // nothing is in flight, so nothing can become ready. Treating it as busy would hang the run
            // on the stranded bit instead of reporting the far more useful missing output.
            //
            // A single-bit source matters here. Feeding a stream into a port nothing empties would
            // collide on the second arrival, and the verdict would be about corruption instead.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.And);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);

            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);
            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.AreEqual(0, built.Simulation.CorruptedCount, "one bit cannot collide with anything");
            Assert.IsTrue(built.Simulation.GetNode(built.FixtureNodeIds["in"]) is SourceNode,
                "sanity: fixture ids resolve to the nodes they name");

            Assert.AreNotEqual(RunOutcome.NeverSettled, verdict.Outcome,
                "a stranded bit is a settled circuit with a missing output");
            Assert.AreEqual(RunOutcome.MissingOutput, verdict.Outcome, verdict.ToString());
        }
    }
}
