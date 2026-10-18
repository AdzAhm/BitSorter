using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Hands an export to the player (Part 9b-4): in a browser, a download; on a desktop and in the
    /// editor, a file in the Exports folder beside the save.
    /// </summary>
    /// <remarks>
    /// **A browser build cannot write a file the player can find**: its persistent data path is the
    /// page's own storage. So it hands the bytes to the page (<c>Plugins/WebGL/BitSorterFiles.jslib</c>),
    /// which makes them a Blob and clicks a link with a download name -- what a page's own "save as" does,
    /// and allowed in the iframe Unity Play and itch.io run the game in, since it follows a click the
    /// player made.
    ///
    /// **Tests send the folder elsewhere** (<see cref="Redirected"/>), as <c>SaveGuard</c> sends the save,
    /// so a test's export never lands among the player's own.
    /// </remarks>
    public static class FileSaver
    {
        /// <summary>The folder exports go to instead of the player's, while a test runs; null otherwise.</summary>
        public static string Redirected { get; set; }

        /// <summary>Where a desktop or the editor saves an export.</summary>
        public static string Folder => Redirected ?? Path.Combine(Application.persistentDataPath, "Exports");

        /// <summary>
        /// Saves <paramref name="bytes"/> as <paramref name="fileName"/>, of type <paramref name="mime"/>,
        /// and says where, in words for the chip: <c>to your downloads</c>, or <c>in</c> and the folder.
        /// A file of the same name already there is replaced, as saving again means.
        /// </summary>
        public static string Save(string fileName, byte[] bytes, string mime)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BitSorterDownload(fileName, bytes, bytes.Length, mime);
            return "to your downloads";
#else
            string folder = Folder;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, fileName), bytes);
            return "in " + folder;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BitSorterDownload(string name, byte[] bytes, int length, string mime);
#endif
    }
}
