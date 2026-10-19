using System;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>
    /// The rules of 4-state values one at a time, each with a worked example, where the differential
    /// tests check them all at once on random values.
    /// </summary>
    public class LogicVecTests
    {
        private static LogicVec V(string digits, bool signed = false) => LogicVec.FromBinary(digits, signed);

        private static void Is(string expected, LogicVec actual) => Assert.AreEqual(expected, actual.Format(Radix.Binary));

        // -----------------------------------------------------------------
        // The truth tables, all sixteen pairs, against the standard's own tables (IEEE 1800, 11-7 to 11-10)
        // -----------------------------------------------------------------

        [TestCase("&", "0000", "01xx", "0xxx", "0xxx")]
        [TestCase("|", "01xx", "1111", "x1xx", "x1xx")]
        [TestCase("^", "01xx", "10xx", "xxxx", "xxxx")]
        [TestCase("~^", "10xx", "01xx", "xxxx", "xxxx")]
        public void ABitwiseOperator_FollowsItsTruthTable(string op, string row0, string row1, string rowX, string rowZ)
        {
            const string order = "01xz";
            string[] rows = { row0, row1, rowX, rowZ };

            for (int p = 0; p < 4; p++)
            {
                for (int q = 0; q < 4; q++)
                {
                    LogicVec a = V(order[p].ToString()), b = V(order[q].ToString());
                    LogicVec result = op == "&" ? LogicVec.And(a, b) : op == "|" ? LogicVec.Or(a, b)
                        : op == "^" ? LogicVec.Xor(a, b) : LogicVec.Xnor(a, b);

                    Assert.AreEqual(rows[p][q].ToString(), result.Format(Radix.Binary), $"{order[p]} {op} {order[q]}");
                }
            }
        }

        [Test]
        public void Not_SwapsZeroAndOne_AndMakesXOfXAndZ() => Is("10xx", LogicVec.Not(V("01xz")));

        // -----------------------------------------------------------------
        // Unknowns
        // -----------------------------------------------------------------

        [Test]
        public void AnyUnknownBit_MakesAWholeSumUnknown()
        {
            Is("xxxx", LogicVec.Add(V("000x"), V("0001")));
            Is("xxxx", LogicVec.Multiply(V("z000"), V("0000")));
        }

        [Test]
        public void DivisionByZero_IsUnknown()
        {
            Is("xxxx", LogicVec.Divide(V("0110"), V("0000")));
            Is("xxxx", LogicVec.Modulo(V("0110"), V("0000")));
        }

        [Test]
        public void Equality_IsZero_AsSoonAsAKnownBitDiffers_WhateverIsUnknown()
        {
            Is("0", LogicVec.Equal(V("1x00"), V("0x00")));
            Is("x", LogicVec.Equal(V("1x00"), V("1000")));
            Is("1", LogicVec.Equal(V("1100"), V("1100")));
            Is("1", LogicVec.NotEqual(V("1x00"), V("0x00")));
        }

        [Test]
        public void CaseEquality_ComparesXAndZ_AndIsNeverUnknown()
        {
            Is("1", LogicVec.CaseEqual(V("1x0z"), V("1x0z")));
            Is("0", LogicVec.CaseEqual(V("1x0z"), V("1x0x")));
        }

        [Test]
        public void AComparison_WithAnyUnknownBit_IsUnknown() => Is("x", LogicVec.LessThan(V("0z00"), V("1000")));

        [Test]
        public void AValueWithAKnownOne_IsTrue_WhateverElseIsUnknown()
        {
            Assert.AreEqual(Logic4.One, LogicVec.Truth(V("0x10")));
            Assert.AreEqual(Logic4.X, LogicVec.Truth(V("0x00")));
            Assert.AreEqual(Logic4.Zero, LogicVec.Truth(V("0000")));
            Assert.IsFalse(V("0x00").IsTrue, "an unknown condition takes the else branch");
        }

        [Test]
        public void AnUnknownCondition_KeepsTheBitsBothSidesAgreeOn()
        {
            Is("1xx0", LogicVec.Conditional(V("x"), V("1100"), V("1010")));
            Is("x", LogicVec.Conditional(V("z"), V("z"), V("z")));
        }

        [Test]
        public void ASelectOutsideTheValue_ReadsX()
        {
            // Bits -1 to 2 of 4'b0110: bit 2 is 1, bit 1 is 1, bit 0 is 0, and bit -1 does not exist.
            Is("110x", V("0110").Slice(-1, 4));
            Is("x01", V("0110").Slice(2, 3));
            Is("x", V("0110").Bit(LogicVec.FromUInt64(4, 3)));
            Is("x", V("0110").Bit(V("0x1")));
            Is("1", V("0110").Bit(LogicVec.FromUInt64(2, 3)));
        }

        // -----------------------------------------------------------------
        // Signedness and width
        // -----------------------------------------------------------------

        [Test]
        public void ArithmeticShiftRight_OfAnUnsignedValue_IsALogicalShift()
        {
            // The course's ALU shifts unsigned operands with >>>, so this is the case its students meet.
            LogicVec amount = LogicVec.FromUInt64(2, 3);
            Is("0011", LogicVec.ArithmeticShiftRight(V("1100"), amount));
            Is("1111", LogicVec.ArithmeticShiftRight(V("1100", signed: true), amount));
            // x1x0 >>> 2 keeps "x1", moved down, and fills the two vacated top bits with the sign: x.
            Is("xxx1", LogicVec.ArithmeticShiftRight(V("x1x0", signed: true), amount));
        }

        [Test]
        public void AShiftByAnUnknownAmount_IsAllUnknown() => Is("xxxx", LogicVec.ShiftLeft(V("0001"), V("0x")));

        [Test]
        public void AMixedExpression_IsUnsigned_AndZeroExtendsItsSignedOperand()
        {
            // -1 (signed, 4 bits) + 1 (unsigned, 8 bits): unsigned, so the -1 becomes 8'b00001111.
            LogicVec sum = LogicVec.Add(V("1111", signed: true), V("00000001"));
            Is("00010000", sum);
            Assert.IsFalse(sum.IsSigned);

            // Both signed: the -1 sign-extends to 8'b11111111, and the sum is 0.
            Is("00000000", LogicVec.Add(V("1111", signed: true), V("00000001", signed: true)));
        }

        [Test]
        public void SignExtension_CopiesAnUnknownSignBit() => Is("xxx01", V("x01", signed: true).Resize(5));

        [Test]
        public void TheMostNegativeNumber_DividedByMinusOne_WrapsToItself()
        {
            Is("10000000", LogicVec.Divide(V("10000000", signed: true), V("11111111", signed: true)));

            LogicVec min64 = LogicVec.FromInt64(long.MinValue, 64);
            Assert.AreEqual(min64, LogicVec.Divide(min64, LogicVec.FromInt64(-1, 64)));
            Is(new string('0', 64), LogicVec.Modulo(min64, LogicVec.FromInt64(-1, 64)));
        }

        [Test]
        public void SignedDivision_RoundsTowardsZero_AndTheRemainderTakesTheDividendsSign()
        {
            Assert.IsTrue(LogicVec.Divide(LogicVec.FromInt64(-7, 8), LogicVec.FromInt64(2, 8)).TryGetInt64(out long q));
            Assert.IsTrue(LogicVec.Modulo(LogicVec.FromInt64(-7, 8), LogicVec.FromInt64(2, 8)).TryGetInt64(out long r));
            Assert.AreEqual(-3, q);
            Assert.AreEqual(-1, r);
        }

        [Test]
        public void ACarry_RunsAcrossAWordBoundary()
        {
            // 65 bits: the low 64 all ones, plus 1, carries into bit 64 -- the course's own ripple-carry width.
            LogicVec allOnes = LogicVec.FromUInt64(ulong.MaxValue, 65);
            LogicVec sum = LogicVec.Add(allOnes, LogicVec.FromUInt64(1, 65));
            Is("1" + new string('0', 64), sum);
            Is(new string('1', 64), LogicVec.Subtract(sum, LogicVec.FromUInt64(1, 65)).Slice(0, 64));
        }

        [TestCase("0101", "0x01", "1")]   // an x in the pattern matches anything (Icarus: 1)
        [TestCase("01x1", "0101", "x")]   // an x in the value, where the pattern asks for a bit (Icarus: x)
        [TestCase("0101", "1x0x", "0")]   // a known bit that differs, whatever else (Icarus: !=? gives 1)
        [TestCase("x101", "0100", "0")]   // a difference outweighs an unknown elsewhere
        [TestCase("1100", "11zz", "1")]   // z in the pattern is as good as x
        public void AWildcardEquality_IgnoresThePatternsXAndZ(string a, string b, string expected)
        {
            Is(expected, LogicVec.WildcardEqual(V(a), V(b)));
            Is(expected == "x" ? "x" : expected == "1" ? "0" : "1", LogicVec.WildcardNotEqual(V(a), V(b)));
        }

        [Test]
        public void BitCounting_CountsOnlyTheOnes()
        {
            LogicVec b = V("10110x10");
            Assert.IsTrue(LogicVec.CountOnes(b).TryGetInt64(out long ones));
            Assert.AreEqual(4, ones, "x and z are not counted (Icarus: 4)");
            Assert.AreEqual(32, LogicVec.CountOnes(b).Width, "an int");
            Is("1", LogicVec.OneHot(V("00010000")));
            Is("0", LogicVec.OneHot(V("00010001")));
            Is("1", LogicVec.OneHot(V("0001x000")));   // the x is not a 1
            Is("1", LogicVec.OneHot0(V("00000000")));
            Is("0", LogicVec.OneHot0(V("00000011")));
            Is("1", LogicVec.IsUnknown(b));
            Is("0", LogicVec.IsUnknown(V("1010")));
            Is("1", LogicVec.IsUnknown(V("10z0")));
        }

        [Test]
        public void AsTwoState_XAndZBecome0_AndKnownBitsStay()
        {
            Is("1000", V("1x0z").ToTwoState());
            Is("0110", V("0110").ToTwoState());
            Assert.IsTrue(V("x1", signed: true).ToTwoState().IsSigned, "the signedness is kept");

            // Across the word boundary: an x at bit 64 and a z at bit 0 of a 70-bit value.
            LogicVec wide = LogicVec.Concat(V("10x"), LogicVec.Concat(LogicVec.Fill(Logic4.One, 63), V("1z")));
            Is("100" + new string('1', 63) + "10", wide.ToTwoState());
            Assert.IsTrue(wide.ToTwoState().IsKnown);
        }

        [Test]
        public void ConcatenationAndReplication_AreUnsigned_HighFirst()
        {
            LogicVec joined = LogicVec.Concat(V("10", signed: true), V("x1z"));
            Is("10x1z", joined);
            Assert.IsFalse(joined.IsSigned);
            Is("101010", LogicVec.Replicate(V("10"), 3));
        }

        // -----------------------------------------------------------------
        // Limits
        // -----------------------------------------------------------------

        [Test]
        public void AnEmptyValue_IsRefused() =>
            Assert.Throws<ArgumentException>(() => LogicVec.Add(default, V("1")));

        [Test]
        public void AWidthOutsideOneToTheMaximum_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LogicVec.FromUInt64(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogicVec.Fill(Logic4.X, LogicVec.MaxWidth + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogicVec.Replicate(V("1"), LogicVec.MaxWidth + 1));
        }

        [Test]
        public void EqualValues_AreEqual_AndHashAlike_AcrossTheWordBoundary()
        {
            LogicVec a = V(new string('1', 70)), b = LogicVec.Fill(Logic4.One, 70);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, b.AsSigned(), "signedness is part of a value");
        }
    }
}
