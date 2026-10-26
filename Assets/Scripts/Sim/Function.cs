using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// A function, compiled to run inside an expression (Part L2): its statements as instructions, and
    /// the signals of its own -- its arguments, its result and its locals -- which it reads and writes
    /// in the same array of values as the design's.
    /// </summary>
    /// <remarks>
    /// **A function called in an expression runs where the expression is evaluated**, all at once, as the
    /// standard has a function (IEEE 1800, 13.4): no delay, no event control, no nonblocking write, nothing
    /// written but its own signals. So a continuous assignment, a condition or an <c>always_comb</c> can call
    /// one. **What a call waits on is its arguments**: a continuous assignment and <c>@*</c> are evaluated
    /// again when an argument changes, not when something the function's statements read does -- only an
    /// <c>always_comb</c> waits on those too (<see cref="BodyReads"/>; IEEE 1800, 9.2.2.2.1). Icarus agrees,
    /// so a function that reads a module signal it is not given goes stale in an <c>assign</c> there as here.
    ///
    /// **Its own signals are the design's own working variables** (<see cref="SignalInfo.Local"/>):
    /// written straight into the array of values, never recorded, and read by nothing outside it. A
    /// static function keeps them from one call to the next, as the standard's static functions do; an
    /// automatic one starts each call from their starting values.
    ///
    /// **A call that comes back into a function already running** -- recursion, which an automatic
    /// function may do -- keeps the outer call's own signals aside and puts them back after, so each call
    /// has its own. Only such a call copies anything.
    ///
    /// A function called as a statement, which may also print or write the design's signals, is not this:
    /// the elaborator writes its statements into the calling process, as it does a task's.
    /// </remarks>
    internal sealed class FunctionInfo
    {
        public FunctionInfo(string name) => Name = name;

        /// <summary>Its full name: <c>alu.parity</c>.</summary>
        public string Name { get; }

        public Instruction[] Code = Array.Empty<Instruction>();

        /// <summary>The input arguments' signals, in the order a call gives them.</summary>
        public int[] Inputs = Array.Empty<int>();

        /// <summary>The result's signal; -1 for a <c>void</c> function.</summary>
        public int Result = -1;

        /// <summary>Every signal of its own: arguments, result, locals and the compiler's working variables.</summary>
        public int[] Locals = Array.Empty<int>();

        /// <summary>Each of <see cref="Locals"/>' declarations, for writing it at its width and type and starting it again.</summary>
        public SignalInfo[] LocalInfo = Array.Empty<SignalInfo>();

        /// <summary>Whether each call starts from fresh locals (<c>function automatic</c>), or keeps them (the default, static).</summary>
        public bool Automatic;

        /// <summary>
        /// The design's signals the statements read, its own left out, and those the functions it calls
        /// read in turn: what an <c>always_comb</c> that calls it waits on, besides the arguments. Null
        /// while it is still being compiled.
        /// </summary>
        public int[] BodyReads;

        /// <summary>How many calls are running now: more than none means a call has come back in.</summary>
        internal int Depth;

        private int _frames;

        /// <summary>
        /// What one call costs on the machine's stack, in evaluations (<see cref="Expr.Height"/>): its statements' deepest
        /// expression, and the frames of the call itself -- the expression's, the call's and its statements' run. Worked out
        /// the first time it is called, since the code does not change after.
        /// </summary>
        internal int Frames
        {
            get
            {
                if (_frames == 0)
                {
                    int height = 0;
                    foreach (Instruction ins in Code)
                    {
                        height = Math.Max(height, Math.Max(Expr.HeightOf(ins.Value), Expr.HeightOf(ins.Other)));
                        foreach (LPiece piece in ins.Target ?? Array.Empty<LPiece>())
                            height = Math.Max(height, Math.Max(Expr.HeightOf(piece.Offset), Expr.HeightOf(piece.Word)));
                    }

                    _frames = height + FunctionRunner.CallFrames;
                }

                return _frames;
            }
        }

        private Dictionary<int, SignalInfo> _info;

        /// <summary>The declaration of one of its own signals, or null for a signal that is not its own.</summary>
        public SignalInfo InfoOf(int signal)
        {
            if (_info == null)
            {
                _info = new Dictionary<int, SignalInfo>(Locals.Length);
                for (int i = 0; i < Locals.Length; i++)
                    _info[Locals[i]] = LocalInfo[i];
            }

            return _info.TryGetValue(signal, out SignalInfo info) ? info : null;
        }

        public override string ToString() => Name;
    }

    /// <summary>Runs a <see cref="FunctionInfo"/>'s statements on an array of values.</summary>
    /// <remarks>
    /// **A call runs on the machine's own stack**, each call inside another, and each evaluation inside a call a frame
    /// deeper, so a function that calls itself with no way out would overflow it -- which nothing can catch: the editor, a
    /// desktop build or a browser's page would end there. So the calls under way hold at most <see cref="MaxFrames"/>
    /// frames between them, counted by what each costs (<see cref="FunctionInfo.Frames"/>) -- a call with a deep expression
    /// costs more than one with a shallow one -- and at most <see cref="MaxNesting"/> calls, across every function; a call
    /// past either gives x. Everything under one call from an expression runs at most <see cref="MaxSteps"/> instructions.
    /// Each is left in <see cref="Stopped"/> for the run to say (Part 13f). One run at a time, as the pure layers run.
    /// </remarks>
    internal static class FunctionRunner
    {
        /// <summary>
        /// The most instructions a call from an expression may run, the calls under it included: a loop that never ends
        /// stops here, with the result it had, rather than hanging the run. Far past any function a course writes.
        /// </summary>
        public const long MaxSteps = 10_000_000;

        /// <summary>How many calls may be under way at once, each inside the last, across every function. Far past any a course writes.</summary>
        public const int MaxNesting = 100;

        /// <summary>
        /// How many frames the calls under way may hold between them. A thread's stack of one megabyte -- the smallest any
        /// host gives -- overflowed at about a thousand, measured on .NET 10 with evaluations nested in a recursion; six
        /// hundred leaves the room the caller's own frames need, and a hundred calls of a one-line body fit it.
        /// </summary>
        public const int MaxFrames = 600;

        /// <summary>The frames a call costs besides its expressions: the call's expression, the call and the statements' run.</summary>
        internal const int CallFrames = 4;

        private static int _nesting;
        private static int _frames;
        private static long _steps;

        /// <summary>Why a call stopped short since this was last taken, in the run's words; null when none did.</summary>
        internal static string Stopped { get; private set; }

        /// <summary><see cref="Stopped"/>, taken: the run says it once.</summary>
        internal static string TakeStopped()
        {
            string stopped = Stopped;
            Stopped = null;
            return stopped;
        }

        /// <summary>The value an expression's call gives: the arguments' values in, the statements run, the result out.</summary>
        public static LogicVec Call(FunctionInfo f, LogicVec[] arguments, LogicVec[] values, int width, bool signed)
        {
            if (_nesting >= MaxNesting || _frames + f.Frames > MaxFrames)
            {
                Stopped ??= $"{f.Name} was called {_nesting} deep, each call inside the last, and the call past that gave x. " +
                    "Does each call come nearer to one that does not call again?";
                LogicVec x = LogicVec.AllX(width);
                return signed ? x.AsSigned() : x;
            }

            if (_nesting == 0)
                _steps = 0;

            LogicVec[] kept = null;
            if (f.Depth > 0)
            {
                // A call coming back in: the running call's own signals go aside until this one is done.
                kept = new LogicVec[f.Locals.Length];
                for (int i = 0; i < f.Locals.Length; i++)
                    kept[i] = values[f.Locals[i]];
            }

            if (f.Automatic || kept != null)
            {
                for (int i = 0; i < f.Locals.Length; i++)
                    values[f.Locals[i]] = Start(f.LocalInfo[i]);
            }

            for (int i = 0; i < f.Inputs.Length; i++)
            {
                SignalInfo info = f.InfoOf(f.Inputs[i]);
                values[f.Inputs[i]] = Simulator.Shape(arguments[i], info);
            }

            int frames = f.Frames;
            f.Depth++;
            _nesting++;
            _frames += frames;
            try
            {
                Run(f, values);
            }
            finally
            {
                f.Depth--;
                _nesting--;
                _frames -= frames;
            }

            LogicVec result = f.Result >= 0 ? values[f.Result] : LogicVec.AllX(width);
            result = result.Width == width ? result : result.Extend(width, result.IsSigned);
            result = signed ? result.AsSigned() : result.AsUnsigned();

            if (kept != null)
                for (int i = 0; i < f.Locals.Length; i++)
                    values[f.Locals[i]] = kept[i];

            return result;
        }

        /// <summary>A signal's value before anything writes it: what it was declared with, else 0 for a 2-state type and x for the rest.</summary>
        public static LogicVec Start(SignalInfo info) =>
            Simulator.Shape(info.Initial ?? LogicVec.AllX(info.Width), info);

        private static void Run(FunctionInfo f, LogicVec[] values)
        {
            Instruction[] code = f.Code;
            int pc = 0;

            while (pc < code.Length)
            {
                if (++_steps > MaxSteps)
                {
                    Stopped ??= $"{f.Name} ran {MaxSteps.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} steps in one call " +
                        "and was stopped there, with the result it had. Does a loop in it never end?";
                    return;
                }

                ref Instruction ins = ref code[pc];
                switch (ins.Op)
                {
                    case OpCode.Assign:
                        Store(f, values, ins.Target, ins.TargetWidth, ins.Value.Evaluate(values));
                        pc++;
                        break;

                    case OpCode.Jump:
                        pc = ins.Jump;
                        break;

                    case OpCode.JumpUnless:
                        pc = LogicVec.Truth(ins.Value.Evaluate(values)) == Logic4.One ? pc + 1 : ins.Jump;
                        break;

                    case OpCode.JumpIf:
                        pc = LogicVec.Truth(ins.Value.Evaluate(values)) == Logic4.One ? ins.Jump : pc + 1;
                        break;

                    case OpCode.JumpIfMatch:
                        pc = Matching.CaseMatches(ins.Value.Evaluate(values), ins.Other.Evaluate(values), ins.Case) ? ins.Jump : pc + 1;
                        break;

                    default:
                        // End, and anything the elaborator let through that a function cannot do.
                        return;
                }
            }
        }

        /// <summary>A blocking write to the function's own signals, piece by piece, as <c>Simulator.Store</c> writes a process's.</summary>
        private static void Store(FunctionInfo f, LogicVec[] values, LPiece[] target, int total, LogicVec value)
        {
            if (value.Width != total)
                value = value.Extend(total, false);

            int offset = total;
            foreach (LPiece piece in target)
            {
                offset -= piece.Width;
                LogicVec part = target.Length == 1 ? value : value.Slice(offset, piece.Width);

                int lowest = piece.Lowest;
                if (piece.Offset != null && !Simulator.TryOffset(piece.Offset, values, out lowest))
                    continue;

                SignalInfo info = f.InfoOf(piece.Signal);
                if (info == null || piece.Word != null || !Simulator.Clip(info.Width, ref lowest, ref part))
                    continue;   // the elaborator writes only its own whole signals and their bits

                values[piece.Signal] = Simulator.Shape(Simulator.Splice(values[piece.Signal], lowest, part), info);
            }
        }
    }

    public abstract partial class Expr
    {
        /// <summary>A call of a function written in the design, with its arguments, at the function's own width and signedness.</summary>
        internal static Expr Call(FunctionInfo function, Expr[] arguments, int width, bool signed) =>
            new CallExpr(function, arguments, width, signed);

        private sealed class CallExpr : Expr
        {
            private readonly FunctionInfo _function;
            private readonly Expr[] _arguments;
            private readonly LogicVec[] _given;
            private readonly int _width;
            private readonly bool _signed;

            public CallExpr(FunctionInfo function, Expr[] arguments, int width, bool signed)
            {
                _function = function ?? throw new ArgumentNullException(nameof(function));
                _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
                _given = new LogicVec[arguments.Length];
                _width = width;
                _signed = signed;
            }

            public override int Width => _width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                // Every argument first, in the caller's own values, before the function starts on its own.
                for (int i = 0; i < _arguments.Length; i++)
                    _given[i] = _arguments[i].Evaluate(values);

                return FunctionRunner.Call(_function, _given, values, _width, _signed);
            }

            /// <summary>The arguments' reads only: what the function's statements read is not an operand of the call (IEEE 1800, 9.2.2.2.1).</summary>
            public override void CollectReads(ICollection<int> reads)
            {
                foreach (Expr argument in _arguments)
                    argument.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                calls.Add(_function);
                foreach (Expr argument in _arguments)
                    argument.CollectCalls(calls);
            }

            internal override int Height => 1 + MaxHeight(_arguments);
        }
    }
}
