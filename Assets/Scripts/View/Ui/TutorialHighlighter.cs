using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// Draws a pulsing ring around whatever the current tutorial step is asking for — a palette row,
    /// the Run button, a board cell, or a port.
    /// </summary>
    /// <remarks>
    /// Two kinds of target and therefore two kinds of ring, because the palette lives on a screen
    /// space Canvas and the board lives in world space, and no single transform is in both. They are
    /// kept in one component anyway: only one step is ever current, so only one thing may be
    /// highlighted, and splitting that across two components would make "highlight exactly one
    /// thing" a rule nobody owns.
    ///
    /// World rings come from <see cref="PortGeometry"/> and <see cref="PlacementGrid"/> — the same
    /// geometry the hit tester uses — so a ring lands exactly where the click has to go rather than
    /// approximately near it.
    ///
    /// Never a raycast target. A ring drawn over the Run button that swallowed the click on it would
    /// be a tutorial that prevents its own next step.
    /// </remarks>
    public sealed class TutorialHighlighter : MonoBehaviour
    {
        [Tooltip("Canvas the screen-space rings are built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        [SerializeField] private Color _colour = new Color(0.46f, 0.94f, 0.90f);

        [Tooltip("Slow, so it reads as guidance rather than as the danger throb on the board.")]
        [SerializeField] private float _pulseHz = 1.4f;

        [SerializeField] private float _minAlpha = 0.35f;
        [SerializeField] private float _maxAlpha = 0.95f;

        [Tooltip("How far outside a canvas target the ring sits.")]
        [SerializeField] private float _canvasPadding = 10f;

        [Tooltip("World-space diameter of a ring on a port or a cell.")]
        [SerializeField] private float _worldSize = 0.9f;

        [SerializeField] private float _worldSwell = 0.15f;

        private readonly List<Image> _canvasRings = new List<Image>();
        private readonly List<SpriteRenderer> _worldRings = new List<SpriteRenderer>();

        private int _canvasUsed;
        private int _worldUsed;
        private Transform _worldContainer;

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();

            _worldContainer = new GameObject("Tutorial rings").transform;
            _worldContainer.SetParent(transform, false);
        }

        /// <summary>
        /// Starts a frame's worth of highlighting. Everything shown last frame is released, so a
        /// caller that points at nothing this frame highlights nothing.
        /// </summary>
        /// <remarks>
        /// Rebuilt per frame rather than diffed. A step's targets move — a port ring has to follow
        /// the gate the player just placed — and a pool of at most four rings is cheaper to refill
        /// than to keep in step.
        /// </remarks>
        public void Begin()
        {
            _canvasUsed = 0;
            _worldUsed = 0;
        }

        /// <summary>Hides whatever was not claimed since <see cref="Begin"/>.</summary>
        /// <remarks>
        /// Null-checks both pools. Nothing should be able to destroy a ring now that they are no
        /// longer parented to what they point at, but a pool that hands out a dead object takes the
        /// tutorial down with an exception per frame -- the same belt-and-braces
        /// <see cref="ProceduralAudio"/> keeps on its clip cache.
        /// </remarks>
        public void End()
        {
            for (int i = _canvasUsed; i < _canvasRings.Count; i++)
            {
                if (_canvasRings[i] != null)
                    _canvasRings[i].gameObject.SetActive(false);
            }

            for (int i = _worldUsed; i < _worldRings.Count; i++)
            {
                if (_worldRings[i] != null)
                    _worldRings[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Reused by every canvas ring; GetWorldCorners fills it in place.</summary>
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>Rings a Canvas element: a palette row, or the Run button.</summary>
        /// <remarks>
        /// Positioned over the target, not parented to it.
        ///
        /// Parenting looked better -- the ring followed its target for free -- and it made the pool
        /// hand out destroyed objects. <see cref="GatePaletteView"/> destroys every palette row when
        /// the level changes, and a ring parented to a row went with it, leaving a dead Image in
        /// <see cref="_canvasRings"/>. Starting the tutorial, leaving it during a step that rings
        /// the palette, and starting it again then threw MissingReferenceException on the pooled
        /// ring, every frame, until the tutorial was skipped.
        ///
        /// Following the target costs nothing anyway: the director re-resolves its targets and calls
        /// this every frame, so a rebuilt palette is picked up on the next one regardless.
        /// </remarks>
        public void PointAt(RectTransform target)
        {
            if (target == null || _canvas == null)
                return;

            Image ring = NextCanvasRing();

            target.GetWorldCorners(Corners);

            // Corners are bottom-left, top-left, top-right, bottom-right. World space for an
            // overlay canvas is screen pixels, and sizeDelta is in canvas units, hence the divide.
            float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;

            float width = Vector3.Distance(Corners[0], Corners[3]) / scale;
            float height = Vector3.Distance(Corners[0], Corners[1]) / scale;

            ring.rectTransform.position = (Corners[0] + Corners[2]) * 0.5f;
            ring.rectTransform.sizeDelta = new Vector2(
                width + 2f * _canvasPadding, height + 2f * _canvasPadding);

            ring.color = Tinted();
            ring.gameObject.SetActive(true);
        }

        /// <summary>Rings a point on the board: a cell, a port, or a fixture.</summary>
        public void PointAt(Vector2 world, float scale = 1f)
        {
            SpriteRenderer ring = NextWorldRing();

            float swell = Mathf.Lerp(1f - _worldSwell, 1f + _worldSwell, Pulse());

            ring.transform.position = world;
            ring.transform.localScale = Vector3.one * _worldSize * scale * swell;
            ring.color = Tinted();
            ring.gameObject.SetActive(true);
        }

        private float Pulse() => PortState.Pulse(ViewTime.Now, _pulseHz);

        private Color Tinted()
        {
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, Pulse());
            return new Color(_colour.r, _colour.g, _colour.b, alpha);
        }

        private Image NextCanvasRing()
        {
            // A pooled ring that has somehow been destroyed is replaced rather than handed out.
            // Without this the whole tutorial dies on a MissingReferenceException per frame.
            while (_canvasUsed < _canvasRings.Count)
            {
                Image pooled = _canvasRings[_canvasUsed];

                if (pooled != null)
                {
                    _canvasUsed++;
                    return pooled;
                }

                _canvasRings.RemoveAt(_canvasUsed);
            }

            Image ring = UiTheme.Panel_("Tutorial ring", _canvas.transform, _colour);
            ring.sprite = ProceduralSprites.RoundedSquare();
            ring.raycastTarget = false;   // never eat the click the step is asking for

            // Centred, so sizeDelta is the ring's actual size and PointAt can place it over its
            // target by world position. It used to be stretched to fill a parent it no longer has.
            ring.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            _canvasRings.Add(ring);
            _canvasUsed++;

            return ring;
        }

        private SpriteRenderer NextWorldRing()
        {
            if (_worldUsed < _worldRings.Count)
                return _worldRings[_worldUsed++];

            var host = new GameObject("Tutorial ring");
            host.transform.SetParent(_worldContainer, false);

            var ring = host.AddComponent<SpriteRenderer>();
            ring.sprite = ProceduralSprites.Ring();

            // Above the board, the wires and the port stubs, below the bits, so a highlight never
            // hides the thing it is pointing at.
            ring.sortingOrder = ViewLayers.TutorialRing;

            _worldRings.Add(ring);
            _worldUsed++;

            return ring;
        }
    }
}
