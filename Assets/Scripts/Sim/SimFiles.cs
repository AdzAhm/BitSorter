using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>
    /// The files a running design reads and writes: <c>$readmemh</c>'s memory images and what
    /// <c>$writememh</c> writes. The pure layers touch no disk of their own (<c>Docs/architecture-5.md</c>,
    /// section 3), so the editor, the browser and the tests each hand theirs over through this.
    /// </summary>
    public interface ISimFiles
    {
        /// <summary>The text of the file at <paramref name="path"/>, if there is one.</summary>
        bool TryRead(string path, out string text);

        /// <summary>Writes the file at <paramref name="path"/>, replacing whatever was there.</summary>
        void Write(string path, string text);
    }

    /// <summary>Files held in memory, by path: the tests' files, and the browser's.</summary>
    public sealed class MemorySimFiles : ISimFiles
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

        public MemorySimFiles Add(string path, string text)
        {
            _files[SourcePaths.Normalise(path)] = text ?? throw new ArgumentNullException(nameof(text));
            return this;
        }

        public bool TryRead(string path, out string text) => _files.TryGetValue(SourcePaths.Normalise(path), out text);

        public void Write(string path, string text) => _files[SourcePaths.Normalise(path)] = text;

        /// <summary>Every path held, written ones included.</summary>
        public IEnumerable<string> Paths => _files.Keys;
    }
}
