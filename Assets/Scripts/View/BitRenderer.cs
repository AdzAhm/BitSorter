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

        private Dictionary<long, SpriteRenderer> _live = new Dictionary<long, SpriteRenderer>();
        private Dictionary<long, SpriteRenderer> _next = new Dictionary<long, SpriteRenderer>();
        private readonly Stack<SpriteRenderer> _pool = new Stack<SpriteRenderer>();

        private Transform _container;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

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

                Vector2 from = _runner.PositionOf(edge.Source.Owner.Id);
                Vector2 to = _runner.PositionOf(edge.Target.Owner.Id);

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
                        sprite = Rent();

                    sprite.transform.position = Vector2.Lerp(from, to, Travelled(bit, fraction));
                    sprite.color = bit.Value == Bit.One ? _oneColour : _zeroColour;

                    _next[key] = sprite;
                }
            }

            // Whatever is still in _live was not seen this frame, so those bits are gone.
            // Dictionary<,> has a struct enumerator, so this foreach does not allocate.
            foreach (KeyValuePair<long, SpriteRenderer> stale in _live)
                Release(stale.Value);

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

        private SpriteRenderer Rent()
        {
            if (_pool.Count > 0)
            {
                SpriteRenderer pooled = _pool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            GameObject instance = ViewSprites.Spawn(_bitPrefab, _container, "Bit");
            instance.transform.localScale = Vector3.one * _bitSize;

            var renderer = instance.GetComponent<SpriteRenderer>();
            renderer.sortingOrder = 2;   // in front of nodes and wires
            return renderer;
        }

        private void Release(SpriteRenderer sprite)
        {
            sprite.gameObject.SetActive(false);
            _pool.Push(sprite);
        }
    }
}
