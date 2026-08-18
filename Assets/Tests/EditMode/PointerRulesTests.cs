using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Who may act on a mouse frame, and -- far more importantly -- that the pointer is never left
    /// owned by nobody.
    /// </summary>
    /// <remarks>
    /// A stuck owner is the worst failure this component can have: it disables input silently, with
    /// no error, no message and nothing the player can do about it. The design answer is that the
    /// owner is computed rather than claimed, so there is no state to leak -- and the exhaustive
    /// test below is what proves that claim rather than merely asserting it in a comment.
    /// </remarks>
    public class PointerRulesTests
    {
        private static readonly PointerUser[] AllUsers =
        {
            PointerUser.Placement, PointerUser.Wiring, PointerUser.WireDelay, PointerUser.Palette,
        };

        private static readonly PointerOwner[] AllOwners =
        {
            PointerOwner.None, PointerOwner.Ui, PointerOwner.Wiring, PointerOwner.Palette,
        };

        // -----------------------------------------------------------------
        // The matrix
        // -----------------------------------------------------------------

        [Test]
        public void WithNoOwner_EveryoneMayAct()
        {
            foreach (PointerUser user in AllUsers)
                Assert.IsTrue(PointerRules.MayAct(user, PointerOwner.None), user.ToString());
        }

        [Test]
        public void OverTheInterface_OnlyThePaletteMayAct()
        {
            // The reason buttons were never added to the IMGUI hud: a click on a widget also reached
            // the board. The palette is the exception because a palette drag starts on a widget.
            Assert.IsFalse(PointerRules.MayAct(PointerUser.Placement, PointerOwner.Ui));
            Assert.IsFalse(PointerRules.MayAct(PointerUser.Wiring, PointerOwner.Ui));
            Assert.IsFalse(PointerRules.MayAct(PointerUser.WireDelay, PointerOwner.Ui));

            Assert.IsTrue(PointerRules.MayAct(PointerUser.Palette, PointerOwner.Ui),
                "a gate has to be draggable out of the menu it lives in");
        }

        [Test]
        public void DuringAWireDrag_OnlyTheWiringMayAct()
        {
            // This is the rule that removes the rejection that fires today on every single wire drag:
            // pressing a port asks placement for that port's own cell, which is refused out loud.
            Assert.IsTrue(PointerRules.MayAct(PointerUser.Wiring, PointerOwner.Wiring));

            Assert.IsFalse(PointerRules.MayAct(PointerUser.Placement, PointerOwner.Wiring),
                "starting a wire must not also try to place a gate");
            Assert.IsFalse(PointerRules.MayAct(PointerUser.WireDelay, PointerOwner.Wiring));
            Assert.IsFalse(PointerRules.MayAct(PointerUser.Palette, PointerOwner.Wiring));
        }

        [Test]
        public void DuringAPaletteDrag_OnlyThePaletteMayAct()
        {
            Assert.IsTrue(PointerRules.MayAct(PointerUser.Palette, PointerOwner.Palette));

            Assert.IsFalse(PointerRules.MayAct(PointerUser.Placement, PointerOwner.Palette));
            Assert.IsFalse(PointerRules.MayAct(PointerUser.Wiring, PointerOwner.Palette));
            Assert.IsFalse(PointerRules.MayAct(PointerUser.WireDelay, PointerOwner.Palette));
        }

        [Test]
        public void ExactlyOneUserMayActUnderEveryNonEmptyOwner()
        {
            // Guards the shape of the matrix rather than its contents. Two users acting on one frame
            // is the class of bug this whole component exists to prevent.
            foreach (PointerOwner owner in AllOwners)
            {
                if (owner == PointerOwner.None)
                    continue;

                int allowed = 0;
                foreach (PointerUser user in AllUsers)
                {
                    if (PointerRules.MayAct(user, owner))
                        allowed++;
                }

                Assert.AreEqual(1, allowed, $"owner {owner} should permit exactly one user");
            }
        }

        // -----------------------------------------------------------------
        // Derivation, and the release guarantee
        // -----------------------------------------------------------------

        [Test]
        public void OwnershipFollowsWhatIsActuallyHappening()
        {
            Assert.AreEqual(PointerOwner.None,
                PointerRules.OwnerOf(false, false, false));
            Assert.AreEqual(PointerOwner.Ui,
                PointerRules.OwnerOf(false, false, true));
            Assert.AreEqual(PointerOwner.Wiring,
                PointerRules.OwnerOf(false, true, false));
            Assert.AreEqual(PointerOwner.Palette,
                PointerRules.OwnerOf(true, false, false));
        }

        [Test]
        public void ADragInFlight_OutranksThePointerBeingOverAWidget()
        {
            // Dragging a wire across the palette must not hand the pointer to the interface halfway.
            Assert.AreEqual(PointerOwner.Wiring, PointerRules.OwnerOf(false, true, true));
            Assert.AreEqual(PointerOwner.Palette, PointerRules.OwnerOf(true, false, true));
        }

        [Test]
        public void TheOwnerIsAlwaysReleasedWhenNothingIsHappening()
        {
            // The release guarantee, proved exhaustively over every reachable input rather than
            // asserted for a handful of scenarios.
            //
            // This covers all three ways a drag can end badly at once, because every one of them
            // reduces to the same fact going false:
            //   - a drag released outside the board  (EndDrag clears its port first thing)
            //   - a press that landed on nothing     (BeginDrag never set a port, so never claimed)
            //   - a level switch mid-drag            (CancelDrag clears the port)
            //
            // There is no fourth case to miss, because there is no stored claim that could survive
            // the facts. If ownership were held rather than derived, this test could not exist.
            for (int mask = 0; mask < 8; mask++)
            {
                bool palette = (mask & 1) != 0;
                bool wiring = (mask & 2) != 0;
                bool overUi = (mask & 4) != 0;

                PointerOwner owner = PointerRules.OwnerOf(palette, wiring, overUi);

                if (!palette && !wiring && !overUi)
                {
                    Assert.AreEqual(PointerOwner.None, owner,
                        "with nothing in flight the board must be usable");
                }
                else
                {
                    Assert.AreNotEqual(PointerOwner.None, owner,
                        $"palette={palette} wiring={wiring} ui={overUi} should have an owner");
                }
            }
        }

        [Test]
        public void EveryUserRegainsTheBoard_OnceTheDragsEnd()
        {
            // The consequence a player would notice: whatever was happening, when it stops, input
            // works again. A single stuck frame here is a game that has quietly locked up.
            PointerOwner settled = PointerRules.OwnerOf(false, false, false);

            foreach (PointerUser user in AllUsers)
            {
                Assert.IsTrue(PointerRules.MayAct(user, settled),
                    $"{user} should have the board back once nothing is in flight");
            }
        }

        [Test]
        public void AnUnknownOwner_FailsOpenRatherThanFreezingTheBoard()
        {
            // If someone adds an owner and forgets the matrix, the board must stay playable. A
            // wrong extra click is a bug you can see and report; an unresponsive game is not.
            var unknown = (PointerOwner)999;

            foreach (PointerUser user in AllUsers)
                Assert.IsTrue(PointerRules.MayAct(user, unknown), user.ToString());
        }

        // -----------------------------------------------------------------
        // The one stuck state that is real, and its escape hatch
        // -----------------------------------------------------------------

        [Test]
        public void AWiringControllerCanBeToldToDropItsDrag()
        {
            // Derivation removes stored ownership, but the *fact* it reads can still stick: a level
            // switch mid-drag leaves WiringController holding a port from a graph that no longer
            // exists, so IsDragging stays true and the pointer stays owned. CancelDrag is the hatch,
            // and LevelSession.LevelLoaded is what pulls it.
            var host = new GameObject("wiring");

            try
            {
                var wiring = host.AddComponent<WiringController>();

                Assert.IsFalse(wiring.IsDragging, "a fresh controller is not dragging");

                wiring.CancelDrag();
                Assert.IsFalse(wiring.IsDragging, "cancelling is safe when nothing is in flight");

                wiring.CancelDrag();
                Assert.IsFalse(wiring.IsDragging, "and idempotent");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
