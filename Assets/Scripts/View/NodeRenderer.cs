using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Spawns one square per node at its mapped position, coloured by node type. Nodes never
    /// move, so this runs once and then does nothing.
    /// </summary>
    public sealed class NodeRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameObject _nodePrefab;
        [SerializeField] private float _nodeSize = 1.2f;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();
        }

        private void Start()
        {
            if (_runner == null || !_runner.IsReady)
            {
                Debug.LogError($"{nameof(NodeRenderer)} has no {nameof(SimulationRunner)}.", this);
                return;
            }

            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                GameObject instance = ViewSprites.Spawn(_nodePrefab, transform, $"Node {id} - {node}");
                instance.transform.position = _runner.PositionOf(id);
                instance.transform.localScale = Vector3.one * _nodeSize;

                var renderer = instance.GetComponent<SpriteRenderer>();
                renderer.color = ColourFor(node);
                renderer.sortingOrder = 0;
            }
        }

        private static Color ColourFor(Node node)
        {
            if (node is SourceNode) return new Color(0.30f, 0.72f, 0.42f);   // green
            if (node is SinkNode) return new Color(0.85f, 0.35f, 0.35f);     // red
            if (node is XorGate) return new Color(0.35f, 0.55f, 0.90f);      // blue
            if (node is AndGate) return new Color(0.92f, 0.70f, 0.25f);      // amber
            if (node is OrGate) return new Color(0.65f, 0.45f, 0.85f);       // violet
            if (node is NandGate) return new Color(0.45f, 0.80f, 0.78f);     // teal
            if (node is NorGate) return new Color(0.78f, 0.76f, 0.42f);      // olive
            if (node is NotGate) return new Color(0.85f, 0.50f, 0.70f);      // pink
            return new Color(0.55f, 0.55f, 0.60f);                           // pass-through, etc
        }
    }
}
