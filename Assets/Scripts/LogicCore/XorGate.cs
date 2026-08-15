namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits one when the inputs differ.
    /// <code>
    /// a | b | out
    /// 0 | 0 |  0
    /// 0 | 1 |  1
    /// 1 | 0 |  1
    /// 1 | 1 |  0
    /// </code>
    /// </summary>
    public sealed class XorGate : Node
    {
        public XorGate() : base(2, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            bool differ = inputs[0] != inputs[1];
            Out(0).Emit(differ ? Bit.One : Bit.Zero);
        }
    }
}
