using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>A fault the code editor marks: where it is in the text, counted from 1, how serious, and what.</summary>
    public readonly struct CodeMark
    {
        public CodeMark(int line, int column, string message, bool isError)
        {
            Line = line;
            Column = column;
            Message = message;
            IsError = isError;
        }

        public int Line { get; }
        public int Column { get; }
        public string Message { get; }
        public bool IsError { get; }

        /// <summary>The mark as a line of the editor's list: where, then what.</summary>
        public override string ToString() => "line " + Line.ToString(CultureInfo.InvariantCulture) + ": " + Message;
    }

    /// <summary>
    /// The text of a written block being edited (Part 12d): what the editor shows, which may run ahead of the document --
    /// typed and not yet applied, or with an error the document would refuse -- and the faults in it, each at its line.
    /// </summary>
    /// <remarks>
    /// **Typing applies itself.** Once the typing has paused for <see cref="PauseMilliseconds"/> the text is checked, as the
    /// document would check it, and with no error it is applied -- one undo step -- and the run made again, so the waves
    /// follow the code as it is written. Ctrl+Enter applies at once (<see cref="Apply"/>). Text with an error stays the
    /// editor's, marked, and the document keeps the last text that worked, so the run is never of a design that does not
    /// build.
    ///
    /// **Undo is the document's.** An undo or a redo changes the module's text under the editor, and the editor follows
    /// it, setting aside anything typed since: what was undone to is the last text that applied.
    ///
    /// **And the typing has its own, inside the editor** (Part 14g): Ctrl+Z and Ctrl+Y while the editor has the keyboard
    /// step back and forward through what was typed (<see cref="UndoTyping"/>), a burst of typing one step and an edit of
    /// whole lines -- Tab, Ctrl+/ -- one of its own, as the browser's editor does; what they come to applies once they pause,
    /// as typing does. A desktop build's field had none, so Ctrl+Z there did nothing while typing. An undo or a redo of the
    /// document starts it afresh, since the text under it is not what it remembers.
    /// </remarks>
    public sealed class CodeDocument
    {
        /// <summary>How long the typing pauses before the text is checked and applied, in milliseconds.</summary>
        public const double PauseMilliseconds = 700;

        /// <summary>How long a pause in the typing starts a step of its own history, in milliseconds.</summary>
        public const double TypingStepMilliseconds = 1000;

        /// <summary>How many steps of the typing are kept.</summary>
        public const int TypingHistory = 200;

        private readonly WorkbenchSession _session;
        private readonly List<CodeMark> _marks = new List<CodeMark>();
        private readonly List<string> _elsewhere = new List<string>();

        /// <summary>The texts the typing can step back to, the latest last, and forward to again after a step back.</summary>
        private readonly List<string> _typedBack = new List<string>(), _typedOn = new List<string>();

        /// <summary>When the last step of typing was, for grouping a burst into one step; NaN to start a new one.</summary>
        private double _stepAt = double.NaN;

        /// <summary>The module's text the editor last took from the document, or gave it.</summary>
        private string _base;

        /// <summary>When the text last changed, if it has not been checked since; NaN when it has.</summary>
        private double _typedAt = double.NaN;

        internal CodeDocument(WorkbenchSession session, string module)
        {
            _session = session;
            Module = module;
            _base = Applied ?? string.Empty;
            Text = _base;
            Check();
        }

        /// <summary>The written module edited.</summary>
        public string Module { get; }

        /// <summary>The text as the editor has it.</summary>
        public string Text { get; private set; }

        /// <summary>The module's text in the document: the last text that applied.</summary>
        public string Applied => _session.Project.Find(Module)?.Source;

        /// <summary>Whether the editor's text is not the document's: typed and not yet applied, or refused.</summary>
        public bool Pending => Text != (Applied ?? string.Empty);

        /// <summary>The faults in the text, by line: errors, which keep it from applying, and warnings, which do not.</summary>
        public IReadOnlyList<CodeMark> Marks => _marks;

        /// <summary>What the text would break outside itself -- a wire left on a pin the header no longer has -- in the document's words.</summary>
        public IReadOnlyList<string> Elsewhere => _elsewhere;

        /// <summary>Whether the text, as it is, would be refused.</summary>
        public bool HasErrors
        {
            get
            {
                if (_elsewhere.Count > 0)
                    return true;
                foreach (CodeMark mark in _marks)
                    if (mark.IsError)
                        return true;
                return false;
            }
        }

        /// <summary>What keeps the text from applying, the first of it; null when nothing does.</summary>
        public string FirstProblem
        {
            get
            {
                foreach (CodeMark mark in _marks)
                    if (mark.IsError)
                        return mark.ToString();
                return _elsewhere.Count > 0 ? _elsewhere[0] : null;
            }
        }

        /// <summary>Changes each time the text, its marks or what it breaks change: what the editor redraws on.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// The editor's text changed to <paramref name="text"/> at <paramref name="now"/> milliseconds: checked once the typing
        /// pauses, and a step of the typing's history once it has paused for <see cref="TypingStepMilliseconds"/>.
        /// </summary>
        public void Type(string text, double now)
        {
            text = text ?? string.Empty;
            if (text == Text)
                return;
            if (double.IsNaN(_stepAt) || now - _stepAt > TypingStepMilliseconds)
                Remember();
            _stepAt = now;
            _typedOn.Clear();
            Text = text;
            _typedAt = now;
            Version++;
        }

        /// <summary>An edit of whole lines (<see cref="CodeEdits"/>: Tab, Ctrl+/) at <paramref name="now"/>: typed, and a step of its own.</summary>
        public void Edit(string text, double now)
        {
            text = text ?? string.Empty;
            if (text == Text)
                return;
            Remember();
            _stepAt = double.NaN;
            _typedOn.Clear();
            Text = text;
            _typedAt = now;
            Version++;
        }

        /// <summary>Whether Ctrl+Z in the editor has a step of typing to go back to.</summary>
        public bool CanUndoTyping => _typedBack.Count > 0;

        /// <summary>Whether Ctrl+Y in the editor has a step to go forward to.</summary>
        public bool CanRedoTyping => _typedOn.Count > 0;

        /// <summary>The typing a step back (Ctrl+Z in the editor), applied once it pauses as typing is. False with nothing to go back to.</summary>
        public bool UndoTyping(double now) => Step(_typedBack, _typedOn, now);

        /// <summary>The typing a step forward again (Ctrl+Y, Ctrl+Shift+Z in the editor). False with nothing to go forward to.</summary>
        public bool RedoTyping(double now) => Step(_typedOn, _typedBack, now);

        private bool Step(List<string> from, List<string> to, double now)
        {
            if (from.Count == 0)
                return false;
            to.Add(Text);
            Text = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            _stepAt = double.NaN;
            _typedAt = now;
            Version++;
            return true;
        }

        /// <summary>The text as it is, a step the typing can go back to.</summary>
        private void Remember()
        {
            if (_typedBack.Count > 0 && _typedBack[_typedBack.Count - 1] == Text)
                return;
            _typedBack.Add(Text);
            if (_typedBack.Count > TypingHistory)
                _typedBack.RemoveAt(0);
        }

        /// <summary>The typing's history forgotten: the text under the editor is no longer what it remembers.</summary>
        private void ForgetTyping()
        {
            _typedBack.Clear();
            _typedOn.Clear();
            _stepAt = double.NaN;
        }

        /// <summary>
        /// One frame at <paramref name="now"/> milliseconds: the editor following the document if an undo or a redo changed
        /// it, or, once the typing has paused, the text checked and, with no error, applied. True when anything changed.
        /// </summary>
        public bool Frame(double now)
        {
            string applied = Applied ?? string.Empty;
            if (applied != _base)
            {
                _base = applied;
                Text = applied;
                _typedAt = double.NaN;
                ForgetTyping();
                Check();
                return true;
            }

            if (double.IsNaN(_typedAt) || now - _typedAt < PauseMilliseconds)
                return false;
            Apply();
            return true;
        }

        /// <summary>The text checked now and, with no error, applied (Ctrl+Enter): one undo step, and the run made again.</summary>
        public EditResult Apply()
        {
            _typedAt = double.NaN;
            bool clean = Check();
            if (!Pending)
                return EditResult.Ok();
            if (!clean)
                return EditResult.Refused(FirstProblem);

            EditResult result = _session.SetSource(Text);
            if (result.Done)
            {
                _base = Text;
            }
            else
            {
                _elsewhere.Add(result.Refusal);
                Version++;
            }

            return result;
        }

        /// <summary>The editor's text put back to the document's, setting aside what was typed since it last applied.</summary>
        public void Revert()
        {
            _base = Applied ?? string.Empty;
            if (Text != _base)
            {
                // What was typed is a step Ctrl+Z in the editor can go back to.
                Remember();
                _typedOn.Clear();
                _stepAt = double.NaN;
            }
            Text = _base;
            _typedAt = double.NaN;
            Check();
        }

        /// <summary>Where in <see cref="Text"/> line <paramref name="line"/>, column <paramref name="column"/> is: where the caret goes for a mark.</summary>
        public int OffsetOf(int line, int column)
        {
            int offset = 0;
            for (int l = 1; l < line && offset < Text.Length; l++)
            {
                int next = Text.IndexOf('\n', offset);
                if (next < 0)
                    return Text.Length;
                offset = next + 1;
            }

            int end = Text.IndexOf('\n', offset);
            if (end < 0)
                end = Text.Length;
            return System.Math.Min(end, offset + System.Math.Max(0, column - 1));
        }

        /// <summary>The text held to the document's rules as it would be with it: its marks, and what it would break elsewhere. True with no error.</summary>
        private bool Check()
        {
            _marks.Clear();
            _elsewhere.Clear();
            Version++;

            Project project = _session.Project;
            Module module = project.Find(Module);
            if (module == null || !module.IsWritten)
                return false;

            Project edited = project.WithModule(module with { Source = Text });
            List<BoardDiagnostic> before = new List<BoardDiagnostic>(_session.Editor.Diagnostics);
            List<BoardDiagnostic> after = BoardRules.Check(edited);
            foreach (BoardDiagnostic d in after)
                if (d.Module == Module && d.Line > 0)
                    _marks.Add(new CodeMark(d.Line, d.Column, d.Message, d.IsError));
            _marks.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));

            // What it breaks elsewhere only once the text itself reads: a text that does not has no pins for a while, and
            // every wire on its block would be listed as broken by the one fault already marked.
            bool reads = true;
            foreach (CodeMark mark in _marks)
                reads &= !mark.IsError;
            if (reads)
                foreach (BoardDiagnostic d in BoardRules.NewErrors(before, after))
                    if (!(d.Module == Module && d.Line > 0))
                        _elsewhere.Add(d.Module != null && d.Module != Module ? d.Module + ": " + d.Message : d.Message);

            return !HasErrors;
        }
    }
}
