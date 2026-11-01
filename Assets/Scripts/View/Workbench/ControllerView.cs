using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// A controller drawn as the textbook draws one, in the circuit's window (Part 15h-6e, Ahmad's choice, 2026-11-01): a state
    /// machine as its states -- each a box of its name and what it sets -- and the arrows between them, labelled in the code's
    /// own words; a decoder as its control table. The state the run is in and the arrow it came by are lit, and each table's
    /// row its subject picks. Laid out by <see cref="ControllerLayout"/>, painted with UI Toolkit's own painter in the style's
    /// colours, its words labels; the wheel zooms about the pointer, a drag moves it, and FIT fits it again.
    /// </summary>
    /// <remarks>
    /// Opaque over the drawing it stands for, and a control (<see cref="ShellTheme.ControlClass"/>), so the workbench leaves the
    /// wheel and the pointer over it to it: nothing reaches the drawing under it.
    /// </remarks>
    public sealed class ControllerView : VisualElement
    {
        public const string ViewName = "Workbench controller";

        /// <summary>The most it zooms in, and out, on its fit.</summary>
        public const float MostIn = 4f, MostOut = 0.5f;

        private const float Margin = 24f;

        private readonly Label _probe, _boldProbe;
        private readonly List<(Label Label, Vec2 At, bool Box)> _words = new List<(Label, Vec2, bool)>();
        private ControllerReading _reading;
        private ControllerLayout _layout;
        private int _state = -1, _from = -1;
        private int[] _rows = new int[0];
        private float _fit = 1f, _zoom = 1f;
        private Vector2 _pan;
        private bool _panning;
        private Vector2 _panFrom;
        private Vector2 _laidFor;

        public ControllerView()
        {
            name = ViewName;
            AddToClassList(ShellTheme.ControlClass);
            pickingMode = PickingMode.Position;
            style.position = Position.Absolute;
            style.overflow = Overflow.Hidden;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Placed());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);

            // Measures of the face the words are set in, off screen: a proportional face's width is no count of its letters.
            _probe = ShellTheme.Label("measure", "", UiType.Caption, Color.clear);
            _boldProbe = ShellTheme.Label("measure bold", "", UiType.Caption, Color.clear, bold: true);
            foreach (Label probe in new[] { _probe, _boldProbe })
            {
                probe.style.position = Position.Absolute;
                probe.style.visibility = Visibility.Hidden;
                Add(probe);
            }
        }

        /// <summary>The reading drawn; null for none.</summary>
        public ControllerReading Reading => _reading;

        /// <summary>What is drawn, laid out; null before the view has a size.</summary>
        public ControllerLayout Layout => _layout;

        /// <summary>How much the drawing is scaled from its layout's units, fit and zoom together.</summary>
        public float Scale => _fit * _zoom;

        /// <summary>The state lit, the one it came from, and each table's row lit: -1 for none.</summary>
        public int LitState => _state;

        public int LitFrom => _from;

        public IReadOnlyList<int> LitRows => _rows;

        /// <summary>Draws <paramref name="reading"/>, fitted, where it is another reading than the one drawn.</summary>
        public void Show(ControllerReading reading)
        {
            if (ReferenceEquals(reading, _reading))
                return;
            _reading = reading;
            _layout = null;
            _laidFor = default;
            _zoom = 1f;
            _pan = Vector2.zero;
            Placed();
        }

        /// <summary>Lights the state, the arrow it came by and the tables' rows of <paramref name="now"/>, where they changed.</summary>
        public void Light(ControllerMoment now)
        {
            bool changed = now.State != _state || now.CameFrom != _from || now.Rows.Count != _rows.Length;
            for (int i = 0; !changed && i < _rows.Length; i++)
                changed = _rows[i] != now.Rows[i];
            if (!changed)
                return;
            _state = now.State;
            _from = now.CameFrom;
            if (_rows.Length != now.Rows.Count)
                _rows = new int[now.Rows.Count];
            for (int i = 0; i < _rows.Length; i++)
                _rows[i] = now.Rows[i];
            MarkDirtyRepaint();
        }

        /// <summary>The whole of it in view again (FIT).</summary>
        public void Fit()
        {
            _zoom = 1f;
            _pan = Vector2.zero;
            Place();
        }

        // ---- laying out

        private float Measure(string text, bool bold)
        {
            Label probe = bold ? _boldProbe : _probe;
            float width = string.IsNullOrEmpty(text) ? 0f : probe.MeasureTextSize(text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined).x;
            // Before the panel has styled the probe it measures nothing: a fair guess for the shell's face, wide rather than narrow.
            return width > 0f && !float.IsNaN(width) ? width : text.Length * UiTheme.SizeOf(UiType.Caption) * (bold ? 0.66f : 0.6f);
        }

        private static float LineHeight => UiTheme.SizeOf(UiType.Caption) * 1.45f;

        /// <summary>Laid out once the view has a size and a reading; then fitted and placed.</summary>
        private void Placed()
        {
            float width = float.IsNaN(layout.width) ? 0f : layout.width, height = float.IsNaN(layout.height) ? 0f : layout.height;
            if (_reading == null || width <= 0f || height <= 0f)
            {
                ClearWords();
                MarkDirtyRepaint();
                return;
            }
            if (_layout == null)
            {
                _layout = ControllerLayout.Of(_reading, Measure, LineHeight);
                BuildWords();
            }
            var size = new Vector2(width, height);
            if (size != _laidFor)
            {
                _laidFor = size;
                Vec2 span = _layout.Max - _layout.Min;
                _fit = Mathf.Min((width - 2f * Margin) / Mathf.Max(1f, span.x), (height - 2f * Margin) / Mathf.Max(1f, span.y), 1.5f);
                _fit = Mathf.Max(_fit, 0.05f);
            }
            Place();
        }

        private void ClearWords()
        {
            foreach ((Label label, Vec2 _, bool _) in _words)
                label.RemoveFromHierarchy();
            _words.Clear();
        }

        /// <summary>A label for every word drawn: each box's lines, each arrow's, each table's title and cells.</summary>
        private void BuildWords()
        {
            ClearWords();
            SchematicPalette palette = SchematicPalette.Current;
            float lh = LineHeight;
            foreach (ControllerBox box in _layout.Boxes)
                for (int l = 0; l < box.Lines.Count; l++)
                    Word(box.Lines[l], new Vec2(box.Min.x + ControllerLayout.Pad, box.Min.y + ControllerLayout.Pad + l * lh), l == 0, palette.Text, true);
            foreach (ControllerArrowShape arrow in _layout.Arrows)
                if (arrow.Label != null)
                    Word(arrow.Label, new Vec2(arrow.LabelAt.x, arrow.LabelAt.y - lh), false, palette.TextDim, false);
            foreach (ControllerTableShape table in _layout.Tables)
            {
                Word(table.Title, new Vec2(table.TitleAt.x, table.TitleAt.y - lh), true, palette.TextDim, false);
                foreach (ControllerCell cell in table.Cells)
                    Word(cell.Text, new Vec2(cell.Min.x + ControllerLayout.Pad, cell.Min.y + ControllerLayout.Pad / 2f), cell.Header,
                        cell.Dim ? palette.TextDim : palette.Text, true);
            }
        }

        private void Word(string text, Vec2 at, bool bold, Color colour, bool box)
        {
            Label label = ShellTheme.Label("word", text, UiType.Caption, colour, TextAnchor.UpperLeft, bold);
            label.style.position = Position.Absolute;
            Add(label);
            _words.Add((label, at, box));
        }

        /// <summary>Every word where the drawing now stands, at the size it is now drawn.</summary>
        private void Place()
        {
            if (_layout == null)
                return;
            float scale = Scale;
            float size = Mathf.Max(1f, UiTheme.SizeOf(UiType.Caption) * scale);
            foreach ((Label label, Vec2 at, bool _) in _words)
            {
                Vector2 p = ToView(at);
                label.style.left = p.x;
                label.style.top = p.y;
                label.style.fontSize = size;
            }
            MarkDirtyRepaint();
        }

        /// <summary>Where a point of the layout stands in the view.</summary>
        private Vector2 ToView(Vec2 at)
        {
            float width = float.IsNaN(layout.width) ? 0f : layout.width, height = float.IsNaN(layout.height) ? 0f : layout.height;
            Vec2 middle = new Vec2((_layout.Min.x + _layout.Max.x) / 2f, (_layout.Min.y + _layout.Max.y) / 2f);
            float scale = Scale;
            return new Vector2(width / 2f + (at.x - middle.x) * scale, height / 2f + (at.y - middle.y) * scale) + _pan;
        }

        // ---- the wheel and the pointer

        private void OnWheel(WheelEvent e)
        {
            if (_layout == null)
                return;
            float zoom = Mathf.Clamp(_zoom * (e.delta.y < 0f ? 1.15f : 1f / 1.15f), MostOut, MostIn);
            if (!Mathf.Approximately(zoom, _zoom))
            {
                // About the pointer: what is under it stays under it.
                Vector2 middle = new Vector2(layout.width / 2f, layout.height / 2f);
                Vector2 from = (Vector2)e.localMousePosition - middle - _pan;
                _pan += from - from * (zoom / _zoom);
                _zoom = zoom;
                Place();
            }
            e.StopPropagation();
        }

        private void OnDown(PointerDownEvent e)
        {
            _panning = true;
            _panFrom = e.localPosition;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!_panning)
                return;
            Vector2 now = e.localPosition;
            _pan += now - _panFrom;
            _panFrom = now;
            Place();
        }

        private void OnUp(PointerUpEvent e)
        {
            _panning = false;
            if (this.HasPointerCapture(e.pointerId))
                this.ReleasePointer(e.pointerId);
        }

        // ---- painting

        private void Draw(MeshGenerationContext context)
        {
            SchematicPalette palette = SchematicPalette.Current;
            Painter2D painter = context.painter2D;
            float width = float.IsNaN(layout.width) ? 0f : layout.width, height = float.IsNaN(layout.height) ? 0f : layout.height;

            // Opaque over the drawing it stands for.
            painter.fillColor = palette.Ground;
            painter.BeginPath();
            painter.MoveTo(Vector2.zero);
            painter.LineTo(new Vector2(width, 0f));
            painter.LineTo(new Vector2(width, height));
            painter.LineTo(new Vector2(0f, height));
            painter.ClosePath();
            painter.Fill();
            if (_layout == null || _reading == null)
                return;

            float scale = Scale;
            Color lit = palette.Accent;
            Color litFill = palette.Over(palette.Accent, 0.22f);

            // The tables: each lit row's band, the header's ground, and the lines between the cells.
            for (int t = 0; t < _layout.Tables.Count; t++)
            {
                ControllerTableShape table = _layout.Tables[t];
                int row = t < _rows.Length ? _rows[t] : -1;
                if (row >= 0 && row < table.Rows.Count)
                    Rect(painter, ToView(table.Rows[row].Min), ToView(table.Rows[row].Max), litFill, null, 0f);
                foreach (ControllerCell cell in table.Cells)
                    Rect(painter, ToView(cell.Min), ToView(cell.Max), cell.Header && cell.Min.y < table.Rows[0].Min.y ? palette.Chip : (Color?)null,
                        palette.Outline, 1f);
            }

            // The arrows, the one the machine came by in the accent, over the rest.
            MachineReading machine = _reading.Machine;
            for (int pass = 0; pass < 2 && machine != null; pass++)
                foreach (ControllerArrowShape arrow in _layout.Arrows)
                {
                    MachineArrow a = machine.Arrows[arrow.Arrow];
                    bool came = a.From == _from && a.To == _state && _from != _state;
                    if (came != (pass == 1))
                        continue;
                    Color colour = came ? lit : palette.Outline;
                    painter.strokeColor = colour;
                    painter.lineWidth = came ? 2.5f : 1.5f;
                    painter.BeginPath();
                    Vector2 start = ToView(arrow.Points[0]);
                    painter.MoveTo(start);
                    Vector2 previous = start;
                    for (int i = 1; i < arrow.Points.Count; i++)
                    {
                        Vector2 p = ToView(arrow.Points[i]);
                        if (arrow.Curved)
                        {
                            float mid = (previous.x + p.x) / 2f;
                            painter.BezierCurveTo(new Vector2(mid, previous.y), new Vector2(mid, p.y), p);
                        }
                        else
                            painter.LineTo(p);
                        previous = p;
                    }
                    painter.Stroke();
                    Vector2 end = ToView(arrow.Points[arrow.Points.Count - 1]);
                    Vector2 before = arrow.Curved ? end - new Vector2(1f, 0f) : ToView(arrow.Points[arrow.Points.Count - 2]);
                    Head(painter, end, (end - before).normalized, colour, scale);
                }

            // The states, over the arrows' ends: the start's edge in the accent with the reset's arrow into it, the one the
            // machine is in filled.
            for (int s = 0; s < _layout.Boxes.Count; s++)
            {
                ControllerBox box = _layout.Boxes[s];
                Vector2 min = ToView(box.Min), max = ToView(box.Max);
                bool now = s == _state;
                bool start = machine != null && s == machine.Start;
                Rect(painter, min, max, now ? litFill : palette.Ground, now || start ? lit : palette.Outline, now ? 2.5f : start ? 2f : 1.5f, 6f * scale);
                if (box.Lines.Count > 1)
                {
                    float under = min.y + (ControllerLayout.Pad + LineHeight + 2f) * scale;
                    painter.strokeColor = palette.Outline;
                    painter.lineWidth = 1f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(min.x, under));
                    painter.LineTo(new Vector2(max.x, under));
                    painter.Stroke();
                }
                if (start)
                {
                    Vector2 into = new Vector2(min.x + 10f * scale, min.y);
                    Vector2 from = into + new Vector2(-18f, -18f) * scale;
                    painter.strokeColor = lit;
                    painter.lineWidth = 2f;
                    painter.BeginPath();
                    painter.MoveTo(from);
                    painter.LineTo(into);
                    painter.Stroke();
                    Head(painter, into, (into - from).normalized, lit, scale);
                }
            }
        }

        private static void Rect(Painter2D painter, Vector2 min, Vector2 max, Color? fill, Color? edge, float width, float corner = 0f)
        {
            painter.BeginPath();
            if (corner > 0f)
            {
                corner = Mathf.Min(corner, (max.x - min.x) / 2f, (max.y - min.y) / 2f);
                painter.MoveTo(new Vector2(min.x + corner, min.y));
                painter.LineTo(new Vector2(max.x - corner, min.y));
                painter.ArcTo(new Vector2(max.x, min.y), new Vector2(max.x, min.y + corner), corner);
                painter.LineTo(new Vector2(max.x, max.y - corner));
                painter.ArcTo(new Vector2(max.x, max.y), new Vector2(max.x - corner, max.y), corner);
                painter.LineTo(new Vector2(min.x + corner, max.y));
                painter.ArcTo(new Vector2(min.x, max.y), new Vector2(min.x, max.y - corner), corner);
                painter.LineTo(new Vector2(min.x, min.y + corner));
                painter.ArcTo(new Vector2(min.x, min.y), new Vector2(min.x + corner, min.y), corner);
            }
            else
            {
                painter.MoveTo(min);
                painter.LineTo(new Vector2(max.x, min.y));
                painter.LineTo(max);
                painter.LineTo(new Vector2(min.x, max.y));
            }
            painter.ClosePath();
            if (fill is Color f)
            {
                painter.fillColor = f;
                painter.Fill();
            }
            if (edge is Color e)
            {
                painter.strokeColor = e;
                painter.lineWidth = width;
                painter.Stroke();
            }
        }

        /// <summary>An arrowhead at <paramref name="tip"/>, pointing along <paramref name="direction"/>, sized with the drawing.</summary>
        private static void Head(Painter2D painter, Vector2 tip, Vector2 direction, Color colour, float scale)
        {
            float length = 9f * Mathf.Clamp(scale, 0.6f, 1.6f), half = length / 2f;
            Vector2 side = new Vector2(-direction.y, direction.x);
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(tip - direction * length + side * half);
            painter.LineTo(tip - direction * length - side * half);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
