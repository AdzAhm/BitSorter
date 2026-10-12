using System.IO;

namespace BitSorter.Ci.Oracle
{
    /// <summary>The oracle's designs, in Tools/ci/Oracle/corpus. See the README there for its rules.</summary>
    public static class Corpus
    {
        public const string Folder = "Tools/ci/Oracle/corpus";

        /// <summary>The full path of a corpus file, from its path inside the corpus.</summary>
        public static string File(string relative)
        {
            string full = RepoRoot.Combine(Folder + "/" + relative);

            if (!System.IO.File.Exists(full))
                throw new FileNotFoundException("No such file in the oracle corpus: " + relative, full);

            return full;
        }
    }
}
