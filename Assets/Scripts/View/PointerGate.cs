using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

        /// <summary>Whoever is currently dragging a gate out of the palette, or null.</summary>
        private Object _paletteDragOwner;

        /// <summary>
        /// Whether a gate is being dragged out of the palette.
        /// </summary>
        /// <remarks>
        /// Derived from whether the dragging object still exists, rather than from a flag someone
        /// remembered to clear. That is not fussiness: <see cref="GatePaletteView"/> destroys every
        /// row when the level changes, so a drag interrupted by a level switch never receives its
        /// OnEndDrag and a stored flag would stay true forever -- silently disabling the whole board
        /// with no error.
        ///
        /// Unity's overloaded null check reports a destroyed object as null, so the row being torn
        /// out from under the drag releases the pointer by itself.
        /// </remarks>
        public bool PaletteDragging => _paletteDragOwner != null;

        /// <summary>Claims the pointer for a palette drag.</summary>
        public void BeginPaletteDrag(Object owner) => _paletteDragOwner = owner;

        /// <summary>
        /// Releases a palette drag. Ignores a release from anyone but the current owner, so a stale
        /// end-of-drag cannot cancel a newer one.
        /// </summary>
        public void EndPaletteDrag(Object owner)
        {
            if (_paletteDragOwner == owner)
                _paletteDragOwner = null;
        }

        /// <summary>Whether a wire is mid-drag. False when no wiring controller is wired up.</summary>
        public bool WiringDragging => _wiring != null && _wiring.IsDragging;

        /// <summary>Reused across frames; RaycastAll clears it before filling it.</summary>
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        private PointerEventData _probe;

        /// <summary>
        /// Whether the pointer is over a canvas widget. False until an EventSystem exists, which is
        /// correct: with no interface on screen the pointer cannot be over one.
        /// </summary>
        /// <remarks>
        /// Raycasts explicitly rather than calling EventSystem.IsPointerOverGameObject().
        ///
        /// The no-argument overload of that method reports the state of whichever pointer id the
        /// input module touched most recently, and it reports it from the module's own update rather
        /// than from now. That makes it depend on component execution order and on which device moved
        /// last -- so the answer can lag a frame, or belong to a different pointer entirely. For a
        /// method that decides whether a click reaches the board, "usually right" is not good enough:
        /// being wrong for one frame means a click doing two things at once, which is the exact bug
        /// this whole component exists to prevent.
        ///
        /// A raycast asks the question directly, at the moment it is asked, about this pointer. The
        /// event data and result list are both reused, so the happy path allocates nothing.
        /// </remarks>
        public bool PointerOverUi
        {
            get
            {
                EventSystem events = EventSystem.current;
                Mouse mouse = Mouse.current;

                if (events == null || mouse == null)
                    return false;

                if (_probe == null)
                    _probe = new PointerEventData(events);

                _probe.Reset();
                _probe.position = mouse.position.ReadValue();

                _hits.Clear();
                events.RaycastAll(_probe, _hits);

                return _hits.Count > 0;
            }
        }

        /// <summary>Who the pointer belongs to right now.</summary>
        public PointerOwner Owner => PointerRules.OwnerOf(PaletteDragging, WiringDragging, PointerOverUi);

        /// <summary>Whether <paramref name="user"/> may act on this frame.</summary>
        public bool MayAct(PointerUser user) => PointerRules.MayAct(user, Owner);
    }
}
