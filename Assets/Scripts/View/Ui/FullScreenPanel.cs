using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A panel that takes the whole screen: the workbench, which Settings and the credits are pages of.
    /// </summary>
    /// <remarks>
    /// Showing one is four things, always together: make it active, put it in front of whatever
    /// was opened before it, tell <see cref="UiModal"/> the board is covered, and tell it again
    /// when it is not -- including when the panel is switched off without being closed, or the
    /// keyboard stays dead behind a panel nobody can see. Five panels each wrote those four out in
    /// the 4.x game, and they had drifted: the chapter card never brought itself to the front, so which of two
    /// panels covered the other depended on the order they had been built in.
    ///
    /// What differs between them is what <see cref="OnShown"/> and <see cref="OnHidden"/> are for.
    /// </remarks>
    public abstract class FullScreenPanel : MonoBehaviour
    {
        /// <summary>The panel's full-screen root, built by the panel itself.</summary>
        protected RectTransform Root { get; set; }

        /// <summary>Whether the panel is up.</summary>
        public bool IsShowing { get; private set; }

        /// <summary>Shows or hides the panel, and keeps <see cref="UiModal"/> told.</summary>
        /// <remarks>
        /// A panel that appears fades in (<see cref="UiFade"/>), taking clicks from its first
        /// frame; one asked to show while it is already up does not start again from nothing.
        /// </remarks>
        protected void SetShowing(bool visible)
        {
            bool appearing = visible && !IsShowing;
            IsShowing = visible;

            if (Root != null && Root.gameObject.activeSelf != visible)
                Root.gameObject.SetActive(visible);

            if (visible)
            {
                UiTheme.BringToFront(Root);

                if (appearing)
                {
                    UiFade.In(Root);
                    _openedOn = Time.frameCount;
                }

                UiModal.Opened(this);
                OnShown();
            }
            else
            {
                UiModal.Closed(this);
                OnHidden();
            }
        }

        /// <summary>
        /// Whether the panel came up this frame. A panel does not answer a key on the frame it
        /// opened: the key was pressed before it was there, at something else.
        /// </summary>
        /// <remarks>
        /// Escape takes down the solved card, and at the end of the tutorial that brings the
        /// tutorial's own card up in the same frame -- a card that closes on Escape. Whenever it
        /// updated after the director that raised it, the one press would finish the tutorial on a
        /// card nobody saw.
        /// </remarks>
        protected bool OpenedThisFrame => _openedOn == Time.frameCount;

        private int _openedOn = -1;

        /// <summary>Called once the panel is up and in front, for anything it redraws on opening.</summary>
        protected virtual void OnShown() { }

        /// <summary>Called once the panel is down.</summary>
        protected virtual void OnHidden() { }

        /// <summary>
        /// A panel switched off while open stops covering the board. A level switch destroys panels,
        /// and <see cref="UiModal"/> copes with that; this covers one that is only disabled.
        /// </summary>
        protected virtual void OnDisable() => UiModal.Closed(this);
    }
}
