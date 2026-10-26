using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>How much a line of the transcript matters.</summary>
    public enum TranscriptSeverity { Info, Warning, Error }

    /// <summary>Which lines of the transcript are shown: all, or one severity.</summary>
    public enum TranscriptFilter { All, Errors, Warnings, Messages }

    /// <summary>
    /// One line of the transcript: something the document says about itself -- a part not wired, a name used twice --
    /// or something the run said at a moment -- a <c>$display</c>, an <c>$error</c>, where it stopped.
    /// </summary>
    public readonly struct TranscriptLine
    {
        public TranscriptLine(TranscriptSeverity severity, string text, long time, string module = null, int part = -1, int wire = -1)
        {
            Severity = severity;
            Text = text ?? "";
            Time = time;
            Module = module;
            Part = part;
            Wire = wire;
        }

        public TranscriptSeverity Severity { get; }

        public string Text { get; }

        /// <summary>When the run said it, in the run's unit; -1 for what the document says, which is not of a moment.</summary>
        public long Time { get; }

        /// <summary>For the document's: the module, and the part or the wire it is about, or -1.</summary>
        public string Module { get; }

        /// <inheritdoc cref="Module"/>
        public int Part { get; }

        /// <inheritdoc cref="Module"/>
        public int Wire { get; }

        /// <summary>Whether it is the run's, said at a moment, rather than the document's.</summary>
        public bool IsMessage => Time >= 0;

        public override string ToString() => (IsMessage ? Time + ": " : "") + Severity + ": " + Text;
    }

    /// <summary>
    /// The transcript (Part 11c), Unity-free: everything there is to read about the circuit and its run, in one list --
    /// the document's diagnostics first, then the run's messages in time order, the latter appended as the run is made a
    /// slice at a time -- with a filter by severity, a search, and counts.
    /// </summary>
    /// <remarks>
    /// **It reads, it never asks**: <see cref="Update"/> is handed the document's diagnostics and the run's messages each
    /// frame, and makes the list again only when either has changed -- another document, another run, or the same run
    /// further on, whose new messages are appended without reading the old ones again. **A click on a line** is the
    /// view's to carry out: a message seeks to its moment, a diagnostic about a part or a wire selects it.
    /// </remarks>
    public sealed class TranscriptView
    {
        private readonly List<TranscriptLine> _all = new List<TranscriptLine>();
        private readonly List<TranscriptLine> _shown = new List<TranscriptLine>();
        private IReadOnlyList<BoardDiagnostic> _diagnostics;
        private IReadOnlyList<SimMessage> _messages;
        private int _diagnosticCount = -1, _messageCount;
        private TranscriptFilter _filter;
        private string _search = "";
        private IReadOnlyList<TranscriptLine> _notes = Array.Empty<TranscriptLine>();

        /// <summary>
        /// What was said about the document from outside it -- what an import had to say, each file and line (Part 13d) --
        /// listed before everything else until it is set again.
        /// </summary>
        public IReadOnlyList<TranscriptLine> Notes
        {
            get => _notes;
            set
            {
                _notes = value ?? Array.Empty<TranscriptLine>();
                _diagnostics = null;   // made again at the next update
            }
        }

        /// <summary>What an import said (Part 13d), as lines for <see cref="Notes"/>: its errors as errors, the rest as information.</summary>
        public static IReadOnlyList<TranscriptLine> LinesOf(IReadOnlyList<ImportNote> notes)
        {
            var lines = new List<TranscriptLine>(notes?.Count ?? 0);
            if (notes != null)
                foreach (ImportNote note in notes)
                    lines.Add(new TranscriptLine(note.IsError ? TranscriptSeverity.Error : TranscriptSeverity.Info, note.ToString(), -1));
            return lines;
        }

        /// <summary>The lines shown: <see cref="Filter"/>'s, holding <see cref="Search"/>, in order.</summary>
        public IReadOnlyList<TranscriptLine> Lines => _shown;

        /// <summary>How many lines there are of each severity, whatever is shown.</summary>
        public int Errors { get; private set; }

        /// <inheritdoc cref="Errors"/>
        public int Warnings { get; private set; }

        /// <inheritdoc cref="Errors"/>
        public int Infos { get; private set; }

        /// <summary>Counts up every time the lines shown change: what a view redraws on.</summary>
        public int Version { get; private set; }

        /// <summary>Which lines are shown.</summary>
        public TranscriptFilter Filter
        {
            get => _filter;
            set
            {
                if (_filter == value)
                    return;
                _filter = value;
                Show();
            }
        }

        /// <summary>The text every line shown holds, ignoring case; empty for every line.</summary>
        public string Search
        {
            get => _search;
            set
            {
                value = (value ?? "").Trim();
                if (_search == value)
                    return;
                _search = value;
                Show();
            }
        }

        /// <summary>
        /// The list made from <paramref name="diagnostics"/> and <paramref name="messages"/>, if either has changed since
        /// the last call; true when it has. Messages are appended to as a run is made, so more of the same list is read on.
        /// </summary>
        public bool Update(IReadOnlyList<BoardDiagnostic> diagnostics, IReadOnlyList<SimMessage> messages)
        {
            int diagnosticCount = diagnostics?.Count ?? 0, messageCount = messages?.Count ?? 0;
            bool sameDocument = ReferenceEquals(diagnostics, _diagnostics) && diagnosticCount == _diagnosticCount;
            bool sameRun = ReferenceEquals(messages, _messages);
            if (sameDocument && sameRun && messageCount == _messageCount)
                return false;

            if (!sameDocument || !sameRun || messageCount < _messageCount)
            {
                _all.Clear();
                Errors = Warnings = Infos = 0;
                foreach (TranscriptLine note in _notes)
                    Count(note);
                for (int i = 0; i < diagnosticCount; i++)
                {
                    BoardDiagnostic d = diagnostics[i];
                    Count(new TranscriptLine(Of(d.Severity), d.Message, -1, d.Module, d.Part, d.Wire));
                }

                _messageCount = 0;
            }

            for (int i = _messageCount; i < messageCount; i++)
                Count(new TranscriptLine(Of(messages[i].Severity), messages[i].Text, messages[i].Time));

            _diagnostics = diagnostics;
            _diagnosticCount = diagnosticCount;
            _messages = messages;
            _messageCount = messageCount;
            Show();
            return true;
        }

        private void Count(TranscriptLine line)
        {
            _all.Add(line);
            if (line.Severity == TranscriptSeverity.Error)
                Errors++;
            else if (line.Severity == TranscriptSeverity.Warning)
                Warnings++;
            else
                Infos++;
        }

        private void Show()
        {
            _shown.Clear();
            foreach (TranscriptLine line in _all)
                if (Passes(line))
                    _shown.Add(line);
            Version++;
        }

        private bool Passes(TranscriptLine line)
        {
            switch (_filter)
            {
                case TranscriptFilter.Errors when line.Severity != TranscriptSeverity.Error:
                case TranscriptFilter.Warnings when line.Severity != TranscriptSeverity.Warning:
                case TranscriptFilter.Messages when line.Severity != TranscriptSeverity.Info:
                    return false;
            }

            return _search.Length == 0 || line.Text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TranscriptSeverity Of(DiagnosticSeverity severity) =>
            severity == DiagnosticSeverity.Error ? TranscriptSeverity.Error
            : severity == DiagnosticSeverity.Warning ? TranscriptSeverity.Warning
            : TranscriptSeverity.Info;

        private static TranscriptSeverity Of(Severity severity) =>
            severity == Severity.Error ? TranscriptSeverity.Error
            : severity == Severity.Warning ? TranscriptSeverity.Warning
            : TranscriptSeverity.Info;

        /// <summary>The word a line is marked with, beside its colour: never the colour alone.</summary>
        public static string Mark(TranscriptSeverity severity) =>
            severity == TranscriptSeverity.Error ? "ERROR" : severity == TranscriptSeverity.Warning ? "WARNING" : "INFO";
    }
}
