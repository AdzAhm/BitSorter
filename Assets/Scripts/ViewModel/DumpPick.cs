using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>
        /// The largest dump COMPARE WITH MODELSIM reads (Part 15i-5): a VCD of a long run is far larger than a project's code,
        /// and reading one takes about twenty times its size in memory -- 16 MB about 315 MB, measured in the 15i-5 review.
        /// </summary>
        public const long MostSize = 16L << 20;

        /// <summary>What COMPARE says of a dump called <paramref name="name"/>, <paramref name="size"/> bytes, too large to read.</summary>
        public static string TooLarge(string name, long size) =>
            name + " is " + (size >> 20).ToString(CultureInfo.InvariantCulture) + " MB, more than COMPARE reads (" +
            (MostSize >> 20).ToString(CultureInfo.InvariantCulture) + " MB): run to an earlier moment, or dump fewer signals";

        /// <summary>
        /// The .vcd among <paramref name="files"/> that was read; false, and <paramref name="why"/>, where there is none -- and
        /// for one the page left unread, why it did: past <see cref="MostSize"/>, or picked with others, since COMPARE asks
        /// for one file. Both were told to pick the very file just picked (browser-6).
        /// </summary>
        public static bool TryChoose(IReadOnlyList<SourceFile> files, out SourceFile dump, out string why)
        {
            why = null;
            files = files ?? Array.Empty<SourceFile>();
            foreach (SourceFile file in files)
                if (IsDump(file) && file.Text != null)
                {
                    dump = file;
                    return true;
                }

            dump = default;
            foreach (SourceFile file in files)
            {
                if (!IsDump(file))
                    continue;
                string name = file.Path.Substring(file.Path.LastIndexOf('/') + 1);
                why = files.Count > 1 ? "COMPARE reads one file: pick " + name + " alone"
                    : file.Size > MostSize ? TooLarge(name, file.Size)
                    : name + " could not be read: pick it again";
                return false;
            }

            why = "pick the " + ProjectExport.ModelSimDump + " ModelSim wrote to compare with";
            return false;
        }

        private static bool IsDump(SourceFile file) => file.Path != null && file.Path.EndsWith(".vcd", StringComparison.OrdinalIgnoreCase);
    }
}
