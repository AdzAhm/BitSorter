using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The interactions that only exist once there is a real mouse, a real canvas and a running
    /// frame loop -- which is to say, the ones Edit Mode structurally cannot see.
    /// </summary>
    /// <remarks>
    /// Edit Mode never calls Awake, so every serialized reference stays null and no Update ever runs.
    /// That makes the entire input layer invisible to the existing suite: the arbitration bug this
    /// assembly was created to catch passed 353 Edit Mode tests while being plainly broken in play.
    ///
    /// Devices are created and destroyed per test through InputTestFixture rather than driving the
    /// real hardware, so these run headless and do not fight the editor for the actual cursor.
    /// </remarks>
    [TestFixture]
    public class PointerArbitrationPlayTests : InputTestFixture
    {
        private Mouse _mouse;
        private GameObject _canvasObject;
        private GameObject _eventSystemObject;
        private GameObject _host;

        private PointerGate _gate;
        private WiringController _wiring;

        public override void Setup()
        {
            base.Setup();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            foreach (GameObject o in new[] { _canvasObject, _eventSystemObject, _host })
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }

            base.TearDown();
        }

        /// <summary>A canvas and event system matching what the scene builder produces.</summary>
        private Button BuildInterface()
        {
            _eventSystemObject = new GameObject("Event System");
            _eventSystemObject.AddComponent<EventSystem>();
            _eventSystemObject.AddComponent<InputSystemUIInputModule>();

            _canvasObject = new GameObject("Game UI");
            var canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasObject.AddComponent<CanvasScaler>();
            _canvasObject.AddComponent<GraphicRaycaster>();

            // A button filling the whole screen, so "the pointer is over a widget" needs no fiddly
            // coordinate maths and cannot become flaky on a different resolution.
            var buttonObject = new GameObject("Run", typeof(RectTransform));
            buttonObject.transform.SetParent(_canvasObject.transform, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            buttonObject.AddComponent<Image>();   // a Graphic is what the raycaster actually hits
            return buttonObject.AddComponent<Button>();
        }

        private void BuildBoardSide()
        {
            _host = new GameObject("Simulation");
            _wiring = _host.AddComponent<WiringController>();
            _gate = _host.AddComponent<PointerGate>();
        }

        // -----------------------------------------------------------------
        // The regression this assembly exists for
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ARunButtonClick_DoesNotAlsoPlaceAGate()
        {
            // The failure that is invisible until someone plays the game and wonders why a stray XOR
            // appeared underneath the button they just pressed. IMGUI could not fix it -- GUI.Button
            // does not consume Input System events -- which is the documented reason buttons were
            // never added to the old hud.
            Button run = BuildInterface();
            BuildBoardSide();

            bool buttonFired = false;
            run.onClick.AddListener(() => buttonFired = true);

            Set(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return null;

            Assert.IsTrue(_gate.PointerOverUi,
                "sanity: the pointer must actually be over the button for this test to mean anything");

            Assert.AreEqual(PointerOwner.Ui, _gate.Owner);

            Assert.IsFalse(_gate.MayAct(PointerUser.Placement),
                "a click on the interface must not reach placement");
            Assert.IsFalse(_gate.MayAct(PointerUser.Wiring),
                "nor start a wire");
            Assert.IsFalse(_gate.MayAct(PointerUser.WireDelay),
                "nor re-time one");

            PressAndRelease(_mouse.leftButton);
            yield return null;
            yield return null;

            Assert.IsTrue(buttonFired, "the button itself must still work");
        }

        [UnityTest]
        public IEnumerator WithThePointerOffTheInterface_TheBoardIsUsableAgain()
        {
            // The other half: the gate must not simply disable the board whenever a canvas exists.
            BuildInterface();
            BuildBoardSide();

            // Off the button entirely -- the canvas is present but not under the cursor.
            Set(_mouse.position, new Vector2(-50f, -50f));
            yield return null;

            Assert.IsFalse(_gate.PointerOverUi);
            Assert.AreEqual(PointerOwner.None, _gate.Owner);

            foreach (PointerUser user in new[]
                     { PointerUser.Placement, PointerUser.Wiring, PointerUser.WireDelay })
            {
                Assert.IsTrue(_gate.MayAct(user), $"{user} should have the board");
            }
        }

        [UnityTest]
        public IEnumerator AWiringControllerRunsBeforeTheOtherMouseReaders()
        {
            // Pins the execution order the fix depends on. A press that grabs a port and a press that
            // places a gate are the same press, so the wire drag must be established first. Without
            // this the fix works on some runs and not others.
            //
            // Read off the attribute rather than the importer: MonoImporter is editor-only, and this
            // assembly builds for every platform.
            BuildBoardSide();
            yield return null;

            object[] attributes = typeof(WiringController)
                .GetCustomAttributes(typeof(DefaultExecutionOrder), false);

            Assert.AreEqual(1, attributes.Length,
                "WiringController must declare a DefaultExecutionOrder, or arbitration races");

            var order = (DefaultExecutionOrder)attributes[0];

            Assert.Less(order.order, 0,
                "it must run ahead of the default order so a drag is claimed before placement asks");
        }

        [UnityTest]
        public IEnumerator CancellingADrag_ReturnsTheBoardToEveryone()
        {
            // The stuck-owner guarantee, exercised against the real component rather than the pure
            // rules. A wiring controller that held its port after a cancel would leave the pointer
            // owned forever, with no error and nothing the player could do.
            BuildBoardSide();
            yield return null;

            _wiring.CancelDrag();
            yield return null;

            Assert.IsFalse(_wiring.IsDragging);
            Assert.IsFalse(_gate.WiringDragging);
            Assert.AreEqual(PointerOwner.None, _gate.Owner);
            Assert.IsTrue(_gate.MayAct(PointerUser.Placement));
        }
    }
}
