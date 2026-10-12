namespace BitSorter.Sim
{
    /// <summary>
    /// The engine layer of BitSorter 5.0: elaboration, the 4-state event-driven scheduler, processes,
    /// system tasks, the recorded history and VCD.
    /// </summary>
    /// <remarks>
    /// The middle of three pure layers. It may use <c>BitSorter.Hdl</c> and nothing above it: the
    /// document layer and the view read the engine, never the other way round. Docs/architecture-5.md
    /// has the whole contract -- above all that the same design and stimulus always give the same
    /// history, and that no design, however broken, makes the engine throw or hang.
    ///
    /// The same purity rules as the language layer: C# 9, no engine references, no reflection, no
    /// threads, and file access only through an interface the host provides.
    /// </remarks>
    public static class SimLayer
    {
        /// <summary>The layer's name, as Docs/architecture-5.md uses it.</summary>
        public const string Name = "Sim";
    }
}
