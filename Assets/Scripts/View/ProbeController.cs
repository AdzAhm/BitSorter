using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Alt+click on a wire puts it in the timing diagram, or takes it out: up to four, W1 to W4.
    /// </summary>
    /// <remarks>
    /// A wire the player picks shows, tick by tick, what arrived at its far end -- which is where a
    /// bit waits or collides, and so where a timing fault is found. The recorder already records
    /// every wire from tick 0, so a wire picked mid-run shows everything it has done.
    ///
    /// **Any time the board is on screen**, running or not, which is why the wire is found with
    /// <see cref="SimulationRunner.NearestEdge"/> in the same call as the click: the hover the delay
    /// controller keeps is cleared whenever the board cannot be edited, all through a run.
    ///
    /// **The click is this controller's alone.** Nothing read Alt before it, so an Alt+click was a
    /// plain click: it placed the part in hand on the empty cell under the wire, or started a wire
    /// from a port beside it. <see cref="PointerRules"/> derives <see cref="PointerOwner.Probing"/>
    /// while Alt is held over the board, and every other mouse reader stands aside.
    ///
    /// The player never sees the word "probe": the diagram's rows and the tags on the board say
    /// W1 to W4, and the empty rows say what an Alt+click does.
    /// </remarks>
    public sealed class ProbeController : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;
        [SerializeField] private PointerGate _pointer;

        /// <summary>What a fifth wire is told: the limit, and the way out of it.</summary>
        public const string FullRefusal = "Four wires at most. Alt+click one to take it off.";

        private readonly WireProbes _probes = new WireProbes();
        private Camera _camera;

        /// <summary>The wires in the diagram.</summary>
        public WireProbes Probes => _probes;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_pointer == null) _pointer = FindFirstObjectByType<PointerGate>();
        }

        private void OnEnable()
        {
            if (_runner != null)
                _runner.Rebuilt += OnRebuilt;

            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Rebuilt -= OnRebuilt;

            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;
        }

        private void OnRebuilt() => _probes.Resolve(_runner.View, _runner.NodeCells);

        /// <summary>A new level starts with no wires in the diagram, whatever the last one had.</summary>
        private void OnLevelLoaded(LevelDefinition level) => _probes.Clear();

        private void Update()
        {
            if (_runner == null || !_runner.IsReady || _pointer == null)
                return;

            if (_pointer.Owner != PointerOwner.Probing || !_pointer.MayAct(PointerUser.Probe))
                return;

            Mouse mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            if (_camera == null)
                _camera = Camera.main;

            if (_camera == null)
                return;

            Vector2 world = _camera.ScreenToWorldPoint(mouse.position.ReadValue());
            Toggle(_runner.NearestEdge(world));
        }

        /// <summary>
        /// Puts a wire in the diagram or takes it out, as an Alt+click on it does. Nothing for no wire:
        /// an Alt+click on an empty square or a part does nothing, and says nothing.
        /// </summary>
        public ProbeToggle? Toggle(Edge edge)
        {
            if (edge == null || !WireProbes.TryKeyOf(edge, _runner.NodeCells, out WireKey key))
                return null;

            ProbeToggle result = _probes.Toggle(key, edge.Id);

            if (result == ProbeToggle.Full)
                _runner.RejectEdit(FullRefusal);

            return result;
        }
    }
}
