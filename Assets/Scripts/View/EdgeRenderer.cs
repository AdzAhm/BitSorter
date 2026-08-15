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
        [SerializeField] private Color _labelColour = new Color(0.72f, 0.76f, 0.86f);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Vector2> _labelPositions = new List<Vector2>();
        private readonly List<string> _labelTexts = new List<string>();

        private Transform _container;
        private Material _material;
        private Camera _camera;
        private GUIStyle _labelStyle;
        private int _builtRevision = -1;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            _camera = Camera.main;

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
            _labelPositions.Clear();
            _labelTexts.Clear();

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

                // Stub to stub, from the same geometry the port renderer and hit tester use, so
                // the wire visibly lands on the ports it actually connects.
                Vector2 from = PortGeometry.EndpointOf(edge.Source, _runner.PositionOf(edge.Source.Owner.Id));
                Vector2 to = PortGeometry.EndpointOf(edge.Target, _runner.PositionOf(edge.Target.Owner.Id));

                line.SetPosition(0, from);
                line.SetPosition(1, to);

                // Cached here rather than rebuilt in OnGUI, which runs more than once a frame.
                _labelPositions.Add((from + to) * 0.5f);
                _labelTexts.Add(edge.Delay.ToString());

                _spawned.Add(wire);
            }
        }

        /// <summary>
        /// Each wire's delay as a small number at its midpoint. IMGUI because the HUD already
        /// proves that path works here; a world-space TextMesh would need a builtin font whose
        /// name has changed between Unity versions.
        /// </summary>
        private void OnGUI()
        {
            if (_camera == null || _labelTexts.Count == 0)
                return;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                _labelStyle.normal.textColor = _labelColour;
            }

            for (int i = 0; i < _labelTexts.Count; i++)
            {
                Vector3 screen = _camera.WorldToScreenPoint(_labelPositions[i]);
                if (screen.z < 0f)
                    continue;

                // GUI space counts down from the top, the camera counts up from the bottom.
                var rect = new Rect(screen.x - 14f, Screen.height - screen.y - 10f, 28f, 20f);
                GUI.Label(rect, _labelTexts[i], _labelStyle);
            }
        }

        private static Material WireMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }
    }
}
