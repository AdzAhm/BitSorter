using System;
using System.Collections.Generic;

namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>Where something is in the source: a file, a line and a column, both counted from 1.</summary>
    public readonly struct SourceLocation : IEquatable<SourceLocation>
    {
        public readonly string File;
        public readonly int Line;
        public readonly int Column;

        public SourceLocation(string file, int line, int column)
        {
            File = file;
            Line = line;
            Column = column;
        }

        public bool Equals(SourceLocation other) => File == other.File && Line == other.Line && Column == other.Column;
        public override bool Equals(object obj) => obj is SourceLocation other && Equals(other);
        public override int GetHashCode() => (File?.GetHashCode() ?? 0) ^ (Line * 397) ^ Column;
        public override string ToString() => $"{File}:{Line}:{Column}";
    }

    /// <summary>
    /// How the reader gets at a file an <c>`include</c> names. The pure layers never touch the disk
    /// themselves (Docs/architecture-5.md, section 3): the editor, the browser and the tests each hand
    /// over their own files through this.
    /// </summary>
    public interface ISourceFiles
    {
        /// <summary>The text of the file at <paramref name="path"/>, if there is one.</summary>
        bool TryRead(string path, out string text);
    }

    /// <summary>Files held in memory: what the tests use, and what a browser upload becomes.</summary>
    public sealed class MemorySourceFiles : ISourceFiles
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Adds a file, by a path in the same form an include will use.</summary>
        public MemorySourceFiles Add(string path, string text)
        {
            _files[SourcePaths.Normalise(path)] = text ?? string.Empty;
            return this;
        }

        public bool TryRead(string path, out string text) => _files.TryGetValue(SourcePaths.Normalise(path), out text);
    }

    /// <summary>Paths as the reader handles them: forward slashes, with <c>.</c> and <c>..</c> resolved.</summary>
    public static class SourcePaths
    {
        public static string Normalise(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string[] parts = path.Replace('\\', '/').Split('/');
            var kept = new List<string>(parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                if (part == "." || (part.Length == 0 && i > 0))
                    continue;

                if (part == ".." && kept.Count > 0 && kept[kept.Count - 1] != "..")
                {
                    kept.RemoveAt(kept.Count - 1);
                    continue;
                }

                kept.Add(part);
            }

            return string.Join("/", kept);
        }

        /// <summary>The directory a file is in, with a trailing slash; empty for a file at the root.</summary>
        public static string Directory(string path)
        {
            string normal = Normalise(path);
            int slash = normal.LastIndexOf('/');
            return slash < 0 ? string.Empty : normal.Substring(0, slash + 1);
        }

        /// <summary>
        /// The places an include of <paramref name="include"/> from <paramref name="fromFile"/> is looked
        /// for, in order: beside the including file, then as written. ModelSim's <c>vlog</c> looks in
        /// both, so a project laid out either way reads the same.
        /// </summary>
        public static IEnumerable<string> Candidates(string fromFile, string include)
        {
            string written = Normalise(include);
            bool absolute = written.StartsWith("/", StringComparison.Ordinal) || written.IndexOf(':') >= 0;

            if (!absolute)
            {
                string beside = Normalise(Directory(fromFile) + written);
                yield return beside;

                if (beside != written)
                    yield return written;
            }
            else
            {
                yield return written;
            }
        }
    }
}
