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
            int delayBudget = 0,
            int maxLatency = 0,
            int order = 0,
            string goal = "")
        {
            Order = order;
            Goal = goal ?? string.Empty;
            Name = name;
            Hint = hint;
            TickLimit = tickLimit;
            VectorCount = vectorCount;
            Fixtures = fixtures;
            Budget = budget;
            Expectations = expectations;
            MaxWireDelay = maxWireDelay;
            DelayBudget = delayBudget;
            MaxLatency = maxLatency;
        }

        public string Name { get; }

        /// <summary>
        /// What the player is being asked to build, stated plainly. May be empty, never null.
        /// </summary>
        /// <remarks>
        /// The objective, not a clue, and the distinction is load-bearing. This is free to name gates
        /// and give the function outright -- "SUM gets A XOR B" is a fine goal. <see cref="Hint"/> is
        /// not: it is held to the no-giveaway rules in CurriculumTests, which apply to the hint alone.
        ///
        /// Splitting the two is what stopped hints drifting into stating their own answers. Before
        /// this field existed the hint was the only place to put the objective, so the half adder's
        /// hint ended up naming both its gates and which output each produced.
        /// </remarks>
        public string Goal { get; }

        /// <summary>A nudge towards the goal. May be empty, never null.</summary>
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
        /// Most ticks a bit may take from source to sink, or zero for a level that does not grade on
        /// time. This is the critical path expressed as a rule, and the only reason a correct circuit
        /// may still fail.
        /// </summary>
        /// <remarks>
        /// Set it to the critical path of the intended solution. That is exactly satisfiable rather
        /// than tight: balancing pads the short paths up to the long one and never past it, so
        /// getting the timing right costs nothing against this ceiling.
        /// </remarks>
        public int MaxLatency { get; }

        /// <summary>Whether the level grades on time at all.</summary>
        public bool HasLatencyLimit => MaxLatency > 0;

        /// <summary>
        /// Where this level sits in the run, or zero for a level that names no place.
        /// </summary>
        /// <remarks>
        /// Play order used to be the ordinal sort of the file names, which put the NAND puzzle ahead
        /// of the half adder and the tutorial seventh. This is the fix. Uniqueness across the set is
        /// <see cref="LevelCatalog"/>'s job -- a single file cannot see another one.
        /// </remarks>
        public int Order { get; }

        /// <summary>Whether this level claims a place in the run at all.</summary>
        public bool HasOrder => Order > 0;

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

        /// <summary>
        /// Whether the level stocks this kind at all, however many are still unplaced. The question
        /// the palette asks: a kind that is offered but exhausted stays selectable, because the
        /// player may yet remove one and place it elsewhere.
        /// </summary>
        public bool Offers(GateKind kind) => BudgetFor(kind) > 0;

        /// <summary>
        /// The first kind on the parts list, for a palette that has to start on something. False for
        /// a level that stocks no gates at all, where there is no selection to make.
        /// </summary>
        public bool TryFirstBudgetKind(out GateKind kind)
        {
            if (Budget.Count > 0)
            {
                kind = Budget[0].Kind;
                return true;
            }

            kind = default;
            return false;
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

        /// <summary>
        /// A don't-care: a bit must still arrive in this slot, but either value satisfies it.
        /// <see cref="Value"/> carries nothing meaningful when this is set.
        /// </summary>
        /// <remarks>
        /// An explicit flag rather than widening <see cref="Value"/> to a nullable. CLAUDE.md has
        /// already spent nullable Bit on "this port is empty", and giving the same nullable a second
        /// meaning of "any value" one type away is how that decision stops being readable.
        ///
        /// Note what a don't-care does *not* relax: the arrival. It occupies its slot in the expected
        /// sequence exactly as a literal does, so the count checks in the grader still apply. That is
        /// the difference between this and the '-' of a silent vector, which is dropped entirely.
        /// </remarks>
        public readonly bool IsAny;

        public ExpectedBit(Bit value, int vector)
        {
            Value = value;
            Vector = vector;
            IsAny = false;
        }

        private ExpectedBit(int vector, bool isAny)
        {
            Value = default;
            Vector = vector;
            IsAny = isAny;
        }

        /// <summary>A slot that must receive a bit, of either value.</summary>
        public static ExpectedBit Any(int vector) => new ExpectedBit(vector, true);

        public override string ToString() =>
            IsAny ? $"x (vector {Vector})" : $"{(int)Value} (vector {Vector})";
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
        /// Whether any vector here is silent, written '-'.
        /// </summary>
        /// <remarks>
        /// What makes a positional comparison against <see cref="Expected"/> unsafe. A silent vector
        /// is dropped from that list rather than occupying a slot, so a sink that emits one bit too
        /// many pushes every later reception out of step with the vector it is compared against. The
        /// grader checks this before it compares values, so it can name the vector that actually
        /// misbehaved instead of the first one whose value happens not to line up.
        ///
        /// Note that a don't-care does not have this effect: 'x' keeps its slot.
        /// </remarks>
        public bool HasSilentVectors => Values.IndexOf('-') >= 0;

        /// <summary>
        /// The bits this sink must receive, in order, with the '-' vectors omitted. An empty list
        /// means the sink must receive nothing at all -- which is what makes "the wrong bin must
        /// stay empty" a gradeable rule rather than an unchecked assumption.
        /// </summary>
        public IReadOnlyList<ExpectedBit> Expected { get; }

        public override string ToString() => $"{SinkId} = \"{Values}\"";
    }
}
