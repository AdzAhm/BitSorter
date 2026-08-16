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
        [SerializeField] private float _casingWidth = 0.17f;
        [SerializeField] private Color _casingColour = new Color(0.10f, 0.13f, 0.17f);
        [SerializeField] private float _coreWidth = 0.065f;
        [SerializeField] private Color _coreColour = new Color(0.30f, 0.62f, 0.70f);
        [SerializeField] private Color _labelColour = new Color(0.94f, 0.96f, 1.00f);
        [SerializeField] private Color _labelOutlineColour = new Color(0.02f, 0.03f, 0.05f, 0.95f);
        [SerializeField] private Color _labelBackingColour = new Color(0.03f, 0.04f, 0.06f, 0.85f);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Vector2> _labelPositions = new List<Vector2>();
        private readonly List<string> _labelTexts = new List<string>();

        private Transform _container;
        private Material _material;
        private Camera _camera;
        private GUIStyle _labelStyle;
        private GUIStyle _labelOutlineStyle;
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

                // Stub to stub, from the same geometry the port renderer and hit tester use, so
                // the wire visibly lands on the ports it actually connects.
                Vector2 from = PortGeometry.EndpointOf(edge.Source, _runner.PositionOf(edge.Source.Owner.Id));
                Vector2 to = PortGeometry.EndpointOf(edge.Target, _runner.PositionOf(edge.Target.Owner.Id));

                // Two lines make a trace: a wide dark casing with a thin bright core over it.
                // Cheaper and more predictable than a custom shader.
                Spawn($"Edge {id} casing", from, to, _casingWidth, _casingColour, -2);
                Spawn($"Edge {id} core - {edge}", from, to, _coreWidth, _coreColour, -1);

                // Cached here rather than rebuilt in OnGUI, which runs more than once a frame.
                _labelPositions.Add((from + to) * 0.5f);
                _labelTexts.Add(edge.Delay.ToString());
            }
        }

        private void Spawn(string name, Vector2 from, Vector2 to, float width, Color colour, int sortingOrder)
        {
            var wire = new GameObject(name);
            wire.transform.SetParent(_container, false);

            var line = wire.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.numCapVertices = 6;
            line.material = _material;
            line.startColor = colour;
            line.endColor = colour;
            line.sortingOrder = sortingOrder;

            line.SetPosition(0, from);
            line.SetPosition(1, to);

            _spawned.Add(wire);
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

            EnsureLabelStyles();

            for (int i = 0; i < _labelTexts.Count; i++)
            {
                Vector3 screen = _camera.WorldToScreenPoint(_labelPositions[i]);
                if (screen.z < 0f)
                    continue;

                // GUI space counts down from the top, the camera counts up from the bottom.
                float x = screen.x - 14f;
                float y = Screen.height - screen.y - 10f;
                var rect = new Rect(x, y, 28f, 20f);

                // A soft dark pill behind the number. Bloom brightens whatever is under these
                // labels, and plain text on a glowing wire is unreadable.
                Color previous = GUI.color;
                GUI.color = _labelBackingColour;
                GUI.DrawTexture(new Rect(x + 3f, y + 1f, 22f, 18f), ProceduralSprites.Dot().texture);
                GUI.color = previous;

                // Then an outline, so the digit still reads if the pill lands on a bright spot.
                string text = _labelTexts[i];
                for (int o = 0; o < LabelOutlineOffsets.Length; o += 2)
                {
                    GUI.Label(
                        new Rect(x + LabelOutlineOffsets[o], y + LabelOutlineOffsets[o + 1], 28f, 20f),
                        text, _labelOutlineStyle);
                }

                GUI.Label(rect, text, _labelStyle);
            }
        }

        /// <summary>Offsets for a cheap four-way text outline, as x,y pairs.</summary>
        private static readonly float[] LabelOutlineOffsets = { -1f, 0f, 1f, 0f, 0f, -1f, 0f, 1f };

        private void EnsureLabelStyles()
        {
            if (_labelStyle != null)
                return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
            };
            _labelStyle.normal.textColor = _labelColour;

            _labelOutlineStyle = new GUIStyle(_labelStyle);
            _labelOutlineStyle.normal.textColor = _labelOutlineColour;
        }

        private static Material WireMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }
    }
}
