using System.Runtime.InteropServices;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// CodeMirror over the canvas, in a browser build (Part 12f): the calls <see cref="CodeEditorView"/> makes to the page,
    /// through <c>Assets/Plugins/WebGL/BitSorterCode.jslib</c>, to the editor bundled in <c>codemirror.jspre</c>. Everywhere
    /// else -- the editor, a desktop build, the tests -- <see cref="Available"/> is false, nothing is called, and the code is
    /// typed in TextMesh Pro's field.
    /// </summary>
    internal static class CodeMirrorBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void BitSorterCodeCreate(string colours);
        [DllImport("__Internal")] private static extern void BitSorterCodePlace(float x, float y, float width, float height, float font);
        [DllImport("__Internal")] private static extern void BitSorterCodeShow(int shown);
        [DllImport("__Internal")] private static extern void BitSorterCodeSetText(string text);
        [DllImport("__Internal")] private static extern string BitSorterCodeGetText();
        [DllImport("__Internal")] private static extern int BitSorterCodeTakeChanged();
        [DllImport("__Internal")] private static extern int BitSorterCodeTakeApply();
        [DllImport("__Internal")] private static extern int BitSorterCodeTakeEscape();
        [DllImport("__Internal")] private static extern void BitSorterCodeSetMarks(string marks);
        [DllImport("__Internal")] private static extern void BitSorterCodeGoTo(int line, int column);
        [DllImport("__Internal")] private static extern void BitSorterCodeBlur();
        [DllImport("__Internal")] private static extern int BitSorterCodeFocused();

        public static bool Available => true;
#else
        private static void BitSorterCodeCreate(string colours) { }
        private static void BitSorterCodePlace(float x, float y, float width, float height, float font) { }
        private static void BitSorterCodeShow(int shown) { }
        private static void BitSorterCodeSetText(string text) { }
        private static string BitSorterCodeGetText() => "";
        private static int BitSorterCodeTakeChanged() => 0;
        private static int BitSorterCodeTakeApply() => 0;
        private static int BitSorterCodeTakeEscape() => 0;
        private static void BitSorterCodeSetMarks(string marks) { }
        private static void BitSorterCodeGoTo(int line, int column) { }
        private static void BitSorterCodeBlur() { }
        private static int BitSorterCodeFocused() => 0;

        public static bool Available => false;
#endif

        private static int _focusFrame = -1;
        private static bool _focused;

        public static void Create(string colours) => BitSorterCodeCreate(colours);

        /// <summary>
        /// The editor over <paramref name="screen"/>, a rectangle of the game's screen (its pixels, from the bottom-left), its
        /// letters <paramref name="font"/> of those pixels high, as the game's own code is.
        /// </summary>
        public static void Place(Rect screen, float font) =>
            BitSorterCodePlace(screen.xMin, Screen.height - screen.yMax, screen.width, screen.height, font);

        public static void Show(bool shown) => BitSorterCodeShow(shown ? 1 : 0);
        public static void SetText(string text) => BitSorterCodeSetText(text ?? "");
        public static string GetText() => BitSorterCodeGetText() ?? "";
        public static bool TakeChanged() => BitSorterCodeTakeChanged() != 0;
        public static bool TakeApply() => BitSorterCodeTakeApply() != 0;
        public static bool TakeEscape() => BitSorterCodeTakeEscape() != 0;
        public static void SetMarks(string marks) => BitSorterCodeSetMarks(marks ?? "[]");
        public static void GoTo(int line, int column) => BitSorterCodeGoTo(line, column);
        public static void Blur() => BitSorterCodeBlur();

        /// <summary>
        /// Whether the editor has the keyboard -- so the game's keys are the editor's -- asked of the page once a frame, since
        /// every key the game binds asks it (<see cref="UiText.Typing"/>).
        /// </summary>
        public static bool Focused
        {
            get
            {
                if (!Available)
                    return false;
                if (_focusFrame != Time.frameCount)
                {
                    _focusFrame = Time.frameCount;
                    _focused = BitSorterCodeFocused() != 0;
                }

                return _focused;
            }
        }
    }
}
