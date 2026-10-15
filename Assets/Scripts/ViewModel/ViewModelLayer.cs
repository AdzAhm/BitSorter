namespace BitSorter.ViewModel
{
    /// <summary>
    /// The workbench's view-model: a run read at a moment, the schematic's layout and routes, pan and
    /// zoom, and the waveform's arithmetic -- everything the workbench decides that is not drawing.
    /// </summary>
    /// <remarks>
    /// The fourth pure layer, between the document and the view (Part 9, the plan's section 7.5 item
    /// 1). It may use <c>BitSorter.SystemVerilog</c>, <c>BitSorter.Sim</c> and <c>BitSorter.Board</c>;
    /// the view uses it, and nothing in it knows the view exists. It hands out values and positions,
    /// and the view turns them into colours and meshes.
    ///
    /// The same purity rules as the three below it: C# 9, no engine references, no reflection, no
    /// threads, no file access of its own -- so <c>dotnet test</c> runs it in seconds, in CI as on the
    /// machine. Its points are <see cref="Vec2"/>, which does Unity's arithmetic exactly, so a
    /// position worked out here is the same float in the editor, the browser and a test.
    /// </remarks>
    public static class ViewModelLayer
    {
        /// <summary>The layer's name, as Docs/architecture-5.md uses it.</summary>
        public const string Name = "ViewModel";
    }
}
