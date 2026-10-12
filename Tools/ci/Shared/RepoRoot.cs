using System;
using System.IO;

namespace BitSorter.Ci
{
    /// <summary>Finds the repository a CI test is running from.</summary>
    /// <remarks>
    /// Walks up from the test's own folder to the first one holding both CLAUDE.md and Assets, so it
    /// works from bin/Release under dotnet test, in CI's checkout, and from any working directory.
    /// Shared by the CI test projects as a linked file rather than a project, because it is ten lines
    /// and a project would be one more thing to build.
    /// </remarks>
    public static class RepoRoot
    {
        private static string _path;

        public static string Path => _path ??= Find(AppContext.BaseDirectory);

        /// <summary>A path inside the repository, from forward-slashed parts.</summary>
        public static string Combine(string relative) =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        private static string Find(string start)
        {
            for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "CLAUDE.md")) &&
                    Directory.Exists(System.IO.Path.Combine(dir.FullName, "Assets")))
                    return dir.FullName;
            }

            throw new InvalidOperationException(
                "No folder above " + start + " holds both CLAUDE.md and Assets, so this is not running " +
                "from inside the BitSorter repository.");
        }
    }
}
