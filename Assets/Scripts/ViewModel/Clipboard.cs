using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What Ctrl+C took (Part 10d): parts as they were, where they stood, and the wires between them with their routes.
    /// Kept by value, so the document can change under it and a paste still makes what was copied.
    /// </summary>
    public sealed class Clip
    {
        internal Clip(IReadOnlyList<(Part Part, Placement At)> parts, IReadOnlyList<(Wire Wire, Route Route)> wires, int pastes)
        {
            Parts = parts;
            Wires = wires;
            Pastes = pastes;
        }

        public IReadOnlyList<(Part Part, Placement At)> Parts { get; }
        public IReadOnlyList<(Wire Wire, Route Route)> Wires { get; }

        /// <summary>How many times it has been pasted: each paste lands a step further on, never on the one before.</summary>
        internal int Pastes { get; set; }

        public bool IsEmpty => Parts.Count == 0;
    }

    /// <summary>
    /// Copy, cut and paste on the canvas (Part 10d), Unity-free. A copy takes the parts selected and every wire whose
    /// two ends are on them -- a wire to a part left behind has nowhere to go -- and a paste makes them again, a cell
    /// right and a cell down for each paste, with new ids and the document's own new names, as one step.
    /// </summary>
    /// <remarks>
    /// **What a paste makes is what was copied.** Each part keeps everything it had but its id and its name; each wire
    /// keeps its bends, moved with its parts, and a tap keeps its place along the wire it taps. An input pasted into
    /// the top module is given 0 from the start, as one placed from the strip is, so the waveform can change it at once.
    /// </remarks>
    public static class Clipboard
    {
        /// <summary>How far each paste lands from what was copied, in cells: right and down.</summary>
        public const double Step = 1;

        /// <summary>What <paramref name="parts"/> of <paramref name="module"/> are, with the wires between them; null with nothing to take.</summary>
        public static Clip Copy(Project project, string module, IReadOnlyCollection<int> parts)
        {
            Module m = project?.Find(module);
            if (m == null || parts == null || parts.Count == 0)
                return null;

            var asked = new HashSet<int>(parts);
            var taken = new HashSet<int>();
            var copied = new List<(Part, Placement)>();
            foreach (Part part in m.Parts)
            {
                if (!asked.Contains(part.Id))
                    continue;
                taken.Add(part.Id);
                copied.Add((part, m.Layout.PlacementOf(part.Id)));
            }

            if (copied.Count == 0)
                return null;

            // A wire goes with them when both its ends do: on a part taken, or a tap on a wire that goes. Taps are on
            // wires made before them, so one pass in the module's order settles every one.
            var wires = new List<(Wire, Route)>();
            var going = new HashSet<int>();
            foreach (Wire wire in m.Wires)
            {
                if (!Goes(wire.From, taken, going) || !Goes(wire.To, taken, going))
                    continue;
                going.Add(wire.Id);
                m.Layout.Routes.TryGetValue(wire.Id, out Route route);
                wires.Add((wire, route));
            }

            return new Clip(copied, wires, 0);
        }

        private static bool Goes(Endpoint end, HashSet<int> parts, HashSet<int> wires) =>
            end.IsTap ? wires.Contains(end.TapOf) : parts.Contains(end.Pin.Part);

        /// <summary>
        /// Makes <paramref name="clip"/> again in <paramref name="module"/>, a step further on than the last paste, as one
        /// step. <paramref name="parts"/> and <paramref name="wires"/> are what it made, to select.
        /// </summary>
        public static EditResult Paste(BoardEditor editor, string module, Clip clip, List<int> parts, List<int> wires)
        {
            if (editor == null || clip == null || clip.IsEmpty)
                return EditResult.Refused("There is nothing to paste: copy something first, with Ctrl+C.");

            int pasting = clip.Pastes + 1;
            double dx = Step * pasting, dy = -Step * pasting;
            var made = new Dictionary<int, int>();
            var madeWires = new Dictionary<int, int>();

            EditResult result = editor.Group(() =>
            {
                foreach ((Part part, Placement at) in clip.Parts)
                {
                    var moved = new Placement(new BoardPoint(at.Position.X + dx, at.Position.Y + dy), at.QuarterTurns, at.Flipped);
                    EditResult added = editor.Add(module, part with { Name = null }, moved);
                    if (!added.Done)
                        return added;
                    made[part.Id] = added.Id;

                    EditResult driven = GiveZero(editor, module, added.Id);
                    if (!driven.Done)
                        return driven;
                }

                foreach ((Wire wire, Route route) in clip.Wires)
                {
                    Endpoint from = Remap(wire.From, made, madeWires), to = Remap(wire.To, made, madeWires);
                    EditResult joined = editor.Connect(module, from, to, Moved(route, dx, dy));
                    if (!joined.Done)
                        return joined;
                    madeWires[wire.Id] = joined.Id;   // a net's name is left behind: two nets of one name would be refused
                }

                return EditResult.Ok();
            });

            if (!result.Done)
                return result;

            clip.Pastes = pasting;
            parts?.Clear();
            parts?.AddRange(made.Values);
            wires?.Clear();
            wires?.AddRange(madeWires.Values);
            return result;
        }

        /// <summary>An input of the top module pasted is given 0 from the start, as the strip gives one.</summary>
        private static EditResult GiveZero(BoardEditor editor, string module, int part)
        {
            Project project = editor.Project;
            if (module != project.Top || !(project.Find(module)?.FindPart(part) is PortPart port) || port.Direction != PortDirection.Input)
                return EditResult.Ok(part);

            Stimulus stimulus = project.Stimulus ?? new Stimulus();
            var tracks = new List<Track>(stimulus.Tracks) { new Track { Port = port.Name, Steps = new[] { new Step(0, LogicVec.FromUInt64(0, port.Width)) } } };
            return editor.SetStimulus(stimulus with { Tracks = tracks });
        }

        private static Endpoint Remap(Endpoint end, Dictionary<int, int> parts, Dictionary<int, int> wires) =>
            end.IsTap ? Endpoint.OnWire(wires[end.TapOf]) : Endpoint.At(parts[end.Pin.Part], end.Pin.Pin);

        private static Route Moved(Route route, double dx, double dy)
        {
            if (route == null)
                return null;
            var bends = new BoardPoint[route.Bends.Count];
            for (int i = 0; i < bends.Length; i++)
                bends[i] = new BoardPoint(route.Bends[i].X + dx, route.Bends[i].Y + dy);
            return route with { Bends = bends };
        }
    }

    /// <summary>How the selected parts are lined up (Part 10d): by their outlines' edges or middles, or spread evenly.</summary>
    public enum ArrangeBy
    {
        Left,
        Right,
        Top,
        Bottom,

        /// <summary>Every middle on the selection's middle column.</summary>
        CentresAcross,

        /// <summary>Every middle on the selection's middle row.</summary>
        CentresDown,

        /// <summary>Equal gaps between them, left to right, the first and the last where they are.</summary>
        SpreadAcross,

        /// <summary>Equal gaps between them, top to bottom, the first and the last where they are.</summary>
        SpreadDown,
    }

    /// <summary>
    /// Align and spread (Part 10d), Unity-free: where each selected part goes so that their outlines line up, or stand
    /// at even gaps -- every new place on the quarter grid. Layout only, as a move is.
    /// </summary>
    public static class Arrange
    {
        /// <summary>
        /// The new place of each of <paramref name="parts"/> in <paramref name="module"/> to line them up <paramref name="by"/>;
        /// empty when there are too few to line up -- two, or three to spread.
        /// </summary>
        public static Dictionary<int, Placement> Of(Project project, string module, IReadOnlyCollection<int> parts, ArrangeBy by)
        {
            var to = new Dictionary<int, Placement>();
            Module m = project?.Find(module);
            if (m == null || parts == null)
                return to;

            var asked = new HashSet<int>(parts);
            var outlines = new List<(int Id, Placement At, PartOutline Outline)>();
            foreach (Part part in m.Parts)
                if (asked.Contains(part.Id))
                    outlines.Add((part.Id, m.Layout.PlacementOf(part.Id), PartGeometry.Placed(project, part, m.Layout.PlacementOf(part.Id))));

            bool spread = by == ArrangeBy.SpreadAcross || by == ArrangeBy.SpreadDown;
            if (outlines.Count < (spread ? 3 : 2))
                return to;

            double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
            foreach ((int _, Placement _, PartOutline o) in outlines)
            {
                left = Math.Min(left, o.Left);
                right = Math.Max(right, o.Right);
                bottom = Math.Min(bottom, o.Bottom);
                top = Math.Max(top, o.Top);
            }

            if (spread)
            {
                bool across = by == ArrangeBy.SpreadAcross;
                outlines.Sort((a, b) => across ? Middle(a.Outline, true).CompareTo(Middle(b.Outline, true)) : Middle(b.Outline, false).CompareTo(Middle(a.Outline, false)));
                double first = Middle(outlines[0].Outline, across), last = Middle(outlines[outlines.Count - 1].Outline, across);
                for (int i = 1; i < outlines.Count - 1; i++)
                {
                    double want = first + (last - first) * i / (outlines.Count - 1);
                    double shift = want - Middle(outlines[i].Outline, across);
                    Move(to, outlines[i].Id, outlines[i].At, across ? shift : 0, across ? 0 : shift);
                }

                return to;
            }

            foreach ((int id, Placement at, PartOutline o) in outlines)
            {
                switch (by)
                {
                    case ArrangeBy.Left: Move(to, id, at, left - o.Left, 0); break;
                    case ArrangeBy.Right: Move(to, id, at, right - o.Right, 0); break;
                    case ArrangeBy.Top: Move(to, id, at, 0, top - o.Top); break;
                    case ArrangeBy.Bottom: Move(to, id, at, 0, bottom - o.Bottom); break;
                    case ArrangeBy.CentresAcross: Move(to, id, at, (left + right) / 2 - Middle(o, true), 0); break;
                    case ArrangeBy.CentresDown: Move(to, id, at, 0, (bottom + top) / 2 - Middle(o, false)); break;
                }
            }

            return to;
        }

        private static double Middle(PartOutline o, bool across) => across ? (o.Left + o.Right) / 2 : (o.Bottom + o.Top) / 2;

        /// <summary>A part moved by <paramref name="dx"/>, <paramref name="dy"/>, landing on the quarter grid; left out when it does not move.</summary>
        private static void Move(Dictionary<int, Placement> to, int id, Placement at, double dx, double dy)
        {
            var moved = new BoardPoint(CanvasSnap.Snap(at.Position.X + dx), CanvasSnap.Snap(at.Position.Y + dy));
            if (Math.Abs(moved.X - at.Position.X) < 1e-9 && Math.Abs(moved.Y - at.Position.Y) < 1e-9)
                return;
            to[id] = new Placement(moved, at.QuarterTurns, at.Flipped);
        }
    }

    /// <summary>
    /// The minimap (Part 10d), Unity-free: the whole drawing small in a corner of the circuit's window, the window's own
    /// rectangle on it, and where a click or a drag on it moves the view to. Shown only while some of the drawing is
    /// out of the window: at the fit, or with an edit's drawing still whole in a window that kept its place, a map of
    /// the window's own contents would only be clutter.
    /// </summary>
    public sealed class Minimap
    {
        /// <summary>The most room it takes in the window, in window units.</summary>
        public const float MaxWidth = 168f, MaxHeight = 104f;

        private Minimap(float scale, Vec2 size, Rect2 window)
        {
            Scale = scale;
            Size = size;
            Window = window;
        }

        /// <summary>Window units per drawing unit.</summary>
        public float Scale { get; }

        /// <summary>How big it is drawn, the drawing in it whole.</summary>
        public Vec2 Size { get; }

        /// <summary>The part of the drawing the window shows, on the map, y down; clipped to the map.</summary>
        public Rect2 Window { get; }

        /// <summary>The map of <paramref name="view"/>; null when the whole drawing is in the window already.</summary>
        public static Minimap Of(SchematicView view)
        {
            if (view == null || view.IsFitted || view.Drawing.x <= 0 || view.Drawing.y <= 0)
                return null;

            // What the window shows, in drawing units: from where its corner is to where its far corner is.
            Vec2 from = (new Vec2(0f, 0f) - view.Offset) / view.Zoom, to = (view.Window - view.Offset) / view.Zoom;
            const float Slack = 0.5f;
            if (from.x <= Slack && from.y <= Slack && to.x >= view.Drawing.x - Slack && to.y >= view.Drawing.y - Slack)
                return null;

            float scale = Arith.Min(MaxWidth / view.Drawing.x, MaxHeight / view.Drawing.y);
            Vec2 size = view.Drawing * scale;
            float x0 = Arith.Clamp(from.x * scale, 0f, size.x), x1 = Arith.Clamp(to.x * scale, 0f, size.x);
            float y0 = Arith.Clamp(from.y * scale, 0f, size.y), y1 = Arith.Clamp(to.y * scale, 0f, size.y);
            return new Minimap(scale, size, Rect2.MinMaxRect(x0, y0, x1, y1));
        }

        /// <summary>A point on the map, in the drawing's units.</summary>
        public Vec2 ToDrawing(Vec2 onMap) => onMap / Scale;

        /// <summary>The view moved so that the window's middle is on the drawing at <paramref name="onMap"/>, a point on the map. True when it moved.</summary>
        public bool CentreOn(SchematicView view, Vec2 onMap)
        {
            Vec2 target = view.Window * 0.5f - ToDrawing(onMap) * view.Zoom;
            return view.Pan(target - view.Offset);
        }
    }
}
