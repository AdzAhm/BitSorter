using System;
using System.Collections.Generic;
using BitSorter.Sim;

namespace BitSorter.Board
{
    /// <summary>
    /// A project's timing (Part 14a): its design -- never the testbench round it -- compiled and elaborated as the top, and
    /// reckoned: every path from an input to an output (<see cref="PathDelays"/>), and how fast it can be clocked
    /// (<see cref="StaticTiming"/>), each drawn register's clock-to-Q and setup given to it by name in every instance.
    /// </summary>
    public sealed class TimingAnalysis
    {
        private TimingAnalysis(TimingGraph graph, PathReport paths, TimingReport timing, string problem,
            IReadOnlyDictionary<string, int> gateCounts = null, int gates = 0)
        {
            Graph = graph;
            Paths = paths;
            Timing = timing;
            Problem = problem;
            GateCounts = gateCounts ?? new Dictionary<string, int>();
            Gates = gates;
        }

        /// <summary>
        /// The gates by primitive and inputs, as <see cref="TimingGraph.GateCounts"/> -- and a drawn gate wider than a bit
        /// counted once a bit, since it prints as one bitwise assignment and the graph sees no gate in it (Part 14b).
        /// </summary>
        public IReadOnlyDictionary<string, int> GateCounts { get; }

        /// <summary>Every gate, of every kind, a wide one's every bit.</summary>
        public int Gates { get; }

        /// <summary>The design's timing graph; null when it could not be reckoned.</summary>
        public TimingGraph Graph { get; }

        public PathReport Paths { get; }

        public TimingReport Timing { get; }

        /// <summary>Why there is no analysis, in the player's words; null when there is one.</summary>
        public string Problem { get; }

        public bool Succeeded => Graph != null;

        public static TimingAnalysis Of(Project project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                return Refused("The circuit has errors, so its timing cannot be worked out: the TRANSCRIPT says where.");

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, project.Top);
            if (elaboration.HasErrors)
                return Refused("The circuit has errors, so its timing cannot be worked out: the TRANSCRIPT says where.");

            // Timing is reckoned bit by bit, so a design of millions of bits would take the memory a run never needs.
            long bits = TimingGraph.BitsOf(elaboration.Design);
            if (bits > TimingGraph.MaxBits)
                return Refused("The design has " + bits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                    + " bits of signals, more than the " + TimingGraph.MaxBits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                    + " its timing is worked out for.");

            // A register's timing is its module's; each instance of the module gives it a full name of its own.
            var registers = new Dictionary<string, RegisterTiming>(StringComparer.Ordinal);
            foreach (ScopeInfo scope in elaboration.Design.Scopes)
            {
                if (scope.Module == null || !(compiled.Find(scope.Module) is CompiledModule module))
                    continue;
                foreach ((string signal, RegisterTiming timing) in module.Registers)
                    registers[scope.Path + "." + signal] = timing;
            }

            TimingGraph graph = TimingGraph.Of(elaboration.Design, registers);

            var counts = new Dictionary<string, int>(graph.GateCounts, StringComparer.Ordinal);
            int gates = graph.Gates;
            foreach (ScopeInfo scope in elaboration.Design.Scopes)
            {
                if (scope.Module == null || !(project.Find(scope.Module) is Module drawn) || drawn.IsWritten)
                    continue;
                foreach (Part part in drawn.Parts)
                    if (part is GatePart gate && gate.Width > 1)
                    {
                        string key = gate.Kind.ToString().ToLowerInvariant() + (gate.Inputs > 1 ? gate.Inputs.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                        counts[key] = (counts.TryGetValue(key, out int n) ? n : 0) + gate.Width;
                        gates += gate.Width;
                    }
            }

            return new TimingAnalysis(graph, PathDelays.Find(graph), StaticTiming.Analyse(graph), null, counts, gates);
        }

        private static TimingAnalysis Refused(string why) => new TimingAnalysis(null, null, null, why);
    }
}
