using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The register as a part the player can hold: what it places, what it is called, and what a
    /// level or a save file has to write to ask for one.
    /// </summary>
    public class RegisterPartTests
    {
        [Test]
        public void ThePaletteKnowsEveryKindItHas()
        {
            // Count is used to walk the palette. Written by hand, it was one behind the enum the
            // moment the register was added, and a part nobody could reach would be the symptom.
            Assert.AreEqual(System.Enum.GetValues(typeof(GateKind)).Length, GatePalette.Count);
        }

        [Test]
        public void PlacingARegister_BuildsOne()
        {
            Node placed = GatePalette.Create(GateKind.Register);

            Assert.IsInstanceOf<RegisterNode>(placed);
            Assert.AreEqual(Bit.Zero, ((RegisterNode)placed).State, "a placed register starts at 0");
        }

        [Test]
        public void ARegisterHasOneInput()
        {
            // The one port is D. Everything else on the palette bar NOT takes two, and the wiring
            // rules ask this rather than the node, so a wrong answer here would refuse legal wires.
            Assert.AreEqual(1, GatePalette.InputsOf(GateKind.Register));
            Assert.AreEqual(1, GatePalette.Create(GateKind.Register).InputCount);
        }

        [TestCase("Register")]
        [TestCase("register")]
        [TestCase("  REGISTER  ")]
        public void ALevelOrSaveAsksForOneByName(string written)
        {
            // Level budgets and saved boards both spell kinds as text, and both go through here.
            Assert.IsTrue(GatePalette.TryParse(written, out GateKind kind), written);
            Assert.AreEqual(GateKind.Register, kind);
        }

        [Test]
        public void ItIsLabelledOnThePartsList()
        {
            Assert.AreEqual("REG", GatePalette.Label(GateKind.Register));
        }
    }
}
