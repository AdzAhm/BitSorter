using System.Collections.Generic;
using System.Text;

namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>
    /// Turns source text into tokens: names, keywords, numbers, strings, directives and symbols, each
    /// with its file, line and column.
    /// </summary>
    /// <remarks>
    /// - **A number is one token however it is spaced.** The standard allows <c>8 'h FF</c>; the lexer
    ///   joins it to <c>8'hFF</c>, which is what <see cref="Literal.Parse"/> reads. A size and a base that
    ///   reach the parser apart -- <c>`W'hFF</c>, the size a macro's, or the base on the next line -- the
    ///   parser joins. But a delay's number is never a size: in <c>#2 'b1</c> the delay is 2 and the value
    ///   <c>'b1</c>, since a delay is a number alone (IEEE 1800 A.2.2.3), as Icarus and ModelSim read it.
    /// - **A comment later on a line is kept**, on the token before it, because a port's or a
    ///   declaration's comment is part of what the course writes and the printer gives back.
    /// - **Look-alike characters are read as what they look like, with a warning.** Code copied out of a
    ///   slide arrives with <c>’</c> for an apostrophe and <c>–</c> for a minus; failing on them would
    ///   be exact and unhelpful, and the warning still says which character it was.
    /// - **A string reads every escape of the standard's table 5-1** -- octal and hex codes among them -- and a
    ///   backslash before a line's end continues it onto the next (5.9); the printer writes each back.
    /// - It never throws: an unexpected character is an error and is skipped.
    /// </remarks>
    public static class Lexer
    {
        private static readonly string[] Symbols =
        {
            "<<<=", ">>>=",
            "===", "!==", "<<<", ">>>", "==?", "!=?", "<<=", ">>=", "->>",
            "==", "!=", "<=", ">=", "&&", "||", "<<", ">>", "**", "+:", "-:", "~&", "~|", "~^", "^~", "->", "++",
            "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "::", ".*", "'{",
            "(", ")", "[", "]", "{", "}", ",", ";", ":", ".", "#", "=", "+", "-", "*", "/", "%", "&", "|", "^",
            "~", "!", "<", ">", "?", "@", "$", "'",
        };

        public static List<Token> Lex(string text, string file, DiagnosticBag diagnostics)
        {
            var lexer = new State(text ?? string.Empty, file ?? string.Empty, diagnostics);
            lexer.Run();
            return lexer.Tokens;
        }

        /// <summary>
        /// <paramref name="text"/> with every look-alike read as code written as what it was read as -- an apostrophe, a
        /// double quote, a minus, a space -- and nothing else changed (Part 15i-2): what is exported is then what ran here,
        /// which ModelSim and Icarus, refusing a curly quote, would otherwise not take. One character for one, so every line
        /// stays where it was -- but a minus that would join the character beside it into another operator is kept apart by a
        /// space. A comment, and the text inside a string, are left as written.
        /// </summary>
        public static string Straightened(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(LookalikeCharacters) < 0)
                return text;

            var lexer = new State(text, string.Empty, new DiagnosticBag());
            lexer.Run();
            if (lexer.Lookalikes.Count == 0)
                return text;

            var replaced = new HashSet<int>(lexer.Lookalikes);
            var written = new StringBuilder(text.Length + 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!replaced.Contains(i))
                {
                    written.Append(c);
                    continue;
                }
                if (c == '‘' || c == '’')
                {
                    written.Append('\'');
                }
                else if (c == '“' || c == '”')
                {
                    written.Append('"');
                }
                else if (c == ' ' || c == '﻿')
                {
                    written.Append(' ');
                }
                else
                {
                    // A minus beside what it would join into another operator -- a –-b read as a - -b, not a --b -- is kept
                    // apart from it by a space, the one place a character becomes two (the 15i-2 review).
                    if (i > 0 && text[i - 1] == '-')
                        written.Append(' ');
                    written.Append('-');
                    if (i + 1 < text.Length && (text[i + 1] == '-' || text[i + 1] == '>' || text[i + 1] == '=' || text[i + 1] == ':'))
                        written.Append(' ');
                }
            }
            return written.ToString();
        }

        private static readonly char[] LookalikeCharacters = { '‘', '’', '“', '”', '–', '—', '−', ' ', '﻿' };

        private sealed class State
        {
            private readonly string _text;
            private readonly string _file;
            private readonly DiagnosticBag _diagnostics;
            private int _at;
            private int _line = 1;
            private int _lineStart;
            private int _logicalLine = 1;

            /// <summary>The logical line the last `define was read on: the one line a comment's last backslash continues.</summary>
            private int _defineLine;

            public readonly List<Token> Tokens = new List<Token>();

            /// <summary>Where each look-alike read as code is, by its place in the text: <see cref="Straightened"/>'s.</summary>
            public readonly List<int> Lookalikes = new List<int>();

            public State(string text, string file, DiagnosticBag diagnostics)
            {
                _text = text;
                _file = file;
                _diagnostics = diagnostics;
            }

            private char Peek(int ahead = 0) => _at + ahead < _text.Length ? _text[_at + ahead] : '\0';
            private SourceLocation Here => new SourceLocation(_file, _line, _at - _lineStart + 1);

            public void Run()
            {
                while (true)
                {
                    SkipSpaceAndComments();

                    if (_at >= _text.Length)
                    {
                        Tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, Here, _logicalLine));
                        return;
                    }

                    ReadToken();
                }
            }

            private void Advance()
            {
                if (_text[_at] == '\n')
                {
                    // A backslash just before the newline continues the logical line (a multi-line define).
                    bool continued = _at > 0 && (_text[_at - 1] == '\\' || (_text[_at - 1] == '\r' && _at > 1 && _text[_at - 2] == '\\'));
                    _line++;
                    _lineStart = _at + 1;
                    if (!continued)
                        _logicalLine++;
                }

                _at++;
            }

            /// <summary>At a newline: steps over it as the end of the logical line, whatever is before it.</summary>
            private void EndLine()
            {
                _line++;
                _lineStart = _at + 1;
                _logicalLine++;
                _at++;
            }

            private void SkipSpaceAndComments()
            {
                while (_at < _text.Length)
                {
                    char c = _text[_at];

                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v')
                    {
                        Advance();
                    }
                    else if (c == ' ' || c == '﻿')
                    {
                        // Read as a space, as ever -- and, but for a byte-order mark the file starts with, said so and written as
                        // one in an export, which ModelSim and Icarus refuse it in (the 15i-2 review).
                        if (_at > 0 || c == ' ')
                        {
                            _diagnostics.Warning(Here, DiagnosticCodes.LookalikeCharacter,
                                (c == ' ' ? "A no-break space" : "A byte-order mark") +
                                " is not a space to every simulator; it was read as one. Code copied from a slide often has these.");
                            Lookalikes.Add(_at);
                        }
                        Advance();
                    }
                    else if (c == '\\' && (Peek(1) == '\n' || (Peek(1) == '\r' && Peek(2) == '\n')))
                    {
                        Advance();   // a line continuation: the newline that follows does not end the logical line
                    }
                    else if (c == '/' && Peek(1) == '/')
                    {
                        int line = _line;
                        int start = _at + 2;
                        while (_at < _text.Length && _text[_at] != '\n')
                            _at++;

                        string comment = _text.Substring(start, _at - start).Trim();
                        Token last = Tokens.Count > 0 ? Tokens[Tokens.Count - 1] : null;
                        if (last != null && last.Location.Line == line && last.Location.File == _file && comment.Length > 0)
                            last.TrailingComment = last.TrailingComment == null ? comment : last.TrailingComment + " " + comment;

                        // A backslash ending the comment continues a `define's body, as Icarus and ModelSim read it, and no
                        // other line: a `timescale or an `include whose comment ended in a path's backslash took the next
                        // line into its arguments, and a module written there was gone (the 16d sweep).
                        if (_at < _text.Length && _defineLine != _logicalLine)
                            EndLine();
                    }
                    else if (c == '/' && Peek(1) == '*')
                    {
                        SourceLocation start = Here;
                        Advance();
                        Advance();
                        bool closed = false;

                        while (_at < _text.Length)
                        {
                            if (_text[_at] == '*' && Peek(1) == '/')
                            {
                                Advance();
                                Advance();
                                closed = true;
                                break;
                            }

                            Advance();
                        }

                        if (!closed)
                            _diagnostics.Error(start, DiagnosticCodes.UnterminatedComment, "This /* comment is never closed with */.");
                    }
                    else
                    {
                        return;
                    }
                }
            }

            private void Add(TokenKind kind, string text, SourceLocation at, int logicalLine) =>
                Tokens.Add(new Token(kind, text, at, logicalLine));

            private void ReadToken()
            {
                SourceLocation start = Here;
                int logical = _logicalLine;
                char c = _text[_at];

                if (IsIdentifierStart(c))
                {
                    string word = ReadWhile(IsIdentifierPart);
                    Add(Keywords.IsKeyword(word) ? TokenKind.Keyword : TokenKind.Identifier, word, start, logical);
                    return;
                }

                if (c == '\\')
                {
                    // An escaped identifier: everything up to the next white space.
                    Advance();
                    var name = new StringBuilder();
                    while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]))
                    {
                        name.Append(_text[_at]);
                        Advance();
                    }

                    if (name.Length == 0)
                        _diagnostics.Error(start, DiagnosticCodes.UnexpectedCharacter, "A backslash must start an escaped name.");
                    else
                        Add(TokenKind.Identifier, name.ToString(), start, logical);
                    return;
                }

                if (c == '$' && IsIdentifierPart(Peek(1)))
                {
                    Advance();
                    Add(TokenKind.SystemName, "$" + ReadWhile(IsIdentifierPart), start, logical);
                    return;
                }

                // In a macro's body: `` joins the tokens either side of it into one, and `"...`" is a string
                // the macro's arguments are put into (IEEE 1800, 22.5.1), kept as written until then.
                if (c == '`' && Peek(1) == '`')
                {
                    Advance();
                    Advance();
                    Add(TokenKind.Directive, "``", start, logical);
                    return;
                }

                if (c == '`' && Peek(1) == '"')
                {
                    Advance();
                    Advance();
                    var raw = new StringBuilder();
                    while (_at < _text.Length && !(_text[_at] == '`' && Peek(1) == '"') && _text[_at] != '\n')
                    {
                        raw.Append(_text[_at]);
                        Advance();
                    }

                    if (_at < _text.Length && _text[_at] == '`')
                    {
                        Advance();
                        Advance();
                    }
                    else
                    {
                        _diagnostics.Error(start, DiagnosticCodes.Expected, "`\" starts a string in a macro, which needs a `\" to close it on the same line.");
                    }

                    Add(TokenKind.Directive, "`\"" + raw, start, logical);
                    return;
                }

                if (c == '`' && IsIdentifierStart(Peek(1)))
                {
                    Advance();
                    string directive = "`" + ReadWhile(IsIdentifierPart);
                    if (directive == "`define")
                        _defineLine = logical;
                    Add(TokenKind.Directive, directive, start, logical);
                    return;
                }

                if (char.IsDigit(c))
                {
                    ReadNumber(start, logical);
                    return;
                }

                if (IsApostrophe(c))
                {
                    if (TryReadUnsizedLiteral(start, logical, string.Empty))
                        return;
                }

                if (c == '"' || c == '“' || c == '”')
                {
                    ReadString(start, logical);
                    return;
                }

                if (c == '–' || c == '—' || c == '−')
                {
                    _diagnostics.Warning(start, DiagnosticCodes.LookalikeCharacter,
                        $"'{c}' is a dash, not a minus sign; it was read as '-'. Code copied from a slide often has these.");
                    Lookalikes.Add(_at);
                    Advance();
                    Add(TokenKind.Symbol, "-", start, logical);
                    return;
                }

                if (c == '‘' || c == '’')
                {
                    _diagnostics.Warning(start, DiagnosticCodes.LookalikeCharacter,
                        $"'{c}' is a curly quote, not an apostrophe; it was read as \"'\". Code copied from a slide often has these.");
                    Lookalikes.Add(_at);
                    Advance();
                    Add(TokenKind.Symbol, "'", start, logical);
                    return;
                }

                foreach (string symbol in Symbols)
                {
                    if (string.CompareOrdinal(_text, _at, symbol, 0, symbol.Length) == 0)
                    {
                        for (int i = 0; i < symbol.Length; i++)
                            Advance();
                        Add(TokenKind.Symbol, symbol, start, logical);
                        return;
                    }
                }

                _diagnostics.Error(start, DiagnosticCodes.UnexpectedCharacter,
                    c < ' ' ? $"An unexpected control character (code {(int)c})." : $"'{c}' is not part of SystemVerilog here.");
                Advance();
            }

            private string ReadWhile(System.Func<char, bool> keep)
            {
                int start = _at;
                while (_at < _text.Length && keep(_text[_at]))
                    Advance();
                return _text.Substring(start, _at - start);
            }

            /// <summary>
            /// A number starting with a digit: a plain decimal, a real, or the size of a based literal
            /// (<c>8'hFF</c>, <c>4 'b 1010</c>).
            /// </summary>
            private void ReadNumber(SourceLocation start, int logical)
            {
                string digits = ReadWhile(ch => char.IsDigit(ch) || ch == '_');

                // A real: 1.5, 2.0e-3, 1e9.
                if ((Peek() == '.' && char.IsDigit(Peek(1))) || ((Peek() == 'e' || Peek() == 'E') && (char.IsDigit(Peek(1)) || ((Peek(1) == '-' || Peek(1) == '+') && char.IsDigit(Peek(2))))))
                {
                    var real = new StringBuilder(digits);
                    if (Peek() == '.')
                    {
                        real.Append('.');
                        Advance();
                        real.Append(ReadWhile(ch => char.IsDigit(ch) || ch == '_'));
                    }

                    if (Peek() == 'e' || Peek() == 'E')
                    {
                        real.Append('e');
                        Advance();
                        if (Peek() == '-' || Peek() == '+')
                        {
                            real.Append(Peek());
                            Advance();
                        }
                        real.Append(ReadWhile(ch => char.IsDigit(ch) || ch == '_'));
                    }

                    Add(TokenKind.RealNumber, real.ToString(), start, logical);
                    return;
                }

                // A delay's number stands alone: #2 'b1 and #2'b1 are a delay of 2 and the value 'b1, as Icarus and ModelSim
                // read them -- joined, the delay was taken for a size and the statement had no value (the 16d sweep's review).
                if (Tokens.Count > 0 && Tokens[Tokens.Count - 1].Kind == TokenKind.Symbol && Tokens[Tokens.Count - 1].Text == "#")
                {
                    Add(TokenKind.Number, digits, start, logical);
                    return;
                }

                // A size, if an apostrophe and a base follow -- possibly after spaces.
                int save = _at, saveLine = _line, saveLineStart = _lineStart, saveLogical = _logicalLine;
                SkipSpaceOnly();

                if (IsApostrophe(Peek()) && TryReadUnsizedLiteral(start, logical, digits))
                    return;

                _at = save;
                _line = saveLine;
                _lineStart = saveLineStart;
                _logicalLine = saveLogical;
                Add(TokenKind.Number, digits, start, logical);
            }

            private void SkipSpaceOnly()
            {
                while (_at < _text.Length && (_text[_at] == ' ' || _text[_at] == '\t'))
                    Advance();
            }

            /// <summary>
            /// At an apostrophe: <c>'hFF</c>, <c>'sd5</c>, <c>'0</c>, <c>'x</c> -- prefixed by
            /// <paramref name="size"/> when a size was read. False, reading nothing, when the apostrophe
            /// starts something else, such as a cast <c>8'(x)</c>.
            /// </summary>
            private bool TryReadUnsizedLiteral(SourceLocation start, int logical, string size)
            {
                char apostrophe = Peek();
                int offset = 1;
                bool signed = false;

                if (Peek(offset) == 's' || Peek(offset) == 'S')
                {
                    signed = true;
                    offset++;
                }

                char baseChar = char.ToLowerInvariant(Peek(offset));
                bool based = baseChar == 'b' || baseChar == 'o' || baseChar == 'd' || baseChar == 'h';

                if (!based)
                {
                    // '0 '1 'x 'z: a fill, only without a size.
                    char fill = Peek(1);
                    if (size.Length == 0 && !signed && (fill == '0' || fill == '1' || fill == 'x' || fill == 'X' || fill == 'z' || fill == 'Z') && !IsIdentifierPart(Peek(2)))
                    {
                        WarnIfCurly(apostrophe, start);
                        Advance();
                        Advance();
                        Add(TokenKind.Number, "'" + fill, start, logical);
                        return true;
                    }

                    if (size.Length == 0 && Peek(1) == '{')
                    {
                        WarnIfCurly(apostrophe, start);   // the 15i-2 review: read as code, and was never said or straightened
                        Advance();
                        Advance();
                        Add(TokenKind.Symbol, "'{", start, logical);
                        return true;
                    }

                    return false;
                }

                WarnIfCurly(apostrophe, start);
                for (int i = 0; i < offset + 1; i++)
                    Advance();

                SkipSpaceOnly();
                string digits = ReadWhile(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '?');

                if (digits.Length == 0)
                    _diagnostics.Error(start, DiagnosticCodes.BadNumber, $"The literal {size}'{(signed ? "s" : "")}{baseChar} has a base but no digits.");

                Add(TokenKind.Number, size + "'" + (signed ? "s" : "") + baseChar + digits, start, logical);
                return true;
            }

            /// <summary>Called with the text at the apostrophe, before it is read.</summary>
            private void WarnIfCurly(char apostrophe, SourceLocation at)
            {
                if (apostrophe == '\'')
                    return;
                _diagnostics.Warning(at, DiagnosticCodes.LookalikeCharacter,
                    $"'{apostrophe}' is a curly quote, not an apostrophe; it was read as \"'\". Code copied from a slide often has these.");
                Lookalikes.Add(_at);
            }

            private void ReadString(SourceLocation start, int logical)
            {
                // Opened with a plain quote, it ends only at one, and a curly quote inside is text; opened with a curly quote,
                // copied from a slide, it ends at either (the 16d sweep: any of the three ended any string).
                bool curly = _text[_at] != '"';
                if (curly)
                {
                    _diagnostics.Warning(start, DiagnosticCodes.LookalikeCharacter,
                        $"'{_text[_at]}' is a curly quote; it was read as '\"'. Code copied from a slide often has these.");
                    Lookalikes.Add(_at);
                }

                Advance();
                var value = new StringBuilder();

                while (_at < _text.Length)
                {
                    char c = _text[_at];

                    if (c == '"' || (curly && (c == '“' || c == '”')))
                    {
                        if (c != '"')
                            Lookalikes.Add(_at);
                        Advance();
                        Add(TokenKind.String, value.ToString(), start, logical);
                        return;
                    }

                    if (c == '\n')
                        break;

                    if (c == '\\' && _at + 1 < _text.Length)
                    {
                        Advance();
                        ReadEscape(value);
                        continue;
                    }

                    value.Append(c);
                    Advance();
                }

                _diagnostics.Error(start, DiagnosticCodes.UnterminatedString, "This string is never closed with a \" on the same line.");
                Add(TokenKind.String, value.ToString(), start, logical);
            }

            /// <summary>
            /// What follows a backslash in a string, at the character after it: every escape of IEEE 1800's table 5-1 --
            /// \n, \t, \\, \", \v, \f, \a, \ddd in octal and \xdd in hex -- and a backslash before a line's end, which drops
            /// both (5.9); any other character is itself. Only \n and \t were read until the 16d sweep, so "\101" was the
            /// text 101, where Icarus and ModelSim print A.
            /// </summary>
            private void ReadEscape(StringBuilder value)
            {
                char escaped = _text[_at];

                if (escaped == '\n' || (escaped == '\r' && Peek(1) == '\n'))
                {
                    if (escaped == '\r')
                        Advance();
                    Advance();   // a continued line: the backslash and the line break are not part of the string
                    return;
                }

                if (escaped >= '0' && escaped <= '7')
                {
                    int code = 0;
                    for (int digits = 0; digits < 3 && _at < _text.Length && _text[_at] >= '0' && _text[_at] <= '7'; digits++)
                    {
                        code = code * 8 + (_text[_at] - '0');
                        Advance();
                    }
                    value.Append((char)(code & 0xFF));
                    return;
                }

                if (escaped == 'x' && IsHexDigit(Peek(1)))
                {
                    Advance();
                    int code = 0;
                    for (int digits = 0; digits < 2 && _at < _text.Length && IsHexDigit(_text[_at]); digits++)
                    {
                        code = code * 16 + HexValue(_text[_at]);
                        Advance();
                    }
                    value.Append((char)code);
                    return;
                }

                switch (escaped)
                {
                    case 'n': value.Append('\n'); break;
                    case 't': value.Append('\t'); break;
                    case 'v': value.Append('\v'); break;
                    case 'f': value.Append('\f'); break;
                    case 'a': value.Append('\a'); break;
                    default: value.Append(escaped); break;
                }
                Advance();
            }

            private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

            private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

            private static bool IsApostrophe(char c) => c == '\'' || c == '’' || c == '‘';
        }

        internal static bool IsIdentifierStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

        internal static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || (c >= '0' && c <= '9') || c == '$';
    }
}
