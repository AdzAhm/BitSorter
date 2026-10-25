using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Copy and paste, align and spread, wires routed again, the minimap, and the right-click that offers them (Part 10d),
    /// all Unity-free.
    /// </summary>
    public class ArrangeTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static int Must(EditResult r)
        {
            Assert.IsTrue(r.Done, r.Refusal);
            return r.Id;
        }

        /// <summary>An input a into a NOT and an AND, the NOT also into the AND, the AND to y; the NOT's wire to the AND bent.</summary>
        private sealed class Bench
        {
            public Bench()
            {
                var editor = new BoardEditor(Project.New("bench"));
                A = Must(PartsCatalog.Place(editor, "bench", PartsCatalog.Find(editor.Project, "bench", "input"), new Placement(1, 0)));
                Not = Must(editor.Add("bench", new GatePart { Kind = EngineGate.Not, Inputs = 1 }, new Placement(4, -2)));
                And = Must(editor.Add("bench", new GatePart { Kind = EngineGate.And }, new Placement(7, 0)));
                Y = Must(editor.Add("bench", new PortPart { Name = "y", Direction = PortDirection.Output }, new Placement(10, 0)));
                ToAnd = Must(editor.Connect("bench", Endpoint.At(A, "out"), Endpoint.At(And, "a")));
                ToNot = Must(editor.Connect("bench", Endpoint.At(Not, "a"), Endpoint.OnWire(ToAnd), new Route { TapAt = 0.25 }));
                Bent = Must(editor.Connect("bench", Endpoint.At(Not, "y"), Endpoint.At(And, "b"), new Route { Bends = new[] { new BoardPoint(5.5, -2), new BoardPoint(5.5, -0.25) } }));
                Must(editor.Connect("bench", Endpoint.At(And, "y"), Endpoint.At(Y, "in")));
                Session = new WorkbenchSession(editor.Project, Ticking(0.01));
                Session.Finish();
                Editor = new CanvasEditor(Session);
            }

            public int A, Not, And, Y, ToAnd, ToNot, Bent;
            public WorkbenchSession Session;
            public CanvasEditor Editor;
            public Module Module => Session.Project.TopModule;

            public void Select(params int[] parts) => Editor.Selection.Box(parts, new int[0], extend: false);
        }

        // -----------------------------------------------------------------
        // Copy and paste
        // -----------------------------------------------------------------

        [Test]
        public void ACopy_TakesThePartsAndTheWiresBetweenThem_AndNotAWireToAPartLeftBehind()
        {
            var b = new Bench();
            Clip clip = Clipboard.Copy(b.Session.Project, b.Session.Module, new[] { b.Not, b.And });
            CollectionAssert.AreEquivalent(new[] { b.Not, b.And }, clip.Parts.Select(p => p.Part.Id));
            CollectionAssert.AreEquivalent(new[] { b.Bent }, clip.Wires.Select(w => w.Wire.Id),
                "the NOT to the AND; not a's wire, nor the tap on it, nor the AND's to y, each with an end left behind");

            Clip all = Clipboard.Copy(b.Session.Project, b.Session.Module, new[] { b.A, b.Not, b.And, b.Y });
            Assert.AreEqual(4, all.Wires.Count, "everything: the tap goes with the wire it taps");
            Assert.IsNull(Clipboard.Copy(b.Session.Project, b.Session.Module, new int[0]));
        }

        [Test]
        public void APaste_MakesThemAgain_ACellOn_WithNewIdsAndNames_TheirWiresAndBends_AsOneStep()
        {
            var b = new Bench();
            b.Select(b.A, b.Not, b.And);
            Clip clip = b.Editor.Copy();
            int before = b.Module.Parts.Count;

            EditResult pasted = b.Editor.Paste(clip);
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            Assert.AreEqual(before + 3, b.Module.Parts.Count);
            Assert.IsTrue(b.Session.Replaying, "new parts change what the circuit does: replayed");

            List<int> made = b.Editor.Selection.Parts.ToList();
            Assert.AreEqual(3, made.Count, "what the paste made is selected");
            CollectionAssert.IsEmpty(made.Intersect(new[] { b.A, b.Not, b.And }), "new ids");
            Assert.AreEqual(3, b.Editor.Selection.Wires.Count, "a's wire to the AND, the tap on it to the NOT, and the NOT's to the AND");

            Part newNot = b.Module.Parts.Single(p => made.Contains(p.Id) && p is GatePart g && g.Kind == EngineGate.Not);
            Assert.AreEqual(new Placement(5, -3), b.Module.Layout.PlacementOf(newNot.Id), "a cell right and a cell down");
            Assert.AreNotEqual(b.Module.FindPart(b.Not).Name, newNot.Name, "a name of its own");

            Wire bent = b.Module.Wires.Single(w => !w.From.IsTap && w.From.Pin.Part == newNot.Id && w.From.Pin.Pin == "y");
            CollectionAssert.AreEqual(new[] { new BoardPoint(6.5, -3), new BoardPoint(6.5, -1.25) }, b.Module.Layout.Routes[bent.Id].Bends, "its bends moved with it");
            Wire tap = b.Module.Wires.Single(w => !w.From.IsTap && w.From.Pin.Part == newNot.Id && w.From.Pin.Pin == "a");
            Assert.IsTrue(tap.To.IsTap && b.Editor.Selection.Wires.Contains(tap.To.TapOf), "the tap is on the pasted wire, not the old one");
            Assert.AreEqual(0.25, b.Module.Layout.Routes[tap.Id].TapAt, 1e-9, "at the same place along it");

            string newInput = b.Module.Parts.Single(p => made.Contains(p.Id) && p is PortPart).Name;
            Assert.IsTrue(b.Session.Project.Stimulus.Tracks.Any(t => t.Port == newInput), "the pasted input is given 0, as one placed is");
            Assert.IsEmpty(BoardRules.Check(b.Session.Project).Where(d => d.IsError));

            Assert.IsTrue(b.Editor.Paste(clip).Done);
            Assert.IsTrue(b.Module.Parts.Any(p => p is GatePart g && g.Kind == EngineGate.Not && b.Module.Layout.PlacementOf(p.Id).Equals(new Placement(6, -4))),
                "a second paste lands a step further on");

            Assert.IsTrue(b.Session.Undo());
            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual(before, b.Session.Project.TopModule.Parts.Count, "each paste one step");
        }

        [Test]
        public void ACut_TakesTheSelectionAway_AndAPasteBringsItBack()
        {
            var b = new Bench();
            b.Select(b.Not);
            Clip clip = b.Editor.Cut();
            Assert.IsNotNull(clip);
            Assert.IsNull(b.Module.FindPart(b.Not), "cut");
            Assert.IsTrue(b.Editor.Paste(clip).Done);
            Assert.AreEqual(1, b.Module.Parts.Count(p => p is GatePart g && g.Kind == EngineGate.Not), "and back, a cell on");

            b.Editor.Selection.Clear();
            Assert.IsNull(b.Editor.Copy());
            StringAssert.Contains("Select a part", b.Editor.Refusal);
            EditResult nothing = b.Editor.Paste(null);
            StringAssert.Contains("copy something first", nothing.Refusal);
        }

        // -----------------------------------------------------------------
        // Align and spread
        // -----------------------------------------------------------------

        private static PartOutline Outline(Bench b, int part) =>
            PartGeometry.Placed(b.Session.Project, b.Module.FindPart(part), b.Module.Layout.PlacementOf(part));

        [Test]
        public void Aligned_TheOutlinesShareAnEdgeOrAMiddle_OnTheGrid_AsOneStep_WithNothingReplayed()
        {
            var b = new Bench();
            b.Select(b.Not, b.And);
            Assert.IsTrue(b.Editor.Arrange(ArrangeBy.Top).Done);
            Assert.AreEqual(Outline(b, b.Not).Top, Outline(b, b.And).Top, 1e-9, "tops in line");
            Assert.IsFalse(b.Session.Replaying, "where parts stand changes nothing the circuit does");

            Assert.IsTrue(b.Editor.Arrange(ArrangeBy.CentresAcross).Done);
            double middle(PartOutline o) => (o.Left + o.Right) / 2;
            Assert.AreEqual(middle(Outline(b, b.Not)), middle(Outline(b, b.And)), 1e-9, "middles on one column");
            BoardPoint at = b.Module.Layout.PlacementOf(b.Not).Position;
            Assert.AreEqual(CanvasSnap.Snap(at), at, "on the quarter grid");

            Assert.IsTrue(b.Session.Undo());
            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual(new Placement(4, -2), b.Session.Project.TopModule.Layout.PlacementOf(b.Not), "one step each");

            b.Select(b.Not);
            StringAssert.Contains("two parts", b.Editor.Arrange(ArrangeBy.Left).Refusal);
        }

        [Test]
        public void Spread_LeavesEqualGaps_TheFirstAndTheLastWhereTheyAre()
        {
            var b = new Bench();
            Must(b.Session.Move(new Dictionary<int, Placement> { [b.Not] = new Placement(3, 3) }));
            b.Select(b.A, b.Not, b.Y);
            StringAssert.Contains("three parts", new Func<string>(() => { b.Select(b.A, b.Not); return b.Editor.Arrange(ArrangeBy.SpreadAcross).Refusal; })());

            b.Select(b.A, b.Not, b.Y);
            Assert.IsTrue(b.Editor.Arrange(ArrangeBy.SpreadAcross).Done);
            double m(int part) => (Outline(b, part).Left + Outline(b, part).Right) / 2;
            Assert.AreEqual(m(b.Not) - m(b.A), m(b.Y) - m(b.Not), 0.25, "equal gaps, to the quarter grid");
            Assert.AreEqual(new Placement(1, 0), b.Module.Layout.PlacementOf(b.A), "the first where it was");
            Assert.AreEqual(new Placement(10, 0), b.Module.Layout.PlacementOf(b.Y), "the last too");
        }

        [Test]
        public void AWireRoutedAgain_ForgetsItsBends_AndATapKeepsItsPlace()
        {
            var b = new Bench();
            b.Editor.Selection.Box(new int[0], new[] { b.Bent, b.ToNot }, extend: false);
            Assert.IsTrue(b.Editor.AutoRoute().Done);
            Assert.AreEqual(0, b.Module.Layout.Routes[b.Bent].Bends.Count, "the automatic route");
            Assert.AreEqual(0.25, b.Module.Layout.Routes[b.ToNot].TapAt, 1e-9, "the tap where it was along a's wire");
            Assert.IsFalse(b.Session.Replaying);
            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual(2, b.Session.Project.TopModule.Layout.Routes[b.Bent].Bends.Count, "one step");

            b.Editor.Selection.Clear();
            StringAssert.Contains("Select a wire", b.Editor.AutoRoute().Refusal);
        }

        // -----------------------------------------------------------------
        // The right-click, and the minimap
        // -----------------------------------------------------------------

        [Test]
        public void ARightClick_OpensTheMenu_OnWhatItIsOn_SelectingItWhenItWasNotSelected()
        {
            var pointer = new CanvasPointer();
            var commands = new List<CanvasCommand>();
            pointer.Press(new CanvasPress { Button = 1, Target = new CanvasTarget(CanvasTargetKind.Part, part: 3), Board = new BoardPoint(4, 0) }, commands);
            Assert.AreEqual("Menu part 3", string.Join(", ", commands));

            var b = new Bench();
            b.Select(b.Not, b.And);
            b.Editor.Do(new CanvasCommand(CanvasGesture.Menu, target: new CanvasTarget(CanvasTargetKind.Pin, part: b.And, pin: "y")));
            CollectionAssert.AreEquivalent(new[] { b.Not, b.And }, b.Editor.Selection.Parts, "on the selection: kept, for the menu to act on");
            b.Editor.Do(new CanvasCommand(CanvasGesture.Menu, target: new CanvasTarget(CanvasTargetKind.Part, part: b.Y)));
            CollectionAssert.AreEqual(new[] { b.Y }, b.Editor.Selection.Parts, "on something else: it, alone");
            b.Editor.Do(new CanvasCommand(CanvasGesture.Menu, target: CanvasTarget.Nothing));
            CollectionAssert.AreEqual(new[] { b.Y }, b.Editor.Selection.Parts, "on nothing: as it was");
        }

        [Test]
        public void TheMinimap_IsShownOnlyWithSomeOfTheDrawingOutOfTheWindow_ShowsTheWindowOnIt_AndAClickOnItCentresTheView()
        {
            var view = new SchematicView(new Vec2(800f, 400f), new Vec2(0f, 0f), new Vec2(1600f, 800f));
            view.Fit();
            Assert.IsNull(Minimap.Of(view), "at the fit, the whole drawing is in the window: no map");
            view.ZoomAbout(new Vec2(400f, 200f), -1);
            Assert.IsFalse(view.IsFitted, "sanity: zoomed out past the fit");
            Assert.IsNull(Minimap.Of(view), "the drawing still whole in the window, smaller: no map either");
            view.Fit();

            view.ZoomAbout(new Vec2(400f, 200f), 4);
            Minimap map = Minimap.Of(view);
            Assert.IsNotNull(map);
            Assert.LessOrEqual(map.Size.x, Minimap.MaxWidth + 0.01f);
            Assert.LessOrEqual(map.Size.y, Minimap.MaxHeight + 0.01f);
            Assert.Less(map.Window.width, map.Size.x, "zoomed in, the window is a part of the drawing");
            Assert.Greater(map.Window.width, 0f);

            Assert.IsTrue(map.CentreOn(view, new Vec2(map.Size.x * 0.25f, map.Size.y * 0.5f)));
            Vec2 middle = (view.Window * 0.5f - view.Offset) / view.Zoom;
            Assert.AreEqual(view.Drawing.x * 0.25f, middle.x, 1f, "the window's middle on the drawing where the map was clicked");
            Assert.AreEqual(view.Drawing.y * 0.5f, middle.y, 1f);
        }
    }
}
