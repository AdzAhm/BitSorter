using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>SystemVerilog's built-in gate primitives (IEEE 1800, 28.4 to 28.6).</summary>
    public enum GateKind
    {
        And, Nand, Or, Nor, Xor, Xnor,

        /// <summary><c>buf</c>: 0 and 1 pass; x and z give x.</summary>
        Buf,

        /// <summary><c>not</c>.</summary>
        Not,

        /// <summary><c>bufif1 (out, data, control)</c>: the data when control is 1, z when it is 0.</summary>
        Bufif1,

        /// <summary><c>bufif0</c>: the data when control is 0, z when it is 1.</summary>
        Bufif0,

        /// <summary><c>notif1</c>: the data inverted when control is 1, z when it is 0.</summary>
        Notif1,

        /// <summary><c>notif0</c>: the data inverted when control is 0, z when it is 1.</summary>
        Notif0,
    }

    /// <summary>What each primitive computes from its one-bit inputs.</summary>
    public static class Gates
    {
        /// <summary>How many inputs a primitive takes: at least two for the logic gates, one for buf and not, two for the tri-states.</summary>
        public static bool AcceptsInputCount(GateKind kind, int count)
        {
            switch (kind)
            {
                case GateKind.Buf:
                case GateKind.Not:
                    return count == 1;
                case GateKind.Bufif1:
                case GateKind.Bufif0:
                case GateKind.Notif1:
                case GateKind.Notif0:
                    return count == 2;
                default:
                    return count >= 2;
            }
        }

        /// <summary>
        /// The gate's output for these inputs. The logic gates fold the standard's truth tables over
        /// their inputs, so a known 0 still decides an AND. A tri-state whose control is x or z gives
        /// x: the standard's L and H, "0 or z" and "1 or z", need strengths, which are not modelled.
        /// </summary>
        public static LogicVec Evaluate(GateKind kind, LogicVec[] values, int[] inputs)
        {
            var gathered = new LogicVec[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
                gathered[i] = values[inputs[i]];
            return Evaluate(kind, gathered);
        }

        /// <summary>The gate's output for its inputs' values, in order: data first, then a tri-state's control.</summary>
        public static LogicVec Evaluate(GateKind kind, LogicVec[] inputs)
        {
            switch (kind)
            {
                case GateKind.Buf: return Buffer(inputs[0]);
                case GateKind.Not: return LogicVec.Not(inputs[0]);
                case GateKind.Bufif1: return TriState(inputs[0], inputs[1], enableOn: Logic4.One, invert: false);
                case GateKind.Bufif0: return TriState(inputs[0], inputs[1], enableOn: Logic4.Zero, invert: false);
                case GateKind.Notif1: return TriState(inputs[0], inputs[1], enableOn: Logic4.One, invert: true);
                case GateKind.Notif0: return TriState(inputs[0], inputs[1], enableOn: Logic4.Zero, invert: true);
            }

            LogicVec result = inputs[0];
            for (int i = 1; i < inputs.Length; i++)
            {
                LogicVec next = inputs[i];
                switch (kind)
                {
                    case GateKind.And: case GateKind.Nand: result = LogicVec.And(result, next); break;
                    case GateKind.Or: case GateKind.Nor: result = LogicVec.Or(result, next); break;
                    default: result = LogicVec.Xor(result, next); break;
                }
            }

            bool inverted = kind == GateKind.Nand || kind == GateKind.Nor || kind == GateKind.Xnor;
            return inverted ? LogicVec.Not(result) : Buffer(result);
        }

        /// <summary>0 and 1 unchanged; x and z both x. Two inversions do exactly that.</summary>
        private static LogicVec Buffer(LogicVec v) => LogicVec.Not(LogicVec.Not(v));

        private static LogicVec TriState(LogicVec data, LogicVec control, Logic4 enableOn, bool invert)
        {
            Logic4 c = control[0];

            if (c == enableOn)
                return invert ? LogicVec.Not(data) : Buffer(data);

            if (c == Logic4.Zero || c == Logic4.One)
                return LogicVec.Fill(Logic4.Z, 1);

            return LogicVec.Fill(Logic4.X, 1);
        }
    }
}
