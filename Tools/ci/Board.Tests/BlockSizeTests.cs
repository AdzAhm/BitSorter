using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A block resized by dragging its selection's handles (Part 16, asked 2026-11-02): the size kept with its placement, its
    /// pins on its sides and spread down them on the quarter grid, never smaller than natural nor larger than the cap, turned
    /// as it is placed, saved and read back, kept by every edit that moves a part -- and a natural block exactly as it was.
    /// </summary>
    public class BlockSizeTests
    {
        /// <summary>A project whose top, m, holds one block of inner -- three inputs, one output -- at the origin, placed as <paramref name="at"/>.</summary>
        private static (Project Project, BlockPart Block) Blocked(Placement at)
        {
            var editor = new BoardEditor(Project.New("m"));
            SampleBoards.Must(editor.AddModule("inner"));
            var inner = new SampleBoards.Builder(editor, "inner");
            int a = inner.In("a"), b = inner.In("b"), c = inner.In("c"), y = inner.Out("y");
            int g = inner.Gate(GateKind.And, inputs: 3);
            inner.Wire(a, "out", g, "a");
            inner.Wire(b, "out", g, "b");
            inner.Wire(c, "out", g, "c");
            inner.Wire(g, "y", y, "in");
            int id = SampleBoards.Must(editor.Add("m", new BlockPart { Module = "inner" }, at));
            return (editor.Project, (BlockPart)editor.Project.Find("m").Parts.Single(p => p.Id == id));
        }

        private static bool OnTheQuarterGrid(double v) => System.Math.Abs(v * 4 - System.Math.Round(v * 4)) < 1e-9;

        [Test]
        public void ANaturalBlock_IsDrawnAsItAlwaysWas()
        {
            (Project project, BlockPart block) = Blocked(new Placement(0, 0));
            PartOutline natural = PartGeometry.Local(project, block);
            Assert.AreEqual((-1.5, -0.75, 1.5, 0.75), (natural.Left, natural.Bottom, natural.Right, natural.Top), "three cells wide, three pins tall");
            CollectionAssert.AreEqual(new[] { 0.5, 0.0, -0.5 }, natural.Pins.Where(p => p.Facing == Facing.Left).Select(p => p.At.Y));

            (double width, double height) = PartGeometry.NaturalSize(project, block);
            PartOutline sizedNatural = PartGeometry.Local(project, block, new Placement(new Point(0, 0), width: width, height: height));
            CollectionAssert.AreEqual(natural.Pins.Select(p => (p.Pin, p.At.X, p.At.Y)), sizedNatural.Pins.Select(p => (p.Pin, p.At.X, p.At.Y)),
                "a size the block has naturally draws it as it always was");
        }

        [Test]
        public void AResizedBlock_HasItsPinsOnItsSides_SpreadDownThemOnTheQuarterGrid()
        {
            (Project project, BlockPart block) = Blocked(new Placement(0, 0));
            PartOutline sized = PartGeometry.Local(project, block, new Placement(new Point(0, 0), width: 6, height: 4));
            Assert.AreEqual((-3.0, -2.0, 3.0, 2.0), (sized.Left, sized.Bottom, sized.Right, sized.Top));
            foreach (PinSpot pin in sized.Pins)
            {
                Assert.AreEqual(pin.Facing == Facing.Left ? -3.0 : 3.0, pin.At.X, $"{pin.Pin} on its side");
                Assert.IsTrue(OnTheQuarterGrid(pin.At.Y), $"{pin.Pin} on the quarter grid, at {pin.At.Y}");
                Assert.Less(System.Math.Abs(pin.At.Y), 2.0, $"{pin.Pin} inside the box");
            }

            // Three pins in four cells: the largest half-cell step that fits 3.5 cells is 1.5.
            CollectionAssert.AreEqual(new[] { 1.5, 0.0, -1.5 }, sized.Pins.Where(p => p.Facing == Facing.Left).Select(p => p.At.Y));
            Assert.AreEqual(0.0, sized.Pins.Single(p => p.Pin == "y").At.Y, "the lone output stays in the middle");
        }

        [Test]
        public void ASize_NeverGoesBelowNatural_NorAboveTheCap()
        {
            (Project project, BlockPart block) = Blocked(new Placement(0, 0));
            PartOutline small = PartGeometry.Local(project, block, new Placement(new Point(0, 0), width: 1, height: 0.5));
            Assert.AreEqual((-1.5, -0.75, 1.5, 0.75), (small.Left, small.Bottom, small.Right, small.Top), "natural, at the least");
            PartOutline huge = PartGeometry.Local(project, block, new Placement(new Point(0, 0), width: 500, height: 500));
            Assert.AreEqual(PartGeometry.MaxSize, huge.Right - huge.Left);
            Assert.AreEqual(PartGeometry.MaxSize, huge.Top - huge.Bottom);
        }

        [Test]
        public void OnlyABlockOrABox_TakesASize()
        {
            Assert.IsTrue(PartGeometry.Resizable(new BlockPart { Module = "inner" }));
            Assert.IsTrue(PartGeometry.Resizable(new BoxPart()));
            Assert.IsFalse(PartGeometry.Resizable(new GatePart { Kind = GateKind.And, Inputs = 2 }));
            PartOutline gate = PartGeometry.Local(null, new GatePart { Kind = GateKind.And, Inputs = 2 }, new Placement(new Point(0, 0), width: 9, height: 9));
            Assert.AreEqual((-1.0, 1.0), (gate.Left, gate.Right), "a gate keeps its own size");
        }

        [Test]
        public void AResizedBlock_Turned_IsPlacedAlongTheOtherAxis()
        {
            (Project project, BlockPart block) = Blocked(new Placement(0, 0));
            PartOutline placed = PartGeometry.Placed(project, block, new Placement(new Point(2, 1), 1, false, 6, 4));
            Assert.AreEqual((0.0, -2.0, 4.0, 4.0), (placed.Left, placed.Bottom, placed.Right, placed.Top), "turned a quarter: four wide, six tall");
            foreach (PinSpot pin in placed.Pins)
                Assert.IsTrue(pin.Facing == Facing.Down ? pin.At.Y == -2.0 : pin.At.Y == 4.0, $"{pin.Pin} on its edge, facing {pin.Facing}");
        }

        [Test]
        public void EveryHelper_KeepsTheSize_AndEqualityCountsIt()
        {
            var sized = new Placement(new Point(1, 2), 1, true, 6, 4);
            Assert.IsTrue(sized.HasSize);
            Assert.IsFalse(new Placement(1, 2).HasSize);
            Assert.AreEqual((6.0, 4.0), (sized.Moved(1, 1).Width, sized.Moved(1, 1).Height));
            Assert.AreEqual(new Placement(new Point(5, 5), 1, true, 6, 4), sized.WithPosition(new Point(5, 5)));
            Assert.AreEqual(new Placement(new Point(0, 0), 2, false, 6, 4), sized.Turned(new Point(0, 0), 2, false));
            Assert.AreEqual(new Placement(new Point(1, 2), 1, true), sized.WithSize(0, 0));
            Assert.AreNotEqual(sized, sized.WithSize(6, 4.5), "a size is part of where a part stands");
            Assert.AreNotEqual(sized.GetHashCode(), sized.WithSize(8, 4).GetHashCode());
        }

        [Test]
        public void ASize_IsSavedAndReadBack_AndAFileFromBeforeReadsNatural()
        {
            (Project project, BlockPart block) = Blocked(new Placement(new Point(1, 2), 0, false, 6, 4));
            var problems = new List<string>();
            ProjectRecord record = ProjectRecord.From(project);
            Project read = record.ToProject(problems);
            Assert.IsEmpty(problems);
            Assert.AreEqual(new Placement(new Point(1, 2), 0, false, 6, 4), read.Find("m").Layout.PlacementOf(block.Id));

            foreach (ModuleRecord module in record.modules)
                foreach (PlacementRecord p in module.placements)
                    p.width = p.height = 0;   // as a file from before has neither field
            Assert.IsFalse(record.ToProject(problems).Find("m").Layout.PlacementOf(block.Id).HasSize);
        }

        [Test]
        public void MovingTheBlock_KeepsItsSize()
        {
            (Project project, BlockPart block) = Blocked(new Placement(new Point(0, 0), 0, false, 6, 4));
            var editor = new BoardEditor(project);
            Placement was = project.Find("m").Layout.PlacementOf(block.Id);
            Assert.IsTrue(editor.MoveParts("m", new Dictionary<int, Placement> { [block.Id] = was.Moved(3, -1) }).Done);
            Assert.AreEqual(new Placement(new Point(3, -1), 0, false, 6, 4), editor.Project.Find("m").Layout.PlacementOf(block.Id));
        }

        // -----------------------------------------------------------------
        // On the drawing of code
        // -----------------------------------------------------------------

        private const string Inner = "module inner (input logic a, b, output logic y);\n    assign y = a & b;\nendmodule\n";

        private const string Outer = "module m (input logic a, b, c, output logic y, z);\n    inner u (.a(a), .b(b), .y(y));\n    assign z = a | c;\nendmodule\n";

        private static RtlDrawing Draw(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(e.HasErrors);
            return RtlDrawing.Of(project, RtlExtractor.Of(e.Design, "m"), "m", arranged: project.Find("m"));
        }

        /// <summary>
        /// On the drawing of code, an instance's block resized is kept with the module by its key, as a part put by hand is, and
        /// the drawing lays everything else out clear of it.
        /// </summary>
        [Test]
        public void OnTheDrawingOfCode_AResizedBlock_IsKept_AndNothingStandsOnIt()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("inner", Inner).Done);
            Assert.IsTrue(editor.AddWrittenModule("m", Outer).Done);
            editor = new BoardEditor(editor.Project with { Top = "m" });
            RtlDrawing drawing = Draw(editor.Project);
            BlockPart u = drawing.Module.Parts.OfType<BlockPart>().Single();
            Assert.AreEqual("inst:u", drawing.KeyOf(u.Id));

            Placement grown = drawing.Module.Layout.PlacementOf(u.Id).WithSize(12, 10);
            Assert.IsTrue(editor.Arrange("m", new Dictionary<string, Placement> { ["inst:u"] = grown }).Done);
            RtlDrawing again = Draw(editor.Project);
            Part block = again.Module.Parts.OfType<BlockPart>().Single();
            Assert.AreEqual(grown, again.Module.Layout.PlacementOf(block.Id), "the size kept by its key");

            List<PartOutline> outlines = again.Module.Parts.Select(p => PartGeometry.Placed(again.Project, p, again.Module.Layout.PlacementOf(p.Id))).ToList();
            for (int i = 0; i < outlines.Count; i++)
                for (int j = i + 1; j < outlines.Count; j++)
                    Assert.IsFalse(outlines[i].Left < outlines[j].Right && outlines[j].Left < outlines[i].Right
                        && outlines[i].Bottom < outlines[j].Top && outlines[j].Bottom < outlines[i].Top,
                        $"{again.Module.Parts[i].Name} and {again.Module.Parts[j].Name} stand apart");
        }
    }
}
