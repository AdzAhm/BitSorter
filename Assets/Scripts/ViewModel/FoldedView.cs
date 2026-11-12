using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Code as it shows with some of its blocks folded (5.1): each folded block its first line, a mark, and its last line's
    /// text (<c>module ha ( … endmodule</c>), and the way between what shows and the code itself -- where a character is,
    /// which line a shown line is, and what an edit of the shown text does to the code. The code is never changed by a fold.
    /// </summary>
    /// <remarks>
    /// **The mark is one thing, never letters to type into.** A deletion that takes any of it takes the whole block it
    /// hides, as Notepad++ deletes a folded block selected across; text typed inside it goes after the block. Either opens
    /// the fold. An edit anywhere else keeps every fold it does not touch, matched again in the code it makes by where its
    /// first line ends.
    /// </remarks>
    public sealed class FoldedView
    {
        private readonly List<(int Real, int Shown, int Length)> _shownParts = new List<(int, int, int)>();
        private readonly List<(int Shown, CodeFold Fold)> _marks = new List<(int, CodeFold)>();
        private readonly string _mark;
        private readonly HashSet<int> _folded;

        private FoldedView(string real, IReadOnlyList<CodeFold> folds, HashSet<int> folded, string mark)
        {
            Real = real ?? "";
            Folds = folds;
            _folded = folded;
            _mark = mark;

            var shown = new System.Text.StringBuilder(Real.Length);
            int at = 0;
            foreach (CodeFold fold in folds)
            {
                if (!folded.Contains(fold.FirstLine) || fold.From < at)
                    continue;   // open, or inside a block folded already
                _shownParts.Add((at, shown.Length, fold.From - at));
                shown.Append(Real, at, fold.From - at);
                _marks.Add((shown.Length, fold));
                shown.Append(mark);
                at = fold.To;
            }
            _shownParts.Add((at, shown.Length, Real.Length - at));
            shown.Append(Real, at, Real.Length - at);
            Shown = shown.ToString();
        }

        /// <summary>
        /// <paramref name="real"/> with the blocks whose first lines are in <paramref name="folded"/> folded, each shown with
        /// <paramref name="mark"/> in place of what it hides; a line that starts no block is passed over.
        /// </summary>
        public static FoldedView Of(string real, IEnumerable<int> folded, string mark = " … ")
        {
            IReadOnlyList<CodeFold> folds = CodeFolds.Of(real ?? "");
            var kept = new HashSet<int>();
            if (folded != null)
                foreach (int line in folded)
                    foreach (CodeFold fold in folds)
                        if (fold.FirstLine == line)
                            kept.Add(line);
            return new FoldedView(real, folds, kept, mark ?? " … ");
        }

        /// <summary>The code itself.</summary>
        public string Real { get; }

        /// <summary>The code as it shows, its folded blocks each one line.</summary>
        public string Shown { get; }

        /// <summary>Every block of the code that folds, open or not, by first line.</summary>
        public IReadOnlyList<CodeFold> Folds { get; }

        /// <summary>The first lines of the blocks folded: a block inside another folded one is kept folded, and shows so once that opens.</summary>
        public IReadOnlyCollection<int> Folded => _folded;

        /// <summary>The block that starts on line <paramref name="line"/>, from 0, if one does.</summary>
        public CodeFold? FoldAt(int line)
        {
            foreach (CodeFold fold in Folds)
                if (fold.FirstLine == line)
                    return fold;
            return null;
        }

        /// <summary>Whether the block on line <paramref name="line"/> is folded.</summary>
        public bool IsFolded(int line) => _folded.Contains(line);

        /// <summary>The same code with the block on line <paramref name="line"/> folded, or opened, or as it was where no block starts there.</summary>
        public FoldedView Toggled(int line)
        {
            if (FoldAt(line) == null)
                return this;
            var folded = new HashSet<int>(_folded);
            if (!folded.Remove(line))
                folded.Add(line);
            return new FoldedView(Real, Folds, folded, _mark);
        }

        /// <summary>The same code with every block folded, or every block opened.</summary>
        public FoldedView All(bool fold)
        {
            var folded = new HashSet<int>();
            if (fold)
                foreach (CodeFold each in Folds)
                    folded.Add(each.FirstLine);
            return new FoldedView(Real, Folds, folded, _mark);
        }

        /// <summary>The same code with every folded block that hides <paramref name="real"/> opened: where a caret is sent into one.</summary>
        public FoldedView Showing(int real)
        {
            var folded = new HashSet<int>(_folded);
            bool changed = false;
            foreach (CodeFold fold in Folds)
                if (folded.Contains(fold.FirstLine) && real > fold.From && real < fold.To)
                    changed |= folded.Remove(fold.FirstLine);
            return changed ? new FoldedView(Real, Folds, folded, _mark) : this;
        }

        /// <summary>The innermost block holding offset <paramref name="real"/> of the code, if any: what Ctrl+Shift+[ folds.</summary>
        public CodeFold? Around(int real)
        {
            CodeFold? best = null;
            int[] starts = CodeFolds.LineStarts(Real);
            int line = CodeFolds.LineOf(starts, Math.Max(0, Math.Min(real, Real.Length)));
            foreach (CodeFold fold in Folds)
                if (fold.FirstLine <= line && line <= fold.LastLine && (best == null || fold.FirstLine >= best.Value.FirstLine))
                    best = fold;
            return best;
        }

        /// <summary>Where offset <paramref name="real"/> of the code shows: in a folded block, at its mark.</summary>
        public int ShownOf(int real)
        {
            foreach ((int r, int s, int length) in _shownParts)
                if (real >= r && real <= r + length)
                    return s + (real - r);
            foreach ((int s, CodeFold fold) in _marks)
                if (real > fold.From && real < fold.To)
                    return s;
            return Shown.Length;
        }

        /// <summary>
        /// Where shown offset <paramref name="shown"/> is in the code: inside a mark, the start of the block it hides, or its
        /// end where <paramref name="end"/> -- the end of a range being replaced.
        /// </summary>
        public int RealOf(int shown, bool end = false)
        {
            foreach ((int r, int s, int length) in _shownParts)
                if (shown >= s && shown <= s + length)
                    return r + (shown - s);
            foreach ((int s, CodeFold fold) in _marks)
                if (shown > s && shown < s + _mark.Length)
                    return end ? fold.To : fold.From;
            return Real.Length;
        }

        /// <summary>For each shown line, from 0, the line of the code it starts: what its number in the margin is.</summary>
        public int[] RealLines()
        {
            int[] realStarts = CodeFolds.LineStarts(Real);
            int[] shownStarts = CodeFolds.LineStarts(Shown);
            var lines = new int[shownStarts.Length];
            for (int i = 0; i < shownStarts.Length; i++)
                lines[i] = CodeFolds.LineOf(realStarts, RealOf(shownStarts[i]));
            return lines;
        }

        /// <summary>
        /// What an edit of the shown text makes of the code (<paramref name="shownAfter"/>, the shown text after it): the code
        /// after it, where its caret is, and the code shown with every fold the edit left alone still folded.
        /// </summary>
        public (string Real, int Caret, FoldedView View) Edit(string shownAfter)
        {
            string before = Shown, after = shownAfter ?? "";
            int prefix = 0, most = Math.Min(before.Length, after.Length);
            while (prefix < most && before[prefix] == after[prefix])
                prefix++;
            int suffix = 0;
            while (suffix < most - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
                suffix++;

            int removedEnd = before.Length - suffix;
            string inserted = after.Substring(prefix, after.Length - suffix - prefix);
            bool insertion = removedEnd == prefix;

            // Text typed inside a mark goes after the block it hides; a deletion that takes any of a mark takes its block.
            int realStart = insertion && InsideMark(prefix, out CodeFold typedIn) ? typedIn.To : RealOf(prefix);
            int realEnd = insertion ? realStart : RealOf(removedEnd, end: true);
            if (realEnd < realStart)
                realEnd = realStart;

            string real = Real.Substring(0, realStart) + inserted + Real.Substring(realEnd);
            int delta = inserted.Length - (realEnd - realStart);

            // Every fold the edit left alone, matched again by where its first line ends.
            var keptEnds = new HashSet<int>();
            foreach (CodeFold fold in Folds)
            {
                if (!IsFolded(fold.FirstLine))
                    continue;
                bool touched = insertion ? realStart > fold.From && realStart < fold.To : realStart < fold.To && realEnd > fold.From;
                if (!touched)
                    keptEnds.Add(fold.From >= realEnd ? fold.From + delta : fold.From);
            }
            IReadOnlyList<CodeFold> folds = CodeFolds.Of(real);
            var folded = new HashSet<int>();
            foreach (CodeFold fold in folds)
                if (keptEnds.Contains(fold.From))
                    folded.Add(fold.FirstLine);
            return (real, realStart + inserted.Length, new FoldedView(real, folds, folded, _mark));
        }

        private bool InsideMark(int shown, out CodeFold fold)
        {
            foreach ((int s, CodeFold f) in _marks)
                if (shown > s && shown < s + _mark.Length)
                {
                    fold = f;
                    return true;
                }
            fold = default;
            return false;
        }
    }
}
