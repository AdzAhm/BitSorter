using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws one wire per edge, from its source node's position to its target's, with its delay shown
    /// both as a number and as division marks across the wire.
    /// </summary>
    /// <remarks>
    /// The marks are why this is more than a line. A wire carrying four ticks gets three cross-hatches,
    /// dividing it into four, so two paths of unequal total delay are visible without reading digits --
    /// which is the whole lesson of the balancing levels. A delay-1 wire gets none, so the default look
    /// is unchanged and bare means "nothing added".
    ///
    /// Cross-hatches rather than dots along the wire on purpose. Round pips would read as bits in
    /// transit, and where the bits are is the one thing on screen that must stay unambiguous.
    ///
    /// Two passes per frame, unlike the other renderers: <see cref="Rebuild"/> only when the graph
    /// changes shape, then <see cref="ApplyHighlight"/> every frame, because what the cursor is over
    /// changes without the graph changing at all.
    /// </remarks>
    public sealed class EdgeRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private WireDelayController _delay;
        [SerializeField] private SparkEffects _sparks;
        [SerializeField] private float _casingWidth = 0.17f;
        [SerializeField] private Color _casingColour = new Color(0.10f, 0.13f, 0.17f);
        [SerializeField] private float _coreWidth = 0.065f;
        [SerializeField] private Color _coreColour = new Color(0.30f, 0.62f, 0.70f);
        [SerializeField] private Color _hoverColour = new Color(0.62f, 0.92f, 1.00f);
        [SerializeField] private Color _flashColour = new Color(1.00f, 0.95f, 0.70f);
        [SerializeField] private Color _markColour = new Color(0.58f, 0.80f, 0.88f);
        [SerializeField] private float _markLength = 0.20f;
        [SerializeField] private float _markWidth = 0.055f;
        [SerializeField] private Color _labelColour = new Color(0.94f, 0.96f, 1.00f);
        [SerializeField] private Color _labelOutlineColour = new Color(0.02f, 0.03f, 0.05f, 0.95f);
        [SerializeField] private Color _labelBackingColour = new Color(0.03f, 0.04f, 0.06f, 0.85f);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Per drawn edge, in step with each other. Index is not the edge id.</summary>
        private readonly List<int> _edgeIds = new List<int>();
        private readonly List<LineRenderer> _cores = new List<LineRenderer>();
        private readonly List<Vector2> _labelPositions = new List<Vector2>();
        private readonly List<string> _labelTexts = new List<string>();

        private Transform _container;
        private Material _material;
        private Camera _camera;
        private GUIStyle _labelStyle;
        private GUIStyle _labelHoverStyle;
        private GUIStyle _labelOutlineStyle;
        private int _builtRevision = -1;
        private int _sparkedCount;

        /// <summary>Offset of the number from the wire's centreline, so the marks have the middle.</summary>
        private const float LabelOffset = 0.34f;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_delay == null)
                _delay = FindFirstObjectByType<WireDelayController>();

            if (_sparks == null)
                _sparks = FindFirstObjectByType<SparkEffects>();

            _camera = Camera.main;

            _container = new GameObject("Edges").transform;
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

            // Unconditional: hover and the flash both change without the graph changing.
            ApplyHighlight();
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
            _edgeIds.Clear();
            _cores.Clear();
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
                LineRenderer core = Spawn($"Edge {id} core - {edge}", from, to, _coreWidth, _coreColour, -1);

                SpawnMarks(id, from, to, edge.Delay);

                _edgeIds.Add(id);
                _cores.Add(core);

                // Cached here rather than rebuilt in OnGUI, which runs more than once a frame. Nudged
                // off the centreline so the number and the division marks do not overlap.
                Vector2 midpoint = (from + to) * 0.5f;
                _labelPositions.Add(midpoint + Normal(from, to) * LabelOffset);
                _labelTexts.Add(edge.Delay.ToString());
            }
        }

        /// <summary>
        /// <paramref name="delay"/> minus one hatches, dividing the wire into that many equal parts.
        /// A delay-1 wire gets none.
        /// </summary>
        private void SpawnMarks(int edgeId, Vector2 from, Vector2 to, int delay)
        {
            if (delay < 2)
                return;

            Vector2 normal = Normal(from, to);

            for (int i = 1; i < delay; i++)
            {
                Vector2 centre = Vector2.Lerp(from, to, i / (float)delay);
                Vector2 half = normal * (_markLength * 0.5f);

                // Across the wire, not along it, so a hatch cannot be mistaken for a travelling bit.
                Spawn($"Edge {edgeId} mark {i}", centre - half, centre + half,
                    _markWidth, _markColour, -1);
            }
        }

        /// <summary>Unit vector perpendicular to the wire. Arbitrary but stable for a zero-length one.</summary>
        private static Vector2 Normal(Vector2 from, Vector2 to)
        {
            Vector2 along = to - from;

            return along.sqrMagnitude < 1e-6f
                ? Vector2.up
                : new Vector2(-along.y, along.x).normalized;
        }

        /// <summary>
        /// Recolours the hovered and just-changed wires. Runs every frame and touches only colours, so
        /// it never rebuilds geometry for a cursor move.
        /// </summary>
        private void ApplyHighlight()
        {
            int hovered = _delay != null ? _delay.HoveredEdgeId : -1;

            for (int i = 0; i < _cores.Count; i++)
            {
                LineRenderer core = _cores[i];
                if (core == null)
                    continue;

                int edgeId = _edgeIds[i];
                Color colour = _coreColour;

                if (edgeId == hovered)
                    colour = _hoverColour;

                // The flash wins over hover: it is the acknowledgement of an action the player just took.
                float flash = _delay != null ? _delay.FlashStrengthFor(edgeId) : 0f;
                if (flash > 0f)
                    colour = Color.Lerp(colour, _flashColour, flash);

                core.startColor = colour;
                core.endColor = colour;
            }

            FireChangeSpark();
        }

        /// <summary>
        /// One burst per change, at the wire that changed.
        /// </summary>
        /// <remarks>
        /// Keyed on the controller's change counter rather than on the edge id. Scrolling the same wire
        /// twice leaves the id identical, so an id-keyed guard would swallow every repeat -- which is
        /// most of them, since finding the right delay means scrolling one wire several times.
        /// </remarks>
        private void FireChangeSpark()
        {
            if (_sparks == null || _delay == null || _delay.ChangeCount == _sparkedCount)
                return;

            int changed = _delay.ChangedEdgeId;

            for (int i = 0; i < _edgeIds.Count; i++)
            {
                if (_edgeIds[i] != changed)
                    continue;

                _sparks.Burst(_labelPositions[i], _flashColour);
                break;
            }

            // Consumed either way. If the edge is somehow not drawn, retrying every frame would be
            // worse than missing one burst.
            _sparkedCount = _delay.ChangeCount;
        }

        private LineRenderer Spawn(
            string name, Vector2 from, Vector2 to, float width, Color colour, int sortingOrder)
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
            return line;
        }

        /// <summary>
        /// Each wire's delay as a small number beside it. IMGUI because the HUD already proves that
        /// path works here; a world-space TextMesh would need a builtin font whose name has changed
        /// between Unity versions.
        /// </summary>
        private void OnGUI()
        {
            if (_camera == null || _labelTexts.Count == 0)
                return;

            EnsureLabelStyles();

            int hovered = _delay != null ? _delay.HoveredEdgeId : -1;

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

                bool isHovered = i < _edgeIds.Count && _edgeIds[i] == hovered;
                GUI.Label(rect, text, isHovered ? _labelHoverStyle : _labelStyle);
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

            // Bigger as well as brighter: on a busy board colour alone is easy to miss.
            _labelHoverStyle = new GUIStyle(_labelStyle) { fontSize = 17 };
            _labelHoverStyle.normal.textColor = _hoverColour;

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
