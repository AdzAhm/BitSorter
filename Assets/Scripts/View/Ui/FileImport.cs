using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BitSorter.Board;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// What an import reads (Part 13d): the files the player picks, or a folder's -- in a browser through the page's own
    /// picker, and since 5.1 on a desktop and in the editor through Windows' own Open dialog (<see cref="Choose"/>), where it
    /// read only the Import folder beside the save. That folder is still where COMPARE WITH MODELSIM'S RUN finds ModelSim's
    /// dump (<see cref="NewestDump"/>).
    /// </summary>
    /// <remarks>
    /// **A browser build cannot read the player's disk**, only what the player hands the page. So the page opens its own
    /// file picker (<c>Plugins/WebGL/BitSorterFiles.jslib</c>), reads what is picked as text, and keeps it until the game
    /// takes it (<see cref="TryTake"/>), a frame or a few later -- the pull the code editor's bridge uses (Part 12f), so
    /// nothing in the page calls into the game.
    ///
    /// **Tests send the folder elsewhere** (<see cref="Redirected"/>), as <c>SaveGuard</c> sends the save and the exports,
    /// **and never open the dialog** (<see cref="Chooser"/>): it waits for a person, and would hold the run up for ever.
    /// </remarks>
    public static class FileImport
    {
        /// <summary>The Import folder's stand-in while a test runs; null otherwise.</summary>
        public static string Redirected { get; set; }

        /// <summary>The Import folder beside the save: where a desktop or the editor finds ModelSim's dump.</summary>
        public static string Folder => Redirected ?? Path.Combine(Application.persistentDataPath, "Import");

        /// <summary>
        /// What <see cref="Choose"/> asks in place of Windows' dialog while it is set, and null otherwise: <c>SaveGuard</c> sets
        /// one that cancels, so no test can open a dialog that waits for a person, and a test that imports sets one that answers.
        /// </summary>
        public static Func<bool, string[]> Chooser { get; set; }

        /// <summary>The Open dialog's titles, for a folder and for files.</summary>
        public const string FolderTitle = "Import a project's folder", FilesTitle = "Import a project's files";

        /// <summary>
        /// A desktop's import asks for a <paramref name="folder"/> or for files, in Windows' own Open dialog (since 5.1):
        /// the folder or the files chosen, or null when it was cancelled. Modal, so it returns once the dialog closes. It opens
        /// in Documents the first time and where it was left after that, which Windows keeps for the program itself.
        /// </summary>
        public static string[] Choose(bool folder)
        {
            if (Chooser != null)
                return Chooser(folder);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string start = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!folder)
                return WindowsFileDialog.PickFiles(FilesTitle, start, "SystemVerilog and memory files", "*.sv;*.svh;*.v;*.vh;*.hex;*.mem;*.txt");
            string chosen = WindowsFileDialog.PickFolder(FolderTitle, start);
            return chosen == null ? null : new[] { chosen };
#else
            return folder ? new[] { Folder } : null;   // no dialog to be had: the Import folder, as before 5.1
#endif
        }

        /// <summary>Whether the player picks the files in the page -- a browser -- rather than in Windows' Open dialog (<see cref="Choose"/>).</summary>
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

        /// <summary>
        /// Every file in <paramref name="folder"/> and the folders in it, each by its path inside it, in order. Sizes are
        /// looked at before anything is read (Part 13f): a file past <see cref="ProjectImporter.MaxFileSize"/> is left
        /// <see cref="SourceFile.Unread"/>, and so is every file when there are more, or more in all, than an import takes
        /// (<see cref="ProjectImporter.Reads"/>) -- the import says why. A file that cannot be read is left unread too.
        /// A hidden file or folder is passed over (<see cref="IsHidden"/>), and so is a folder inside that is only a link to
        /// another, or that cannot be listed; the folder itself not listing throws.
        ///
        /// **It stops looking once it has found more files than an import takes** (5.1): a folder chosen by mistake --
        /// Documents, a whole drive -- would otherwise be walked to its last file, with nothing on screen moving.
        /// </summary>
        public static List<SourceFile> ReadFolder(string folder)
        {
            var found = new List<(string Relative, string Path, long Size)>();
            var folders = new Queue<string>();
            folders.Enqueue(folder);
            while (folders.Count > 0 && found.Count <= ProjectImporter.MaxFiles)
            {
                string at = folders.Dequeue();
                string[] files, inside;
                try
                {
                    files = Directory.GetFiles(at);
                    inside = Directory.GetDirectories(at);
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && at != folder)
                {
                    continue;   // a folder inside that is not the player's to list
                }

                Array.Sort(files, StringComparer.Ordinal);   // the same files found first every time, where it stops
                foreach (string path in files)
                {
                    string relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
                    if (IsHidden(relative))
                        continue;
                    found.Add((relative, path, SizeOf(path)));
                    if (found.Count > ProjectImporter.MaxFiles)
                        break;
                }

                Array.Sort(inside, StringComparer.Ordinal);
                foreach (string next in inside)
                    if (!IsHidden(Path.GetFileName(next)) && !IsLink(next))
                        folders.Enqueue(next);
            }

            return Read(found);
        }

        /// <summary>
        /// The files at <paramref name="paths"/> -- chosen together, from one folder -- each by its name, read as
        /// <see cref="ReadFolder"/> reads a folder's; a hidden one passed over.
        /// </summary>
        public static List<SourceFile> ReadFiles(IReadOnlyList<string> paths)
        {
            var found = new List<(string Relative, string Path, long Size)>();
            foreach (string path in paths ?? Array.Empty<string>())
            {
                string name = Path.GetFileName(path ?? "");
                if (name.Length > 0 && !IsHidden(name))
                    found.Add((name, path, SizeOf(path)));
            }

            return Read(found);
        }

        /// <summary>A file's size, or 0 where it cannot be asked: reading it then fails too, and leaves it unread.</summary>
        private static long SizeOf(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>Whether a folder is only a link to another -- a junction or a symbolic link -- which could lead back round.</summary>
        private static bool IsLink(string folder)
        {
            try
            {
                return (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return true;   // not to be gone into
            }
        }

        /// <summary>The files found, in order, read where an import takes them and left unread where it does not.</summary>
        private static List<SourceFile> Read(List<(string Relative, string Path, long Size)> found)
        {
            found.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
            long total = 0;
            foreach (var (_, _, size) in found)
                if (size <= ProjectImporter.MaxFileSize)
                    total += size;   // one too large is left out on its own, and no part of the whole
            bool reads = ProjectImporter.Reads(found.Count, total);

            var files = new List<SourceFile>(found.Count);
            foreach ((string relative, string path, long size) in found)
            {
                if (!reads || size > ProjectImporter.MaxFileSize)
                {
                    files.Add(SourceFile.Unread(relative, size));
                    continue;
                }

                try
                {
                    files.Add(new SourceFile(relative, File.ReadAllText(path)));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    files.Add(SourceFile.Unread(relative, size));   // open elsewhere, or not the player's to read
                }
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

        /// <summary>
        /// Opens the Import folder in the system's file browser, making it first if it is not there -- where COMPARE finds no
        /// dump in it. While a test runs (<see cref="Redirected"/>) it only makes it: a test opens no window on the screen of
        /// whoever runs it.
        /// </summary>
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            if (Redirected == null)
                Application.OpenURL("file:///" + Folder.Replace('\\', '/').TrimStart('/'));
        }

        /// <summary>
        /// Asks the page for files -- several, or a <paramref name="folder"/>'s -- which <see cref="TryTake"/> has once they are
        /// read. The page is handed the import's limits, so it reads what a desktop would (<see cref="ReadFolder"/>).
        /// </summary>
        public static void Pick(bool folder)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BitSorterPickFiles(folder ? 1 : 0, ProjectImporter.MaxFiles, (int)ProjectImporter.MaxFileSize, (int)ProjectImporter.MaxTotalSize);
#endif
        }

        /// <summary>The largest dump COMPARE WITH MODELSIM reads (<see cref="BitSorter.ViewModel.DumpPick.MostSize"/>).</summary>
        public const long MaxDumpSize = BitSorter.ViewModel.DumpPick.MostSize;

        /// <summary>Asks the page for one file, a dump to compare with, which <see cref="TryTake"/> has once it is read.</summary>
        public static void PickDump()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BitSorterPickFiles(0, 1, (int)MaxDumpSize, (int)MaxDumpSize);
#endif
        }

        /// <summary>
        /// The newest .vcd in the Import folder or a folder in it, or null: where a desktop finds ModelSim's dump. Too large a
        /// one is still the newest, for COMPARE to say so rather than reach past it to an older one.
        /// </summary>
        public static string NewestDump()
        {
            string folder = Folder;
            if (!Directory.Exists(folder))
                return null;
            string newest = null;
            DateTime when = DateTime.MinValue;
            foreach (string path in Directory.GetFiles(folder, "*.vcd", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                if (IsHidden(Path.GetRelativePath(folder, path).Replace('\\', '/')))
                    continue;
                if (info.LastWriteTimeUtc > when)
                {
                    when = info.LastWriteTimeUtc;
                    newest = path;
                }
            }
            return newest;
        }

        /// <summary>The files picked, once the page has read them all; false until then, and after they are taken.</summary>
        public static bool TryTake(out List<SourceFile> files)
        {
            files = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            string json = BitSorterTakeFiles();
            if (string.IsNullOrEmpty(json))
                return false;
            files = FilesPicked(json);
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// The files the page read, as it hands them over in <paramref name="json"/>: a hidden folder's left out, and the rest in
        /// the order <see cref="ReadFolder"/> reads a folder in, by their paths. The page hands them over as the browser listed
        /// them, and the import runs the first testbench it finds, so the same folder could run another testbench in a browser
        /// than on the desktop (browser-7).
        /// </summary>
        public static List<SourceFile> FilesPicked(string json)
        {
            Picked picked = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Picked>(json);
            var files = new List<SourceFile>();
            foreach (PickedFile file in picked?.files ?? Array.Empty<PickedFile>())
                if (file != null && !IsHidden(file.path))
                    files.Add(file.read ? new SourceFile(file.path, file.text) : SourceFile.Unread(file.path, file.size));
            files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return files;
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
            public bool read;
            public long size;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BitSorterPickFiles(int folder, int maxFiles, int maxFileSize, int maxTotalSize);

        [DllImport("__Internal")]
        private static extern string BitSorterTakeFiles();
#endif
    }
}
