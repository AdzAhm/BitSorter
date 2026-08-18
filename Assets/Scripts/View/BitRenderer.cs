using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws every bit currently in transit, one pooled sprite each, interpolated along its wire.
    /// </summary>
    /// <remarks>
    /// Sprites are keyed by (edge id, ticks remaining). Within one tick that pair is constant, so
    /// a bit keeps the same sprite across every frame of that tick. At a tick boundary the
    /// remaining count drops by exactly one, so the lookup also tries the previous tick's key --
    /// which means a bit keeps one sprite for its whole journey rather than being handed a fresh
    /// one each tick.
    ///
    /// Nothing here allocates per frame: the simulation is polled by index, the dictionaries are
    /// reused and swapped rather than rebuilt, and sprites come from a pool.
    /// </remarks>
    public sealed class BitRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameObject _bitPrefab;
        [SerializeField] private float _bitSize = 0.42f;
        [SerializeField] private Color _zeroColour = new Color(0.42f, 0.48f, 0.58f);
        [SerializeField] private Color _oneColour = new Color(1.00f, 0.88f, 0.32f);

        [SerializeField] private SparkEffects _sparks;
        [SerializeField] private float _glowScale = 2.4f;
        [SerializeField] private float _glowAlpha = 0.55f;
        [SerializeField] private float _trailSeconds = 0.22f;

        private Dictionary<long, SpriteRenderer> _live = new Dictionary<long, SpriteRenderer>();
        private Dictionary<long, SpriteRenderer> _next = new Dictionary<long, SpriteRenderer>();
        private readonly Stack<SpriteRenderer> _pool = new Stack<SpriteRenderer>();
        private readonly Dictionary<SpriteRenderer, SpriteRenderer> _halos =
            new Dictionary<SpriteRenderer, SpriteRenderer>();
        private readonly Dictionary<SpriteRenderer, TrailRenderer> _trails =
            new Dictionary<SpriteRenderer, TrailRenderer>();

        private Transform _container;

        /// <summary>
        /// Gates that have fired since this component woke, and bits that have reached a bin.
        /// </summary>
        /// <remarks>
        /// Monotonic, so a one-shot reaction can tell "it happened again" from "it is still true" by
        /// caching the last value it saw -- the same idiom
        /// <see cref="WireDelayController.ChangeCount"/> uses.
        ///
        /// They live here because this component already works both facts out. It reconstructs them
        /// by diffing bits between frames, since neither the simulation nor the runner announces
        /// anything, and duplicating that diff elsewhere would mean two subtly different ideas of
        /// what "fired" means.
        ///
        /// Sources are deliberately not counted as firing. One emits every tick from tick zero, so
        /// counting them would just be a second, noisier clock.
        /// </remarks>
        public int GateFiredCount { get; private set; }

        /// <inheritdoc cref="GateFiredCount"/>
        public int BinLandedCount { get; private set; }

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_sparks == null)
                _sparks = FindFirstObjectByType<SparkEffects>();

            // Its own container: NodeRenderer and EdgeRenderer tear their objects down on a
            // rebuild, and the pooled sprites must not be caught in that.
            _container = new GameObject("Bits").transform;
            _container.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (_runner == null || !_runner.IsReady)
                return;

            SimulationView view = _runner.View;
            float fraction = _runner.TickProgress;

            _next.Clear();

            for (int edgeId = 0; edgeId < view.EdgeCount; edgeId++)
            {
                Edge edge = view.GetEdge(edgeId);
                if (edge == null)
                    continue;   // removed edge; its sprites are released below by not being seen

                // Same stub endpoints the wire is drawn between, or bits would visibly travel
                // beside their wire instead of along it.
                Vector2 from = PortGeometry.EndpointOf(edge.Source, _runner.PositionOf(edge.Source.Owner.Id));
                Vector2 to = PortGeometry.EndpointOf(edge.Target, _runner.PositionOf(edge.Target.Owner.Id));

                for (int i = 0; i < edge.InTransitCount; i++)
                {
                    BitInTransit bit = edge.GetBitInTransit(i);

                    long key = Key(edge.Id, bit.TicksRemaining);
                    long keyLastTick = Key(edge.Id, bit.TicksRemaining + 1);

                    SpriteRenderer sprite;
                    if (_live.TryGetValue(key, out sprite))
                        _live.Remove(key);
                    else if (_live.TryGetValue(keyLastTick, out sprite))
                        _live.Remove(keyLastTick);   // same bit, one tick further along
                    else
                    {
                        // Neither key matched, so this bit did not exist last frame -- which means
                        // the node feeding this edge just consumed its inputs and fired.
                        sprite = Rent();
                        OnNodeFired(edge, from);
                    }

                    Color colour = bit.Value == Bit.One ? _oneColour : _zeroColour;
                    float travelled = Travelled(bit, fraction);

                    Transform bitTransform = sprite.transform;
                    bitTransform.position = Vector2.Lerp(from, to, travelled);

                    // Face along the wire so the arrival squash compresses in the travel direction.
                    Vector2 direction = to - from;
                    if (direction.sqrMagnitude > 1e-6f)
                        bitTransform.right = direction.normalized;

                    Vector2 scale = BitVisuals.ScaleAt(travelled, _bitSize);
                    bitTransform.localScale = new Vector3(scale.x, scale.y, 1f);

                    sprite.color = colour;
                    Tint(sprite, colour);

                    _next[key] = sprite;
                }
            }

            // Whatever is still in _live was not seen this frame, so those bits are gone.
            // Dictionary<,> has a struct enumerator, so this foreach does not allocate.
            foreach (KeyValuePair<long, SpriteRenderer> stale in _live)
            {
                OnBitGone(view, stale.Key);
                Release(stale.Value);
            }

            _live.Clear();

            Dictionary<long, SpriteRenderer> spent = _live;
            _live = _next;
            _next = spent;
        }

        /// <summary>
        /// Fraction of the wire covered, blending the simulator's whole-tick position with how far
        /// the clock has run into the tick that has not happened yet.
        /// </summary>
        private static float Travelled(BitInTransit bit, float fraction)
        {
            if (bit.TotalDelay <= 0)
                return 0f;

            return Mathf.Clamp01((bit.TotalDelay - bit.TicksRemaining + fraction) / bit.TotalDelay);
        }

        private static long Key(int edgeId, int ticksRemaining) =>
            ((long)edgeId << 32) | (uint)ticksRemaining;

        private static int EdgeOf(long key) => (int)(key >> 32);

        private static int TicksOf(long key) => (int)(key & 0xFFFFFFFFL);

        /// <summary>
        /// A brand-new bit on this edge means its source node consumed its inputs and emitted this
        /// tick. Sparks at the output it came out of.
        /// </summary>
        private void OnNodeFired(Edge edge, Vector2 outputPosition)
        {
            // Counted before the sparks null-check, so the tally is a fact about the simulation
            // rather than a side effect of whether an effects component happens to be wired up.
            if (!(edge.Source.Owner is SourceNode))
                GateFiredCount++;

            if (_sparks == null)
                return;

            _sparks.Burst(outputPosition, NodeShapes.ColourFor(edge.Source.Owner));
        }

        /// <summary>
        /// A bit that vanished with one tick left arrived at its target port. A bit that vanished
        /// because its edge was deleted did not, so the edge has to still exist.
        /// </summary>
        private void OnBitGone(SimulationView view, long key)
        {
            if (TicksOf(key) != 1)
                return;

            int edgeId = EdgeOf(key);
            if (edgeId < 0 || edgeId >= view.EdgeCount)
                return;

            Edge edge = view.GetEdge(edgeId);
            if (edge == null)
                return;   // the wire was deleted; nothing arrived

            // Only arrivals at a bin are counted. A bit reaching a gate's input port is the ordinary
            // business of the circuit and happens constantly; reaching a bin is the result.
            if (edge.Target.Owner is SinkNode)
                BinLandedCount++;

            if (_sparks == null)
                return;

            Vector2 target = PortGeometry.EndpointOf(edge.Target, _runner.PositionOf(edge.Target.Owner.Id));
            _sparks.Burst(target, NodeShapes.ColourFor(edge.Target.Owner));
        }

        /// <summary>Keeps a bit's glow halo and trail in step with its value colour.</summary>
        private void Tint(SpriteRenderer sprite, Color colour)
        {
            if (!_halos.TryGetValue(sprite, out SpriteRenderer halo))
                return;

            halo.color = new Color(colour.r, colour.g, colour.b, _glowAlpha);

            if (_trails.TryGetValue(sprite, out TrailRenderer trail))
            {
                trail.startColor = new Color(colour.r, colour.g, colour.b, 0.75f);
                trail.endColor = new Color(colour.r, colour.g, colour.b, 0f);
            }
        }

        private SpriteRenderer Rent()
        {
            if (_pool.Count > 0)
            {
                SpriteRenderer pooled = _pool.Pop();
                pooled.gameObject.SetActive(true);

                // Mandatory on reuse. A TrailRenderer keeps its points across a reposition, so a
                // recycled bit would draw a streak from wherever the previous one died.
                if (_trails.TryGetValue(pooled, out TrailRenderer trail))
                    trail.Clear();

                return pooled;
            }

            GameObject instance = ViewSprites.Spawn(_bitPrefab, _container, "Bit");
            instance.transform.localScale = Vector3.one * _bitSize;

            var renderer = instance.GetComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.Dot();
            renderer.sortingOrder = 3;   // in front of nodes, wires and port stubs

            // Halo is a child, so it inherits the squash and stays centred on the bit.
            var halo = new GameObject("Glow");
            halo.transform.SetParent(instance.transform, false);
            halo.transform.localScale = Vector3.one * _glowScale;

            var haloRenderer = halo.AddComponent<SpriteRenderer>();
            haloRenderer.sprite = ProceduralSprites.Glow();
            haloRenderer.sortingOrder = 2;
            _halos[renderer] = haloRenderer;

            _trails[renderer] = BuildTrail(instance.transform);

            return renderer;
        }

        /// <summary>
        /// The trail lives on a child, not on the bit itself: Unity allows only one Renderer per
        /// GameObject, and the bit already carries a SpriteRenderer.
        /// </summary>
        private TrailRenderer BuildTrail(Transform parent)
        {
            var host = new GameObject("Trail");
            host.transform.SetParent(parent, false);

            var trail = host.AddComponent<TrailRenderer>();
            trail.time = _trailSeconds;
            trail.material = TrailMaterial();
            trail.numCapVertices = 4;
            trail.sortingOrder = 1;
            trail.minVertexDistance = 0.02f;
            trail.autodestruct = false;

            // Trail geometry is world-space, so the parent's arrival squash does not distort it.
            trail.widthMultiplier = _bitSize * 0.85f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

            return trail;
        }

        private static Material TrailMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            return new Material(shader);
        }

        private void Release(SpriteRenderer sprite)
        {
            if (_trails.TryGetValue(sprite, out TrailRenderer trail))
                trail.Clear();

            sprite.gameObject.SetActive(false);
            _pool.Push(sprite);
        }
    }
}
