using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Enter runs the level's test vectors, R resets to an editable board. Space pauses and resumes a
    /// run, and Right Arrow advances one tick while it is paused.
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

            // Both Enter keys, because a numpad Enter is not the same control.
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                _session.Run();

            if (keyboard.rKey.wasPressedThisFrame)
                _session.ResetBoard();

            if (keyboard.spaceKey.wasPressedThisFrame)
                _runner.TogglePause();

            if (keyboard.rightArrowKey.wasPressedThisFrame && _runner.IsPaused)
                _runner.StepOneTick();
        }
    }
}
