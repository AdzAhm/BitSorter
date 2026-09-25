using System.Collections.Generic;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>What a step is pointing at.</summary>
    public enum TutorialTarget
    {
        None,
        PaletteEntry,
        BoardCell,
        SourcePort,
        GateInput,
        GateOutput,
        SinkPort,
        RunButton,
        Bin,
    }

    /// <summary>One instruction, what it highlights, and nothing else.</summary>
    /// <remarks>
    /// Two targets because wiring is a gesture between two places, and highlighting only the end a
    /// drag starts from leaves the player holding a wire with nowhere to put it.
    /// </remarks>
    public readonly struct TutorialStep
    {
        public readonly string Id;
        public readonly string Text;
        public readonly TutorialTarget From;
        public readonly TutorialTarget To;

        public TutorialStep(string id, string text, TutorialTarget from,
            TutorialTarget to = TutorialTarget.None)
        {
            Id = id;
            Text = text;
            From = from;
            To = to;
        }

        public override string ToString() => Id;
    }

    /// <summary>
    /// Everything about the board a step needs in order to decide whether it is finished.
    /// </summary>
    /// <remarks>
    /// Gathered by <see cref="TutorialDirector"/> and handed here as plain values, so every step's
    /// condition is a pure function of board state and can be tested without a scene -- the split
    /// <see cref="PortState"/> and <see cref="HintRules"/> already use.
    ///
    /// Every field is a fact about the board *now*, never a record of something that happened. That
    /// is what lets a step un-finish: delete the wire and the step it belonged to comes back,
    /// including through Ctrl+Z, with nothing tracking the undo.
    /// </remarks>
    public readonly struct BoardFacts
    {
        public readonly GateKind Selected;
        public readonly bool GateOnCell;
        public readonly bool SourceWiredToGate;
        public readonly bool GateWiredToBin;
        public readonly bool Running;
        public readonly bool Passed;

        /// <summary>The last run ended without the bit arriving as expected.</summary>
        /// <remarks>
        /// Reachable even here: WiringRules allows a second wire into one input port, so a player
        /// who also runs A straight to the bin gets two arrivals and a failed run. Without this the
        /// watch step could never finish, the run step would ask for RUN again, and pressing it
        /// would fail again -- a loop, with the panel still insisting the circuit works.
        /// </remarks>
        public readonly bool RunFailed;

        /// <summary>
        /// Whatever part stands on the square the tutorial's part belongs on, or null.
        /// </summary>
        /// <remarks>
        /// <see cref="GateOnCell"/> says whether the right part is there; this says what is, so a
        /// wrong one can be named. The decoy on that square blocks the step twice over: it is not the
        /// part the step wants, and it refuses the click that would put the right one down.
        /// </remarks>
        public readonly GateKind? PartOnCell;

        public BoardFacts(GateKind selected, bool gateOnCell, bool sourceWiredToGate,
            bool gateWiredToBin, bool running, bool passed, bool runFailed = false,
            GateKind? partOnCell = null)
        {
            Selected = selected;
            GateOnCell = gateOnCell;
            SourceWiredToGate = sourceWiredToGate;
            GateWiredToBin = gateWiredToBin;
            Running = running;
            Passed = passed;
            RunFailed = runFailed;
            PartOnCell = partOnCell;
        }
    }

    /// <summary>
    /// The six steps, in order, and what finishes each one.
    /// </summary>
    /// <remarks>
    /// These teach **which input does what**, and stop there. A level's goal says what you are
    /// trying to do, its hint how to solve that level, and a first-time hint explains why the board
    /// just behaved as it did. So a step here may say that a wire can be scrolled -- that is an
    /// input -- but must not say what a longer wire does to arrival order, because that is the
    /// wireDelay hint's job on the level where it starts to matter. Finishing the tutorial marks no
    /// hint as seen.
    ///
    /// Nothing here blocks anything. A step that is not satisfied simply does not advance, and every
    /// other action stays as legal as it was. Refusing input would mean reaching into
    /// PlacementController, WiringController and PaletteDragSource, and CLAUDE.md is explicit that
    /// pointer ownership is derived and never claimed, because a claim that leaks disables the game
    /// with no way for the player to recover.
    /// </remarks>
    public static class TutorialScript
    {
        public const string SelectId = "select";
        public const string PlaceId = "place";
        public const string WireInId = "wireIn";
        public const string WireOutId = "wireOut";
        public const string RunId = "run";
        public const string WatchId = "watch";

        public static IReadOnlyList<TutorialStep> Steps { get; } = new[]
        {
            new TutorialStep(SelectId,
                "This is the parts list. Click the NOT gate to pick one up.",
                TutorialTarget.PaletteEntry),

            new TutorialStep(PlaceId,
                "Now click the highlighted square to put it down. " +
                "Right click a part to remove it.",
                TutorialTarget.BoardCell),

            new TutorialStep(WireInId,
                "The small dots on each part are ports. " +
                "Drag from A's port to the gate's left port.",
                TutorialTarget.SourcePort, TutorialTarget.GateInput),

            new TutorialStep(WireOutId,
                "Now wire the gate's right port to the bin. " +
                "Right click a wire to remove it.",
                TutorialTarget.GateOutput, TutorialTarget.SinkPort),

            new TutorialStep(RunId,
                "That is a working circuit. Press RUN to send a bit through it.",
                TutorialTarget.RunButton),

            new TutorialStep(WatchId,
                "Follow the bit. Space pauses a run, and the right arrow key moves it forward " +
                "one tick.",
                TutorialTarget.Bin),
        };

        public static int Count => Steps.Count;

        /// <summary>Whether the step at <paramref name="index"/> is finished.</summary>
        /// <remarks>
        /// An out-of-range index is finished, so a director that has walked off the end stops rather
        /// than throwing at the player.
        /// </remarks>
        public static bool IsComplete(int index, BoardFacts facts)
        {
            if (index < 0 || index >= Steps.Count)
                return true;

            switch (Steps[index].Id)
            {
                case SelectId:
                    return facts.Selected == TutorialLevel.Part;

                case PlaceId:
                    return facts.GateOnCell;

                case WireInId:
                    return facts.SourceWiredToGate;

                case WireOutId:
                    return facts.GateWiredToBin;

                // Passed counts as well as Running. A run is over in a couple of seconds and the
                // director may not look until after it has settled, which would otherwise leave the
                // tutorial asking for a button press that has already happened.
                case RunId:
                    return facts.Running || facts.Passed;

                case WatchId:
                    return facts.Passed;

                default:
                    return false;
            }
        }

        /// <summary>
        /// What to say instead of the current step, or null to say the step.
        /// </summary>
        /// <remarks>
        /// A failed run puts the player back on "press RUN" -- correctly, since Run rebuilds first
        /// and works straight after a failure -- but the step's own text would then claim the
        /// circuit works while the board says otherwise. This says what to press, and nothing about
        /// why the run failed: that is the collision hint's subject, on the level where it matters.
        /// </remarks>
        public static string RecoveryText(BoardFacts facts) =>
            facts.RunFailed && !facts.Running && !facts.Passed
                ? "That did not reach the bin. Press RESET to put the board back and try again."
                : null;

        /// <summary>
        /// What to say when the wrong part is on the square the tutorial's part belongs on, or null.
        /// </summary>
        /// <remarks>
        /// From a playtest: the decoy went on the highlighted square, the NOT was picked up, and the
        /// strip went on saying "click the highlighted square to put it down" -- a square that
        /// refuses the click, because something is already on it. The step is right not to accept
        /// the decoy; the player needs to be told that it is the problem, and how to take it off.
        /// It outranks the step's own text from the first step on, since picking up the NOT does not
        /// help while the square it goes on is taken.
        /// </remarks>
        public static string CorrectionText(BoardFacts facts)
        {
            if (facts.GateOnCell || facts.PartOnCell == null || facts.PartOnCell == TutorialLevel.Part)
                return null;

            string wrong = GatePalette.Label(facts.PartOnCell.Value);
            string wanted = GatePalette.Label(TutorialLevel.Part);

            return $"That square is for the {wanted}, and the {wrong} is on it. " +
                   $"Right click the {wrong} to take it off, then put the {wanted} there.";
        }

        /// <summary>
        /// The first step that is not finished, or <see cref="Count"/> when they all are.
        /// </summary>
        /// <remarks>
        /// Scanned from the start every time rather than remembered, which is what makes going
        /// backwards work: delete the wire the player just made and this returns that step again,
        /// with no undo handling anywhere.
        /// </remarks>
        public static int CurrentStep(BoardFacts facts)
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!IsComplete(i, facts))
                    return i;
            }

            return Steps.Count;
        }
    }
}
