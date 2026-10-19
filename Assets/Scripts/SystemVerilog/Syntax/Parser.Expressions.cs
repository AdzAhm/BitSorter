using System.Collections.Generic;

namespace BitSorter.SystemVerilog.Syntax
{
    public sealed partial class Parser
    {
        /// <summary>
        /// How tightly each binary operator binds, higher first (IEEE 1800, table 11-2 -- the course's
        /// own operator table, with <c>**</c> and the wildcard equalities it leaves out). All are left
        /// associative; <c>?:</c>, lower than every one of them, is the right-associative exception.
        /// </summary>
        /// <remarks>
        /// <c>~&amp;</c> and <c>~|</c> between two operands are not the standard's -- it has them only as
        /// reductions -- but the course's first workshop writes <c>assign Z = X ~&amp; Y;</c>, and Icarus
        /// reads it as NAND. They bind as <c>&amp;</c> and <c>|</c> do, and draw a warning.
        /// </remarks>
        private static int BinaryPrecedence(Token token)
        {
            if (token.Kind != TokenKind.Symbol)
                return -1;

            switch (token.Text)
            {
                case "**": return 11;
                case "*": case "/": case "%": return 10;
                case "+": case "-": return 9;
                case "<<": case ">>": case "<<<": case ">>>": return 8;
                case "<": case "<=": case ">": case ">=": return 7;
                case "==": case "!=": case "===": case "!==": case "==?": case "!=?": return 6;
                case "&": case "~&": return 5;
                case "^": case "~^": case "^~": return 4;
                case "|": case "~|": return 3;
                case "&&": return 2;
                case "||": return 1;
                default: return -1;
            }
        }

        private static readonly HashSet<string> UnaryOperators = new HashSet<string>
        {
            "+", "-", "!", "~", "&", "~&", "|", "~|", "^", "~^", "^~",
        };

        /// <summary>An expression, down to the conditional operator.</summary>
        public Expression ParseExpression()
        {
            if (!Enter())
                return new ErrorExpression { Location = Current.Location };

            try
            {
                Expression condition = ParseBinary(1);

                if (!Current.Is("?"))
                    return condition;

                var conditional = new ConditionalExpression { Location = condition.Location, Condition = condition };
                Next();
                conditional.WhenTrue = ParseExpression();
                Expect(":", "between the two choices of '?'");
                conditional.WhenFalse = ParseExpression();
                return conditional;
            }
            finally
            {
                Leave();
            }
        }

        /// <summary><c>inside</c> binds as the relational operators do (IEEE 1800, table 11-2).</summary>
        private const int InsidePrecedence = 7;

        private Expression ParseBinary(int minimum)
        {
            Expression left = ParseUnary();

            while (true)
            {
                if (Current.IsKeyword("inside") && InsidePrecedence >= minimum)
                {
                    Next();
                    left = ParseInsideSet(left);
                    continue;
                }

                int precedence = BinaryPrecedence(Current);
                if (precedence < minimum)
                    return left;

                Token op = Next();
                if (op.Text == "~&" || op.Text == "~|")
                {
                    string plain = op.Text == "~&" ? "&" : "|";
                    _diagnostics.Warning(op.Location, DiagnosticCodes.NonStandardOperator,
                        $"'a {op.Text} b' is not standard SystemVerilog, where {op.Text} only reduces one value; " +
                        $"BitSorter and some simulators read it as {(plain == "&" ? "NAND" : "NOR")}. ~(a {plain} b) is the same and works everywhere.");
                }

                Expression right = ParseBinary(precedence + 1);
                left = new BinaryExpression { Location = left.Location, Operator = op.Text, Left = left, Right = right };
            }
        }

        /// <summary>The set after <c>inside</c>: <c>{1, 3, [6:9]}</c>.</summary>
        private Expression ParseInsideSet(Expression value)
        {
            var inside = new InsideExpression { Location = value.Location, Value = value };
            if (!Expect("{", "to open the set after 'inside'"))
                return inside;

            do
            {
                inside.Set.Add(ParseSetMember());
            }
            while (Accept(","));

            Expect("}", "to close the set after 'inside'");
            return inside;
        }

        /// <summary>A member of an <c>inside</c> set or a <c>case inside</c> label: a value, or a range <c>[low:high]</c>.</summary>
        internal Expression ParseSetMember()
        {
            if (!Current.Is("["))
                return ParseExpression();

            var range = new ValueRange { Location = Next().Location };
            range.Low = ParseExpression();
            Expect(":", "between the ends of the range");
            range.High = ParseExpression();
            Expect("]", "to close the range");
            return range;
        }

        private Expression ParseUnary()
        {
            if (Current.Kind == TokenKind.Symbol && UnaryOperators.Contains(Current.Text))
            {
                if (!Enter())
                    return new ErrorExpression { Location = Current.Location };

                try
                {
                    Token op = Next();
                    return new UnaryExpression { Location = op.Location, Operator = op.Text, Operand = ParseUnary() };
                }
                finally
                {
                    Leave();
                }
            }

            if (Current.Is("++") || Current.Is("--"))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported,
                    $"'{Current.Text}' inside an expression is not supported by BitSorter; write it as its own statement.");
                Next();
            }

