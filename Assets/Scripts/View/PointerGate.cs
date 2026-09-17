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
    /// Holds no state of its own beyond the palette flag and one per-frame sample, on purpose. The
    /// owner is recomputed from live facts every time it is asked, so there is nothing to leak and
    /// no way to wedge the board -- see <see cref="PointerRules"/> for why that matters more than it
    /// might sound. The sample is only believed on the frame it was taken, so it cannot leak either.
    ///
    /// **It runs before everything else**, and that is what the sample is for. When a press and its
    /// release arrive in the same frame -- a touchpad tap does this -- the event system handles the
    /// whole click in its own Update. A button that closes its panel has then already removed the
    /// interface from under the pointer by the time a mouse reader at the default order asks, and
    /// the press landed on the board as well: the main menu's SANDBOX item dropped a gate that way.
    /// Looking once at the very start of the frame, before any interface has reacted to its input,
    /// catches the interface that the click was aimed at.
    ///
    /// The pointer-over-interface question needs an EventSystem, which does not exist until the
    /// canvas is built. Until then it answers false, which is correct: with no interface on screen,
    /// the pointer can never be over one.
    /// </remarks>
    [DefaultExecutionOrder(-30000)]
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

        /// <summary>Whether the pointer was over the interface when this frame began.</summary>
        private bool _overUiAtFrameStart;

        /// <summary>The frame <see cref="_overUiAtFrameStart"/> belongs to.</summary>
        private int _sampledFrame = -1;

        private void Update()
        {
            _overUiAtFrameStart = RaycastNow();
            _sampledFrame = Time.frameCount;
        }

        /// <summary>
        /// Whether the pointer is over a canvas widget now, or was when this frame began.
        /// </summary>
        /// <remarks>
        /// Either is enough. "Was" catches a click that closed the widget earlier in this frame;
        /// "now" covers a caller that asks before this component's first Update, or a widget that
        /// opened under the pointer during the frame.
        /// </remarks>
        public bool PointerOverUi =>
            (_sampledFrame == Time.frameCount && _overUiAtFrameStart) || RaycastNow();

        /// <summary>
        /// Whether the pointer is over a canvas widget at this moment. False until an EventSystem
        /// exists, which is correct: with no interface on screen the pointer cannot be over one.
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
        private bool RaycastNow()
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

        /// <summary>Who the pointer belongs to right now.</summary>
        public PointerOwner Owner => PointerRules.OwnerOf(PaletteDragging, WiringDragging, PointerOverUi);

        /// <summary>Whether <paramref name="user"/> may act on this frame.</summary>
        public bool MayAct(PointerUser user) => PointerRules.MayAct(user, Owner);
    }
}
