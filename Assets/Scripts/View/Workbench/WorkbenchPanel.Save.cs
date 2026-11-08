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
