using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>The codes the elaborator gives. Never reuse or renumber one.</summary>
    public static class ElaborationCodes
    {
        public const string NoTop = "SV0301";
        public const string UnknownModule = "SV0302";
        public const string UnknownName = "SV0303";
        public const string NotConstant = "SV0304";
        public const string WidthMismatch = "SV0305";
        public const string NotYet = "SV0306";
        public const string BadPort = "SV0307";
        public const string Drivers = "SV0308";
        public const string Recursion = "SV0309";
        public const string BadSelect = "SV0310";
        public const string BadDelay = "SV0311";
        public const string ImplicitNet = "SV0312";
        public const string Unconnected = "SV0313";
        public const string Duplicate = "SV0314";
        public const string BadLValue = "SV0315";
        public const string UnsizedInConcatenation = "SV0316";
        public const string BadParameter = "SV0317";
        public const string TooLarge = "SV0318";
        public const string Ignored = "SV0319";
        public const string NeverWaits = "SV0320";
        public const string Misplaced = "SV0321";
        public const string BadFormat = "SV0322";

        /// <summary>A function or task called in a way it cannot be: a task inside an expression, the wrong arguments, a function that waits.</summary>
        public const string BadCall = "SV0323";

        // Lint: warnings about designs that run, but likely not as meant.
        public const string LatchProne = "SV0401";

        /// <summary>A signal read that nothing drives or sets: it reads z, or x (Part 14e).</summary>
        public const string Undriven = "SV0402";

        /// <summary>A signal set that nothing reads (Part 14e).</summary>
        public const string NeverRead = "SV0403";

        /// <summary>A net with several drivers on the same bits, not all of which can let go -- tri-state (Part 14e).</summary>
        public const string SeveralDrivers = "SV0404";

        /// <summary>A plain value assigned to something narrower: its top bits are dropped (Part 14e).</summary>
        public const string Truncated = "SV0405";

        // A gate library: errors, as a drawn gate off the library is refused.

        /// <summary>Written code held to a gate library that breaks it: a gate it has no cell for, off its delays, logic written with operators, or a constant where it takes none (Part 14g).</summary>
        public const string OffLibrary = "SV0501";
    }

    /// <summary>One port of the top module, as elaborated: what a testbench or the workbench drives and watches.</summary>
    public sealed class ElaboratedPort
    {
        internal ElaboratedPort(string name, PortDirection direction, SignalId signal, int width, bool signed)
        {
            Name = name;
            Direction = direction;
            Signal = signal;
            Width = width;
            Signed = signed;
        }

        public string Name { get; }
        public PortDirection Direction { get; }
        public SignalId Signal { get; }
        public int Width { get; }
        public bool Signed { get; }
    }

    /// <summary>
    /// What elaborating a design gave: the flattened <see cref="Sim.Design"/> ready to simulate, the top
    /// module's ports, and everything worth saying -- or, when there were errors, no design and the
    /// errors.
    /// </summary>
    public sealed class Elaboration
    {
        internal Elaboration(Design design, string top, List<Diagnostic> diagnostics, List<ElaboratedPort> ports)
        {
            Design = design;
            Top = top;
            Diagnostics = diagnostics;
            Ports = ports;
        }

        /// <summary>The flattened design; null when elaboration found an error.</summary>
        public Design Design { get; }

        /// <summary>The top module's name.</summary>
        public string Top { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public IReadOnlyList<ElaboratedPort> Ports { get; }

        public bool HasErrors
        {
            get
            {
                foreach (Diagnostic d in Diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error)
                        return true;
                return false;
            }
        }
    }
}
