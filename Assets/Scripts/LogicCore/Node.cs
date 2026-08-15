using System;
using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// A graph vertex owning a fixed set of input and output ports. Fires only when every input
    /// port holds a bit, consumes them all, and may emit on its outputs.
    /// </summary>
    public abstract class Node
    {
        private readonly InputPort[] _inputs;
        private readonly OutputPort[] _outputs;
        private readonly Bit[] _consumeBuffer;

        /// <summary>
        /// Stable identifier, assigned when the node is added to a <see cref="Simulation"/> and
        /// never reused or changed. A renderer can key its visuals to this across ticks, and an
        /// input port is addressed stably by (this id, port index). -1 until the node is added.
        /// </summary>
        public int Id { get; internal set; } = -1;

        /// <summary>
        /// Optional label, used only to make diagnostics and test failures readable. Writable
        /// from anywhere, but it is not simulation state -- setting it cannot change behaviour.
        /// </summary>
        public string Name { get; set; }

        public int InputCount => _inputs.Length;
        public int OutputCount => _outputs.Length;

        /// <remarks>
        /// Convenient, but typed as IReadOnlyList, so iterating one boxes an enumerator. On a
        /// per-frame path use <see cref="InputCount"/> with <see cref="In"/> instead.
        /// </remarks>
        public IReadOnlyList<InputPort> Inputs => _inputs;

        /// <inheritdoc cref="Inputs"/>
        public IReadOnlyList<OutputPort> Outputs => _outputs;

        protected Node(int inputCount, int outputCount)
        {
            if (inputCount < 0)
                throw new ArgumentOutOfRangeException(nameof(inputCount), inputCount, "Cannot be negative.");
            if (outputCount < 0)
                throw new ArgumentOutOfRangeException(nameof(outputCount), outputCount, "Cannot be negative.");

            _inputs = new InputPort[inputCount];
            for (int i = 0; i < inputCount; i++)
                _inputs[i] = new InputPort(this, i);

            _outputs = new OutputPort[outputCount];
            for (int i = 0; i < outputCount; i++)
                _outputs[i] = new OutputPort(this, i);

            _consumeBuffer = new Bit[inputCount];
        }

        public InputPort In(int index) => _inputs[index];
        public OutputPort Out(int index) => _outputs[index];

        /// <summary>
        /// True when every input port holds a bit. Vacuously true for a node with no inputs,
        /// which is what lets <see cref="SourceNode"/> fire every tick without special casing.
        /// </summary>
        public bool IsReadyToEvaluate
        {
            get
            {
                for (int i = 0; i < _inputs.Length; i++)
                {
                    if (!_inputs[i].IsOccupied)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Consumes every input port, then hands the values to <see cref="OnEvaluate"/>.
        /// Consuming here rather than in subclasses guarantees a node can never fire without
        /// clearing its ports.
        /// </summary>
        internal void Evaluate(int tick)
        {
            for (int i = 0; i < _inputs.Length; i++)
                _consumeBuffer[i] = _inputs[i].Consume();

            OnEvaluate(_consumeBuffer, tick);
        }

        /// <param name="inputs">
        /// The consumed input values, indexed by port. Reused between ticks -- do not retain it.
        /// </param>
        /// <param name="tick">The tick currently being executed.</param>
        protected abstract void OnEvaluate(Bit[] inputs, int tick);

        public override string ToString() => Name ?? GetType().Name;
    }
}
