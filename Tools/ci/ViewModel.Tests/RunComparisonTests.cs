using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// COMPARE WITH MODELSIM's comparison (Part 15i-5): the run held to another simulator's dump of it, matched by full
    /// name -- its own dump agreeing with it, a value changed found at its moment on its signal, a signal the dump has not
    /// got listed rather than taken for a difference. That ModelSim's own dump of an exported project agrees is
    /// <c>ModelSimCompareOracleTests</c>'.
    /// </summary>
    public class RunComparisonTests
    {
        private static WorkbenchSession Counter()
        {
            double now = 0;
            var session = new WorkbenchSession(WorkbenchSamples.Counter(), () => now += 0.01);
            session.Finish();
            return session;
        }

        /// <summary>The run's own dump, written as EXPORT writes a VCD and read as COMPARE reads ModelSim's.</summary>
        private static Dictionary<string, VcdTrace> OwnDump(WorkbenchSession session)
        {
            WorkbenchModel model = session.Run.Model;
            var text = new StringWriter();
            VcdWriter.Write(text, model.Session.Design, model.Session.History, model.End);
            return VcdReader.Read(new StringReader(text.ToString()));
        }

        private static RunComparison Compare(WorkbenchSession session, IReadOnlyDictionary<string, VcdTrace> theirs) =>
            RunComparison.Of(session.Run.Model.Session.Design, session.Run.Model.Session.History, session.Run.Model.End, theirs);

        [Test]
        public void TheRunsOwnDump_AgreesWithIt()
        {
            WorkbenchSession session = Counter();
            RunComparison comparison = Compare(session, OwnDump(session));
            Assert.IsTrue(comparison.Agree, comparison.Signal + " at " + comparison.Time);
            Assert.Greater(comparison.Compared, 3);
            CollectionAssert.IsEmpty(comparison.Missing);
            StringAssert.StartsWith("ModelSim agrees: ", comparison.Verdict(session.Unit));
            Assert.AreEqual(TranscriptSeverity.Info, comparison.Lines(session.Unit).Single().Severity);
        }

        [Test]
        public void AValueChanged_IsTheFirstDifference_AtItsMoment_OnItsSignal()
        {
            WorkbenchSession session = Counter();
            Dictionary<string, VcdTrace> theirs = OwnDump(session);
            VcdTrace changed = theirs.Values.First(t => t.Width > 1 && t.Changes.Count > 2);
            (long time, string bits) = changed.Changes[2];
            string other = new string(bits.Select(c => c == '0' ? '1' : '0').ToArray());
            changed.Changes[2] = (time, other);

            RunComparison comparison = Compare(session, theirs);
            Assert.IsFalse(comparison.Agree);
            Assert.LessOrEqual(comparison.Time, time, "the first difference, however many signals follow it");
            if (comparison.Time == time && comparison.Signal == changed.Name)
            {
                Assert.AreEqual(bits, comparison.Ours);
                Assert.AreEqual(other, comparison.Theirs);
            }
            TranscriptLine line = comparison.Lines(session.Unit)[0];
            Assert.AreEqual(TranscriptSeverity.Warning, line.Severity);
            Assert.AreEqual(comparison.Time, line.Time, "at its moment, so a click on it goes there");
            StringAssert.StartsWith("ModelSim differs first at t = ", comparison.Verdict(session.Unit));
        }

        [Test]
        public void ADifferenceFromTheStart_ThatNeverEnds_IsADifferenceAtTimeZero()
        {
            // Every value of one bus inverted, from t = 0 on: the two never agree on it, so it is no start, as a reset taken
            // at once is, but a difference -- shown as the run agreeing, the review found.
            WorkbenchSession session = Counter();
            Dictionary<string, VcdTrace> theirs = OwnDump(session);
            VcdTrace changed = theirs.Values.First(t => t.Width > 1 && t.Changes.Count > 2 && t.Changes[0].Bits.All(c => c == '0' || c == '1'));
            for (int i = 0; i < changed.Changes.Count; i++)
                changed.Changes[i] = (changed.Changes[i].Time, new string(changed.Changes[i].Bits.Select(c => c == '0' ? '1' : '0').ToArray()));

            RunComparison comparison = Compare(session, theirs);
            Assert.IsFalse(comparison.Agree);
            Assert.AreEqual(0, comparison.Time);
            CollectionAssert.DoesNotContain(comparison.DifferAtStart, changed.Name, "never once the same: no start");
        }

        [Test]
        public void ARunPlayedOnPastItsStop_IsComparedOnlyAsFarAsTheDumpGoes()
        {
            // ModelSim's dump ends at the $stop; PLAY carries the run on past it. Compared to t = 119, the first moment past
            // the stop read as a difference.
            WorkbenchSession session = Counter();
            Dictionary<string, VcdTrace> theirs = OwnDump(session);
            long stop = session.Run.Model.End;
            Assert.IsTrue(session.Run.RunOn(stop * 3), "sanity: the Counter's testbench stops on $stop");
            session.Run.Finish();

            RunComparison comparison = Compare(session, theirs);
            Assert.IsTrue(comparison.Agree, comparison.Signal + " at " + comparison.Time);
            Assert.Less(comparison.Until, stop);
        }

        [Test]
        public void ASignalTheDumpHasNot_IsListed_NeverTakenForADifference()
        {
            WorkbenchSession session = Counter();
            Dictionary<string, VcdTrace> theirs = OwnDump(session);
            string gone = theirs.Keys.First();
            theirs.Remove(gone);

            RunComparison comparison = Compare(session, theirs);
            Assert.IsTrue(comparison.Agree);
            CollectionAssert.AreEqual(new[] { gone }, comparison.Missing);
            StringAssert.Contains(gone, comparison.Lines(session.Unit).Last().Text);
            StringAssert.EndsWith("; 1 signal not in its dump", comparison.Verdict(session.Unit), "the chip says so too");
        }
    }
}
