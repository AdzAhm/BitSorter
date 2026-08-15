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

        private readonly List<GameObject> _spawned = new List<GameObject>();
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

            if (_runner.GraphRevision == _builtRevision)
                return;

            Rebuild();
            _builtRevision = _runner.GraphRevision;
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
            renderer.color = isInput ? _inputColour : _outputColour;
            renderer.sortingOrder = 1;   // above the node square, below bits

            _spawned.Add(stub);
        }
    }
}
