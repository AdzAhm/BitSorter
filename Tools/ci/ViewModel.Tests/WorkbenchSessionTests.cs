using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The document under edit and its runs (Part 9b-3): an edit replayed a slice a frame beside the run
    /// it replaces, taking over once it reaches the moment shown, the same history as before up to the
    /// edit, the latest edit winning, undo and redo replayed, and a change of unit relabelling everything.
    /// </summary>
    public class WorkbenchSessionTests
    {
        /// <summary>A clock that moves on by <paramref name="step"/> ms each time it is read, so each slice is a chunk or two.</summary>
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        /// <summary>
        /// A document the rules refuse -- a save edited by hand, or a newer BitSorter's -- is no session, and says why in the
        /// rules' own words; a run saved to end at long's end, which the rules let through and the slicer threw for, is refused
        /// as any such document is (save-1, save-8). A document that compiles opens as ever.
        /// </summary>
        [Test]
        public void ADocumentTheRulesRefuse_IsNoSession_AndSaysWhyInTheirWords()
        {
            Project gate = Project.New("broken") with
            {
                Modules = new[]
                {
                    new Module { Name = "broken", NextId = 2, Parts = new Part[] { new GatePart { Id = 1, Name = "g1", Kind = GateKind.And, Inputs = 1 } } },
                },
            };
            Assert.IsNull(WorkbenchSession.TryOpen(gate, () => 0, out string why));
            StringAssert.Contains("2 to 8", why);

            Project forever = ReferenceCircuit.Document();
            forever = forever with { Stimulus = forever.Stimulus with { End = long.MaxValue } };
            Assert.IsNull(WorkbenchSession.TryOpen(forever, () => 0, out why));
            StringAssert.Contains(ProjectImporter.LatestEnd.ToString(System.Globalization.CultureInfo.InvariantCulture), why);

            Assert.IsNotNull(WorkbenchSession.TryOpen(ReferenceCircuit.Document(), () => 0, out why));
            Assert.IsNull(why);
        }

        /// <summary>The reference circuit, made a few events a slice so a replay takes many frames.</summary>
        private static WorkbenchSession Reference()
        {
            var session = new WorkbenchSession(ReferenceCircuit.Document(), Ticking(5.0), chunkEvents: 3, budgetMilliseconds: 8.0);
            session.Finish();
            return session;
        }

        private static string ValueOf(RunSession run, string port, long time)
        {
            Design design = run.Model.Session.Design;
            return run.Model.Session.History.ValueAt(design.Find(run.Model.Layout.DesignPath + "." + port), time).Format(Radix.Binary);
        }

        /// <summary>Frames at the moment <paramref name="shown"/> until a replay takes over: how many it took.</summary>
        private static int UntilItSwitches(WorkbenchSession session, double shown)
        {
            for (int frames = 1; frames <= 10_000; frames++)
                if (session.Frame(shown) == SessionFrame.Switched)
                    return frames;
            Assert.Fail("the replay never took over");
            return -1;
        }

        /// <summary>Every signal's settled value changes, and every transition finished, before <paramref name="before"/>.</summary>
        private static string[] HistoryBefore(RunSession run, long before)
        {
            Design design = run.Model.Session.Design;
            History history = run.Model.Session.History;
            var lines = new List<string>();

            foreach (SignalInfo info in design.Signals)
            {
                SignalId id = design.Find(info.Name);
                for (int i = 0; i < history.ChangeCount(id); i++)
                {
                    ValueChange change = history.Change(id, i);
                    if (change.Time < before)
                        lines.Add(info.Name + " " + change);
                }
            }

            for (int i = 0; i < history.TransitionCount; i++)
            {
                Transition t = history.TransitionAt(i);
                if (t.Outcome != TransitionOutcome.Pending && t.EndedAt < before)
                    lines.Add(t.ToString());
            }

            return lines.ToArray();
        }

        // -----------------------------------------------------------------
        // An edit replayed
        // -----------------------------------------------------------------

        [Test]
        public void AnInputEdit_IsReplayedBesideTheShownRun_AndTakesOverOnceItReachesTheMomentShown()
        {
            WorkbenchSession session = Reference();
            RunSession before = session.Run;
            Assert.AreEqual("1", ValueOf(before, "a", 10), "sanity: a rises at 6 and stays");

            Bench(session.InvertInput("a", 10));
            Assert.IsTrue(session.Replaying);
            Assert.AreEqual(6, session.ChangedFrom, "a's last stretch starts at 6");
            Assert.AreSame(before, session.Run, "the run shown stays until the replay has caught up");

            int frames = UntilItSwitches(session, 20.0);
            Assert.Greater(frames, 1, "the replay is made a slice a frame, not in one go");
            Assert.AreNotSame(before, session.Run);
            Assert.IsFalse(session.Replaying);
            Assert.AreEqual(1, session.Version);
            Assert.GreaterOrEqual(session.Run.Model.Reached, 20, "it took over only once it had reached the moment shown");
            Assert.AreEqual("0", ValueOf(session.Run, "a", 10), "and it is the edited run");

            session.Finish();
            Assert.IsTrue(session.Run.Done);
        }

        [TestCase("a", 10L)]
        [TestCase("b", 15L)]
        [TestCase("en", 20L)]
        public void AReplay_IsTheShownRunChangeForChange_UpToTheEdit(string port, long at)
        {
            WorkbenchSession session = Reference();
            RunSession before = session.Run;

            Bench(session.InvertInput(port, at));
            long from = session.ChangedFrom;
            session.Finish();

            CollectionAssert.AreEqual(HistoryBefore(before, from), HistoryBefore(session.Run, from),
                $"{port} flipped from {from}: nothing before it may differ");
            Assert.AreNotEqual(ValueOf(before, port, at), ValueOf(session.Run, port, at), "sanity: and it was flipped");
        }

        [Test]
        public void ABusSetOverAStretch_IsReplayed_TheSameUpToTheEdit()
        {
            WorkbenchSession session = Reference();
            RunSession before = session.Run;

            Bench(session.SetInput("s", 12, 15, LogicVec.FromUInt64(0xF, 4)));
            Assert.AreEqual(12, session.ChangedFrom);
            session.Finish();

            CollectionAssert.AreEqual(HistoryBefore(before, 12), HistoryBefore(session.Run, 12));
            Assert.AreEqual("1111", ValueOf(session.Run, "s", 13));
            Assert.AreEqual(ValueOf(before, "s", 16), ValueOf(session.Run, "s", 16), "and back to what it had after the stretch");
        }

        [Test]
        public void TheLatestEdit_Wins_OverAReplayStillBeingMade()
        {
            WorkbenchSession session = Reference();

            Bench(session.InvertInput("a", 10));
            RunSession first = session.Replay;
            session.Frame(20.0);
            Bench(session.InvertInput("b", 15));
            Assert.AreNotSame(first, session.Replay, "the second edit started a replay of its own");

            session.Finish();
            Assert.AreEqual(1, session.Version, "only the latest replay took over");
            Assert.AreEqual("0", ValueOf(session.Run, "a", 10), "with both edits in it");
            Assert.AreEqual("0", ValueOf(session.Run, "b", 15));
        }

        [Test]
        public void UndoAndRedo_AreReplayed_LikeAnyEdit()
        {
            WorkbenchSession session = Reference();
            Bench(session.InvertInput("a", 10));
            session.Finish();
            Assert.AreEqual("0", ValueOf(session.Run, "a", 10));

            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.Replaying);
            session.Finish();
            Assert.AreEqual("1", ValueOf(session.Run, "a", 10), "undone");

            Assert.IsTrue(session.Redo());
            session.Finish();
            Assert.AreEqual("0", ValueOf(session.Run, "a", 10), "and made again");
            Assert.AreEqual(3, session.Version);

            Assert.IsFalse(session.Redo(), "nothing more to redo");
            Assert.IsFalse(session.Replaying, "and nothing replayed for it");
        }

        [Test]
        public void ARefusedEdit_ReplaysNothing()
        {
            WorkbenchSession session = Reference();
            Assert.IsFalse(session.InvertInput("s", 12).Done, "a bus has no one other value");
            Assert.IsFalse(session.SetUnit("weeks").Done);
            Assert.IsFalse(session.Replaying);
            Assert.IsTrue(session.SetUnit("ps").Done, "the unit it is in already is no change");
            Assert.IsFalse(session.Replaying, "and replays nothing");
        }

        // -----------------------------------------------------------------
        // Where a replay goes, and when it takes over
        // -----------------------------------------------------------------

        [Test]
        public void ARunRunOnPastItsStop_IsReplayedAsFar_BeforeItTakesOver()
        {
            WorkbenchSession session = Reference();
            long end = session.Run.Model.End;
            Assert.IsTrue(session.Run.RunOn(end * 2), "sanity: the reference's testbench ends on $stop");
            session.Run.Finish();
            Assert.AreEqual(end * 2, session.Run.Model.End);

            Bench(session.InvertInput("a", 10));
            UntilItSwitches(session, end + 10.0);

            Assert.AreEqual(end * 2, session.Run.Model.End, "the replay's timeline is as long as the one it replaced");
            Assert.GreaterOrEqual(session.Run.Model.Reached, end + 10, "and it took over only once it reached the moment shown");
        }

        [Test]
        public void Restart_TakesAReplayAtOnce()
        {
            WorkbenchSession session = Reference();
            Bench(session.InvertInput("a", 10));
            RunSession replay = session.Replay;
            Assert.IsFalse(replay.Done, "sanity: the replay is still being made");

            Assert.IsTrue(session.TakeReplay());
            Assert.AreSame(replay, session.Run, "the replay is the run shown now, made or not");
            Assert.IsFalse(session.Replaying);
            Assert.AreEqual(1, session.Version);
            Assert.IsFalse(session.TakeReplay(), "nothing more to take");

            while (session.Frame(0.0) == SessionFrame.Grew) { }
            Assert.IsTrue(session.Run.Done, "and it goes on being made as the shown run");
        }

        // -----------------------------------------------------------------
        // The unit
        // -----------------------------------------------------------------

        [Test]
        public void AChangeOfUnit_RelabelsEveryTime_AndChangesNoValue()
        {
            WorkbenchSession session = Reference();
            RunSession before = session.Run;
            Assert.AreEqual("ps", session.Run.Model.Unit, "a project counts in ps unless it says otherwise");

            Bench(session.SetUnit("ns"));
            session.Finish();

            Assert.AreEqual("ns", session.Unit);
            Assert.AreEqual("ns", session.Run.Model.Unit);
            string[] delays = session.Run.Model.Layout.Parts.Select(p => p.Delay).Where(d => d.Length > 0).ToArray();
            Assert.IsNotEmpty(delays, "sanity: the reference's gates have delays");
            foreach (string delay in delays)
                StringAssert.EndsWith(" ns", delay);
            CollectionAssert.AreEqual(HistoryBefore(before, long.MaxValue), HistoryBefore(session.Run, long.MaxValue), "not one value moved");

            Assert.IsTrue(session.Undo());
            session.Finish();
            Assert.AreEqual("ps", session.Run.Model.Unit, "and the unit undoes like any edit");
        }

        // -----------------------------------------------------------------
        // Playing carries over
        // -----------------------------------------------------------------

        [Test]
        public void Playback_SwitchedOntoAReplay_KeepsItsSpeedBreakpointsRunToAndPlace()
        {
            WorkbenchSession session = Reference();
            var playback = new Playback(session.Run, speed: 6);
            playback.Breakpoints.Add(Breakpoint.At(20));
            playback.RunTo = 24;
            playback.Model.Seek(12.0);
            playback.Play();
            Breakpoints breakpoints = playback.Breakpoints;

            Bench(session.InvertInput("a", 15));
            session.Finish();
            playback.Switch(session.Run);

            Assert.AreSame(session.Run.Model, playback.Model);
            Assert.AreEqual(12.0, playback.Model.Time, 1e-9, "the playhead stays where it was");
            Assert.AreEqual(6, playback.Speed);
            Assert.AreEqual(Playback.Speeds[6], playback.Model.UnitsPerSecond, "and the new model is told the speed");
            Assert.AreSame(breakpoints, playback.Breakpoints);
            Assert.AreEqual(24L, playback.RunTo);
            Assert.IsTrue(playback.Playing);

            Assert.IsTrue(playback.Frame(100.0));
            Assert.AreEqual(PauseReason.Breakpoint, playback.Reason, "and the breakpoint fires in the new run");
            Assert.AreEqual(20, playback.ReasonTime);
        }

        private static void Bench(EditResult result) => Assert.IsTrue(result.Done, "refused: " + result.Refusal);

        // -----------------------------------------------------------------
        // An edit the run cannot be made of
        // -----------------------------------------------------------------

        /// <summary>A written module whose memory holds 33.8 million bits: one fits in a design BitSorter simulates, two do not.</summary>
        private const string Big =
            "module big (input logic a, output logic y);\n" +
            "    logic [1023:0] mem [0:32999];\n" +
            "    assign y = a;\n" +
            "endmodule\n";

        /// <summary>
        /// An edit the document's rules let through -- each module checked alone -- whose whole design is past what BitSorter
        /// simulates is refused in the elaborator's words and taken back, with nothing to redo into it. It was kept, and the
        /// replay threw out of the edit; the circuit was saved half a second later, and threw again on every launch (the 16d
        /// sweep).
        /// </summary>
        [Test]
        public void AnEditWhoseDesignIsTooBigToSimulate_IsRefused_AndTakenBack()
        {
            var editor = new BoardEditor(Project.New("top"));
            Bench(editor.AddWrittenModule("big", Big));
            Bench(editor.Add("top", new BlockPart { Name = "b1", Module = "big" }, new Placement(5, 0)));
            var session = new WorkbenchSession(editor.Project, Ticking(0.01));
            session.Finish();
            Project before = session.Project;
            CatalogEntry big = PartsCatalog.For(session.Project, session.Module).Single(e => e.Module == "big");

            EditResult placed = default;
            Assert.DoesNotThrow(() => placed = session.Place(big, new Placement(5, 6)), "an edit the run cannot be made of throws nothing");
            Assert.IsFalse(placed.Done, "refused");
            StringAssert.Contains("million bits", placed.Refusal, "in the elaborator's words");
            Assert.AreSame(before, session.Project, "taken back: the document as it was");
            Assert.IsFalse(session.Editor.CanRedo, "with nothing to redo into it");
            Assert.IsFalse(session.Replaying, "and no replay of it");
            Assert.DoesNotThrow(() => session.Finish());
            Assert.AreEqual(1, session.Run.Model.Session.Design.Scopes.Count(s => s.Kind == ScopeKind.Instance && s.Module == "big"),
                "the run the one block's");
        }
    }
}
