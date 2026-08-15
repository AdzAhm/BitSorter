namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits zero only when both inputs are one -- an <see cref="AndGate"/> inverted.
    /// <code>
    /// a | b | out
    /// 0 | 0 |  1
    /// 0 | 1 |  1
    /// 1 | 0 |  1
    /// 1 | 1 |  0
    /// </code>
    /// </summary>
    public sealed class NandGate : Node
    {
        public NandGate() : base(2, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            bool both = inputs[0] == Bit.One && inputs[1] == Bit.One;
            Out(0).Emit(both ? Bit.Zero : Bit.One);
        }
    }
}
