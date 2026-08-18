using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Lights the bins up when the level is solved.
    /// </summary>
    /// <remarks>
    /// The half of the win state that happens where the player was looking. A panel appears in the
    /// middle of the screen; the bins are where the last bit actually landed, and leaving them
    /// unchanged makes the win feel like it happened to the interface rather than to the circuit.
    ///
    /// Reuses the glow sprite the renderers already put behind every node, brightened and pulsed,
    /// rather than introducing a new effect -- the bloom that makes those glows read as light is
    /// already tuned for exactly this sprite.
    /// </remarks>
    public sealed class SinkCelebration : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;

        [Tooltip("Seconds the bins keep pulsing after a win.")]
        [SerializeField] private float _seconds = 2.4f;

        [Tooltip("Pulses per second.")]
        [SerializeField] private float _rate = 2.6f;

        private readonly List<SpriteRenderer> _glows = new List<SpriteRenderer>();
        private Transform _container;
        private float _remaining;
        private RunState _state = RunState.Editing;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();

            _container = new GameObject("Sink celebration").transform;
            _container.SetParent(transform, false);
        }

        private void Update()
        {
            if (_session == null || _runner == null || !_runner.IsReady)
                return;

            RunState now = _session.State;

            if (now != _state)
            {
                if (now == RunState.Passed)
                    Begin();
                else
                    Stop();

                _state = now;
            }

            if (_remaining <= 0f)
                return;

            _remaining -= Time.deltaTime;

            // Falls away as it goes, so the board settles rather than being left flashing at someone
            // who has stopped looking.
            float fade = Mathf.Clamp01(_remaining / Mathf.Max(0.01f, _seconds));
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * _rate * Mathf.PI * 2f);
            float alpha = fade * Mathf.Lerp(0.35f, 0.95f, pulse);

            for (int i = 0; i < _glows.Count; i++)
            {
                if (_glows[i] == null)
                    continue;

                Color colour = _glows[i].color;
                _glows[i].color = new Color(colour.r, colour.g, colour.b, alpha);
                _glows[i].transform.localScale =
                    Vector3.one * PortGeometry.NodeSize * Mathf.Lerp(2.4f, 3.4f, pulse);
            }

            if (_remaining <= 0f)
                Stop();
        }

        private void Begin()
        {
            Stop();

            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                if (!(node is SinkNode))
                    continue;

                GameObject glow = ViewSprites.Spawn(null, _container, $"Win glow {id}");
                glow.transform.position = _runner.PositionOf(id);

                var renderer = glow.GetComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.Glow();
                renderer.color = NodeShapes.ColourFor(node);

                // Behind the node body, in the slot the ordinary node glow already uses.
                renderer.sortingOrder = -2;

                _glows.Add(renderer);
            }

            _remaining = _seconds;
        }

        private void Stop()
        {
            for (int i = 0; i < _glows.Count; i++)
            {
                if (_glows[i] != null)
                    Destroy(_glows[i].gameObject);
            }

            _glows.Clear();
            _remaining = 0f;
        }
    }
}
