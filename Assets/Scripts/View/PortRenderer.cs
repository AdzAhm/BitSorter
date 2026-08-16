using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws a small stub for every port, so ports are individually visible and clickable.
    /// Positions come from <see cref="PortGeometry"/>, the same function the hit tester uses.
    /// </summary>
    /// <inheritdoc cref="NodeRenderer"/>
    public sealed class PortRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameObject _stubPrefab;
        [SerializeField] private Color _inputColour = new Color(0.62f, 0.66f, 0.76f);
        [SerializeField] private Color _outputColour = new Color(0.80f, 0.78f, 0.58f);

        [SerializeField] private Color _collisionColour = new Color(1.00f, 0.28f, 0.24f);
        [SerializeField] private float _flashSeconds = 0.35f;
        [SerializeField] private float _flashScale = 1.9f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Input stubs by node id and port index, so a collision can find its stub.</summary>
        private readonly Dictionary<PortAddress, SpriteRenderer> _inputStubs =
            new Dictionary<PortAddress, SpriteRenderer>();

        /// <summary>Seconds of flash still owed to a port, keyed the same way.</summary>
        private readonly Dictionary<PortAddress, float> _flashing = new Dictionary<PortAddress, float>();
        private readonly List<PortAddress> _active = new List<PortAddress>();

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

            DetectCollisions();
            AdvanceFlashes();
        }

        /// <summary>
        /// LastCorruptedTick names exactly which port lost bits and on which tick, so no state has
        /// to be diffed. CurrentTick is the tick about to run, so the one just executed is one less.
        /// </summary>
        private void DetectCollisions()
        {
            SimulationView view = _runner.View;
            int justExecuted = view.CurrentTick - 1;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);
                if (node == null)
                    continue;

                for (int i = 0; i < node.InputCount; i++)
                {
                    if (node.In(i).LastCorruptedTick != justExecuted)
                        continue;

                    _flashing[new PortAddress(id, true, i)] = _flashSeconds;
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

                if (remaining <= 0f)
                {
                    stub.color = _inputColour;
                    stub.transform.localScale = Vector3.one * PortGeometry.StubSize;
                    _flashing.Remove(key);
                    continue;
                }

                _flashing[key] = remaining;

                float t = remaining / _flashSeconds;
                stub.color = Color.Lerp(_inputColour, _collisionColour, t);
                stub.transform.localScale =
                    Vector3.one * PortGeometry.StubSize * Mathf.Lerp(1f, _flashScale, t);
            }
        }

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
            _flashing.Clear();   // stub references are about to be replaced

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
            renderer.sprite = ProceduralSprites.Dot();
            renderer.color = isInput ? _inputColour : _outputColour;
            renderer.sortingOrder = 1;   // above the node body, below bits

            if (isInput)
                _inputStubs[new PortAddress(nodeId, true, index)] = renderer;

            _spawned.Add(stub);
        }
    }
}
