using System.Collections.Generic;
using BitSorter.Board;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The PROGRAM chip (Part 15h-6c, Ahmad's choice, 2026-11-01: "a way he can choose what program to run (not code)"): at
    /// the controls' row's right end while the testbench has a parameter of an enum type -- a processor's PROGRAM -- saying
    /// what is chosen, PROGRAM: FIBONACCI, and opening a menu of its members. Choosing is one undo step, and the run is made
    /// again with it (<see cref="BitSorter.ViewModel.WorkbenchSession.Choose"/>). The first such parameter has the chip.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The chip's name.</summary>
        public const string ProgramButton = "Workbench program";

        private const float ProgramWidth = 250f;

        private ShellButton _programButton;
        private ChoiceMenu _choiceMenu;
        private BenchChoice _shownChoice;

        /// <summary>The menu the chip opens.</summary>
        public ChoiceMenu ChoiceMenu => _choiceMenu;

        /// <summary>The choice the chip shows, or null while it is down.</summary>
        public BenchChoice ProgramChoice => _shownChoice;

        private void BuildProgramChip(SchematicPalette palette)
        {
            _programButton = ShellTheme.Button(ProgramButton, "", palette, OpenChoiceMenu);
            ShellTheme.PlaceRight(_programButton, 0f, 0f, ProgramWidth, UiTheme.ButtonHeight);
            _programButton.style.display = DisplayStyle.None;
            _controls.Add(_programButton);
            _choiceMenu = ChoiceMenu.Build(_shell, transform, palette, Choose);
        }

        /// <summary>Opens the chip's menu under it.</summary>
        public void OpenChoiceMenu()
        {
            if (_shownChoice != null)
                _choiceMenu?.Open(UnderTheChip(ProgramButton), _shownChoice);
        }

        /// <summary><paramref name="member"/> chosen for the testbench's <paramref name="name"/>, as the menu chooses it.</summary>
        public void Choose(string name, string member)
        {
            if (_session != null && name != null && member != null)
                Said(_session.Choose(name, member));
        }

        /// <summary>
        /// Each frame: the chip up while the testbench has a choice and no exercise has the row's end, saying what is chosen.
        /// The run's own chip, beside it, is made room for.
        /// </summary>
        private void RefreshProgramChip()
        {
            if (_programButton == null)
                return;
            IReadOnlyList<BenchChoice> choices = _session != null && !ExerciseControlsShown ? _session.Choices : null;
            BenchChoice choice = choices != null && choices.Count > 0 ? choices[0] : null;
            if (!ReferenceEquals(choice, _shownChoice))
            {
                _shownChoice = choice;
                _programButton.style.display = choice != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (choice != null)
                    SetText(_programButton, choice.Name + ": " + BenchChoice.Said(choice.Chosen ?? "").ToUpperInvariant());
                if (!ExerciseControlsShown && Chip != null)
                    Chip.style.right = ProgramChipRoom();
            }
        }

        /// <summary>The room the chip takes at the row's end, with its gap; none while it is down.</summary>
        private float ProgramChipRoom() => _shownChoice != null ? ProgramWidth + RowGap : 0f;

        /// <summary>Whether GOAL and CHECK hold the row's end: an exercise open in place 0.</summary>
        private bool ExerciseControlsShown => _exercise != null && _sample == 0;
    }
}
