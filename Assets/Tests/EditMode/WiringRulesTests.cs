using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The full matrix of what a port-to-port drag may and may not become. WiringRules is pure and
    /// free of UnityEngine types precisely so this can be exhaustive without a scene or a
    /// MonoBehaviour.
    /// </summary>
    public class WiringRulesTests
    {
        private Simulation _sim;
        private SourceNode _source;      // id 0, 0 in / 1 out
        private SourceNode _otherSource; // id 1, 0 in / 1 out
        private AndGate _gate;           // id 2, 2 in / 1 out
        private SinkNode _sink;          // id 3, 1 in / 0 out

        [SetUp]
        public void SetUp()
        {
            _sim = new Simulation();
            _source = _sim.Add(new SourceNode(new[] { Bit.One }) { Name = "source" });
            _otherSource = _sim.Add(new SourceNode(new[] { Bit.One }) { Name = "otherSource" });
            _gate = _sim.Add(new AndGate { Name = "gate" });
            _sink = _sim.Add(new SinkNode() { Name = "sink" });
        }

        private PortAddress Out(Node node, int index = 0) => new PortAddress(node.Id, false, index);
        private PortAddress In(Node node, int index = 0) => new PortAddress(node.Id, true, index);

        private WiringVerdict Validate(PortAddress from, PortAddress to) =>
            WiringRules.Validate(_sim.View, from, to);

        // -----------------------------------------------------------------
        // Accepted
        // -----------------------------------------------------------------

        [Test]
        public void OutputToInput_IsValid()
        {
            WiringVerdict verdict = Validate(Out(_source), In(_gate, 0));

            Assert.IsTrue(verdict.IsValid, verdict.ToString());
            Assert.AreSame(_source.Out(0), verdict.Source);
            Assert.AreSame(_gate.In(0), verdict.Target);
        }

        [Test]
        public void InputToOutput_ReverseDrag_ProducesTheSameOrientation()
        {
            WiringVerdict forward = Validate(Out(_source), In(_gate, 1));
            WiringVerdict backward = Validate(In(_gate, 1), Out(_source));

            Assert.IsTrue(backward.IsValid, backward.ToString());

            // Whichever end was grabbed, the edge is always output to input.
            Assert.AreSame(forward.Source, backward.Source);
            Assert.AreSame(forward.Target, backward.Target);
        }

        [Test]
        public void FanIn_FromADifferentOutput_IsAllowed()
        {
            _sim.Connect(_source.Out(0), _gate.In(0), 1);

            // LogicCore documents fan-in as how collisions arise and has a test that depends on it,
            // so refusing it here would make a tested capability unreachable from the game.
            WiringVerdict verdict = Validate(Out(_otherSource), In(_gate, 0));

            Assert.IsTrue(verdict.IsValid, verdict.ToString());
        }

        [Test]
        public void SelfLoop_IsAllowed()
        {
            // Well defined because every edge delay is at least 1, and it is the shape the planned
            // RegisterNode work will need.
            WiringVerdict verdict = Validate(Out(_gate), In(_gate, 0));

            Assert.IsTrue(verdict.IsValid, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // Refused
        // -----------------------------------------------------------------

        [Test]
        public void NoPortAtEitherEnd_IsRefused()
        {
            AssertRefused(Validate(Out(_source), PortAddress.None), WiringOutcome.NoPort);
            AssertRefused(Validate(PortAddress.None, In(_gate, 0)), WiringOutcome.NoPort);
        }

        [Test]
        public void ReleasingOnTheSamePort_IsRefusedSilently()
        {
            WiringVerdict verdict = Validate(Out(_source), Out(_source));

            Assert.IsFalse(verdict.IsValid);
            Assert.AreEqual(WiringOutcome.SamePort, verdict.Outcome);

            // A click that never became a drag should not scold the player.
            Assert.IsNull(verdict.Reason, "same-port rejection should carry no message");
        }

        [Test]
        public void OutputToOutput_IsRefused()
        {
            AssertRefused(Validate(Out(_source), Out(_otherSource)), WiringOutcome.SameKind);
        }

        [Test]
        public void InputToInput_IsRefused()
        {
            AssertRefused(Validate(In(_gate, 0), In(_gate, 1)), WiringOutcome.SameKind);
        }

        [Test]
        public void AnExactDuplicatePair_IsRefused()
        {
            _sim.Connect(_source.Out(0), _gate.In(0), 1);

            // Unlike fan-in from a different output, a duplicate puts two bits on the same port
            // every single tick -- never anything but a mistake.
            AssertRefused(Validate(Out(_source), In(_gate, 0)), WiringOutcome.Duplicate);
        }

        [Test]
        public void APortIndexThatDoesNotExist_IsRefused()
        {
            AssertRefused(Validate(Out(_source), In(_sink, 3)), WiringOutcome.NoPort);
            AssertRefused(Validate(Out(_sink), In(_gate, 0)), WiringOutcome.NoPort);
        }

        [Test]
        public void AnAddressPointingAtARemovedNode_IsRefusedRatherThanThrowing()
        {
            // Not hypothetical: a drag begun before the player removed a node ends holding exactly
            // this. Capture the id first, since a removed node reports Id -1.
            PortAddress doomed = In(_gate, 0);
            _sim.Remove(_gate);

            WiringVerdict verdict = WiringVerdict.Reject(WiringOutcome.Valid, null);
            Assert.DoesNotThrow(() => verdict = Validate(Out(_source), doomed));

            AssertRefused(verdict, WiringOutcome.MissingNode);
        }

        [Test]
        public void AnAddressBeyondTheIdRange_IsRefusedRatherThanThrowing()
        {
            var beyond = new PortAddress(_sim.NodeCount + 5, true, 0);

            WiringVerdict verdict = WiringVerdict.Reject(WiringOutcome.Valid, null);
            Assert.DoesNotThrow(() => verdict = Validate(Out(_source), beyond));

            AssertRefused(verdict, WiringOutcome.MissingNode);
        }

        private static void AssertRefused(WiringVerdict verdict, WiringOutcome expected)
        {
            Assert.IsFalse(verdict.IsValid, "expected a refusal");
            Assert.AreEqual(expected, verdict.Outcome);
            Assert.IsNotNull(verdict.Reason, "a refusal the player can trigger needs a reason");
            Assert.IsNull(verdict.Source);
            Assert.IsNull(verdict.Target);
        }
    }
}
