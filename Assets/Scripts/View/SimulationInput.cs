using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Space pauses and resumes. Right Arrow advances exactly one tick, but only while paused.
    /// </summary>
    /// <remarks>
    /// Uses the Input System package rather than the UnityEngine.Input class. This project has
    /// Active Input Handling set to "Input System Package (New)", under which the old API throws
    /// an InvalidOperationException the first time it is read.
    /// </remarks>
    public sealed class SimulationInput : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();
        }

        private void Update()
        {
            if (_runner == null)
                return;

            // Null whenever no keyboard is connected, so this must be checked every frame.
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.spaceKey.wasPressedThisFrame)
                _runner.TogglePause();

            if (keyboard.rightArrowKey.wasPressedThisFrame && _runner.IsPaused)
                _runner.StepOneTick();
        }
    }
}
