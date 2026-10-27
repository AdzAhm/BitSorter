using System;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// A question before something a click should not do by itself (Part 13d): replacing MY CIRCUIT with an import,
    /// converting a testbench, deleting a module (Part 14g, which Ctrl+Z still brings back). The question, then the thing to
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
            _question.text = question;
            _yes.text = yes;
            _onYes = onYes;
            Show(screenPoint);
        }
    }
}
