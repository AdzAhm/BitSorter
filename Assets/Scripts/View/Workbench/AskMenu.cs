using System;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// A question before something a click should not do by itself (Part 13d): converting a testbench, deleting a module
    /// (Part 14g, which Ctrl+Z still brings back), deleting a circuit (Part 15a, which nothing does), quitting (5.1). It once asked before an
    /// import replaced MY CIRCUIT; since Part 15a an import is a circuit of its own. The question, then the thing to
    /// do and CANCEL; Escape or a click off it is CANCEL.
    /// </summary>
    public sealed class AskMenu : WaveMenu
    {
        /// <summary>The menu's name, its question's, and its two items'.</summary>
        public const string RootName = "Workbench ask", QuestionName = "Workbench ask question", YesItem = "Workbench ask yes", NoItem = "Workbench ask no";

        private const float ItemWidth = 520f;

        private UnityEngine.UIElements.Label _question;
        private ShellButton _yes;
        private Action _onYes;

        /// <summary>What it asks now; empty while it is not up.</summary>
        public string Question => IsOpen ? _question.text : "";

        public static AskMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette)
        {
            AskMenu menu = Make<AskMenu>(shell, owner, RootName, palette, ItemWidth);
            menu._question = menu.Note(QuestionName);
            menu._yes = menu.Item(YesItem, "", () => menu._onYes?.Invoke());
            menu.Item(NoItem, "CANCEL", () => { });
            return menu;
        }

        /// <summary>Asks <paramref name="question"/> with its corner at <paramref name="screenPoint"/>; <paramref name="yes"/> does <paramref name="onYes"/>.</summary>
        public void Ask(Vector2 screenPoint, string question, string yes, Action onYes)
        {
            Set(question, yes, onYes);
            Show(screenPoint);
        }

        /// <summary>
        /// Asks <paramref name="question"/> above <paramref name="anchor"/> (screen pixels, y up), never over it, so a
        /// double-click on the control that asked cannot answer (QUIT, 5.1); <paramref name="yes"/> does <paramref name="onYes"/>,
        /// drawn red when it is <paramref name="destructive"/> (<see cref="ShellTheme.Destructive"/>), as RESET PROGRESS's
        /// answer is.
        /// </summary>
        public void AskAbove(Rect anchor, string question, string yes, Action onYes, bool destructive = false)
        {
            Set(question, yes, onYes, destructive);
            ShowAbove(anchor);
        }

        /// <summary>Whether the answer is drawn as one that destroys something or ends the session.</summary>
        public bool AnswerIsDestructive { get; private set; }

        private void Set(string question, string yes, Action onYes, bool destructive = false)
        {
            _question.text = question;
            _yes.text = yes;
            _onYes = onYes;
            if (destructive)
            {
                ShellTheme.Destructive(_yes, Palette);
            }
            else
            {
                // The answer as every item is drawn (ShellTheme.Button), after a red one: the menu is the same for every question.
                ShellTheme.Outline(_yes, Palette.Outline, Palette.Ground);
                _yes.style.color = Palette.Text;
            }
            AnswerIsDestructive = destructive;
        }
    }
}
