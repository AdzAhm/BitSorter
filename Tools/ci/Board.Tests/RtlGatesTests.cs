using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// GATES (Part 15h-3): every operator part and mux of up to four bits opened into gates computes what the box does, for
    /// every input of 0s and 1s; what stays a box says why.
    /// </summary>
    public class RtlGatesTests
    {
        private static IEnumerable<OperatorPart> Openable()
        {
            foreach (OperatorKind op in Operators.All)
            {
                if (op == OperatorKind.Divide || op == OperatorKind.Modulo || op == OperatorKind.CaseEqual || op == OperatorKind.CaseNotEqual)
                    continue;
                for (int w = 1; w <= RtlGates.MostBits; w++)
                    foreach (int bw in Operators.BOwnWidth(op) ? new[] { 1, 2, 3 } : new[] { 0 })
                        foreach (bool signed in Operators.SignMatters(op) ? new[] { false, true } : new[] { false })
                            yield return new OperatorPart { Op = op, Width = w, BWidth = bw, Signed = signed };
            }
        }

        private static IEnumerable<TestCaseData> Cases() =>
            Openable().Select(p => new TestCaseData(p).SetName($"{p.Op} a{p.Width} b{p.WidthOfB}{(p.Signed ? " signed" : "")}"));

        [TestCaseSource(nameof(Cases))]
        public void AnOperatorOpenedIntoGates_ComputesWhatItsBoxDoes(OperatorPart part)
        {
            Project gates = RtlGates.Open(part, "gates", out string why);
            Assert.IsNotNull(gates, why);
            Run(gates, part, (inputs) => OperatorPartTests.Reference(part.Op, inputs["a"], inputs.TryGetValue("b", out LogicVec b) ? b : default, part.Signed && Operators.SignMatters(part.Op)));
        }

        [TestCase(2, 1), TestCase(2, 3), TestCase(4, 1), TestCase(4, 2), TestCase(8, 1)]
        public void AMuxOpenedIntoGates_PassesTheInputItsSelectPicks(int inputs, int width)
        {
            var mux = new MuxPart { Inputs = inputs, Width = width };
            Project gates = RtlGates.Open(mux, "gates", out string why);
            Assert.IsNotNull(gates, why);
            Run(gates, mux, values =>
            {
                values["s"].TryGetUInt64(out ulong select);
                return values["d" + select];
            });
        }

        /// <summary>A part the board's rules refuse -- as one read from a file may be -- opens into nothing, and says why rather than throwing.</summary>
        [Test]
        public void APartTheRulesRefuse_OpensIntoNothing_AndSaysWhy()
        {
            foreach (Part part in new Part[]
            {
                new OperatorPart { Op = OperatorKind.Add, Width = 0 },
                new OperatorPart { Op = OperatorKind.Add, Width = -3 },
                new OperatorPart { Op = OperatorKind.And, Width = 4, BWidth = 2 },
                new MuxPart { Inputs = 1 },
                new MuxPart { Inputs = 16 },
            })
            {
                Assert.IsNull(RtlGates.Open(part, "g", out string why), part.ToString());
                StringAssert.Contains("the board takes", why);
            }
        }

        [Test]
        public void WhatStaysABox_SaysWhy()
        {
            Assert.IsNull(RtlGates.Open(new OperatorPart { Op = OperatorKind.Add, Width = 5 }, "g", out string wide));
            StringAssert.Contains("past 4 bits", wide);
            Assert.IsNull(RtlGates.Open(new OperatorPart { Op = OperatorKind.Divide, Width = 2 }, "g", out string divide));
            StringAssert.Contains("subtractor", divide);
            Assert.IsNull(RtlGates.Open(new OperatorPart { Op = OperatorKind.CaseEqual, Width = 2 }, "g", out string caseEqual));
            StringAssert.Contains("x and z", caseEqual);
            Assert.IsNull(RtlGates.Open(new MuxPart { Width = 8 }, "g", out string wideMux));
            StringAssert.Contains("past 4 bits", wideMux);
        }

        [Test]
        public void ASumWithItsCarry_IsTheRippleOfItsNumbers_ItsCarryOnTop([Values(1, 2, 3, 4)] int width)
        {
            Project gates = RtlGates.OpenWithCarry(OperatorKind.Add, width, "gates", out string why);
            Assert.IsNotNull(gates, why);
            var part = new OperatorPart { Op = OperatorKind.Add, Width = width };
            Run(gates, part, v => LogicVec.FromUInt64(Number(v["a"]) + Number(v["b"]), width + 1));
            Assert.AreEqual(2 * width - 1, gates.Find("gates").Parts.OfType<GatePart>().Count(g => g.Kind == GateKind.Xor),
                "a half adder's XOR, then two a full adder");
        }

        /// <summary>{borrow, d} = a - b: the difference of the two widened with 0s, its top bit the borrow, on every value.</summary>
        [Test]
        public void ADifferenceWithItsBorrow_IsTheLanguagesOfTheTwoWidened([Values(1, 2, 3, 4)] int width)
        {
            Project gates = RtlGates.OpenWithCarry(OperatorKind.Subtract, width, "gates", out string why);
            Assert.IsNotNull(gates, why);
            var part = new OperatorPart { Op = OperatorKind.Subtract, Width = width };
            ulong mask = (1UL << (width + 1)) - 1;
            Run(gates, part, v => LogicVec.FromUInt64((Number(v["a"]) - Number(v["b"])) & mask, width + 1));
        }

        private static ulong Number(LogicVec value) => value.TryGetUInt64(out ulong number) ? number : throw new ArgumentException("not a number");

        [Test]
        public void ASumWithItsCarry_PastFourBits_StaysABox()
        {
            Assert.IsNull(RtlGates.OpenWithCarry(OperatorKind.Add, 5, "gates", out string why));
            StringAssert.Contains("5 bits", why);
            Assert.IsNull(RtlGates.OpenWithCarry(OperatorKind.Multiply, 4, "gates", out why));
            Assert.IsNotNull(why);
        }

        [Test]
        public void AnAdderOpened_IsTheTextbooksRipple()
        {
            Module adder = RtlGates.Open(new OperatorPart { Op = OperatorKind.Add, Width = 4 }, "gates", out _).Find("gates");
            int xors = adder.Parts.OfType<GatePart>().Count(g => g.Kind == GateKind.Xor);
            Assert.AreEqual(4 + 3, xors, "a half adder's XOR, then two a full adder");
        }

        /// <summary>
        /// The opening, run on every combination of its inputs -- or, past 2^12 of them, on 4,096 drawn at random -- held to
        /// <paramref name="expected"/> at each.
        /// </summary>
        private static void Run(Project gates, Part part, Func<Dictionary<string, LogicVec>, LogicVec> expected)
        {
            CompiledProject compiled = BoardCompiler.Compile(gates);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, "gates");
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            Design design = elaboration.Design;

            Pin[] ins = part.Pins(null).Where(p => p.Direction == PinDirection.In).ToArray();
            int bits = ins.Sum(p => p.Width);
            SignalId Signal(string name) => design.SignalAt(Enumerable.Range(0, design.Signals.Count).Single(i => design.Signals[i].Name == "gates." + name));
            SignalId y = Signal("y");

            const long gap = 1000;
            var random = new Random(bits * 31 + ins.Length);
            long count = bits <= 12 ? 1L << bits : 4096;
            var stimulus = new BitSorter.Sim.Stimulus();
            var applied = new List<Dictionary<string, LogicVec>>();
            for (long v = 0; v < count; v++)
            {
                ulong all = bits <= 12 ? (ulong)v : (ulong)random.Next() << 31 | (ulong)random.Next();
                var values = new Dictionary<string, LogicVec>();
                int at = 0;
                foreach (Pin pin in ins)
                {
                    LogicVec value = LogicVec.FromUInt64((all >> at) & ((1UL << pin.Width) - 1), pin.Width);
                    values[pin.Name] = value;
                    stimulus.Set(v * gap, Signal(pin.Name), value);
                    at += pin.Width;
                }
                applied.Add(values);
            }

            var sim = new Simulator(design, stimulus, null);
            var wrong = new List<string>();
            for (int v = 0; v < applied.Count; v++)
            {
                sim.RunTo(v * gap + gap - 1);
                string want = expected(applied[v]).Format(Radix.Binary), got = sim.Value(y).Format(Radix.Binary);
                if (want != got)
                    wrong.Add(string.Join(" ", applied[v].Select(p => p.Key + "=" + p.Value.Format(Radix.Binary))) + $": gates give {got}, the box {want}");
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong.Take(12)));

            // Every gate feeds the answer: no carry out an adder drops, no sum a comparison never reads (the review of #107).
            Module gates2 = gates.Find("gates");
            Netlist nets = Netlist.Of(gates, gates2);
            foreach (Net net in nets.Nets)
                if (net.Drivers.Count > 0 && !(gates2.FindPart(net.Drivers[0].Part) is PortPart))
                    Assert.Greater(net.Pins.Count, net.Drivers.Count, $"{gates2.FindPart(net.Drivers[0].Part).Name}.{net.Drivers[0].Pin} feeds nothing");
        }
    }
}
