using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BitSorter.View
{
    /// <summary>
    /// Says when the pointer lets go of the control it is on.
    /// </summary>
    /// <remarks>
    /// A slider reports every value it passes through and nothing when the drag ends, and the end is
    /// the moment worth acting on for anything costly: the volume is applied at every step of a drag
    /// but written out once, here. On the slider's own object, because a release goes to the object
    /// the press went to, and every component on it that listens hears it.
    /// </remarks>
    public sealed class PointerRelease : MonoBehaviour, IPointerUpHandler
    {
        /// <summary>Raised when the pointer is released after pressing this control.</summary>
        public event Action Released;

        public void OnPointerUp(PointerEventData eventData) => Released?.Invoke();
    }
}
