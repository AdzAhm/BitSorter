using UnityEngine;
using UnityEngine.EventSystems;

namespace BitSorter.View
{
    /// <summary>
    /// The one place that answers "may I act on this mouse frame". Every mouse-reading component
    /// asks it before doing anything.
    /// </summary>
    /// <remarks>
    /// Holds no state of its own beyond the palette flag, on purpose. The owner is recomputed from
    /// live facts every time it is asked, so there is nothing to leak and no way to wedge the board
    /// -- see <see cref="PointerRules"/> for why that matters more than it might sound.
    ///
    /// The pointer-over-interface question needs an EventSystem, which does not exist until the
    /// canvas is built. Until then it answers false, which is correct: with no interface on screen,
    /// the pointer can never be over one.
    /// </remarks>
    public sealed class PointerGate : MonoBehaviour
    {
        [Tooltip("Consulted so a wire drag owns the pointer for its whole duration.")]
        [SerializeField] private WiringController _wiring;

        /// <summary>
        /// Set by the palette while a gate is being dragged out of it. A plain property rather than
        /// a poll, because the palette is the only thing that knows, and it knows exactly.
        /// </summary>
        public bool PaletteDragging { get; set; }

        /// <summary>Whether a wire is mid-drag. False when no wiring controller is wired up.</summary>
        public bool WiringDragging => _wiring != null && _wiring.IsDragging;

        /// <summary>
        /// Whether the pointer is over a canvas widget. False until an EventSystem exists.
        /// </summary>
        public bool PointerOverUi =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>Who the pointer belongs to right now.</summary>
        public PointerOwner Owner => PointerRules.OwnerOf(PaletteDragging, WiringDragging, PointerOverUi);

        /// <summary>Whether <paramref name="user"/> may act on this frame.</summary>
        public bool MayAct(PointerUser user) => PointerRules.MayAct(user, Owner);
    }
}
