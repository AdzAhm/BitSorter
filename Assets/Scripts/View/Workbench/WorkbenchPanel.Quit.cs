using System;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// QUIT (5.1; Ahmad: "I couldn't exit the simulator unless I exited full screen"): a red button (<see cref="ShellTheme.Destructive"/>)
    /// at the window's bottom right, at the status line's right end (<see cref="ShellLayout.QuitIn"/>), in a desktop build and the editor alone -- a browser's
    /// tab is the browser's to close. It asks first, above itself so a double-click cannot answer, and the answer keeps what
    /// quitting keeps (<see cref="OnApplicationQuit"/>) before it quits: the circuit, and code typed and not applied as its
    /// draft. It stands over the start page and every other page too, which cover the status line, or there would be no
    /// way out from where BitSorter so often opens.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        public const string QuitButtonName = "Workbench quit";

        /// <summary>What QUIT asks.</summary>
        public const string QuitQuestion = "Quit BitSorter? Everything is saved.";

        /// <summary>
        /// What quits, once QUIT's question is answered: the application. <c>SaveGuard</c> puts one in its place that quits
        /// nothing for every test, and a test that presses QUIT counts its calls; in the editor <see cref="Application.Quit"/>
        /// does nothing anyway.
        /// </summary>
        public static Action Quitter = Application.Quit;

        /// <summary>Whether this build has QUIT: a desktop player does, and the editor; a browser does not.</summary>
        public static bool Quits =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            true;
#endif

        private ShellButton _quit;

        /// <summary>QUIT's button; null in a browser.</summary>
        public ShellButton QuitButton => _quit;

        private void BuildQuit(SchematicPalette palette)
        {
            if (!Quits)
                return;
            _quit = ShellTheme.Button(QuitButtonName, "QUIT", palette, AskToQuit, UiType.Label);
            ShellTheme.Destructive(_quit, palette);   // red, as RESET PROGRESS is (Ahmad: "make it red")
            _shell.Root.Add(_quit);
        }

        /// <summary>
        /// QUIT kept in front of the pages, which bring themselves to the front as they open -- the start page covers the
        /// status line -- but never in front of a popup, which would cover its own question.
        /// </summary>
        private void TickQuit()
        {
            if (_quit == null || ShellPopup.AnyOpen)
                return;
            VisualElement parent = _quit.parent;
            if (parent != null && parent.IndexOf(_quit) != parent.childCount - 1)
                _quit.BringToFront();
        }

        /// <summary>QUIT's question, above the button: QUIT, red, quits; CANCEL, Escape or a click off it does nothing.</summary>
        public void AskToQuit()
        {
            if (_ask == null || _quit == null)
                return;
            _ask.AskAbove(ShellPanel.ScreenRectOf(_quit), QuitQuestion, "QUIT", Quit, destructive: true);
        }

        /// <summary>
        /// Quits, keeping first what <see cref="OnApplicationQuit"/> keeps -- the circuit at its last edit, and code typed and not
        /// applied as its draft -- so it is kept before <see cref="Quitter"/> is called, whatever that does.
        /// </summary>
        public void Quit()
        {
            KeepTheTypedCode();
            KeepMyCircuit(now: true);
            Quitter?.Invoke();
        }
    }
}
