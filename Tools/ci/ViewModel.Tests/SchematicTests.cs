using System.Collections.Generic;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The schematic's geometry: routes, pan and zoom, pointing at a wire, and how a layer is cut into
    /// chunks. Ran in Edit Mode until Part 9 moved the view-model out of Unity, but for the last two
    /// groups, which are new with it.
    /// </summary>
    public class SchematicTests
    {
        // -----------------------------------------------------------------
        // Routes
        // -----------------------------------------------------------------

        [Test]
        public void AStraightRoute_IsAsLongAsTheLineBetweenItsEnds()
        {
            var route = new StageRoute(new Vec2(0, 0), new Vec2(100, 0));

            Assert.AreEqual(100f, route.Length, 1e-3f);
            Assert.AreEqual(new Vec2(50, 0), route.PointAt(50));
        }

        [Test]
        public void ARunStraightThroughItsPinsAndStubs_IsOneStraightPiece()
        {
            // Body, pin, stub, stub, pin, body: what a wire between two gates on one row is made of.
            var route = new StageRoute(new Vec2(0, 0), new Vec2(6, 0), new Vec2(20, 0), new Vec2(80, 0),
                new Vec2(94, 0), new Vec2(100, 0));

            Assert.AreEqual(2, route.PointCount, "a corner it runs straight through is no corner, and is not bent");
            Assert.AreEqual(100f, route.Length, 1e-3f);
            Assert.AreEqual(new Vec2(50, 0), route.PointAt(50));
        }

        [Test]
        public void ARouteThatDoublesBack_StillBendsWhereItTurns()
        {
            var route = new StageRoute(new Vec2(0, 0), new Vec2(100, 0), new Vec2(40, 0));

            Assert.Greater(route.PointCount, 2, "turning back is a corner, however straight the line");
        }

        [Test]
        public void ARoundedBend_IsShorterThanTheCorner_ButNotByMuch()
        {
            var route = new StageRoute(new Vec2(0, 0), new Vec2(100, 0), new Vec2(100, 100));

            Assert.Less(route.Length, 200f, "the bend cuts the corner");
            Assert.Greater(route.Length, 195f, "by a few units, not a diagonal");
            Assert.AreEqual(new Vec2(0, 0), route.PointAt(0));
            Assert.AreEqual(new Vec2(100, 100), route.PointAt(route.Length));
            Assert.AreEqual(new Vec2(100, 100), route.PointAt(route.Length + 50f), "clamped past the end");
        }

        [Test]
        public void APointAlongARoute_MovesForwardAsTheDistanceGrows()
        {
            StageRoute route = WorkbenchModelTests.Shared.Layout.Nets[WorkbenchModelTests.NotA].Branches[0];
            float previous = -1f;

            for (float d = 0f; d <= route.Length; d += route.Length / 40f)
            {
                Vec2 p = route.PointAt(d);
                Assert.GreaterOrEqual(p.x, previous - 1e-3f, "the NOT's output runs left to right throughout");
                previous = p.x;
            }
        }

        // -----------------------------------------------------------------
        // Zooming and moving the circuit
        // -----------------------------------------------------------------

        /// <summary>A window 800 by 400, the drawing 760 by 360 sitting 20 in from its corner.</summary>
        private static SchematicView View() => new SchematicView(new Vec2(800, 400), new Vec2(20, 20), new Vec2(760, 360));

        /// <summary>The point of the drawing under a point of the window.</summary>
        private static Vec2 Under(SchematicView view, Vec2 at) => (at - view.Offset) / view.Zoom;

        [Test]
        public void TheCircuit_StartsWhole_WhereItWasBuilt()
        {
            SchematicView view = View();

            Assert.IsTrue(view.IsFitted);
            Assert.AreEqual(1f, view.Zoom);
            Assert.AreEqual(view.Home, view.Offset);
        }

        [Test]
        public void TheWheel_ZoomsIn_KeepingThePointUnderTheCursorWhereItIs()
        {
            SchematicView view = View();
            var cursor = new Vec2(300, 150);
            Vec2 under = Under(view, cursor);

            Assert.IsTrue(view.ZoomAbout(cursor, 2));
            Assert.AreEqual(SchematicView.NotchFactor * SchematicView.NotchFactor, view.Zoom, 1e-5f);
            Assert.AreEqual(under.x, Under(view, cursor).x, 1e-3f, "the point under the cursor moved across");
            Assert.AreEqual(under.y, Under(view, cursor).y, 1e-3f, "the point under the cursor moved up or down");
            Assert.IsFalse(view.IsFitted);
        }

        [Test]
        public void ZoomingIn_StopsAtItsLimit()
        {
            SchematicView view = View();

            view.ZoomAbout(new Vec2(400, 200), 50);
            Assert.AreEqual(SchematicView.MaxZoom, view.Zoom, 1e-5f);
            Assert.IsFalse(view.ZoomAbout(new Vec2(400, 200), 1), "a notch past the limit changes nothing");
        }

        [Test]
        public void ZoomingOut_ComesBackToTheWholeCircuit_AndGoesOnOutForRoomToPlaceThings()
        {
            SchematicView view = View();
            view.ZoomAbout(new Vec2(700, 50), 3);
            view.Pan(new Vec2(-120, 40));

            view.ZoomAbout(new Vec2(100, 300), -3);
            Assert.IsTrue(view.IsFitted, "as many notches out as in should be the whole circuit, exactly, where it was built");

            // Part 10: on out, past the fit, for room beside what is there -- as far as the floor and no further.
            Assert.IsTrue(view.ZoomAbout(new Vec2(100, 300), -1));
            Assert.Less(view.Zoom, 1f);
            Assert.IsFalse(view.IsFitted);
            view.ZoomAbout(new Vec2(100, 300), -50);
            Assert.AreEqual(SchematicView.MinZoom, view.Zoom, 1e-5f);
            Assert.IsFalse(view.ZoomAbout(new Vec2(100, 300), -1), "a notch past the floor changes nothing");
        }

        [Test]
        public void ADrag_MovesTheCircuit_IntoTheRoomRoundIt_ButNeverOutOfTheWindow()
        {
            SchematicView view = View();
            view.ZoomAbout(new Vec2(400, 200), 2);
            Vec2 before = view.Offset;

            Assert.IsTrue(view.Pan(new Vec2(30, -10)));
            Assert.AreEqual(before + new Vec2(30, -10), view.Offset);

            float room = SchematicView.Room;
            foreach (Vec2 far in new[] { new Vec2(5000, 0), new Vec2(-5000, 0), new Vec2(0, 5000), new Vec2(0, -5000) })
            {
                view.Pan(far);
                Vec2 middle = Under(view, view.Window * 0.5f);
                Assert.IsTrue(middle.x >= -room - 1e-3f && middle.x <= view.Drawing.x + room + 1e-3f && middle.y >= -room - 1e-3f && middle.y <= view.Drawing.y + room + 1e-3f,
                    $"dragged by {far}, the window's middle is off the circuit and the room round it: {middle}");
            }

            view.Pan(new Vec2(5000, 0));
            Assert.Less(Under(view, view.Window * 0.5f).x, 0f, "Part 10: a drag reaches past the circuit's edge, where a part can be placed");
        }

        [Test]
        public void ADrawingThatOutgrowsItsWindow_FitsSmaller_AndAnEditMovesNothingOnScreen()
        {
            SchematicView view = View();
            Vec2 screen = view.Offset + new Vec2(100, 50) * view.Zoom;   // a point of the drawing, on screen

            // An edit grew the drawing 200 to the left and 100 up: the same point is 200 and 100 further in.
            view.Relayout(new Vec2(960, 460), new Vec2(200, 100));
            Assert.AreEqual(screen.x, view.Offset.x + 300 * view.Zoom, 1e-3f, "what was on screen is where it was");
            Assert.AreEqual(screen.y, view.Offset.y + 150 * view.Zoom, 1e-3f);

            view.Fit();
            Assert.Less(view.Zoom, 1f, "the whole drawing no longer fits at its own size, so the fit is smaller");
            Assert.LessOrEqual(view.Drawing.x * view.Zoom, view.Window.x - 2 * view.Home.x + 1e-3f);
            Assert.LessOrEqual(view.Drawing.y * view.Zoom, view.Window.y - 2 * view.Home.y + 1e-3f);
            Assert.IsTrue(view.IsFitted);
        }

        [Test]
        public void Fit_PutsBackTheWholeCircuit()
        {
            SchematicView view = View();
            view.ZoomAbout(new Vec2(10, 10), 4);
            view.Pan(new Vec2(50, 50));

            view.Fit();

            Assert.IsTrue(view.IsFitted);
        }

        // -----------------------------------------------------------------
        // Pointing at a wire
        // -----------------------------------------------------------------

        [Test]
        public void PointingAtAWire_FindsItsNet_AndNothingAwayFromEveryWire()
        {
            SchematicLayout layout = WorkbenchModelTests.Shared.Layout;
            int notA = WorkbenchModelTests.NotA;
            StageRoute wire = layout.Nets[notA].Branches[0];

            Assert.AreEqual(notA, layout.NetAt(wire.PointAt(wire.Length * 0.5f) + new Vec2(0f, 3f), 8f), "beside NOT A's wire");
            Assert.AreEqual(-1, layout.NetAt(new Vec2(-500f, -500f), 8f), "far from every wire");
        }

        [Test]
        public void ANetPointedAt_SaysWhatDrivesIt_AndWhatReadsIt()
        {
            SchematicLayout layout = WorkbenchModelTests.Shared.Layout;
            string notA = layout.Nets[WorkbenchModelTests.NotA].Says;
            StringAssert.StartsWith("na ", notA);
            StringAssert.Contains(".y (NOT)", notA, "driven by the NOT's output");
            StringAssert.Contains(".b (AND)", notA, "read by the AND's second input");

            StringAssert.Contains("from input a to ", layout.Nets[WorkbenchModelTests.A].Says);
            StringAssert.StartsWith("s[3:0] ", layout.Nets[layout.NetNamed("s")].Says, "a bus is named with its width");
        }

        // -----------------------------------------------------------------
        // Cutting a layer into chunks
        // -----------------------------------------------------------------

        [Test]
        public void ALayer_IsCutIntoWholeItems_EachRunUnderItsBudget()
        {
            int[] cost = { 40, 30, 50, 20, 70 };

            List<(int Start, int Count)> runs = Chunks.Cut(cost.Length, i => cost[i], budget: 100);

            CollectionAssert.AreEqual(new[] { (0, 2), (2, 2), (4, 1) }, runs, "40+30, then 50+20, then 70");
        }

        [Test]
        public void AnItemOverTheBudget_IsARunOfItsOwn_AndTheFirstRunCarriesItsExtra()
        {
            int[] cost = { 10, 500, 10 };

            CollectionAssert.AreEqual(new[] { (0, 1), (1, 1), (2, 1) }, Chunks.Cut(cost.Length, i => cost[i], budget: 100),
                "an item too big for any chunk still gets one, alone");
            CollectionAssert.AreEqual(new[] { (0, 2) }, Chunks.Cut(2, i => 30, budget: 100), "two items of 30 share a chunk");
            CollectionAssert.AreEqual(new[] { (0, 1), (1, 1) }, Chunks.Cut(2, i => 30, budget: 100, first: 60),
                "but not after the ports drawn with the first parts chunk: they count against it");
        }

        [Test]
        public void NothingToCut_IsOneEmptyRun()
        {
            CollectionAssert.AreEqual(new[] { (0, 0) }, Chunks.Cut(0, i => 1, budget: 100),
                "a layer with nothing in it still has its chunk, to draw nothing into");
        }

        // -----------------------------------------------------------------
        // Unity's arithmetic, without Unity
        // -----------------------------------------------------------------

        [Test]
        public void TwoPointsWithinATinyDistance_AreEqual_AsUnitysAre_ButNotExactlySo()
        {
            var a = new Vec2(10f, 20f);
            var near = new Vec2(10f + 5e-6f, 20f);

            Assert.IsTrue(a == near, "Unity's == forgives less than 1e-5, which SchematicView's 'did it move' answers rely on");
            Assert.IsFalse(a.Equals(near), "Equals is exact, as Unity's is");
            Assert.IsFalse(a == new Vec2(10f + 2e-5f, 20f));
        }

        [Test]
        public void AVectorWithNoLength_NormalizesToZero_NotToNaN()
        {
            Assert.AreEqual(Vec2.zero, new Vec2(0f, 0f).normalized);
            Assert.AreEqual(Vec2.zero, new Vec2(1e-6f, 0f).normalized, "shorter than Unity's epsilon is no length");
            Assert.AreEqual(1f, new Vec2(3f, 4f).normalized.magnitude, 1e-6f);
        }
    }
}
