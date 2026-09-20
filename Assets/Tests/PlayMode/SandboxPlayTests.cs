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
    /// Free play, through the real scene: what changing the setup does to the circuit around it.
    /// </summary>
    [TestFixture]
    public class SandboxPlayTests
    {
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

        private static IEnumerator OpenFreePlay()
        {
            Find<MainMenu>().Show(false);
            yield return null;

            Find<SandboxPanel>().Open();
            yield return null;
            yield return null;

            Assert.AreEqual(SandboxLevel.Key, Find<LevelSession>().LevelName, "sanity: free play did not load");
        }

        /// <summary>
        /// Flipping one input bit leaves the circuit's undo history and the part in hand alone.
        /// </summary>
        /// <remarks>
        /// Every setup edit used to reload free play from scratch. That is how a level switch is
        /// done, so it did what a level switch does: emptied the undo history, put the selection
        /// back on the first part, cancelled any run and wrote the save file -- for a single click
        /// on a bit.
        /// </remarks>
        [UnityTest]
        public IEnumerator ChangingTheSetup_KeepsTheUndoHistoryAndThePartInHand()
        {
            yield return TestScene.Load();
            yield return OpenFreePlay();

            LevelSession session = Find<LevelSession>();
            PlacementController placement = Find<PlacementController>();

            // Not the first part on the list, so a reset selection would show.
            Assert.IsTrue(placement.TrySelect(GateKind.Xor), "could not pick up an XOR");
            Assert.IsTrue(session.TryPlaceGate(GateKind.Xor, new Vector2Int(0, 0)), "could not place it");
            Assert.IsTrue(session.CanUndo, "sanity: placing a gate is an undoable step");

            Bit before = FirstBitOf(session, "A");

            Button bit = FindButton("bit 0 0");
            Assert.IsNotNull(bit, "no button for source A's first bit");

            bit.onClick.Invoke();
            yield return null;

            Assert.AreNotEqual(before, FirstBitOf(session, "A"),
                "sanity: the click should have flipped A's first bit");

            Assert.IsTrue(session.CanUndo, "flipping a bit threw away the circuit's undo history");
            Assert.AreEqual(GateKind.Xor, placement.Selected, "flipping a bit changed the part in hand");
            Assert.AreEqual(1, session.Blueprint.Placements.Count, "flipping a bit changed the circuit");
        }

        /// <summary>
        /// The level list opened over free play takes the setup panel down with the rest of the HUD.
        /// </summary>
        /// <remarks>
        /// The setup panel is part of the HUD now rather than a modal of its own, so it has to step
        /// aside like the banner and the parts list do. Seen in the browser build drawn through the
        /// level list's backdrop, along with the help badge.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheLevelList_TakesTheSetupPanelDownWithTheHud()
        {
            yield return TestScene.Load();
            yield return OpenFreePlay();

            Assert.IsNotNull(GameObject.Find("Sandbox setup"), "sanity: the setup panel should be showing");
            Assert.IsNotNull(GameObject.Find("Help badge"), "sanity: the help badge should be showing");

            Find<LevelSelectPanel>().Open();
            yield return null;
            yield return null;

            Assert.IsTrue(UiModal.AnyOpen, "sanity: the level list should count as open");
            Assert.IsNull(GameObject.Find("Sandbox setup"), "the setup panel is drawn over the level list");
            Assert.IsNull(GameObject.Find("Help badge"), "the help badge is drawn over the level list");
        }

        /// <summary>
        /// Opening free play does not put the sequential chapter's card on screen.
        /// </summary>
        /// <remarks>
        /// Free play stocks every part there is, registers included, and the card is fired by the
        /// first level whose parts list holds one. Without a guard it therefore takes the screen
        /// the first time anyone opens the sandbox -- and, being a full-screen panel, takes the
        /// setup panel down with the rest of the HUD while it is up.
        ///
        /// `LevelCatalog.IsOffCatalogue` is the one place that knows free play and the tutorial are
        /// not levels in the run, and this is one more thing that has to ask it.
        /// </remarks>
        [UnityTest]
        public IEnumerator OpeningFreePlay_DoesNotShowTheChapterCard()
        {
            yield return TestScene.Load();
            yield return OpenFreePlay();

            ChapterCard card = Find<ChapterCard>();

            Assert.IsNotNull(card, "sanity: the scene should have a chapter card");
            Assert.IsFalse(card.IsShowing, "free play is not a chapter of the run");
            Assert.IsFalse(UiModal.AnyOpen, "nothing should be covering the board in free play");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static Bit FirstBitOf(LevelSession session, string sourceId)
        {
            foreach (LevelFixture fixture in session.Level.Fixtures)
            {
                if (fixture.Id == sourceId)
                    return fixture.Stream[0];
            }

            Assert.Fail($"no source {sourceId} in free play");
            return Bit.Zero;
        }

        private static Button FindButton(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found.GetComponent<Button>() : null;
        }
    }
}
