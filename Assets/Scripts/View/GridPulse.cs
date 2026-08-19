using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A slow wave of brightness across the grid dots, so the board is not completely still while
    /// the player is thinking.
    /// </summary>
    /// <remarks>
    /// An idle board reads as a screenshot. This is the cheapest thing that fixes that without
    /// competing with anything: it touches only the dots, which are the dimmest thing on screen and
    /// carry no information, so nothing the player needs to read is animated.
    ///
    /// Diagonal, because a wave travelling along one axis reads as rows or columns flashing in
    /// unison, which looks like a fault rather than motion. Phase comes from cell position, so the
    /// crest moves across the board.
    ///
    /// Quietens while the simulation is running. Bits moving along wires are the thing to watch
    /// then, and a background that keeps pulsing underneath them is just noise.
    /// </remarks>
    public sealed class GridPulse : MonoBehaviour
    {
        [SerializeField] private PlacementGrid _grid;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Waves per second.")]
        [SerializeField] private float _speed = 0.3f;

        [Tooltip("How much a dot brightens at the crest, as a fraction of its base colour.")]
        [SerializeField] private float _depth = 1.1f;

        [Tooltip("How much a dot grows at the crest, as a fraction of its base size.")]
        [SerializeField] private float _swell = 0.45f;

        [Tooltip("Multiplier applied to the depth while a run is in progress.")]
        [SerializeField] private float _runningDepth = 0.25f;

        private readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();
        private readonly List<float> _phases = new List<float>();

        private Color _base;
        private float _baseScale = 1f;
        private bool _ready;

        private void Awake()
        {
            if (_grid == null) _grid = FindFirstObjectByType<PlacementGrid>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
        }

        /// <summary>
        /// Collects the dots after <see cref="PlacementGrid"/> has built them.
        /// </summary>
        /// <remarks>
        /// The grid spawns its dots in Start, so this cannot run in Start too without depending on
        /// component order. A first-frame Update is late enough to be certain and early enough that
        /// nobody sees the difference.
        /// </remarks>
        private void Collect()
        {
            _ready = true;

            if (_grid == null)
                return;

            Transform container = _grid.transform.Find("Grid dots");

            if (container == null)
                return;

            float period = _grid.CellSize * 6f;

            foreach (Transform dot in container)
            {
                var renderer = dot.GetComponent<SpriteRenderer>();

                if (renderer == null)
                    continue;

                _dots.Add(renderer);

                // Diagonal: x + y, so the crest travels corner to corner.
                Vector3 at = dot.position;
                _phases.Add((at.x + at.y) / period);
            }

            if (_dots.Count > 0)
            {
                _base = _dots[0].color;
                _baseScale = _dots[0].transform.localScale.x;
            }
        }

        private void Update()
        {
            if (!_ready)
                Collect();

            if (_dots.Count == 0)
                return;

            bool running = _runner != null && _runner.IsReady && !_runner.IsIdle();
            float quiet = running ? _runningDepth : 1f;
            float t = Time.time * _speed;

            for (int i = 0; i < _dots.Count; i++)
            {
                // 0..1, so a dot only ever brightens from its authored colour and never dims below
                // it -- the grid's resting appearance stays the one that was chosen.
                float wave = 0.5f + 0.5f * Mathf.Sin((t + _phases[i]) * Mathf.PI * 2f);

                float lift = 1f + _depth * quiet * wave;

                _dots[i].color = new Color(
                    _base.r * lift, _base.g * lift, _base.b * lift, _base.a);

                // Size as well as brightness, because brightness alone did not register.
                //
                // A grid dot is 0.14 world units, which is about seven pixels. Measured in play,
                // the old wave was moving each dot between 1.23x and 1.48x of its base colour --
                // running exactly as intended, and completely imperceptible, because a small
                // brightness step on a dark seven-pixel sprite is below the threshold at which
                // anyone notices anything. Scale is the channel that reads at that size.
                float swell = _baseScale * (1f + _swell * quiet * wave);
                _dots[i].transform.localScale = new Vector3(swell, swell, 1f);
            }
        }
    }
}
