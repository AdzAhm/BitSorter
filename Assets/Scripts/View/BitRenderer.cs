using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Draws every bit currently in transit, one pooled sprite each, interpolated along its wire.
    /// </summary>
    /// <remarks>
    /// Sprites are keyed by (edge id, <see cref="BitInTransit.Serial"/>), which names one bit for
    /// the whole graph's life. So a bit keeps one sprite from emission to delivery, and the frame
    /// diff below means exactly what it says: a key that was not here last frame is a bit that has
    /// just been emitted, and one that has gone is a bit that has left the wire.
    ///
    /// It was keyed by (edge id, ticks remaining), with a second lookup at the previous tick's
    /// count to bridge tick boundaries. That aliases: the count a departing bit vacates is taken
    /// by the bit behind it, so on a delay-1 edge -- which is every wire until the player lengthens
    /// one -- a whole stream read as a single bit that never arrived. No spark fired after the
    /// first, and <see cref="GateFiredCount"/> and <see cref="BinLandedCount"/> stopped counting,
    /// which took the gate and landing cues with them.
    ///
    /// Nothing here allocates per frame: the simulation is polled by index, the dictionaries are
    /// reused and swapped rather than rebuilt, and sprites come from a pool.
    /// </remarks>
    public sealed class BitRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameObject _bitPrefab;
        [SerializeField] private float _bitSize = 0.42f;

        [SerializeField] private SparkEffects _sparks;
        [SerializeField] private float _glowScale = 2.4f;
        [SerializeField] private float _glowAlpha = 0.55f;
        [SerializeField] private float _trailSeconds = 0.22f;

        /// <summary>
        /// One drawn bit: its sprite, and how far it had left to travel when last seen.
        /// </summary>
        /// <remarks>
        /// The remaining count is carried here rather than read back out of the key, which is what
        /// it used to be. <see cref="OnBitGone"/> needs it to tell a bit that arrived from one whose
        /// wire was deleted under it, and the key is now a serial that says nothing about position.
        /// </remarks>
        private readonly struct Tracked
        {
            public readonly SpriteRenderer Sprite;
            public readonly int TicksRemaining;

            public Tracked(SpriteRenderer sprite, int ticksRemaining)
            {
                Sprite = sprite;
                TicksRemaining = ticksRemaining;
            }
        }

        private Dictionary<long, Tracked> _live = new Dictionary<long, Tracked>();
        private Dictionary<long, Tracked> _next = new Dictionary<long, Tracked>();
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

                    long key = Key(edge.Id, bit.Serial);

                    // Worked out before the bit is rented, not after: a recycled object has to
                    // be standing where it belongs before its trail is cleared, or the trail draws
                    // the jump from wherever the last bit died.
                    float travelled = Travelled(bit, fraction);
                    Vector2 at = Vector2.Lerp(from, to, travelled);

                    SpriteRenderer sprite;

                    if (_live.TryGetValue(key, out Tracked already))
                    {
                        _live.Remove(key);
                        sprite = already.Sprite;
                    }
                    else
                    {
                        // This serial was not on the wire last frame, so the node feeding this edge
                        // has just consumed its inputs and fired. One lookup, because the key names
                        // the bit rather than its position.
                        sprite = Rent(at);
                        OnNodeFired(edge, from);
                    }

                    Color colour = BitVisuals.ColourFor(bit.Value);

                    // The bit that is one tick from an occupied port takes the warning colour, in
                    // step with the wire under it and the socket ahead of it. Without this the
                    // board would warn about the destination while the thing arriving looked fine.
                    if (bit.TicksRemaining == 1 && PortState.WillCollide(edge, out bool heldBitDies))
                    {
                        colour = Color.Lerp(colour, PortState.WarningColour(heldBitDies),
                            PortState.Pulse(ViewTime.Now, PortState.WarningHz));
                    }

                    Transform bitTransform = sprite.transform;
                    bitTransform.position = at;

                    // Face along the wire so the arrival squash compresses in the travel direction.
                    Vector2 direction = to - from;
                    if (direction.sqrMagnitude > 1e-6f)
                        bitTransform.right = direction.normalized;

                    Vector2 scale = BitVisuals.ScaleAt(travelled, _bitSize);
                    bitTransform.localScale = new Vector3(scale.x, scale.y, 1f);

                    // Lifted above the bloom threshold here rather than in the colour itself,
                    // so the port a bit lands in and the disc inside a register keep the plain
                    // colour: those sit on a body and would blow it out.
                    Color emissive = BitVisuals.Emissive(colour);

                    sprite.color = emissive;
                    Tint(sprite, emissive, colour);

                    _next[key] = new Tracked(sprite, bit.TicksRemaining);
                }
            }

            // Whatever is still in _live was not seen this frame, so those bits are gone.
            // Dictionary<,> has a struct enumerator, so this foreach does not allocate.
            foreach (KeyValuePair<long, Tracked> stale in _live)
            {
                OnBitGone(view, stale.Key, stale.Value.TicksRemaining);
                Release(stale.Value.Sprite);
            }

            _live.Clear();

            Dictionary<long, Tracked> spent = _live;
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

        /// <summary>
        /// One bit, named for the life of the graph. See <see cref="BitInTransit.Serial"/>.
        /// </summary>
        private static long Key(int edgeId, int serial) =>
            ((long)edgeId << 32) | (uint)serial;

        private static int EdgeOf(long key) => (int)(key >> 32);

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
        /// <param name="ticksRemaining">
        /// What the bit had left to travel when it was last drawn, from <see cref="Tracked"/>. The
        /// key is a serial now and says nothing about how far along the wire the bit had got.
        /// </param>
        private void OnBitGone(SimulationView view, long key, int ticksRemaining)
        {
            if (ticksRemaining != 1)
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
        /// <remarks>
        /// Two colours, because the halo and the trail do different jobs. The halo is the bit's
        /// own light and takes the lifted colour, which is what makes it bloom. The trail says
        /// where the bit has just been and keeps the plain one: lifted, it bloomed at the same
        /// strength as the bit itself and drew a bright smear the length of the wire behind every
        /// bit, which is the thickness a playtester already found too heavy before any of this.
        /// </remarks>
        private void Tint(SpriteRenderer sprite, Color emissive, Color plain)
        {
            if (!_halos.TryGetValue(sprite, out SpriteRenderer halo))
                return;

            halo.color = new Color(emissive.r, emissive.g, emissive.b, _glowAlpha);

            if (_trails.TryGetValue(sprite, out TrailRenderer trail))
            {
                trail.startColor = new Color(plain.r, plain.g, plain.b, 0.75f);
                trail.endColor = new Color(plain.r, plain.g, plain.b, 0f);
            }
        }

        /// <summary>
        /// A bit sprite, standing at <paramref name="at"/> before it is shown.
        /// </summary>
        /// <remarks>
        /// The position is taken rather than left to the caller, because the order is the whole
        /// point. A TrailRenderer emits along whatever path its transform takes, so a pooled bit
        /// that is shown where the last one died and moved afterwards draws a line between the
        /// two -- and clearing the trail before that move does nothing about it. Placed first,
        /// shown second, cleared third.
        ///
        /// It was clear-then-move, and in play that was a thin diagonal across the board every
        /// time a gate fired, in the colour of whichever bit had just been recycled. A fresh
        /// sprite had the same fault from the other end: it appeared at the container's origin
        /// and streaked from there to its wire.
        /// </remarks>
        private SpriteRenderer Rent(Vector2 at)
        {
            if (_pool.Count > 0)
            {
                SpriteRenderer pooled = _pool.Pop();

                pooled.transform.position = at;
                pooled.gameObject.SetActive(true);

                if (_trails.TryGetValue(pooled, out TrailRenderer trail))
                    trail.Clear();

                return pooled;
            }

            GameObject instance = ViewSprites.Spawn(_bitPrefab, _container, "Bit");
            instance.transform.position = at;
            instance.transform.localScale = Vector3.one * _bitSize;

            var renderer = instance.GetComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.Dot();
            renderer.sortingOrder = ViewLayers.Bit;

            // Halo is a child, so it inherits the squash and stays centred on the bit.
            var halo = new GameObject("Glow");
            halo.transform.SetParent(instance.transform, false);
            halo.transform.localScale = Vector3.one * _glowScale;

            var haloRenderer = halo.AddComponent<SpriteRenderer>();
            haloRenderer.sprite = ProceduralSprites.Glow();
            haloRenderer.sortingOrder = ViewLayers.BitGlow;
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
            trail.sortingOrder = ViewLayers.NodeDetail;
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
