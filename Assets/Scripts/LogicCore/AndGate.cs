namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits one only when both inputs are one. Like every node, it fires only once both input
    /// ports are filled -- a bit arriving alone waits in its port until its partner lands.
    /// <code>
    /// a | b | out
    /// 0 | 0 |  0
    /// 0 | 1 |  0
    /// 1 | 0 |  0
    /// 1 | 1 |  1
    /// </code>
    /// </summary>
    public sealed class AndGate : Node
    {
        public AndGate() : base(2, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            bool both = inputs[0] == Bit.One && inputs[1] == Bit.One;
            Out(0).Emit(both ? Bit.One : Bit.Zero);
        }
    }
}
