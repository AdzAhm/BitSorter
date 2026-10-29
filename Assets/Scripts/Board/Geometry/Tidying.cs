using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// TIDY on a drawn circuit (Part 15h-4f, asked 2026-10-29: "will it also have auto tidy up button?"): its parts laid out
    /// as the drawing of code is (<see cref="LayeredLayout"/>) -- the inputs on the left, each part a column past what feeds
    /// it, the outputs on the right, a register after the logic that gives it its next value -- every part unturned, every
    /// wire's bends dropped back to its automatic route, and the whole kept where the circuit was. Layout only, so what the
    /// circuit does cannot change (ADR-7).
    /// </summary>
    /// <remarks>
    /// The middle of the parts' places is kept, moved by whole quarter cells, so a tidy circuit tidied again is the same --
    /// which is how <see cref="Same"/> can say there is nothing to tidy.
    /// </remarks>
    public static class Tidying
    {
        /// <summary>The layout TIDY gives <paramref name="module"/>, a drawn one, in <paramref name="project"/>.</summary>
        public static Layout Of(Project project, Module module)
        {
            IReadOnlyList<Part> parts = module.Parts;
            var index = new Dictionary<int, int>();
            var first = new HashSet<int>();
            var last = new HashSet<int>();
            var registers = new HashSet<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                index[parts[i].Id] = i;
                if (parts[i] is PortPart port)
                    (port.Direction == PortDirection.Input ? first : last).Add(i);
                else if (parts[i] is RegisterPart)
                    registers.Add(i);
            }

            // What feeds what: every pin that drives a net, to every part reading it.
            var links = new List<(int From, int To)>();
            foreach (Net net in Netlist.Of(project, module).Nets)
                foreach (PinRef driver in net.Drivers)
                {
                    if (!index.TryGetValue(driver.Part, out int from))
                        continue;
                    foreach (PinRef pin in net.Pins)
                    {
                        if (pin.Part == driver.Part || !index.TryGetValue(pin.Part, out int to))
                            continue;
                        if (Drives(net, pin) && !net.IsInOut(pin))
                            continue;   // another driver of a bus, not a reader of it
                        links.Add((from, to));
                    }
                }

            Placement[] placed = LayeredLayout.Arrange(project, parts, links, first, last, registers);

            // Kept where the circuit was: the middle of the new places on the middle of the old.
            (double oldX, double oldY) = Middle(parts.Count, i => module.Layout.PlacementOf(parts[i].Id).Position);
            (double newX, double newY) = Middle(parts.Count, i => placed[i].Position);
            double dx = Snap(oldX - newX), dy = Snap(oldY - newY);
            var at = new Dictionary<int, Placement>();
            for (int i = 0; i < parts.Count; i++)
                at[parts[i].Id] = new Placement(new Point(placed[i].Position.X + dx, placed[i].Position.Y + dy));

            // Every wire on its automatic route; a tap keeps where along the tapped wire it joins.
            var routes = new Dictionary<int, Route>();
            foreach (Wire wire in module.Wires)
                if ((wire.From.IsTap || wire.To.IsTap) && module.Layout.Routes.TryGetValue(wire.Id, out Route route))
                    routes[wire.Id] = new Route { TapAt = route.TapAt };

            return module.Layout with { Parts = at, Routes = routes };
        }

        /// <summary>Whether two layouts put every part and wire in the same place -- the view's pan and zoom aside.</summary>
        public static bool Same(Layout a, Layout b)
        {
            if (a.Parts.Count != b.Parts.Count || a.Routes.Count != b.Routes.Count)
                return false;
            foreach (KeyValuePair<int, Placement> part in a.Parts)
                if (!b.Parts.TryGetValue(part.Key, out Placement other) || !other.Equals(part.Value))
                    return false;
            foreach (KeyValuePair<int, Route> route in a.Routes)
            {
                if (!b.Routes.TryGetValue(route.Key, out Route other) || other.Smooth != route.Value.Smooth ||
                    !other.TapAt.Equals(route.Value.TapAt) || other.Bends.Count != route.Value.Bends.Count)
                    return false;
                for (int i = 0; i < other.Bends.Count; i++)
                    if (!other.Bends[i].Equals(route.Value.Bends[i]))
                        return false;
            }
            return true;
        }

        private static bool Drives(Net net, PinRef pin)
        {
            foreach (PinRef driver in net.Drivers)
                if (driver.Equals(pin))
                    return true;
            return false;
        }

        /// <summary>The middle of the box round <paramref name="count"/> places.</summary>
        private static (double X, double Y) Middle(int count, Func<int, Point> place)
        {
            if (count == 0)
                return (0, 0);
            double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                Point p = place(i);
                left = Math.Min(left, p.X);
                right = Math.Max(right, p.X);
                bottom = Math.Min(bottom, p.Y);
                top = Math.Max(top, p.Y);
            }
            return ((left + right) / 2, (bottom + top) / 2);
        }

        private static double Snap(double value) => Math.Round(value * 4) / 4;
    }
}
