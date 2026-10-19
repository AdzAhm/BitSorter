using System.Collections.Generic;

namespace BitSorter.SystemVerilog.Syntax
{
    public enum DiagnosticSeverity { Error, Warning, Info }

    /// <summary>
    /// Something the reader has to say about the source: where, how serious, a stable code, and a plain
    /// sentence. Errors stop a design from being built; warnings do not.
    /// </summary>
    /// <remarks>
    /// The message is written for a student: what is wrong in the words of the course, and the way out
    /// when there is one -- never the parser's own vocabulary. The code is what a test or a filter keys
    /// on, so it never changes once given.
    /// </remarks>
    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticSeverity severity, SourceLocation location, string code, string message)
        {
            Severity = severity;
            Location = location;
            Code = code;
            Message = message;
        }

        public DiagnosticSeverity Severity { get; }
        public SourceLocation Location { get; }
        public string Code { get; }
        public string Message { get; }

        public override string ToString() =>
            $"{Location}: {(Severity == DiagnosticSeverity.Error ? "error" : Severity == DiagnosticSeverity.Warning ? "warning" : "note")} {Code}: {Message}";
    }

    /// <summary>The codes the reader gives. Never reuse or renumber one.</summary>
    public static class DiagnosticCodes
    {
        // Reading characters.
        public const string UnexpectedCharacter = "SV0001";
        public const string UnterminatedComment = "SV0002";
        public const string UnterminatedString = "SV0003";
        public const string LookalikeCharacter = "SV0004";
        public const string BadNumber = "SV0005";

        // The preprocessor.
        public const string IncludeNotFound = "SV0101";
        public const string IncludeCycle = "SV0102";
        public const string IncludeTooDeep = "SV0103";
        public const string UndefinedMacro = "SV0104";
        public const string MacroArguments = "SV0105";   // a macro's arguments: too many, too few, not closed (Part L6)
        public const string UnbalancedConditional = "SV0106";
        public const string UnsupportedDirective = "SV0107";
        public const string MacroTooDeep = "SV0108";

        // The parser.
        public const string Expected = "SV0201";
        public const string Unsupported = "SV0202";
        public const string TooDeep = "SV0203";
        public const string NotInModule = "SV0204";
        public const string AssignmentOutsideProcess = "SV0205";
        public const string MismatchedLabel = "SV0206";
        public const string NonStandardOperator = "SV0207";
    }

    /// <summary>Diagnostics as they are gathered.</summary>
    public sealed class DiagnosticBag
    {
        private readonly List<Diagnostic> _items = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Items => _items;

        public bool HasErrors
        {
            get
            {
                foreach (Diagnostic d in _items)
                    if (d.Severity == DiagnosticSeverity.Error)
                        return true;
                return false;
            }
        }

        /// <summary>A cap, so a file of nonsense gives a readable number of messages and not one per token.</summary>
        public const int MaxItems = 500;

        public void Error(SourceLocation at, string code, string message) => Add(DiagnosticSeverity.Error, at, code, message);
        public void Warning(SourceLocation at, string code, string message) => Add(DiagnosticSeverity.Warning, at, code, message);

        private void Add(DiagnosticSeverity severity, SourceLocation at, string code, string message)
        {
            if (_items.Count < MaxItems)
                _items.Add(new Diagnostic(severity, at, code, message));
        }
    }
}
