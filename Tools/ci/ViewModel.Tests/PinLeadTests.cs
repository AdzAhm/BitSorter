using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A lead on every pin (Part 15g): a short line from the pin to the body, which ends in the open circle a wire starts
    /// from. Ahmad asked for "a small tiny line that we can wire from" (2026-10-28) -- a port's box and a constant's stood
    /// on their pins, and a block's a quarter of a cell from them. The bodies stepped back; the pins did not move, so no
    /// wire, route or saved circuit does.
    /// </summary>
    public class PinLeadTests
    {
        private static readonly SchematicLayout.Frame Frame = new SchematicLayout.Frame(40f, -10, 10, 0f);

        /// <summary>Every part the strip offers, with a block among them, as the strip makes it.</summary>
        private static IEnumerable<(string Key, Project Project, Part Part)> EveryPart()
        {
            Project project = WorkbenchSamples.FullAdder();   // its half adder is a block to place
            foreach (CatalogEntry entry in PartsCatalog.For(project, project.Top))
                yield return (entry.Key, project, PartsCatalog.Make(project, project.Top, entry));
        }

        [Test]
        public void EveryPinOfEveryPart_HasALeadOfAtLeastAQuarterCell()
        {
            int pins = 0;
            foreach ((string key, Project project, Part part) in EveryPart())
            {
                PartOutline local = PartGeometry.Local(project, part);
                foreach (PinSpot pin in local.Pins)
                {
                    (double x, double y) = part is PortPart
                        ? SymbolGeometry.PortLeadEnd(pin)
                        : SymbolGeometry.LeadEnd(Layout(part, local), pin);
                    double lead = Math.Sqrt((x - pin.At.X) * (x - pin.At.X) + (y - pin.At.Y) * (y - pin.At.Y));
                    Assert.GreaterOrEqual(lead, 0.25 - 1e-9, $"{key}.{pin.Pin} has a lead of {lead} cells");
                    pins++;
                }
            }

            Assert.Greater(pins, 30, "sanity: the strip's parts and their pins were all asked");
        }

        [Test]
        public void APortsBox_StandsALeadInFromItsPin_InsideItsOutline()
        {
            foreach ((string key, Project project, Part part) in EveryPart())
            {
                if (!(part is PortPart))
                    continue;
                PartOutline local = PartGeometry.Local(project, part);
                PinSpot pin = local.Pins.Single();
                (double left, double bottom, double right, double top) = SymbolGeometry.PortBox(local);

                double near = pin.Facing == Facing.Left ? left : right;
                Assert.AreEqual(SymbolGeometry.Lead, Math.Abs(near - pin.At.X), 1e-9, $"{key}'s box a lead from its pin");
                Assert.AreEqual(SymbolGeometry.PortLeadEnd(pin).X, near, 1e-9, $"{key}'s lead ends on its box");
                Assert.IsTrue(left >= local.Left && right <= local.Right && bottom >= local.Bottom && top <= local.Top,
                    $"{key}'s box inside its outline, which routes keep clear of and which did not change");
                Assert.Greater(right - left, 0.25, $"{key}'s box keeps room for its name");
            }
        }

        /// <summary>
        /// The pins of the parts whose bodies stepped back, where they stood before Part 15g. A pin moved would move every
        /// wire on it, and every circuit saved with one.
        /// </summary>
        [TestCase("input", "out", 0.5, 0.0)]
        [TestCase("output", "in", -0.5, 0.0)]
        [TestCase("constant", "y", 0.5, 0.0)]
        [TestCase("and", "a", -1.0, 0.25)]
        [TestCase("and", "b", -1.0, -0.25)]
        [TestCase("and", "y", 1.0, 0.0)]
        public void ThePins_StayWhereTheyWere(string key, string pin, double x, double y)
        {
            (string _, Project project, Part part) = EveryPart().Single(p => p.Key == key);
            Assert.IsTrue(PartGeometry.Local(project, part).TryPin(pin, out PinSpot spot), $"{key} has no pin {pin}");
            Assert.AreEqual(x, spot.At.X, 1e-9);
            Assert.AreEqual(y, spot.At.Y, 1e-9);
        }

        [Test]
        public void ABlocksPins_StayACellAndAHalfOut_ItsBoxALeadIn()
        {
            (string key, Project project, Part part) = EveryPart().Single(p => p.Part is BlockPart);
            PartOutline local = PartGeometry.Local(project, part);
            foreach (PinSpot pin in local.Pins)
            {
                Assert.AreEqual(pin.Facing == Facing.Left ? -1.5 : 1.5, pin.At.X, 1e-9, $"{key}.{pin.Pin} where it stood");
                Assert.AreEqual(pin.Facing == Facing.Left ? -SymbolGeometry.BlockHalf : SymbolGeometry.BlockHalf,
                    SymbolGeometry.LeadEnd(Layout(part, local), pin).X, 1e-9, $"{key}.{pin.Pin}'s lead ends on the box");
            }
        }

        /// <summary>A constant's box keeps the room its literal had: its outline grew by the lead, away from its pin.</summary>
        [Test]
        public void AConstantsBox_KeepsItsLiteralsRoom_ALeadShortOfItsPin()
        {
            (string _, Project project, Part part) = EveryPart().Single(p => p.Key == "constant");
            PartOutline local = PartGeometry.Local(project, part);
            PinSpot pin = local.Pins.Single();
            double boxRight = SymbolGeometry.LeadEnd(Layout(part, local), pin).X;
            Assert.AreEqual(pin.At.X - SymbolGeometry.Lead, boxRight, 1e-9);
            Assert.GreaterOrEqual(boxRight - local.Left, 1.0 - 1e-9, "at least the cell a constant's box always had");
        }

        private static PartLayout Layout(Part part, PartOutline local) =>
            new PartLayout(DocumentSchematic.SymbolOf(part), part, new Placement(new BoardPoint(0, 0)), local, "p", "", null,
                Array.Empty<string>(), Frame);
    }
}
