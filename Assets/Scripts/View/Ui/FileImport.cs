using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BitSorter.Board;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// What an import reads (Part 13d): on a desktop and in the editor, every file in the Import folder beside the save,
    /// made the first time it is asked for; in a browser, the files the player picks, or a folder's.
    /// </summary>
    /// <remarks>
    /// **A browser build cannot read the player's disk**, only what the player hands the page. So the page opens its own
    /// file picker (<c>Plugins/WebGL/BitSorterFiles.jslib</c>), reads what is picked as text, and keeps it until the game
    /// takes it (<see cref="TryTake"/>), a frame or a few later -- the pull the code editor's bridge uses (Part 12f), so
    /// nothing in the page calls into the game.
    ///
    /// **Tests send the folder elsewhere** (<see cref="Redirected"/>), as <c>SaveGuard</c> sends the save and the exports.
    /// </remarks>
    public static class FileImport
    {
        /// <summary>The folder an import reads instead of the player's, while a test runs; null otherwise.</summary>
        public static string Redirected { get; set; }

        /// <summary>Where a desktop or the editor reads an import from.</summary>
        public static string Folder => Redirected ?? Path.Combine(Application.persistentDataPath, "Import");

        /// <summary>Whether the player picks the files in the page -- a browser -- rather than putting them in the folder.</summary>
        public static bool Picks
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Every file in the folder and the folders in it, each by its path inside it, in order; the folder made if it is not there.</summary>
        public static List<SourceFile> ReadFolder()
        {
            string folder = Folder;
            Directory.CreateDirectory(folder);
            var paths = new List<string>(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
            paths.Sort(StringComparer.Ordinal);

            var files = new List<SourceFile>(paths.Count);
            foreach (string path in paths)
            {
                string relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
                if (!IsHidden(relative))
                    files.Add(new SourceFile(relative, File.ReadAllText(path)));
            }

            return files;
        }

        /// <summary>
        /// Whether a path is hidden: a name, or a folder on the way to it, starting with a dot -- a system's own files, and a
        /// project's <c>.git</c>, whose every object would otherwise come in as a file of the project.
        /// </summary>
        public static bool IsHidden(string path)
        {
            foreach (string part in (path ?? "").Replace('\\', '/').Split('/'))
                if (part.StartsWith(".", StringComparison.Ordinal) && part != "." && part != "..")
                    return true;
            return false;
        }

        /// <summary>Opens the folder in the system's file browser, making it first if it is not there.</summary>
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL("file:///" + Folder.Replace('\\', '/').TrimStart('/'));
        }

        /// <summary>Asks the page for files -- several, or a <paramref name="folder"/>'s -- which <see cref="TryTake"/> has once they are read.</summary>
        public static void Pick(bool folder)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BitSorterPickFiles(folder ? 1 : 0);
#endif
        }

        /// <summary>The files picked, once the page has read them all; false until then, and after they are taken.</summary>
        public static bool TryTake(out List<SourceFile> files)
        {
            files = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            string json = BitSorterTakeFiles();
            if (string.IsNullOrEmpty(json))
                return false;
            Picked picked = JsonUtility.FromJson<Picked>(json);
            files = new List<SourceFile>();
            foreach (PickedFile file in picked?.files ?? Array.Empty<PickedFile>())
                if (!IsHidden(file.path))
                    files.Add(new SourceFile(file.path, file.text));
            return true;
#else
            return false;
#endif
        }

        [Serializable]
        private sealed class Picked
        {
            public PickedFile[] files;
        }

        [Serializable]
        private sealed class PickedFile
        {
            public string path;
            public string text;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BitSorterPickFiles(int folder);

        [DllImport("__Internal")]
        private static extern string BitSorterTakeFiles();
#endif
    }
}
