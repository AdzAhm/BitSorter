using BitSorter.Board;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// PARTS in an exercise (Part 15d): a register placed starts where the document says, an exercise's start or x; only
    /// what it stocks is offered, no port, and each counted part says how many are left.
    /// </summary>
    public class ExercisePartsTests
    {
        [Test]
        public void AnExercise_OffersWhatItStocks_NoPort_AndHowManyAreLeft()
        {
            Exercise exercise = BitSorter.Board.Tests.ExerciseFiles.Load("half-adder");
            var editor = new BoardEditor(exercise.ToProject(new System.Collections.Generic.List<string>()));
            string top = editor.Project.Top;
            var offered = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(PartsCatalog.For(editor.Project, top), e => e.Key));
            CollectionAssert.Contains(offered, "xor");
            CollectionAssert.Contains(offered, "and");
            CollectionAssert.DoesNotContain(offered, "or", "not stocked");
            CollectionAssert.DoesNotContain(offered, "register", "no register stocked");
            CollectionAssert.DoesNotContain(offered, "input", "its ports are its test's");

            CatalogEntry xor = PartsCatalog.Find(editor.Project, top, "xor");
            Assert.AreEqual(1, PartsCatalog.Left(editor.Project, xor));
            Assert.AreEqual("XOR 1", PartsCatalog.Caption(editor.Project, xor));
            Assert.IsTrue(PartsCatalog.Place(editor, top, xor, new Placement(8, 0)).Done);
            Assert.AreEqual("XOR 0", PartsCatalog.Caption(editor.Project, xor), "none left once it is placed");

            Project own = Project.New("mine");
            CatalogEntry free = PartsCatalog.Find(own, "mine", "xor");
            Assert.IsNull(PartsCatalog.Left(own, free), "no library, no count");
            Assert.AreEqual("XOR", PartsCatalog.Caption(own, free));
            Assert.IsNotNull(PartsCatalog.Find(own, "mine", "input"), "a circuit of the student's own has its ports to place");
        }

        /// <summary>
        /// In an exercise, a cut of everything takes all but its ports, which are its test's (the #88 review), and pastes back
        /// onto them: the clip's ports are the exercise's own, not more of them (the 16d sweep: the paste was refused whole, so
        /// what was cut could come back only by Ctrl+Z).
        /// </summary>
        [Test]
        public void InAnExercise_ACutOfEverything_PastesBackOntoItsOwnPorts()
        {
            Exercise exercise = BitSorter.Board.Tests.ExerciseFiles.Load("half-adder");
            var session = new WorkbenchSession(exercise.ToProject(new System.Collections.Generic.List<string>()), () => 0.0);
            session.Finish();
            Module top = session.Project.TopModule;
            int Port(string name) => System.Linq.Enumerable.Single(session.Project.TopModule.Ports, p => p.Name == name).Id;
            EditResult xor = session.Place(PartsCatalog.Find(session.Project, session.Module, "xor"), new Placement(8, -2));
            Assert.IsTrue(xor.Done, xor.Refusal);
            Assert.IsTrue(session.Connect(Endpoint.At(Port("a"), "out"), Endpoint.At(xor.Id, "a")).Done);
            Assert.IsTrue(session.Connect(Endpoint.At(Port("b"), "out"), Endpoint.At(xor.Id, "b")).Done);
            Assert.IsTrue(session.Connect(Endpoint.At(xor.Id, "y"), Endpoint.At(Port("sum"), "in")).Done);
            int ports = top.Ports.Count;

            var editor = new CanvasEditor(session);
            editor.SelectAll();
            Clip clip = editor.Cut();
            Assert.IsNotNull(clip, editor.Refusal);
            Assert.AreEqual(ports, session.Project.TopModule.Parts.Count, "the cut took all but the ports");

            EditResult pasted = editor.Paste(clip);
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            Module after = session.Project.TopModule;
            Assert.AreEqual(ports, after.Ports.Count, "no port more");
            GatePart gate = (GatePart)System.Linq.Enumerable.Single(after.Parts, p => p is GatePart);
            Netlist nets = Netlist.Of(session.Project, after);
            Assert.AreSame(nets.NetOf(new PinRef(Port("a"), "out")), nets.NetOf(new PinRef(gate.Id, "a")), "wired back to the exercise's own a");
            Assert.AreSame(nets.NetOf(new PinRef(Port("sum"), "in")), nets.NetOf(new PinRef(gate.Id, "y")), "and its own sum");
        }

        [Test]
        public void ARegisterPlaced_StartsWhereTheDocumentSays()
        {
            Project project = Project.New("late");
            CatalogEntry register = PartsCatalog.Find(project, "late", "register");
            Assert.IsNotNull(register, "sanity: PARTS has a register");

            Assert.IsNull(((RegisterPart)PartsCatalog.Make(project, "late", register)).Initial, "x, in a circuit of the student's own");

            Project exercise = project with { Settings = project.Settings with { RegisterStart = LogicVec.FromBinary("0") } };
            Assert.AreEqual("0", ((RegisterPart)PartsCatalog.Make(exercise, "late", register)).Initial?.Format(Radix.Binary),
                "an exercise's start, as every 4.x register started at 0");
        }
    }
}
