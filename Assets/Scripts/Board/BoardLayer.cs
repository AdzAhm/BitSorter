namespace BitSorter.Board
{
    /// <summary>
    /// The document layer of BitSorter 5.0: modules, parts, wires and their layout, the testbench's
    /// stimulus, editing commands and undo, import and export, migration of older saves, and the
    /// course's analyses.
    /// </summary>
    /// <remarks>
    /// The third of four pure layers -- SystemVerilog, Sim, Board, then the workbench's view-model,
    /// <c>BitSorter.ViewModel</c>. It may use <c>BitSorter.SystemVerilog</c> and <c>BitSorter.Sim</c>; the
    /// view-model and the view use it, and nothing in it knows either exists. A drawn board reaches the engine as the
    /// same syntax tree its exported SystemVerilog is printed from, so what the code panel shows is
    /// what runs. Docs/architecture-5.md has the data model.
    ///
    /// The same purity rules as the other three layers: C# 9, no engine references, no reflection, no
    /// threads, no file access of its own.
    /// </remarks>
    public static class BoardLayer
    {
        /// <summary>The layer's name, as Docs/architecture-5.md uses it.</summary>
        public const string Name = "Board";
    }
}
