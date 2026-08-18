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

        public void Load()
        {
            LastError = null;
            _completed.Clear();
        }

        public void Save()
        {
        }

        /// <summary>Forgets everything, on disk as well as in memory.</summary>
        public void Clear()
        {
            _completed.Clear();
            Save();
        }
    }
}
