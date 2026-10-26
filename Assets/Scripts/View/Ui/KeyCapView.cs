using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A key drawn beside a button, shown or hidden as <see cref="KeyCaps"/> says, and again whenever the setting changes.
    /// </summary>
    /// <remarks>
    /// Hidden by its alpha, never by switching it off: a key switched off cannot hear the setting come back, and whatever
    /// else shows or hides it -- a panel, the HUD stepping aside -- goes on doing so untouched.
    /// </remarks>
    public sealed class KeyCapView : MonoBehaviour
    {
        private CanvasGroup _group;

        /// <summary>Whether this key is drawn now.</summary>
        public bool Drawn => _group == null || _group.alpha > 0f;

        private void OnEnable()
        {
            KeyCaps.Changed += Apply;
            Apply();
        }

        private void OnDisable() => KeyCaps.Changed -= Apply;

        private void Apply()
        {
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            _group.alpha = KeyCaps.Shown ? 1f : 0f;
        }
    }
}
