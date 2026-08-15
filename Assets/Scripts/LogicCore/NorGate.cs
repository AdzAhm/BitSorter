namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits one only when both inputs are zero -- an <see cref="OrGate"/> inverted.
    /// <code>
    /// a | b | out
    /// 0 | 0 |  1
    /// 0 | 1 |  0
    /// 1 | 0 |  0
    /// 1 | 1 |  0
    /// </code>
    /// </summary>
    public sealed class NorGate : Node
    {
        public NorGate() : base(2, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            bool either = inputs[0] == Bit.One || inputs[1] == Bit.One;
            Out(0).Emit(either ? Bit.Zero : Bit.One);
        }
    }
}
