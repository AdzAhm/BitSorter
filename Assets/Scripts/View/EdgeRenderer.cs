using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws one LineRenderer per edge, from its source node's position to its target's, and
    /// rebuilds whenever the graph's shape changes.
    /// </summary>
    /// <inheritdoc cref="NodeRenderer"/>
    public sealed class EdgeRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private float _wireWidth = 0.09f;
        [SerializeField] private Color _wireColour = new Color(0.32f, 0.34f, 0.40f);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Transform _container;
        private Material _material;
        private int _builtRevision = -1;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            _container = new GameObject("Edges").transform;
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

            if (_material == null)
                _material = WireMaterial();

            SimulationView view = _runner.View;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);
                if (edge == null)
                    continue;   // retired id

                var wire = new GameObject($"Edge {id} - {edge}");
                wire.transform.SetParent(_container, false);

                var line = wire.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = _wireWidth;
                line.numCapVertices = 4;
                line.material = _material;
                line.startColor = _wireColour;
                line.endColor = _wireColour;
                line.sortingOrder = -1;   // behind nodes and bits, in front of the grid

                // A node id is the only thing LogicCore and the layout agree on.
                line.SetPosition(0, _runner.PositionOf(edge.Source.Owner.Id));
                line.SetPosition(1, _runner.PositionOf(edge.Target.Owner.Id));

                _spawned.Add(wire);
            }
        }

        private static Material WireMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }
    }
}
