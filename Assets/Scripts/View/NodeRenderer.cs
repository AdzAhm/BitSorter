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

        [Tooltip("How long a register's capture flash lasts. How far it swells is geometry, and " +
                 "lives in PortGeometry with the body it has to stay inside.")]
        [SerializeField] private float _captureSeconds = 0.28f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Body and glow renderers by node id, so the stall pass can find them.</summary>
        private readonly Dictionary<int, SpriteRenderer> _bodies = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _halos = new Dictionary<int, SpriteRenderer>();

        /// <summary>
        /// The colour a node has when nothing is wrong, kept because the stall pass overwrites it
        /// and <see cref="NodeShapes.ColourFor"/> would otherwise have to be asked every frame.
        /// </summary>
        private readonly Dictionary<int, Color> _baseColours = new Dictionary<int, Color>();

        /// <summary>The bit drawn inside each register, what it last showed, and its capture flash.</summary>
        private readonly Dictionary<int, SpriteRenderer> _heldBits = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, Bit> _heldValues = new Dictionary<int, Bit>();
        private readonly Dictionary<int, float> _capturing = new Dictionary<int, float>();

        /// <summary>
        /// The disc above each source showing what it sends next, the plate it sits on, and what it
        /// last showed.
        /// </summary>
        private readonly Dictionary<int, SpriteRenderer> _nextBits = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _nextPlates = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, Bit> _nextValues = new Dictionary<int, Bit>();

        /// <summary>A block as drawn: its box and glow and their resting colour, for the stall pass.</summary>
        private readonly struct DrawnBlock
        {
            public readonly BuiltBlock Block;
            public readonly SpriteRenderer Body;
            public readonly SpriteRenderer Halo;
            public readonly Color Colour;

            public DrawnBlock(BuiltBlock block, SpriteRenderer body, SpriteRenderer halo, Color colour)
            {
                Block = block;
                Body = body;
                Halo = halo;
                Colour = colour;
            }
        }

        private readonly List<DrawnBlock> _blocks = new List<DrawnBlock>();

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
            ApplyHeldBits();
            ApplyNextBits();
        }

        /// <summary>
        /// Draws, above each source, the bit it will send next, and hides it once there is none.
        /// </summary>
        /// <remarks>
        /// A playtester asked to see what a source was about to send (2026-09-28): a stream is a row
        /// of bits in a file, and on the board the only way to learn it was to run it and watch.
        /// Drawn as the register's held bit is -- the same disc, digit and colour, on the same kind of
        /// solid plate in the register's colour -- because both are a value sitting on a part rather
        /// than one travelling. The plate is not decoration: the first version drew the disc straight
        /// onto the board, where an indigo 0 is about 2:1 against the ground and was hard to find.
        /// While the board is being built it shows the first bit, and as a run goes it steps along
        /// the stream.
        ///
        /// Redrawn only when the value changes, the idiom every renderer here uses; the lookup it
        /// makes each frame walks at most the silent ticks of one clock period.
        /// </remarks>
        private void ApplyNextBits()
        {
            SimulationView view = _runner.View;

            foreach (KeyValuePair<int, SpriteRenderer> pair in _nextBits)
            {
                SpriteRenderer disc = pair.Value;

                if (disc == null || !(view.GetNode(pair.Key) is SourceNode source))
                    continue;

                Bit? next = source.NextBit;

                if (disc.enabled != next.HasValue)
                {
                    disc.enabled = next.HasValue;

                    if (_nextPlates.TryGetValue(pair.Key, out SpriteRenderer plate) && plate != null)
                        plate.enabled = next.HasValue;
                }

                if (!next.HasValue)
                    continue;

                if (_nextValues.TryGetValue(pair.Key, out Bit shown) && shown == next.Value)
                    continue;

                _nextValues[pair.Key] = next.Value;
                disc.sprite = Look.Current.Bits == BitStyle.Digit
                    ? ProceduralSprites.HeldBit(next.Value)
                    : ProceduralSprites.Circle();
                disc.color = BitVisuals.ColourFor(next.Value);
            }
        }

        /// <summary>
        /// The disc above a source, showing the bit it will send next, on its plate.
        /// </summary>
        /// <remarks>
        /// The plate is on the body's layer and the disc on the detail's, as a register's plate and
        /// its held bit are, so the disc is always the one drawn on top.
        /// </remarks>
        private void SpawnNextBit(int id, Vector2 centre)
        {
            Vector2 at = PortGeometry.NextBitPositionOf(centre);

            GameObject plate = ViewSprites.Spawn(_nodePrefab, _container, $"Next plate {id}");
            plate.transform.position = at;
            plate.transform.localScale =
                Vector3.one * PortGeometry.ScaleForRadius(PortGeometry.NextBitPlateRadius);

            var plateRenderer = plate.GetComponent<SpriteRenderer>();
            plateRenderer.sprite = ProceduralSprites.Circle();
            plateRenderer.color = Palette.Current.Register;
            plateRenderer.sortingOrder = ViewLayers.NodeBody;

            _spawned.Add(plate);
            _nextPlates[id] = plateRenderer;

            GameObject next = ViewSprites.Spawn(_nodePrefab, _container, $"Next {id}");
            next.transform.position = at;
            next.transform.localScale =
                Vector3.one * PortGeometry.ScaleForRadius(PortGeometry.NextBitRadius);

            var renderer = next.GetComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.Circle();
            renderer.sortingOrder = ViewLayers.NodeDetail;

            _spawned.Add(next);
            _nextBits[id] = renderer;
        }

        /// <summary>The source's next bit as the board draws it, or null if none is drawn: for the tests.</summary>
        public Bit? ShownNextBit(int nodeId) =>
            _nextBits.TryGetValue(nodeId, out SpriteRenderer disc) && disc != null && disc.enabled
                && _nextValues.TryGetValue(nodeId, out Bit value)
                ? value
                : (Bit?)null;

        /// <summary>Whether the plate under a source's next bit is drawn: for the tests.</summary>
        public bool ShowsNextBitPlate(int nodeId) =>
            _nextPlates.TryGetValue(nodeId, out SpriteRenderer plate) && plate != null && plate.enabled;

        /// <summary>
        /// Draws the bit each register is holding, and flashes it when that bit changes.
        /// </summary>
        /// <remarks>
        /// A register's whole point is the value inside it, and a run is watched rather than read:
        /// the state of a machine has to be visible on the board while it works, not worked out
        /// afterwards from what reached the bins.
        ///
        /// Polled against a cached copy, the same idiom every other renderer here uses. The flash
        /// is a swell rather than a brightening. That was first argued from bloom, which no longer
        /// reaches a held bit -- it is drawn in plain colour -- but it stands: a plain colour made
        /// brighter can only move towards white, which erases which value it was, and a change of
        /// size keeps the colour and the digit and does not depend on colour at all.
        /// </remarks>
        private void ApplyHeldBits()
        {
            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                if (!_heldBits.TryGetValue(id, out SpriteRenderer disc) || disc == null)
                    continue;

                if (!(view.GetNode(id) is RegisterNode register))
                    continue;

                bool seen = _heldValues.TryGetValue(id, out Bit shown);

                if (!seen || shown != register.State)
                {
                    _heldValues[id] = register.State;

                    // Only a change swells. The first sight of a register after a rebuild is not
                    // one: every edit rebuilds the board, and swelling then made every register on
                    // it look as though it had just captured something.
                    if (seen)
                        _capturing[id] = _captureSeconds;

                    // The state of a machine, so it says its value by its shape as a bit in
                    // flight does -- not by its colour alone.
                    if (Look.Current.Bits == BitStyle.Digit)
                        disc.sprite = ProceduralSprites.HeldBit(register.State);
                }

                float left = _capturing.TryGetValue(id, out float remaining) ? remaining : 0f;

                if (left > 0f)
                    _capturing[id] = Mathf.Max(0f, left - Time.deltaTime);

                float swell = _captureSeconds <= 0f ? 1f
                    : Mathf.Lerp(1f, PortGeometry.HeldBitSwell, left / _captureSeconds);

                disc.color = BitVisuals.ColourFor(register.State);
                disc.transform.localScale = Vector3.one *
                    PortGeometry.ScaleForRadius(PortGeometry.HeldBitRadius * swell);
            }
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
        /// attempt raised the glow to an urgent amber, and the gate became a single bright blob with
        /// its ports somewhere inside it -- inverting the hierarchy, since the sockets are what
        /// actually say what is being held. Dimming instead lets the held bit stand out against the
        /// gate, which is both legible and true: the gate really has gone dormant, and the bit
        /// really is the only thing happening on it.
        ///
        /// The blob was blamed on bloom, which no longer reaches a gate at all. Rendered again with
        /// bloom off, the brighter version was the same blob: the halo and the body are the largest
        /// things on a gate and the held bit the smallest, and that is the reason that holds.
        ///
        /// The amber is left as a slow low breath, enough to separate "waiting" from "idle"
        /// without competing with anything.
        /// </remarks>
        private void ApplyStallStates()
        {
            SimulationView view = _runner.View;
            float breath = PortState.Pulse(ViewTime.Now, _stallPulseHz);

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
                    : new Color(baseColour.r, baseColour.g, baseColour.b, GlowAlpha(node));
            }

            // A block breathes when anything inside it is waiting: its gates are out of sight, and
            // the box is what stands for them.
            for (int b = 0; b < _blocks.Count; b++)
            {
                DrawnBlock drawn = _blocks[b];
                bool stalled = false;

                for (int g = 0; g < drawn.Block.Gates.Count && !stalled; g++)
                {
                    Node inside = view.GetNode(drawn.Block.Gates[g]);
                    stalled = inside != null && PortState.IsStalled(inside);
                }

                Color colour = drawn.Colour;

                if (drawn.Body != null)
                    drawn.Body.color = stalled ? Dormant(colour) : colour;

                if (drawn.Halo != null)
                {
                    drawn.Halo.color = stalled
                        ? new Color(PortState.Waiting.r, PortState.Waiting.g, PortState.Waiting.b,
                            Mathf.Lerp(_stallGlowMin, _stallGlowMax, breath))
                        : new Color(colour.r, colour.g, colour.b, BlockGlowAlpha);
                }
            }
        }

        /// <summary>How strongly a block's halo glows at rest: as a gate's does.</summary>
        private float BlockGlowAlpha => _glowAlpha * Look.Current.GateGlow;

        /// <summary>How strongly this node's halo glows at rest, in the current look.</summary>
        private float GlowAlpha(Node node) =>
            _glowAlpha * (node is SourceNode || node is SinkNode ? Look.Current.FixtureGlow : Look.Current.GateGlow);

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
            _heldBits.Clear();
            _heldValues.Clear();
            _capturing.Clear();
            _nextBits.Clear();
            _nextPlates.Clear();
            _nextValues.Clear();
            _blocks.Clear();

            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                // A retired id, or one of a block's nodes, which the block's box stands for.
                if (node == null || !_runner.IsShownNode(id))
                    continue;

                Vector2 centre = _runner.PositionOf(id);
                Color colour = NodeShapes.ColourFor(node);

                // Glow first so it sits behind the body.
                GameObject halo = ViewSprites.Spawn(_nodePrefab, _container, $"Glow {id}");
                halo.transform.position = centre;
                halo.transform.localScale = Vector3.one * PortGeometry.NodeSize * _glowScale;

                var haloRenderer = halo.GetComponent<SpriteRenderer>();
                haloRenderer.sprite = ProceduralSprites.Glow();
                haloRenderer.color = new Color(colour.r, colour.g, colour.b, GlowAlpha(node));
                haloRenderer.sortingOrder = ViewLayers.NodeGlow;

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
                renderer.sortingOrder = ViewLayers.NodeBody;

                _spawned.Add(instance);
                _bodies[id] = renderer;

                if (node is RegisterNode)
                    SpawnHeldBit(id, centre, colour);

                if (node is SourceNode)
                    SpawnNextBit(id, centre);

                SpawnLabel(node, centre, colour);
            }

            BuiltCircuit circuit = _runner.Circuit;

            for (int b = 0; b < circuit.Blocks.Count; b++)
                SpawnBlock(circuit.Blocks[b]);
        }

        /// <summary>
        /// A block's box over every cell it covers, its glow behind it, its name above it, and the name
        /// of each port inside it beside the port.
        /// </summary>
        /// <remarks>
        /// One box for the whole block: the gates inside are not drawn, and neither are the anchors
        /// that stand on its faces -- their sockets are, by the port renderer, where the box's ports
        /// are. Its glow is a gate's, stretched to its height.
        /// </remarks>
        private void SpawnBlock(BuiltBlock block)
        {
            Vector2 centre = _runner.BlockCentre(block);
            float height = _runner.BlockHeight(block);
            Color colour = NodeShapes.BlockColour();

            GameObject halo = ViewSprites.Spawn(_nodePrefab, _container, $"Glow {block}");
            halo.transform.position = centre;
            halo.transform.localScale = new Vector3(PortGeometry.NodeSize * _glowScale, height * _glowScale, 1f);

            var haloRenderer = halo.GetComponent<SpriteRenderer>();
            haloRenderer.sprite = ProceduralSprites.Glow();
            haloRenderer.color = new Color(colour.r, colour.g, colour.b, BlockGlowAlpha);
            haloRenderer.sortingOrder = ViewLayers.NodeGlow;
            _spawned.Add(halo);

            GameObject box = ViewSprites.Spawn(_nodePrefab, _container, $"Block {block}");
            box.transform.position = centre;

            // Sliced, so the corners and the rim keep a gate's size however tall the box is.
            var body = box.GetComponent<SpriteRenderer>();
            body.sprite = NodeShapes.BlockSprite();
            body.drawMode = SpriteDrawMode.Sliced;
            body.size = PortGeometry.BlockBodySize(height);
            body.color = colour;
            body.sortingOrder = ViewLayers.NodeBody;

            // After the draw mode, not before: switching a renderer to sliced rewrites its transform's
            // scale to keep the size the sprite had, which drew every box 1.2 times too large.
            box.transform.localScale = Vector3.one;
            _spawned.Add(box);

            _blocks.Add(new DrawnBlock(block, body, haloRenderer, colour));

            // Above the box, as far from it as a fixture's name is below its own.
            Vector2 nameAt = new Vector2(centre.x, centre.y + height * 0.5f + LabelDrop - PortGeometry.NodeSize * 0.5f);
            SpawnText($"Label {block.Definition.Name}", block.Definition.Name.ToUpperInvariant(), nameAt,
                LabelFontSize, colour, TMPro.TextAlignmentOptions.Center, PortGeometry.NodeSize * 2.4f);

            Color portColour = NodeShapes.BlockLabelColour();

            for (int i = 0; i < block.Definition.Inputs.Count; i++)
                SpawnPortLabel(block.Definition.Inputs[i].Id, _runner.BlockPortPosition(block, true, i), true, portColour);

            for (int j = 0; j < block.Definition.Outputs.Count; j++)
                SpawnPortLabel(block.Definition.Outputs[j].Id, _runner.BlockPortPosition(block, false, j), false, portColour);
        }

        /// <summary>Size of a fixture's name, and of a block's.</summary>
        private const float LabelFontSize = 3.2f;

        /// <summary>Size of a block's port names: smaller than its own name, inside a box a node wide.</summary>
        private const float PortLabelFontSize = 2.6f;

        /// <summary>How far in from the box's face a port's name starts, clear of the socket on it.</summary>
        private const float PortLabelInset = PortGeometry.StubSize * 0.5f + 0.06f;

        /// <summary>The smallest a port's name shrinks to, to keep clear of the name across the box.</summary>
        private const float PortLabelMinFontSize = 1.6f;

        /// <summary>
        /// A port's name, inside the box beside the port: left-aligned on the left face, right on the
        /// right.
        /// </summary>
        /// <remarks>
        /// Each takes the half of the box on its own side, less a gap, and shrinks to fit there. An
        /// input and an output often share a row -- a full adder's cin and cout -- and at the size a
        /// short name is drawn the two long ones ran into each other.
        /// </remarks>
        private void SpawnPortLabel(string id, Vector2 port, bool isInput, Color colour)
        {
            float width = PortGeometry.BlockWidth * 0.5f - PortLabelInset - PortLabelGap * 0.5f;
            float x = isInput ? port.x + PortLabelInset + width * 0.5f : port.x - PortLabelInset - width * 0.5f;

            TMPro.TextMeshPro text = SpawnText($"Port label {id}", id, new Vector2(x, port.y), PortLabelFontSize, colour,
                isInput ? TMPro.TextAlignmentOptions.Left : TMPro.TextAlignmentOptions.Right, width);

            text.enableAutoSizing = true;
            text.fontSizeMin = PortLabelMinFontSize;
            text.fontSizeMax = PortLabelFontSize;
        }

        /// <summary>The space kept between an input's name and an output's on one row.</summary>
        private const float PortLabelGap = 0.06f;

        private TMPro.TextMeshPro SpawnText(
            string name, string content, Vector2 at, float size, Color colour, TMPro.TextAlignmentOptions alignment,
            float width)
        {
            var host = new GameObject(name);
            host.transform.SetParent(_container, false);
            host.transform.position = at;

            var text = host.AddComponent<TMPro.TextMeshPro>();
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            text.color = colour;
            text.sortingOrder = ViewLayers.NodeDetail;

            host.GetComponent<RectTransform>().sizeDelta = new Vector2(width, LabelHeight);
            _spawned.Add(host);
            return text;
        }

        /// <summary>
        /// How far towards the camera a register's plate is drawn from its body, in world units.
        /// </summary>
        /// <remarks>
        /// The plate is on the body's layer and lies over the body, and Unity breaks a tie in sorting
        /// order by depth and leaves a tie in depth to chance. Towards the camera is in front. The
        /// camera is orthographic, so this changes nothing about where or how large it is drawn.
        /// </remarks>
        private const float PlateLift = 0.001f;

        /// <summary>The disc inside a register, showing the bit it is holding, on its plate.</summary>
        /// <remarks>
        /// Above the body and below the bits in transit, like a fixture's label: a bit arriving at
        /// the register must not disappear behind the one already in it.
        ///
        /// The plate is the body's own colour, solid, and is what the bit is seen against. Neon
        /// Board draws the body as glass, see-through in the middle where the bit sits, and a 0 was
        /// about 2.5:1 against what showed through it (measured 2026-09-28). It does not swell with a
        /// capture -- the bit swells over it -- so a capture is still a swell and not a brightening.
        /// Where the body is already solid, as in Classic, the plate is the same colour on the same
        /// colour.
        /// </remarks>
        private void SpawnHeldBit(int id, Vector2 centre, Color colour)
        {
            Vector2 at = PortGeometry.HeldBitPositionOf(centre);

            GameObject plate = ViewSprites.Spawn(_nodePrefab, _container, $"Held plate {id}");
            plate.transform.position = new Vector3(at.x, at.y, -PlateLift);
            plate.transform.localScale =
                Vector3.one * PortGeometry.ScaleForRadius(PortGeometry.HeldBitPlateRadius);

            var plateRenderer = plate.GetComponent<SpriteRenderer>();
            plateRenderer.sprite = ProceduralSprites.Circle();
            plateRenderer.color = colour;
            plateRenderer.sortingOrder = ViewLayers.NodeBody;

            _spawned.Add(plate);

            GameObject held = ViewSprites.Spawn(_nodePrefab, _container, $"Held {id}");
            held.transform.position = at;
            held.transform.localScale =
                Vector3.one * PortGeometry.ScaleForRadius(PortGeometry.HeldBitRadius);

            var renderer = held.GetComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.Circle();
            renderer.sortingOrder = ViewLayers.NodeDetail;

            _spawned.Add(held);
            _heldBits[id] = renderer;
        }

        /// <summary>How far below its fixture's centre a fixture's name sits.</summary>
        public const float LabelDrop = PortGeometry.NodeSize * 0.78f;

        /// <summary>How tall a fixture's name is, in world units.</summary>
        public const float LabelHeight = 0.6f;

        /// <summary>
        /// How far below its fixture's centre the bottom of a fixture's name reaches, which the
        /// camera frames a tall board by.
        /// </summary>
        public const float LabelReach = LabelDrop + LabelHeight * 0.5f;

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
            host.transform.position = centre + new Vector2(0f, -LabelDrop);

            var text = host.AddComponent<TMPro.TextMeshPro>();
            text.text = node.Name.ToUpperInvariant();
            text.fontSize = 3.2f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = colour;

            // Sits above the board and the wires but below the bits, so a bit landing in a bin is
            // never hidden behind the bin's own name.
            text.sortingOrder = ViewLayers.NodeDetail;

            var rect = host.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PortGeometry.NodeSize * 2.4f, LabelHeight);

            _spawned.Add(host);
        }
    }
}