            return ParsePostfix(ParsePrimary());
        }

        /// <summary>
        /// Something that can be assigned to: a name, with selects, or a concatenation of them. Read
        /// without binary operators, so the <c>&lt;=</c> after it is the assignment and not a comparison.
        /// </summary>
        private Expression ParseLValue()
        {
            if (Current.Is("{"))
                return ParsePostfix(ParseConcatenation());

            return ParsePostfix(ParsePrimary());
        }

        private Expression ParsePrimary()
        {
            Token t = Current;

            switch (t.Kind)
            {
                case TokenKind.Number:
                {
                    Next();

                    // A size cast: 8'(x).
                    if (Current.Is("'") && Peek(1).Is("("))
                    {
                        Next();
                        return ParseCastBody(new CastExpression { Location = t.Location, TargetText = t.Text, Width = Literal(t) });
                    }

                    return Literal(t);
                }

                case TokenKind.RealNumber:
                    Next();
                    return new RealLiteral { Location = t.Location, Text = t.Text };

                case TokenKind.String:
                    Next();
                    return new StringLiteral { Location = t.Location, Value = t.Text };

                case TokenKind.SystemName:
                {
                    Next();
                    var call = new CallExpression { Location = t.Location, Name = t.Text };
                    if (Current.Is("("))
                    {
                        call.HasParentheses = true;
                        ParseArguments(call.Arguments, out bool _, call.Name);
                    }
                    return call;
                }

                case TokenKind.Identifier:
                    return ParseNameOrCall();

                case TokenKind.Keyword:
                    if ((t.Text == "signed" || t.Text == "unsigned" || IsDataTypeStart(t)) && Peek(1).Is("'") && Peek(2).Is("("))
                    {
                        Next();
                        Next();
                        return ParseCastBody(new CastExpression { Location = t.Location, TargetText = t.Text });
                    }

                    Error(t.Location, DiagnosticCodes.Expected, $"Expected a value here, but found the keyword '{t.Text}'.");
                    return new ErrorExpression { Location = t.Location };

                case TokenKind.Directive:
                    Next();   // an undefined macro, already reported
                    return new ErrorExpression { Location = t.Location };
            }

            if (t.Is("("))
            {
                Next();
                Expression inner = ParseExpression();

                if (Accept(":"))
                {
                    _diagnostics.Warning(Previous.Location, DiagnosticCodes.Unsupported, "A min:typ:max value uses its typical (middle) value in BitSorter.");
                    inner = ParseExpression();
                    if (Accept(":"))
                        ParseExpression();
                }

                Expect(")", "to close the '('");
                return new ParenthesizedExpression { Location = t.Location, Inner = inner };
            }

            if (t.Is("{"))
                return ParseConcatenation();

            if (t.Is("'{"))
                return ParseAssignmentPattern();

            Error(t.Location, DiagnosticCodes.Expected, $"Expected a value here, but found {Describe(t)}.");
            return new ErrorExpression { Location = t.Location };
        }

        private NumberLiteral Literal(Token t)
        {
            LiteralResult result = SystemVerilog.Literal.Parse(t.Text);

            if (!result.Ok)
            {
                Error(t.Location, DiagnosticCodes.BadNumber, result.Error);
                return new NumberLiteral { Location = t.Location, Text = t.Text, Value = LogicVec.Fill(Logic4.X, 32) };
            }

            if (result.Warning != null)
                _diagnostics.Warning(t.Location, DiagnosticCodes.BadNumber, result.Warning);

            return new NumberLiteral { Location = t.Location, Text = t.Text, Value = result.Value, FillsContext = result.FillsContext };
        }

        private Expression ParseNameOrCall()
        {
            Token first = Next();
            var name = new NameExpression { Location = first.Location };
            name.Path.Add(first.Text);

            if (Current.Is("::"))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, "Packages are not supported by BitSorter, so 'package::name' cannot be used.");
                Next();
                if (Current.Kind == TokenKind.Identifier)
                    name.Path[0] = Next().Text;
            }

            if (Current.Is("'") && Peek(1).Is("("))
            {
                Next();
                return ParseCastBody(new CastExpression { Location = first.Location, TargetText = first.Text });
            }

            if (Current.Is("("))
            {
                var call = new CallExpression { Location = first.Location, Name = first.Text, HasParentheses = true };
                ParseArguments(call.Arguments, out bool _, call.Name);
                return call;
            }

            // A hierarchical name: dut.ctl.state.
            while (Current.Is(".") && Peek(1).Kind == TokenKind.Identifier)
            {
                Next();
                name.Path.Add(Next().Text);
            }

            // A method of the value the rest names: s.name(), dut.state.next(2).
            if (name.Path.Count > 1 && Current.Is("("))
            {
                string method = name.Path[name.Path.Count - 1];
                name.Path.RemoveAt(name.Path.Count - 1);
                var call = new MethodCallExpression { Location = name.Location, Target = name, Method = method, HasParentheses = true };
                ParseArguments(call.Arguments, out bool _, method);
                return call;
            }

            return name;
        }

        private CastExpression ParseCastBody(CastExpression cast)
        {
            Expect("(", "after the cast's apostrophe");
            cast.Value = ParseExpression();
            Expect(")", "to close the cast");
            return cast;
        }

        /// <summary>
        /// <c>( a, b, c )</c> after a call's name. Empty places -- <c>$display(, x)</c> -- are kept as
        /// nulls, which only system tasks allow.
        /// </summary>
        private void ParseArguments(List<Expression> into, out bool hadEmpty, string callee)
        {
            hadEmpty = false;
            Next();   // (

            if (Accept(")"))
                return;

            do
            {
                if (Current.Is(",") || Current.Is(")"))
                {
                    into.Add(null);
                    hadEmpty = true;
                    continue;
                }

                into.Add(ParseExpression());
            }
            while (Accept(","));

            Expect(")", $"to close the arguments of {callee}");
        }

        private Expression ParsePostfix(Expression expression)
        {
            while (true)
            {
                if (Current.Is("["))
                {
                    Next();
                    Expression first = ParseExpression();

                    if (Accept(":"))
                    {
                        var range = new RangeSelectExpression { Location = expression.Location, Target = expression, Left = first, Kind = RangeKind.Constant };
                        range.Right = ParseExpression();
                        Expect("]", "to close the part-select");
                        expression = range;
                    }
                    else if (Current.Is("+:") || Current.Is("-:"))
                    {
                        var kind = Next().Text == "+:" ? RangeKind.IndexedUp : RangeKind.IndexedDown;
                        var range = new RangeSelectExpression { Location = expression.Location, Target = expression, Left = first, Kind = kind };
                        range.Right = ParseExpression();
                        Expect("]", "to close the part-select");
                        expression = range;
                    }
                    else
                    {
                        Expect("]", "to close the bit-select");
                        expression = new IndexExpression { Location = expression.Location, Target = expression, Index = first };
                    }

                    continue;
                }

                if (Current.Is(".") && Peek(1).Kind == TokenKind.Identifier)
                {
                    Next();
                    string member = Next().Text;

                    // s.next().name(): a method of what came before.
                    if (Current.Is("("))
                    {
                        var call = new MethodCallExpression { Location = expression.Location, Target = expression, Method = member, HasParentheses = true };
                        ParseArguments(call.Arguments, out bool _, member);
                        expression = call;
                        continue;
                    }

                    expression = new MemberExpression { Location = expression.Location, Target = expression, Member = member };
                    continue;
                }

                return expression;
            }
        }

        /// <summary><c>{a, b}</c>, or a replication <c>{n{a, b}}</c>.</summary>
        private Expression ParseConcatenation()
        {
            Token open = Next();

            if (!Enter())
                return new ErrorExpression { Location = open.Location };

            try
            {
                if (Accept("}"))
                {
                    Error(open.Location, DiagnosticCodes.Expected, "'{}' has nothing in it: a concatenation needs at least one value.");
                    return new ErrorExpression { Location = open.Location };
                }

                Expression first = ParseExpression();

                if (Current.Is("{"))
                {
                    Expression repeated = ParseConcatenation();
                    var replication = new ReplicationExpression
                    {
                        Location = open.Location,
                        Count = first,
                        Value = repeated as ConcatenationExpression ?? Wrap(repeated),
                    };

                    Expect("}", "to close the replication");
                    return replication;
                }

                var concatenation = new ConcatenationExpression { Location = open.Location };
                concatenation.Items.Add(first);

                while (Accept(","))
                    concatenation.Items.Add(ParseExpression());

                Expect("}", "to close the concatenation");
                return concatenation;
            }
            finally
            {
                Leave();
            }
        }

        private static ConcatenationExpression Wrap(Expression inner)
        {
            var wrapper = new ConcatenationExpression { Location = inner.Location };
            wrapper.Items.Add(inner);
            return wrapper;
        }

        private Expression ParseAssignmentPattern()
        {
            var pattern = new AssignmentPatternExpression { Location = Current.Location };
            Next();

            if (!Current.Is("}"))
            {
                do
                {
                    if (AcceptKeyword("default"))
                    {
                        Expect(":", "after 'default' in '{...}");
                        pattern.Default = ParseExpression();
                        continue;
                    }

                    // A key -- a member's name, an index -- and its value, or a value in its place.
                    Expression item = ParseExpression();
                    Expression key = null;
                    if (Accept(":"))
                    {
                        key = item;
                        item = ParseExpression();
                    }

                    pattern.Keys.Add(key);
                    pattern.Items.Add(item);
                }
                while (Accept(","));
            }

            Expect("}", "to close the '{...}");
            return pattern;
        }
    }
}
