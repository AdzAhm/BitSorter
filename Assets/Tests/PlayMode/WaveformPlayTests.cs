using System.Collections;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The timing diagram's strip along the bottom: how it opens, what it moves out of its way, and
    /// what it records.
    /// </summary>
    [TestFixture]
    public class WaveformPlayTests
    {
        private const string Level = "half-adder";

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        private static IEnumerator OnTheBoard(string level = Level)
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel(level), "the level did not load");
            yield return null;
            yield return null;
        }

        /// <summary>Clicks the badge and gives everything a frame to follow, and one for the camera.</summary>
        private static IEnumerator ClickTheBadge()
        {
            Button badge = Find<WaveformPanel>().Badge;
            Assert.IsNotNull(badge, "the timing diagram has no badge");

            badge.onClick.Invoke();
            yield return null;
            yield return null;
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>The badge opens the strip as F8 does, and nothing else, and closes it again.</summary>
        /// <remarks>
        /// Diagnostics came up with the strip for the one day the strip was on F2. They are F2's
        /// again, with the clock diagram, and the badge is the strip's alone.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheBadge_OpensTheDiagram_AndNothingElse()
        {
            yield return OnTheBoard();

            WaveformPanel diagram = Find<WaveformPanel>();
            DiagnosticsPanel diagnostics = Find<DiagnosticsPanel>();
            Assert.IsFalse(diagram.IsOpen, "sanity: the diagram starts closed");

            yield return ClickTheBadge();

            Assert.IsTrue(diagram.IsOpen, "the badge did not open the timing diagram");
            Assert.IsTrue(diagram.IsShowing, "the timing diagram is open and not drawn");
            Assert.IsFalse(diagnostics.IsShowing, "diagnostics came up with the timing diagram; they are F2's");
            Assert.IsFalse(Find<ClockDiagram>().IsOpen, "the badge switched F2 on");

            yield return ClickTheBadge();

            Assert.IsFalse(diagram.IsShowing, "the badge did not close the timing diagram");
        }

        /// <summary>
        /// Opening the strip lifts the board clear of it -- the bottom row's names stay above the
        /// strip -- and closing it puts the camera back exactly where it was.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBoard_IsLiftedClearOfTheStrip_AndPutBackWhenItCloses()
        {
            yield return OnTheBoard();

            Camera camera = Camera.main;
            PlacementGrid grid = Find<PlacementGrid>();
            WaveformPanel diagram = Find<WaveformPanel>();

            float size = camera.orthographicSize;
            Vector3 position = camera.transform.position;
            Assert.AreEqual(0f, position.y, "sanity: the camera starts level with the board");

            yield return ClickTheBadge();

            Assert.Less(camera.transform.position.y, 0f, "the board was not lifted");
            Assert.GreaterOrEqual(camera.orthographicSize, size, "the board grew when the strip opened");

            float bottomNames = -grid.HalfExtents.y * grid.CellSize - NodeRenderer.LabelReach;
            float onScreen = camera.WorldToScreenPoint(new Vector3(0f, bottomNames, 0f)).y;
            Assert.GreaterOrEqual(onScreen, diagram.ScreenTopEdge - 1f,
                "the names under the board's bottom row are under the strip");

            Rect strip = ScreenRect(diagram.Root);
            Assert.AreEqual(diagram.ScreenTopEdge, strip.yMax, 1f, "the strip is not where the board was framed above");

            yield return ClickTheBadge();

            Assert.AreEqual(size, camera.orthographicSize, "closing the strip left the board a different size");
            Assert.AreEqual(position, camera.transform.position, "closing the strip left the camera elsewhere");
        }

        /// <summary>The strip steps aside for a full-screen panel and comes back as it was.</summary>
        [UnityTest]
        public IEnumerator TheStrip_HidesWithTheHud()
        {
            yield return OnTheBoard();

            WaveformPanel diagram = Find<WaveformPanel>();
            yield return ClickTheBadge();
            Assert.IsTrue(diagram.IsShowing, "sanity: the strip should be up");

            MainMenu menu = Find<MainMenu>();
            menu.Show(true);

            for (int frame = 0; frame < 240 && UiFade.AnyMoving; frame++)
                yield return null;

            yield return null;

            Assert.IsTrue(diagram.IsOpen, "the menu switched the diagram off rather than covering it");
            Assert.IsFalse(diagram.IsShowing, "the strip is drawn over the main menu");

            menu.Show(false);
            yield return null;
            yield return null;

            Assert.IsTrue(diagram.IsShowing, "the strip did not come back when the menu closed");
        }

        /// <summary>With the help panel open, the strip stops short of it rather than running under it.</summary>
        [UnityTest]
        public IEnumerator TheStrip_StopsShortOfAnOpenHelpPanel()
        {
            yield return OnTheBoard();

            WaveformPanel diagram = Find<WaveformPanel>();
            HelpPanel help = Find<HelpPanel>();

            yield return ClickTheBadge();

            GameObject.Find("Help badge").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;

            Assert.Greater(help.ScreenLeftEdge, 0f, "sanity: the help panel should be open");
            Assert.LessOrEqual(ScreenRect(diagram.Root).xMax, help.ScreenLeftEdge + 0.5f,
                "the strip runs under the help panel");
        }

        /// <summary>
        /// The solved card sits above the strip while it is open, over the board's bottom row, and on
        /// its own row again once it is closed.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSolvedCard_SitsAboveTheOpenStrip()
        {
            yield return OnTheBoard();

            WinPanel card = Find<WinPanel>();
            WaveformPanel diagram = Find<WaveformPanel>();

            Assert.AreEqual(UiRows.SolvedCard.Offset, card.CardBottom, "sanity: closed, the card is on its row");

            yield return ClickTheBadge();

            Assert.GreaterOrEqual(card.CardBottom, UiRows.PanelFloor + diagram.Height,
                "the solved card would sit on the open strip");
        }

        /// <summary>
        /// A run is recorded into the strip's rows as it goes, the sources as their streams, and the
        /// strip is fitted to the run rather than drawn as a sliver at its left.
        /// </summary>
        [UnityTest]
        public IEnumerator ARun_IsRecordedIntoTheRows()
        {
            yield return OnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            WaveformPanel diagram = Find<WaveformPanel>();

            yield return ClickTheBadge();

            Assert.AreEqual(2 + 2 + WaveformPanel.WireRows, diagram.RowCount,
                "the half adder's rows should be its two sources, its two bins and the wire rows");
            Assert.GreaterOrEqual(diagram.VisibleTicks, WaveformZoom.ExpectedRun(4, 1, 4),
                "the strip should hold the half adder's whole expected run");
            Assert.Greater(diagram.TickWidth, 24f,
                "the run is drawn as narrow as when every tick was 24 wide, a sliver at the strip's left");

            session.Run();
            runner.SetPaused(true);

            for (int i = 0; i < 4; i++)
                runner.StepOneTick();

            yield return null;

            WaveformRecorder recorder = diagram.Recorder;
            Assert.AreEqual(3, recorder.LastTick, "the steps were not recorded");

            string a = "";
            for (int tick = 0; tick < 4; tick++)
                a += recorder.SourceCell(0, tick).Value == Bit.One ? "1" : "0";

            Assert.AreEqual("0011", a, "source a's row is not its stream");
            Assert.AreEqual(0, diagram.WindowStart, "a short run should not scroll");
        }

        /// <summary>
        /// After the window changes shape, the ticks still fit inside the strip.
        /// </summary>
        /// <remarks>
        /// The room for ticks was worked out again only when the strip's side insets moved, and they
        /// are fixed in canvas units, so a window turning squarer -- a browser tab going fullscreen
        /// is one -- left the old room in place: the waves, crosses and playhead ran on past the
        /// strip's right-hand edge. The canvas is made narrower here the way a squarer window makes
        /// it, through the scaler's reference resolution.
        /// </remarks>
        [UnityTest]
        public IEnumerator AfterTheWindowChangesShape_TheTicksStillFitTheStrip()
        {
            yield return OnTheBoard("out-of-step");

            WaveformPanel diagram = Find<WaveformPanel>();
            yield return ClickTheBadge();

            float before = diagram.Root.rect.width;
            Assert.LessOrEqual(diagram.VisibleTicks * diagram.TickWidth, TicksRoom(diagram) + 0.5f,
                "sanity: the ticks run past the strip before anything changed");

            var scaler = Find<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1200f, scaler.referenceResolution.y);

            yield return null;
            yield return null;
            yield return null;

            Assert.Less(diagram.Root.rect.width, before - 50f, "sanity: the strip did not get narrower");
            Assert.LessOrEqual(diagram.VisibleTicks * diagram.TickWidth, TicksRoom(diagram) + 0.5f,
                "after the window changed shape, the ticks are drawn on past the strip's edge");
        }

        /// <summary>The width the ticks have, right of the rows' names, in canvas units.</summary>
        private static float TicksRoom(WaveformPanel diagram) =>
            diagram.Root.rect.width - 2f * WaveformPanel.Padding - WaveformPanel.LabelWidth;

        /// <summary>The middle of the strip on screen, where the wheel is turned.</summary>
        private static Vector2 MiddleOfTheStrip(WaveformPanel diagram) => ScreenRect(diagram.Root).center;

        /// <summary>
        /// A point on the strip just right of the rows' names, where the first tick in view is drawn.
        /// </summary>
        private static Vector2 StartOfTheTicks(WaveformPanel diagram)
        {
            Rect strip = ScreenRect(diagram.Root);
            float scale = diagram.Root.lossyScale.x;
            return new Vector2(strip.xMin + (WaveformPanel.Padding + WaveformPanel.LabelWidth + 2f) * scale, strip.center.y);
        }

        private static readonly Vector2 WheelUp = new Vector2(0f, 120f);
        private static readonly Vector2 WheelDown = new Vector2(0f, -120f);

        /// <summary>
        /// The wheel over the strip zooms in from the fitted run and back out to it -- and does
        /// nothing to the strip anywhere else.
        /// </summary>
        /// <remarks>
        /// On Out of step, whose run of sixteen vectors fits narrower than the widest fitted tick, so
        /// the zoom is plainly from the fit; a short run zooms too (<c>WaveformZoomTests</c>). The
        /// wheel off the strip is turned over the banner, where nothing on the board can take it
        /// either.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheWheel_ZoomsInOverTheStrip_AndBackOutToTheWholeRun()
        {
            yield return OnTheBoard("out-of-step");

            WaveformPanel diagram = Find<WaveformPanel>();
            yield return ClickTheBadge();

            float fitted = diagram.TickWidth;
            Assert.AreEqual(1f, diagram.Zoom, "the strip opened zoomed");
            Assert.GreaterOrEqual(diagram.VisibleTicks, WaveformZoom.ExpectedRun(16, 1, 4),
                "the strip opened without the whole expected run in view");
            Assert.Less(fitted, WaveformZoom.MaxTickWidth, "sanity: the run fits too wide to zoom into");

            Vector2 offTheStrip = new Vector2(Screen.width * 0.5f, Screen.height - 4f);
            Assert.IsFalse(diagram.TakeWheel(offTheStrip, WheelUp, false), "the wheel off the strip zoomed it");
            Assert.AreEqual(1f, diagram.Zoom, "the wheel off the strip zoomed it");

            Assert.IsTrue(diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelUp, false), "the wheel did not zoom in");
            yield return null;

            Assert.Greater(diagram.Zoom, 1f);
            Assert.Greater(diagram.TickWidth, fitted, "zoomed in, the ticks are no wider");

            Assert.IsTrue(diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelDown, false), "the wheel did not zoom out");
            yield return null;

            Assert.AreEqual(1f, diagram.Zoom, "one notch out did not undo one notch in");
            Assert.AreEqual(fitted, diagram.TickWidth, 1e-3f, "back out, the run is not fitted as it was");

            Assert.IsFalse(diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelDown, false),
                "the wheel zoomed out past the whole run");
        }

        /// <summary>
        /// Zoomed in on a run longer than the view, the zoom keeps the start of the run where the
        /// cursor was, and Shift with the wheel moves through time -- back to following the run once
        /// it reaches the latest ticks.
        /// </summary>
        [UnityTest]
        public IEnumerator ZoomedIn_ShiftAndTheWheel_MoveThroughTheRun()
        {
            yield return OnTheBoard("out-of-step");

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            WaveformPanel diagram = Find<WaveformPanel>();

            yield return ClickTheBadge();

            session.Run();
            runner.SetPaused(true);

            for (int i = 0; i < 16; i++)
                runner.StepOneTick();

            yield return null;
            Assert.AreEqual(15, diagram.LastTick, "sanity: the steps were not recorded");

            // In at the start of the ticks, until the run no longer fits.
            for (int notch = 0; notch < 8 && diagram.VisibleTicks > diagram.LastTick; notch++)
            {
                diagram.TakeWheel(StartOfTheTicks(diagram), WheelUp, false);
                yield return null;
            }

            Assert.LessOrEqual(diagram.VisibleTicks, diagram.LastTick, "sanity: zoomed all the way in, the run still fits");
            Assert.AreEqual(0, diagram.WindowStart, "zoomed in at the run's start, the start went out of view");

            Assert.IsTrue(diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelDown, true), "Shift and the wheel moved nothing");
            yield return null;

            int step = WaveformZoom.PanTicks(diagram.VisibleTicks);
            Assert.AreEqual(step, diagram.WindowStart, "Shift and the wheel down did not move later");

            Assert.IsTrue(diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelUp, true));
            yield return null;
            Assert.AreEqual(0, diagram.WindowStart, "Shift and the wheel up did not move back");

            Assert.IsFalse(diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelUp, true), "moved before the first tick");

            for (int notch = 0; notch < 40; notch++)
            {
                diagram.TakeWheel(MiddleOfTheStrip(diagram), WheelDown, true);
                yield return null;
            }

            Assert.AreEqual(diagram.LastTick - diagram.VisibleTicks + 1, diagram.WindowStart,
                "moved to the end, the view does not show the latest ticks");

            // Following again: the next tick moves the view with it.
            int before = diagram.WindowStart;
            runner.StepOneTick();
            yield return null;

            // Asserted, not assumed: under an "if" the following check passed by being skipped.
            Assert.AreEqual(16, diagram.LastTick, "sanity: the run should still be going, and the step recorded");
            Assert.AreEqual(before + 1, diagram.WindowStart, "at the end, the view stopped following the run");
        }
    }
}
