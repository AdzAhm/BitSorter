using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The operator part (Part 15h-3): one of the language's operators as a part, compiling to one continuous assignment. Every
    /// kind runs here as the operator does on its own (<see cref="LogicVec"/>'s), x and z included; under Icarus the same board
    /// is run by <c>BoardOracleTests</c> with every sample, and saved, routed, printed and exported with them.
    /// </summary>
    public class OperatorPartTests
    {
        /// <summary>The operator as <see cref="LogicVec"/> has it, apart from the compiler: what the part must compute.</summary>
        internal static LogicVec Reference(OperatorKind op, LogicVec a, LogicVec b, bool signed)
        {
            if (signed)
            {
                a = a.AsSigned();
                if (!Operators.IsShift(op))
                    b = b.AsSigned();
            }

            switch (op)
            {
                case OperatorKind.Add: return LogicVec.Add(a, b);
                case OperatorKind.Subtract: return LogicVec.Subtract(a, b);
                case OperatorKind.Multiply: return LogicVec.Multiply(a, b);
                case OperatorKind.Divide: return LogicVec.Divide(a, b);
                case OperatorKind.Modulo: return LogicVec.Modulo(a, b);
                case OperatorKind.And: return LogicVec.And(a, b);
                case OperatorKind.Or: return LogicVec.Or(a, b);
                case OperatorKind.Xor: return LogicVec.Xor(a, b);
                case OperatorKind.Xnor: return LogicVec.Xnor(a, b);
                case OperatorKind.Less: return LogicVec.LessThan(a, b);
                case OperatorKind.LessOrEqual: return LogicVec.LessOrEqual(a, b);
                case OperatorKind.Greater: return LogicVec.GreaterThan(a, b);
                case OperatorKind.GreaterOrEqual: return LogicVec.GreaterOrEqual(a, b);
                case OperatorKind.Equal: return LogicVec.Equal(a, b);
                case OperatorKind.NotEqual: return LogicVec.NotEqual(a, b);
                case OperatorKind.CaseEqual: return LogicVec.CaseEqual(a, b);
                case OperatorKind.CaseNotEqual: return LogicVec.CaseNotEqual(a, b);
                case OperatorKind.LogicalAnd: return LogicVec.LogicalAnd(a, b);
                case OperatorKind.LogicalOr: return LogicVec.LogicalOr(a, b);
                case OperatorKind.ShiftLeft: return LogicVec.ShiftLeft(a, b);
                case OperatorKind.ShiftRight: return LogicVec.ShiftRight(a, b);
                case OperatorKind.ArithmeticShiftLeft: return LogicVec.ArithmeticShiftLeft(a, b);
                case OperatorKind.ArithmeticShiftRight: return LogicVec.ArithmeticShiftRight(a, b);
                case OperatorKind.Negate: return LogicVec.Negate(a);
                case OperatorKind.Not: return LogicVec.Not(a);
                case OperatorKind.LogicalNot: return LogicVec.LogicalNot(a);
                case OperatorKind.ReduceAnd: return LogicVec.ReduceAnd(a);
                case OperatorKind.ReduceOr: return LogicVec.ReduceOr(a);
                case OperatorKind.ReduceXor: return LogicVec.ReduceXor(a);
                case OperatorKind.ReduceNand: return LogicVec.ReduceNand(a);
                case OperatorKind.ReduceNor: return LogicVec.ReduceNor(a);
                case OperatorKind.ReduceXnor: return LogicVec.ReduceXnor(a);
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        /// <summary>Every kind is the engine's operator of the same name, and back; the engine's others no part is.</summary>
        [Test]
        public void EveryOperator_IsTheEnginesOperatorOfTheSameName()
        {
            foreach (OperatorKind op in Operators.All)
            {
                if (Operators.TryBinary(op, out BinaryOp binary))
                {
                    Assert.IsTrue(Operators.TryFrom(binary, out OperatorKind back) && back == op, op.ToString());
                    Assert.IsFalse(Operators.IsUnary(op));
                }
                else
                {
                    Assert.IsTrue(Operators.TryUnary(op, out UnaryOp unary) && Operators.TryFrom(unary, out OperatorKind back) && back == op, op.ToString());
                }

                Assert.IsTrue(Operators.TryToken(Operators.Token(op), Operators.IsUnary(op), out OperatorKind read) && read == op,
                    $"{op} is read back from its token, {Operators.Token(op)}");
            }

            Assert.IsFalse(Operators.TryFrom(BinaryOp.WildcardEqual, out _), "==? is no part");
            Assert.IsFalse(Operators.TryFrom(UnaryOp.CountOnes, out _), "$countones is no part");
        }

        /// <summary>
        /// Every operator part, through every combination of a 3-bit a and b and a 2-bit amount and then x and z, gives what the
        /// operator gives on its own -- signed where a sign changes the answer.
        /// </summary>
        [Test]
        public void EveryOperatorPart_ComputesWhatItsOperatorDoes()
        {
            Project project = SampleBoards.Build("operators");
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            Design design = elaboration.Design;
            var sim = new Simulator(design);

            SignalId Signal(string name) => design.SignalAt(Enumerable.Range(0, design.Signals.Count).Single(i => design.Signals[i].Name == "operators_tb.dut." + name || design.Signals[i].Name == "operators." + name));

            SignalId a = Signal("a"), b = Signal("b"), s = Signal("s");
            var wrong = new List<string>();
            int compared = 0;
            for (int v = 0; v < 259; v++)
            {
                sim.RunTo(v * 10 + 9);
                LogicVec va = sim.Value(a), vb = sim.Value(b), vs = sim.Value(s);
                foreach (OperatorKind op in Operators.All)
                    foreach (bool signed in new[] { false, true })
                    {
                        if (signed && !Operators.SignMatters(op))
                            continue;
                        LogicVec expected = Reference(op, va, Operators.BOwnWidth(op) ? vs : vb, signed);
                        LogicVec got = sim.Value(Signal(SampleBoards.OperatorOutput(op, signed)));
                        compared++;
                        if (expected.Format(Radix.Binary) != got.Format(Radix.Binary))
                            wrong.Add($"{SampleBoards.OperatorOutput(op, signed)} at a={va.Format(Radix.Binary)} b={vb.Format(Radix.Binary)} s={vs.Format(Radix.Binary)}: " +
                                $"{got.Format(Radix.Binary)}, but the operator gives {expected.Format(Radix.Binary)}");
                    }
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong.Take(20)));
            Assert.Greater(compared, 259 * 30);
        }

        /// <summary>The code an operator prints as: one assignment, signed operands only where a sign changes the answer, a shift's amount never.</summary>
        [Test]
        public void AnOperatorPart_PrintsAsOneAssignment()
        {
            string code = string.Join("\n", BoardPrinter.Print(BoardCompiler.Compile(SampleBoards.Build("operators"))).Select(p => p.Text));
            StringAssert.Contains("$signed(a) < $signed(b)", code);
            StringAssert.Contains("$signed(a) >>> s", code);
            StringAssert.Contains("a + b", code);
            StringAssert.Contains("~&a", code);
            StringAssert.DoesNotContain("$signed(s)", code);
        }

        /// <summary>The rules: a width of b's own only where b may have one, a sign only where it changes the answer, and no operator in a gate library.</summary>
        [Test]
        public void AnOperatorPart_IsHeldToTheBoardsRules()
        {
            string Refused(OperatorPart part)
            {
                var editor = new BoardEditor(Project.New("m"));
                EditResult added = editor.Add("m", part, new Placement(0, 0));
                return added.Done ? null : added.Refusal;
            }

            Assert.IsNull(Refused(new OperatorPart { Op = OperatorKind.ShiftLeft, Width = 8, BWidth = 3 }));
            StringAssert.Contains("as wide as a", Refused(new OperatorPart { Op = OperatorKind.Add, Width = 8, BWidth = 3 }));
            StringAssert.Contains("signed or not", Refused(new OperatorPart { Op = OperatorKind.Add, Signed = true }));
            Assert.IsNull(Refused(new OperatorPart { Op = OperatorKind.Less, Signed = true }));
            Assert.IsNotNull(Refused(new OperatorPart { Op = OperatorKind.Add, Width = 0 }));
        }

        /// <summary>A shift's amount comes in on top, as an ALU's control does; every other operand on the left, the answer on the right.</summary>
        [Test]
        public void AShiftsAmount_ComesInOnTop()
        {
            PartOutline shift = PartGeometry.Local(null, new OperatorPart { Op = OperatorKind.ShiftLeft, BWidth = 2 });
            Assert.IsTrue(shift.TryPin("b", out PinSpot amount) && amount.Facing == Facing.Up);
            Assert.AreEqual(shift.Top, amount.At.Y, "on its top edge");
            PartOutline add = PartGeometry.Local(null, new OperatorPart { Op = OperatorKind.Add });
            Assert.IsTrue(add.TryPin("b", out PinSpot b) && b.Facing == Facing.Left);
            Assert.IsTrue(add.TryPin("y", out PinSpot y) && y.Facing == Facing.Right);
        }
    }
}
