using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Draws one wire per edge, from its source node's position to its target's, with its delay shown
    /// both as a number and as buffer triangles along the wire.
    /// </summary>
    /// <remarks>
    /// The marks are why this is more than a line. A wire carrying four ticks gets three buffer
    /// triangles, dividing it into four, so two paths of unequal total delay are visible without
    /// reading digits -- which is the whole lesson of the balancing levels. A delay-1 wire gets none,
    /// so the default look is unchanged and bare means "nothing added".
    ///
    /// **Buffers, not the cross-hatches they replaced** (2026-10-01, Ahmad's choice of three drawn
    /// side by side): the textbook symbol for a delay with nothing else in it, pointing the way the
    /// bits go, one per tick past the first and no cap. Hollow, never round, on purpose: a round pip
    /// would read as a bit in transit, and where the bits are is the one thing on screen that must
    /// stay unambiguous.
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
        [SerializeField] private ProbeController _probes;
        [SerializeField] private float _casingWidth = 0.17f;
        [SerializeField] private float _coreWidth = 0.065f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Per drawn edge, in step with each other. Index is not the edge id.</summary>
        private readonly List<int> _edgeIds = new List<int>();
        private readonly List<LineRenderer> _cores = new List<LineRenderer>();
        private readonly List<Vector2> _labelPositions = new List<Vector2>();
        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();

        private Transform _container;
        private Material _material;
        private Camera _camera;
        private int _builtRevision = -1;
        private int _sparkedCount;

        /// <summary>The W1-W4 tags on wires in the timing diagram, and what they were built for.</summary>
        private readonly List<GameObject> _tags = new List<GameObject>();
        private int _taggedProbes = -1;
        private int _taggedGraph = -1;

        /// <summary>A buffer triangle's size, along and across the wire, where the wire has room.</summary>
        /// <remarks>About the casing's width across, so the triangle stands just proud of the wire.</remarks>
        public const float MarkSize = 0.2f;

        /// <summary>The most of the gap between two triangles' centres one may take, so they never touch.</summary>
        public const float MarkFill = 0.9f;

        /// <summary>
        /// How large each buffer triangle on a wire of this length and delay is drawn, or zero for a
        /// wire that gets none.
        /// </summary>
        public static float MarkSizeFor(float length, int delay) =>
            delay < 2 || length <= 0f ? 0f : Mathf.Min(MarkSize, length / delay * MarkFill);

        /// <summary>
        /// The depths of a triangle's fill and of its outline: both share the wire core's sorting
        /// order, so each is put nearer the camera than what it covers.
        /// </summary>
        /// <remarks>
        /// The camera is at -10 looking along +z, so nearer is more negative. A tie in sorting
        /// order and depth is drawn in whichever order Unity happens to pick, so the fill could go
        /// under the core line on one capture and over it on the next.
        /// </remarks>
        private const float MarkFillDepth = -0.01f;

        /// <inheritdoc cref="MarkFillDepth"/>
        private const float MarkDepth = -0.02f;

        /// <summary>A tag's pill: the delay label's, wide enough for two characters.</summary>
        private static readonly Vector2 TagPill = new Vector2(0.52f, 0.28f);

        /// <summary>Offset of the number from the wire's centreline, so the marks have the middle.</summary>
        /// <remarks>
        /// Grown with the bits, so a larger bit passes beside its wire's number rather than under
        /// it. Where a digit passed under it, every 0 on the board went by with a 1 printed over
        /// its head.
        ///
        /// Worked out from the bit and the pill rather than stated: half a bit, a gap, half the pill.
        /// It was a constant times the bit's scale, which held only while the pill stayed the size
        /// it was tuned against -- grown a third, it would have touched the bits in Classic.
        /// </remarks>
        private static float LabelOffset =>
            BitRenderer.DefaultBitSize * Look.Current.BitScale * 0.5f + LabelGap + LabelPill.y * 0.5f;

        /// <summary>Clear space between a passing bit and the number's pill.</summary>
        private const float LabelGap = 0.03f;

        /// <summary>
        /// How far from the wire's centreline the number's pill begins, for the test that keeps it
        /// clear of the bits.
        /// </summary>
        public static float LabelInnerEdge => LabelOffset - LabelPill.y * 0.5f;

        /// <summary>The number's size, at rest and under the cursor.</summary>
        /// <remarks>
        /// A third larger than it was: in a playtest, 2026-09-25, the numbers read as "a bit smaller
        /// than expected" -- they are the only thing on the board that says what scrolling a wire
        /// did, so they should be read at a glance.
        /// </remarks>
        private const float LabelFontSize = 2.0f;

        /// <inheritdoc cref="LabelFontSize"/>
        private const float LabelHoverFontSize = 2.4f;

        /// <summary>The dark pill behind the number, in world units, grown with the number.</summary>
        private static readonly Vector2 LabelPill = new Vector2(0.35f, 0.28f);

        /// <summary>Index into <see cref="_labels"/> of the one drawn large, or -1.</summary>
        private int _hoveredLabel = -1;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_delay == null)
                _delay = FindFirstObjectByType<WireDelayController>();

            if (_sparks == null)
                _sparks = FindFirstObjectByType<SparkEffects>();

            if (_probes == null)
                _probes = FindFirstObjectByType<ProbeController>();

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

            int probes = _probes != null ? _probes.Probes.Revision : 0;

            if (probes != _taggedProbes || _builtRevision != _taggedGraph)
            {
                TagTheProbedWires();
                _taggedProbes = probes;
                _taggedGraph = _builtRevision;
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
            _labels.Clear();
            _hoveredLabel = -1;

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
                Spawn($"Edge {id} casing", from, to, _casingWidth, Palette.Current.WireCasing,
                    ViewLayers.WireCasing);
                LineRenderer core = Spawn($"Edge {id} core - {edge}", from, to, _coreWidth,
                    Palette.Current.WireCore, ViewLayers.WireCore);

                SpawnMarks(id, from, to, edge.Delay);

                _edgeIds.Add(id);
                _cores.Add(core);

                // Nudged off the centreline so the number and the division marks do not overlap.
                Vector2 midpoint = (from + to) * 0.5f;
                Vector2 labelAt = midpoint + Normal(from, to) * LabelOffset;

                _labelPositions.Add(labelAt);
                _labels.Add(SpawnLabel(id, labelAt, edge.Delay));
            }
        }

        /// <summary>
        /// A wire's delay as a small number beside it, on the board with the wire it describes.
        /// </summary>
        /// <remarks>
        /// These were drawn with IMGUI, which paints after the canvas -- so nothing on the canvas
        /// could ever cover them. They broke through twice: across the rows of the level list,
        /// which was patched by hiding them by hand behind full-screen panels; and on top of the
        /// solved card, which is not a full-screen panel and so was never covered by the patch.
        /// Drawn here, they sit under the canvas like everything else on the board, and every
        /// panel covers them without being told to. The reason IMGUI was chosen -- that world
        /// text would need a built-in font whose name changes between versions -- stopped
        /// applying once the board's own labels were TextMeshPro.
        ///
        /// Above the bits, as they were before: a number under a glowing bit is unreadable, and
        /// the dark pill is what keeps it readable over a bright wire.
        /// </remarks>
        private TextMeshPro SpawnLabel(int edgeId, Vector2 at, int delay)
        {
            var host = new GameObject($"Edge {edgeId} delay");
            host.transform.SetParent(_container, false);
            host.transform.position = at;
            _spawned.Add(host);

            // Children, not components on the host: one renderer per GameObject, and scaling the
            // pill must not scale the number.
            var pill = new GameObject("backing");
            pill.transform.SetParent(host.transform, false);

            var backing = pill.AddComponent<SpriteRenderer>();
            backing.sprite = ProceduralSprites.Dot();
            backing.color = Palette.Current.DelayLabelBacking;
            backing.sortingOrder = ViewLayers.WireLabelBacking;

            Vector2 native = backing.sprite.bounds.size;
            pill.transform.localScale = new Vector3(LabelPill.x / native.x, LabelPill.y / native.y, 1f);

            var number = new GameObject("number");
            number.transform.SetParent(host.transform, false);

            var text = number.AddComponent<TextMeshPro>();
            text.text = delay.ToString();
            text.fontSize = LabelFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Palette.Current.DelayLabel;
            text.sortingOrder = ViewLayers.WireLabel;
            text.rectTransform.sizeDelta = new Vector2(LabelPill.x * 2f, LabelPill.y);

            return text;
        }

        /// <summary>
        /// A tag on each wire in the timing diagram -- W1 to W4, as its row there is named -- on the
        /// side of the wire the delay's number is not.
        /// </summary>
        /// <remarks>
        /// The middle of a wire is taken: its bits travel through it, and a delay of two puts a
        /// buffer triangle there. The number sits off to one side, so the tag takes the other, at the same
        /// distance, which keeps it as clear of a passing bit as the number is. In the text colour
        /// on the number's dark pill: every hue on the board already means a part, a bit or a state,
        /// and the W says what it is.
        /// </remarks>
        private void TagTheProbedWires()
        {
            for (int i = 0; i < _tags.Count; i++)
            {
                if (_tags[i] != null)
                    Destroy(_tags[i]);
            }

            _tags.Clear();

            if (_probes == null)
                return;

            SimulationView view = _runner.View;

            for (int slot = 0; slot < WireProbes.Slots; slot++)
            {
                int id = _probes.Probes.EdgeIdAt(slot);

                if (id < 0 || id >= view.EdgeCount)
                    continue;

                Edge edge = view.GetEdge(id);

                if (edge == null)
                    continue;

                Vector2 from = PortGeometry.EndpointOf(edge.Source, _runner.PositionOf(edge.Source.Owner.Id));
                Vector2 to = PortGeometry.EndpointOf(edge.Target, _runner.PositionOf(edge.Target.Owner.Id));
                Vector2 at = (from + to) * 0.5f - Normal(from, to) * LabelOffset;

                _tags.Add(SpawnTag(slot, at));
            }
        }

        private GameObject SpawnTag(int slot, Vector2 at)
        {
            var host = new GameObject($"Wire tag W{slot + 1}");
            host.transform.SetParent(_container, false);
            host.transform.position = at;

            var pill = new GameObject("backing");
            pill.transform.SetParent(host.transform, false);

            var backing = pill.AddComponent<SpriteRenderer>();
            backing.sprite = ProceduralSprites.Dot();
            backing.color = Palette.Current.DelayLabelBacking;
            backing.sortingOrder = ViewLayers.WireLabelBacking;

            Vector2 native = backing.sprite.bounds.size;
            pill.transform.localScale = new Vector3(TagPill.x / native.x, TagPill.y / native.y, 1f);

            var name = new GameObject("name");
            name.transform.SetParent(host.transform, false);

            var text = name.AddComponent<TextMeshPro>();
            text.text = "W" + (slot + 1);
            text.fontSize = LabelFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Palette.Current.Text;
            text.sortingOrder = ViewLayers.WireLabel;
            text.rectTransform.sizeDelta = new Vector2(TagPill.x * 2f, TagPill.y);

            return host;
        }

        /// <summary>
        /// <paramref name="delay"/> minus one buffer triangles, dividing the wire into that many
        /// equal parts and pointing the way the bits go. A delay-1 wire gets none.
        /// </summary>
        /// <remarks>
        /// <see cref="MarkSize"/> where there is room, and smaller only where there is not: a wire
        /// between neighbouring cells is 0.8 long, so nine ticks on it put eight triangles under a
        /// tenth of a unit apart.
        /// </remarks>
        private void SpawnMarks(int edgeId, Vector2 from, Vector2 to, int delay)
        {
            if (delay < 2)
                return;

            Vector2 along = to - from;
            float size = MarkSizeFor(along.magnitude, delay);

            if (size <= 0f)
                return;

            Quaternion pointing = Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);

            for (int i = 1; i < delay; i++)
            {
                Vector2 centre = Vector2.Lerp(from, to, i / (float)delay);

                SpawnMark($"Edge {edgeId} buffer {i} fill", ProceduralSprites.WireBufferFill(),
                    centre, pointing, size, Palette.Current.WireCasing, MarkFillDepth);
                SpawnMark($"Edge {edgeId} buffer {i}", ProceduralSprites.WireBuffer(),
                    centre, pointing, size, Palette.Current.WireMark, MarkDepth);
            }
        }

        private void SpawnMark(string name, Sprite sprite, Vector2 centre, Quaternion pointing, float size,
            Color colour, float depth)
        {
            var mark = new GameObject(name);
            mark.transform.SetParent(_container, false);
            mark.transform.SetPositionAndRotation(new Vector3(centre.x, centre.y, depth), pointing);

            Vector2 native = sprite.bounds.size;
            mark.transform.localScale = new Vector3(size / native.x, size / native.y, 1f);

            var renderer = mark.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = colour;
            renderer.sortingOrder = ViewLayers.WireMark;

            _spawned.Add(mark);
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

            HighlightLabel(hovered);

            for (int i = 0; i < _cores.Count; i++)
            {
                LineRenderer core = _cores[i];
                if (core == null)
                    continue;

                int edgeId = _edgeIds[i];
                Color colour = Palette.Current.WireCore;

                if (edgeId == hovered)
                    colour = Palette.Current.WireHover;

                // Outranks hover: what this wire is about to do matters more than what the cursor
                // happens to be near. Tinting the whole wire rather than the stretch ahead of the
                // bit keeps this to a colour change, so no geometry is rebuilt for a warning -- and
                // it ties the wire to the port it is aimed at, which is the thing being warned about.
                Edge edge = _runner.View.GetEdge(edgeId);

                if (edge != null && PortState.WillCollide(edge, out bool heldBitDies))
                {
                    colour = Color.Lerp(colour, PortState.WarningColour(heldBitDies),
                        PortState.Pulse(ViewTime.Now, PortState.WarningHz));
                }

                // The flash wins over hover: it is the acknowledgement of an action the player just took.
                float flash = _delay != null ? _delay.FlashStrengthFor(edgeId) : 0f;
                if (flash > 0f)
                    colour = Color.Lerp(colour, Palette.Current.WireFlash, flash);

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

                _sparks.Burst(_labelPositions[i], Palette.Current.WireFlash);
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
        /// Draws the hovered wire's number large, and puts the last one back.
        /// </summary>
        /// <remarks>
        /// Only on a change. Setting a font size marks the text for a mesh rebuild even when the
        /// size is the one it already had, and this runs every frame.
        /// </remarks>
        private void HighlightLabel(int hoveredEdgeId)
        {
            int index = _edgeIds.IndexOf(hoveredEdgeId);

            if (index == _hoveredLabel)
                return;

            if (_hoveredLabel >= 0 && _hoveredLabel < _labels.Count && _labels[_hoveredLabel] != null)
                _labels[_hoveredLabel].fontSize = LabelFontSize;

            if (index >= 0 && index < _labels.Count && _labels[index] != null)
                _labels[index].fontSize = LabelHoverFontSize;

            _hoveredLabel = index;
        }

        private static Material WireMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }
    }
}
