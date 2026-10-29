using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// An operator part or a mux opened into the textbook's gates (Part 15h-3): what a drawing's GATES switch shows in place
    /// of a small box -- bitwise and reductions as gates, add and subtract as full adders, a comparison as a subtractor's
    /// carry, a product as an array of adders, a shift as a barrel of muxes, a mux as AND-OR. Each is a module of its own,
    /// its ports the part's pins, so it compiles and runs as any drawing does.
    /// </summary>
    /// <remarks>
    /// **On 0s and 1s it computes what the box does**, every input checked (<c>RtlGatesTests</c>). With an x or a z the gates
    /// may know more than the operator, which gives x for a whole sum with one unknown bit where a ripple of gates still
    /// knows the bits below it -- real gates are not as cautious as the language's arithmetic.
    ///
    /// **What stays a box, and says why**: anything wider than <see cref="MostBits"/> bits, which would be too many gates to
    /// read; a divider and a remainder, a subtractor a bit for each bit of the answer; and <c>===</c> and <c>!==</c>, which
    /// compare x and z as values -- something no gate does.
    /// </remarks>
    public static class RtlGates
    {
        /// <summary>The widest operand opened into gates.</summary>
        public const int MostBits = 4;

        /// <summary>
        /// <paramref name="part"/> opened into gates, as a project whose top, <paramref name="name"/>, has the part's pins for
        /// its ports; null, with why, for one that stays a box.
        /// </summary>
        public static Project Open(Part part, string name, out string why)
        {
            why = null;
            switch (part)
            {
                case OperatorPart op:
                    if (op.Width > MostBits || op.WidthOfB > MostBits)
                    {
                        why = $"It is {Bits(Math.Max(op.Width, op.WidthOfB))} wide: past {MostBits} bits {Operators.Called(op.Op)} is too many gates to read, so it stays a box.";
                        return null;
                    }
                    if (op.Op == OperatorKind.Divide || op.Op == OperatorKind.Modulo)
                    {
                        why = $"{Capital(Operators.Called(op.Op))} is a subtractor for each bit of its answer, too many gates to read: it stays a box.";
                        return null;
                    }
                    if (op.Op == OperatorKind.CaseEqual || op.Op == OperatorKind.CaseNotEqual)
                    {
                        why = $"{Operators.Token(op.Op)} compares x and z as values, which no gate does: it stays a box.";
                        return null;
                    }
                    break;

                case MuxPart mux:
                    if (mux.Width > MostBits)
                    {
                        why = $"It is {Bits(mux.Width)} wide: past {MostBits} bits a mux is too many gates to read, so it stays a box.";
                        return null;
                    }
                    break;

                default:
                    why = "Only an operator or a mux opens into gates.";
                    return null;
            }

            var gates = new Builder(name, part);
            if (part is OperatorPart o)
                gates.Operator(o);
            else
                gates.Mux((MuxPart)part);
            return gates.Project;
        }

        private static string Bits(int width) => width == 1 ? "1 bit" : width + " bits";

        private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>A place a wire starts from: a part's output pin.</summary>
        private readonly struct Wire
        {
            public Wire(int part, string pin)
            {
                Part = part;
                Pin = pin;
            }

            public readonly int Part;
            public readonly string Pin;
        }

        /// <summary>The gates of one opening, added through the editor, so each is held to the rules a drawing is.</summary>
        private sealed class Builder
        {
            private readonly BoardEditor _editor;
            private readonly string _module;
            private readonly Dictionary<string, Wire[]> _bits = new Dictionary<string, Wire[]>();
            private readonly int[] _rows = new int[64];
            private Wire? _zero, _one;
            private int _column = 2;

            public Builder(string name, Part part)
            {
                _module = name;
                _editor = new BoardEditor(Project.New(name));

                // The part's pins as ports: its inputs down the left, each a bus taken apart, its output on the right.
                int row = 0;
                foreach (Pin pin in part.Pins(null))
                {
                    if (pin.Direction == PinDirection.Out)
                        continue;
                    int port = Put(new PortPart { Name = pin.Name, Width = pin.Width }, 0, row);
                    row += pin.Width + 1;
                    if (pin.Width == 1)
                    {
                        _bits[pin.Name] = new[] { new Wire(port, "out") };
                        continue;
                    }

                    int split = Put(new SplitterPart { Width = pin.Width }, 1, row - pin.Width - 1);
                    Connect(new Wire(port, "out"), split, "in");
                    var bits = new Wire[pin.Width];
                    for (int i = 0; i < pin.Width; i++)
                        bits[i] = new Wire(split, "o" + i);
                    _bits[pin.Name] = bits;
                }
            }

            public Project Project => _editor.Project;

            private Wire[] In(string pin) => _bits[pin];

            private int Put(Part part, int column, int row) =>
                Must(_editor.Add(_module, part, new Placement(new Point(column * 4, -row * 2))));

            private void Connect(Wire from, int to, string pin) => Must(_editor.Connect(_module, Endpoint.At(from.Part, from.Pin), Endpoint.At(to, pin)));

            private static int Must(EditResult result)
            {
                if (!result.Done)
                    throw new InvalidOperationException("RtlGates made a part the board refuses: " + result.Refusal);
                return result.Id;
            }

            /// <summary>A gate on <paramref name="inputs"/>, in the next free row of the column after the latest of them.</summary>
            private Wire Gate(GateKind kind, params Wire[] inputs)
            {
                int column = _column;
                int id = Put(new GatePart { Kind = kind, Inputs = inputs.Length }, column, _rows[column]++);
                for (int i = 0; i < inputs.Length; i++)
                    Connect(inputs[i], id, GatePart.InputName(i));
                return new Wire(id, "y");
            }

            private Wire Not(Wire a) => Gate(GateKind.Not, a);

            private Wire Zero => _zero ??= new Wire(Put(new ConstantPart { Value = LogicVec.FromUInt64(0, 1) }, 1, _rows[1]++ + 20), "y");

            private Wire One => _one ??= new Wire(Put(new ConstantPart { Value = LogicVec.FromUInt64(1, 1) }, 1, _rows[1]++ + 20), "y");

            /// <summary>The next column: a stage of gates after the last.</summary>
            private void Stage() => _column++;

            /// <summary>The answer's bits joined into y.</summary>
            private void Out(params Wire[] bits)
            {
                int column = _column + 1;
                int y = Put(new PortPart { Name = "y", Direction = PortDirection.Output, Width = bits.Length }, column + 1, 0);
                if (bits.Length == 1)
                {
                    Connect(bits[0], y, "in");
                    return;
                }

                int join = Put(new JoinerPart { Width = bits.Length }, column, 0);
                for (int i = 0; i < bits.Length; i++)
                    Connect(bits[i], join, "i" + i);
                Connect(new Wire(join, "out"), y, "in");
            }

            /// <summary>
            /// Two numbers added a bit at a time, the textbook's ripple: a half adder where there is no carry in, then full
            /// adders -- the sum <c>x ^ y ^ c</c> and the carry <c>x&amp;y | c&amp;(x^y)</c>. The sums, and the carry out of the top.
            /// </summary>
            private Wire[] Ripple(Wire[] x, Wire[] y, Wire? carryIn, out Wire carry)
            {
                var sums = new Wire[x.Length];
                Wire? c = carryIn;
                for (int i = 0; i < x.Length; i++)
                {
                    Wire half = Gate(GateKind.Xor, x[i], y[i]);
                    Wire both = Gate(GateKind.And, x[i], y[i]);
                    if (c == null)
                    {
                        sums[i] = half;
                        c = both;
                    }
                    else
                    {
                        sums[i] = Gate(GateKind.Xor, half, c.Value);
                        c = Gate(GateKind.Or, both, Gate(GateKind.And, c.Value, half));
                    }
                    Stage();
                }

                carry = c.Value;
                return sums;
            }

            public void Mux(MuxPart mux)
            {
                // Each input's own line, decoded from the select; then each bit an OR of each input's bit ANDed with its line.
                Wire[] s = In("s");
                var nots = new Wire[s.Length];
                for (int j = 0; j < s.Length; j++)
                    nots[j] = Not(s[j]);
                Stage();
                var lines = new Wire[mux.Inputs];
                for (int k = 0; k < mux.Inputs; k++)
                {
                    var literals = new Wire[s.Length];
                    for (int j = 0; j < s.Length; j++)
                        literals[j] = ((k >> j) & 1) == 1 ? s[j] : nots[j];
                    lines[k] = literals.Length == 1 ? literals[0] : Gate(GateKind.And, literals);
                }
                Stage();

                var y = new Wire[mux.Width];
                for (int i = 0; i < mux.Width; i++)
                {
                    var terms = new Wire[mux.Inputs];
                    for (int k = 0; k < mux.Inputs; k++)
                        terms[k] = Gate(GateKind.And, In("d" + k)[i], lines[k]);
                    y[i] = Gate(GateKind.Or, terms);
                }
                Out(y);
            }

            public void Operator(OperatorPart op)
            {
                Wire[] a = In("a");
                Wire[] b = Operators.IsUnary(op.Op) ? null : In("b");
                int w = a.Length;
                var y = new Wire[op.WidthOfY];
                bool signed = op.Signed && Operators.SignMatters(op.Op);

                switch (op.Op)
                {
                    case OperatorKind.And:
                    case OperatorKind.Or:
                    case OperatorKind.Xor:
                    case OperatorKind.Xnor:
                    {
                        GateKind kind = op.Op == OperatorKind.And ? GateKind.And : op.Op == OperatorKind.Or ? GateKind.Or
                            : op.Op == OperatorKind.Xor ? GateKind.Xor : GateKind.Xnor;
                        for (int i = 0; i < w; i++)
                            y[i] = Gate(kind, a[i], b[i]);
                        break;
                    }

                    case OperatorKind.Not:
                        for (int i = 0; i < w; i++)
                            y[i] = Not(a[i]);
                        break;

                    case OperatorKind.ReduceAnd:
                    case OperatorKind.ReduceOr:
                    case OperatorKind.ReduceXor:
                    case OperatorKind.ReduceNand:
                    case OperatorKind.ReduceNor:
                    case OperatorKind.ReduceXnor:
                    {
                        bool inverts = op.Op == OperatorKind.ReduceNand || op.Op == OperatorKind.ReduceNor || op.Op == OperatorKind.ReduceXnor;
                        GateKind kind = op.Op == OperatorKind.ReduceAnd ? GateKind.And : op.Op == OperatorKind.ReduceOr ? GateKind.Or
                            : op.Op == OperatorKind.ReduceXor ? GateKind.Xor : op.Op == OperatorKind.ReduceNand ? GateKind.Nand
                            : op.Op == OperatorKind.ReduceNor ? GateKind.Nor : GateKind.Xnor;
                        y[0] = w == 1 ? Gate(inverts ? GateKind.Not : GateKind.Buf, a[0]) : Gate(kind, a);
                        break;
                    }

                    case OperatorKind.LogicalNot:
                        y[0] = w == 1 ? Not(a[0]) : Gate(GateKind.Nor, a);
                        break;

                    case OperatorKind.LogicalAnd:
                    {
                        Wire anyA = w == 1 ? a[0] : Gate(GateKind.Or, a), anyB = b.Length == 1 ? b[0] : Gate(GateKind.Or, b);
                        Stage();
                        y[0] = Gate(GateKind.And, anyA, anyB);
                        break;
                    }

                    case OperatorKind.LogicalOr:
                    {
                        var all = new Wire[w + b.Length];
                        a.CopyTo(all, 0);
                        b.CopyTo(all, w);
                        y[0] = Gate(GateKind.Or, all);
                        break;
                    }

                    case OperatorKind.Equal:
                    case OperatorKind.NotEqual:
                    {
                        // A bit at a time alike, all of them; or unalike, any of them.
                        bool equal = op.Op == OperatorKind.Equal;
                        var bits = new Wire[w];
                        for (int i = 0; i < w; i++)
                            bits[i] = Gate(equal ? GateKind.Xnor : GateKind.Xor, a[i], b[i]);
                        Stage();
                        y[0] = w == 1 ? bits[0] : Gate(equal ? GateKind.And : GateKind.Or, bits);
                        break;
                    }

                    case OperatorKind.Add:
                        y = Ripple(a, b, null, out _);
                        break;

                    case OperatorKind.Subtract:
                    {
                        // a + ~b + 1: b inverted, and the 1 carried in.
                        var nb = new Wire[w];
                        for (int i = 0; i < w; i++)
                            nb[i] = Not(b[i]);
                        Stage();
                        y = Ripple(a, nb, One, out _);
                        break;
                    }

                    case OperatorKind.Negate:
                    {
                        // ~a + 1: a inverted, and 1 rippled up through half adders.
                        Wire c = One;
                        for (int i = 0; i < w; i++)
                        {
                            Wire na = Not(a[i]);
                            Stage();
                            y[i] = Gate(GateKind.Xor, na, c);
                            c = Gate(GateKind.And, na, c);
                        }
                        break;
                    }

                    case OperatorKind.Less:
                    case OperatorKind.LessOrEqual:
                    case OperatorKind.Greater:
                    case OperatorKind.GreaterOrEqual:
                    {
                        // x - z borrows when x < z: the carry out of x + ~z + 1 is 1 just when x >= z. Signed, each top bit
                        // inverted first, which puts the negatives below the rest in the same unsigned order.
                        bool swap = op.Op == OperatorKind.Greater || op.Op == OperatorKind.LessOrEqual;
                        Wire[] x = swap ? b : a, z = swap ? a : b;
                        var top = new Wire[w];
                        var nz = new Wire[w];
                        for (int i = 0; i < w; i++)
                        {
                            top[i] = signed && i == w - 1 ? Not(x[i]) : x[i];
                            nz[i] = signed && i == w - 1 ? z[i] : Not(z[i]);
                        }
                        Stage();
                        Ripple(top, nz, One, out Wire noBorrow);
                        y[0] = op.Op == OperatorKind.Less || op.Op == OperatorKind.Greater ? Not(noBorrow) : noBorrow;
                        break;
                    }

                    case OperatorKind.Multiply:
                    {
                        // The array multiplier: each row a's bits ANDed with one bit of b, shifted one place more and added in.
                        var sum = new Wire[w];
                        for (int j = 0; j < w; j++)
                            sum[j] = Gate(GateKind.And, a[j], b[0]);
                        Stage();
                        for (int i = 1; i < w; i++)
                        {
                            var row = new Wire[w - i];
                            var upper = new Wire[w - i];
                            for (int j = 0; j < w - i; j++)
                            {
                                row[j] = Gate(GateKind.And, a[j], b[i]);
                                upper[j] = sum[i + j];
                            }
                            Stage();
                            Wire[] added = Ripple(upper, row, null, out _);
                            for (int j = 0; j < w - i; j++)
                                sum[i + j] = added[j];
                        }
                        y = sum;
                        break;
                    }

                    default:
                        y = Shift(op, a, b, signed);
                        break;
                }

                Out(y);
            }

            /// <summary>
            /// A barrel shifter: a stage of muxes for each bit of the amount that moves a by less than its width, each passing a
            /// on or shifted by that bit's weight; and past them, any higher amount bit set gives every bit the fill -- 0, or
            /// a's sign for a signed <c>&gt;&gt;&gt;</c>.
            /// </summary>
            private Wire[] Shift(OperatorPart op, Wire[] a, Wire[] amount, bool signed)
            {
                int w = a.Length;
                bool left = op.Op == OperatorKind.ShiftLeft || op.Op == OperatorKind.ArithmeticShiftLeft;
                Wire fill = op.Op == OperatorKind.ArithmeticShiftRight && signed ? a[w - 1] : Zero;
                var current = (Wire[])a.Clone();
                int k = 0;
                for (; k < amount.Length && (1 << k) < w; k++)
                {
                    int by = 1 << k;
                    var next = new Wire[w];
                    for (int i = 0; i < w; i++)
                    {
                        int from = left ? i - by : i + by;
                        Wire shifted = from >= 0 && from < w ? current[from] : fill;
                        next[i] = Choose(current[i], shifted, amount[k]);
                    }
                    current = next;
                    Stage();
                }

                if (k == amount.Length)
                    return current;
                var high = new Wire[amount.Length - k];
                Array.Copy(amount, k, high, 0, high.Length);
                Wire over = high.Length == 1 ? high[0] : Gate(GateKind.Or, high);
                Stage();
                var result = new Wire[w];
                for (int i = 0; i < w; i++)
                    result[i] = Choose(current[i], fill, over);
                return result;
            }

            /// <summary>A two-input mux: <paramref name="whenOne"/> where <paramref name="select"/> is 1.</summary>
            private Wire Choose(Wire whenZero, Wire whenOne, Wire select)
            {
                int column = _column;
                int id = Put(new MuxPart { Inputs = 2, Width = 1 }, column, _rows[column]++);
                Connect(whenZero, id, "d0");
                Connect(whenOne, id, "d1");
                Connect(select, id, "s");
                return new Wire(id, "y");
            }
        }
    }
}
