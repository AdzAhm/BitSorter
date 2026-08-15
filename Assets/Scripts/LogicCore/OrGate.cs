namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits one when either input is one.
    /// <code>
    /// a | b | out
    /// 0 | 0 |  0
    /// 0 | 1 |  1
    /// 1 | 0 |  1
    /// 1 | 1 |  1
    /// </code>
    /// </summary>
    public sealed class OrGate : Node
    {
        public OrGate() : base(2, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            bool either = inputs[0] == Bit.One || inputs[1] == Bit.One;
            Out(0).Emit(either ? Bit.One : Bit.Zero);
        }
    }
}
