using System;
using BitSorter.Board;
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
        /// <summary>The ids of the exercises solved.</summary>
        public string[] completed;

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

        /// <summary>The save's format: <see cref="ProjectRecord.Format"/> from 5.0 on, and absent -- 0 -- in every 4.x file.</summary>
        public int format;

        /// <summary>5.0's documents, each under its key: a circuit's name, or an exercise's id.</summary>
        /// <remarks>
        /// The 4.x game's boards, free-play boards and blocks were kept beside these until Part 16a deleted the game; a file
        /// that still has them is read without them, and written without them.
        /// </remarks>
        public SavedProject[] projects;

        /// <summary>
        /// The keys of the documents last opened on the workbench, the latest first (Part 15a): what OPEN lists first and
        /// the start page's RECENT. A new key, absent -- null -- in every file from before it.
        /// </summary>
        public string[] recent;

        /// <summary>
        /// The keys of the documents locked against change (Part 15h-4d; an imported project opens locked). A new key,
        /// absent -- null, nothing locked -- in every file from before it.
        /// </summary>
        public string[] locked;

        /// <summary>
        /// Each imported circuit as it came in, under its key (Part 15h-4d): what FILE, BACK TO AS IMPORTED puts back. A new
        /// key, absent -- nothing to go back to -- in every file from before it.
        /// </summary>
        public SavedProject[] imported;

        /// <summary>
        /// What was open last on the workbench (Part 15a), which BitSorter opens on again: a circuit's key, a sample's
        /// (<c>sample/</c> and its name), or empty for the start page. Absent, so the start page, in every file from before it.
        /// </summary>
        public string lastOpen;

        /// <summary>
        /// Each exercise's best (Part 15d): the fewest gates and the quickest settling it has been solved with. New: 4.x's
        /// bests were in ticks of a token game, and mean nothing here. Absent, so none, in every older file.
        /// </summary>
        public ExerciseBestRecord[] exerciseBests;

        /// <summary>
        /// The program last chosen on each sample (Ahmad, 2026-11-02): a sample is made afresh as it opens, so its PROGRAM is
        /// kept here and put back as it opens again. A new key, absent -- each sample on its own program -- in every file
        /// from before it.
        /// </summary>
        public SampleProgramRecord[] samplePrograms;
    }

    /// <summary>A program chosen on a sample, as the save holds it: the sample's name, the parameter, the member chosen.</summary>
    [Serializable]
    public sealed class SampleProgramRecord
    {
        public string sample;
        public string parameter;
        public string member;
    }

    /// <summary>
    /// An exercise's best, as the save holds it (Part 15d): its id, the fewest gates, the quickest settle in its unit, -1
    /// for none -- an exercise of registers alone has no path from an input to an output to settle.
    /// </summary>
    [Serializable]
    public sealed class ExerciseBestRecord
    {
        public string id;
        public int gates;
        public long settle = -1;
    }

    /// <summary>One document in the save, under the key of the board it belongs to.</summary>
    [Serializable]
    public sealed class SavedProject
    {
        public string key;
        public ProjectRecord project;
    }

    /// <summary>
    /// What is kept between sessions: the documents, which exercises have been solved, the first-time hints seen, and what
    /// was open last.
    /// </summary>
    /// <remarks>
    /// A plain class rather than a MonoBehaviour, taking its path as a constructor argument, so the
    /// whole thing can be tested against a scratch file without a scene and without touching the
    /// player's real save.
    ///
    /// Nothing here throws. A save file is the one piece of state the game cannot recreate, but it is
    /// also the one most likely to be truncated by a crash or edited by hand -- and losing a session
    /// to a stack trace on startup is a far worse failure than losing the record of which exercises were
    /// solved. Anything unreadable is treated as "nothing completed yet".
    ///
    /// Treated as, not thrown away. The next save would replace an unreadable file, so a copy is
    /// kept beside it first, as <c>progress.json.unreadable</c>, for anyone who wants to recover it by
    /// hand. And a save is never written over the file it replaces: see <see cref="Save"/>.
    /// </remarks>
    public sealed class ProgressStore
    {
        private readonly string _path;
        private readonly HashSet<string> _completed = new HashSet<string>(StringComparer.Ordinal);

        private readonly HashSet<string> _hintsSeen = new HashSet<string>(StringComparer.Ordinal);

        private readonly HashSet<string> _milestones = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>5.0's documents, by their keys, in the order they were put in.</summary>
        private readonly List<SavedProject> _projects = new List<SavedProject>();
        private readonly List<string> _recent = new List<string>();
        private readonly List<string> _locked = new List<string>();
        private readonly List<SavedProject> _imported = new List<SavedProject>();
        private string _lastOpen = "";
        private readonly List<SampleProgramRecord> _samplePrograms = new List<SampleProgramRecord>();
        private readonly Dictionary<string, (int Gates, long? Settle)> _exerciseBests = new Dictionary<string, (int Gates, long? Settle)>(StringComparer.Ordinal);

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
            _hintsSeen.Clear();
            _milestones.Clear();
            _projects.Clear();
            _recent.Clear();
            _locked.Clear();
            _imported.Clear();
            _lastOpen = "";
            _samplePrograms.Clear();
            _exerciseBests.Clear();
            LoadedFormat = 0;

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
                // format was shaped around. Each array is guarded on its own.
                if (file.completed != null)
                {
                    foreach (string name in file.completed)
                    {
                        if (!string.IsNullOrEmpty(name))
                            _completed.Add(name);
                    }
                }

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

                LoadedFormat = file.format;

                // Absent in every 4.x file, so null; guarded like every key before it.
                if (file.projects != null)
                {
                    foreach (SavedProject saved in file.projects)
                    {
                        if (saved != null && !string.IsNullOrEmpty(saved.key) && saved.project != null)
                            Put(saved.key, saved.project);
                    }
                }

                if (file.recent != null)
                    foreach (string key in file.recent)
                        if (!string.IsNullOrEmpty(key) && !_recent.Contains(key))
                            _recent.Add(key);
                if (file.locked != null)
                    foreach (string key in file.locked)
                        if (!string.IsNullOrEmpty(key) && !_locked.Contains(key))
                            _locked.Add(key);
                if (file.imported != null)
                    foreach (SavedProject saved in file.imported)
                        if (saved != null && !string.IsNullOrEmpty(saved.key) && saved.project != null && IndexOfImported(saved.key) < 0)
                            _imported.Add(saved);
                _lastOpen = file.lastOpen ?? "";
                if (file.exerciseBests != null)
                    foreach (ExerciseBestRecord best in file.exerciseBests)
                        if (best != null && !string.IsNullOrEmpty(best.id))
                            _exerciseBests[best.id] = (best.gates, best.settle >= 0 ? best.settle : (long?)null);
                if (file.samplePrograms != null)
                    foreach (SampleProgramRecord chosen in file.samplePrograms)
                        if (chosen != null)
                            SetSampleProgram(chosen.sample, chosen.parameter, chosen.member);
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
                    hintsSeen = new string[_hintsSeen.Count],
                    milestones = new string[_milestones.Count],
                    format = ProjectRecord.Format,
                    projects = _projects.ToArray(),
                    recent = _recent.ToArray(),
                    locked = _locked.ToArray(),
                    imported = _imported.ToArray(),
                    lastOpen = _lastOpen,
                    exerciseBests = ExerciseBestRecords(),
                    samplePrograms = _samplePrograms.ToArray(),
                };

                _completed.CopyTo(file.completed);
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
            _hintsSeen.Clear();
            _milestones.Clear();
            _projects.Clear();
            _recent.Clear();
            _locked.Clear();
            _imported.Clear();
            _lastOpen = "";
            _samplePrograms.Clear();
            _exerciseBests.Clear();
            Save();
        }

        /// <summary>
        /// Forgets the exercises and nothing the student made outside them (Part 15g, RESET PROGRESS): every solved mark,
        /// every answer kept under one of <paramref name="exercises"/>' ids; the bests; the tutorial and every first-time
        /// hint, which are milestones and hints seen; and an exercise in the recent list or as the last thing open. MY CIRCUIT
        /// and every named circuit are kept.
        /// </summary>
        public void ForgetTheExercises(ICollection<string> exercises)
        {
            _completed.Clear();
            _hintsSeen.Clear();
            _milestones.Clear();
            _exerciseBests.Clear();
            _projects.RemoveAll(p => p != null && exercises.Contains(p.key));
            _recent.RemoveAll(IsAnExercise);
            if (IsAnExercise(_lastOpen))
                _lastOpen = "";
            Save();

            bool IsAnExercise(string key) => key != null && (key.StartsWith(ExerciseKeyPrefix, StringComparison.Ordinal) || exercises.Contains(key));
        }

        /// <summary>What an exercise is kept as in the recent list and as the last thing open: the view-model's <c>Documents.ExercisePrefix</c>.</summary>
        private const string ExerciseKeyPrefix = BitSorter.ViewModel.Documents.ExercisePrefix;

        /// <summary>The format of the file last read: 0 for a 4.x save, <see cref="ProjectRecord.Format"/> for one 5.0 wrote.</summary>
        public int LoadedFormat { get; private set; }

        /// <summary>The keys of every document held, in the order they were put in.</summary>
        public string[] ProjectKeys()
        {
            var keys = new string[_projects.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = _projects[i].key;
            return keys;
        }

        public bool HasProject(string key) => IndexOfProject(key) >= 0;

        /// <summary>
        /// The document under <paramref name="key"/>, if there is one; anything in it that could not be
        /// read goes into <paramref name="problems"/> rather than failing the read.
        /// </summary>
        public bool TryGetProject(string key, out Project project, List<string> problems)
        {
            int index = IndexOfProject(key);
            project = index < 0 ? null : _projects[index].project.ToProject(problems ?? new List<string>());
            return project != null;
        }

        /// <summary>Keeps <paramref name="project"/> under <paramref name="key"/>, in place of any there was. Not written until <see cref="Save"/>.</summary>
        public void SetProject(string key, Project project)
        {
            if (string.IsNullOrEmpty(key) || project == null)
                return;
            Put(key, ProjectRecord.From(project));
        }

        /// <summary>The keys of the documents last opened, the latest first (Part 15a).</summary>
        public IReadOnlyList<string> Recent => _recent;

        /// <summary>An exercise's best so far (Part 15d), its settle null where there is none to reckon; false for none yet.</summary>
        public bool TryGetExerciseBest(string id, out int gates, out long? settle)
        {
            gates = 0;
            settle = null;
            if (string.IsNullOrEmpty(id) || !_exerciseBests.TryGetValue(id, out (int Gates, long? Settle) best))
                return false;
            gates = best.Gates;
            settle = best.Settle;
            return true;
        }

        /// <summary>
        /// Keeps <paramref name="gates"/> and <paramref name="settle"/> as <paramref name="id"/>'s best where they beat it, each
        /// on its own -- a settle where there was none beats none -- and says which did. Not written until <see cref="Save"/>.
        /// </summary>
        public (bool FewerGates, bool Quicker) RecordExerciseBest(string id, int gates, long? settle)
        {
            if (string.IsNullOrEmpty(id))
                return (false, false);
            if (!_exerciseBests.TryGetValue(id, out (int Gates, long? Settle) best))
            {
                _exerciseBests[id] = (gates, settle);
                return (true, settle.HasValue);
            }
            bool fewer = gates < best.Gates;
            bool quicker = settle.HasValue && (!best.Settle.HasValue || settle.Value < best.Settle.Value);
            _exerciseBests[id] = (Math.Min(gates, best.Gates), quicker ? settle : best.Settle);
            return (fewer, quicker);
        }

        private ExerciseBestRecord[] ExerciseBestRecords()
        {
            var records = new List<ExerciseBestRecord>(_exerciseBests.Count);
            foreach (KeyValuePair<string, (int Gates, long? Settle)> best in _exerciseBests)
                records.Add(new ExerciseBestRecord { id = best.Key, gates = best.Value.Gates, settle = best.Value.Settle ?? -1 });
            return records.ToArray();
        }

        /// <summary>What was open last on the workbench (Part 15a): a circuit's key, a sample's, or empty for the start page. Not written until <see cref="Save"/>.</summary>
        public string LastOpen
        {
            get => _lastOpen;
            set => _lastOpen = value ?? "";
        }

        /// <summary>Puts <paramref name="key"/> at the front of <see cref="Recent"/>, which stays short. Not written until <see cref="Save"/>.</summary>
        public void Touch(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            IReadOnlyList<string> touched = BitSorter.ViewModel.Documents.Touched(_recent, key);
            _recent.Clear();
            _recent.AddRange(touched);
        }

        /// <summary>Takes the document under <paramref name="key"/> out of the save, and out of <see cref="Recent"/>. Not written until <see cref="Save"/>.</summary>
        public void RemoveProject(string key)
        {
            int index = IndexOfProject(key);
            if (index >= 0)
                _projects.RemoveAt(index);
            _recent.Remove(key);
            _locked.Remove(key);
            int imported = IndexOfImported(key);
            if (imported >= 0)
                _imported.RemoveAt(imported);
        }

        /// <summary>Keeps <paramref name="project"/> as the circuit under <paramref name="key"/> came in (Part 15h-4d). Not written until <see cref="Save"/>.</summary>
        public void SetImported(string key, Project project)
        {
            if (string.IsNullOrEmpty(key) || project == null)
                return;
            int index = IndexOfImported(key);
            var saved = new SavedProject { key = key, project = ProjectRecord.From(project) };
            if (index >= 0)
                _imported[index] = saved;
            else
                _imported.Add(saved);
        }

        /// <summary>Whether the circuit under <paramref name="key"/> was imported, and so can go back to how it came in.</summary>
        public bool HasImported(string key) => IndexOfImported(key) >= 0;

        /// <summary>The circuit under <paramref name="key"/> as it came in, if it was imported; what could not be read goes into <paramref name="problems"/>.</summary>
        public bool TryGetImported(string key, out Project project, List<string> problems)
        {
            int index = IndexOfImported(key);
            project = index < 0 ? null : _imported[index].project.ToProject(problems ?? new List<string>());
            return project != null;
        }

        private int IndexOfImported(string key)
        {
            for (int i = 0; i < _imported.Count; i++)
                if (string.Equals(_imported[i].key, key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>The programs last chosen on <paramref name="sample"/>, by parameter; empty for none.</summary>
        public IReadOnlyDictionary<string, string> SamplePrograms(string sample)
        {
            var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SampleProgramRecord record in _samplePrograms)
                if (string.Equals(record.sample, sample, StringComparison.Ordinal))
                    chosen[record.parameter] = record.member;
            return chosen;
        }

        /// <summary>
        /// Keeps <paramref name="member"/> as the program last chosen for <paramref name="parameter"/> on <paramref name="sample"/>;
        /// whether that changed anything. Not written until <see cref="Save"/>.
        /// </summary>
        public bool SetSampleProgram(string sample, string parameter, string member)
        {
            if (string.IsNullOrEmpty(sample) || string.IsNullOrEmpty(parameter) || string.IsNullOrEmpty(member))
                return false;
            foreach (SampleProgramRecord record in _samplePrograms)
            {
                if (!string.Equals(record.sample, sample, StringComparison.Ordinal) || !string.Equals(record.parameter, parameter, StringComparison.Ordinal))
                    continue;
                if (string.Equals(record.member, member, StringComparison.Ordinal))
                    return false;
                record.member = member;
                return true;
            }

            _samplePrograms.Add(new SampleProgramRecord { sample = sample, parameter = parameter, member = member });
            return true;
        }

        /// <summary>Whether the document under <paramref name="key"/> is locked against change (Part 15h-4d).</summary>
        public bool IsLocked(string key) => !string.IsNullOrEmpty(key) && _locked.Contains(key);

        /// <summary>Locks or unlocks the document under <paramref name="key"/>. Not written until <see cref="Save"/>.</summary>
        public void SetLocked(string key, bool locked)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (!locked)
                _locked.Remove(key);
            else if (!_locked.Contains(key))
                _locked.Add(key);
        }

        /// <summary>
        /// Keeps the document under <paramref name="from"/> under <paramref name="to"/> instead, in its place in the save and
        /// in <see cref="Recent"/>; false when there is none, or <paramref name="to"/> is taken. Not written until <see cref="Save"/>.
        /// </summary>
        public bool RenameProject(string from, string to)
        {
            int index = IndexOfProject(from);
            if (index < 0 || string.IsNullOrEmpty(to) || (to != from && IndexOfProject(to) >= 0))
                return false;
            _projects[index] = new SavedProject { key = to, project = _projects[index].project };
            int recent = _recent.IndexOf(from);
            if (recent >= 0)
                _recent[recent] = to;
            int locked = _locked.IndexOf(from);
            if (locked >= 0)
                _locked[locked] = to;
            int imported = IndexOfImported(from);
            if (imported >= 0)
                _imported[imported] = new SavedProject { key = to, project = _imported[imported].project };
            if (_lastOpen == from)
                _lastOpen = to;
            return true;
        }

        private void Put(string key, ProjectRecord file)
        {
            int index = IndexOfProject(key);
            var saved = new SavedProject { key = key, project = file };
            if (index >= 0)
                _projects[index] = saved;
            else
                _projects.Add(saved);
        }

        private int IndexOfProject(string key)
        {
            for (int i = 0; i < _projects.Count; i++)
                if (string.Equals(_projects[i].key, key, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
