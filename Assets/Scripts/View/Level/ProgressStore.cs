using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The save file's shape, exactly as JsonUtility writes it.
    /// </summary>
    /// <remarks>
    /// Completion is a list of names rather than a count, and that is the whole design. JsonUtility
    /// cannot tell a missing key from an explicit zero -- a trap this codebase has already been
    /// bitten by and documents in several places -- so "levels completed: 0" would be
    /// indistinguishable from "never played". A name is either in the list or it is not, and an
    /// absent list is unambiguously nobody.
    /// </remarks>
    [Serializable]
    public sealed class ProgressFile
    {
        /// <summary>Level file names, without extension, as LevelLoader takes them.</summary>
        public string[] completed;

        /// <summary>What was left on each board, and the best it has been solved.</summary>
        public SavedBoard[] boards;

        /// <summary>
        /// Ids of the first-time hints this player has already been shown.
        /// </summary>
        /// <remarks>
        /// A list of ids for the same reason <see cref="completed"/> is a list of names: presence
        /// means seen, and an absent array is unambiguously "nothing shown yet". A count or a set
        /// of flags would run straight into the JsonUtility trap this format was shaped around,
        /// where a missing key and an explicit zero read the same.
        /// </remarks>
        public string[] hintsSeen;

        /// <summary>
        /// One-off things this player has been through, such as the guided tutorial.
        /// </summary>
        /// <remarks>
        /// Its own array rather than a name in <see cref="completed"/>: that one counts solved
        /// levels, and <c>MenuRules.AllSolved</c> and the ending panel both read the count, so a
        /// tutorial in there would make nine levels look like ten. And not in
        /// <see cref="hintsSeen"/> either, whose ids are held to hint rules by tests.
        /// </remarks>
        public string[] milestones;

        /// <summary>Free play's boards, each under its own name, and which one is open.</summary>
        /// <remarks>
        /// Kept apart from <see cref="boards"/>, where free play's one board sat until 4.0.0 under
        /// the key <c>"sandbox"</c>. <see cref="ProgressStore.Load"/> moves a board found there into
        /// this list, so a file written by 4.0.0 or later never has one in both places.
        /// </remarks>
        public FreePlayFile freePlay;
    }

    /// <summary>Free play's named boards, as the save file holds them.</summary>
    [Serializable]
    public sealed class FreePlayFile
    {
        /// <summary>Which of <see cref="saves"/> is open.</summary>
        public int active;

        public FreePlaySave[] saves;
    }

    /// <summary>One named free-play board, its setup included.</summary>
    [Serializable]
    public sealed class FreePlaySave
    {
        public string name;

        /// <summary>The board, carrying its <see cref="SavedBoard.sandbox"/> setup as it always has.</summary>
        public SavedBoard board;
    }

    /// <summary>
    /// Which levels have been solved, remembered between sessions.
    /// </summary>
    /// <remarks>
    /// A plain class rather than a MonoBehaviour, taking its path as a constructor argument, so the
    /// whole thing can be tested against a scratch file without a scene and without touching the
    /// player's real save.
    ///
    /// Nothing here throws. A save file is the one piece of state the game cannot recreate, but it is
    /// also the one most likely to be truncated by a crash or edited by hand -- and losing a session
    /// to a stack trace on startup is a far worse failure than losing the record of which levels were
    /// finished. Anything unreadable is treated as "nothing completed yet".
    ///
    /// Treated as, not thrown away. The next save would replace an unreadable file, so a copy is
    /// kept beside it first, as <c>progress.json.unreadable</c>, for anyone who wants to recover it by
    /// hand. And a save is never written over the file it replaces: see <see cref="Save"/>.
    /// </remarks>
    public sealed class ProgressStore
    {
        private readonly string _path;
        private readonly HashSet<string> _completed = new HashSet<string>(StringComparer.Ordinal);

        private readonly Dictionary<string, SavedBoard> _boards =
            new Dictionary<string, SavedBoard>(StringComparer.Ordinal);

        private readonly HashSet<string> _hintsSeen = new HashSet<string>(StringComparer.Ordinal);

        private readonly HashSet<string> _milestones = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Free play's boards, in the order the player made them.</summary>
        private readonly List<FreePlaySave> _freePlay = new List<FreePlaySave>();

        /// <summary>Which of <see cref="_freePlay"/> is open; meaningless while there are none.</summary>
        private int _activeFreePlay;

        public ProgressStore(string path)
        {
            _path = path;
        }

        /// <summary>Where a save is written before it is moved over the real file.</summary>
        private string TempPath => _path + ".tmp";

        /// <summary>Where the last save that could not be read is kept.</summary>
        private string UnreadablePath => _path + ".unreadable";

        /// <summary>Where the real game keeps its progress.</summary>
        public static string DefaultPath =>
            Redirected ?? Path.Combine(Application.persistentDataPath, "progress.json");

        /// <summary>
        /// Sends every store built from <see cref="DefaultPath"/> somewhere else. Null in the game.
        /// </summary>
        /// <remarks>
        /// This exists because Play Mode tests load the real scene, and the real scene opens the real
        /// save. The first attempt at protecting it moved the file aside and moved it back afterwards,
        /// which has two failure modes and hit both: the restore logic destroyed a save outright once,
        /// and an interrupted run left the file missing, so the game looked like a fresh install until
        /// somebody put it back by hand.
        ///
        /// Redirecting removes the class rather than handling it. The player's file is never opened,
        /// copied, moved or deleted by a test, so there is no state to get wrong and nothing to
        /// restore if a run dies half way through.
        ///
        /// It has to be set before the scene loads: <see cref="ProgressTracker"/> builds its store in
        /// Awake and never looks at the path again.
        /// </remarks>
        public static string Redirected { get; set; }

        /// <summary>Why the last load failed, or null. For diagnostics, never for control flow.</summary>
        public string LastError { get; private set; }

        public bool IsComplete(string levelName) =>
            !string.IsNullOrEmpty(levelName) && _completed.Contains(levelName);

        public int CompletedCount => _completed.Count;

        /// <summary>Records a level as solved and writes the file. Idempotent.</summary>
        public bool MarkComplete(string levelName)
        {
            if (string.IsNullOrEmpty(levelName) || !_completed.Add(levelName))
                return false;

            Save();
            return true;
        }

        /// <summary>Whether a first-time hint has already been shown to this player.</summary>
        public bool HasSeenHint(string hintId) =>
            !string.IsNullOrEmpty(hintId) && _hintsSeen.Contains(hintId);

        /// <summary>
        /// Records a first-time hint as shown and writes the file. Idempotent, and returns whether
        /// this was the first time -- which is the caller's cue to actually show it.
        /// </summary>
        /// <remarks>
        /// Writes immediately, exactly as <see cref="MarkComplete"/> does. There are a handful of
        /// these in a save's whole lifetime, and a hint shown twice because the game closed before a
        /// deferred write is the one failure this is meant to prevent.
        /// </remarks>
        public bool MarkHintSeen(string hintId)
        {
            if (string.IsNullOrEmpty(hintId) || !_hintsSeen.Add(hintId))
                return false;

            Save();
            return true;
        }

        /// <summary>Whether this player has already been through a one-off, such as the tutorial.</summary>
        public bool HasMilestone(string id) =>
            !string.IsNullOrEmpty(id) && _milestones.Contains(id);

        /// <summary>Records a one-off and writes the file. Idempotent; true only the first time.</summary>
        public bool MarkMilestone(string id)
        {
            if (string.IsNullOrEmpty(id) || !_milestones.Add(id))
                return false;

            Save();
            return true;
        }

        /// <summary>
        /// Reads the file, or starts empty if there is nothing readable there.
        /// </summary>
        /// <remarks>
        /// Never throws, and the catch is deliberately broad. Every distinct failure here -- no file,
        /// no permission, half a file, a file someone edited by hand -- has exactly the same correct
        /// response, which is to carry on with no progress recorded. Enumerating them would add
        /// branches without adding behaviour, and any one missed would crash the game on startup.
        /// </remarks>
        public void Load()
        {
            LastError = null;
            _completed.Clear();
            _boards.Clear();
            _hintsSeen.Clear();
            _milestones.Clear();
            _freePlay.Clear();
            _activeFreePlay = 0;

            string source = null;

            try
            {
                source = ReadablePath();

                if (source == null)
                    return;   // a first run is not a failure

                string json = File.ReadAllText(source);

                if (string.IsNullOrWhiteSpace(json))
                    return;

                var file = JsonUtility.FromJson<ProgressFile>(json);

                if (file == null)
                    return;

                // Null rather than empty when the key is absent, which is the JsonUtility trap this
                // format was shaped around. Each array is guarded on its own, so a file written
                // before boards existed still restores its completions.
                if (file.completed != null)
                {
                    foreach (string name in file.completed)
                    {
                        if (!string.IsNullOrEmpty(name))
                            _completed.Add(name);
                    }
                }

                if (file.boards != null)
                {
                    foreach (SavedBoard board in file.boards)
                    {
                        if (board != null && !string.IsNullOrEmpty(board.level))
                            _boards[board.level] = board;
                    }
                }

                // Guarded on its own, like the two above, so a save written before hints existed
                // still restores its completions and boards rather than being read as corrupt.
                if (file.hintsSeen != null)
                {
                    foreach (string id in file.hintsSeen)
                    {
                        if (!string.IsNullOrEmpty(id))
                            _hintsSeen.Add(id);
                    }
                }

                if (file.milestones != null)
                {
                    foreach (string id in file.milestones)
                    {
                        if (!string.IsNullOrEmpty(id))
                            _milestones.Add(id);
                    }
                }

                LoadFreePlay(file.freePlay);
            }
            catch (Exception exception)
            {
                // Recorded rather than thrown. Someone debugging a lost save can find out why; a
                // player mid-session never has to.
                LastError = exception.Message;
                _completed.Clear();

                // Cleared too, erring towards showing a hint again rather than silently swallowing
                // one: a half-read file must not leave a player taught by a save it could not parse.
                _hintsSeen.Clear();
                _milestones.Clear();

                KeepAside(source);
            }
        }

        /// <summary>
        /// The file to read: the save itself, or a finished write that never got moved into place.
        /// </summary>
        /// <remarks>
        /// <see cref="Save"/> removes the old file before moving the new one in, so a crash between
        /// the two leaves only the temporary file, and it is complete. Reading it is the difference
        /// between losing nothing and looking like a fresh install.
        ///
        /// When both exist, the real file wins. The temporary one is then either a finished write
        /// that died before replacing it, which costs only the last change, or a half-finished one,
        /// which must not be read at all.
        /// </remarks>
        private string ReadablePath()
        {
            if (File.Exists(_path))
                return _path;

            return File.Exists(TempPath) ? TempPath : null;
        }

        /// <summary>
        /// Copies a save that could not be read to where the next save will not replace it.
        /// </summary>
        private void KeepAside(string source)
        {
            if (source == null)
                return;

            try
            {
                File.Copy(source, UnreadablePath, true);
            }
            catch (Exception)
            {
                // The load's own error is already recorded, and there is nothing more to try.
            }
        }

        /// <summary>Writes the file, and says nothing if it cannot.</summary>
        /// <remarks>
        /// Never written in place. Writing over the real file empties it first, so a crash part way
        /// through left nothing complete anywhere. The whole save goes to a temporary file beside it,
        /// the old file is removed, and the new one is moved into its place; see
        /// <see cref="ReadablePath"/> for how each point of failure reads back.
        ///
        /// Delete and move rather than File.Replace, because rename is the one file operation every
        /// platform this ships to supports, the browser's virtual file system included.
        /// </remarks>
        public void Save()
        {
            try
            {
                var file = new ProgressFile
                {
                    completed = new string[_completed.Count],
                    boards = new SavedBoard[_boards.Count],
                    hintsSeen = new string[_hintsSeen.Count],
                    milestones = new string[_milestones.Count],
                    freePlay = new FreePlayFile
                    {
                        active = _activeFreePlay,
                        saves = _freePlay.ToArray(),
                    },
                };

                _completed.CopyTo(file.completed);
                _boards.Values.CopyTo(file.boards, 0);
                _hintsSeen.CopyTo(file.hintsSeen);
                _milestones.CopyTo(file.milestones);

                string directory = Path.GetDirectoryName(_path);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(TempPath, JsonUtility.ToJson(file, true));

                if (File.Exists(_path))
                    File.Delete(_path);

                File.Move(TempPath, _path);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
            }
        }

        /// <summary>Forgets everything, on disk as well as in memory.</summary>
        public void Clear()
        {
            _completed.Clear();
            _boards.Clear();
            _hintsSeen.Clear();
            _milestones.Clear();
            _freePlay.Clear();
            _activeFreePlay = 0;
            Save();
        }

        // -----------------------------------------------------------------
        // Boards and personal bests
        // -----------------------------------------------------------------

        /// <summary>What was left on a level's board, or null if it has never been touched.</summary>
        /// <remarks>
        /// Free play's key means the open board, whichever of the player's that is. Everything that
        /// saves and restores boards -- <see cref="ProgressTracker"/>, the setup panel -- goes on
        /// asking for one board by one key, and only the store knows there are several.
        /// </remarks>
        public SavedBoard BoardFor(string level)
        {
            if (string.IsNullOrEmpty(level))
                return null;

            if (level == SandboxLevel.Key)
                return _freePlay.Count > 0 ? _freePlay[_activeFreePlay].board : null;

            return _boards.TryGetValue(level, out SavedBoard board) ? board : null;
        }

        /// <summary>Puts a board in its place: the open free-play board, or the level's.</summary>
        private void Put(string level, SavedBoard board)
        {
            if (level != SandboxLevel.Key)
            {
                _boards[level] = board;
                return;
            }

            // Free play saved before any board of its own was made: that is the first one.
            if (_freePlay.Count == 0)
            {
                _freePlay.Add(new FreePlaySave { name = BoardNames.First });
                _activeFreePlay = 0;
            }

            _freePlay[_activeFreePlay].board = board;
        }

        /// <summary>Remembers what is on a level's board, keeping any record already set.</summary>
        public void SaveBoard(string level, SavedBoard board)
        {
            if (StageBoard(level, board))
                Save();
        }

        /// <summary>
        /// Same merge as <see cref="SaveBoard"/>, but leaves the file alone. False when there was
        /// nothing to record.
        /// </summary>
        /// <remarks>
        /// For a caller that is about to cause a write anyway and would otherwise cause a second one.
        /// The sandbox panel is the case: changing a source re-adopts the level, which raises
        /// LevelUnloading, which makes <see cref="ProgressTracker"/> save the board -- so staging the
        /// setup here lets that one write carry it, instead of following it with another.
        ///
        /// Safe to stage and never write: every path that matters writes afterwards. Leaving the
        /// sandbox raises LevelUnloading and quitting saves the open board, and both merge whatever
        /// is staged here into the file.
        /// </remarks>
        public bool StageBoard(string level, SavedBoard board)
        {
            if (string.IsNullOrEmpty(level) || board == null)
                return false;

            SavedBoard existing = BoardFor(level);

            // The record outlives the layout. A player who wipes a board has not lost the fact that
            // they once solved it in four gates.
            if (existing != null)
            {
                board.bestGates = existing.bestGates;
                board.bestLatency = existing.bestLatency;

                // So does free play's set of sources and sinks, and for the same reason. Routine
                // board saves come from ProgressTracker, which knows nothing about sandboxes and
                // leaves this null; carrying it forward here is what stops an ordinary save of the
                // gates wiping the fixtures they are wired to.
                if (board.sandbox == null)
                    board.sandbox = existing.sandbox;
            }

            board.level = level;
            Put(level, board);

            return true;
        }

        /// <summary>Fewest gates a level has been solved with, or zero for no record.</summary>
        public int BestGates(string level) => BoardFor(level)?.bestGates ?? 0;

        /// <inheritdoc cref="BestGates"/>
        public int BestLatency(string level) => BoardFor(level)?.bestLatency ?? 0;

        /// <summary>
        /// Records a solution, and says which of the two records it beat.
        /// </summary>
        /// <remarks>
        /// The two are tracked separately and improve independently, because they genuinely trade
        /// against each other -- the XOR-trick multiplexer is a gate smaller and a tick of budget
        /// dearer than the textbook one. Collapsing them into a single "better" would make one of
        /// the two invisible, and the trade is the lesson.
        /// </remarks>
        public bool RecordBest(string level, int gates, int latency, out bool gatesBeaten, out bool latencyBeaten)
        {
            gatesBeaten = false;
            latencyBeaten = false;

            if (string.IsNullOrEmpty(level) || gates < 0 || latency < 0)
                return false;

            SavedBoard board = BoardFor(level);

            if (board == null)
            {
                board = new SavedBoard { level = level };
                Put(level, board);
            }

            // A first solve sets both records without being a personal best -- there was nothing to
            // beat, and telling someone they beat their record on their first attempt is hollow.
            bool first = board.bestGates == 0 && board.bestLatency == 0;

            if (board.bestGates == 0 || gates < board.bestGates)
            {
                gatesBeaten = !first && board.bestGates != 0;
                board.bestGates = gates;
            }

            if (board.bestLatency == 0 || latency < board.bestLatency)
            {
                latencyBeaten = !first && board.bestLatency != 0;
                board.bestLatency = latency;
            }

            Save();

            return gatesBeaten || latencyBeaten;
        }

        // -----------------------------------------------------------------
        // Free play's boards
        // -----------------------------------------------------------------

        /// <summary>How many boards free play has.</summary>
        public int FreePlayCount => _freePlay.Count;

        /// <summary>Which board is open. Zero while there are none.</summary>
        public int ActiveFreePlay => _activeFreePlay;

        /// <summary>A board's name, or null for an index with no board.</summary>
        public string FreePlayName(int index) =>
            index >= 0 && index < _freePlay.Count ? _freePlay[index].name : null;

        /// <summary>A board's saved state, or null for an index with no board.</summary>
        public SavedBoard FreePlayBoard(int index) =>
            index >= 0 && index < _freePlay.Count ? _freePlay[index].board : null;

        /// <summary>Every board's name, in order.</summary>
        public string[] FreePlayNames()
        {
            var names = new string[_freePlay.Count];

            for (int i = 0; i < names.Length; i++)
                names[i] = _freePlay[i].name;

            return names;
        }

        /// <summary>Every name but one, for asking whether that one may be renamed to something.</summary>
        public string[] FreePlayNamesExcept(int index)
        {
            var names = new List<string>(_freePlay.Count);

            for (int i = 0; i < _freePlay.Count; i++)
            {
                if (i != index)
                    names.Add(_freePlay[i].name);
            }

            return names.ToArray();
        }

        /// <summary>Opens another board, and writes the file. False for an index with no board.</summary>
        /// <remarks>
        /// Only the choice: nothing is loaded here. The open board is saved under free play's key
        /// when free play is left, and that key means whichever board is open -- so the switch has
        /// to come after that save and before the next restore. <see cref="LevelSession.Adopt"/>
        /// runs it there.
        /// </remarks>
        public bool SelectFreePlay(int index)
        {
            if (index < 0 || index >= _freePlay.Count)
                return false;

            _activeFreePlay = index;
            Save();
            return true;
        }

        /// <summary>
        /// Adds a board under the given name and writes the file. Its index, or -1 if free play is
        /// full or the name cannot be used.
        /// </summary>
        /// <remarks>Does not open it; see <see cref="SelectFreePlay"/> for why that is separate.</remarks>
        public int AddFreePlay(string name, SavedBoard board)
        {
            if (board == null || _freePlay.Count >= BoardNames.MaxBoards ||
                BoardNames.Refusal(name, FreePlayNames()) != null)
            {
                return -1;
            }

            board.level = SandboxLevel.Key;
            _freePlay.Add(new FreePlaySave { name = BoardNames.Clean(name), board = board });
            Save();

            return _freePlay.Count - 1;
        }

        /// <summary>Renames a board and writes the file, or says why it cannot.</summary>
        public bool RenameFreePlay(int index, string name, out string refusal)
        {
            refusal = index >= 0 && index < _freePlay.Count
                ? BoardNames.Refusal(name, FreePlayNamesExcept(index))
                : "There is no such board.";

            if (refusal != null)
                return false;

            _freePlay[index].name = BoardNames.Clean(name);
            Save();
            return true;
        }

        /// <summary>
        /// Removes a board and writes the file; the last one is emptied instead, keeping its name.
        /// </summary>
        /// <remarks>
        /// Free play always has a board to open, so the last cannot go -- but a delete that did
        /// nothing would say the button is broken. Emptied, it is a fresh board under the name the
        /// player gave it.
        ///
        /// Removing the open board opens the one that took its place in the list, or the one before
        /// it at the end. Whoever called this loads that one.
        /// </remarks>
        public bool DeleteFreePlay(int index)
        {
            if (index < 0 || index >= _freePlay.Count)
                return false;

            if (_freePlay.Count == 1)
            {
                _freePlay[0].board = new SavedBoard
                {
                    level = SandboxLevel.Key,
                    sandbox = SandboxLevel.Default(SandboxLevel.Board),
                };
            }
            else
            {
                _freePlay.RemoveAt(index);

                if (_activeFreePlay > index || _activeFreePlay >= _freePlay.Count)
                    _activeFreePlay--;
            }

            Save();
            return true;
        }

        /// <summary>
        /// Reads free play's boards, bringing a file from before they had names up to date.
        /// </summary>
        /// <remarks>
        /// Up to 3.0.2 free play had one board, kept among the levels' boards under
        /// <c>"sandbox"</c>. It becomes the first named board and leaves that list, so the next save
        /// writes it in one place only. A file with named boards already is left as it is, and a
        /// stray board under the old key beside them is dropped: a file only ever has that from a
        /// hand edit, and the named ones are the player's.
        ///
        /// Every board is moved to free play's current layout here, not when it is opened, so a
        /// board switched to later in the session arrives on the right slots too.
        /// </remarks>
        private void LoadFreePlay(FreePlayFile file)
        {
            if (file?.saves != null)
            {
                foreach (FreePlaySave save in file.saves)
                {
                    if (save?.board == null || _freePlay.Count >= BoardNames.MaxBoards)
                        continue;

                    // A name the rules would refuse -- empty, too long, or a second copy of one
                    // already read -- is renamed rather than dropped: the board is what matters.
                    string name = BoardNames.Refusal(save.name, FreePlayNames()) == null
                        ? BoardNames.Clean(save.name)
                        : _freePlay.Count == 0 ? BoardNames.First : BoardNames.NextDefault(FreePlayNames());

                    save.board.level = SandboxLevel.Key;
                    _freePlay.Add(new FreePlaySave { name = name, board = save.board });
                }
            }

            if (_boards.TryGetValue(SandboxLevel.Key, out SavedBoard single))
            {
                _boards.Remove(SandboxLevel.Key);

                if (_freePlay.Count == 0)
                    _freePlay.Add(new FreePlaySave { name = BoardNames.First, board = single });
            }

            int active = file?.active ?? 0;
            _activeFreePlay = active >= 0 && active < _freePlay.Count ? active : 0;

            foreach (FreePlaySave save in _freePlay)
                SandboxLevel.Migrate(save.board);
        }
    }
}
