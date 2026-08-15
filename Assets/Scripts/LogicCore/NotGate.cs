namespace BitSorter.LogicCore
{
    /// <summary>
    /// Inverts its input.
    /// <code>
    /// in | out
    ///  0 |  1
    ///  1 |  0
    /// </code>
    /// </summary>
    public sealed class NotGate : Node
    {
        public NotGate() : base(1, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            Out(0).Emit(inputs[0] == Bit.Zero ? Bit.One : Bit.Zero);
        }
    }
}
