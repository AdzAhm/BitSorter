using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.ViewModel;
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
            if (_session == null || name == null || member == null)
                return;
            BitSorter.Board.EditResult result = _session.Choose(name, member);
            Said(result);
            // The run the choice makes is shown whole when it takes over, its own length: the last program's window would
            // leave the playhead off its end (the 15h-6c shots: ISA's moment 6000 under SUM's 1,350).
            if (result.Done && _session.Replay != null)   // the program running chosen again makes no run
                _fitWavesOnSwitch = true;
        }

        /// <summary>Whether the next run to take over is shown whole: one a program was chosen for.</summary>
        private bool _fitWavesOnSwitch;

        /// <summary>The window the waves show, for the tests.</summary>
        public BitSorter.ViewModel.WaveView WaveWindow => _wave?.View;

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
                RememberTheProgram(choice);
                _programButton.style.display = choice != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (choice != null)
                    SetText(_programButton, choice.Name + ": " + (choice.Chosen != null ? BenchChoice.Said(choice.Chosen).ToUpperInvariant() : "AS WRITTEN"));
                if (!ExerciseControlsShown && Chip != null)
                    Chip.style.right = ProgramChipRoom();
            }
        }

        /// <summary>
        /// Sample <paramref name="sample"/> made afresh, on the program last chosen on it (Ahmad, 2026-11-02): the save keeps
        /// it, since a sample is never kept itself, and it is put in before the run is made -- no step, no second run.
        /// </summary>
        private static Project SampleOnItsProgram(int sample)
        {
            Project built = WorkbenchSamples.Chip[sample].Build();
            ProgressStore store = Store();
            return store == null ? built : BenchChoices.Applied(built, store.SamplePrograms(WorkbenchSamples.Chip[sample].Name));
        }

        /// <summary>
        /// The program running on a sample kept as the one it opens on next: whenever the chip sees it change, so a choice
        /// from the chip or a start-page card, and an undo of one, are each remembered. A circuit of one's own keeps its
        /// choice in its document already.
        /// </summary>
        private void RememberTheProgram(BenchChoice choice)
        {
            if (_sample == 0 || choice?.Chosen == null)
                return;
            ProgressStore store = Store();
            if (store != null && store.SetSampleProgram(WorkbenchSamples.Chip[_sample].Name, choice.Name, choice.Chosen))
                store.Save();
        }

        /// <summary>The room the chip takes at the row's end, with its gap; none while it is down.</summary>
        private float ProgramChipRoom() => _shownChoice != null ? ProgramWidth + RowGap : 0f;

        /// <summary>Whether GOAL and CHECK hold the row's end: an exercise open in place 0.</summary>
        private bool ExerciseControlsShown => _exercise != null && _sample == 0;
    }
}
