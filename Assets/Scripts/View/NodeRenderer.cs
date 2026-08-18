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

        private readonly List<GameObject> _spawned = new List<GameObject>();
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

                // Deactivate as well as destroy: Destroy only takes effect at end of frame, and
                // the replacement is spawned before then.
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

                GameObject instance = ViewSprites.Spawn(_nodePrefab, _container, $"Node {id} - {node}");
                instance.transform.position = centre;
                // Shared with PortGeometry, which places the stubs on this shape's faces.
                instance.transform.localScale = Vector3.one * PortGeometry.NodeSize;

                var renderer = instance.GetComponent<SpriteRenderer>();
                renderer.sprite = NodeShapes.SpriteFor(node);
                renderer.color = colour;
                renderer.sortingOrder = 0;

                _spawned.Add(instance);

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
