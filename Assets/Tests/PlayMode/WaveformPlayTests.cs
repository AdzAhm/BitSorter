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

        private static IEnumerator OnTheBoard()
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel(Level), "the level did not load");
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
        /// strip holds far more than the twenty-four ticks it was once planned at.
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
            Assert.Greater(diagram.VisibleTicks, 24, "the strip should hold most of a run");

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
    }
}
