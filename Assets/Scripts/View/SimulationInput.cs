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
    /// Keys are one of two ways in; the run buttons on the canvas are the other, and both end up
    /// at the same methods on SimulationRunner. This used to say there were no buttons, because
    /// IMGUI's GUI.Button does not consume Input System mouse events -- a Run button drawn that way
    /// fired and *also* let the same click reach PlacementController and WiringController. What it
    /// said was needed is what was then built: a canvas, and PointerGate to decide who owns a
    /// click.
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
            //
            // And the frame one closed on, for the reason the level list asks the same: the chapter
            // card closes on Enter, which is also the run key, and whichever of the two Unity updated
            // first decided whether that one press ran an empty board as well.
            if (UiModal.OpenOrJustClosed)
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
            //
            if (keyboard.eKey.wasPressedThisFrame)
                StepLevel(1, "This is the last level.");

            if (keyboard.qKey.wasPressedThisFrame)
                StepLevel(-1, "This is the first level.");

            if (keyboard.spaceKey.wasPressedThisFrame)
                _runner.TogglePause();

            if (keyboard.rightArrowKey.wasPressedThisFrame && _runner.IsPaused)
                _runner.StepOneTick();
        }

        /// <summary>
        /// Q or E: one level along, or a toast saying this is the end of the run.
        /// </summary>
        /// <remarks>
        /// Neither end wraps any more, and a key that does nothing and says nothing reads as a broken
        /// key. The toast is decided by where the player is, not by the load failing, so a level that
        /// failed to load for some other reason is not blamed on the run ending. The tutorial comes
        /// before the first level, so Q there says so; free play is not one of the levels, so
        /// neither key leaves it.
        /// </remarks>
        private void StepLevel(int step, string atTheEnd)
        {
            if (_session.LevelName == SandboxLevel.Key)
            {
                _runner.RejectEdit(NotALevel);
                return;
            }

            int current = _session.LevelIndex;

            if (LevelSession.NextIndex(current, step, _session.AvailableLevels.Count) < 0)
            {
                _runner.RejectEdit(current < 0 ? BeforeTheFirst : atTheEnd);
                return;
            }

            _session.CycleLevel(step);
        }

        /// <summary>What Q says in the tutorial, which comes before the first level.</summary>
        public const string BeforeTheFirst = "The tutorial comes before the first level.";

        /// <summary>
        /// What Q and E say in free play, which is not one of the levels -- and the key that goes to
        /// them, since stepping to one is what the player was trying to do.
        /// </summary>
        public const string NotALevel = "Free play is not one of the levels. M for the level list.";
    }
}
