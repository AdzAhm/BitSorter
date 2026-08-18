using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Burns a mark onto every port where a collision destroyed a bit, and leaves it there until
    /// the next run.
    /// </summary>
    /// <remarks>
    /// The bits-lost meter says how many were destroyed. It never said where, so a player with six
    /// gates on the board had a number and no way to act on it. This turns the same fact into a
    /// place: the mark sits on the port that took the second bit, which on an unbalanced circuit is
    /// exactly the junction whose two paths disagree.
    ///
    /// Marks persist deliberately. A collision lasts one tick, and a flash that has faded by the
    /// time the run ends is a flash the player was not looking at. These stay put through the
    /// verdict, so the evidence is still on the board while they read what went wrong, and clear
    /// only when the graph is rebuilt -- which is to say when they act on it.
    ///
    /// The simulation is the only authority on where the marks go; this component derives every
    /// one from <see cref="SimulationView.CorruptionSites"/> and never decides anything itself.
    /// </remarks>
    public sealed class ScorchMarks : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Colour of the mark. Defaults to the red already used for destruction.")]
        [SerializeField] private Color _colour = new Color(0.95f, 0.30f, 0.28f);

        [SerializeField] private float _size = 1.15f;
        [SerializeField] private float _alpha = 0.5f;

        [Tooltip("Seconds the mark takes to bloom in, so it reads as something that just happened.")]
        [SerializeField] private float _bloomSeconds = 0.25f;

        private struct Mark
        {
            public SpriteRenderer Renderer;
            public float Born;
        }

        private readonly Dictionary<InputPort, Mark> _marks = new Dictionary<InputPort, Mark>();
        private readonly List<Mark> _order = new List<Mark>();

        private Transform _container;
        private int _revision = -1;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();

            _container = new GameObject("Scorch marks").transform;
            _container.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (_runner == null || !_runner.IsReady)
                return;

            // A rebuild is the only thing that clears them: the graph the marks referred to is gone,
            // and the ports they were keyed by belong to it.
            if (_runner.GraphRevision != _revision)
            {
                _revision = _runner.GraphRevision;
                Clear();
            }

            SimulationView view = _runner.View;
            IReadOnlyList<InputPort> sites = view.CorruptionSites;

            for (int i = 0; i < sites.Count; i++)
                Ensure(sites[i]);

            Animate();
        }

        /// <summary>Adds a mark for a port that has not been marked yet.</summary>
        private void Ensure(InputPort port)
        {
            if (_marks.ContainsKey(port))
                return;

            // The owner's id is how the runner finds where to draw. A removed node reports -1, and
            // the simulation drops its sites when it goes, so this is belt and braces rather than a
            // case that is expected to fire.
            if (port.Owner == null || port.Owner.Id < 0)
                return;

            var go = new GameObject($"Scorch {port.Owner.Name}.{port.Index}");
            go.transform.SetParent(_container, false);
            go.transform.position = PortGeometry.EndpointOf(port, _runner.PositionOf(port.Owner.Id));

            var renderer = go.AddComponent<SpriteRenderer>();

            // The bloom the bits already use, rather than a new sprite. It is the one shape in the
            // project tuned to fade at its edges, which is what a scorch wants.
            renderer.sprite = ProceduralSprites.Glow();
            renderer.color = new Color(_colour.r, _colour.g, _colour.b, 0f);
            renderer.sortingOrder = -4;   // over the grid dots, under the gate it marks

            var mark = new Mark { Renderer = renderer, Born = Time.time };

            _marks.Add(port, mark);
            _order.Add(mark);
        }

        /// <summary>
        /// Blooms each mark in over <see cref="_bloomSeconds"/> and then leaves it alone.
        /// </summary>
        /// <remarks>
        /// Stops touching a mark once it has settled, so a board full of them costs nothing per
        /// frame beyond the loop itself.
        /// </remarks>
        private void Animate()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                Mark mark = _order[i];
                float age = Time.time - mark.Born;

                if (age > _bloomSeconds)
                    continue;

                float t = _bloomSeconds <= 0f ? 1f : Mathf.Clamp01(age / _bloomSeconds);

                SpriteRenderer renderer = mark.Renderer;
                renderer.color = new Color(_colour.r, _colour.g, _colour.b, _alpha * t);

                // Overshoots and settles, so it lands rather than simply appearing.
                float scale = _size * (1f + 0.5f * (1f - t) * (1f - t));
                renderer.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void Clear()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                if (_order[i].Renderer != null)
                    Destroy(_order[i].Renderer.gameObject);
            }

            _marks.Clear();
            _order.Clear();
        }
    }
}
