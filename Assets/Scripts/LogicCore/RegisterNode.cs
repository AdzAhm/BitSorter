namespace BitSorter.LogicCore
{
    /// <summary>
    /// One bit of memory: emits the bit it starts with, then hands on every bit it is given and
    /// remembers it. A D flip-flop.
    /// </summary>
    /// <remarks>
    /// **Memory has to be a primitive here.** The textbook construction is a cross-coupled NOR
    /// latch, and consume semantics kills it twice: each gate waits on the other's first output so
    /// it deadlocks at startup, and its external input port, once consumed, is never refilled, so
    /// it stalls after one firing. No arrangement of gates can hold a value, which is why this
    /// exists.
    ///
    /// **It emits before it has been given anything.** The first firing is on the first tick of the
    /// run, with an empty port -- nothing can have arrived yet, because every edge takes at least a
    /// tick and emission is the last phase. That first bit is what breaks the startup deadlock: a
    /// loop of register into logic and back has one bit circulating in it from the beginning, which
    /// is the machine's state. Without it the logic waits on the register and the register waits on
    /// the logic, forever.
    ///
    /// An earlier sketch put that bit on a wire instead -- "edges that start with bits already in
    /// transit". Same arithmetic, worse game: a bit sitting on a wire belongs to nothing the player
    /// can point at, while a part that starts out holding 0 can be looked at, labelled, and shown
    /// on the board.
    ///
    /// **Every register starts at 0, like a reset**, and nothing can author it otherwise. A level
    /// that wants to start elsewhere encodes its states so the reset state is 0 -- which is what
    /// real designs do anyway, and it keeps the level format, the save file and the palette out of
    /// it entirely.
    ///
    /// **It takes no time of its own.** The bit it is handed goes straight out in the same tick, so
    /// a register costs nothing but the wires either side of it. What it does instead is shift the
    /// stream: its *k*-th output is the bit it was given on cycle *k-1*, so it hands that bit on one
    /// clock **earlier** than a plain wire would. That is the whole of the arithmetic, and it is why
    /// a circuit comparing a bit with the one before it needs no padding at all.
    ///
    /// **A loop must close within one clock period.** The shortest loop is two wires, so a level
    /// with feedback has to space its vectors at least that far apart; the state then comes back
    /// before the next input does. A loop slower than its clock falls behind, and the inputs pile
    /// up into a collision -- the same way this game says every other timing mistake.
    ///
    /// Order-independence is untouched: this emits in the evaluate phase like every other node, and
    /// reads nothing but its own port and its own held bit.
    /// </remarks>
    public sealed class RegisterNode : Node
    {
        private bool _started;

        public RegisterNode(Bit initial = Bit.Zero) : base(1, 1)
        {
            Initial = initial;
            State = initial;
        }

        /// <summary>The bit it holds before the run begins. Zero for every register in the game.</summary>
        public Bit Initial { get; }

        /// <summary>
        /// The bit it is holding: what it last emitted, and what a renderer draws inside it.
        /// </summary>
        public Bit State { get; private set; }

        /// <summary>Whether it has emitted its starting bit yet. False only before the first tick.</summary>
        public bool HasStarted => _started;

        /// <inheritdoc/>
        public override bool IsReadyToEvaluate => !_started || base.IsReadyToEvaluate;

        /// <inheritdoc/>
        internal override void Evaluate(int tick)
        {
            // The starting bit, on the first tick of the run. There is nothing in the port to
            // consume -- and nothing can be, this early -- so the base call is skipped rather than
            // left to throw on an empty port.
            if (!_started)
            {
                _started = true;
                Out(0).Emit(State);
                return;
            }

            base.Evaluate(tick);
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            State = inputs[0];
            Out(0).Emit(State);
        }
    }
}
