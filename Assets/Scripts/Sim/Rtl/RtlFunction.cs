using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// A call of a function written in the design, opened where it is called (Part 15h-2): its statements run symbolically as a
    /// process's are, its arguments the nets the call gives it, and its result the net the call's value is -- so a function is
    /// drawn as the circuit it describes, once for each call, as a synthesis tool inlines one.
    /// </summary>
    /// <remarks>
    /// **What the engine does is what is opened** (<see cref="FunctionRunner"/>): the arguments shaped as the function's inputs
    /// hold them; an automatic function, or a call that comes back into one already running, starts its own signals afresh; a
    /// write lands only in the function's own signals; anything a function cannot do ends the call.
    ///
    /// **What is no circuit stays a box, with why**: a static function that reads one of its own signals before writing it,
    /// which gives what its last call left; a call inside a call past <see cref="MostCalls"/> deep, as a recursion with no
    /// constant end is; and, in a continuous assignment, a function whose statements read the module's signals -- the engine
    /// works the assignment out again only when an argument changes, so the result goes stale when the signal does, which no
    /// wire does. Inside a process the same goes stale only where its wait leaves the signal out, and the process says so.
    /// </remarks>
    internal sealed partial class RtlBuilder
    {
        /// <summary>How many calls may be opened each inside the last: a recursion deeper than this has no constant end.</summary>
        internal const int MostCalls = 32;

        // The calls being opened, outermost first; and the signals of the innermost that may not be read before written.
        private readonly List<FunctionInfo> _calling = new List<FunctionInfo>();
        private HashSet<int> _unbound;

        /// <summary>The net of a call's value: the function opened into cells, or, where it is no circuit, the call as a box.</summary>
        public int Call(FunctionInfo function, Expr[] arguments, Expr call, int width, bool signed)
        {
            var given = new int[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
                given[i] = Of(arguments[i]);

            if (_overlay != null)
                return Inline(function, given, width, signed);

            // In a continuous assignment: opened if it can be, the call a box otherwise, nothing made on the way kept.
            if (function.BodyReads != null && function.BodyReads.Length > 0)
                return Opaque(call);
            int cellsBefore = _cells.Count, netsBefore = _nets.Count;
            _steps = 0;
            try
            {
                _overlay = new Dictionary<int, int>();
                return Inline(function, given, width, signed);
            }
            catch (NotHardware)
            {
                _overlay = null;
                Rollback(cellsBefore, netsBefore);
                return Opaque(call);
            }
            finally
            {
                _overlay = null;
            }
        }

        private int Inline(FunctionInfo f, int[] given, int width, bool signed)
        {
            if (_calling.Count >= MostCalls)
                throw new NotHardware("It calls " + ShortName(f) + " inside itself more than " + MostCalls + " deep, with no constant end: no fixed circuit to draw.");

            // What the call sees: everything the caller has written so far, and its own signals as the engine starts them.
            var first = new Path { Pc = 0, Now = new Dictionary<int, int>(_overlay) };
            HashSet<int> unbound = null;
            if (f.Automatic || _calling.Contains(f))
            {
                for (int i = 0; i < f.Locals.Length; i++)
                    first.Now[f.Locals[i]] = Constant(FunctionRunner.Start(f.LocalInfo[i]));
            }
            else
            {
                unbound = new HashSet<int>(f.Locals);
                foreach (int local in f.Locals)
                    first.Now.Remove(local);
            }
            for (int i = 0; i < f.Inputs.Length && i < given.Length; i++)
            {
                first.Now[f.Inputs[i]] = ShapeAs(given[i], f.Inputs[i]);
                unbound?.Remove(f.Inputs[i]);
            }

            HashSet<int> savedUnbound = _unbound;
            _unbound = unbound;
            _calling.Add(f);
            Dictionary<int, int> now;
            try
            {
                Run(f.Code, 0, f.Code.Length, out now, out _, f, first);
            }
            finally
            {
                _calling.RemoveAt(_calling.Count - 1);
                _unbound = savedUnbound;
            }

            if (f.Result < 0)
                return Constant(signed ? LogicVec.AllX(width).AsSigned() : LogicVec.AllX(width));
            if (!now.TryGetValue(f.Result, out int result))
                throw new NotHardware(ShortName(f) + " does not give its result on every path, so it gives what its last call left: a static function's memory, not a circuit. Is it meant to be automatic?");

            // At the call's width, extended by the result's own sign, then as signed as the call is.
            bool resultSigned = _design.Signals[f.Result].Signed;
            if (_nets[result].Width != width)
                result = Resize(result, width, resultSigned);
            return resultSigned == signed ? result : Signedness(result, signed, width);
        }

        /// <summary>A value as <paramref name="signal"/> holds it, as <c>Simulator.Shape</c> shapes one: at its width, plain bits extended, then its sign and type.</summary>
        private int ShapeAs(int value, int signal)
        {
            int width = _design.Signals[signal].Width;
            if (_nets[value].Width != width)
                value = Resize(value, width, false);
            return Splice(-1, value, 0, signal);
        }

        /// <summary>A static function's own signal read before this call wrote it: what its last call left, which no circuit holds.</summary>
        private void Unbound(int signal)
        {
            string function = _calling.Count > 0 ? ShortName(_calling[_calling.Count - 1]) : "A function";
            throw new NotHardware(function + " reads " + LastName(_design.Signals[signal].Name) + " before writing it, so it gives what its last call left: " +
                "a static function's memory, not a circuit. Is it meant to be automatic?");
        }

        private string ShortName(FunctionInfo f) => f.Name.StartsWith(_prefix, System.StringComparison.Ordinal) ? f.Name.Substring(_prefix.Length) : LastName(f.Name);

        private static string LastName(string name) => name.Substring(name.LastIndexOf('.') + 1);
    }
}
