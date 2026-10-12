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
