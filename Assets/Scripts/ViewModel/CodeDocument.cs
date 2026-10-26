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
    /// </remarks>
    public sealed class CodeDocument
    {
        /// <summary>How long the typing pauses before the text is checked and applied, in milliseconds.</summary>
        public const double PauseMilliseconds = 700;

        private readonly WorkbenchSession _session;
        private readonly List<CodeMark> _marks = new List<CodeMark>();
        private readonly List<string> _elsewhere = new List<string>();

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

        /// <summary>The editor's text changed to <paramref name="text"/> at <paramref name="now"/> milliseconds: checked once the typing pauses.</summary>
        public void Type(string text, double now)
        {
            text = text ?? string.Empty;
            if (text == Text)
                return;
            Text = text;
            _typedAt = now;
            Version++;
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
