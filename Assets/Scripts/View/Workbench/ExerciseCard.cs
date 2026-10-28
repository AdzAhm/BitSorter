using System;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// An exercise's card (Part 15d), a menu of the workbench's: its brief -- its number and title, its goal, and the hint
    /// behind SHOW THE HINT, which the student asks for -- up as the exercise opens and under GOAL; and, once CHECK has
    /// solved it, what it took, the student's best, and the next one.
    /// </summary>
    public sealed class ExerciseCard : WaveMenu
    {
        /// <summary>The card's name, its lines' and its items'.</summary>
        public const string RootName = "Workbench exercise card", HeadingName = "Workbench exercise card heading",
            GoalName = "Workbench exercise card goal", HintName = "Workbench exercise card hint",
            HintItem = "Workbench exercise card show hint", NextItem = "Workbench exercise card next",
            ListItem = "Workbench exercise card list", CloseItem = "Workbench exercise card close";

        private const float ItemWidth = 600f;

        private Label _heading, _goal, _hint;
        private ShellButton _showHint, _next, _list, _close;
        private string _hintText;
        private Vector2 _at;
        private Action _onNext, _onList;

        /// <summary>What its lines say now; empty while it is not up.</summary>
        public string Heading => IsOpen ? _heading.text : "";

        /// <inheritdoc cref="Heading"/>
        public string Goal => IsOpen ? _goal.text : "";

        /// <inheritdoc cref="Heading"/>
        public string Hint => IsOpen ? _hint.text : "";

        /// <summary>Whether it is the solved card, rather than the brief.</summary>
        public bool Solved { get; private set; }

        /// <summary>What its NEXT item says; empty when there is no next one.</summary>
        public string NextCaption => _next.style.display == DisplayStyle.None ? "" : _next.text;

        public static ExerciseCard Build(ShellPanel shell, Transform owner, SchematicPalette palette)
        {
            ExerciseCard card = Make<ExerciseCard>(shell, owner, RootName, palette, ItemWidth);
            card._heading = card.Note(HeadingName);
            card._heading.style.color = palette.Accent;
            card._heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            card._goal = card.Note(GoalName);
            card._hint = card.Note(HintName);
            card._hint.style.color = palette.TextDim;
            card._showHint = card.Item(HintItem, "SHOW THE HINT", card.ShowTheHint);
            card._next = card.Item(NextItem, "", () => card._onNext?.Invoke());
            card._list = card.Item(ListItem, "ALL EXERCISES", () => card._onList?.Invoke());
            card._close = card.Item(CloseItem, "CLOSE", () => { }, breakBefore: true);
            return card;
        }

        /// <summary>The brief: <paramref name="heading"/>, the goal, and the hint behind an item, its corner at <paramref name="screenPoint"/>.</summary>
        public void Brief(Vector2 screenPoint, string heading, string goal, string hint)
        {
            Solved = false;
            _at = screenPoint;
            _heading.text = heading;
            _goal.text = goal;
            _hint.text = "";
            _hintText = hint;
            Offer(_showHint, !string.IsNullOrEmpty(hint));
            Offer(_next, false);
            Offer(_list, false);
            _close.text = "CLOSE";
            Show(screenPoint);
        }

        /// <summary>The brief again, the hint shown.</summary>
        public void ShowTheHint()
        {
            _hint.text = "HINT: " + _hintText;
            Offer(_showHint, false);
            Show(_at);
        }

        /// <summary>
        /// The solved card: <paramref name="heading"/>, what it <paramref name="took"/>, the student's <paramref name="best"/>,
        /// and NEXT as <paramref name="next"/> doing <paramref name="onNext"/> -- left off with none -- beside ALL EXERCISES.
        /// </summary>
        public void Solve(Vector2 screenPoint, string heading, string took, string best, string next, Action onNext, Action onList)
        {
            Solved = true;
            _at = screenPoint;
            _heading.text = "SOLVED: " + heading;
            _goal.text = took;
            _hint.text = best ?? "";
            _onNext = onNext;
            _onList = onList;
            Offer(_showHint, false);
            _next.text = next ?? "";
            Offer(_next, !string.IsNullOrEmpty(next) && onNext != null);
            Offer(_list, onList != null);
            _close.text = "STAY HERE";
            Show(screenPoint);
        }
    }
}
