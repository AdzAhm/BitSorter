using System;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>
    /// Every operator on values of 64 bits or fewer allocates nothing: the guardrail that keeps the
    /// engine's hot path at zero bytes as the code changes.
    /// </summary>
    /// <remarks>
    /// The engine evaluates operators millions of times in a run like the course's processor, inside
    /// a browser where a garbage collection is a visible stall. The commitment is that the narrow
    /// path -- every operator, the widening an operator does itself, selects and concatenations that
    /// stay within a word -- never allocates. Values over 64 bits, literals and formatted text do,
    /// by design, and are not measured here.
    ///
    /// Measured with the thread's own allocation counter, over many repetitions after a warm-up,
    /// so a one-off allocation (a static initialiser, the JIT) is not counted and a per-call one is
    /// counted many times over.
    /// </remarks>
    public class AllocationTests
    {
        private static readonly LogicVec A = LogicVec.FromBinary("1011x01001110010", signed: true);
        private static readonly LogicVec B = LogicVec.FromBinary("0110100111", signed: true);
        private static readonly LogicVec Known = LogicVec.FromInt64(-12345, 40);
        private static readonly LogicVec Divisor = LogicVec.FromInt64(37, 24);
        private static readonly LogicVec Amount = LogicVec.FromUInt64(3, 5);
        private static readonly LogicVec Condition = LogicVec.FromBinary("x");

        private static long Allocated(Action body)
        {
            for (int i = 0; i < 100; i++)
                body();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                body();

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static readonly (string Name, Action Body)[] Operations =
        {
            ("bitwise", () => { LogicVec.And(A, B); LogicVec.Or(A, B); LogicVec.Xor(A, B); LogicVec.Xnor(A, B); LogicVec.Not(A); }),
            ("reduction", () => { LogicVec.ReduceAnd(A); LogicVec.ReduceOr(A); LogicVec.ReduceXor(Known); LogicVec.ReduceNand(A); }),
            ("logical", () => { LogicVec.LogicalNot(A); LogicVec.LogicalAnd(A, B); LogicVec.LogicalOr(A, B); _ = A.IsTrue; }),
            ("arithmetic", () => { LogicVec.Add(Known, Divisor); LogicVec.Subtract(Known, Divisor); LogicVec.Multiply(Known, Divisor); LogicVec.Negate(Known); }),
            ("division", () => { LogicVec.Divide(Known, Divisor); LogicVec.Modulo(Known, Divisor); LogicVec.Divide(Known.AsUnsigned(), Divisor); }),
            ("arithmetic with x", () => { LogicVec.Add(A, B); LogicVec.Divide(A, B); }),
            ("comparison", () => { LogicVec.LessThan(Known, Divisor); LogicVec.GreaterOrEqual(Known, Divisor); LogicVec.Equal(A, B); LogicVec.CaseEqual(A, B); LogicVec.NotEqual(A, B); }),
            ("shifts", () => { LogicVec.ShiftLeft(A, Amount); LogicVec.ShiftRight(A, Amount); LogicVec.ArithmeticShiftRight(A, Amount); LogicVec.ShiftLeft(A, 2); }),
            ("selects", () => { A.Slice(3, 8); A.Bit(Amount); _ = A[4]; }),
            ("concatenation", () => { LogicVec.Concat(A, B); LogicVec.Replicate(B, 3); }),
            ("conditional", () => { LogicVec.Conditional(Condition, A, B); LogicVec.Conditional(Known, A, B); }),
            ("width", () => { A.Extend(60, true); A.Resize(8); A.AsUnsigned(); LogicVec.Fill(Logic4.X, 64); }),
            ("reading", () => { Known.TryGetInt64(out _); Divisor.TryGetUInt64(out _); _ = A.IsKnown; _ = A.Equals(B); }),
        };

        private static string[] Names => Array.ConvertAll(Operations, o => o.Name);

        [TestCaseSource(nameof(Names))]
        public void NarrowValues_AllocateNothing(string name)
        {
            Action body = Array.Find(Operations, o => o.Name == name).Body;
            Assert.AreEqual(0, Allocated(body), $"{name} allocated on the narrow path");
        }

        /// <summary>The paired test: the counter does see an allocation when there is one.</summary>
        [Test]
        public void TheCounter_SeesAWideValuesAllocation()
        {
            LogicVec wide = LogicVec.FromUInt64(1, 65);
            Assert.Greater(Allocated(() => LogicVec.Add(wide, wide)), 0);
        }
    }
}
