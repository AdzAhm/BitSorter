using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace BitSorter.View
{
    /// <summary>
    /// Drawing only when something changes (5.1, Ahmad: "we need a good optimization for that"): the Windows build drew every
    /// frame at the screen's rate with nothing moving, about 56% of the GPU in Task Manager. While nothing happens the window is
    /// drawn one frame in ten (<see cref="IdleRendering"/>, Unity's <see cref="OnDemandRendering"/>); input is read every frame,
    /// so the first frame of anything done is drawn at once. In a desktop build alone, as the frame cap is: never in the editor,
    /// so every test and reference shot is drawn every frame, unless a test asks (<see cref="ThrottlesWhenIdle"/>).
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>
        /// Whether the window is drawn less often while nothing happens: in a desktop build. Not in the editor, whose Game view
        /// the tests and the reference shots draw in, and not in a browser yet, where it has not been tried.
        /// </summary>
        public static bool ThrottlesWhenIdle { get; set; } =
#if UNITY_EDITOR || UNITY_WEBGL
            false;
#else
            true;
#endif

        private readonly IdleRendering _idle = new IdleRendering();
        private int _idleWidth = -1, _idleHeight = -1;
        private int _redrawnFrame = -1;

        /// <summary>One frame in how many the window is drawn now: 1 while anything happens.</summary>
        public int DrawInterval => _idle.Interval;

        /// <summary>What happened this frame, and so how often to draw: asked first in every frame, whatever is up.</summary>
        private void TickIdle()
        {
            if (!ThrottlesWhenIdle)
            {
                if (OnDemandRendering.renderFrameInterval != 1)
                    OnDemandRendering.renderFrameInterval = 1;
                return;
            }

            bool resized = Screen.width != _idleWidth || Screen.height != _idleHeight;
            _idleWidth = Screen.width;
            _idleHeight = Screen.height;

            var facts = new IdleFacts(
                input: InputThisFrame(),
                resized: resized,
                playing: Playing,
                making: _session?.Run != null && !_session.Run.Done,
                working: (_logic != null && !_logic.Done) || (_states != null && !_states.Done) || (_measure != null && !_measure.Done),
                moving: UiFade.AnyMoving || (_credits != null && _credits.IsShowing && !_credits.AtRest),   // the roll the workbench found as it was built
                redrawn: _redrawnFrame >= UnityEngine.Time.frameCount - 1);
            int interval = _idle.Frame(UnityEngine.Time.unscaledTimeAsDouble, facts);
            if (OnDemandRendering.renderFrameInterval != interval)
                OnDemandRendering.renderFrameInterval = interval;
        }

        /// <summary>The pointer moved, a mouse button or a key is down, or the wheel turned.</summary>
        private static bool InputThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue() != Vector2.zero || mouse.scroll.ReadValue() != Vector2.zero
                || mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
                return true;
            return UiText.AnyKeyDown;
        }

        /// <summary>Every frame drawn again once the workbench is gone: a look changed, a test done.</summary>
        private void StopIdling()
        {
            if (OnDemandRendering.renderFrameInterval != 1)
                OnDemandRendering.renderFrameInterval = 1;
        }
    }
}
