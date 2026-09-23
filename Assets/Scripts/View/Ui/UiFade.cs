using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Fades a piece of interface in as it appears. Never holds up a click.
    /// </summary>
    /// <remarks>
    /// Every panel used to appear in a single frame, and the only motion anywhere in the interface
    /// was the bits-lost meter's punch. A full-screen panel dropping over the board with no
    /// transition reads as the board being replaced rather than covered.
    ///
    /// Only the alpha moves. The group's <c>interactable</c> and <c>blocksRaycasts</c> are left as
    /// they are, so a panel takes a click on the frame it appears and a click on a half-faded
    /// button does what a click on a whole one does. A panel that refused clicks while it faded in
    /// would swallow them instead: it already covers the board, so the press reaches nothing.
    ///
    /// Counted on <see cref="Time.deltaTime"/>, as the meter's punch is, so the capture's fixed
    /// frame time makes a fade repeatable. It is an event counting from its own start, not ambient
    /// motion, so it does not read <see cref="ViewTime"/> -- which a capture pins still.
    ///
    /// Hiding is not faded. A panel that lingered on its way out would sit over the board after the
    /// player had closed it, which is what a stuck panel looks like.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UiFade : MonoBehaviour
    {
        /// <summary>How long a fade in takes.</summary>
        public const float Seconds = 0.16f;

        private static readonly List<UiFade> Live = new List<UiFade>();

        private CanvasGroup _group;
        private float _elapsed = Seconds;

        /// <summary>Whether any fade on screen is still moving.</summary>
        /// <remarks>
        /// Worked out from the fades themselves each time it is asked, never kept as a count: a
        /// count that missed a decrement would say a fade was moving for the rest of the session.
        /// </remarks>
        public static bool AnyMoving
        {
            get
            {
                foreach (UiFade fade in Live)
                {
                    if (fade != null && fade.IsMoving)
                        return true;
                }

                return false;
            }
        }

        /// <summary>How far through its fade this piece of interface is drawn, 0 to 1.</summary>
        public float Alpha => _group != null ? _group.alpha : 1f;

        private bool IsMoving => _elapsed < Seconds;

        /// <summary>Fades <paramref name="target"/> in from nothing, starting now.</summary>
        public static void In(Component target)
        {
            if (target == null)
                return;

            // TryGetComponent rather than GetComponent and a null check: in the editor a missing
            // component comes back as a stand-in object that is not null to the ?? operator.
            if (!target.TryGetComponent(out UiFade fade))
                fade = target.gameObject.AddComponent<UiFade>();

            fade.Restart();
        }

        private void Restart()
        {
            if (_group == null && !TryGetComponent(out _group))
                _group = gameObject.AddComponent<CanvasGroup>();

            _elapsed = 0f;
            _group.alpha = 0f;
        }

        private void OnEnable() => Live.Add(this);

        private void Update()
        {
            if (!IsMoving)
                return;

            _elapsed += Time.deltaTime;

            // Eased out: most of the way in the first few frames, so it reads as quick.
            float t = Mathf.Clamp01(_elapsed / Seconds);
            _group.alpha = 1f - (1f - t) * (1f - t);
        }

        /// <summary>
        /// Hidden mid-fade, it finishes at once: whole the next time it appears, and not counted as
        /// moving while nobody can see it.
        /// </summary>
        private void OnDisable()
        {
            Live.Remove(this);
            _elapsed = Seconds;

            if (_group != null)
                _group.alpha = 1f;
        }
    }
}
