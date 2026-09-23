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

            // The HUD stays under a panel while it fades in over it, and steps aside once covered.
            for (int frame = 0; frame < 600 && UiFade.AnyMoving; frame++)
                yield return null;

            Assert.IsFalse(UiFade.AnyMoving, "the level list never finished fading in");
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

        /// <summary>
        /// When the truth-table button is off, the reason printed beside it is the real one.
        /// </summary>
        /// <remarks>
        /// The button is off in two situations -- too many sources for a full table to fit, or
        /// streams that already are one -- and a fresh sandbox opens in the second. The note only
        /// knew about the first, so every fresh free-play session told the player that a table
        /// "needs 3 sources or fewer" while showing them two. Seen in the reference screenshots.
        ///
        /// A false reason is worse than none: it sends somebody looking for a problem that is not
        /// there. Checked for truth rather than wording, so the sentence can be rewritten freely.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTableButtonsReason_IsTheRealOne()
        {
            yield return TestScene.Load();
            yield return OpenFreePlay();

            Button table = FindButton("truth table");
            Assert.IsNotNull(table, "no truth table button in the setup panel");
            Assert.IsFalse(table.interactable,
                "sanity: a fresh sandbox opens on the full two-input table, so the button is off");

            int sources = int.Parse(SetupLabel("Sources count").text);
            string[] notes = SetupNotes();

            Assert.IsNotEmpty(notes, "the table button is off and nothing says why");

            foreach (string note in notes)
            {
                if (note.Contains("sources or fewer"))
                {
                    Assert.Greater(sources, SandboxRules.MaxTableSources,
                        $"the panel says \"{note}\" while showing {sources} sources -- the button is " +
                        "off because the streams are already every combination, not because of the count");
                }
            }
        }

        /// <summary>
        /// The setup panel's notes wrap inside the panel instead of running off its edge.
        /// </summary>
        /// <remarks>
        /// The helper that draws them is called Wrapped and never turned wrapping on: every label
        /// starts out NoWrap, so a note longer than the panel's inner width ran straight past its
        /// right edge -- seen in the reference screenshots. It also gave every note a fixed box and
        /// a fixed step down, so a note that did wrap to a third line would have printed over the
        /// row beneath it.
        ///
        /// Four sources, because that is what shows the longest note; a shorter one might happen
        /// to fit on a line and pass this for the wrong reason.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheSetupPanelsNotes_StayInsideThePanel()
        {
            yield return TestScene.Load();
            yield return OpenFreePlay();

            for (int press = 0; press < 6 && Sources() <= SandboxRules.MaxTableSources; press++)
            {
                StepButton("Sources", "+").onClick.Invoke();
                yield return null;
                yield return null;
            }

            Assert.Greater(Sources(), SandboxRules.MaxTableSources,
                "sanity: the source count never passed the table limit, so the long note never showed");

            RectTransform panel = GameObject.Find("Sandbox setup").GetComponent<RectTransform>();
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float panelRight = corners[2].x;

            bool sawANote = false;

            foreach (TMPro.TextMeshProUGUI note in SetupNoteLabels())
            {
                sawANote = true;
                note.ForceMeshUpdate();

                Bounds drawn = note.textBounds;
                float textRight = note.transform.TransformPoint(new Vector3(drawn.max.x, 0f, 0f)).x;

                Assert.LessOrEqual(textRight, panelRight + 0.5f,
                    $"\"{note.text}\" runs {textRight - panelRight:F0}px past the setup panel's right edge");

                Assert.GreaterOrEqual(drawn.min.y, note.rectTransform.rect.yMin - 0.5f,
                    $"\"{note.text}\" wraps below its own box, onto the row beneath it");
            }

            Assert.IsTrue(sawANote, "sanity: four sources should show why the table button is off");
        }

        private static int Sources() => int.Parse(SetupLabel("Sources count").text);

        /// <summary>
        /// The - or + on one stepper row. Every stepper names its buttons the same, so the row is
        /// found by sitting at the same height as its caption.
        /// </summary>
        private static Button StepButton(string caption, string glyph)
        {
            float row = SetupLabel(caption).rectTransform.anchoredPosition.y;
            GameObject root = GameObject.Find("Sandbox setup");

            foreach (Button button in root.GetComponentsInChildren<Button>())
            {
                if (button.name == $"step {glyph}"
                    && Mathf.Abs(button.GetComponent<RectTransform>().anchoredPosition.y - row) < 0.5f)
                {
                    return button;
                }
            }

            Assert.Fail($"no '{glyph}' on the {caption} row");
            return null;
        }

        private static System.Collections.Generic.List<TMPro.TextMeshProUGUI> SetupNoteLabels()
        {
            var notes = new System.Collections.Generic.List<TMPro.TextMeshProUGUI>();

            foreach (TMPro.TextMeshProUGUI label in
                     GameObject.Find("Sandbox setup").GetComponentsInChildren<TMPro.TextMeshProUGUI>())
            {
                if (label.name == "note" && !string.IsNullOrEmpty(label.text))
                    notes.Add(label);
            }

            return notes;
        }

        /// <summary>A label inside free play's setup panel, by the name the panel builds it with.</summary>
        private static TMPro.TextMeshProUGUI SetupLabel(string name)
        {
            GameObject root = GameObject.Find("Sandbox setup");
            Assert.IsNotNull(root, "the setup panel is not on screen");

            foreach (TMPro.TextMeshProUGUI label in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
            {
                if (label.name == name)
                    return label;
            }

            Assert.Fail($"no label '{name}' in the setup panel");
            return null;
        }

        /// <summary>Every explanatory note the setup panel is currently showing.</summary>
        private static string[] SetupNotes()
        {
            GameObject root = GameObject.Find("Sandbox setup");
            Assert.IsNotNull(root, "the setup panel is not on screen");

            var notes = new System.Collections.Generic.List<string>();

            foreach (TMPro.TextMeshProUGUI label in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
            {
                if (label.name == "note" && !string.IsNullOrEmpty(label.text))
                    notes.Add(label.text);
            }

            return notes.ToArray();
        }

        private static Button FindButton(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found.GetComponent<Button>() : null;
        }
    }
}
