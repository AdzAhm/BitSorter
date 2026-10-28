using System.Collections.Generic;
using BitSorter.Board;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A 4.x save's boards made into 5.0 documents (Docs/architecture-5.md, section 8): each level board
    /// on its level, each free-play board on its own setup, kept in the save under the board's key.
    /// </summary>
    /// <remarks>
    /// Only a board with no document yet is migrated, so running it twice changes nothing and a document
    /// edited in 5.0 is never replaced by its 4.x board. The 4.x keys are left as they are: the 4.x game
    /// reads them until Part 16 retires it. Each board is rebuilt exactly as the game would rebuild it
    /// (<see cref="BoardSerializer.Restore"/>), on its level's board, then migrated through
    /// <see cref="LegacyCircuits"/>.
    /// </remarks>
    public static class SaveMigration
    {
        /// <summary>The start of a free-play board's key; its name follows.</summary>
        public const string FreePlayKey = "free-play/";

        /// <summary>The board a level is drawn on when it names none, as the scene's is.</summary>
        public static readonly Vector2Int DefaultBoard = new Vector2Int(4, 2);

        /// <summary>
        /// Migrates every board in <paramref name="store"/> that has no document, and keeps each. Says,
        /// in words, what did not come across. Does not write the file.
        /// </summary>
        public static List<string> MigrateAll(ProgressStore store)
        {
            var notes = new List<string>();

            foreach (string key in store.BoardLevels())
            {
                if (key == TutorialLevel.Key || store.HasProject(key))
                    continue;

                LevelLoadResult loaded = LevelLoader.Load(key, DefaultBoard);
                if (!loaded.IsValid)
                {
                    notes.Add($"The board for {key} was left as it was: the level did not load ({loaded.Error}).");
                    continue;
                }

                LevelDefinition level = loaded.Level;
                Migrate(store, key, key, level, store.BoardFor(key), level.HasBoard ? level.BoardHalfExtents : DefaultBoard, notes);
            }

            string[] names = store.FreePlayNames();
            for (int i = 0; i < names.Length; i++)
            {
                string key = FreePlayKey + names[i];
                if (store.HasProject(key))
                    continue;

                SavedBoard board = store.FreePlayBoard(i);
                LevelDefinition level = SandboxLevel.Build(board?.sandbox, SandboxLevel.Board);
                Migrate(store, key, names[i], level, board, SandboxLevel.Board, notes);
            }

            return notes;
        }

        /// <summary>
        /// Migrates the one 4.x board kept for the level <paramref name="key"/>, when it has one and no document yet: what an
        /// exercise of the level's id opens (Part 15d). One at a time and only when asked, so a board is never migrated twice
        /// and nothing the student has since deleted comes back. True when it made a document. Does not write the file.
        /// </summary>
        public static bool MigrateOne(ProgressStore store, string key, List<string> notes)
        {
            if (store == null || string.IsNullOrEmpty(key) || key == TutorialLevel.Key || store.HasProject(key) || store.BoardFor(key) == null)
                return false;

            LevelLoadResult loaded = LevelLoader.Load(key, DefaultBoard);
            if (!loaded.IsValid)
            {
                notes.Add($"The board for {key} was left as it was: the level did not load ({loaded.Error}).");
                return false;
            }

            LevelDefinition level = loaded.Level;
            Migrate(store, key, key, level, store.BoardFor(key), level.HasBoard ? level.BoardHalfExtents : DefaultBoard, notes);
            return store.HasProject(key);
        }

        private static void Migrate(
            ProgressStore store, string key, string name, LevelDefinition level, SavedBoard saved, Vector2Int extents, List<string> notes)
        {
            var blueprint = new CircuitBlueprint();
            int dropped = BoardSerializer.Restore(saved, level, blueprint, extents);
            if (dropped > 0)
                notes.Add($"{key}: {dropped} saved item(s) no longer fit the level and were left out, as 4.x would have.");

            MigrationResult migrated = LegacyCircuits.Migrate(name, level, blueprint);
            foreach (string note in migrated.Notes)
                notes.Add($"{key}: {note}");

            store.SetProject(key, migrated.Project);
        }
    }
}
