using System;
using System.Diagnostics;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>Number literals read by IEEE 1800, 5.7.1, and values written out as a waveform and <c>$display</c> show them.</summary>
    public class LiteralAndFormatTests
    {
        private static LogicVec Read(string text)
        {
            LiteralResult result = Literal.Parse(text);
            Assert.IsTrue(result.Ok, $"'{text}': {result.Error}");
            return result.Value;
        }

        // -----------------------------------------------------------------
        // Reading
        // -----------------------------------------------------------------

        [TestCase("42", 32, true, "00000000000000000000000000101010")]
        [TestCase("8'hA5", 8, false, "10100101")]
        [TestCase("8'ha5", 8, false, "10100101")]
        [TestCase("4'b1x0z", 4, false, "1x0z")]
        [TestCase("4'b1_0_1_0", 4, false, "1010")]
        [TestCase("'d7", 32, false, "00000000000000000000000000000111")]
        [TestCase("16'sd5", 16, true, "0000000000000101")]
        [TestCase("6'o17", 6, false, "001111")]
        [TestCase("4'b?", 4, false, "zzzz")]
        public void ALiteral_ReadsAsTheStandardSays(string text, int width, bool signed, string bits)
        {
            LogicVec value = Read(text);
            Assert.AreEqual(width, value.Width, "width");
            Assert.AreEqual(signed, value.IsSigned, "signedness");
            Assert.AreEqual(bits, value.Format(Radix.Binary));
        }

        [TestCase("8'hx", "xxxxxxxx")]
        [TestCase("8'hz", "zzzzzzzz")]
        [TestCase("12'hx5", "xxxxxxxx0101")]
        [TestCase("8'h1", "00000001")]
        [TestCase("8'dx", "xxxxxxxx")]
        [TestCase("'hx", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
        public void AShortLiteral_PadsWithXOrZ_OnlyIfItsLeftmostDigitIsOne(string text, string bits) =>
            Assert.AreEqual(bits, Read(text).Format(Radix.Binary));

        [TestCase("'0", Logic4.Zero)]
        [TestCase("'1", Logic4.One)]
        [TestCase("'x", Logic4.X)]
        [TestCase("'z", Logic4.Z)]
        public void AnUnbasedUnsizedLiteral_FillsItsContext(string text, Logic4 bit)
        {
            LiteralResult result = Literal.Parse(text);
            Assert.IsTrue(result.FillsContext);
            Assert.AreEqual(bit, result.Value[0]);
        }

        [TestCase("4'hFF", true)]
        [TestCase("4'h0F", false)]
        [TestCase("8'd300", true)]
        public void TooManyDigits_Truncate_AndWarnOnlyWhenSomethingWasLost(string text, bool warns)
        {
            LiteralResult result = Literal.Parse(text);
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(warns, result.Warning != null, result.Warning);
        }

        [TestCase("")]
        [TestCase("8'q12")]
        [TestCase("8'h")]
        [TestCase("0'b1")]
        [TestCase("4'b102")]
        [TestCase("8'd1x")]
        [TestCase("8'h_1")]
        [TestCase("abc")]
        [TestCase("_12")]
        public void NotALiteral_SaysWhy(string text)
        {
            LiteralResult result = Literal.Parse(text);
            Assert.IsFalse(result.Ok, $"'{text}' read as {result.Value}");
            Assert.IsNotEmpty(result.Error);
        }

        /// <summary>
        /// The 16d sweep: a decimal literal was read with BigInteger.Parse and its bits counted by shifting it one bit at a
        /// time, a new number each shift, before its width was checked -- a hundred thousand digits made 6.9 GB and took
        /// 1.5 seconds under .NET 10 (worse on Unity's runtimes, whose Parse multiplies by ten a digit), in one frame, and
        /// again on every edit. A browser frees memory only between frames.
        /// </summary>
        [TestCase("")]
        [TestCase("'d")]
        [TestCase("8'd")]
        [TestCase("400'sd")]
        public void ALongDecimalLiteral_IsReadInLittleMoreThanItsOwnSize(string prefix)
        {
            string text = prefix + new string('9', 100_000);
            Literal.Parse(prefix + "99");

            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            LiteralResult result = Literal.Parse(text);
            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(result.Ok, result.Error);
            Assert.Less(allocated, 2_000_000, $"{allocated:N0} bytes allocated reading 100,000 digits");
            Assert.Less(timer.ElapsedMilliseconds, 1000, "100,000 digits read in well under a second");
        }

        /// <summary>
        /// The decimal reader works nine digits at a time in words of its own: held to BigInteger, which it replaced, on
        /// numbers of every length round the word boundaries, sized, unsized, signed and plain, with leading zeros.
        /// </summary>
        [Test]
        public void DecimalDigits_ReadAsBigIntegerReadsThem()
        {
            var random = new Random(1800);
            for (int round = 0; round < 400; round++)
            {
                int length = round < 60 ? round + 1 : random.Next(1, 120);
                var digits = new char[length];
                for (int i = 0; i < length; i++)
                    digits[i] = (char)('0' + random.Next(10));
                if (round % 7 == 0)
                    digits[0] = '0';
                string number = new string(digits);
                var big = System.Numerics.BigInteger.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
                int bits = 0;
                while (System.Numerics.BigInteger.Pow(2, bits) <= big)
                    bits++;

                foreach (string prefix in new[] { "", "'d", "'sd", "7'd", "64'd", "65'sd", "130'd" })
                {
                    LiteralResult result = Literal.Parse(prefix + number);
                    Assert.IsTrue(result.Ok, $"{prefix}{number}: {result.Error}");
                    int width = result.Value.Width;
                    Assert.GreaterOrEqual(Literal.WidthBound(prefix + number), width, $"{prefix}{number}: the bound a reader holds it to");
                    string expected = Bits(big % System.Numerics.BigInteger.Pow(2, width), width);
                    Assert.AreEqual(expected, result.Value.Format(Radix.Binary), $"{prefix}{number}");

                    int needed = prefix.Length == 0 ? bits + 1 : Math.Max(1, bits);
                    int sizedTo = prefix.Length > 2 && char.IsDigit(prefix[0]) ? int.Parse(prefix.Substring(0, prefix.IndexOf('\''))) : -1;
                    if (sizedTo < 0)
                        Assert.AreEqual(Math.Max(Literal.UnsizedWidth, needed), width, $"{prefix}{number}: its width");
                    else
                        Assert.AreEqual(needed > sizedTo, result.Warning != null, $"{prefix}{number}: warned of bits lost");
                }
            }
        }

        private static string Bits(System.Numerics.BigInteger value, int width)
        {
            var text = new char[width];
            for (int i = width - 1; i >= 0; i--)
            {
                text[i] = value.IsEven ? '0' : '1';
                value >>= 1;
            }
            return new string(text);
        }

        [TestCase("")]
        [TestCase("'d")]
        [TestCase("8'd")]
        public void ADecimalLiteralTooWideForAnyValue_IsRefusedAtOnce(string prefix)
        {
            string text = prefix + new string('9', 320_000);   // more than a million bits

            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            LiteralResult result = Literal.Parse(text);
            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("wider than BitSorter allows", result.Error);
            Assert.Less(result.Error.Length, 200, "the number named cut short, not in full: " + result.Error);
            Assert.Less(allocated, 2_000_000, $"{allocated:N0} bytes allocated refusing it");
            Assert.Less(timer.ElapsedMilliseconds, 200, "refused before anything is worked out");
        }

        [Test]
        public void TheWidestDecimalLiteral_IsRead_AndOneBitMore_IsRefused()
        {
            var widest = System.Numerics.BigInteger.Pow(2, LogicVec.MaxWidth) - 1;
            string digits = widest.ToString(System.Globalization.CultureInfo.InvariantCulture);
            LiteralResult read = Literal.Parse("'d" + digits);
            Assert.IsTrue(read.Ok, read.Error);
            Assert.AreEqual(LogicVec.MaxWidth, read.Value.Width);
            Assert.IsTrue(read.Value.IsKnown && LogicVec.ReduceAnd(read.Value)[0] == Logic4.One, "every bit a 1");

            LiteralResult over = Literal.Parse("'d" + (widest + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.IsFalse(over.Ok);
            Assert.IsFalse(Literal.Parse(digits).Ok, "a plain number needs a sign bit too");
        }

        // -----------------------------------------------------------------
        // Writing
        // -----------------------------------------------------------------

        [TestCase("1010xxxx", Radix.Hex, "ax")]
        [TestCase("10x01111", Radix.Hex, "Xf")]
        [TestCase("zzzz0z01", Radix.Hex, "zZ")]
        [TestCase("xzxz0001", Radix.Hex, "X1")]
        [TestCase("11111111", Radix.Unsigned, "255")]
        [TestCase("11111111", Radix.Signed, "-1")]
        [TestCase("1111x111", Radix.Unsigned, "X")]
        [TestCase("zzzzzzzz", Radix.Unsigned, "z")]
        [TestCase("0000zz00", Radix.Signed, "Z")]
        [TestCase("101", Radix.Octal, "5")]
        public void AValue_IsWrittenAsADisplayWould(string bits, Radix radix, string text) =>
            Assert.AreEqual(text, LogicVec.FromBinary(bits).Format(radix));

        [Test]
        public void AWideValue_IsWrittenInDecimal()
        {
            LogicVec carry = LogicVec.Add(LogicVec.FromUInt64(ulong.MaxValue, 65), LogicVec.FromUInt64(1, 65));
            Assert.AreEqual("18446744073709551616", carry.Format(Radix.Unsigned));
            Assert.AreEqual("-18446744073709551616", carry.AsSigned().Format(Radix.Signed));
        }

        [TestCase("4'b1x0z", Radix.Binary)]
        [TestCase("8'ha5", Radix.Hex)]
        [TestCase("8'd165", Radix.Unsigned)]
        [TestCase("8'sd5", Radix.Signed)]
        [TestCase("70'h2a_ffff_ffff_ffff_fffx", Radix.Hex)]
        public void ALiteral_WrittenOut_ReadsBackTheSame(string text, Radix radix)
        {
            LogicVec value = Read(text);
            Assert.AreEqual(value, Read(value.ToLiteral(radix)), value.ToLiteral(radix));
        }

        [Test]
        public void ANegativeSignedDecimal_IsWrittenAsMinusALiteral()
        {
            LogicVec minusThree = LogicVec.FromInt64(-3, 8);
            Assert.AreEqual("-8'sd3", minusThree.ToLiteral(Radix.Signed));
            Assert.AreEqual("8'sb11111101", minusThree.ToString());
        }
    }
}
