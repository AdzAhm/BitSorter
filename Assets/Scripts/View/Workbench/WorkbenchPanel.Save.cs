using System;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BitSorter.View
{
    /// <summary>
    /// What the workbench says of the save itself (Ahmad's decisions, 2026-11-03). Held -- another window wrote it since this
    /// one read it (browser-1), or it could not be read as BitSorter opened (save-7) -- nothing is written over it: the chip
    /// says so in plain words until the save is read again, the question to reload goes up once, and FILE has RELOAD THE SAVE.
    /// A save a newer BitSorter wrote is said to be one as BitSorter opens, a copy of it kept beside it before the first write.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>What the chip says while another window's save is held to (browser-1).</summary>
        public const string SavedElsewhere =
            "Another BitSorter window saved since this one opened, so this one saves nothing rather than write over it. " +
            "FILE, RELOAD THE SAVE opens what that window saved; FILE, EXPORT keeps a copy of what is here first.";

        /// <summary>What the chip says while the save could not be read (save-7).</summary>
        public const string SaveNotRead =
            "BitSorter could not read its save as it opened, so it saves nothing rather than write over it. " +
            "FILE, RELOAD THE SAVE reads it again.";

        /// <summary>What BitSorter says as it opens on a save a newer BitSorter wrote.</summary>
        public const string SaveFromANewerBitSorter =
            "This save is from a newer BitSorter: what this one does not know of it is left out as it saves, so the save is " +
            "copied beside itself, as it is, first.";

        /// <summary>The question to reload, as the save is found held: after another window's save, or after a save not read.</summary>
        public const string ReloadQuestion =
            "Another BitSorter window saved since this one opened. Open what it saved? What was changed here since goes.";

        /// <inheritdoc cref="ReloadQuestion"/>
        public const string ReadAgainQuestion =
            "BitSorter could not read its save as it opened. Read it again? What was done here since goes.";

        /// <summary>The answer that reloads.</summary>
        public const string ReloadYes = "RELOAD THE SAVE";

        /// <summary>Why the save is held now, as the store says; none without one.</summary>
        private SaveHold Held() => _tracker != null && _tracker.Store != null ? _tracker.Store.Held : SaveHold.None;

        /// <summary>What the chip says while the save is held; null while it is not.</summary>
        private static string HeldWords(SaveHold held) =>
            held == SaveHold.WrittenElsewhere ? SavedElsewhere : held == SaveHold.CouldNotRead ? SaveNotRead : null;

        /// <summary>The hold last said: each is said, and the question asked, once, as it is found.</summary>
        private SaveHold _heldSaid;

        /// <summary>The save found held, said and the question to reload asked: called every frame.</summary>
        private void WatchTheSave()
        {
            SaveHold held = Held();
            if (held == _heldSaid)
                return;
            _heldSaid = held;
            if (held == SaveHold.None || _ask == null)
                return;

            Notice(HeldWords(held));
            _ask.Ask(UnderTheFileChip(), held == SaveHold.WrittenElsewhere ? ReloadQuestion : ReadAgainQuestion, ReloadYes, ReloadTheSave);
        }

        /// <summary>
        /// FILE, RELOAD THE SAVE: BitSorter as it opens again, on the save as it is now. In a browser, the page loaded again,
        /// which reads the save from the page's own storage, where the other tab wrote it; elsewhere the scene, whose
        /// progress tracker reads the file afresh as it wakes.
        /// </summary>
        public void ReloadTheSave()
        {
            if (BrowserSave.Available)
            {
                BrowserSave.ReloadPage();
                return;
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
        }

        // -----------------------------------------------------------------
        // A circuit the save keeps that cannot be opened (save-1)
        // -----------------------------------------------------------------

        /// <summary>The place on the SAMPLE chip of the reference circuit, which opens in place of one that cannot.</summary>
        private const int ReferenceSample = 1;

        /// <summary>What the circuit asked for last and not opened is called, and why it was not; null once it is said.</summary>
        private string _couldNotOpen, _couldNotOpenWhy;

        /// <summary>The key of the circuit not opened -- an exercise's, its id -- which START IT AGAIN starts again blank.</summary>
        private string _couldNotOpenKey;

        /// <summary>
        /// The session of what is to be shown: a sample, or the circuit open. One the save keeps that cannot be opened -- one the
        /// rules refuse, edited by hand or written by a newer BitSorter -- threw from the workbench's Start, which then never
        /// built, on every launch that opened it, and from FILE, OPEN half way through the switch (save-1, save-4). It is left in
        /// the save as it is, and never kept over, for the reference circuit is shown in its place -- MY CIRCUIT where the SAMPLE
        /// chip goes back to -- and why is said once what is shown is built (<see cref="SayItCouldNotOpen"/>).
        /// </summary>
        private WorkbenchSession OpenSession()
        {
            Func<double> clock = () => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0;
            if (_sample != 0)
                return new WorkbenchSession(SampleOnItsProgram(_sample), clock);

            // A circuit opens with the code typed into it and not applied, as the save kept it (Ahmad, 2026-11-03); a sample is
            // never written, and opens as it is made.
            // An exercise's drawn answer reads as code under the exercise's header, as WRITE IT wrote it before 5.1 -- and, where
            // the exercise is answered by drawing alone, not at all.
            Exercise exercise = _exercise;
            WorkbenchSession opened = WorkbenchSession.TryOpen(MyCircuit(), clock, out string why, DraftsOf(_documentKey),
                exercise != null ? (project, module) => module == project.Top ? exercise.Written(project).Source : null : (Func<Project, string, string>)null);
            if (opened != null)
            {
                opened.CodeAllowed = exercise == null || exercise.Code;
                return opened;
            }

            _couldNotOpen = _exercise != null ? _exercise.Title
                : _documentKey == WorkbenchSamples.MyCircuitKey ? "MY CIRCUIT" : Documents.NameOf(_documentKey);
            _couldNotOpenWhy = why;
            _couldNotOpenKey = _exercise != null ? _exercise.Id : _documentKey;
            Debug.LogWarning($"BitSorter: {_couldNotOpen} could not be opened -- {why}");

            LeaveTheExercise();
            _documentKey = WorkbenchSamples.MyCircuitKey;
            _sample = ReferenceSample;
            return new WorkbenchSession(SampleOnItsProgram(_sample), clock);
        }

        /// <summary>What is said of a circuit called <paramref name="name"/> that could not be opened, <paramref name="why"/>.</summary>
        public static string CouldNotOpen(string name, string why) =>
            name + " could not be opened: " + why + " It is kept in the save as it was, and the reference circuit is shown instead.";

        /// <summary>What START IT AGAIN asks first, of a circuit called <paramref name="name"/>.</summary>
        public static string StartAgainQuestion(string name) =>
            "Start " + name + " again, blank? What the save keeps of it now goes for good.";

        /// <summary>The answer that starts it again.</summary>
        public const string StartAgainYes = "START IT AGAIN";

        /// <summary>A circuit that could not be opened said, and the question to start it again asked, once what is shown is built.</summary>
        private void SayItCouldNotOpen()
        {
            if (_couldNotOpen == null)
                return;
            string name = _couldNotOpen;
            _couldNotOpen = null;
            Notice(CouldNotOpen(name, _couldNotOpenWhy));
            _askedStartAgain = _couldNotOpenKey;
            _ask?.Ask(UnderTheFileChip(), StartAgainQuestion(name), StartAgainYes, StartAgain);
        }

        /// <summary>The circuit START IT AGAIN was asked of: the answer starts that one again or nothing.</summary>
        private string _askedStartAgain;

        /// <summary>
        /// START IT AGAIN: the circuit that could not be opened made blank in the save, and opened -- MY CIRCUIT as it starts, a
        /// circuit of the student's under its own name, an exercise as it starts.
        /// </summary>
        public void StartAgain()
        {
            string key = _askedStartAgain;
            _askedStartAgain = null;
            ProgressStore store = Store();
            if (key == null || store == null)
                return;

            if (Documents.IsDocument(key))
            {
                store.SetProject(key, key == WorkbenchSamples.MyCircuitKey ? WorkbenchSamples.Blank() : Project.New(Documents.ModuleNameFor(Documents.NameOf(key))));
                store.Save();
                OpenDocument(key);
                return;
            }

            store.RemoveProject(key);   // an exercise's answer: the exercise as it starts
            store.Save();
            OpenExercise(key);
        }

        /// <summary>What the save itself has to say as BitSorter opens: held, or a newer BitSorter's; null for nothing.</summary>
        private string WhatTheSaveSays()
        {
            ProgressStore store = _tracker != null ? _tracker.Store : null;
            if (store == null)
                return null;
            if (store.Held != SaveHold.None)
                return null;   // said, with the question, by WatchTheSave
            return store.FromANewerVersion != null ? SaveFromANewerBitSorter : null;
        }
    }
}
