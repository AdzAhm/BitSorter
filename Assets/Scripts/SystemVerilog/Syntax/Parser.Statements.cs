namespace BitSorter.SystemVerilog.Syntax
{
    public sealed partial class Parser
    {
        /// <summary>Keywords a statement can never run into: where a block missing its <c>end</c> stops.</summary>
        private bool AtHardStop =>
            Current.IsKeyword("endmodule") || Current.IsKeyword("endfunction") || Current.IsKeyword("endtask") ||
            Current.IsKeyword("endgenerate") || Current.IsKeyword("endcase");

        private Statement ParseStatement()
        {
            if (!Enter())
                return new NullStatement { Location = Current.Location };

            try
            {
                Statement statement = ParseStatementInner();
                return statement;
            }
            finally
            {
                Leave();
            }
        }

        private Statement ParseStatementInner()
        {
            Token t = Current;

            if (t.Kind == TokenKind.Keyword)
            {
                switch (t.Text)
                {
                    case "begin":
                        return ParseBlock();

                    case "if":
                        return ParseIf(null);

                    case "unique":
                    case "unique0":
                    case "priority":
                        Next();
                        if (Current.IsKeyword("if"))
                            return ParseIf(t.Text);
                        if (Current.IsKeyword("case") || Current.IsKeyword("casez") || Current.IsKeyword("casex"))
                            return ParseCase(t.Text);
                        Error(Current.Location, DiagnosticCodes.Expected, $"Expected 'if' or 'case' after '{t.Text}', but found {Describe(Current)}.");
                        return new NullStatement { Location = t.Location };

                    case "case":
                    case "casez":
                    case "casex":
                        return ParseCase(null);

                    case "for":
                        return ParseFor();

                    case "repeat":
                    {
                        var repeat = new RepeatStatement { Location = t.Location };
                        Next();
                        if (Expect("(", "after 'repeat'"))
                        {
                            repeat.Count = ParseExpression();
                            Expect(")", "to close the repeat count");
                        }

                        repeat.Body = ParseStatement();
                        return repeat;
                    }

                    case "while":
                    {
                        var loop = new WhileStatement { Location = t.Location };
                        Next();
                        if (Expect("(", "after 'while'"))
                        {
                            loop.Condition = ParseExpression();
                            Expect(")", "to close the condition");
                        }

                        loop.Body = ParseStatement();
                        return loop;
                    }

                    case "do":
                    {
                        var loop = new WhileStatement { Location = t.Location, IsDoWhile = true };
                        Next();
                        loop.Body = ParseStatement();
                        if (ExpectKeyword("while", "after the body of 'do'") && Expect("(", "after 'while'"))
                        {
                            loop.Condition = ParseExpression();
                            Expect(")", "to close the condition");
                        }

                        Expect(";", "after the do-while");
                        return loop;
                    }

                    case "forever":
                        Next();
                        return new ForeverStatement { Location = t.Location, Body = ParseStatement() };

                    case "wait" when Peek(1).IsKeyword("fork"):
                        Next();
                        Next();
                        Expect(";", "after wait fork");
                        return new WaitForkStatement { Location = t.Location, Comment = Previous.TrailingComment };

                    case "wait":
                    {
                        var wait = new WaitStatement { Location = t.Location };
                        Next();
                        if (Expect("(", "after 'wait'"))
                        {
                            wait.Condition = ParseExpression();
                            Expect(")", "to close the condition");
                        }

                        wait.Body = ParseStatement();
                        return wait;
                    }

                    case "return":
                    {
                        Next();
                        var statement = new ReturnStatement { Location = t.Location };
                        if (!Current.Is(";"))
                            statement.Value = ParseExpression();
                        Expect(";", "after return");
                        return statement;
                    }

                    case "break":
                    case "continue":
                        Next();
                        Expect(";", $"after {t.Text}");
                        return new JumpStatement { Location = t.Location, Keyword = t.Text };

                    case "disable":
                    {
                        Next();
                        var jump = new JumpStatement { Location = t.Location, Keyword = "disable" };
                        jump.Target = Current.IsKeyword("fork") ? Next().Text : ExpectIdentifier("the block to disable");
                        Expect(";", "after disable");
                        return jump;
                    }

                    case "fork":
                        return ParseFork(null);

                    case "foreach":
                        return ParseForeach();

                    case "assert":
                    case "assume":
                    case "cover":
                        return ParseAssertion(null);

                    case "force":
                    case "release":
                    {
                        Next();
                        var force = new ForceStatement { Location = t.Location, Keyword = t.Text, Target = ParseLValue() };
                        if (t.Text == "force" && Expect("=", "after what force overrides"))
                            force.Value = ParseExpression();
                        if (!Expect(";", $"after {t.Text}"))
                            SkipStatementEnd();
                        force.Comment = Previous.TrailingComment;
                        return force;
                    }

                    case "deassign":
                        return SkipUnsupportedStatement($"'{t.Text}' is not supported by BitSorter; use force and release", null);

                    case "void":
                        return SkipUnsupportedStatement("void'(...) is not supported by BitSorter; call the function as a statement", null);
                }
            }

            if (t.Is("#"))
            {
                var timed = new TimedStatement { Location = t.Location, Control = ParseDelay() };
                timed.Body = ParseStatement();
                return timed;
            }

            if (t.Is("@"))
            {
                var timed = new TimedStatement { Location = t.Location, Control = ParseEventControl() };
                timed.Body = ParseStatement();
                return timed;
            }

            if (t.Is(";"))
            {
                Next();
                return new NullStatement { Location = t.Location, Comment = t.TrailingComment };
            }

            if (t.Is("->") || t.Is("->>"))
            {
                Next();
                var trigger = new EventTriggerStatement { Location = t.Location, Nonblocking = t.Text == "->>", Target = ParseLValue() };
                if (!Expect(";", $"after {t.Text} and its event"))
                    SkipStatementEnd();
                trigger.Comment = Previous.TrailingComment;
                return trigger;
            }

            // A label before a statement: check: assert (...), name: begin ... end, name: fork ... join.
            if (t.Kind == TokenKind.Identifier && Peek(1).Is(":") && Peek(2).Kind == TokenKind.Keyword)
            {
                switch (Peek(2).Text)
                {
                    case "assert":
                    case "assume":
                    case "cover":
                        Next();
                        Next();
                        return ParseAssertion(t.Text);

                    case "begin":
                    {
                        Next();
                        Next();
                        BlockStatement block = ParseBlock();
                        block.Label = block.Label ?? t.Text;
                        return block;
                    }

                    case "fork":
                        Next();
                        Next();
                        return ParseFork(t.Text);
                }
            }

            if (t.Is("++") || t.Is("--"))
            {
                Next();
                var increment = new IncrementStatement { Location = t.Location, Operator = t.Text, Prefix = true, Target = ParseLValue() };
                Expect(";", $"after {t.Text}");
                increment.Comment = Previous.TrailingComment;
                return increment;
            }

            if (t.Kind == TokenKind.SystemName)
                return ParseCallStatement();

            if (t.Kind == TokenKind.Identifier || t.Is("{"))
                return ParseAssignmentOrCall();

            Error(t.Location, DiagnosticCodes.Expected, $"Expected a statement here, but found {Describe(t)}.");
            if (!AtHardStop && !Current.IsKeyword("end"))
                Next();
            return new NullStatement { Location = t.Location };
        }

        /// <summary><c>fork [: name] declarations statements join | join_any | join_none [: name]</c>: each statement a branch.</summary>
        private ForkStatement ParseFork(string label)
        {
            var fork = new ForkStatement { Location = Next().Location, Label = label };
            if (Accept(":"))
                fork.Label = ExpectIdentifier("the fork's name");

            while (!AtEnd && !AtHardStop && !Current.IsKeyword("join") && !Current.IsKeyword("join_any") &&
                   !Current.IsKeyword("join_none") && !Current.IsKeyword("end"))
            {
                int start = _at;

                if (IsDeclarationStart())
                    ParseLocalDeclaration(fork.Declarations);
                else
                    fork.Branches.Add(ParseStatement());

                if (_at == start && !AtEnd)
                    Next();
            }

            if (Current.IsKeyword("join") || Current.IsKeyword("join_any") || Current.IsKeyword("join_none"))
            {
                fork.Join = Next().Text;
                if (Accept(":"))
                {
                    string closing = ExpectIdentifier($"the fork's name after {fork.Join}");
                    if (closing != null && closing != fork.Label)
                        _diagnostics.Warning(Previous.Location, DiagnosticCodes.MismatchedLabel, $"'{fork.Join} : {closing}' closes fork {fork.Label ?? "(unnamed)"}.");
                }
            }
            else
            {
                Error(Current.Location, DiagnosticCodes.Expected, $"Expected join, join_any or join_none to close the fork, but found {Describe(Current)}.");
            }

            return fork;
        }

        /// <summary>
        /// <c>assert (c) pass; else fail;</c>, <c>assume</c> and <c>cover</c>, immediate. A concurrent one --
        /// <c>assert property</c> -- and a deferred one -- <c>assert #0</c>, <c>assert final</c> -- are refused.
        /// </summary>
        private Statement ParseAssertion(string label)
        {
            Token keyword = Current;
            Token after = Peek(1);
            if (after.IsKeyword("property") || after.IsKeyword("sequence"))
                return SkipUnsupportedStatement($"Concurrent assertions ({keyword.Text} property) are not supported by BitSorter; an immediate one, {keyword.Text} (...), is", null);
            if (after.Is("#") || after.IsKeyword("final"))
                return SkipUnsupportedStatement($"Deferred assertions ({keyword.Text} #0, {keyword.Text} final) are not supported by BitSorter yet; an immediate one, {keyword.Text} (...), is", null);

            Next();
            var assertion = new AssertionStatement { Location = keyword.Location, Keyword = keyword.Text, Label = label };

            if (Expect("(", $"after '{keyword.Text}'"))
            {
                assertion.Condition = ParseExpression();
                Expect(")", $"to close what {keyword.Text} checks");
            }
            else
            {
                assertion.Condition = new ErrorExpression { Location = Current.Location };
            }

            // The pass action may be a lone ; and the else still follow it (IEEE 1800, 16.3).
            if (!Accept(";") && !Current.IsKeyword("else"))
                assertion.Pass = ParseStatement();

            if (keyword.Text != "cover" && AcceptKeyword("else"))
                assertion.Fail = ParseStatement();

            return assertion;
        }

        private UnsupportedStatement SkipUnsupportedStatement(string message, string end)
        {
            UnsupportedItem skipped = SkipUnsupported(message, end);
            return new UnsupportedStatement { Location = skipped.Location, What = skipped.What, Text = skipped.Text };
        }

        private BlockStatement ParseBlock()
        {
            var block = new BlockStatement { Location = Current.Location };
            Next();

            if (Accept(":"))
                block.Label = ExpectIdentifier("the block's name");

            while (!AtEnd && !Current.IsKeyword("end") && !AtHardStop)
            {
                int start = _at;

                if (IsDeclarationStart())
                    ParseLocalDeclaration(block.Declarations);
                else
                    block.Statements.Add(ParseStatement());

                if (_at == start && !AtEnd)
                    Next();
            }

            if (ExpectKeyword("end", block.Label != null ? $"to close block {block.Label}" : "to close the 'begin'") && Accept(":"))
            {
                string label = ExpectIdentifier("the block's name after 'end :'");
                if (label != null && label != block.Label)
                    _diagnostics.Warning(Previous.Location, DiagnosticCodes.MismatchedLabel, $"'end : {label}' closes block {block.Label ?? "(unnamed)"}.");
            }

            return block;
        }

        private IfStatement ParseIf(string qualifier)
        {
            var statement = new IfStatement { Location = Current.Location, Qualifier = qualifier };
            Next();

            if (Expect("(", "after 'if'"))
            {
                statement.Condition = ParseExpression();
                Expect(")", "to close the condition");
            }
            else
            {
                statement.Condition = new ErrorExpression { Location = Current.Location };
            }

            statement.Then = ParseStatement();

            if (AcceptKeyword("else"))
                statement.Else = ParseStatement();

            return statement;
        }

        private CaseStatement ParseCase(string qualifier)
        {
            var statement = new CaseStatement { Location = Current.Location, Qualifier = qualifier, Keyword = Next().Text };

            if (Expect("(", $"after '{statement.Keyword}'"))
            {
                statement.Subject = ParseExpression();
                Expect(")", "to close what the case looks at");
            }
            else
            {
                statement.Subject = new ErrorExpression { Location = Current.Location };
            }

            if (Current.IsKeyword("inside"))
            {
                Next();
                statement.Inside = true;
            }
            else if (Current.IsKeyword("matches"))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, $"'case ... {Current.Text}' is not supported by BitSorter.");
                Next();
            }

            while (!AtEnd && !Current.IsKeyword("endcase") && !Current.IsKeyword("endmodule") && !Current.IsKeyword("endfunction") && !Current.IsKeyword("endtask"))
            {
                int start = _at;
                var item = new CaseItem { Location = Current.Location };

                if (AcceptKeyword("default"))
                {
                    Accept(":");
                }
                else
                {
                    do
                    {
                        item.Labels.Add(statement.Inside ? ParseSetMember() : ParseExpression());
                    }
                    while (Accept(","));

                    Expect(":", "after the case value");
                }

                item.Body = ParseStatement();
                statement.Items.Add(item);

                if (_at == start && !AtEnd)
                    Next();
            }

            ExpectKeyword("endcase", $"to close the {statement.Keyword}");
            return statement;
        }

        /// <summary><c>foreach (mem[i, j]) ...</c>: the array's name, then a variable a dimension, a place left empty to skip one.</summary>
        private ForeachStatement ParseForeach()
        {
            var loop = new ForeachStatement { Location = Next().Location };
            Expect("(", "after 'foreach'");

            var array = new NameExpression { Location = Current.Location };
            array.Path.Add(ExpectIdentifier("the array foreach walks") ?? "?");
            while (Current.Is(".") && Peek(1).Kind == TokenKind.Identifier)
            {
                Next();
                array.Path.Add(Next().Text);
            }

            loop.Array = array;

            if (Expect("[", "to give foreach its loop variables, as foreach (mem[i])"))
            {
                do
                {
                    loop.Variables.Add(Current.Kind == TokenKind.Identifier ? Next().Text : null);
                }
                while (Accept(","));

                Expect("]", "to close foreach's loop variables");
            }

            Expect(")", "to close foreach's array");
            loop.Body = ParseStatement();
            return loop;
        }

        private ForStatement ParseFor()
        {
            var loop = new ForStatement { Location = Current.Location };
            Next();

            if (!Expect("(", "after 'for'"))
            {
                loop.Body = new NullStatement { Location = Current.Location };
                return loop;
            }

            if (!Current.Is(";"))
            {
                do
                {
                    var initializer = new ForInitializer { Location = Current.Location };

                    if (IsDataTypeStart(Current) || (Current.Kind == TokenKind.Identifier && (_typeNames.Contains(Current.Text) || Peek(1).Kind == TokenKind.Identifier)))
                        initializer.Type = ParseDataType();

                    initializer.Target = ParseLValue();
                    if (Expect("=", "to give the loop variable its first value"))
                        initializer.Value = ParseExpression();

                    loop.Initializers.Add(initializer);
                }
                while (Accept(","));
            }

            Expect(";", "after the loop's start");

            if (!Current.Is(";"))
                loop.Condition = ParseExpression();

            Expect(";", "after the loop's condition");

            if (!Current.Is(")"))
            {
                do
                {
                    loop.Steps.Add(ParseStep());
                }
                while (Accept(","));
            }

            Expect(")", "to close the loop's header");
            loop.Body = ParseStatement();
            return loop;
        }

        /// <summary>A for loop's step: <c>i++</c>, <c>++i</c>, <c>i += 2</c> or <c>i = i + 1</c>, with no <c>;</c>.</summary>
        private Statement ParseStep()
        {
            Token t = Current;

            if (Accept("++") || Accept("--"))
                return new IncrementStatement { Location = t.Location, Operator = t.Text, Prefix = true, Target = ParseLValue() };

            Expression target = ParseLValue();

            if (Current.Is("++") || Current.Is("--"))
                return new IncrementStatement { Location = t.Location, Operator = Next().Text, Target = target };

            if (IsAssignmentOperator(Current))
                return new AssignmentStatement { Location = t.Location, Target = target, Operator = Next().Text, Value = ParseExpression() };

            Error(Current.Location, DiagnosticCodes.Expected, $"Expected the loop's step -- i++ or i = i + 1 -- but found {Describe(Current)}.");
            return new NullStatement { Location = t.Location };
        }

        private static bool IsAssignmentOperator(Token token) =>
            token.Kind == TokenKind.Symbol && (token.Text == "=" || token.Text == "<=" || token.Text == "+=" || token.Text == "-=" ||
            token.Text == "*=" || token.Text == "/=" || token.Text == "%=" || token.Text == "&=" || token.Text == "|=" ||
            token.Text == "^=" || token.Text == "<<=" || token.Text == ">>=" || token.Text == "<<<=" || token.Text == ">>>=");

        private Statement ParseAssignmentOrCall()
        {
            Token start = Current;
            Expression target = ParseLValue();

            // A task or function called as a statement: send(x); or send;
            if (target is CallExpression call && Current.Is(";"))
            {
                Next();
                var statement = new CallStatement { Location = call.Location, Name = call.Name, HasParentheses = call.HasParentheses };
                statement.Arguments.AddRange(call.Arguments);
                statement.Comment = Previous.TrailingComment;
                return statement;
            }

            if (target is NameExpression name && name.Path.Count == 1 && Current.Is(";"))
            {
                Next();
                return new CallStatement { Location = name.Location, Name = name.Name, Comment = Previous.TrailingComment };
            }

            if (Current.Is("++") || Current.Is("--"))
            {
                var increment = new IncrementStatement { Location = start.Location, Operator = Next().Text, Target = target };
                Expect(";", $"after {increment.Operator}");
                increment.Comment = Previous.TrailingComment;
                return increment;
            }

            if (IsAssignmentOperator(Current))
            {
                var assignment = new AssignmentStatement { Location = start.Location, Target = target, Operator = Next().Text };

                if (Current.Is("#"))
                    assignment.IntraAssignment = ParseDelay();
                else if (Current.Is("@"))
                    assignment.IntraAssignment = ParseEventControl();

                assignment.Value = ParseExpression();

                if (!Expect(";", "at the end of this assignment"))
                    SkipStatementEnd();

                assignment.Comment = Previous.TrailingComment;
                return assignment;
            }

            if (Current.Is("=="))
            {
                Error(Current.Location, DiagnosticCodes.Expected, "'==' compares; to assign, write '=' (or '<=' in always_ff).");
                SkipStatementEnd();
                return new NullStatement { Location = start.Location };
            }

            Error(Current.Location, DiagnosticCodes.Expected,
                $"Expected '=' or '<=' to make this an assignment, but found {Describe(Current)}.");
            SkipStatementEnd();
            return new NullStatement { Location = start.Location };
        }

        /// <summary>After an error in a statement: past its <c>;</c>, without running into what follows a block.</summary>
        private void SkipStatementEnd()
        {
            while (!AtEnd && !AtHardStop && !Current.IsKeyword("end"))
            {
                if (Accept(";"))
                    return;
                Next();
            }
        }

        private CallStatement ParseCallStatement()
        {
            Token name = Next();
            var call = new CallStatement { Location = name.Location, Name = name.Text };

            if (Current.Is("("))
            {
                call.HasParentheses = true;
                ParseArguments(call.Arguments, out bool _, name.Text);
            }

            if (!Expect(";", $"after {name.Text}"))
                SkipStatementEnd();

            call.Comment = Previous.TrailingComment;
            return call;
        }

        /// <summary><c>@(posedge clk, posedge rst)</c>, <c>@(a or b)</c>, <c>@*</c>, <c>@(*)</c>, <c>@clk</c>.</summary>
        private EventControl ParseEventControl()
        {
            var control = new EventControl { Location = Current.Location };
            Next();

            if (Accept("*"))
            {
                control.Implicit = true;
                return control;
            }

            if (!Current.Is("("))
            {
                control.Bare = true;
                control.Events.Add(new EventExpression { Location = Current.Location, Value = ParseLValue() });
                return control;
            }

            Next();

            if (Accept("*"))
            {
                control.Implicit = true;
                Expect(")", "to close @(*)");
                return control;
            }

            while (true)
            {
                var ev = new EventExpression { Location = Current.Location };

                if (Current.IsKeyword("posedge") || Current.IsKeyword("negedge") || Current.IsKeyword("edge"))
                    ev.Edge = Next().Text;

                ev.Value = ParseExpression();

                if (Current.IsKeyword("iff"))
                {
                    Error(Current.Location, DiagnosticCodes.Unsupported, "'iff' in an event is not supported by BitSorter.");
                    Next();
                    ParseExpression();
                }

                control.Events.Add(ev);

                if (AcceptKeyword("or"))
                {
                    control.UsesOr = true;
                    continue;
                }

                if (Accept(","))
                    continue;

                break;
            }

            Expect(")", "to close the event list");
            return control;
        }
    }
}
