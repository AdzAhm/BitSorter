using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The guided tutorial (Part 15f), on the first exercise: which input does what, and nothing else -- take the NOT from
    /// PARTS, put it down, wire it in and out, CHECK. Each step is a predicate over the document and what is in hand, so
    /// nothing blocks input: a step not met simply does not advance, and one un-met -- the NOT deleted, Ctrl+Z -- goes back
    /// by itself, with nothing tracking the undo. What a wire's delay does, or a gate's, is left to the first-time hints
    /// (<see cref="SimulatorHints"/>), and <c>SimulatorHintTests</c> holds its words apart from theirs and the exercises'.
    /// </summary>
    public static class GuidedTutorial
    {
        /// <summary>The exercise it runs on.</summary>
        public const string ExerciseId = "route-the-bit";

        /// <summary>The ports it names, as the exercise has them.</summary>
        public const string In = "in", Out = "binOne";

        public enum Step { TakeTheNot, PutItDown, WireIn, WireOut, Check, Done }

        /// <summary>What each step asks, in order; the last is said once it is done.</summary>
        public static string TextFor(Step step)
        {
            switch (step)
            {
                case Step.TakeTheNot: return "Take the NOT: click it in PARTS, on the left.";
                case Step.PutItDown: return "Click the canvas between in and binOne to put it down there.";
                case Step.WireIn: return "Draw a wire: press on in's pin, and let go on the NOT's input pin.";
                case Step.WireOut: return "Now one from the NOT's output pin to binOne's pin.";
                case Step.Check: return "Press CHECK, at the top right, to grade the circuit.";
                default: return "Solved. EXERCISES has the rest of them, one after another.";
            }
        }

        /// <summary>Every step's words, for the four-word rule.</summary>
        public static IEnumerable<string> AllText()
        {
            for (Step step = Step.TakeTheNot; step <= Step.Done; step++)
                yield return TextFor(step);
        }

        /// <summary>
        /// The step <paramref name="project"/> is at: the first whose ask is not yet met. <paramref name="inHand"/> is what
        /// PARTS has put in hand, and <paramref name="solved"/> whether CHECK has passed it.
        /// </summary>
        public static Step At(Project project, CatalogEntry inHand, bool solved)
        {
            if (solved)
                return Step.Done;
            Module top = project?.TopModule;
            if (top == null || top.IsWritten)
                return Step.TakeTheNot;

            int not = -1;
            foreach (Part part in top.Parts)
                if (part is GatePart gate && gate.Kind == GateKind.Not)
                    not = part.Id;
            if (not < 0)
                return inHand != null && inHand.Kind == CatalogKind.Gate && inHand.Gate == GateKind.Not ? Step.PutItDown : Step.TakeTheNot;

            int input = PortId(top, In), output = PortId(top, Out);
            if (!Wired(top, input, not))
                return Step.WireIn;
            if (!Wired(top, not, output))
                return Step.WireOut;
            return Step.Check;
        }

        private static int PortId(Module top, string name)
        {
            foreach (PortPart port in top.Ports)
                if (port.Name == name)
                    return port.Id;
            return -1;
        }

        /// <summary>Whether a wire joins <paramref name="from"/> to <paramref name="to"/>, either way round.</summary>
        private static bool Wired(Module top, int from, int to)
        {
            foreach (Wire wire in top.Wires)
                if (!wire.From.IsTap && !wire.To.IsTap &&
                    ((wire.From.Pin.Part == from && wire.To.Pin.Part == to) || (wire.From.Pin.Part == to && wire.To.Pin.Part == from)))
                    return true;
            return false;
        }
    }
}
