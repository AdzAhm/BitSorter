using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Enter runs the level's test vectors, R resets to an editable board, Q and E change level.
    /// Space pauses and resumes a run, and Right Arrow advances one tick while paused.
    /// </summary>
    /// <remarks>
    /// Uses the Input System package rather than the UnityEngine.Input class. This project has
    /// Active Input Handling set to "Input System Package (New)", under which the old API throws
    /// an InvalidOperationException the first time it is read.
    ///
    /// Keys rather than on-screen buttons, for now. IMGUI's GUI.Button does not consume Input System
    /// mouse events, so a Run button drawn in the HUD would fire and *also* let the same click reach
    /// PlacementController and WiringController, which already both act on
    /// leftButton.wasPressedThisFrame. Real buttons need a canvas and something to claim a click.
    ///
    /// Pause is deliberately independent of the run state: SimulationRunner.ClockRunning is what
    /// decides whether the clock may advance at all, so Space cannot start an editable board ticking.
    /// </remarks>
    public sealed class SimulationInput : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        private void Awake()
        {
            if (_session == null)
                _session = FindFirstObjectByType<LevelSession>();

            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();
        }

        private void Update()
        {
            if (_runner == null || _session == null)
                return;

            // Null whenever no keyboard is connected, so this must be checked every frame.
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // A panel covering the board takes the keyboard with it. A scrim stops clicks by itself,
            // because the pointer gate sees the interface, but keys would otherwise carry straight
            // through -- Q behind the main menu changing level under it, space starting the clock on
            // a board nobody can see.
            if (UiModal.AnyOpen)
                return;

            // Ctrl+Z and Ctrl+Y, with Ctrl+Shift+Z as the redo binding a lot of people reach for
            // first. Handled ahead of everything below and returning once matched, so a modified
            // press cannot also fire the unmodified action -- z and y are unbound today, but leaving
            // that to luck is how R ended up needing its own shift check further down.
            //
            // The session decides whether either is legal; both are refused unless the board is
            // editable, so this does not need to know the run state.
            if (keyboard.ctrlKey.isPressed)
            {
                if (keyboard.zKey.wasPressedThisFrame)
                {
                    if (keyboard.shiftKey.isPressed)
                        _session.Redo();
                    else
                        _session.Undo();

                    return;
                }

                if (keyboard.yKey.wasPressedThisFrame)
                {
                    _session.Redo();
                    return;
                }
            }

            // Both Enter keys, because a numpad Enter is not the same control.
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                _session.Run();

            // Shift+R clears the board and is RunControls' to handle, so it must not also reset here.
            if (keyboard.rKey.wasPressedThisFrame && !keyboard.shiftKey.isPressed)
                _session.ResetBoard();

            // Q/E rather than the serialized level name, which cannot be trusted to stick: rebuilding
            // the scene recreates the component with its default, and an inspector edit made during
            // Play is reverted when Play exits. Page Up/Down were the original binding but are absent
            // on compact keyboards; Tab/Shift+Tab and [ / ] were already taken by other controls.
            if (keyboard.eKey.wasPressedThisFrame)
                _session.CycleLevel(1);

            if (keyboard.qKey.wasPressedThisFrame)
                _session.CycleLevel(-1);

            if (keyboard.spaceKey.wasPressedThisFrame)
                _runner.TogglePause();

            if (keyboard.rightArrowKey.wasPressedThisFrame && _runner.IsPaused)
                _runner.StepOneTick();
        }
    }
}
