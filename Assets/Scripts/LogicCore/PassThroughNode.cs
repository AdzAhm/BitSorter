namespace BitSorter.LogicCore
{
    /// <summary>Consumes one bit and re-emits it unchanged. Useful for adding delay to a path.</summary>
    public sealed class PassThroughNode : Node
    {
        public PassThroughNode() : base(1, 1)
        {
        }

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            Out(0).Emit(inputs[0]);
        }
    }
}
