using System;
using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What the editing overlay draws (Part 10a), in the stage's units, y down: the selected parts' outlines and
    /// wires, a selection being dragged where it is going and its wires following it, a box being drawn, the
    /// alignment guides, the part in hand where it would land, and a wire being drawn (Part 10b).
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

        /// <summary>The wire being drawn, as it would run now; null for none.</summary>
        public Vec2[] Draft;

        /// <summary>Where it starts, and the pin or the junction it would end on -- null when the pointer is on neither.</summary>
        public Vec2? DraftStart, DraftEnd;

        /// <summary>Whether the document would refuse the wire ending there: the end is drawn as one it cannot take.</summary>
        public bool DraftRefused;

        /// <summary>The corners of every selected wire, where a drag moves one (Part 10b-2).</summary>
        public readonly List<Vec2> Handles = new List<Vec2>();

        /// <summary>
        /// The timing path picked in the TIMING tab (Part 14b), as this drawing has it (<see cref="TimingLight"/>): the wires
        /// it takes, the parts it passes through, and the ports it starts and ends at.
        /// </summary>
        public readonly List<StageRoute> PathWires = new List<StageRoute>();

        /// <inheritdoc cref="PathWires"/>
        public readonly List<Rect2> PathParts = new List<Rect2>();

        /// <inheritdoc cref="PathWires"/>
        public readonly List<Rect2> PathPorts = new List<Rect2>();

        public void Clear()
        {
            Selected.Clear();
            SelectedWires.Clear();
            Ghosts.Clear();
            GhostWires.Clear();
            Guides.Clear();
            Box = null;
            Hand = null;
            Draft = null;
            DraftStart = DraftEnd = null;
            DraftRefused = false;
            Handles.Clear();
            PathWires.Clear();
            PathParts.Clear();
            PathPorts.Clear();
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

        // The wire being drawn: where the pointer is, what it is on, and whether the document would take it there.
        private WireDraft _draft;
        private BoardPoint _draftAt;
        private bool _draftFree, _draftShown;
        private CanvasTarget _draftOver = CanvasTarget.Nothing;
        private CanvasTarget _checkedOver = CanvasTarget.Nothing;
        private string _draftWhyNot;

        // What the wire being drawn keeps clear of, as the document stands: its wires, by net, and its parts (Part 10e).
        private WireObstacles _draftAround;
        private Dictionary<int, int> _draftNets;
        private readonly HashSet<int> _draftOwn = new HashSet<int>();

        // A selected wire being reshaped: which, its points as it was drawn when taken hold of, which piece or
        // corner, and the points it would have now.
        private int _reshaping = -1;
        private IReadOnlyList<BoardPoint> _reshapeFrom;
        private CanvasTarget _reshapeBy;
        private List<BoardPoint> _reshaped;

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

        /// <summary>
        /// What the last delete left behind that the player may not expect, or null: a module whose last block went, which
        /// stays in the project as the player's work, and how to take it out (the 14g report: code1 stayed in CODE and PARTS).
        /// </summary>
        public string Note { get; private set; }

        /// <summary>Counts every change to what the overlay draws.</summary>
        public int Version { get; private set; }

        /// <summary>Whether the selection is being dragged.</summary>
        public bool Moving => _start != null;

        /// <summary>Whether a wire is being drawn (Part 10b).</summary>
        public bool Drafting => _draft != null;

        /// <summary>Whether a selected wire is being reshaped (Part 10b-2).</summary>
        public bool Reshaping => _reshaping >= 0;

        /// <summary>The wire being drawn; null for none.</summary>
        public WireDraft Draft => _draft;

        /// <summary>Why the document would refuse the wire being drawn ending where the pointer is; null when it would take it, or the pointer is on nothing it could end on.</summary>
        public string DraftRefusal => _draftWhyNot;

        private Module Module => _session.Project.Find(_session.Module);

        /// <summary>The document drawn again (<see cref="WorkbenchSession.LayoutVersion"/>, or a replay taking over): found again, and the selection kept to what is still there.</summary>
        public void Relaid()
        {
            _layout = _session.Run.Model.Layout;
            _hit = new CanvasHit(_session.Project, Module);
            Selection.Keep(Module);

            // A wire being drawn from something an undo took away has nowhere to start; and what it may end on is asked again.
            if (_draft != null && !StillThere(_draft.From))
                _draft = null;

            // A block in hand of a module an edit took away -- deleted from PARTS, or a MAKE BLOCK undone -- has nothing to
            // place, and every click would be refused (the 14g review).
            if (InHand != null && InHand.Kind == CatalogKind.Block && _session.Project.Find(InHand.Module) == null)
                PutDown();
            _draftAround = null;
            _checkedOver = CanvasTarget.Nothing;
            _draftWhyNot = null;
            Version++;
        }

        /// <summary>Where a point of the stage is on the board.</summary>
        public BoardPoint ToBoard(Vec2 stage) => _layout.Board?.ToBoard(stage) ?? default;

        /// <summary>What a point of the stage is on, the view <paramref name="zoom"/>ed as it is.</summary>
        public CanvasTarget TargetAt(Vec2 stage, float zoom)
        {
            if (_layout.Board == null)
                return CanvasTarget.Nothing;
            BoardPoint at = ToBoard(stage);
            double reach = ReachPixels / Arith.Max(zoom, 1e-3f) / _layout.Board.Cell;

            // A selected wire's corners before anything: they are drawn over everything, and only while it is selected.
            double best = reach * reach;
            CanvasTarget corner = CanvasTarget.Nothing;
            foreach (int wire in Selection.Wires)
            {
                IReadOnlyList<BoardPoint> points = _hit.WireOf(wire);
                if (points == null)
                    continue;
                double length = WireGeometry.Length(points), walked = 0;
                for (int i = 1; i < points.Count; i++)
                {
                    walked += Distance(points[i - 1], points[i]);
                    if (i == points.Count - 1)
                        break;
                    double squared = (points[i].X - at.X) * (points[i].X - at.X) + (points[i].Y - at.Y) * (points[i].Y - at.Y);
                    if (squared <= best)
                    {
                        best = squared;
                        corner = new CanvasTarget(CanvasTargetKind.Bend, wire: wire, segment: i, along: length > 0 ? walked / length : 0);
                    }
                }
            }

            return corner.Kind != CanvasTargetKind.Nothing ? corner : _hit.At(at, reach);
        }

        private static double Distance(BoardPoint a, BoardPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        // -----------------------------------------------------------------
        // The part in hand
        // -----------------------------------------------------------------

        /// <summary>Takes a part from the strip in hand: the next click places it.</summary>
        public void Take(CatalogEntry entry)
        {
            InHand = entry;
            _draft = null;
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

        /// <summary>
        /// The pointer is at <paramref name="stage"/>, or off the canvas, the view <paramref name="zoom"/>ed as it is:
        /// where the part in hand would land, on the grid or, <paramref name="free"/>, exactly -- and where the wire
        /// being drawn would run to, and what it would end on.
        /// </summary>
        public void Point(Vec2? stage, bool free, float zoom = 1f)
        {
            bool shown = stage.HasValue && InHand != null;
            BoardPoint at = shown ? ToBoard(stage.Value) : default;
            if (shown != _handShown || (shown && (!at.Equals(_handAt) || free != _handFree)))
            {
                _handShown = shown;
                _handAt = at;
                _handFree = free;
                Version++;
            }

            if (_draft == null)
                return;

            bool drafted = stage.HasValue;
            BoardPoint to = drafted ? ToBoard(stage.Value) : _draftAt;
            CanvasTarget over = drafted ? TargetAt(stage.Value, zoom) : CanvasTarget.Nothing;
            if (over.Kind == CanvasTargetKind.Bend)
                over = new CanvasTarget(CanvasTargetKind.Wire, wire: over.Wire, along: over.Along);
            if (over.Kind != CanvasTargetKind.Pin && over.Kind != CanvasTargetKind.Wire)
                over = CanvasTarget.Nothing;
            if (drafted == _draftShown && to.Equals(_draftAt) && free == _draftFree && over.Equals(_draftOver))
                return;

            _draftShown = drafted;
            _draftAt = to;
            _draftFree = free;
            _draftOver = over;
            Check(over);
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
                    AbandonWire();
                    return null;

                case CanvasGesture.BeginWire:
                    BeginWire(command.Target, command.At);
                    return null;

                case CanvasGesture.WireBend:
                    if (_draft == null)
                        return null;
                    _draft.Bend(command.Free ? command.At : CanvasSnap.Snap(command.At));
                    _draftAt = command.At;
                    Version++;
                    return null;

                case CanvasGesture.EndWire:
                    return EndWire(command.Target.Kind == CanvasTargetKind.Bend
                        ? new CanvasTarget(CanvasTargetKind.Wire, wire: command.Target.Wire, along: command.Target.Along)
                        : command.Target, command.At);

                case CanvasGesture.BeginReshape:
                    BeginReshape(command.Target);
                    return null;

                case CanvasGesture.Menu:
                {
                    // A right-click on something not selected selects it alone, as a click would; on the selection, or on
                    // nothing, it keeps what is selected for the menu to act on.
                    CanvasTarget on = command.Target;
                    if (on.Kind == CanvasTargetKind.Pin)
                        on = new CanvasTarget(CanvasTargetKind.Part, part: on.Part);
                    else if (on.Kind == CanvasTargetKind.Bend)
                        on = new CanvasTarget(CanvasTargetKind.Wire, wire: on.Wire);
                    if (on.Kind != CanvasTargetKind.Nothing && !Selection.Has(on))
                    {
                        Selection.Pick(on, extend: false, toggle: false);
                        Version++;
                    }

                    return null;
                }

                case CanvasGesture.Inspect:
                    // What is inspected is selected, alone, and any wire the first click began is let go.
                    AbandonWire();
                    Selection.Clear();
                    Selection.Pick(command.Target.Kind == CanvasTargetKind.Pin || command.Target.Kind == CanvasTargetKind.Part
                        ? new CanvasTarget(CanvasTargetKind.Part, part: command.Target.Part)
                        : new CanvasTarget(CanvasTargetKind.Wire, wire: command.Target.Wire), extend: false, toggle: false);
                    Version++;
                    return null;

                case CanvasGesture.ReshapeTo:
                    ReshapeTo(command.At, command.From, command.Free);
                    return null;

                case CanvasGesture.EndReshape:
                {
                    if (_reshaping < 0)
                        return null;
                    ReshapeTo(command.At, command.From, command.Free);
                    int wire = _reshaping;
                    List<BoardPoint> shaped = _reshaped;
                    bool changed = shaped != null && !SamePoints(shaped, _reshapeFrom);
                    _reshaping = -1;
                    _reshaped = null;
                    Version++;
                    if (!changed)
                        return null;

                    Module module = Module;
                    double tapAt = module.Layout.Routes.TryGetValue(wire, out Route was) ? was.TapAt : 0.5;
                    return Said(_session.Reroute(wire, new Route { Bends = WireShape.Bends(shaped), TapAt = tapAt }));
                }

                default:
                    return null;
            }
        }

        // -----------------------------------------------------------------
        // Stepping inside a block (Part 12a)
        // -----------------------------------------------------------------

        /// <summary>
        /// Steps inside <paramref name="target"/>, a double-click's, when it is a block (<see cref="WorkbenchSession.StepInto"/>);
        /// false for anything else, which a double-click inspects.
        /// </summary>
        public bool StepInto(CanvasTarget target) => target.Kind == CanvasTargetKind.Part && Stepped(_session.StepInto(target.Part));

        /// <summary>Steps <paramref name="levels"/> out (<see cref="WorkbenchSession.StepOut"/>); false at the top.</summary>
        public bool StepOut(int levels = 1) => Stepped(_session.StepOut(levels));

        /// <summary>Steps to an instance by its path in the design (<see cref="WorkbenchSession.StepToScope"/>).</summary>
        public bool StepToScope(string scope) => Stepped(_session.StepToScope(scope));

        /// <summary>
        /// After a step, what was selected, in hand, drawn or dragged is let go: it was the module stepped out of, and a
        /// part's id in one module is another part's in the next.
        /// </summary>
        private bool Stepped(bool stepped)
        {
            if (!stepped)
                return false;
            Abandon();
            AbandonWire();
            PutDown();
            Selection.Clear();
            Refusal = null;
            Relaid();
            return true;
        }

        /// <summary>Lets go of a drag or a box without making it: Escape mid-drag.</summary>
        public void Abandon()
        {
            if (_start == null && !_boxing && _reshaping < 0)
                return;
            _start = null;
            _boxing = false;
            _reshaping = -1;
            _reshaped = null;
            _guides.Clear();
            Version++;
        }

        // -----------------------------------------------------------------
        // A selected wire reshaped (Part 10b-2)
        // -----------------------------------------------------------------

        private void BeginReshape(CanvasTarget target)
        {
            if ((target.Kind != CanvasTargetKind.Wire && target.Kind != CanvasTargetKind.Bend) || !Selection.HasWire(target.Wire))
                return;
            IReadOnlyList<BoardPoint> points = _hit.WireOf(target.Wire);
            if (points == null || points.Count < 2)
                return;
            _reshaping = target.Wire;
            _reshapeFrom = points;
            _reshapeBy = target;
            _reshaped = new List<BoardPoint>(points);
            Version++;
        }

        /// <summary>The piece or corner taken hold of at <paramref name="from"/>, dragged to <paramref name="at"/>: landing on the quarter grid, or exactly with <paramref name="free"/>.</summary>
        private void ReshapeTo(BoardPoint at, BoardPoint from, bool free)
        {
            if (_reshaping < 0)
                return;

            double dx = at.X - from.X, dy = at.Y - from.Y;
            if (_reshapeBy.Kind == CanvasTargetKind.Bend)
            {
                BoardPoint corner = _reshapeFrom[_reshapeBy.Segment];
                BoardPoint to = new BoardPoint(corner.X + dx, corner.Y + dy);
                if (!free)
                    to = CanvasSnap.Snap(to);
                _reshaped = WireShape.MoveCorner(_reshapeFrom, _reshapeBy.Segment, to.X - corner.X, to.Y - corner.Y);
            }
            else
            {
                // The piece lands on the grid: its own line, moved, on a quarter cell.
                BoardPoint a = _reshapeFrom[_reshapeBy.Segment];
                double x = a.X + dx, y = a.Y + dy;
                if (!free)
                {
                    x = CanvasSnap.Snap(x);
                    y = CanvasSnap.Snap(y);
                }

                _reshaped = WireShape.MoveSegment(_reshapeFrom, _reshapeBy.Segment, x - a.X, y - a.Y);
            }

            Version++;
        }

        private static bool SamePoints(IReadOnlyList<BoardPoint> a, IReadOnlyList<BoardPoint> b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (Math.Abs(a[i].X - b[i].X) > 1e-9 || Math.Abs(a[i].Y - b[i].Y) > 1e-9)
                    return false;
            return true;
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
        // A wire being drawn (Part 10b)
        // -----------------------------------------------------------------

        /// <summary>Lets go of the wire being drawn, making nothing: Escape, or the right button.</summary>
        public void AbandonWire()
        {
            if (_draft == null)
                return;
            _draft = null;
            _draftOver = _checkedOver = CanvasTarget.Nothing;
            _draftWhyNot = null;
            Version++;
        }

        private void BeginWire(CanvasTarget target, BoardPoint at)
        {
            if (!TryEnd(target, out Endpoint from, out BoardPoint start, out Facing? facing, out double? tapAt))
                return;
            PutDown();
            _draft = new WireDraft(from, start, facing, tapAt ?? 0.5, from.IsTap ? _hit.WireOf(from.TapOf) : null);
            _draftAround = null;
            _draftAt = at;
            _draftShown = true;
            _draftOver = _checkedOver = CanvasTarget.Nothing;
            _draftWhyNot = null;
            Version++;
        }

        /// <summary>
        /// The wire being drawn ends on <paramref name="target"/> and is made, one step for undo, and selected. Back on
        /// the end it started from it is not ended -- a short drag that came back -- and on a wire it started from it
        /// bends instead; a refusal leaves it being drawn, and says why.
        /// </summary>
        private EditResult? EndWire(CanvasTarget target, BoardPoint at)
        {
            if (_draft == null || !TryEnd(target, out Endpoint to, out BoardPoint _, out Facing? _, out double? tapAt))
                return null;
            if (to.IsTap && _draft.From.IsTap && to.TapOf == _draft.From.TapOf)
            {
                // Two taps on one wire are one end to the document; to the student it is the wire, still, and a bend.
                _draft.Bend(CanvasSnap.Snap(at));
                Version++;
                return null;
            }

            if (to.Equals(_draft.From))
                return null;

            string why = WhyNot(to, tapAt);
            if (why != null)
            {
                Version++;
                return Said(EditResult.Refused(why));
            }

            EditResult made = _session.Connect(_draft.From, to, _draft.RouteTo(tapAt));
            if (made.Done)
            {
                _draft = null;
                _draftOver = _checkedOver = CanvasTarget.Nothing;
                _draftWhyNot = null;
                Selection.Clear();
                Selection.Pick(new CanvasTarget(CanvasTargetKind.Wire, wire: made.Id), extend: false, toggle: false);
            }

            Version++;
            return Said(made);
        }

        /// <summary>
        /// Where a wire would start or end on <paramref name="target"/>: a pin as it is placed and the way it faces, or a
        /// tap on a wire, put on the grid, and how far along that wire it is. False for anything else.
        /// </summary>
        /// <summary>The document's wires, by net, and its parts, made once for the wire being drawn and again after an edit.</summary>
        private WireObstacles DraftAround()
        {
            if (_draftAround == null)
            {
                Module module = Module;
                _draftNets = WireGeometry.WireNets(module);
                _draftAround = WireGeometry.Obstacles(_session.Project, module, WireGeometry.Wires(_session.Project, module), _draftNets);
            }

            return _draftAround;
        }

        /// <summary>The net an end is part of (<see cref="WireGeometry.WireNets"/>): a tapped wire's, or a pin's wires'; -1 for a pin no wire meets yet.</summary>
        private int NetOf(Endpoint end)
        {
            if (_draftNets == null)
                return -1;
            if (end.IsTap)
                return _draftNets.TryGetValue(end.TapOf, out int tapped) ? tapped : -1;
            foreach (Wire wire in Module.Wires)
                if ((!wire.From.IsTap && wire.From.Pin.Equals(end.Pin)) || (!wire.To.IsTap && wire.To.Pin.Equals(end.Pin)))
                    return _draftNets.TryGetValue(wire.Id, out int net) ? net : -1;
            return -1;
        }

        private bool TryEnd(CanvasTarget target, out Endpoint end, out BoardPoint at, out Facing? facing, out double? tapAt)
        {
            end = default;
            at = default;
            facing = null;
            tapAt = null;
            switch (target.Kind)
            {
                case CanvasTargetKind.Pin:
                {
                    PartOutline outline = _hit.OutlineOf(target.Part);
                    if (outline == null || !outline.TryPin(target.Pin, out PinSpot spot))
                        return false;
                    end = Endpoint.At(target.Part, target.Pin);
                    at = spot.At;
                    facing = spot.Facing;
                    return true;
                }

                case CanvasTargetKind.Wire:
                {
                    IReadOnlyList<BoardPoint> points = _hit.WireOf(target.Wire);
                    if (points == null)
                        return false;
                    at = WireDraft.TapPoint(points, target.Along, out double along);
                    end = Endpoint.OnWire(target.Wire);
                    tapAt = along;
                    if (_draft != null)
                        facing = WireGeometry.TapFacing(points, along, at, _draft.Last);
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>Why the document would refuse the wire being drawn ending at <paramref name="to"/>; null when it would take it.</summary>
        private string WhyNot(Endpoint to, double? tapAt)
        {
            if (_draft.From.IsTap && to.IsTap)
                return "A wire drawn out of a wire ends on a pin: to join two wires, draw from a pin onto one of them.";

            // Asked of the document itself, on a copy, so the words are the ones the edit would be refused with.
            var trial = new BoardEditor(_session.Project);
            EditResult tried = trial.Connect(_session.Module, _draft.From, to, _draft.RouteTo(tapAt));
            return tried.Done ? null : tried.Refusal;
        }

        /// <summary>Whether the document would take the wire ending on what the pointer is over, asked once each time it moves onto something new.</summary>
        private void Check(CanvasTarget over)
        {
            if (over.Kind == CanvasTargetKind.Nothing)
            {
                _checkedOver = over;
                _draftWhyNot = null;
                return;
            }

            if (over.Kind == _checkedOver.Kind && over.Part == _checkedOver.Part && over.Pin == _checkedOver.Pin && over.Wire == _checkedOver.Wire)
                return;
            _checkedOver = over;
            _draftWhyNot = null;
            if (TryEnd(over, out Endpoint to, out BoardPoint _, out Facing? _, out double? tapAt) && !to.Equals(_draft.From)
                && !(to.IsTap && _draft.From.IsTap && to.TapOf == _draft.From.TapOf))
                _draftWhyNot = WhyNot(to, tapAt);
        }

        /// <summary>Whether what an end is on is still in the document.</summary>
        private bool StillThere(Endpoint end)
        {
            Module module = Module;
            if (module == null)
                return false;
            return end.IsTap ? module.FindWire(end.TapOf) != null : module.FindPart(end.Pin.Part) != null;
        }

        // -----------------------------------------------------------------
        // Edits of the selection
        // -----------------------------------------------------------------

        /// <summary>Takes the selection away (Delete), parts and wires; refused with nothing selected.</summary>
        public EditResult Delete()
        {
            Note = null;
            if (Selection.IsEmpty)
                return Said(EditResult.Refused("Nothing is selected to take away."));

            // The modules of the blocks going, to say which of them is left with no block at all.
            var modules = new List<string>();
            Module shown = Module;
            foreach (int part in Selection.Parts)
                if (shown?.FindPart(part) is BlockPart block && !modules.Contains(block.Module))
                    modules.Add(block.Module);

            EditResult result = _session.Remove(new List<int>(Selection.Parts), new List<int>(Selection.Wires));
            if (result.Done)
            {
                Selection.Clear();
                var left = new List<string>();
                bool written = false;
                foreach (string name in modules)
                    if (_session.Project.Find(name) is Module kept && _session.WhyNotRemoveModule(name) == null)
                    {
                        left.Add(name);
                        written |= kept.IsWritten;
                    }

                if (left.Count == 1)
                    Note = $"{left[0]} stays in the project, as its {(written ? "code" : "drawing")} does: right-click it in PARTS to delete it.";
                else if (left.Count > 1)
                    Note = $"{string.Join(" and ", left)} stay in the project, as their work does: right-click one in PARTS to delete it.";
            }

            Version++;
            return Said(result);
        }

        /// <summary>Turns the selected parts a quarter at a time, counter-clockwise for more than zero (Q, and Shift+Q back).</summary>
        public EditResult Turn(int quarterTurns) => Said(_session.Turn(new List<int>(Selection.Parts), quarterTurns));

        /// <summary>Mirrors the selected parts left to right (V).</summary>
        public EditResult Mirror() => Said(_session.Turn(new List<int>(Selection.Parts), 0, mirror: true));

        // -----------------------------------------------------------------
        // Copy, paste and arrange (Part 10d)
        // -----------------------------------------------------------------

        /// <summary>The parts selected, and the wires between them, taken for a paste (Ctrl+C); null, and a refusal said, with no part selected.</summary>
        public Clip Copy()
        {
            Refusal = null;
            Clip clip = Clipboard.Copy(_session.Project, _session.Module, new List<int>(Selection.Parts));
            if (clip == null)
                Refusal = "Select a part to copy: a wire goes with the parts at both its ends.";
            return clip;
        }

        /// <summary>The selection copied and taken away (Ctrl+X): the copy, or null when there was nothing to take.</summary>
        public Clip Cut()
        {
            Clip clip = Copy();
            if (clip == null)
                return null;
            return Delete().Done ? clip : null;
        }

        /// <summary><paramref name="clip"/> made again a step on (Ctrl+V), one step for undo, and what it made selected.</summary>
        public EditResult Paste(Clip clip)
        {
            var parts = new List<int>();
            var wires = new List<int>();
            EditResult pasted = _session.Paste(clip, parts, wires);
            if (pasted.Done)
                Selection.Box(parts, wires, extend: false);
            Version++;
            return Said(pasted);
        }

        /// <summary>The selected parts lined up or spread <paramref name="by"/>: layout only, one step.</summary>
        public EditResult Arrange(ArrangeBy by)
        {
            Dictionary<int, Placement> to = BitSorter.ViewModel.Arrange.Of(_session.Project, _session.Module, new List<int>(Selection.Parts), by);
            if (to.Count == 0)
            {
                bool spread = by == ArrangeBy.SpreadAcross || by == ArrangeBy.SpreadDown;
                int needed = spread ? 3 : 2;
                return Said(Selection.Parts.Count < needed
                    ? EditResult.Refused($"Select {(spread ? "three" : "two")} parts or more to {(spread ? "spread" : "line up")}.")
                    : EditResult.Ok());
            }

            return Said(_session.Move(to));
        }

        /// <summary>The selected wires put back on the automatic route, their own bends forgotten: layout only, one step.</summary>
        public EditResult AutoRoute()
        {
            if (Selection.Wires.Count == 0)
                return Said(EditResult.Refused("Select a wire to route again."));
            return Said(_session.AutoRoute(new List<int>(Selection.Wires)));
        }

        /// <summary>The name a block made from the selection is offered: <c>block1</c>, <c>block2</c>... (Part 12b).</summary>
        public string FreeBlockName => BlockMaker.FreeName(_session.Project);

        /// <summary>
        /// The parts selected made into a block called <paramref name="name"/> (Part 12b), one undo step, and the new block
        /// selected; refused, in the document's words, for a selection with a port in it, a net driven both inside and
        /// out, or a name taken.
        /// </summary>
        public EditResult MakeBlock(string name)
        {
            if (Selection.Parts.Count == 0)
                return Said(EditResult.Refused("Select the parts to make a block of."));
            EditResult result = _session.MakeBlock(new List<int>(Selection.Parts), name);
            if (result.Done)
            {
                Selection.Clear();
                Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: result.Id), extend: false, toggle: false);
            }

            Version++;
            return Said(result);
        }

        /// <summary>The one thing selected, to inspect with Enter (Part 10c); nothing with none, or with more than one.</summary>
        public CanvasTarget Single
        {
            get
            {
                if (Selection.Parts.Count == 1 && Selection.Wires.Count == 0)
                    foreach (int part in Selection.Parts)
                        return new CanvasTarget(CanvasTargetKind.Part, part: part);
                if (Selection.Wires.Count == 1 && Selection.Parts.Count == 0)
                    foreach (int wire in Selection.Wires)
                        return new CanvasTarget(CanvasTargetKind.Wire, wire: wire);
                return CanvasTarget.Nothing;
            }
        }

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

        /// <summary>How far outside a part the timing path's outline runs, in cells: outside a selection's, so both show.</summary>
        public const double PathRoom = SelectedRoom + 0.12;

        private IReadOnlyList<IReadOnlyList<string>> _path;

        /// <summary>What of the path lit this layout has, worked out once for each layout and path rather than at every redraw.</summary>
        private TimingLight _light;
        private SchematicLayout _lightLayout;
        private IReadOnlyList<IReadOnlyList<string>> _lightPath;

        /// <summary>
        /// The timing paths the overlay lights (Part 14b): one line of the TIMING tab, every identical path of it, each its
        /// signals named from the design's top; null for none.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>> Path => _path;

        /// <summary>Lights the timing paths <paramref name="paths"/>, or none for null: the overlay is drawn again.</summary>
        public void Light(IReadOnlyList<IReadOnlyList<string>> paths)
        {
            if (ReferenceEquals(paths, _path))
                return;
            _path = paths;
            Version++;
        }

        /// <summary>Fills <paramref name="overlay"/> with what the overlay draws now.</summary>
        public void Overlay(CanvasOverlay overlay)
        {
            overlay.Clear();
            SchematicLayout.Frame frame = _layout.Board;
            Module module = Module;
            if (frame == null || module == null)
                return;

            if (_path != null)
            {
                if (!ReferenceEquals(_lightLayout, _layout) || !ReferenceEquals(_lightPath, _path))
                {
                    _light = TimingLight.Of(_layout, _path);
                    _lightLayout = _layout;
                    _lightPath = _path;
                }
                TimingLight light = _light;
                foreach (int part in light.Parts)
                {
                    PartOutline outline = _hit.OutlineOf(part);
                    if (outline != null)
                        overlay.PathParts.Add(Stage(frame, outline, PathRoom, 0, 0));
                }
                foreach ((int net, int branch) in light.Branches)
                    overlay.PathWires.Add(_layout.Nets[net].Branches[branch]);
                foreach (int port in light.Ports)
                    overlay.PathPorts.Add(_layout.Ports[port].Box);
            }

            foreach (int part in Selection.Parts)
            {
                PartOutline outline = _hit.OutlineOf(part);
                if (outline != null)
                    overlay.Selected.Add(Stage(frame, outline, SelectedRoom, 0, 0));
            }

            foreach (int wire in Selection.Wires)
            {
                IReadOnlyList<BoardPoint> points = wire == _reshaping && _reshaped != null ? _reshaped : _hit.WireOf(wire);
                if (points == null)
                    continue;
                overlay.SelectedWires.Add(Stage(frame, points));
                for (int i = 1; i < points.Count - 1; i++)
                    overlay.Handles.Add(frame.ToStage(points[i]));
            }

            if (_reshaping >= 0 && _reshaped != null)
                overlay.GhostWires.Add(Stage(frame, _reshaped));

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

            if (_draft != null)
            {
                BoardPoint end = _draftFree ? _draftAt : CanvasSnap.Snap(_draftAt);
                Facing? entering = null, endFacing = null;
                BoardPoint endAt = default;
                Endpoint to = default;
                bool onEnd = _draftOver.Kind != CanvasTargetKind.Nothing
                    && TryEnd(_draftOver, out to, out endAt, out endFacing, out double? _);
                if (onEnd)
                {
                    end = endAt;
                    entering = endFacing;
                    overlay.DraftEnd = frame.ToStage(endAt);
                    overlay.DraftRefused = _draftWhyNot != null;
                }

                // Clear of the other nets' wires, as the wire once made is routed: its own net is the start's, and the end's it joins.
                WireObstacles around = DraftAround();
                _draftOwn.Clear();
                _draftOwn.Add(NetOf(_draft.From));
                if (onEnd)
                    _draftOwn.Add(NetOf(to));
                overlay.Draft = Stage(frame, _draft.PathTo(end, entering, around, _draftOwn));
                overlay.DraftStart = frame.ToStage(_draft.Start);
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
