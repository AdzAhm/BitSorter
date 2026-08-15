using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws one LineRenderer per edge, from its source node's position to its target's. Edges
    /// never move, so this runs once.
    /// </summary>
    public sealed class EdgeRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private float _wireWidth = 0.09f;
        [SerializeField] private Color _wireColour = new Color(0.32f, 0.34f, 0.40f);

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();
        }

        private void Start()
        {
            if (_runner == null || !_runner.IsReady)
            {
                Debug.LogError($"{nameof(EdgeRenderer)} has no {nameof(SimulationRunner)}.", this);
                return;
            }

            Material material = WireMaterial();
            SimulationView view = _runner.View;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);

                var wire = new GameObject($"Edge {id} - {edge}");
                wire.transform.SetParent(transform, false);

                var line = wire.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = _wireWidth;
                line.numCapVertices = 4;
                line.material = material;
                line.startColor = _wireColour;
                line.endColor = _wireColour;
                line.sortingOrder = -1;   // behind nodes and bits

                // A node id is the only thing LogicCore and the layout agree on.
                line.SetPosition(0, _runner.PositionOf(edge.Source.Owner.Id));
                line.SetPosition(1, _runner.PositionOf(edge.Target.Owner.Id));
            }
        }

        private static Material WireMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }
    }
}
