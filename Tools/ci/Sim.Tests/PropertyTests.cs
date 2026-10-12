using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// What must hold of every run: the same answer whatever order a design was built in, the same
    /// answer run in slices or at once, a replay that matches, and a VCD that reads back as the run.
    /// </summary>
    public class PropertyTests
    {
        /// <summary>A random feed-forward circuit of gates and assignments, delays 0 to 4, and random stimulus.</summary>
        private sealed class Recipe
        {
            public readonly List<(string Name, int Width, bool Variable)> Signals = new List<(string, int, bool)>();
            public readonly List<(GateKind Kind, string Name, string Output, Delay Delay, string[] Inputs)> Gates = new List<(GateKind, string, string, Delay, string[])>();
            public readonly List<(long Time, string Signal, char Bit)> Writes = new List<(long, string, char)>();

            public static Recipe Random(int seed)
            {
                var random = new Random(seed);
                var recipe = new Recipe();
                var available = new List<string>();

                for (int i = 0; i < 8; i++)
                {
                    recipe.Signals.Add(($"in{i}", 1, true));
                    available.Add($"in{i}");
                }

                GateKind[] kinds = { GateKind.And, GateKind.Or, GateKind.Nand, GateKind.Nor, GateKind.Xor, GateKind.Xnor, GateKind.Not, GateKind.Buf };

                for (int g = 0; g < 40; g++)
                {
                    GateKind kind = kinds[random.Next(kinds.Length)];
                    int inputs = kind == GateKind.Not || kind == GateKind.Buf ? 1 : 2 + random.Next(2);
                    string[] chosen = Enumerable.Range(0, inputs).Select(_ => available[random.Next(available.Count)]).ToArray();
                    Delay delay = random.Next(3) == 0 ? Delay.Of(random.Next(5), random.Next(5)) : Delay.Of(random.Next(5));

                    string output = $"n{g}";
                    recipe.Signals.Add((output, 1, false));
                    recipe.Gates.Add((kind, $"g{g}", output, delay, chosen));
                    available.Add(output);
                }

                for (int w = 0; w < 60; w++)
                    recipe.Writes.Add((random.Next(100), $"in{random.Next(8)}", "01xz"[random.Next(4)]));

                return recipe;
            }

            /// <summary>Builds it, signals and gates added in an order shuffled by <paramref name="shuffle"/> (0: as listed).</summary>
            public (Design, Stimulus) Build(int shuffle)
            {
                var random = new Random(shuffle);
                var b = new NetlistBuilder("random");
                var ids = new Dictionary<string, SignalId>();

                foreach (var s in shuffle == 0 ? Signals : Signals.OrderBy(_ => random.Next()).ToList())
                    ids[s.Name] = s.Variable ? b.Variable(s.Name, s.Width) : b.Net(s.Name, s.Width);

                foreach (var g in shuffle == 0 ? Gates : Gates.OrderBy(_ => random.Next()).ToList())
                    b.Gate(g.Kind, g.Name, ids[g.Output], g.Delay, g.Inputs.Select(i => ids[i]).ToArray());

                var stimulus = new Stimulus();
                foreach (var w in Writes.OrderBy(w => w.Time))
                    stimulus.Set(w.Time, ids[w.Signal], LogicVec.FromBinary(w.Bit.ToString()));

                return (b.Build(), stimulus);
            }
        }

        /// <summary>A run reduced to what the order of construction must not change: by name, in canonical order.</summary>
        private static string Fingerprint(Simulator sim)
        {
            Design design = sim.Design;
            var lines = new List<string>();

            for (int s = 0; s < design.Signals.Count; s++)
            {
                var id = design.SignalAt(s);
                lines.Add($"{design.Signals[s].Name}: " + string.Join(" ", Probes.Settled(sim.History, id)));
            }

            for (int i = 0; i < sim.History.TransitionCount; i++)
            {
                Transition t = sim.History.TransitionAt(i);
                lines.Add($"T {design.Drivers[t.Driver].Name} {t.ScheduledAt}->{t.DueAt} {t.Value.Format(Radix.Binary)} {t.Outcome} {t.EndedAt}");
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void TheOrderADesignIsBuiltIn_ChangesNothing(int seed)
        {
            Recipe recipe = Recipe.Random(seed);

            (Design design, Stimulus stimulus) = recipe.Build(0);
            var listed = new Simulator(design, stimulus);
            listed.RunTo(200);
            string expected = Fingerprint(listed);

            for (int shuffle = 1; shuffle <= 4; shuffle++)
            {
                (Design d, Stimulus s) = recipe.Build(shuffle * 7919 + seed);
                var shuffled = new Simulator(d, s);
                shuffled.RunTo(200);
                Assert.AreEqual(expected, Fingerprint(shuffled), $"shuffle {shuffle} gave another history");
            }
        }

        [Test]
        public void ARunInSlices_IsTheSameRunAsInOneGo()
        {
            (Design design, Stimulus stimulus) = Recipe.Random(11).Build(0);

            var whole = new Simulator(design, stimulus);
            whole.RunTo(200);

            var sliced = new Simulator(design, stimulus);
            RunResult result;
            int calls = 0;
            do
            {
                result = sliced.RunTo(200, maxEvents: 1);
                calls++;
            }
            while (result.Status == RunStatus.Paused);

            Assert.Greater(calls, 10, "sanity: the budget really did slice the run");
            Assert.AreEqual(Fingerprint(whole), Fingerprint(sliced));
        }

        [Test]
        public void AReplay_WithTheSameStimulus_IsTheSameRun_AndWithAnotherIsAFreshOne()
        {
            (Design design, Stimulus stimulus) = Recipe.Random(12).Build(0);
            var session = new SimSession(design, stimulus);
            session.RunTo(150);

            Assert.AreEqual(Fingerprint(session.Simulator), Fingerprint(session.Replay(stimulus).Simulator));

            var edited = new Stimulus().Set(0, design.Find("in0"), Logic4.One);
            var fresh = new Simulator(design, edited);
            fresh.RunTo(150);

            SimSession replayed = session.Replay(edited);
            Assert.AreEqual(150, replayed.Reached);
            Assert.AreEqual(Fingerprint(fresh), Fingerprint(replayed.Simulator));
        }

        [Test]
        public void SteppingAndSeeking_ReadTheHistory()
        {
            (Design design, Stimulus stimulus) = Probes.Build();
            var session = new SimSession(design, stimulus);
            session.RunTo(40);

            Assert.AreEqual(1, session.StepForward(0), "the first change after time 0 is P3's, P4's and P11's at 1");
            Assert.AreEqual(15, session.StepBack(16), "the last change before 16 is P6's x and P9's fall at 15");

            SignalId y9 = design.Find("y9");
            Assert.AreEqual("x", session.ValueAt(y9, 1).Format(Radix.Binary));
            Assert.AreEqual("1", session.ValueAt(y9, 13).Format(Radix.Binary));
            Assert.AreEqual("0", session.ValueAt(y9, 39).Format(Radix.Binary));
        }

        [Test]
        public void AVcd_ReadsBackAsTheRun()
        {
            (Design design, Stimulus stimulus) = Probes.Build();
            var sim = new Simulator(design, stimulus);
            sim.RunTo(40);

            var text = new StringWriter();
            VcdWriter.Write(text, design, sim.History, 40);
            Dictionary<string, VcdTrace> traces = VcdReader.Read(new StringReader(text.ToString()));

            for (int s = 0; s < design.Signals.Count; s++)
            {
                string name = $"{Probes.Module}.{design.Signals[s].Name}";
                Assert.IsTrue(traces.ContainsKey(name), $"the VCD has no {name}");

                string[] fromVcd = traces[name].Changes.Select(c => $"{c.Time} {c.Bits}").ToArray();
                Assert.That(fromVcd, Is.EqualTo(Probes.Settled(sim.History, design.SignalAt(s))), name);
            }
        }
    }
}
