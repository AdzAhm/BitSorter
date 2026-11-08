using System;
using System.Collections.Generic;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The dump COMPARE WITH MODELSIM'S RUN holds the run to (Part 15i-5), among the files a browser's page read: the
    /// <see cref="ProjectExport.ModelSimDump"/> an exported run.do has ModelSim write -- or, where there is none to compare
    /// with, why, in words.
    /// </summary>
    public static class DumpPick
    {
        /// <summary>The .vcd among <paramref name="files"/> that was read; false, and <paramref name="why"/>, where there is none.</summary>
        public static bool TryChoose(IReadOnlyList<SourceFile> files, out SourceFile dump, out string why)
        {
            why = null;
            foreach (SourceFile file in files ?? Array.Empty<SourceFile>())
                if (file.Path.EndsWith(".vcd", StringComparison.OrdinalIgnoreCase) && file.Text != null)
                {
                    dump = file;
                    return true;
                }

            dump = default;
            why = "pick the " + ProjectExport.ModelSimDump + " ModelSim wrote to compare with";
            return false;
        }
    }
}
