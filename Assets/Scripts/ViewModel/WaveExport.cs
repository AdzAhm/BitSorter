using System;
using System.IO;
using System.Text;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What the waveform exports (Part 9b-4): the run as a VCD in the run's own unit, and the name a
    /// file of it takes. The PNG and the saving are the view's.
    /// </summary>
    /// <remarks>
    /// **The run as far as it is made**: a VCD of a run still being made, or suspended at a
    /// <c>$stop</c>, ends where the run has got, so what it holds is what the waveform could show, and a
    /// run carried on past its <c>$stop</c> exports with the timeline it grew to. Every signal of the
    /// design is in it, not only the rows shown -- GTKWave picks its own -- written by the engine's
    /// <see cref="VcdWriter"/>, whose output the oracle already reads back against Icarus's.
    ///
    /// **`$timescale 1ps` for a run in ps**: the unit labels time and rescales nothing (invariant 7),
    /// so a VCD's numbers are the run's and its timescale the one whole unit they count.
    ///
    /// Writes to a <see cref="TextWriter"/> the caller opens, as the engine does: this layer touches no
    /// files, and the view hands the text to whatever saves it on the platform it runs on.
    /// </remarks>
    public static class WaveExport
    {
        /// <summary>The name an export takes when nothing better can be made of the run's.</summary>
        public const string FallbackName = "waveform";

        /// <summary>The moment the run is made to: what an export of it reaches.</summary>
        public static long MadeTo(WorkbenchModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            return model.Complete ? model.End : Math.Min(model.End, model.Reached);
        }

        /// <summary>The run <paramref name="model"/> shows, as far as it is made, as a VCD in its unit.</summary>
        public static void WriteVcd(TextWriter output, WorkbenchModel model)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            VcdWriter.Write(output, model.Session.Design, model.Session.History, MadeTo(model), "1" + model.Unit);
        }

        /// <summary>The VCD as one string: what the view hands to a download or a file.</summary>
        public static string Vcd(WorkbenchModel model)
        {
            var text = new StringWriter();
            WriteVcd(text, model);
            return text.ToString();
        }

        /// <summary>
        /// A file's name for an export of <paramref name="name"/> -- a sample's, a project's -- with
        /// <paramref name="extension"/>: lower case, a letter, digit or underscore each, anything else an
        /// underscore and no two in a row, so it is the same file name on every system and a shell need
        /// not quote it. <c>Full adder</c> and <c>vcd</c> make <c>full_adder.vcd</c>.
        /// </summary>
        public static string FileName(string name, string extension)
        {
            if (string.IsNullOrEmpty(extension))
                throw new ArgumentException("A file's name needs its extension: vcd, png.", nameof(extension));

            var safe = new StringBuilder();
            foreach (char c in name ?? "")
            {
                char lower = char.ToLowerInvariant(c);
                bool kept = (lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9');
                if (kept)
                    safe.Append(lower);
                else if (safe.Length > 0 && safe[safe.Length - 1] != '_')
                    safe.Append('_');
            }

            while (safe.Length > 0 && safe[safe.Length - 1] == '_')
                safe.Length--;

            return (safe.Length > 0 ? safe.ToString() : FallbackName) + "." + extension.TrimStart('.').ToLowerInvariant();
        }
    }
}
