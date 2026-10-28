using System.Collections.Generic;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// A state diagram drawn (Part 15g): each state a circle, round an oval in the order running reached them, the start's with
    /// an arrow in; each way between two states an arrow, bowed apart when both ways are taken, and a loop where a state keeps
    /// itself; each labelled with the inputs that take it -- and in a Mealy machine what the outputs read on it, in a Moore
    /// machine in the state's circle. Drawn with UI Toolkit's own painter, in the style's colours; its words are labels.
    /// </summary>
    /// <remarks>
    /// **Round an oval, evenly along it**, not a circle: the dock is several times wider than it is tall, and sixteen states on
    /// a circle that fits its height would overlap. A circle shrinks only once its neighbours would crowd it.
    /// </remarks>
    public sealed class StateDiagramView : VisualElement
    {
        /// <summary>A state circle's radius with room for it, and the smallest it shrinks to with none.</summary>
        public const float StateRadius = 30f, LeastRadius = 16f;

        // The room outside the oval: beside it for an arrow's label, over and under it for a loop and its label.
        private const float SideRoom = 70f, UpRoom = 54f;

        private StateMachine _machine;
        private readonly List<(Vector2 From, Vector2 To, Vector2 Bend, bool Loop)> _arrows = new List<(Vector2, Vector2, Vector2, bool)>();
        private Vector2[] _centres = new Vector2[0];
        private Vector2 _middle, _startFrom;
        private float _radius = StateRadius;

        public StateDiagramView()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Lay());
        }

        /// <summary>The machine drawn; null for none.</summary>
        public StateMachine Machine
        {
            get => _machine;
            set
            {
                _machine = value;
                Lay();
            }
        }

        /// <summary>Each state's centre as drawn, the start's first: what a test reads.</summary>
        public IReadOnlyList<Vector2> Centres => _centres;

        /// <summary>The states' radius as drawn.</summary>
        public float Radius => _radius;

        /// <summary>Where every state goes, every arrow runs and every label sits, for the size the view has now.</summary>
        private void Lay()
        {
            Clear();
            _arrows.Clear();
            _centres = new Vector2[0];
            StateMachine machine = _machine;
            float width = float.IsNaN(layout.width) ? 0f : layout.width, height = float.IsNaN(layout.height) ? 0f : layout.height;
            SchematicPalette palette = SchematicPalette.Current;
            if (machine == null || machine.States.Count == 0)
            {
                MarkDirtyRepaint();
                return;
            }
            if (width < 4f * StateRadius || height < 4f * StateRadius)
            {
                Label tooSmall = ShellTheme.Label("too small", "Drag the splitter up to see the diagram.", UiType.Caption, palette.TextDim, TextAnchor.MiddleCenter);
                ShellTheme.Place(tooSmall, 0f, 0f, width, height);
                Add(tooSmall);
                MarkDirtyRepaint();
                return;
            }

            int n = machine.States.Count;
            _middle = new Vector2(width / 2f, height / 2f);
            // In a short dock the room over and under the oval goes before the oval does: flattened to a line, round it twice
            // over, every other state would stand on another (the 15g-6 review). A loop's label there may be cut off.
            float down = height / 2f - UpRoom - StateRadius;
            if (down < StateRadius)
                down = Mathf.Max(0f, height / 2f - StateRadius - 6f);
            _centres = Oval(_middle, Mathf.Max(0f, width / 2f - SideRoom - StateRadius), down, n);
            float closest = float.MaxValue;
            for (int i = 0; i < n && n > 1; i++)
                closest = Mathf.Min(closest, Vector2.Distance(_centres[i], _centres[(i + 1) % n]));
            _radius = Mathf.Clamp(closest / 2f - 8f, LeastRadius, StateRadius);

            // The states, each its code -- and in a Moore machine its outputs under it.
            for (int i = 0; i < n; i++)
            {
                string says = machine.States[i];
                if (machine.Moore && machine.OutputIn(i) is string output)
                    says += "\n" + output;
                Label label = ShellTheme.Label("state " + i, says, UiType.Caption, palette.Text, TextAnchor.MiddleCenter);
                ShellTheme.Place(label, _centres[i].x - _radius, _centres[i].y - _radius, 2f * _radius, 2f * _radius);
                Add(label);
            }
            // The arrow in comes from up and to the left, clear of a loop, which stands straight out.
            _startFrom = _centres[0] + new Vector2(-0.7071f, -0.7071f) * (_radius + 30f);

            // Each pair of states one arrow, labelled with every input that takes it.
            var labels = new Dictionary<(int, int), List<string>>();
            foreach (StateTransition t in machine.Transitions)
            {
                if (!labels.TryGetValue((t.From, t.To), out List<string> list))
                    labels[(t.From, t.To)] = list = new List<string>();
                list.Add(machine.Moore ? t.Input : t.Input + "/" + t.Output);
            }

            foreach (KeyValuePair<(int From, int To), List<string>> pair in labels)
            {
                (int from, int to) = pair.Key;
                Vector2 a = _centres[from], b = _centres[to];
                Vector2 labelAt;
                if (from == to)
                {
                    Vector2 outward = Outward(a);
                    _arrows.Add((a, a, a + outward * (_radius + 14f), true));
                    labelAt = a + outward * (_radius + 40f);
                }
                else
                {
                    // Bowed to its right, so the way back, bowed to its own right, keeps clear of it.
                    Vector2 along = (b - a).normalized, right = new Vector2(-along.y, along.x);
                    bool both = labels.ContainsKey((to, from));
                    Vector2 bend = (a + b) / 2f + (both ? right * 26f : Vector2.zero);
                    _arrows.Add((a, b, bend, false));
                    labelAt = bend + right * (both ? 14f : 12f);
                }
                // A machine with no inputs but the clock has nothing to say on its arrows.
                if (machine.InputBits.Count == 0 && machine.Moore)
                    continue;
                Label label = ShellTheme.Label("arrow", string.Join(", ", pair.Value), UiType.Micro, palette.TextDim, TextAnchor.MiddleCenter);
                ShellTheme.Place(label, labelAt.x - 60f, labelAt.y - 10f, 120f, 20f);
                Add(label);
            }

            MarkDirtyRepaint();
        }

        /// <summary>Away from the oval's middle, from a state on it: up, for a state alone.</summary>
        private Vector2 Outward(Vector2 state) => _centres.Length < 2 || state == _middle ? new Vector2(0f, -1f) : (state - _middle).normalized;

        /// <summary>
        /// <paramref name="n"/> points round an oval, the first at its top and on clockwise, evenly along its length -- two side by
        /// side, the first on the left, as an oval wider than it is tall has room for them.
        /// </summary>
        private static Vector2[] Oval(Vector2 middle, float across, float down, int n)
        {
            var at = new Vector2[n];
            const int steps = 720;
            var points = new Vector2[steps + 1];
            var lengths = new float[steps + 1];
            float first = n == 2 ? Mathf.PI : -Mathf.PI / 2f;
            for (int i = 0; i <= steps; i++)
            {
                float angle = first + 2f * Mathf.PI * i / steps;
                points[i] = middle + new Vector2(across * Mathf.Cos(angle), down * Mathf.Sin(angle));
                if (i > 0)
                    lengths[i] = lengths[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
            float total = lengths[steps];
            int j = 0;
            for (int k = 0; k < n; k++)
            {
                if (n == 1 || total <= 0f)
                {
                    at[k] = middle;
                    continue;
                }
                float want = total * k / n;
                while (j < steps - 1 && lengths[j + 1] < want)
                    j++;
                float span = lengths[j + 1] - lengths[j];
                at[k] = Vector2.Lerp(points[j], points[j + 1], span > 0f ? (want - lengths[j]) / span : 0f);
            }
            return at;
        }

        private void Draw(MeshGenerationContext context)
        {
            StateMachine machine = _machine;
            if (machine == null || _centres.Length != machine.States.Count || _centres.Length == 0)
                return;

            SchematicPalette palette = SchematicPalette.Current;
            Painter2D painter = context.painter2D;
            painter.lineWidth = 1.5f;
            painter.strokeColor = palette.Outline;

            foreach ((Vector2 from, Vector2 to, Vector2 bend, bool loop) in _arrows)
            {
                if (loop)
                {
                    // A loop: a circle beside the state, out and back to its edge, an arrowhead where it comes back.
                    Vector2 outward = (bend - from).normalized;
                    painter.BeginPath();
                    painter.Arc(bend, 14f, 0f, 360f);
                    painter.Stroke();
                    Vector2 tangent = new Vector2(-outward.y, outward.x);
                    Head(painter, from + outward * _radius + tangent * 6f, -outward, palette.Outline);
                    continue;
                }

                // From edge to edge, through the bend, the head on the target's edge.
                Vector2 start = from + (bend - from).normalized * _radius;
                Vector2 end = to + (bend - to).normalized * _radius;
                painter.BeginPath();
                painter.MoveTo(start);
                painter.QuadraticCurveTo(bend, end);
                painter.Stroke();
                Head(painter, end, (end - bend).normalized, palette.Outline);
            }

            // The arrow into the start.
            Vector2 into = _centres[0] + (_startFrom - _centres[0]).normalized * _radius;
            painter.strokeColor = palette.Accent;
            painter.BeginPath();
            painter.MoveTo(_startFrom);
            painter.LineTo(into);
            painter.Stroke();
            Head(painter, into, (into - _startFrom).normalized, palette.Accent);

            // The states, over the arrows' ends: the start's in the accent.
            for (int i = 0; i < _centres.Length; i++)
            {
                painter.fillColor = palette.Ground;
                painter.strokeColor = i == 0 ? palette.Accent : palette.Outline;
                painter.lineWidth = i == 0 ? 2.5f : 1.5f;
                painter.BeginPath();
                painter.Arc(_centres[i], _radius, 0f, 360f);
                painter.Fill();
                painter.Stroke();

                // A Moore machine's state over a bar, its outputs under it, as the textbook draws one.
                if (machine.Moore && machine.OutputIn(i) != null)
                {
                    painter.strokeColor = palette.Outline;
                    painter.lineWidth = 1f;
                    painter.BeginPath();
                    painter.MoveTo(_centres[i] + new Vector2(-0.7f * _radius, 0f));
                    painter.LineTo(_centres[i] + new Vector2(0.7f * _radius, 0f));
                    painter.Stroke();
                }
            }
        }

        /// <summary>An arrowhead at <paramref name="tip"/>, pointing along <paramref name="direction"/>.</summary>
        private static void Head(Painter2D painter, Vector2 tip, Vector2 direction, Color colour)
        {
            Vector2 side = new Vector2(-direction.y, direction.x);
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(tip - direction * 9f + side * 4.5f);
            painter.LineTo(tip - direction * 9f - side * 4.5f);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
