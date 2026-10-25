using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>How a row names its signal.</summary>
    public enum WaveNames
    {
        /// <summary>Its key, below the design: <c>h1.s</c>, <c>s[3:0]</c>.</summary>
        Short,

        /// <summary>Its whole path as ModelSim writes it: <c>/full_adder_tb/dut/h1/s</c>.</summary>
        Full,
    }

    /// <summary>One signal in the list: kept by its key, so it is found again when the design is built again.</summary>
    public sealed class WaveSignal
    {
        internal WaveSignal(string key) => Key = key;

        /// <summary>Where it is in the design (<see cref="SignalIndex.KeyOf"/>); a net renamed takes its row with it (<see cref="WaveList.Rekey"/>).</summary>
        public string Key { get; internal set; }

        /// <summary>The name it was given in place of its own; null for none.</summary>
        public string Rename { get; internal set; }

        /// <summary>The radix it is written in; null for its signal's default.</summary>
        public WaveRadix? Radix { get; internal set; }

        /// <summary>Its signal in the design as last resolved; -1 when the design has none by its key.</summary>
        public int Signal { get; internal set; } = -1;

        public override string ToString() => Key;
    }

    /// <summary>A named run of signals the list shows under one header, open or shut.</summary>
    public sealed class WaveGroup
    {
        internal WaveGroup(string name) => Name = name;

        public string Name { get; internal set; }

        /// <summary>Whether its signals are shown under its header.</summary>
        public bool Expanded { get; internal set; } = true;

        public IReadOnlyList<WaveSignal> Signals => _signals;

        internal readonly List<WaveSignal> _signals = new List<WaveSignal>();

        public override string ToString() => Name;
    }

    /// <summary>A line of the list as it is shown: a group's header, or a signal, inside a group or not.</summary>
    public readonly struct WaveLine
    {
        internal WaveLine(WaveSignal signal, WaveGroup group, bool selected)
        {
            Signal = signal;
            Group = group;
            Selected = selected;
        }

        /// <summary>The signal it shows; null for a group's header.</summary>
        public WaveSignal Signal { get; }

        /// <summary>The group it is the header of, or the signal is in; null for a signal in no group.</summary>
        public WaveGroup Group { get; }

        public bool IsHeader => Signal == null;

        public bool Selected { get; }
    }

    /// <summary>
    /// The waveform's rows as the student arranges them: which signals, in what order, which are
    /// grouped under a header, what each is called and in which radix, and which are selected.
    /// </summary>
    /// <remarks>
    /// **A row is kept by its key, never by its signal's index** (<see cref="SignalIndex"/>): the
    /// design is built again on every edit and its indices with it, and a key is where the signal is
    /// below the design, which an edit elsewhere leaves alone. <see cref="Resolve"/> finds each key in
    /// the new design; one it no longer has keeps its place, showing nothing, until it is removed or
    /// the design has it again.
    ///
    /// **Selection is of rows, not of places in the list**, so moving, grouping or opening a group
    /// keeps what was selected. A selected group's header stands for all of it: its signals are what a
    /// radix is set on and what removing takes.
    ///
    /// Groups do not nest. Grouping takes the selected signals, from wherever they are, into a new group
    /// where the first of them was; ungrouping puts a group's signals back where its header was.
    /// </remarks>
    public sealed class WaveList
    {
        // The list's top level: each a WaveSignal or a WaveGroup.
        private readonly List<object> _items = new List<object>();
        private readonly HashSet<object> _selected = new HashSet<object>();
        private readonly List<WaveLine> _lines = new List<WaveLine>();
        private object _anchor;
        private bool _dirty = true;

        /// <summary>A list of the signals named by <paramref name="keys"/>, in their order, resolved in <paramref name="index"/>.</summary>
        public WaveList(SignalIndex index, IEnumerable<string> keys)
        {
            Index = index ?? throw new ArgumentNullException(nameof(index));
            if (keys != null)
                foreach (string key in keys)
                    if (key != null && Find(key) == null)
                        _items.Add(new WaveSignal(key));
            Resolve(index);
        }

        /// <summary>What it last resolved against.</summary>
        public SignalIndex Index { get; private set; }

        /// <summary>How the rows name their signals.</summary>
        public WaveNames Names
        {
            get => _names;
            set
            {
                if (_names == value) return;
                _names = value;
                Changed();
            }
        }

        private WaveNames _names = WaveNames.Short;

        /// <summary>Counts every change to the list: what tells the view to lay its rows out again.</summary>
        public int Version { get; private set; }

        /// <summary>The lines as shown, top to bottom: headers, and every signal not in a shut group.</summary>
        public IReadOnlyList<WaveLine> Lines
        {
            get
            {
                if (_dirty)
                    Flatten();
                return _lines;
            }
        }

        /// <summary>Every signal in the list, in order, shown or not.</summary>
        public IEnumerable<WaveSignal> AllSignals
        {
            get
            {
                foreach (object item in _items)
                {
                    if (item is WaveSignal signal)
                        yield return signal;
                    else
                        foreach (WaveSignal inner in ((WaveGroup)item)._signals)
                            yield return inner;
                }
            }
        }

        /// <summary>The signals selected, a selected group's all among them, in order.</summary>
        public List<WaveSignal> SelectedSignals()
        {
            var selected = new List<WaveSignal>();
            foreach (object item in _items)
            {
                if (item is WaveSignal signal)
                {
                    if (_selected.Contains(signal))
                        selected.Add(signal);
                    continue;
                }

                var group = (WaveGroup)item;
                foreach (WaveSignal inner in group._signals)
                    if (_selected.Contains(group) || _selected.Contains(inner))
                        selected.Add(inner);
            }

            return selected;
        }

        /// <summary>Whether anything is selected.</summary>
        public bool AnySelected => _selected.Count > 0;

        /// <summary>What a line is called, as <see cref="Names"/> says: a header its group's name, a signal its new name or its own.</summary>
        public string LabelOf(WaveLine line)
        {
            if (line.IsHeader)
                return line.Group.Name;
            WaveSignal signal = line.Signal;
            if (signal.Rename != null)
                return signal.Rename;
            if (signal.Signal < 0)
                return signal.Key;
            return _names == WaveNames.Full ? Index.FullName(signal.Signal) : Index.ShortName(signal.Signal);
        }

        // -----------------------------------------------------------------
        // Changing the list
        // -----------------------------------------------------------------

        /// <summary>
        /// Adds the signal at <paramref name="signal"/> of the design at the end, selected alone, and
        /// gives its line; a signal already in the list is selected where it is instead, and its group
        /// opened. -1 for a signal the index does not list.
        /// </summary>
        public int Add(int signal)
        {
            string key = Index.KeyOf(signal);
            if (key == null || !Index.IsListed(signal, includeCells: true))
                return -1;

            WaveSignal row = Find(key);
            if (row == null)
            {
                row = new WaveSignal(key) { Signal = signal };
                _items.Add(row);
            }
            else
            {
                WaveGroup group = GroupOf(row);
                if (group != null)
                    group.Expanded = true;
            }

            _selected.Clear();
            _selected.Add(row);
            _anchor = row;
            Changed();
            return LineOf(row);
        }

        /// <summary>
        /// The rows following the document's ports through an edit (Part 10a): a port placed gets a row, unselected, and
        /// a port taken away loses its row, in a group or not. <paramref name="was"/> and <paramref name="now"/> are the
        /// ports' keys before and after, <paramref name="now"/> in the layout's order. True when the list changed.
        /// </summary>
        /// <remarks>
        /// **The ports' rows keep the layout's order** (Part 10e), in the places ports' rows hold among the rest -- a new
        /// port's row taking a new place at the end -- so they read as they will when the circuit is opened again, which
        /// makes its rows from the layout alone. Rows the student added stay where they are, and so does a port's row
        /// in a group. A port placed used to keep the place at the end, and a half adder built a, b, then y and z read
        /// a, b, y, z, and a, y, b, z after a reload (the Part 10 browser smoke test).
        /// </remarks>
        public bool FollowPorts(IReadOnlyCollection<string> was, IReadOnlyCollection<string> now)
        {
            var before = new HashSet<string>(was ?? Array.Empty<string>(), StringComparer.Ordinal);
            var after = new HashSet<string>(now ?? Array.Empty<string>(), StringComparer.Ordinal);
            bool changed = false;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i] is WaveSignal signal && before.Contains(signal.Key) && !after.Contains(signal.Key))
                {
                    _selected.Remove(signal);
                    _items.RemoveAt(i);
                    changed = true;
                }
                else if (_items[i] is WaveGroup group)
                {
                    changed |= group._signals.RemoveAll(s => before.Contains(s.Key) && !after.Contains(s.Key)) > 0;
                    if (group._signals.Count == 0)
                        _items.RemoveAt(i);
                }
            }

            foreach (string key in now ?? Array.Empty<string>())
            {
                if (before.Contains(key) || Find(key) != null)
                    continue;
                _items.Add(new WaveSignal(key) { Signal = Index.TryFind(key, out int found) ? found : -1 });
                changed = true;
            }

            // The ports' rows put in the layout's order, in the places they hold.
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string key in now ?? Array.Empty<string>())
                if (!order.ContainsKey(key))
                    order[key] = order.Count;
            var places = new List<int>();
            var rows = new List<WaveSignal>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] is WaveSignal row && order.ContainsKey(row.Key))
                {
                    places.Add(i);
                    rows.Add(row);
                }
            }

            rows.Sort((p, q) => order[p.Key].CompareTo(order[q.Key]));
            for (int i = 0; i < places.Count; i++)
            {
                if (!ReferenceEquals(_items[places[i]], rows[i]))
                {
                    _items[places[i]] = rows[i];
                    changed = true;
                }
            }

            if (changed)
                Changed();
            return changed;
        }

        /// <summary>
        /// The rows of a signal renamed (Part 10c) -- a net or a port given a new name in the inspector -- kept under its new
        /// key, with their place, their own names and their radixes, so the run made with the new name finds them. True
        /// when any row was the old key's.
        /// </summary>
        public bool Rekey(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
                return false;
            bool changed = false;
            foreach (WaveSignal signal in AllSignals)
            {
                if (signal.Key != from)
                    continue;
                signal.Key = to;
                changed = true;
            }

            if (changed)
                Changed();
            return changed;
        }

        /// <summary>Takes every selected row out: a selected group with all its signals. True when anything went.</summary>
        public bool RemoveSelected()
        {
            if (_selected.Count == 0)
                return false;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_selected.Contains(_items[i]))
                {
                    _items.RemoveAt(i);
                    continue;
                }

                if (_items[i] is WaveGroup group)
                {
                    group._signals.RemoveAll(s => _selected.Contains(s));
                    if (group._signals.Count == 0)
                        _items.RemoveAt(i);
                }
            }

            _selected.Clear();
            _anchor = null;
            Changed();
            return true;
        }

        /// <summary>
        /// The signals at <paramref name="signals"/> of the design put in one new group called <paramref name="name"/>
        /// (Part 11c, a click on a scope of the hierarchy): those not in the list yet added at its end, those already in it
        /// taken out of where they were. The group is where the first of them was, open and selected; null when none of
        /// them is one the index lists.
        /// </summary>
        public WaveGroup AddGroup(string name, IEnumerable<int> signals)
        {
            var rows = new List<WaveSignal>();
            foreach (int signal in signals)
            {
                string key = Index.KeyOf(signal);
                if (key == null || !Index.IsListed(signal, includeCells: true))
                    continue;
                WaveSignal row = Find(key);
                if (row == null)
                {
                    row = new WaveSignal(key) { Signal = signal };
                    _items.Add(row);
                }

                if (!rows.Contains(row))
                    rows.Add(row);
            }

            if (rows.Count == 0)
                return null;

            _selected.Clear();
            foreach (WaveSignal row in rows)
                _selected.Add(row);
            _anchor = rows[0];
            return GroupSelected(name);
        }

        /// <summary>
        /// Puts the selected signals into a new group called <paramref name="name"/>, where the first of
        /// them was, and selects it; null when no signal is selected. A signal already in a group leaves it.
        /// </summary>
        public WaveGroup GroupSelected(string name)
        {
            List<WaveSignal> signals = SelectedSignals();
            if (signals.Count == 0)
                return null;

            var group = new WaveGroup(string.IsNullOrWhiteSpace(name) ? "group" : name.Trim());
            int at = TopIndexOf(signals[0]);

            foreach (WaveSignal signal in signals)
            {
                int top = TopIndexOf(signal);
                if (_items[top] == signal)
                {
                    _items.RemoveAt(top);
                    if (top < at) at--;
                }
                else
                {
                    var from = (WaveGroup)_items[top];
                    from._signals.Remove(signal);
                    if (from._signals.Count == 0)
                    {
                        _items.RemoveAt(top);
                        if (top < at) at--;
                    }
                }

                group._signals.Add(signal);
            }

            _items.Insert(Math.Min(at, _items.Count), group);
            _selected.Clear();
            _selected.Add(group);
            _anchor = group;
            Changed();
            return group;
        }

        /// <summary>Puts a group's signals back in the list where its header was, selected.</summary>
        public void Ungroup(WaveGroup group)
        {
            int at = _items.IndexOf(group);
            if (at < 0)
                return;

            _items.RemoveAt(at);
            _items.InsertRange(at, group._signals);
            _selected.Remove(group);
            foreach (WaveSignal signal in group._signals)
                _selected.Add(signal);
            Changed();
        }

        /// <summary>Opens a shut group, or shuts an open one.</summary>
        public void Toggle(WaveGroup group)
        {
            if (!_items.Contains(group))
                return;
            group.Expanded = !group.Expanded;
            if (!group.Expanded)
                foreach (WaveSignal signal in group._signals)
                    _selected.Remove(signal);
            Changed();
        }

        /// <summary>Gives a line a name of its own: a header its group's, a signal one in place of its own; empty puts a signal's own name back.</summary>
        public void Rename(int line, string name)
        {
            WaveLine shown = Lines[line];
            string trimmed = name?.Trim();
            if (shown.IsHeader)
            {
                if (string.IsNullOrEmpty(trimmed))
                    return;
                shown.Group.Name = trimmed;
            }
            else
            {
                shown.Signal.Rename = string.IsNullOrEmpty(trimmed) ? null : trimmed;
            }

            Changed();
        }

        /// <summary>Sets the radix of every selected signal; null puts each back to its default.</summary>
        public void SetRadix(WaveRadix? radix)
        {
            foreach (WaveSignal signal in SelectedSignals())
                signal.Radix = radix;
            Changed();
        }

        /// <summary>
        /// Moves line <paramref name="line"/> to where line <paramref name="to"/> is now, within its
        /// level: a signal among the list's top level or within its own group, a header among the top level.
        /// </summary>
        public void Move(int line, int to)
        {
            WaveLine from = Lines[line], target = Lines[Math.Max(0, Math.Min(to, _lines.Count - 1))];
            object item = from.IsHeader ? (object)from.Group : from.Signal;
            WaveGroup within = from.IsHeader ? null : GroupOf(from.Signal);

            if (within != null)
            {
                if (target.Group != within || target.IsHeader)
                    return;
                within._signals.Remove(from.Signal);
                within._signals.Insert(Math.Min(within._signals.IndexOf(target.Signal) + (to > line ? 1 : 0), within._signals.Count), from.Signal);
            }
            else
            {
                object at = target.Group != null ? target.Group : (object)target.Signal;
                if (at == item)
                    return;
                _items.Remove(item);
                int index = _items.IndexOf(at);
                _items.Insert(to > line ? index + 1 : index, item);
            }

            Changed();
        }

        // -----------------------------------------------------------------
        // Selection
        // -----------------------------------------------------------------

        /// <summary>
        /// Selects line <paramref name="line"/>: alone, or with <paramref name="extend"/> every line from
        /// the last one selected to it, or with <paramref name="toggle"/> in or out of what is selected.
        /// </summary>
        public void Select(int line, bool extend = false, bool toggle = false)
        {
            WaveLine shown = Lines[line];
            object item = shown.IsHeader ? (object)shown.Group : shown.Signal;

            if (toggle)
            {
                if (!_selected.Remove(item))
                    _selected.Add(item);
                _anchor = item;
            }
            else if (extend && _anchor != null && LineOfItem(_anchor) >= 0)
            {
                int from = LineOfItem(_anchor);
                _selected.Clear();
                for (int i = Math.Min(from, line); i <= Math.Max(from, line); i++)
                    _selected.Add(_lines[i].IsHeader ? (object)_lines[i].Group : _lines[i].Signal);
            }
            else
            {
                _selected.Clear();
                _selected.Add(item);
                _anchor = item;
            }

            Changed();
        }

        /// <summary>Selects nothing.</summary>
        public void ClearSelection()
        {
            if (_selected.Count == 0)
                return;
            _selected.Clear();
            _anchor = null;
            Changed();
        }

        // -----------------------------------------------------------------
        // The design
        // -----------------------------------------------------------------

        /// <summary>
        /// Finds every row's signal again in <paramref name="index"/>, by its key: what a design built
        /// again needs. A key the new design does not have leaves its row in place, showing nothing.
        /// </summary>
        public void Resolve(SignalIndex index)
        {
            Index = index ?? throw new ArgumentNullException(nameof(index));
            foreach (WaveSignal signal in AllSignals)
                signal.Signal = index.TryFind(signal.Key, out int found) ? found : -1;
            Changed();
        }

        /// <summary>
        /// What the model's rows are (<see cref="WorkbenchModel.SetRows"/>): one per line, a header's
        /// -1, and each signal's radix or its default from <paramref name="defaultRadix"/>.
        /// </summary>
        public void Rows(List<int> signals, List<WaveRadix> radixes, Func<int, WaveRadix> defaultRadix)
        {
            if (signals == null) throw new ArgumentNullException(nameof(signals));
            if (radixes == null) throw new ArgumentNullException(nameof(radixes));
            if (defaultRadix == null) throw new ArgumentNullException(nameof(defaultRadix));

            signals.Clear();
            radixes.Clear();
            foreach (WaveLine line in Lines)
            {
                int signal = line.IsHeader ? -1 : line.Signal.Signal;
                signals.Add(signal);
                radixes.Add(!line.IsHeader && line.Signal.Radix.HasValue ? line.Signal.Radix.Value : defaultRadix(signal));
            }
        }

        // -----------------------------------------------------------------

        /// <summary>The line a signal is shown on, or -1 (none, or in a shut group).</summary>
        public int LineOf(WaveSignal signal) => LineOfItem(signal);

        /// <summary>The group a signal is in, or null.</summary>
        public WaveGroup GroupOf(WaveSignal signal)
        {
            foreach (object item in _items)
                if (item is WaveGroup group && group._signals.Contains(signal))
                    return group;
            return null;
        }

        /// <summary>The row in the list with <paramref name="key"/>, or null.</summary>
        public WaveSignal Find(string key)
        {
            foreach (WaveSignal signal in AllSignals)
                if (signal.Key == key)
                    return signal;
            return null;
        }

        private int LineOfItem(object item)
        {
            IReadOnlyList<WaveLine> lines = Lines;
            for (int i = 0; i < lines.Count; i++)
                if ((lines[i].IsHeader ? (object)lines[i].Group : lines[i].Signal) == item)
                    return i;
            return -1;
        }

        private int TopIndexOf(WaveSignal signal)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] == signal || (_items[i] is WaveGroup group && group._signals.Contains(signal)))
                    return i;
            return -1;
        }

        private void Changed()
        {
            _dirty = true;
            Version++;
        }

        private void Flatten()
        {
            _lines.Clear();
            foreach (object item in _items)
            {
                if (item is WaveSignal signal)
                {
                    _lines.Add(new WaveLine(signal, null, _selected.Contains(signal)));
                    continue;
                }

                var group = (WaveGroup)item;
                bool whole = _selected.Contains(group);
                _lines.Add(new WaveLine(null, group, whole));
                if (group.Expanded)
                    foreach (WaveSignal inner in group._signals)
                        _lines.Add(new WaveLine(inner, group, whole || _selected.Contains(inner)));
            }

            _dirty = false;
        }
    }
}
