using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws a stub for every port, and makes the input stubs say what they are holding: hollow
    /// when empty, filled with the bit's own colour when one is waiting, and pulsing when a second
    /// bit is one tick away from colliding with it.
    /// </summary>
    /// <remarks>
    /// Positions come from <see cref="PortGeometry"/>, the same function the hit tester uses.
    ///
    /// This component owns the input stub's colour, sprite and scale outright, and everything that
    /// wants to say something about a port says it here. Ports carry four overlapping messages --
    /// resting state, an imminent collision, the flash when one happens, and the value being held
    /// -- and a second component writing the same SpriteRenderer would produce whichever of them
    /// ran last that frame. That is the drift <see cref="PortGeometry"/>'s own remarks warn about,
    /// arrived at from the other direction.
    ///
    /// Filled against hollow is doing the real work. A paused board has no animation to read, so
    /// the difference between holding and empty has to survive being looked at once, and shape
    /// does that where brightness alone would not. The pulse is emphasis on top, never the carrier.
    ///
    /// Output stubs are deliberately left alone. <see cref="OutputPort"/> holds no state, so giving
    /// them the same treatment would imply a symmetry that does not exist.
    /// </remarks>
    public sealed class PortRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameObject _stubPrefab;

        [Tooltip("An input port with nothing in it.")]
        [SerializeField] private Color _inputColour = new Color(0.62f, 0.66f, 0.76f);
        [SerializeField] private Color _outputColour = new Color(0.80f, 0.78f, 0.58f);

        [SerializeField] private Color _collisionColour = new Color(1.00f, 0.28f, 0.24f);
        [SerializeField] private float _flashSeconds = 0.35f;
        [SerializeField] private float _flashScale = 1.9f;

        [Tooltip("How much larger a socket holding a bit is drawn than an empty one.")]
        [SerializeField] private float _heldScale = 1.28f;

        [Tooltip("How far a socket swells at the peak of an imminent-collision throb.")]
        [SerializeField] private float _warningScale = 1.7f;

        [Tooltip("Halo on a socket that is holding a bit, so a landed bit keeps the glow it flew with.")]
        [SerializeField] private float _heldGlowScale = 2.6f;
        [SerializeField] private float _heldGlowAlpha = 0.60f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Input stubs by node id and port index, so a collision can find its stub.</summary>
        private readonly Dictionary<PortAddress, SpriteRenderer> _inputStubs =
            new Dictionary<PortAddress, SpriteRenderer>();

        /// <summary>The halo behind each input stub, lit only while the socket holds something.</summary>
        private readonly Dictionary<PortAddress, SpriteRenderer> _inputGlows =
            new Dictionary<PortAddress, SpriteRenderer>();

        /// <summary>Seconds of flash still owed to a port, keyed the same way.</summary>
        private readonly Dictionary<PortAddress, float> _flashing = new Dictionary<PortAddress, float>();

        /// <summary>Which collisions have already been shown, so one is not flashed twice.</summary>
        private readonly CollisionWatch _collisions = new CollisionWatch();
        private readonly List<PortAddress> _active = new List<PortAddress>();

        /// <summary>
        /// Ports a bit will hit next tick, and whether the bit already sitting there dies too.
        /// Rebuilt every frame rather than kept, so it cannot outlive the tick it describes.
        /// </summary>
        private readonly Dictionary<PortAddress, bool> _doomed = new Dictionary<PortAddress, bool>();

        private Transform _container;
        private int _builtRevision = -1;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            _container = new GameObject("Ports").transform;
            _container.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (_runner == null || !_runner.IsReady)
                return;

            if (_runner.GraphRevision != _builtRevision)
            {
                Rebuild();
                _builtRevision = _runner.GraphRevision;
            }

            Forecast();
            ApplyPortStates();

            // Last, so the aftermath of a collision paints over the resting state rather than
            // being overwritten by it.
            DetectCollisions();
            AdvanceFlashes();
        }

        // -----------------------------------------------------------------
        // Waiting state
        // -----------------------------------------------------------------

        /// <summary>
        /// Finds every port about to be hit, from the edges rather than the ports, because only an
        /// edge knows what is on its way.
        /// </summary>
        /// <remarks>
        /// Two wires can deliver into one port on the same tick, so the worse outcome wins: if
        /// either arrival differs from the value being held, the held bit does not survive.
        /// </remarks>
        private void Forecast()
        {
            _doomed.Clear();

            SimulationView view = _runner.View;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);

                if (edge == null)
                    continue;   // retired id

                if (!PortState.WillCollide(edge, out bool heldBitDies))
                    continue;

                var key = new PortAddress(edge.Target.Owner.Id, true, edge.Target.Index);

                _doomed[key] = _doomed.TryGetValue(key, out bool already)
                    ? already || heldBitDies
                    : heldBitDies;
            }
        }

        private void ApplyPortStates()
        {
            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                if (node == null)
                    continue;   // retired id

                for (int i = 0; i < node.InputCount; i++)
                {
                    var key = new PortAddress(id, true, i);

                    // A port mid-flash is showing what just happened to it, which outranks what it
                    // is holding now -- and on a mixed collision it is holding nothing.
                    if (_flashing.ContainsKey(key))
                        continue;

                    if (!_inputStubs.TryGetValue(key, out SpriteRenderer stub) || stub == null)
                        continue;

                    Paint(stub, node.In(i), key);
                }
            }
        }

        private void Paint(SpriteRenderer stub, InputPort port, PortAddress key)
        {
            bool holding = port.IsOccupied;

            stub.sprite = holding ? ProceduralSprites.Dot() : ProceduralSprites.Ring();

            Color colour = RestingColourOf(port);
            float scale = holding ? _heldScale : 1f;

            if (_doomed.TryGetValue(key, out bool heldBitDies))
            {
                // A throb rather than a blink, so it reads as urgency without competing with the
                // flash a real collision produces.
                float pulse = PortState.Pulse(Time.time, PortState.WarningHz);

                colour = Color.Lerp(colour, PortState.WarningColour(heldBitDies), pulse);
                scale *= Mathf.Lerp(1f, _warningScale, pulse);
            }

            stub.color = colour;
            stub.transform.localScale = Vector3.one * PortGeometry.StubSize * scale;

            // A held bit needs the glow it had on the wire. Without it a waiting zero -- deliberately
            // the dimmest colour on the board -- disappears against the gate it is sitting on, which
            // is precisely the gate that has gone dim because that bit is stuck there.
            if (_inputGlows.TryGetValue(key, out SpriteRenderer glow) && glow != null)
                glow.color = new Color(colour.r, colour.g, colour.b, holding ? _heldGlowAlpha : 0f);
        }

        /// <summary>
        /// What a port looks like when nothing is happening to it: the bit's own colour if it is
        /// holding one, so a bit that lands keeps the identity it had on the wire.
        /// </summary>
        private Color RestingColourOf(InputPort port) =>
            port != null && port.IsOccupied
                ? BitVisuals.ColourFor(port.Pending.Value)
                : _inputColour;

        private InputPort PortAt(PortAddress key)
        {
            Node node = _runner.NodeAt(key.NodeId);

            return node != null && key.Index < node.InputCount ? node.In(key.Index) : null;
        }

        // -----------------------------------------------------------------
        // Collision aftermath
        // -----------------------------------------------------------------

        /// <summary>
        /// LastCollisionTick names exactly which port lost bits and on which tick, so no state has
        /// to be diffed here.
        /// </summary>
        /// <remarks>
        /// LastCollisionTick, not LastCorruptedTick. The second is the poison flag and is set only
        /// where a port is emptied, so keying on it meant a matching-value collision -- which loses
        /// a bit and leaves the port holding its value -- never flashed at all.
        /// </remarks>
        /// <remarks>
        /// Whether a collision is news belongs to <see cref="CollisionWatch"/>, which is reachable
        /// from Edit Mode; this is left with the countdown and the drawing. It no longer compares
        /// against the tick just executed -- a standing fact is not an event, and treating it as one
        /// re-armed the flash on every frame once the clock stopped.
        /// </remarks>
        private void DetectCollisions()
        {
            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);
                if (node == null)
                    continue;

                for (int i = 0; i < node.InputCount; i++)
                {
                    var key = new PortAddress(id, true, i);

                    if (!_collisions.IsNews(key, node.In(i).LastCollisionTick))
                        continue;

                    _flashing[key] = _flashSeconds;
                }
            }
        }

        private void AdvanceFlashes()
        {
            if (_flashing.Count == 0)
                return;

            // Keys are snapshotted first: the loop writes the countdown back and removes finished
            // entries, neither of which is legal while enumerating the dictionary.
            _active.Clear();
            foreach (KeyValuePair<PortAddress, float> entry in _flashing)
                _active.Add(entry.Key);

            for (int i = 0; i < _active.Count; i++)
            {
                PortAddress key = _active[i];
                float remaining = _flashing[key] - Time.deltaTime;

                if (!_inputStubs.TryGetValue(key, out SpriteRenderer stub) || stub == null)
                {
                    _flashing.Remove(key);   // the port was rebuilt or removed mid-flash
                    continue;
                }

                // Resolved against the port rather than a fixed colour: a matching collision leaves
                // the port still holding its value, so fading back to the empty colour would blank
                // a socket that is not empty.
                InputPort port = PortAt(key);

                if (remaining <= 0f)
                {
                    _flashing.Remove(key);

                    // Handed straight back to the resting look, so the port is never left for a
                    // frame showing the end of a flash that is over.
                    if (port != null)
                        Paint(stub, port, key);

                    continue;
                }

                _flashing[key] = remaining;

                float t = remaining / _flashSeconds;
                stub.color = Color.Lerp(RestingColourOf(port), _collisionColour, t);
                stub.transform.localScale =
                    Vector3.one * PortGeometry.StubSize * Mathf.Lerp(1f, _flashScale, t);
            }
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Rebuild()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] == null)
                    continue;

                _spawned[i].SetActive(false);
                Destroy(_spawned[i]);
            }

            _spawned.Clear();
            _inputStubs.Clear();
            _inputGlows.Clear();
            _flashing.Clear();   // stub references are about to be replaced
            _collisions.Clear();
            _doomed.Clear();

            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);
                if (node == null)
                    continue;   // retired id

                Vector2 centre = _runner.PositionOf(id);

                for (int i = 0; i < node.InputCount; i++)
                    Spawn(centre, id, true, i, node.InputCount);

                for (int i = 0; i < node.OutputCount; i++)
                    Spawn(centre, id, false, i, node.OutputCount);
            }
        }

        private void Spawn(Vector2 centre, int nodeId, bool isInput, int index, int count)
        {
            string label = $"{(isInput ? "In" : "Out")} {nodeId}.{index}";

            GameObject stub = ViewSprites.Spawn(_stubPrefab, _container, label);
            stub.transform.position = PortGeometry.PositionOf(centre, isInput, index, count);
            stub.transform.localScale = Vector3.one * PortGeometry.StubSize;

            var renderer = stub.GetComponent<SpriteRenderer>();

            // Inputs start hollow and are painted properly on the first frame; outputs never
            // change, so they are finished here.
            renderer.sprite = isInput ? ProceduralSprites.Ring() : ProceduralSprites.Dot();
            renderer.color = isInput ? _inputColour : _outputColour;
            // Above the scorch mark, which is drawn at 2 and is four times the width of a stub. A
            // port that has collided before is exactly the one whose state is worth reading, so the
            // burn must not bury it.
            renderer.sortingOrder = 3;

            if (isInput)
            {
                var key = new PortAddress(nodeId, true, index);
                _inputStubs[key] = renderer;
                _inputGlows[key] = SpawnGlow(stub.transform);
            }

            _spawned.Add(stub);
        }

        /// <summary>
        /// The halo behind one input socket. A child, so it follows the stub's swell when a
        /// collision is coming and needs no position of its own.
        /// </summary>
        private SpriteRenderer SpawnGlow(Transform stub)
        {
            var host = new GameObject("Socket glow");
            host.transform.SetParent(stub, false);
            host.transform.localScale = Vector3.one * _heldGlowScale;

            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.Glow();
            renderer.sortingOrder = 1;
            renderer.color = Color.clear;   // lit only once something is being held

            return renderer;
        }
    }
}
