using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>
    /// One row of a <see cref="UiStack"/>: how far it sits from the stack's edge, and how tall it is.
    /// </summary>
    /// <remarks>
    /// Only a stack makes one, which is the point. A row that could be written down anywhere is a
    /// row whose clearance from its neighbours somebody works out by hand, and that is how every
    /// overlap this interface has shipped came about.
    /// </remarks>
    public readonly struct UiRow
    {
        /// <summary>What sits in this row, for messages.</summary>
        public readonly string Name;

        /// <summary>Distance from the stack's edge to the near side of the row.</summary>
        public readonly float Offset;

        /// <summary>How much of the stack the row takes.</summary>
        public readonly float Height;

        /// <summary>Distance from the stack's edge to the far side of the row.</summary>
        public float End => Offset + Height;

        internal UiRow(string name, float offset, float height)
        {
            Name = name;
            Offset = offset;
            Height = height;
        }

        public override string ToString() => $"{Name} {Offset}..{End}";
    }

    /// <summary>
    /// A column of rows measured from one edge of the screen, each placed clear of the one before
    /// it. Pure: it needs no canvas, and allocates only while it is being built.
    /// </summary>
    /// <remarks>
    /// Every row used to be a constant stated as the previous row plus its height plus a gap. That
    /// held as long as every author remembered to write it that way, and three times somebody did
    /// not -- the refusal toast over the controls line, the verdict over the first-time hint, the
    /// clock strip over the verdict -- and each time the fix was one more constant written the
    /// right way and one more test asserting it. A stack makes "clear of the row before" the only
    /// thing <see cref="Add(string, float)"/> can do, so no two rows of one stack can overlap by
    /// construction, and the tests shrink to one that checks nobody went round it.
    ///
    /// Not a LayoutGroup. Those rebuild on a canvas callback, which puts layout back into the
    /// order components happen to update in; this is worked out once, before anything is drawn.
    /// </remarks>
    public sealed class UiStack
    {
        private readonly List<UiRow> _rows = new List<UiRow>();
        private readonly float _start;

        /// <param name="name">Which stack this is, for messages.</param>
        /// <param name="start">Where the first row starts, measured from the edge.</param>
        /// <param name="gap">The space left between one row and the next unless a row says otherwise.</param>
        public UiStack(string name, float start, float gap)
        {
            Name = name;
            _start = start;
            Gap = gap;
        }

        /// <summary>Which stack this is.</summary>
        public string Name { get; }

        /// <summary>The space between rows unless a row asks for another.</summary>
        public float Gap { get; }

        /// <summary>Every row, nearest the edge first.</summary>
        public IReadOnlyList<UiRow> Rows => _rows;

        /// <summary>Where the next row would start: the last row's far side plus the gap.</summary>
        public float Next => _rows.Count == 0 ? _start : _rows[_rows.Count - 1].End + Gap;

        /// <summary>A row of this height, the usual gap clear of the one before.</summary>
        public UiRow Add(string name, float height) =>
            _rows.Count == 0 ? Place(name, _start, height) : Add(name, height, Gap);

        /// <summary>
        /// A row of this height, <paramref name="gapBefore"/> clear of the one before -- for a row
        /// that belongs to the one before it, like a caption hanging under a badge.
        /// </summary>
        public UiRow Add(string name, float height, float gapBefore) =>
            Place(name, _rows.Count == 0 ? _start : _rows[_rows.Count - 1].End + gapBefore, height);

        private UiRow Place(string name, float offset, float height)
        {
            // Either would put this row over the one before it, which is the one thing a stack is
            // here to make impossible.
            if (height < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(height), height, $"{Name}: '{name}' has a negative height");

            if (_rows.Count > 0 && offset < _rows[_rows.Count - 1].End)
                throw new System.ArgumentOutOfRangeException(nameof(offset), offset, $"{Name}: '{name}' starts inside the row before it");

            var row = new UiRow(name, offset, height);
            _rows.Add(row);
            return row;
        }
    }
}
