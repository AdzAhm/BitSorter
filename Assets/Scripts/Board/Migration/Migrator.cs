using System.Text;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// What is left of the 4.x board migration: the one rule that names a module.
    /// </summary>
    /// <remarks>
    /// This made a 4.x board into a 5.0 document (section 8 of <c>Docs/architecture-5.md</c>) -- gates, wires and
    /// blocks across, every output's settled value after each vector held to what the level expected. Part 16a deleted
    /// the game and its save keys, and Part 16b the migration with them (Ahmad, 2026-11-02: "idc about the old saves
    /// tbh this is like a new phase in the project"). <see cref="NameFrom"/> stays because every exercise's and every
    /// circuit's module is named by it, and the exercises keep the 4.x levels' ids.
    /// </remarks>
    public static class Migrator
    {
        /// <summary>A name SystemVerilog takes, made from whatever 4.x called something.</summary>
        public static string NameFrom(string text, string fallback)
        {
            var name = new StringBuilder();
            foreach (char c in text ?? string.Empty)
                name.Append(char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '_');

            string made = name.ToString().Trim('_');
            if (made.Length == 0)
                made = fallback;
            if (char.IsDigit(made[0]))
                made = fallback + "_" + made;
            if (Keywords.IsKeyword(made))
                made += "_";
            return made;
        }
    }
}
