using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using Button = UnityEngine.UI.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The lock (Part 15h-4d, asked 2026-10-29): a padlock in the circuit's window's top strip, left of FIT, that locks the
    /// circuit open against change and unlocks it (<see cref="WorkbenchSession.Locked"/>), kept in the save beside it. An
    /// imported project opens locked, but for a course template, which is imported to be written into. Locked, the code's
    /// editor is read only too. In the top strip rather than the window's lower corner, which Ahmad asked for: in a browser
    /// the code is a box the page lays over everything under that strip, and the lock was under it exactly when the code
    /// was to be unlocked. Only a circuit has one: a sample and an exercise never lock.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        public const string LockButtonName = "Workbench lock";

        public const string LockedNotice = "locked: the circuit and its code take no change until the lock is pressed again";
        public const string UnlockedNotice = "unlocked: the circuit and its code take changes";

        private const float LockWidth = 40f;

        private Button _lock;
        private WorkbenchGraphic _lockIcon;
        private bool _lockShown;

        /// <summary>The padlock, shown over a circuit.</summary>
        public Button LockButton => _lock;

        private bool Lockable => _session != null && _sample == 0 && _exercise == null;

        private void BuildLock(SchematicPalette palette)
        {
            _lock = CanvasButton(LockButtonName, Root, "", out TextMeshProUGUI _, UiType.Body, palette);
            _lockIcon = Layer("padlock", _lock.GetComponent<RectTransform>(), WorkbenchGraphic.Layer.Padlock, Vector2.zero, new Vector2(LockWidth, 32f));
            _lock.onClick.AddListener(() =>
            {
                ToggleLock();
                UiTheme.Defocus();
            });
            Show(_lock, false);
        }

        /// <summary>Locks the circuit open, or unlocks it, and keeps which in the save; nothing on a sample or an exercise.</summary>
        public void ToggleLock()
        {
            if (!Lockable)
                return;
            // Code typed and not yet applied is applied first, as EXPORT does, or the lock refused: locked, it could never
            // be applied, nor the circuit exported with it (the review of the lock).
            if (!_session.Locked && !ApplyTypedCode("locked"))
                return;
            _session.Locked = !_session.Locked;
            if (Store() is ProgressStore store)
            {
                store.SetLocked(_documentKey, _session.Locked);
                store.Save();
            }
            Notice(_session.Locked ? LockedNotice : UnlockedNotice);
        }

        /// <summary>The padlock up over a circuit, drawn shut or open as it is, left of FIT's place; the code read only while shut.</summary>
        private void TickLock()
        {
            if (_codeView != null)
                _codeView.ReadOnly = _session != null && _session.Locked;
            if (_lock == null)
                return;

            bool shown = Lockable && !PageShowing;
            if (_codeView != null)
                _codeView.KeepsLockRoom = shown;
            if (shown != _lockShown)
            {
                Show(_lock, shown);
                if (shown)
                    _lock.transform.SetAsLastSibling();   // over the code's editor, as the switches beside it are
                _lockShown = shown;
            }
            if (!shown)
                return;

            // Left of FIT's place; over code, just left of the DRAWING switch, where the code's strip keeps room for it
            // (CodeEditorView.LockRoom) clear of REVERT.
            float right = _session.ShowsCode ? -(CodeEditorView.SwitchRoom + 8f)
                : _fit != null ? _fit.GetComponent<RectTransform>().anchoredPosition.x - 72f - 8f : -8f;
            Vector2 window = SizeOf(_regions.Circuit);
            Vector2 at = CornerOf(_regions.Circuit) + new Vector2(window.x + right - LockWidth, 8f);
            var rect = _lock.GetComponent<RectTransform>();
            if (rect.anchoredPosition != new Vector2(at.x, -at.y))
                TopLeft(rect, at, new Vector2(LockWidth, 32f));

            if (_lockIcon.Closed != _session.Locked)
            {
                _lockIcon.Closed = _session.Locked;
                _lockIcon.SetVerticesDirty();
            }
        }
    }
}
