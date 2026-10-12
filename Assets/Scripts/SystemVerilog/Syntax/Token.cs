using System.Collections.Generic;

namespace BitSorter.SystemVerilog.Syntax
{
    public enum TokenKind
    {
        EndOfFile,
        Identifier,
        Keyword,

        /// <summary>A system task or function: <c>$display</c>, <c>$clog2</c>.</summary>
        SystemName,

        /// <summary>An integer literal, with any size and base joined into one: <c>8'hA5</c>.</summary>
        Number,

        RealNumber,
        String,

        /// <summary>A compiler directive or a macro use: <c>`include</c>, <c>`WIDTH</c>.</summary>
        Directive,

        Symbol,
    }

    /// <summary>One word, number or symbol of the source, and where it was.</summary>
    public sealed class Token
    {
        public Token(TokenKind kind, string text, SourceLocation location, int logicalLine)
        {
            Kind = kind;
            Text = text;
            Location = location;
            LogicalLine = logicalLine;
        }

        public TokenKind Kind { get; }
        public string Text { get; }
        public SourceLocation Location { get; }

        /// <summary>
        /// The line a directive's arguments run to: the physical line, except that a backslash at a
        /// line's end continues it, as a multi-line <c>`define</c> needs.
        /// </summary>
        public int LogicalLine { get; }

        /// <summary>A <c>//</c> comment later on the same line, which a port or a declaration keeps.</summary>
        public string TrailingComment { get; internal set; }

        public bool Is(string symbol) => Kind == TokenKind.Symbol && Text == symbol;
        public bool IsKeyword(string keyword) => Kind == TokenKind.Keyword && Text == keyword;

        public override string ToString() => $"{Kind} '{Text}' at {Location}";
    }

    /// <summary>SystemVerilog's reserved words (IEEE 1800, Annex B): a name that is one of these is not a name.</summary>
    public static class Keywords
    {
        public static bool IsKeyword(string word) => All.Contains(word);

        private static readonly HashSet<string> All = new HashSet<string>
        {
            "accept_on", "alias", "always", "always_comb", "always_ff", "always_latch", "and", "assert", "assign",
            "assume", "automatic", "before", "begin", "bind", "bins", "binsof", "bit", "break", "buf", "bufif0",
            "bufif1", "byte", "case", "casex", "casez", "cell", "chandle", "checker", "class", "clocking", "cmos",
            "config", "const", "constraint", "context", "continue", "cover", "covergroup", "coverpoint", "cross",
            "deassign", "default", "defparam", "design", "disable", "dist", "do", "edge", "else", "end", "endcase",
            "endchecker", "endclass", "endclocking", "endconfig", "endfunction", "endgenerate", "endgroup",
            "endinterface", "endmodule", "endpackage", "endprimitive", "endprogram", "endproperty", "endspecify",
            "endsequence", "endtable", "endtask", "enum", "event", "eventually", "expect", "export", "extends",
            "extern", "final", "first_match", "for", "force", "foreach", "forever", "fork", "forkjoin", "function",
            "generate", "genvar", "global", "highz0", "highz1", "if", "iff", "ifnone", "ignore_bins", "illegal_bins",
            "implements", "implies", "import", "incdir", "include", "initial", "inout", "input", "inside",
            "instance", "int", "integer", "interconnect", "interface", "intersect", "join", "join_any", "join_none",
            "large", "let", "liblist", "library", "local", "localparam", "logic", "longint", "macromodule",
            "matches", "medium", "modport", "module", "nand", "negedge", "nettype", "new", "nexttime", "nmos",
            "nor", "noshowcancelled", "not", "notif0", "notif1", "null", "or", "output", "package", "packed",
            "parameter", "pmos", "posedge", "primitive", "priority", "program", "property", "protected", "pull0",
            "pull1", "pulldown", "pullup", "pulsestyle_ondetect", "pulsestyle_onevent", "pure", "rand", "randc",
            "randcase", "randsequence", "rcmos", "real", "realtime", "ref", "reg", "reject_on", "release", "repeat",
            "restrict", "return", "rnmos", "rpmos", "rtran", "rtranif0", "rtranif1", "s_always", "s_eventually",
            "s_nexttime", "s_until", "s_until_with", "scalared", "sequence", "shortint", "shortreal",
            "showcancelled", "signed", "small", "soft", "solve", "specify", "specparam", "static", "string",
            "strong", "strong0", "strong1", "struct", "super", "supply0", "supply1", "sync_accept_on",
            "sync_reject_on", "table", "tagged", "task", "this", "throughout", "time", "timeprecision", "timeunit",
            "tran", "tranif0", "tranif1", "tri", "tri0", "tri1", "triand", "trior", "trireg", "type", "typedef",
            "union", "unique", "unique0", "unsigned", "until", "until_with", "untyped", "use", "uwire", "var",
            "vectored", "virtual", "void", "wait", "wait_order", "wand", "weak", "weak0", "weak1", "while",
            "wildcard", "wire", "with", "within", "wor", "xnor", "xor",
        };
    }
}
