#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || WINDOWS_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BitSorter.View
{
    /// <summary>
    /// Windows' own Open dialog (the Common Item Dialog, <c>IFileOpenDialog</c>), for a folder or for files: what a desktop
    /// import picks with since 5.1, where it could read only the Import folder (Unity gives a built game no file dialog).
    /// </summary>
    /// <remarks>
    /// Shown modal over the game's window, on the calling thread, which waits until it closes; the game draws nothing while
    /// it is open, as with any modal dialog. COM interop, which the Windows build's Mono runs. No <c>UnityEngine</c> in here,
    /// so the file compiles on its own for a check outside Unity (<c>WINDOWS_PROBE</c>): a console program driven by
    /// keystrokes held it to a folder chosen, files chosen and Cancel before it came in (2026-11-11), since no test here can
    /// open a dialog that waits for a person (<see cref="FileImport.Chooser"/>).
    /// </remarks>
    public static class WindowsFileDialog
    {
        /// <summary>The folder chosen, or null when the dialog was cancelled or could not be shown.</summary>
        public static string PickFolder(string title, string start)
        {
            string[] chosen = Show(title, start, folder: true);
            return chosen == null || chosen.Length == 0 ? null : chosen[0];
        }

        /// <summary>The files chosen, one or several from one folder, or null when the dialog was cancelled or could not be shown.</summary>
        public static string[] PickFiles(string title, string start, string typesName, string typesSpec)
        {
            return Show(title, start, folder: false, typesName, typesSpec);
        }

        private static string[] Show(string title, string start, bool folder, string typesName = null, string typesSpec = null)
        {
            IFileOpenDialog dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogClass();
                dialog.GetOptions(out uint options);
                options |= ForceFileSystem | NoChangeDir | PathMustExist;
                options |= folder ? PickFolders : AllowMultiSelect | FileMustExist;
                dialog.SetOptions(options);
                if (!string.IsNullOrEmpty(title))
                    dialog.SetTitle(title);
                if (!folder && !string.IsNullOrEmpty(typesSpec))
                {
                    var types = new[]
                    {
                        new FilterSpec { Name = typesName ?? typesSpec, Spec = typesSpec },
                        new FilterSpec { Name = "All files", Spec = "*.*" },
                    };
                    dialog.SetFileTypes((uint)types.Length, types);
                }

                // Where it opens the first time only: from then on Windows opens it where the last one was left, which it
                // keeps for the program itself (SetDefaultFolder, where SetFolder would overrule it every time).
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
                {
                    Guid shellItem = ShellItemId;
                    if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref shellItem, out IShellItem at) == 0 && at != null)
                    {
                        dialog.SetDefaultFolder(at);
                        Marshal.ReleaseComObject(at);
                    }
                }

                int shown = dialog.Show(GetActiveWindow());
                if (shown != 0)
                    return null;   // cancelled (ERROR_CANCELLED), or the dialog could not be shown

                dialog.GetResults(out IShellItemArray results);
                try
                {
                    results.GetCount(out uint count);
                    var paths = new List<string>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        results.GetItemAt(i, out IShellItem item);
                        try
                        {
                            item.GetDisplayName(FileSystemPath, out IntPtr name);
                            try
                            {
                                string path = Marshal.PtrToStringUni(name);
                                if (!string.IsNullOrEmpty(path))
                                    paths.Add(path);
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(name);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                    return paths.ToArray();
                }
                finally
                {
                    Marshal.ReleaseComObject(results);
                }
            }
            catch (COMException)
            {
                return null;   // no dialog to be had: the import says nothing was chosen, as a cancel does
            }
            catch (InvalidCastException)
            {
                return null;
            }
            finally
            {
                if (dialog != null)
                    Marshal.ReleaseComObject(dialog);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The Common Item Dialog, as shobjidl_core.h declares it. A COM interface's methods are its vtable in order, and C#
        // cannot inherit one interface's slots into another, so IFileOpenDialog lists IModalWindow's and IFileDialog's first.
        // ---------------------------------------------------------------------------------------------------------------

        private const uint NoChangeDir = 0x8, PickFolders = 0x20, ForceFileSystem = 0x40, AllowMultiSelect = 0x200,
            PathMustExist = 0x800, FileMustExist = 0x1000;

        private const uint FileSystemPath = 0x80058000;   // SIGDN_FILESYSPATH

        private static readonly Guid ShellItemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");   // IShellItem's, below

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
        }

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogClass
        {
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr owner);

            // IFileDialog
            void SetFileTypes(uint count, [In, MarshalAs(UnmanagedType.LPArray)] FilterSpec[] types);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int where);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);

            // IFileOpenDialog
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint form, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr store);
            void GetPropertyDescriptionList(IntPtr keyType, ref Guid interfaceId, out IntPtr list);
            void GetAttributes(int how, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context,
            ref Guid interfaceId, out IShellItem item);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
    }
}
#endif
