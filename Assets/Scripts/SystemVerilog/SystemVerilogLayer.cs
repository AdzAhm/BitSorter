namespace BitSorter.SystemVerilog
{
    /// <summary>
    /// The SystemVerilog layer of BitSorter 5.0: the language's 4-state values, and its lexer, parser,
    /// syntax tree and printer.
    /// </summary>
    /// <remarks>
    /// The bottom of three pure layers -- SystemVerilog, then <c>BitSorter.Sim</c>, then
    /// <c>BitSorter.Board</c> -- and nothing here may refer to either of the other two. That
    /// direction, and the rest of the rules below, are written out in Docs/architecture-5.md and held
    /// by Tools/ci/Repo.Tests.
    ///
    /// Pure C# 9 with no engine references, no reflection, no threads and no file access of its own,
    /// so it builds unchanged under the plain .NET SDK, Unity's IL2CPP and WebGL. Tools/ci builds it
    /// against netstandard2.1, the profile Unity compiles against, so an API Unity lacks fails there
    /// before it reaches the editor.
    /// </remarks>
    public static class SystemVerilogLayer
    {
        /// <summary>The layer's name, as Docs/architecture-5.md uses it.</summary>
        public const string Name = "SystemVerilog";
    }
}
