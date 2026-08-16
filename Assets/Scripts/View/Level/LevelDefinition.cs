using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>The two things a level may pin down that the player cannot move or delete.</summary>
    public enum FixtureKind
    {
        Source,
        Sink,
    }

    /// <summary>
    /// A validated, immutable level. Every string in <see cref="LevelFile"/> has become a real type
    /// by the time one of these exists, so nothing downstream re-checks anything.
    /// </summary>
    /// <remarks>
    /// Built only by <see cref="LevelLoader.Validate"/>. The constructor is deliberately not public
    /// state to assemble piecemeal: a half-built level is the thing the two-layer split exists to
    /// make unrepresentable.
    /// </remarks>
    public sealed class LevelDefinition
    {
        /// <summary>
        /// The cap a level gets when it names none. Single digit on purpose: the delay is drawn on the
        /// wire itself, and two digits need a wider pill than <see cref="EdgeRenderer"/> draws.
        /// </summary>
        /// <remarks>
        /// Lives here rather than on <see cref="LevelLoader"/> so the validated model does not depend
        /// on the layer that reads files. The loader substitutes it; the model owns it.
        /// </remarks>
        public const int DefaultMaxWireDelay = 9;

        public LevelDefinition(
            string name,
            string hint,
            int tickLimit,
            int vectorCount,
            IReadOnlyList<LevelFixture> fixtures,
            IReadOnlyList<LevelBudgetEntry> budget,
            IReadOnlyList<LevelExpectation> expectations,
            int maxWireDelay = DefaultMaxWireDelay,
            int delayBudget = 0)
        {
            Name = name;
            Hint = hint;
            TickLimit = tickLimit;
            VectorCount = vectorCount;
            Fixtures = fixtures;
            Budget = budget;
            Expectations = expectations;
            MaxWireDelay = maxWireDelay;
            DelayBudget = delayBudget;
        }

        public string Name { get; }

        /// <summary>One line for the HUD. May be empty, never null.</summary>
        public string Hint { get; }

        /// <summary>
        /// Ticks a run may take before it is failed as never settling. A safety net against
        /// oscillators, not a difficulty knob -- see <see cref="LevelLoader.DefaultTickLimit"/>.
        /// </summary>
        public int TickLimit { get; }

        /// <summary>
        /// How many test vectors the level streams. Equal to the length of every source stream and
        /// every expectation string, which the validator has already proved consistent.
        /// </summary>
        public int VectorCount { get; }

        /// <summary>
        /// In file order, which is also the order they are added to the simulation. Node ids are
        /// nothing but Add call order, so this ordering is what makes a rebuild reproducible.
        /// </summary>
        public IReadOnlyList<LevelFixture> Fixtures { get; }

        public IReadOnlyList<LevelBudgetEntry> Budget { get; }

        /// <summary>
        /// Most ticks the player may put on any one wire. A level with this at 1 has fixed wiring:
        /// nothing can be re-timed, which is how the levels written before delay existed behave.
        /// </summary>
        public int MaxWireDelay { get; }

        /// <summary>
        /// Total ticks the player may add across all wires, above the default of 1 each. Zero means
        /// unlimited -- see <see cref="HasDelayBudget"/>.
        /// </summary>
        public int DelayBudget { get; }

        /// <summary>
        /// Whether the level caps total added delay at all. False leaves lengthening unrestricted,
        /// which is safe: grading ignores arrival ticks, so a longer route cannot buy a wrong answer.
        /// </summary>
        public bool HasDelayBudget => DelayBudget > 0;

        /// <summary>
        /// In file order, which is the order the grader checks them and therefore the order failures
        /// are reported in.
        /// </summary>
        public IReadOnlyList<LevelExpectation> Expectations { get; }

        /// <summary>The fixture with this id, or null. Linear: a level has a handful of fixtures.</summary>
        public LevelFixture FixtureById(string id)
        {
            for (int i = 0; i < Fixtures.Count; i++)
            {
                if (Fixtures[i].Id == id)
                    return Fixtures[i];
            }

            return null;
        }

        /// <summary>
        /// The fixture pinned to this cell, or null if the cell is free for the player. Linear, and
        /// called only on edits, not per frame.
        /// </summary>
        public LevelFixture FixtureAt(Vector2Int cell)
        {
            for (int i = 0; i < Fixtures.Count; i++)
            {
                if (Fixtures[i].Cell == cell)
                    return Fixtures[i];
            }

            return null;
        }

        /// <summary>
        /// How many of a kind the player may place, or zero for a kind this level does not offer.
        /// </summary>
        public int BudgetFor(GateKind kind)
        {
            for (int i = 0; i < Budget.Count; i++)
            {
                if (Budget[i].Kind == kind)
                    return Budget[i].Count;
            }

            return 0;
        }

        public override string ToString() => $"{Name} ({VectorCount} vectors)";
    }

    /// <summary>A source or sink the level pins to a cell.</summary>
    public sealed class LevelFixture
    {
        public LevelFixture(string id, FixtureKind kind, Vector2Int cell, IReadOnlyList<Bit> stream)
        {
            Id = id;
            Kind = kind;
            Cell = cell;
            Stream = stream;
        }

        /// <summary>Stable name the expectations refer to. Unique within a level.</summary>
        public string Id { get; }

        public FixtureKind Kind { get; }

        public Vector2Int Cell { get; }

        /// <summary>
        /// For a source, one bit per test vector. Empty for a sink. Dense by construction: a
        /// SourceNode emits one bit per tick with no way to skip, so the validator refuses a '-'
        /// here rather than pretending sparse streams exist.
        /// </summary>
        public IReadOnlyList<Bit> Stream { get; }

        public override string ToString() => $"{Id} ({Kind} at {Cell})";
    }

    /// <summary>One row of the parts list.</summary>
    public readonly struct LevelBudgetEntry
    {
        public readonly GateKind Kind;
        public readonly int Count;

        public LevelBudgetEntry(GateKind kind, int count)
        {
            Kind = kind;
            Count = count;
        }

        public override string ToString() => $"{GatePalette.Label(Kind)} x{Count}";
    }

    /// <summary>
    /// One bit a sink is expected to receive, tagged with the test vector that should have produced
    /// it.
    /// </summary>
    /// <remarks>
    /// The tag is the whole reason this type exists. Expectations may contain '-' for vectors that
    /// produce nothing at a given sink, so the third bit a sink receives is not necessarily the
    /// third vector's. Carrying the vector index alongside each expected bit is what lets a failure
    /// name a vector instead of a position in a list.
    /// </remarks>
    public readonly struct ExpectedBit
    {
        public readonly Bit Value;
        public readonly int Vector;

        public ExpectedBit(Bit value, int vector)
        {
            Value = value;
            Vector = vector;
        }

        public override string ToString() => $"{(int)Value} (vector {Vector})";
    }

    /// <summary>What one sink must have received once a run settles.</summary>
    public sealed class LevelExpectation
    {
        public LevelExpectation(string sinkId, string values, IReadOnlyList<ExpectedBit> expected)
        {
            SinkId = sinkId;
            Values = values;
            Expected = expected;
        }

        /// <summary>The id of the Sink fixture this grades.</summary>
        public string SinkId { get; }

        /// <summary>The original string, kept verbatim for display and diagnostics.</summary>
        public string Values { get; }

        /// <summary>
        /// The bits this sink must receive, in order, with the '-' vectors omitted. An empty list
        /// means the sink must receive nothing at all -- which is what makes "the wrong bin must
        /// stay empty" a gradeable rule rather than an unchecked assumption.
        /// </summary>
        public IReadOnlyList<ExpectedBit> Expected { get; }

        public override string ToString() => $"{SinkId} = \"{Values}\"";
    }
}
