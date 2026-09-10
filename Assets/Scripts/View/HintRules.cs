using System.Collections.Generic;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>
    /// The first-time hints: what they say, and what has to happen before one is worth saying.
    /// </summary>
    /// <remarks>
    /// A level's <see cref="LevelDefinition.Goal"/> states the objective and its
    /// <see cref="LevelDefinition.Hint"/> nudges towards the answer for that level. These are the
    /// third thing: they explain a *mechanic*, once ever, at the moment the player first meets it.
    /// Keeping those three jobs apart is what stops a hint here becoming a second copy of a level's
    /// hint, and CurriculumTests holds the line.
    ///
    /// Pure and scene-free, like <see cref="PortState"/> and <see cref="MenuRules"/>, so the
    /// triggers can be tested against a real simulation without a canvas.
    /// </remarks>
    public static class HintRules
    {
        public const string Stalled = "stalled";
        public const string Collision = "collision";
        public const string WireDelay = "wireDelay";

        /// <summary>
        /// Consecutive ticks a gate must sit stalled mid-run before it is worth explaining.
        /// </summary>
        /// <remarks>
        /// Above any legitimate imbalance rather than at the first stalled frame. A gate whose two
        /// inputs arrive a tick apart is stalled for that tick on a circuit that then works
        /// perfectly, and firing there would be teaching a player about a mistake they did not make.
        /// The levels that budget delay cap a wire at 3, so four ticks of waiting is past anything
        /// the player could have arranged on purpose.
        ///
        /// Note there is no need to guard against seeing a gate mid-delivery: Simulation.Tick
        /// delivers every arrival before any node evaluates, and the view renders only between whole
        /// ticks, so a gate that got both its inputs together is never observed holding one.
        /// </remarks>
        public const int StallTicks = 4;

        /// <summary>What each hint says. Null for an id that is not a hint.</summary>
        public static string TextFor(string id)
        {
            switch (id)
            {
                case Stalled:
                    return "Gates fire only when every input is holding a bit. " +
                           "That one is short of at least one, so it will sit there.";

                case Collision:
                    return "An input port holds one bit at a time. A second bit arriving has " +
                           "nowhere to go, and the burn mark shows where it happened.";

                case WireDelay:
                    return "Scroll the wheel over a wire to change the number on it. " +
                           "A bigger number means the bit takes longer to cross.";

                default:
                    return null;
            }
        }

        /// <summary>Every hint id, for tests that hold all of them to the same rule.</summary>
        public static IReadOnlyList<string> All { get; } =
            new[] { Stalled, Collision, WireDelay };

        /// <summary>
        /// Whether a stalled gate has earned an explanation.
        /// </summary>
        /// <param name="settled">The run has stopped: nothing further can arrive.</param>
        /// <param name="anyStalled">At least one gate is holding bits it cannot act on.</param>
        /// <param name="longestStallTicks">Ticks the longest-running current stall has lasted.</param>
        /// <param name="threshold"><see cref="StallTicks"/>, or a level-specific override.</param>
        /// <remarks>
        /// A settled run needs no threshold at all. Once the board is idle nothing else is coming,
        /// so a gate still holding is stuck for good and there is nothing to wait and see about.
        /// </remarks>
        public static bool StallEarnsAHint(
            bool settled, bool anyStalled, int longestStallTicks, int threshold)
        {
            if (!anyStalled)
                return false;

            return settled || longestStallTicks >= threshold;
        }
    }

    /// <summary>
    /// How long each gate has been stalled, in ticks.
    /// </summary>
    /// <remarks>
    /// Keyed per node so two separate short stalls can never add up to one long one -- a gate that
    /// stalls for two ticks, fires, and stalls again for two has not been waiting for four.
    ///
    /// Counted in ticks rather than seconds because that is what the wait actually is. A player who
    /// pauses on a stalled board is not learning anything new by staring, and a paused board
    /// advances no ticks, so the clock correctly stops with it.
    /// </remarks>
    public sealed class StallClock
    {
        /// <summary>Node id to the tick its current stall began.</summary>
        private readonly Dictionary<int, int> _since = new Dictionary<int, int>();
        private readonly List<int> _ended = new List<int>();

        public void Clear() => _since.Clear();

        /// <summary>Whether anything was stalled as of the last <see cref="Observe"/>.</summary>
        public bool AnyStalled => _since.Count > 0;

        /// <summary>
        /// Takes the graph as it stands and returns how long the longest current stall has run,
        /// in ticks. Zero when nothing is stalled.
        /// </summary>
        /// <remarks>
        /// Takes no null guard: SimulationView is a readonly struct, so there is nothing to guard
        /// against. Callers reach it through SimulationRunner.View, which is only meaningful once
        /// IsReady, and that is where the check belongs.
        /// </remarks>
        public int Observe(SimulationView view, int tick)
        {
            int longest = 0;

            _ended.Clear();
            foreach (KeyValuePair<int, int> entry in _since)
                _ended.Add(entry.Key);

            // Anything still stalled is removed from the ended list as it is seen, so what is left
            // is exactly the gates that have started moving again.
            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                if (node == null || !PortState.IsStalled(node))
                    continue;

                _ended.Remove(id);

                if (!_since.TryGetValue(id, out int began))
                {
                    began = tick;
                    _since[id] = began;
                }

                int age = tick - began;

                if (age > longest)
                    longest = age;
            }

            for (int i = 0; i < _ended.Count; i++)
                _since.Remove(_ended[i]);

            return longest;
        }
    }
}
