using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The component that carries <see cref="PointerRules"/> into the scene, and the three
    /// controllers that must consult it.
    /// </summary>
    /// <remarks>
    /// Edit Mode never runs Awake, so what is reachable here is the gate's own derivation and the
    /// serialized wiring of its consumers. The behaviour a player would actually notice -- that
    /// starting a wire no longer flashes a rejection, and that a button click does not also place a
    /// gate -- needs a real EventSystem and a real mouse, and is covered by the Play Mode suite.
    /// </remarks>
    public class PointerGateTests
    {
        private GameObject _host;
        private PointerGate _gate;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("pointer gate");
            _gate = _host.AddComponent<PointerGate>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        // -----------------------------------------------------------------
        // Derivation
        // -----------------------------------------------------------------

        [Test]
        public void AFreshGate_OwnsNothing()
        {
            // No drags, no interface, no wiring controller attached. The board must be fully usable
            // -- a gate that defaults to owning the pointer would freeze the game on load.
            Assert.AreEqual(PointerOwner.None, _gate.Owner);
            Assert.IsFalse(_gate.PaletteDragging);
            Assert.IsFalse(_gate.WiringDragging, "no wiring controller means no drag");
            Assert.IsFalse(_gate.PointerOverUi, "with no EventSystem the pointer cannot be over one");
        }

        [Test]
        public void WithNoOwner_EveryConsumerMayAct()
        {
            foreach (PointerUser user in new[]
                     { PointerUser.Placement, PointerUser.Wiring, PointerUser.WireDelay, PointerUser.Palette })
            {
                Assert.IsTrue(_gate.MayAct(user), user.ToString());
            }
        }

        // -----------------------------------------------------------------
        // The gate has to actually gate
        // -----------------------------------------------------------------

        [Test]
        public void APaletteDrag_TakesTheBoardFromEveryoneElse()
        {
            _gate.PaletteDragging = true;

            Assert.AreEqual(PointerOwner.Palette, _gate.Owner);

            Assert.IsFalse(_gate.MayAct(PointerUser.Placement),
                "dragging a gate out of the menu must not also place one where the cursor is");
            Assert.IsFalse(_gate.MayAct(PointerUser.Wiring));
            Assert.IsFalse(_gate.MayAct(PointerUser.WireDelay));

            Assert.IsTrue(_gate.MayAct(PointerUser.Palette), "the palette owns its own drag");
        }

        [Test]
        public void EndingAPaletteDrag_GivesTheBoardBack()
        {
            // The release path, at the component level. A palette drag that ended without handing
            // the board back would look exactly like the game having crashed.
            _gate.PaletteDragging = true;
            Assert.AreNotEqual(PointerOwner.None, _gate.Owner);

            _gate.PaletteDragging = false;

            Assert.AreEqual(PointerOwner.None, _gate.Owner);
            Assert.IsTrue(_gate.MayAct(PointerUser.Placement), "placement should work again");
            Assert.IsTrue(_gate.MayAct(PointerUser.WireDelay), "and so should re-timing");
        }

        [Test]
        public void TheGateAgreesWithTheRulesItCarries()
        {
            // The component must not develop its own opinion. Whatever PointerRules says for the
            // current owner is what the gate must answer, for every consumer.
            foreach (bool palette in new[] { false, true })
            {
                _gate.PaletteDragging = palette;
                PointerOwner owner = _gate.Owner;

                foreach (PointerUser user in new[]
                         { PointerUser.Placement, PointerUser.Wiring, PointerUser.WireDelay, PointerUser.Palette })
                {
                    Assert.AreEqual(PointerRules.MayAct(user, owner), _gate.MayAct(user),
                        $"gate disagreed with the rules for {user} under {owner}");
                }
            }
        }

        // -----------------------------------------------------------------
        // The consumers must hold one
        // -----------------------------------------------------------------

        [Test]
        public void EveryMouseReadingController_HasAPointerGateField()
        {
            // The bug this whole step exists to fix is three components acting on one press with no
            // arbitration. A controller that does not even hold a gate cannot be consulting it, so
            // this catches the wiring being forgotten rather than merely being wrong.
            foreach (System.Type type in new[]
                     {
                         typeof(PlacementController),
                         typeof(WiringController),
                         typeof(WireDelayController),
                     })
            {
                var host = new GameObject(type.Name);

                try
                {
                    var component = host.AddComponent(type) as MonoBehaviour;
                    Assert.IsNotNull(component, type.Name);

                    var serialized = new UnityEditor.SerializedObject(component);
                    UnityEditor.SerializedProperty property = serialized.FindProperty("_pointer");

                    Assert.IsNotNull(property,
                        $"{type.Name} has no serialized _pointer field, so it cannot consult the gate");
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
        }
    }
}
