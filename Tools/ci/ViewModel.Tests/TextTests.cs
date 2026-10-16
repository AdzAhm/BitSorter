using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The waveform's words (Part 9b-2a): a value in each radix with <c>$display</c>'s rules for a
    /// digit not fully known, how wide that can get, the cache that formats each change once, and time
    /// written and read the same in every culture.
    /// </summary>
    public class TextTests
    {
        private static readonly WaveRadix[] AllRadixes =
            { WaveRadix.Binary, WaveRadix.Octal, WaveRadix.Hex, WaveRadix.Unsigned, WaveRadix.Signed, WaveRadix.Symbolic };

        private static LogicVec Bits(string digits, bool signed = false) => LogicVec.FromBinary(digits, signed);

        private static LogicVec Byte(ulong value) => LogicVec.FromUInt64(value, 8);

        /// <summary>A state machine's enum, two bits: <c>typedef enum logic [1:0] {IDLE, RUN, DONE} state_t;</c>.</summary>
        private static EnumInfo States() => new EnumInfo("state_t", 2, new[]
        {
            new EnumMemberInfo("IDLE", LogicVec.FromUInt64(0, 2)),
            new EnumMemberInfo("RUN", LogicVec.FromUInt64(1, 2)),
            new EnumMemberInfo("DONE", LogicVec.FromUInt64(2, 2)),
        });

        /// <summary>
        /// A culture that writes numbers every way the invariant one does not: a comma for the point, a
        /// point between thousands, and the true minus sign. Built from the invariant culture, so it
        /// exists even where .NET runs with no culture data at all.
        /// </summary>
        private static CultureInfo Contrary()
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ",";
            culture.NumberFormat.NumberGroupSeparator = ".";
            culture.NumberFormat.NegativeSign = "\u2212";
            return culture;
        }

        private static void InCulture(CultureInfo culture, Action act)
        {
            CultureInfo before = CultureInfo.CurrentCulture, beforeUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                act();
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
                CultureInfo.CurrentUICulture = beforeUi;
            }
        }

        private static string Written(double time, int decimals, string unit)
        {
            var into = new char[TimeText.MaxLength(decimals, unit)];
            int length = TimeText.Write(time, decimals, unit, into);
            return new string(into, 0, length);
        }

        private static long Parsed(string text, string unit = "ns")
        {
            Assert.IsTrue(TimeText.TryParse(text, unit, out long time, out string why), $"'{text}' was refused: {why}");
            Assert.IsNull(why, "a time read has no reason against it");
            return time;
        }

        private static string Refused(string text, string unit = "ns")
        {
            Assert.IsFalse(TimeText.TryParse(text, unit, out long time, out string why), $"'{text}' was read as {time}");
            Assert.AreEqual(0, time, "a refused time reads as nothing");
            Assert.IsFalse(string.IsNullOrEmpty(why), "a refusal says why");
            return why;
        }

        // -----------------------------------------------------------------
        // A value's text
        // -----------------------------------------------------------------

        [Test]
        public void OneBit_IsItsLevel_InEveryRadix()
        {
            foreach (WaveRadix radix in AllRadixes)
            {
                Assert.AreEqual("0", ValueText.Of(Bits("0"), radix), radix.ToString());
                Assert.AreEqual("1", ValueText.Of(Bits("1"), radix), radix.ToString());
                Assert.AreEqual("x", ValueText.Of(Bits("x"), radix), radix.ToString());
                Assert.AreEqual("z", ValueText.Of(Bits("z"), radix), radix.ToString());
                Assert.AreEqual("1", ValueText.Of(Bits("1", signed: true), radix), $"{radix}: a high signed bit is a level, not -1");
            }
        }

        [Test]
        public void Hex_WritesTheLettersInCapitals()
        {
            Assert.AreEqual("3A", ValueText.Of(Byte(0x3a), WaveRadix.Hex));
            Assert.AreEqual("FF", ValueText.Of(Byte(0xff), WaveRadix.Hex));
            Assert.AreEqual("30", ValueText.Of(Byte(0x30), WaveRadix.Hex));
            Assert.AreEqual("DEADBEEF", ValueText.Of(LogicVec.FromUInt64(0xdeadbeef, 32), WaveRadix.Hex));
        }

        [Test]
        public void BinaryOctalAndHex_WriteEveryDigitTheWidthHas_LeadingZerosIncluded()
        {
            Assert.AreEqual("0A", ValueText.Of(Byte(0x0a), WaveRadix.Hex));
            Assert.AreEqual("00F", ValueText.Of(LogicVec.FromUInt64(0xf, 12), WaveRadix.Hex));
            Assert.AreEqual("00000101", ValueText.Of(Byte(5), WaveRadix.Binary));
            Assert.AreEqual("005", ValueText.Of(Byte(5), WaveRadix.Octal));
            Assert.AreEqual("1x0z", ValueText.Of(Bits("1x0z"), WaveRadix.Binary));
        }

        [Test]
        public void ADigitNotFullyKnown_FollowsDisplaysRule()
        {
            Assert.AreEqual("X3", ValueText.Of(Bits("1x000011"), WaveRadix.Hex), "some bits x: X, and the known digit still reads");
            Assert.AreEqual("Z3", ValueText.Of(Bits("1z000011"), WaveRadix.Hex), "some bits z and none x: Z");
            Assert.AreEqual("X3", ValueText.Of(Bits("xz000011"), WaveRadix.Hex), "x and z together, not all of either: X");
            Assert.AreEqual("x3", ValueText.Of(Bits("xxxx0011"), WaveRadix.Hex), "every bit x: x");
            Assert.AreEqual("0z", ValueText.Of(Bits("0000zzzz"), WaveRadix.Hex), "every bit z: z");
            Assert.AreEqual("xx", ValueText.Of(LogicVec.AllX(8), WaveRadix.Hex));
            Assert.AreEqual("zz", ValueText.Of(LogicVec.Fill(Logic4.Z, 8), WaveRadix.Hex));
            Assert.AreEqual("5X", ValueText.Of(Bits("101x11"), WaveRadix.Octal));
        }

        [Test]
        public void RaisingTheLetters_LeavesTheCaseOfAnXOrAZAlone()
        {
            // Upper-casing the whole string would make this "XA": a digit claimed partly known that is not.
            Assert.AreEqual("xA", ValueText.Of(Bits("xxxx1010"), WaveRadix.Hex));
            Assert.AreEqual("zB", ValueText.Of(Bits("zzzz1011"), WaveRadix.Hex));
            Assert.AreEqual("XC", ValueText.Of(Bits("x0001100"), WaveRadix.Hex));
        }

        [Test]
        public void Decimal_IsTheRadixsReading_NotTheDeclarations()
        {
            Assert.AreEqual("253", ValueText.Of(Byte(253), WaveRadix.Unsigned));
            Assert.AreEqual("-3", ValueText.Of(Byte(253), WaveRadix.Signed), "an unsigned bus read as signed");
            Assert.AreEqual("-3", ValueText.Of(LogicVec.FromInt64(-3, 8), WaveRadix.Signed));
            Assert.AreEqual("253", ValueText.Of(LogicVec.FromInt64(-3, 8), WaveRadix.Unsigned), "a signed bus read as unsigned");
            Assert.AreEqual("127", ValueText.Of(Byte(127), WaveRadix.Signed));
            Assert.AreEqual("-128", ValueText.Of(Byte(128), WaveRadix.Signed));
            Assert.AreEqual("0", ValueText.Of(Byte(0), WaveRadix.Unsigned));

            LogicVec twoTo64 = Bits("1" + new string('0', 64));
            Assert.AreEqual("18446744073709551616", ValueText.Of(twoTo64, WaveRadix.Unsigned), "a 65-bit carry chain's top bit");
            Assert.AreEqual("-18446744073709551616", ValueText.Of(twoTo64, WaveRadix.Signed));
        }

        [Test]
        public void Decimal_WithAnUnknownBit_IsNoNumber()
        {
            foreach (WaveRadix radix in new[] { WaveRadix.Unsigned, WaveRadix.Signed })
            {
                Assert.AreEqual("x", ValueText.Of(LogicVec.AllX(8), radix), "every bit x");
                Assert.AreEqual("z", ValueText.Of(LogicVec.Fill(Logic4.Z, 8), radix), "every bit z");
                Assert.AreEqual("X", ValueText.Of(Bits("0000001x"), radix), "some bits x");
                Assert.AreEqual("Z", ValueText.Of(Bits("0000001z"), radix), "some bits z and none x");
                Assert.AreEqual("X", ValueText.Of(Bits("xxxxzzzz"), radix), "x and z, every bit unknown");
            }
        }

        [Test]
        public void Symbolic_IsTheMembersName_OrHex()
        {
            EnumInfo states = States();

            Assert.AreEqual("IDLE", ValueText.Of(LogicVec.FromUInt64(0, 2), WaveRadix.Symbolic, states));
            Assert.AreEqual("RUN", ValueText.Of(LogicVec.FromUInt64(1, 2), WaveRadix.Symbolic, states));
            Assert.AreEqual("DONE", ValueText.Of(LogicVec.FromUInt64(2, 2), WaveRadix.Symbolic, states));
            Assert.AreEqual("3", ValueText.Of(LogicVec.FromUInt64(3, 2), WaveRadix.Symbolic, states), "a code the enum never names reads in hex");
            Assert.AreEqual("x", ValueText.Of(LogicVec.AllX(2), WaveRadix.Symbolic, states), "a state register before reset stays x");
            Assert.AreEqual("X", ValueText.Of(Bits("1x"), WaveRadix.Symbolic, states), "and partly x keeps $display's rule");
            Assert.AreEqual("01", ValueText.Of(LogicVec.FromUInt64(1, 2), WaveRadix.Binary, states), "the enum is read only in Symbolic");

            var opcodes = new EnumInfo("op_t", 8, new[] { new EnumMemberInfo("LOAD", Byte(0x03)), new EnumMemberInfo("STORE", Byte(0x23)) });
            Assert.AreEqual("STORE", ValueText.Of(Byte(0x23), WaveRadix.Symbolic, opcodes));
            Assert.AreEqual("3A", ValueText.Of(Byte(0x3a), WaveRadix.Symbolic, opcodes), "a non-member falls back to hex, in capitals");
        }

        [Test]
        public void Symbolic_WithNoEnumOrTheWrongWidth_IsHex()
        {
            Assert.AreEqual("3A", ValueText.Of(Byte(0x3a), WaveRadix.Symbolic));
            Assert.AreEqual("01", ValueText.Of(Byte(1), WaveRadix.Symbolic, States()), "a member is named only at its own width");
        }

        [Test]
        public void Symbolic_NamesAOneBitEnum()
        {
            var power = new EnumInfo("power_t", 1, new[]
            {
                new EnumMemberInfo("OFF", LogicVec.FromUInt64(0, 1)),
                new EnumMemberInfo("ON", LogicVec.FromUInt64(1, 1)),
            });

            Assert.AreEqual("ON", ValueText.Of(Bits("1"), WaveRadix.Symbolic, power), "the name is the radix's whole point");
            Assert.AreEqual("x", ValueText.Of(Bits("x"), WaveRadix.Symbolic, power), "and an unknown bit is still its level");
            Assert.AreEqual("1", ValueText.Of(Bits("1"), WaveRadix.Hex, power));
        }

        [Test]
        public void NoValue_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => ValueText.Of(default, WaveRadix.Hex));
        }

        // -----------------------------------------------------------------
        // How wide a value's text gets
        // -----------------------------------------------------------------

        [Test]
        public void MaxChars_IsTheWidestTextOfTheWidth()
        {
            Assert.AreEqual(2, ValueText.MaxChars(8, WaveRadix.Hex));
            Assert.AreEqual(3, ValueText.MaxChars(8, WaveRadix.Unsigned), "255");
            Assert.AreEqual(4, ValueText.MaxChars(8, WaveRadix.Signed), "-128");
            Assert.AreEqual(8, ValueText.MaxChars(8, WaveRadix.Binary));
            Assert.AreEqual(3, ValueText.MaxChars(8, WaveRadix.Octal));
            Assert.AreEqual(2, ValueText.MaxChars(8, WaveRadix.Symbolic), "no enum: hex");
            Assert.AreEqual(17, ValueText.MaxChars(65, WaveRadix.Hex));
            Assert.AreEqual(20, ValueText.MaxChars(65, WaveRadix.Unsigned), "36893488147419103231");
            Assert.AreEqual(21, ValueText.MaxChars(65, WaveRadix.Signed), "-18446744073709551616");
            Assert.AreEqual(2, ValueText.MaxChars(2, WaveRadix.Signed), "-2");

            foreach (WaveRadix radix in AllRadixes)
                Assert.AreEqual(1, ValueText.MaxChars(1, radix), $"{radix}: one bit is one level");

            Assert.Throws<ArgumentOutOfRangeException>(() => ValueText.MaxChars(0, WaveRadix.Hex));
        }

        [Test]
        public void MaxChars_IsReached_AndNeverPassed_ByAnyValueOfItsWidth()
        {
            var random = new Random(9);
            const string states = "01xz";

            for (int width = 1; width <= 12; width++)
            {
                foreach (WaveRadix radix in AllRadixes)
                {
                    int longest = 0;
                    for (ulong v = 0; v < 1UL << width; v++)
                        longest = Math.Max(longest, ValueText.Of(LogicVec.FromUInt64(v, width), radix).Length);

                    for (int k = 0; k < 200; k++)
                    {
                        var digits = new char[width];
                        for (int i = 0; i < width; i++)
                            digits[i] = states[random.Next(4)];
                        int length = ValueText.Of(Bits(new string(digits)), radix).Length;
                        Assert.LessOrEqual(length, ValueText.MaxChars(width, radix), $"{new string(digits)} in {radix}");
                    }

                    Assert.AreEqual(longest, ValueText.MaxChars(width, radix), $"{width} bits in {radix}");
                }
            }
        }

        [Test]
        public void MaxChars_DecimalDigits_AgreeWithBigInteger_AtEveryWidthToThreeHundred_AndWhereTheFormulaIsClosest()
        {
            // 325,147 is where w log10 2 comes nearest a whole number at any width up to MaxWidth, 1.6e-7
            // above one; the others are the next nearest on the way there, above and below. MaxWidth
            // itself is left out: formatting 2^(2^20) takes BigInteger five seconds and tests nothing
            // the nearest case does not.
            var widths = new List<int>();
            for (int w = 2; w <= 300; w++)
                widths.Add(w);
            widths.AddRange(new[] { 2136, 13301, 28738, 42039, 70777, 254370, 325147, 325148 });

            foreach (int width in widths)
            {
                int unsignedChars = (BigInteger.Pow(2, width) - 1).ToString(CultureInfo.InvariantCulture).Length;
                int signedChars = 1 + BigInteger.Pow(2, width - 1).ToString(CultureInfo.InvariantCulture).Length;

                Assert.AreEqual(unsignedChars, ValueText.MaxChars(width, WaveRadix.Unsigned), $"unsigned, {width} bits");
                Assert.AreEqual(signedChars, ValueText.MaxChars(width, WaveRadix.Signed), $"signed, {width} bits");
            }
        }

        [Test]
        public void MaxChars_Symbolic_IsTheLongestNameOrTheHexWidth()
        {
            var wordy = new EnumInfo("state_t", 2, new[]
            {
                new EnumMemberInfo("IDLE", LogicVec.FromUInt64(0, 2)),
                new EnumMemberInfo("RUNNING", LogicVec.FromUInt64(1, 2)),
            });
            Assert.AreEqual(7, ValueText.MaxChars(2, WaveRadix.Symbolic, wordy), "RUNNING");
            Assert.AreEqual(1, ValueText.MaxChars(2, WaveRadix.Hex, wordy), "the enum is read only in Symbolic");

            var terse = new EnumInfo("op_t", 32, new[]
            {
                new EnumMemberInfo("A", LogicVec.FromUInt64(0, 32)),
                new EnumMemberInfo("B", LogicVec.FromUInt64(1, 32)),
            });
            Assert.AreEqual(8, ValueText.MaxChars(32, WaveRadix.Symbolic, terse), "a value no member holds is eight hex digits");

            Assert.AreEqual(2, ValueText.MaxChars(8, WaveRadix.Symbolic, wordy), "names of another width can never be written on it");

            var power = new EnumInfo("power_t", 1, new[] { new EnumMemberInfo("OFF", LogicVec.FromUInt64(0, 1)) });
            Assert.AreEqual(3, ValueText.MaxChars(1, WaveRadix.Symbolic, power), "one bit, named");
        }

        // -----------------------------------------------------------------
        // The cache
        // -----------------------------------------------------------------

        [Test]
        public void TheCache_FormatsOnce_AndHandsBackTheSameString()
        {
            var cache = new TextCache();

            string first = cache.Get(4, 0, Byte(0x3a), WaveRadix.Hex);
            Assert.AreEqual("3A", first);
            Assert.AreEqual(1, cache.Made);

            string again = cache.Get(4, 0, Byte(0x3a), WaveRadix.Hex);
            Assert.AreSame(first, again, "the same instance, not an equal one");
            Assert.AreEqual(1, cache.Made, "and nothing formatted the second time");
        }

        [Test]
        public void TheCache_KeepsEachRadix_SoSwitchingBackCostsNothing()
        {
            var cache = new TextCache();

            string hex = cache.Get(0, 3, Byte(253), WaveRadix.Hex);
            string signed = cache.Get(0, 3, Byte(253), WaveRadix.Signed);
            Assert.AreEqual("FD", hex);
            Assert.AreEqual("-3", signed);
            Assert.AreEqual(2, cache.Made);

            Assert.AreSame(hex, cache.Get(0, 3, Byte(253), WaveRadix.Hex));
            Assert.AreEqual(2, cache.Made);

            Assert.AreEqual("RUN", cache.Get(1, 0, LogicVec.FromUInt64(1, 2), WaveRadix.Symbolic, States()));
            Assert.AreEqual("FD", cache.Get(-7, 1000, Byte(253), WaveRadix.Hex), "any key, and an index far past the rest");
        }

        [Test]
        public void TheCache_AsksAgainForNothing_OnceItsWindowIsFormatted()
        {
            var cache = new TextCache();
            var values = new LogicVec[300];
            for (int i = 0; i < values.Length; i++)
                values[i] = Byte((ulong)(i * 37 % 256));
            EnumInfo states = States();

            void Window()
            {
                for (int key = 0; key < 3; key++)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        cache.Get(key, i, values[i], WaveRadix.Hex);
                        cache.Get(key, i, values[i], WaveRadix.Signed);
                    }

                    cache.Get(10 + key, 0, LogicVec.FromUInt64(2, 2), WaveRadix.Symbolic, states);
                }
            }

            Window();
            int made = cache.Made;

            long before = GC.GetAllocatedBytesForCurrentThread();
            Window();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "a frame over a window already formatted allocated");
            Assert.AreEqual(made, cache.Made, "and formatted nothing");
        }

        [Test]
        public void TheCache_Cleared_FormatsANewRunsValues()
        {
            var cache = new TextCache();
            Assert.AreEqual("01", cache.Get(0, 0, Byte(1), WaveRadix.Hex));

            cache.Clear();
            Assert.AreEqual(0, cache.Made);
            Assert.AreEqual("02", cache.Get(0, 0, Byte(2), WaveRadix.Hex), "the run made again holds a new value at the same index");
        }

        [Test]
        public void TheCache_RefusesANegativeIndexOrARadixThatIsNotOne()
        {
            var cache = new TextCache();
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(0, -1, Byte(1), WaveRadix.Hex));
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(0, 0, Byte(1), (WaveRadix)6));
        }

        // -----------------------------------------------------------------
        // Writing a time
        // -----------------------------------------------------------------

        [Test]
        public void Write_WritesTheTimeAndItsUnit()
        {
            Assert.AreEqual("12.5 ns", Written(12.5, 1, "ns"));
            Assert.AreEqual("0 ns", Written(0, 0, "ns"));
            Assert.AreEqual("3.25", Written(3.25, 2, null), "no unit, no space");
            Assert.AreEqual("3.25", Written(3.25, 2, ""));
            Assert.AreEqual("12.50 ns", Written(12.5, 2, "ns"), "every decimal asked for, so an axis's labels line up");
            Assert.AreEqual("-2.5 ns", Written(-2.5, 1, "ns"));
            Assert.AreEqual("0.0 ns", Written(-0.04, 1, "ns"), "rounded to nothing is never -0");
            Assert.AreEqual("10.0 ns", Written(9.96, 1, "ns"), "the fraction's rounding carries into the whole part");
            Assert.AreEqual("0.3 ns", Written(0.1 * 3, 1, "ns"), "0.30000000000000004");
            Assert.AreEqual("0.13 ns", Written(0.125, 2, "ns"), "half away from zero");
            Assert.AreEqual("1000000000000000 ps", Written(1e15, 0, "ps"));
        }

        [Test]
        public void Write_AgreesWithInvariantFixedPoint_AlongAnAxis()
        {
            foreach (double step in new[] { 1, 2, 5, 0.5, 0.25, 0.2, 0.1, 0.05, 0.025, 0.01 })
            {
                int decimals = TimeText.Decimals(step);
                string format = "F" + decimals.ToString(CultureInfo.InvariantCulture);

                for (int k = 0; k <= 4000; k++)
                {
                    double time = k * step;
                    Assert.AreEqual(time.ToString(format, CultureInfo.InvariantCulture) + " ns", Written(time, decimals, "ns"),
                        $"tick {k} of {step}");
                }
            }
        }

        [Test]
        public void Write_AllocatesNothing()
        {
            var into = new char[TimeText.MaxLength(2, "ns")];
            int written = 0;
            for (int k = 0; k < 100; k++)
                written += TimeText.Write(k * 0.25, 2, "ns", into);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 10_000; k++)
                written += TimeText.Write(k * 0.25 - 50, 2, "ns", into);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "writing a time allocated");
            Assert.Greater(written, 0);
        }

        [Test]
        public void Write_MaxLength_HoldsTheLongestTime_AndAShorterBufferIsRefused()
        {
            Assert.AreEqual("-9100000000000000000.000000000 ns", Written(-9.1e18, 9, "ns"));

            Assert.Throws<ArgumentException>(() => TimeText.Write(12.5, 1, "ns", new char[6]));
            Assert.Throws<ArgumentNullException>(() => TimeText.Write(1, 0, "ns", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Write(double.NaN, 0, "ns", new char[32]));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Write(double.PositiveInfinity, 0, "ns", new char[32]));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Write(1e19, 0, "ns", new char[32]));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Write(1, -1, "ns", new char[32]));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Write(1, TimeText.MaxDecimals + 1, "ns", new char[64]));
        }

        [Test]
        public void Decimals_AreAsManyAsATickStepNeeds()
        {
            Assert.AreEqual(0, TimeText.Decimals(1));
            Assert.AreEqual(1, TimeText.Decimals(0.5));
            Assert.AreEqual(2, TimeText.Decimals(0.25));
            Assert.AreEqual(1, TimeText.Decimals(0.1));
            Assert.AreEqual(1, TimeText.Decimals(0.2));
            Assert.AreEqual(2, TimeText.Decimals(0.05));
            Assert.AreEqual(1, TimeText.Decimals(2.5));
            Assert.AreEqual(0, TimeText.Decimals(1000));
            Assert.AreEqual(3, TimeText.Decimals(0.001));
            Assert.AreEqual(1, TimeText.Decimals(0.1 + 0.2), "0.30000000000000004 is 0.3");
            Assert.AreEqual(9, TimeText.Decimals(1e-9));
            Assert.AreEqual(TimeText.MaxDecimals, TimeText.Decimals(1.0 / 3), "no decimals make a third whole");

            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Decimals(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Decimals(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Decimals(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeText.Decimals(double.PositiveInfinity));
        }

        // -----------------------------------------------------------------
        // Reading a time
        // -----------------------------------------------------------------

        [Test]
        public void TryParse_ReadsATimeInAnyUnit_AsWholeProjectUnits()
        {
            Assert.AreEqual(50, Parsed("50"));
            Assert.AreEqual(50, Parsed("50ns"));
            Assert.AreEqual(50, Parsed("50 ns"));
            Assert.AreEqual(50, Parsed("  50 ns  "));
            Assert.AreEqual(1500, Parsed("1.5 us"));
            Assert.AreEqual(2, Parsed("2ps", "ps"));
            Assert.AreEqual(2, Parsed("2000ps"));
            Assert.AreEqual(1_000_000_000, Parsed("1 s"));
            Assert.AreEqual(500_000, Parsed("0.5 ms"));
            Assert.AreEqual(3, Parsed("3000000 fs"));
            Assert.AreEqual(0, Parsed("0"));
            Assert.AreEqual(0, Parsed("-0"), "minus nothing is nothing, not before the run");
            Assert.AreEqual(0, Parsed("0.0 us"));
        }

        [Test]
        public void TryParse_ReadsTheNumberExactly()
        {
            Assert.AreEqual(1005, Parsed("1.005 us"), "1.005 * 1000 in doubles is 1004.9999999999999");
            Assert.AreEqual(1, Parsed("1.000000000000000000000000 ns"), "trailing zeros of any length");
            Assert.AreEqual(500, Parsed(".5 us"));
            Assert.AreEqual(50, Parsed("50."));
            Assert.AreEqual(7, Parsed("007"));
            Assert.AreEqual(long.MaxValue, Parsed("9223372036854775807"));
            Assert.AreEqual(long.MaxValue, Parsed("9223372036854775.807 us"));
        }

        [Test]
        public void TryParse_TakesUnderscoresAnyCaseAndMicro()
        {
            Assert.AreEqual(1000, Parsed("1_000"));
            Assert.AreEqual(1_000_500, Parsed("1_000.5 us"));
            Assert.AreEqual(50, Parsed("50 NS"));
            Assert.AreEqual(2000, Parsed("2 Us"));
            Assert.AreEqual(1500, Parsed("1.5 \u00b5s"), "the micro sign");
            Assert.AreEqual(1500, Parsed("1.5\u03bcs"), "the Greek mu");
        }

        [Test]
        public void TryParse_RefusesATimeThatIsNotWhole_AndSaysWhatItWouldBe()
        {
            StringAssert.Contains("is 1.5 ns", Refused("1.5"));
            StringAssert.Contains("whole number of ns", Refused("1.5"));
            StringAssert.Contains("is 0.002 ns", Refused("2ps"));
            StringAssert.Contains("is 1.000000000000000000001 ns", Refused("1.000000000000000000001"),
                "a long fraction is not whole, not too large");
            StringAssert.Contains("is 0.0000015 ns", Refused("1.5 fs"));
        }

        [Test]
        public void TryParse_RefusesANegativeTime()
        {
            StringAssert.Contains("0 or later", Refused("-5"));
            StringAssert.Contains("0 or later", Refused("-1.5 us"));
        }

        [Test]
        public void TryParse_RefusesAUnitThatIsNotOne()
        {
            string why = Refused("5 qs");
            StringAssert.Contains("'qs' is not a time unit", why);
            StringAssert.Contains("s, ms, us, ns, ps or fs", why, "and gives the way out");
            StringAssert.Contains("not a time unit", Refused("5 sec"));
            StringAssert.Contains("not a time unit", Refused("5 ss"));
            StringAssert.Contains("not a time unit", Refused("5 cycles"));
        }

        [Test]
        public void TryParse_RefusesWhatIsNotATime()
        {
            foreach (string garbage in new[] { "abc", "5 5", "1.2.3", "1,5", "ns", "5 ns x", "_5", ".", "- 5", "1e3", "+5", "5-" })
                StringAssert.Contains("is not a time", Refused(garbage), garbage);

            StringAssert.Contains("Write a time", Refused(""));
            StringAssert.Contains("Write a time", Refused("   "));
            StringAssert.Contains("Write a time", Refused(null));
        }

        [Test]
        public void TryParse_RefusesATimePastALong()
        {
            StringAssert.Contains("later than any run can reach", Refused("9223372036854775808"));
            StringAssert.Contains("later than any run can reach", Refused("10000 s", "fs"));
            StringAssert.Contains("later than any run can reach", Refused("99999999999999999999999"));
        }

        [Test]
        public void TryParse_InAProjectWhoseUnitIsNotATimeUnit_ReadsPlainNumbersAndItsOwnName()
        {
            Assert.AreEqual(5, Parsed("5", "cycles"));
            Assert.AreEqual(5, Parsed("5 cycles", "cycles"));
            Assert.AreEqual(5, Parsed("5 Cycles", "cycles"));
            StringAssert.Contains("is 1.5 cycles", Refused("1.5", "cycles"));
            StringAssert.Contains("no time unit converts to", Refused("5 ns", "cycles"));
            StringAssert.Contains("not a time unit", Refused("5 ticks", "cycles"));
            Assert.AreEqual(5, Parsed("5", null));
            StringAssert.Contains("no unit to convert to", Refused("5 ns", null));
        }

        // -----------------------------------------------------------------
        // Every culture alike
        // -----------------------------------------------------------------

        [Test]
        public void Text_IsTheSame_InACultureWithACommaForAPoint()
        {
            InCulture(Contrary(), () =>
            {
                Assert.AreEqual(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, "the culture is really in force");
                Assert.AreEqual("-3", ValueText.Of(LogicVec.FromInt64(-3, 8), WaveRadix.Signed));
                Assert.AreEqual("-128", ValueText.Of(Byte(128), WaveRadix.Signed));
                Assert.AreEqual("4294967295", ValueText.Of(LogicVec.FromUInt64(uint.MaxValue, 32), WaveRadix.Unsigned), "no thousands separator");
                Assert.AreEqual("12.5 ns", Written(12.5, 1, "ns"));
                Assert.AreEqual("-2.25 ns", Written(-2.25, 2, "ns"));
                Assert.AreEqual(2, TimeText.Decimals(0.25));
                Assert.AreEqual(1500, Parsed("1.5 us"));
                StringAssert.Contains("is not a time", Refused("1,5 us"), "a comma is not a point anywhere");
                StringAssert.Contains("is 1.5 ns", Refused("1.5"), "and a refusal writes its number the same way");
            });
        }
    }
}
