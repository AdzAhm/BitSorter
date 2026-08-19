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
    /// </remarks>
    public sealed class ProgressStore
    {
        private readonly string _path;
        private readonly HashSet<string> _completed = new HashSet<string>(StringComparer.Ordinal);

        private readonly Dictionary<string, SavedBoard> _boards =
            new Dictionary<string, SavedBoard>(StringComparer.Ordinal);

        public ProgressStore(string path)
        {
            _path = path;
        }

        /// <summary>Where the real game keeps its progress.</summary>
        public static string DefaultPath =>
            Path.Combine(Application.persistentDataPath, "progress.json");

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

            try
            {
                if (!File.Exists(_path))
                    return;   // a first run is not a failure

                string json = File.ReadAllText(_path);

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
            }
            catch (Exception exception)
            {
                // Recorded rather than thrown. Someone debugging a lost save can find out why; a
                // player mid-session never has to.
                LastError = exception.Message;
                _completed.Clear();
            }
        }

        /// <summary>Writes the file, and says nothing if it cannot.</summary>
        /// <inheritdoc cref="Load"/>
        public void Save()
        {
            try
            {
                var file = new ProgressFile
                {
                    completed = new string[_completed.Count],
                    boards = new SavedBoard[_boards.Count],
                };

                _completed.CopyTo(file.completed);
                _boards.Values.CopyTo(file.boards, 0);

                string directory = Path.GetDirectoryName(_path);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_path, JsonUtility.ToJson(file, true));
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
            Save();
        }

        // -----------------------------------------------------------------
        // Boards and personal bests
        // -----------------------------------------------------------------

        /// <summary>What was left on a level's board, or null if it has never been touched.</summary>
        public SavedBoard BoardFor(string level) =>
            !string.IsNullOrEmpty(level) && _boards.TryGetValue(level, out SavedBoard board)
                ? board
                : null;

        /// <summary>Remembers what is on a level's board, keeping any record already set.</summary>
        public void SaveBoard(string level, SavedBoard board)
        {
            if (string.IsNullOrEmpty(level) || board == null)
                return;

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
            _boards[level] = board;

            Save();
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
                _boards[level] = board;
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
    }
}
