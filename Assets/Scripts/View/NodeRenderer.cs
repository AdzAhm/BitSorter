using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Spawns one square per node at its mapped position, coloured by node type, and rebuilds
    /// whenever the graph's shape changes.
    /// </summary>
    /// <remarks>
    /// Tracks the objects it spawned rather than clearing its children. All three renderers hang
    /// off the same GameObject, so tearing down by child would delete the other two's work.
    /// </remarks>
    public sealed class NodeRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameObject _nodePrefab;
        [SerializeField] private float _glowScale = 2.1f;
        [SerializeField] private float _glowAlpha = 0.42f;

        [Tooltip("How far a stalled gate's body fades towards grey.")]
        [SerializeField] private float _stallFade = 0.55f;

        [Tooltip("Deliberately slow. A stalled gate breathes; a doomed port throbs.")]
        [SerializeField] private float _stallPulseHz = 1.1f;

        [Tooltip("Range the stalled glow breathes between. Both below the lit glow, on purpose.")]
        [SerializeField] private float _stallGlowMin = 0.10f;
        [SerializeField] private float _stallGlowMax = 0.30f;

        [Tooltip("How far a stalled gate's body dims, on top of losing its colour.")]
        [SerializeField] private float _stallDim = 0.62f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Body and glow renderers by node id, so the stall pass can find them.</summary>
        private readonly Dictionary<int, SpriteRenderer> _bodies = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _halos = new Dictionary<int, SpriteRenderer>();

        /// <summary>
        /// The colour a node has when nothing is wrong, kept because the stall pass overwrites it
        /// and <see cref="NodeShapes.ColourFor"/> would otherwise have to be asked every frame.
        /// </summary>
        private readonly Dictionary<int, Color> _baseColours = new Dictionary<int, Color>();

        private Transform _container;
        private int _builtRevision = -1;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            _container = new GameObject("Nodes").transform;
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

            ApplyStallStates();
        }

        /// <summary>
        /// Marks every gate that is holding bits it cannot act on.
        /// </summary>
        /// <remarks>
        /// The glow carries this rather than a second sprite. It was static and coloured by node
        /// type, but type is already told by the silhouette -- which CLAUDE.md names as the cue
        /// bloom is chosen to preserve -- so the glow was free to mean something that changes.
        ///
        /// A stalled gate goes **darker**, not brighter, and this is the whole trick. The first
        /// attempt raised the glow to an urgent amber, and under bloom the gate blew out into a
        /// single bright blob with its ports somewhere inside it -- inverting the hierarchy, since
        /// the sockets are what actually say what is being held. Dimming instead lets the held bit
        /// become the brightest thing on the gate, which is both legible and true: the gate really
        /// has gone dormant, and the bit really is the only thing happening on it.
        ///
        /// The amber is left as a slow low breath, enough to separate "waiting" from "idle"
        /// without competing with anything.
        /// </remarks>
        private void ApplyStallStates()
        {
            SimulationView view = _runner.View;
            float breath = PortState.Pulse(Time.time, _stallPulseHz);

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                if (node == null)
                    continue;   // retired id

                if (!_baseColours.TryGetValue(id, out Color baseColour))
                    continue;

                bool stalled = PortState.IsStalled(node);

                if (_bodies.TryGetValue(id, out SpriteRenderer body) && body != null)
                    body.color = stalled ? Dormant(baseColour) : baseColour;

                if (!_halos.TryGetValue(id, out SpriteRenderer halo) || halo == null)
                    continue;

                halo.color = stalled
                    ? new Color(PortState.Waiting.r, PortState.Waiting.g, PortState.Waiting.b,
                        Mathf.Lerp(_stallGlowMin, _stallGlowMax, breath))
                    : new Color(baseColour.r, baseColour.g, baseColour.b, _glowAlpha);
            }
        }

        /// <summary>
        /// What a gate looks like while it can do nothing: drained of its colour, then darkened.
        /// </summary>
        private Color Dormant(Color colour)
        {
            float grey = colour.grayscale;
            Color drained = Color.Lerp(colour, new Color(grey, grey, grey, colour.a), _stallFade);

            return new Color(drained.r * _stallDim, drained.g * _stallDim, drained.b * _stallDim,
                colour.a);
        }

        private void Rebuild()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] == null)
                    continue;

                // Deactivate as well as destroy: Destroy only takes effect at end of frame, and
                // the replacement is spawned before then.
                _spawned[i].SetActive(false);
                Destroy(_spawned[i]);
            }

            _spawned.Clear();

            // The renderers these point at are about to be destroyed.
            _bodies.Clear();
            _halos.Clear();
            _baseColours.Clear();

            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);
                if (node == null)
                    continue;   // retired id

                Vector2 centre = _runner.PositionOf(id);
                Color colour = NodeShapes.ColourFor(node);

                // Glow first so it sits behind the body.
                GameObject halo = ViewSprites.Spawn(_nodePrefab, _container, $"Glow {id}");
                halo.transform.position = centre;
                halo.transform.localScale = Vector3.one * PortGeometry.NodeSize * _glowScale;

                var haloRenderer = halo.GetComponent<SpriteRenderer>();
                haloRenderer.sprite = ProceduralSprites.Glow();
                haloRenderer.color = new Color(colour.r, colour.g, colour.b, _glowAlpha);
                haloRenderer.sortingOrder = -3;

                _spawned.Add(halo);
                _halos[id] = haloRenderer;
                _baseColours[id] = colour;

                GameObject instance = ViewSprites.Spawn(_nodePrefab, _container, $"Node {id} - {node}");
                instance.transform.position = centre;
                // Shared with PortGeometry, which places the stubs on this shape's faces.
                instance.transform.localScale = Vector3.one * PortGeometry.NodeSize;

                var renderer = instance.GetComponent<SpriteRenderer>();
                renderer.sprite = NodeShapes.SpriteFor(node);
                renderer.color = colour;
                renderer.sortingOrder = 0;

                _spawned.Add(instance);
                _bodies[id] = renderer;

                SpawnLabel(node, centre, colour);
            }
        }

        /// <summary>
        /// Writes a fixture's name under it, so "A", "s" or "SUM" in a level's goal names something
        /// the player can actually point at.
        /// </summary>
        /// <remarks>
        /// Sources and sinks only. Every goal in the game refers to them by name -- "make the bin
        /// receive A when s is 0" is unreadable on a board of unlabelled shapes -- whereas a gate's
        /// silhouette already says what it is, and stamping "AND" across it would compete with the
        /// one cue <see cref="NodeShapes"/> deliberately relies on.
        ///
        /// The text comes from <see cref="Node.Name"/>, which the circuit builder already sets from
        /// the fixture id, so the label and the goal are quoting the same string. There is no second
        /// place for a name to be written down and drift.
        ///
        /// Placed below the node rather than on it: a label over the body would be washed out by the
        /// glow exactly when the node is most active.
        /// </remarks>
        private void SpawnLabel(Node node, Vector2 centre, Color colour)
        {
            bool isFixture = node is SourceNode || node is SinkNode;

            if (!isFixture || string.IsNullOrEmpty(node.Name))
                return;

            var host = new GameObject($"Label {node.Name}");
            host.transform.SetParent(_container, false);
            host.transform.position = centre + new Vector2(0f, -PortGeometry.NodeSize * 0.78f);

            var text = host.AddComponent<TMPro.TextMeshPro>();
            text.text = node.Name.ToUpperInvariant();
            text.fontSize = 3.2f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = colour;

            // Sits above the board and the wires but below the bits, so a bit landing in a bin is
            // never hidden behind the bin's own name.
            text.sortingOrder = 1;

            var rect = host.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PortGeometry.NodeSize * 2.4f, 0.6f);

            _spawned.Add(host);
        }
    }
}
