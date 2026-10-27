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
        private TimingAnalysis(TimingGraph graph, PathReport paths, TimingReport timing, string problem)
        {
            Graph = graph;
            Paths = paths;
            Timing = timing;
            Problem = problem;
        }

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
            return new TimingAnalysis(graph, PathDelays.Find(graph), StaticTiming.Analyse(graph), null);
        }

        private static TimingAnalysis Refused(string why) => new TimingAnalysis(null, null, null, why);
    }
}
