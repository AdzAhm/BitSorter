using System;
using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What the editing overlay draws (Part 10a), in the stage's units, y down: the selected parts' outlines and
    /// wires, a selection being dragged where it is going and its wires following it, a box being drawn, the
    /// alignment guides, and the part in hand where it would land.
    /// </summary>
    public sealed class CanvasOverlay
    {
        public readonly List<Rect2> Selected = new List<Rect2>();
        public readonly List<Vec2[]> SelectedWires = new List<Vec2[]>();
        public readonly List<Rect2> Ghosts = new List<Rect2>();
        public readonly List<Vec2[]> GhostWires = new List<Vec2[]>();
        public readonly List<(Vec2 From, Vec2 To)> Guides = new List<(Vec2, Vec2)>();
        public Rect2? Box;
        public Rect2? Hand;

        public void Clear()
        {
            Selected.Clear();
            SelectedWires.Clear();
            Ghosts.Clear();
            GhostWires.Clear();
            Guides.Clear();
            Box = null;
            Hand = null;
        }
    }

    /// <summary>
    /// The canvas being edited (Part 10a): what is selected, the part in hand, a drag of the selection with its grid
    /// and its guides, a box -- the state between the gestures (<see cref="CanvasPointer"/>) and the document's
    /// edits (<see cref="WorkbenchSession"/>), and what the overlay draws of it. Unity-free, so a whole edit is a
    /// test under dotnet; the view only turns the mouse into gestures and draws <see cref="Overlay"/>.
    /// </summary>
    /// <remarks>
    /// **A drag changes nothing until it is let go.** While the selection is dragged, its parts are drawn where
    /// they are going, their wires routed to them, and the document is moved once, on release, one step for
    /// undo. Every edit is the document's to refuse; a refusal is kept in <see cref="Refusal"/> for the view to say.
    /// </remarks>
    public sealed class CanvasEditor
    {
        /// <summary>How near, in window pixels, the pointer has to come to a pin or a wire to be on it.</summary>
        public const float ReachPixels = 8f;

        /// <summary>How far a selected part's outline is drawn outside its own, in cells.</summary>
        public const double SelectedRoom = 0.15;

        private readonly WorkbenchSession _session;
        private SchematicLayout _layout;
        private CanvasHit _hit;

        private Dictionary<int, Placement> _start;
        private int _anchor;
        private double _dx, _dy;
        private readonly List<Guide> _guides = new List<Guide>();

        private BoardPoint _boxFrom, _boxTo;
        private bool _boxing;

        private BoardPoint _handAt;
        private bool _handShown, _handFree;

        public CanvasEditor(WorkbenchSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Relaid();
        }

        public CanvasSelection Selection { get; } = new CanvasSelection();

        /// <summary>What the parts strip put in hand, to be placed by the next click; null for nothing.</summary>
        public CatalogEntry InHand { get; private set; }

        /// <summary>Why the last edit was refused, in the document's words; null when it was not.</summary>
        public string Refusal { get; private set; }

        /// <summary>Counts every change to what the overlay draws.</summary>
        public int Version { get; private set; }

        /// <summary>Whether the selection is being dragged.</summary>
        public bool Moving => _start != null;

        private Module Module => _session.Project.Find(_session.Module);

        /// <summary>The document drawn again (<see cref="WorkbenchSession.LayoutVersion"/>, or a replay taking over): found again, and the selection kept to what is still there.</summary>
        public void Relaid()
        {
            _layout = _session.Run.Model.Layout;
            _hit = new CanvasHit(_session.Project, Module);
            Selection.Keep(Module);
            Version++;
        }

        /// <summary>Where a point of the stage is on the board.</summary>
        public BoardPoint ToBoard(Vec2 stage) => _layout.Board?.ToBoard(stage) ?? default;

        /// <summary>What a point of the stage is on, the view <paramref name="zoom"/>ed as it is.</summary>
        public CanvasTarget TargetAt(Vec2 stage, float zoom)
        {
            if (_layout.Board == null)
                return CanvasTarget.Nothing;
            return _hit.At(ToBoard(stage), ReachPixels / Arith.Max(zoom, 1e-3f) / _layout.Board.Cell);
        }

        // -----------------------------------------------------------------
        // The part in hand
        // -----------------------------------------------------------------

        /// <summary>Takes a part from the strip in hand: the next click places it.</summary>
        public void Take(CatalogEntry entry)
        {
            InHand = entry;
            Version++;
        }

        /// <summary>Puts the part in hand down, placing nothing.</summary>
        public void PutDown()
        {
            if (InHand == null)
                return;
            InHand = null;
            _handShown = false;
            Version++;
        }

        /// <summary>The pointer is at <paramref name="stage"/>, or off the canvas: where the part in hand would land, on the grid or, <paramref name="free"/>, exactly.</summary>
        public void Point(Vec2? stage, bool free)
        {
            bool shown = stage.HasValue && InHand != null;
            BoardPoint at = shown ? ToBoard(stage.Value) : default;
            if (shown == _handShown && (!shown || (at.Equals(_handAt) && free == _handFree)))
                return;
            _handShown = shown;
            _handAt = at;
            _handFree = free;
            Version++;
        }

        // -----------------------------------------------------------------
        // Gestures
        // -----------------------------------------------------------------

        /// <summary>Does what a gesture asks: the selection, a drag, a box, a part placed. The edit made, if one was; null for none.</summary>
        public EditResult? Do(CanvasCommand command)
        {
            Refusal = null;
            switch (command.Gesture)
            {
                case CanvasGesture.Select:
                    Selection.Pick(command.Target, command.Extend, command.Toggle);
                    Version++;
                    return null;

                case CanvasGesture.BeginMove:
                    BeginMove(command.From);
                    return null;

                case CanvasGesture.MoveTo:
                    MoveTo(command.At, command.From, command.Free);
                    return null;

                case CanvasGesture.EndMove:
                {
                    if (_start == null)
                        return null;
                    MoveTo(command.At, command.From, command.Free);
                    var to = new Dictionary<int, Placement>();
                    foreach (KeyValuePair<int, Placement> part in _start)
                        to[part.Key] = new Placement(new BoardPoint(part.Value.Position.X + _dx, part.Value.Position.Y + _dy), part.Value.QuarterTurns, part.Value.Flipped);
                    bool moved = _dx != 0 || _dy != 0;
                    _start = null;
                    _guides.Clear();
                    Version++;
                    return moved ? Said(_session.Move(to)) : (EditResult?)null;
                }

                case CanvasGesture.Box:
                    _boxing = true;
                    _boxFrom = command.From;
                    _boxTo = command.At;
                    Version++;
                    return null;

                case CanvasGesture.EndBox:
                {
                    _boxing = false;
                    var parts = new List<int>();
                    var wires = new List<int>();
                    _hit.In(command.From.X, command.From.Y, command.At.X, command.At.Y, parts, wires);
                    Selection.Box(parts, wires, command.Extend);
                    Version++;
                    return null;
                }

                case CanvasGesture.Place:
                {
                    if (InHand == null)
                        return null;
                    BoardPoint at = command.Free ? command.At : CanvasSnap.Snap(command.At);
                    EditResult placed = _session.Place(InHand, new Placement(at));
                    if (placed.Done)
                    {
                        Selection.Clear();
                        Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: placed.Id), extend: false, toggle: false);
                        if (!command.Extend)
                            PutDown();
                    }

                    Version++;
                    return Said(placed);
                }

                case CanvasGesture.Cancel:
                    PutDown();
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>Lets go of a drag or a box without making it: Escape mid-drag.</summary>
        public void Abandon()
        {
            if (_start == null && !_boxing)
                return;
            _start = null;
            _boxing = false;
            _guides.Clear();
            Version++;
        }

        private void BeginMove(BoardPoint from)
        {
            Module module = Module;
            if (module == null || Selection.Parts.Count == 0)
                return;

            _start = new Dictionary<int, Placement>();
            _anchor = -1;
            foreach (int part in Selection.Parts)
            {
                _start[part] = module.Layout.PlacementOf(part);
                PartOutline outline = _hit.OutlineOf(part);
                if (_anchor < 0 || outline != null && from.X >= outline.Left && from.X <= outline.Right && from.Y >= outline.Bottom && from.Y <= outline.Top)
                    _anchor = part;
            }

            _dx = _dy = 0;
            Version++;
        }

        private void MoveTo(BoardPoint at, BoardPoint from, bool free)
        {
            if (_start == null)
                return;

            (double dx, double dy) = CanvasSnap.Offset(_start[_anchor].Position, at.X - from.X, at.Y - from.Y, free);

            var moving = new List<PartOutline>();
            var others = new List<PartOutline>();
            foreach (Part part in Module.Parts)
            {
                PartOutline outline = _hit.OutlineOf(part.Id);
                if (outline != null)
                    (_start.ContainsKey(part.Id) ? moving : others).Add(outline);
            }

            (_dx, _dy) = AlignmentGuides.Align(moving, dx, dy, others, catchUp: free, _guides);
            Version++;
        }

        // -----------------------------------------------------------------
        // Edits of the selection
        // -----------------------------------------------------------------

        /// <summary>Takes the selection away (Delete), parts and wires; refused with nothing selected.</summary>
        public EditResult Delete()
        {
            if (Selection.IsEmpty)
                return Said(EditResult.Refused("Nothing is selected to take away."));
            EditResult result = _session.Remove(new List<int>(Selection.Parts), new List<int>(Selection.Wires));
            if (result.Done)
                Selection.Clear();
            Version++;
            return Said(result);
        }

        /// <summary>Turns the selected parts a quarter at a time, counter-clockwise for more than zero (Q, and Shift+Q back).</summary>
        public EditResult Turn(int quarterTurns) => Said(_session.Turn(new List<int>(Selection.Parts), quarterTurns));

        /// <summary>Mirrors the selected parts left to right (V).</summary>
        public EditResult Mirror() => Said(_session.Turn(new List<int>(Selection.Parts), 0, mirror: true));

        /// <summary>Every part and wire (Ctrl+A).</summary>
        public void SelectAll()
        {
            if (Module == null)
                return;
            Selection.All(Module);
            Version++;
        }

        private EditResult Said(EditResult result)
        {
            Refusal = result.Done ? null : result.Refusal;
            return result;
        }

        // -----------------------------------------------------------------
        // What the overlay draws
        // -----------------------------------------------------------------

        /// <summary>Fills <paramref name="overlay"/> with what the overlay draws now.</summary>
        public void Overlay(CanvasOverlay overlay)
        {
            overlay.Clear();
            SchematicLayout.Frame frame = _layout.Board;
            Module module = Module;
            if (frame == null || module == null)
                return;

            foreach (int part in Selection.Parts)
            {
                PartOutline outline = _hit.OutlineOf(part);
                if (outline != null)
                    overlay.Selected.Add(Stage(frame, outline, SelectedRoom, 0, 0));
            }

            foreach (int wire in Selection.Wires)
            {
                IReadOnlyList<BoardPoint> points = _hit.WireOf(wire);
                if (points != null)
                    overlay.SelectedWires.Add(Stage(frame, points));
            }

            if (_start != null)
            {
                foreach (int part in _start.Keys)
                {
                    PartOutline outline = _hit.OutlineOf(part);
                    if (outline != null)
                        overlay.Ghosts.Add(Stage(frame, outline, SelectedRoom, _dx, _dy));
                }

                // The wires on the parts being dragged, routed to where the parts are going.
                Layout moved = module.Layout;
                foreach (KeyValuePair<int, Placement> part in _start)
                    moved = moved.WithPart(part.Key, new Placement(new BoardPoint(part.Value.Position.X + _dx, part.Value.Position.Y + _dy), part.Value.QuarterTurns, part.Value.Flipped));
                Dictionary<int, IReadOnlyList<BoardPoint>> routes = WireGeometry.Wires(_session.Project, module with { Layout = moved });
                foreach (Wire wire in module.Wires)
                {
                    bool follows = (!wire.From.IsTap && _start.ContainsKey(wire.From.Pin.Part)) || (!wire.To.IsTap && _start.ContainsKey(wire.To.Pin.Part));
                    if (follows && routes.TryGetValue(wire.Id, out IReadOnlyList<BoardPoint> points) && points.Count >= 2)
                        overlay.GhostWires.Add(Stage(frame, points));
                }

                foreach (Guide guide in _guides)
                {
                    BoardPoint a = guide.Vertical ? new BoardPoint(guide.At, guide.From) : new BoardPoint(guide.From, guide.At);
                    BoardPoint b = guide.Vertical ? new BoardPoint(guide.At, guide.To) : new BoardPoint(guide.To, guide.At);
                    overlay.Guides.Add((frame.ToStage(a), frame.ToStage(b)));
                }
            }

            if (_boxing)
            {
                Vec2 a = frame.ToStage(_boxFrom), b = frame.ToStage(_boxTo);
                overlay.Box = Rect2.MinMaxRect(Arith.Min(a.x, b.x), Arith.Min(a.y, b.y), Arith.Max(a.x, b.x), Arith.Max(a.y, b.y));
            }

            if (_handShown && InHand != null)
            {
                BoardPoint at = _handFree ? _handAt : CanvasSnap.Snap(_handAt);
                Part part = PartsCatalog.Make(_session.Project, _session.Module, InHand);
                overlay.Hand = Stage(frame, PartGeometry.Placed(_session.Project, part, new Placement(at)), 0, 0, 0);
            }
        }

        /// <summary>An outline on the stage, grown by <paramref name="room"/> cells and moved by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
        private static Rect2 Stage(SchematicLayout.Frame frame, PartOutline outline, double room, double dx, double dy)
        {
            Vec2 a = frame.ToStage(new BoardPoint(outline.Left - room + dx, outline.Top + room + dy));
            Vec2 b = frame.ToStage(new BoardPoint(outline.Right + room + dx, outline.Bottom - room + dy));
            return Rect2.MinMaxRect(Arith.Min(a.x, b.x), Arith.Min(a.y, b.y), Arith.Max(a.x, b.x), Arith.Max(a.y, b.y));
        }

        private static Vec2[] Stage(SchematicLayout.Frame frame, IReadOnlyList<BoardPoint> points)
        {
            var stage = new Vec2[points.Count];
            for (int i = 0; i < points.Count; i++)
                stage[i] = frame.ToStage(points[i]);
            return stage;
        }
    }
}
