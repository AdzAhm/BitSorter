using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Owns the level, the player's blueprint and the run state, and is the single entry point for
    /// every edit. Drives Run and Reset by asking <see cref="SimulationRunner"/> to rebuild.
    /// </summary>
    /// <remarks>
    /// The blueprint is the only mutable authority on the circuit. The Simulation is derived from it
    /// and thrown away freely -- on Run, on Reset, and after every single edit. So Reset is not a
    /// restore: nothing was ever snapshotted. It is the same Rebuild call that Run makes, differing
    /// only in the state it lands in.
    ///
    /// Rebuilding on every edit rather than mutating the live graph is deliberate. Edits are legal
    /// only while Editing, where the graph sits at tick 0 with nothing in flight, so a rebuild costs
    /// nothing and makes blueprint-versus-simulation divergence unrepresentable.
    ///
    /// Note the method is ResetBoard, not Reset. MonoBehaviour.Reset is an editor callback Unity
    /// invokes when a component is added or reset from the inspector, so a public Reset() here would
    /// silently be called by the editor at times that have nothing to do with the player.
    /// </remarks>
    public sealed class LevelSession : MonoBehaviour
    {
        [Tooltip("File name without extension, from Assets/Resources/Levels/. " +
                 "Q and E cycle levels at runtime without touching this.")]
        [SerializeField] private string _levelName = "route-the-bit";

        [SerializeField] private SimulationRunner _runner;

        private readonly CircuitBlueprint _blueprint = new CircuitBlueprint();

        /// <summary>Every level file found under Resources/Levels, by name, in cycle order.</summary>
        private string[] _available;

        /// <summary>The loaded level, or null if loading failed.</summary>
        public LevelDefinition Level { get; private set; }

        /// <summary>Why the level would not load, for the HUD. Null when all is well.</summary>
        public string LoadError { get; private set; }

        public RunState State { get; private set; } = RunState.Editing;

        /// <summary>Meaningful once <see cref="State"/> is Passed or Failed.</summary>
        public RunVerdict Verdict { get; private set; }

        /// <summary>
        /// Requires the runner too. Every edit path below reaches through it, so treating a session
        /// with no runner as "loaded" would only move the failure to the first click.
        /// </summary>
        public bool IsLoaded => Level != null && _runner != null;

        /// <summary>The gate both editing controllers check before acting on a click.</summary>
        public bool CanEdit => IsLoaded && State == RunState.Editing;

        /// <summary>What the player built. Read-only to everyone but this component.</summary>
        public CircuitBlueprint Blueprint => _blueprint;

        /// <summary>
        /// The level file currently loaded. Readable so the scene builder can carry the choice across a
        /// rebuild instead of resetting it.
        /// </summary>
        public string LevelName => _levelName;

        /// <summary>Every level, in play order, with the name a player sees.</summary>
        /// <remarks>
        /// Built once and cached. Adding a level to Resources/Levels puts it in the run with no other
        /// change, but it does need a domain reload to be noticed, which is the same as it ever was.
        /// </remarks>
        public IReadOnlyList<LevelEntry> Catalogue => _catalogue ?? (_catalogue = DiscoverCatalogue());

        /// <summary>Level file names in play order. Derived from <see cref="Catalogue"/>.</summary>
        public IReadOnlyList<string> AvailableLevels => _available ?? (_available = NamesOf(Catalogue));

        private IReadOnlyList<LevelEntry> _catalogue;

        private static string[] NamesOf(IReadOnlyList<LevelEntry> catalogue)
        {
            var names = new string[catalogue.Count];

            for (int i = 0; i < catalogue.Count; i++)
                names[i] = catalogue[i].FileName;

            return names;
        }

        /// <summary>
        /// Raised once a level is loaded and the board is back to empty, carrying the new level.
        /// </summary>
        /// <remarks>
        /// Exists for state that is derived from the level but not stored here -- the palette
        /// selection above all. Clearing the blueprint resets everything this component owns, but a
        /// component holding its own level-specific answer has no way to know it went stale, and
        /// would happily carry the previous level's answer onto the next one.
        ///
        /// Subscribers must attach in OnEnable, not Start: the first load happens in this
        /// component's Start, and Unity has run every OnEnable by then but not every Start.
        /// </remarks>
        public event Action<LevelDefinition> LevelLoaded;

        /// <summary>
        /// Raised just before a level is replaced, carrying the file name of the one leaving, while
        /// its board is still intact.
        /// </summary>
        /// <remarks>
        /// The counterpart to <see cref="LevelLoaded"/>, and it exists because that one is too late
        /// for anything that wants to *keep* something. LoadLevel clears the blueprint before it
        /// announces the new level, so by the time LevelLoaded fires the outgoing board is already
        /// gone. Anything saving work has to be told first.
        /// </remarks>
        public event Action<string> LevelUnloading;

        /// <summary>Position of the current level in <see cref="AvailableLevels"/>, or -1.</summary>
        public int LevelIndex
        {
            get
            {
                IReadOnlyList<string> all = AvailableLevels;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == _levelName)
                        return i;
                }

                return -1;
            }
        }

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();
        }

        private void Start()
        {
            // Start, not Awake: loading needs the runner to have found its grid, and component Awake
            // order within one GameObject is not defined. Every renderer already checks
            // SimulationRunner.IsReady, so a frame with no graph draws nothing rather than throwing.
            LoadLevel(_levelName);
        }

        private void Update()
        {
            if (State != RunState.Running || !IsLoaded || _runner == null || !_runner.IsReady)
                return;

            // Idle is checked first. Once nothing is in flight and no source has anything left, extra
            // ticks cannot change any result, so a run that settles exactly on the tick limit is a
            // pass and not a timeout.
            if (_runner.IsIdle())
            {
                Settle(true);
                return;
            }

            if (LevelGrader.HasTimedOut(_runner.View, Level))
                Settle(false);
        }

        // -----------------------------------------------------------------
        // Loading
        // -----------------------------------------------------------------

        /// <summary>
        /// Loads a level by file name and returns to an empty, editable board. Public so a level-select
        /// flow can call it later; nothing does yet.
        /// </summary>
        public bool LoadLevel(string levelName)
        {
            Vector2Int halfExtents = _runner != null ? _runner.HalfExtents : new Vector2Int(4, 2);

            LevelLoadResult result = LevelLoader.Load(levelName, halfExtents);

            if (!result.IsValid)
            {
                Level = null;
                LoadError = result.Error;

                // Listing what does exist turns "no level named that" from a dead end into an answer,
                // and a misremembered file name is the likeliest way to arrive here.
                string known = string.Join(", ", AvailableLevels);

                // An error, not a warning: the game cannot start, and this is the only place the
                // author finds out why.
                Debug.LogError($"BitSorter: {result.Error}. Available: {known}");
                return false;
            }

            // Announced before anything is thrown away, so a listener can keep the outgoing board.
            // Guarded on IsLoaded: the first load has no previous level to save.
            if (IsLoaded)
                LevelUnloading?.Invoke(_levelName);

            _levelName = levelName;
            Level = result.Level;
            LoadError = null;

            _blueprint.Clear();
            ResetBoard();

            // Last, so a subscriber sees a board that is already empty and a level that is already
            // the new one. Anything reading the session from in here gets the finished state.
            LevelLoaded?.Invoke(Level);
            return true;
        }

        /// <summary>
        /// Takes a level that came from somewhere other than a file, under the given key.
        /// </summary>
        /// <remarks>
        /// <see cref="LoadLevel"/> with the file read taken out, and everything after it kept
        /// identical -- the same unload announcement, the same blueprint clear, the same rebuild, the
        /// same load announcement last. That sameness is the point: <see cref="ProgressTracker"/>
        /// saves and restores boards off those two events, so free play gets persistence without
        /// either side knowing the other exists.
        ///
        /// The key stands in for a file name and is what the board is saved against. Nothing checks
        /// that a file by that name exists, and for <see cref="SandboxLevel.Key"/> none does.
        ///
        /// Rebuilding is also how free play applies an edit to its sources: changing them changes the
        /// graph, so the config produces a new definition and it arrives back through here.
        /// </remarks>
        public bool Adopt(LevelDefinition level, string key)
        {
            if (level == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (IsLoaded)
                LevelUnloading?.Invoke(_levelName);

            _levelName = key;
            Level = level;
            LoadError = null;

            _blueprint.Clear();
            ResetBoard();

            LevelLoaded?.Invoke(Level);
            return true;
        }

        /// <summary>
        /// Loads the next level file along, wrapping at the end. Discards whatever was on the board.
        /// </summary>
        /// <remarks>
        /// Deliberately keys off the files present rather than a list written down anywhere, so adding
        /// a level to Resources/Levels puts it in the rotation with no other change. This is a way to
        /// reach a level, not a level-select screen -- CLAUDE.md still has that under "Not yet".
        ///
        /// Cycling rather than editing the serialized field because the field cannot be relied on:
        /// rebuilding the scene recreates the component, and a Play-mode edit to it is reverted when
        /// Play exits. Keys depend on no serialized state and so survive both.
        /// </remarks>
        public bool CycleLevel(int step)
        {
            IReadOnlyList<string> all = AvailableLevels;

            if (all.Count == 0 || step == 0)
                return false;

            return LoadLevel(all[NextIndex(LevelIndex, step, all.Count)]);
        }

        /// <summary>
        /// Where a step of <paramref name="step"/> lands from <paramref name="current"/>, wrapping in
        /// both directions. A negative <paramref name="current"/> means the level is not in the list.
        /// </summary>
        /// <remarks>
        /// Pulled out and made static so the wrap can be tested directly. C# gives a negative result
        /// for a negative left operand of %, so stepping back from the first entry lands on -1 and
        /// throws unless the remainder is nudged positive -- the whole reason this is not inline.
        /// </remarks>
        public static int NextIndex(int current, int step, int count)
        {
            if (count <= 0)
                return -1;

            // An unrecognised current level starts the walk at one end rather than nowhere.
            if (current < 0)
                return step > 0 ? 0 : count - 1;

            return ((current + step) % count + count) % count;
        }

        /// <summary>
        /// Level file names under Resources/Levels, in the order they should be played.
        /// </summary>
        /// <remarks>
        /// Resources.LoadAll does not promise an order, and an order that varied by machine would make
        /// "the next level" mean different things for different people. This used to be an ordinal
        /// sort of the file names, which is stable but pedagogically wrong -- it put the NAND puzzle
        /// ahead of the half adder and the tutorial seventh. Each file now names its own place and
        /// <see cref="LevelCatalog"/> puts them in it.
        ///
        /// Parsing every file to read one integer is affordable: there is a handful of them, and this
        /// runs once, cached by <see cref="AvailableLevels"/>. A file that will not parse still takes
        /// part, unplaced, so a broken level shows up in the rotation and can be selected and
        /// diagnosed rather than silently vanishing.
        /// </remarks>
        private IReadOnlyList<LevelEntry> DiscoverCatalogue()
        {
            Vector2Int halfExtents = _runner != null ? _runner.HalfExtents : new Vector2Int(4, 2);

            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);
            var entries = new List<LevelEntry>(assets.Length);

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] == null)
                    continue;

                LevelLoadResult parsed = LevelLoader.Parse(assets[i].text, halfExtents);

                entries.Add(parsed.IsValid
                    ? new LevelEntry(assets[i].name, parsed.Level.Order, parsed.Level.Name)
                    : new LevelEntry(assets[i].name, 0));
            }

            IReadOnlyList<LevelEntry> ordered = LevelCatalog.Sort(entries, out string clash);

            if (clash != null)
            {
                // An error, not a warning. The run order is now partly arbitrary, and the only place
                // anyone finds out is here.
                Debug.LogError($"BitSorter: {clash}");
            }

            return ordered;
        }

        // -----------------------------------------------------------------
        // Run and reset
        // -----------------------------------------------------------------

        /// <summary>
        /// Streams the level's test vectors through whatever the player has built.
        /// </summary>
        /// <remarks>
        /// Rebuilds first, so a run always begins at tick 0. That makes Run idempotent and means it
        /// works straight after a failed run without needing a Reset in between.
        /// </remarks>
        public void Run()
        {
            if (!IsLoaded || _runner == null)
                return;

            _runner.Rebuild(Level, _blueprint);
            _runner.SetPaused(false);
            _runner.ClockRunning = true;

            Verdict = default;
            State = RunState.Running;
        }

        /// <summary>
        /// Throws away everything the player built, leaving the level's fixtures and an empty board.
        /// </summary>
        /// <remarks>
        /// Deliberately distinct from <see cref="ResetBoard"/>, which only rewinds the clock and
        /// keeps the circuit. The two were the same word in an earlier interface and it was the
        /// wrong word twice: a player who wants to start over has no way to, and a player who wants
        /// to re-run loses their work.
        /// </remarks>
        public void ClearBoard()
        {
            if (!IsLoaded || _runner == null)
                return;

            _blueprint.Clear();
            ResetBoard();
        }

        /// <summary>Returns to the pre-run board so the player can edit and try again.</summary>
        public void ResetBoard()
        {
            if (!IsLoaded || _runner == null)
                return;

            _runner.Rebuild(Level, _blueprint);
            _runner.SetPaused(false);
            _runner.ClockRunning = false;   // Editing holds at tick 0 whatever the pause key says

            Verdict = default;
            State = RunState.Editing;
        }

        private void Settle(bool settled)
        {
            _runner.ClockRunning = false;

            // Free play is not graded at all rather than graded and ignored. Asking the grader for a
            // verdict nobody reads would leave a Failed sitting in Verdict for the status banner to
            // find, and there is nothing for a sandbox circuit to have failed at.
            if (!Level.IsGraded)
            {
                Verdict = default;
                State = RunState.Finished;
                return;
            }

            Verdict = LevelGrader.Grade(_runner.View, Level, _runner.FixtureNodeIds, settled);
            State = Verdict.IsPass ? RunState.Passed : RunState.Failed;
        }

        // -----------------------------------------------------------------
        // Editing
        // -----------------------------------------------------------------

        /// <summary>
        /// Surfaces the refusal for an edit attempted at the wrong time and reports whether it
        /// refused, so a caller can gate itself with a single line. Keeps the wording in
        /// <see cref="LevelRules"/> rather than duplicated across the input components.
        /// </summary>
        public bool RefuseIfNotEditing()
        {
            LevelVerdict gate = LevelRules.CanEdit(State);

            if (gate.IsValid)
                return false;

            _runner.RejectEdit(gate.Reason);
            return true;
        }

        /// <summary>How many of a kind the player may still place.</summary>
        public int RemainingFor(GateKind kind) =>
            IsLoaded ? LevelRules.RemainingFor(Level, _blueprint, kind) : 0;

        /// <summary>
        /// How many of a kind are on the board. What the HUD's parts rows show, so they read the
        /// same way round as the delay row beneath them.
        /// </summary>
        /// <remarks>
        /// Ungated, unlike <see cref="RemainingFor"/>: this asks the blueprint alone, so it needs no
        /// level and no runner and cannot report a stocked board as an empty one.
        /// </remarks>
        public int PlacedCountOf(GateKind kind) => _blueprint.CountOf(kind);

        /// <summary>Ticks of delay already added across every wire.</summary>
        public int SpentDelay => _blueprint.ExtraDelay();

        public bool TryPlaceGate(GateKind kind, Vector2Int cell)
        {
            if (!IsLoaded)
                return false;

            LevelVerdict verdict = LevelRules.CanPlace(
                Level, _blueprint, State, kind, cell, _runner.HalfExtents);

            if (!verdict.IsValid)
            {
                _runner.RejectEdit(verdict.Reason);
                return false;
            }

            _blueprint.Place(cell, kind);
            _runner.Rebuild(Level, _blueprint);
            return true;
        }

        /// <summary>
        /// Removes the gate on a cell. Returns whether the click was <em>handled</em>, not whether
        /// anything was removed.
        /// </summary>
        /// <remarks>
        /// The distinction matters to the caller. An empty cell returns false so the right click can
        /// fall through to deleting the nearest wire, which is how wires are deleted at all. A fixture
        /// returns true: the click was aimed at something real and got its refusal, so it must not
        /// also delete a wire that happens to pass nearby.
        /// </remarks>
        public bool TryRemoveAt(Vector2Int cell)
        {
            if (!IsLoaded)
                return false;

            LevelVerdict verdict = LevelRules.CanRemove(Level, _blueprint, State, cell);

            if (!verdict.IsValid)
            {
                _runner.RejectEdit(verdict.Reason);   // null stays silent
                return verdict.Outcome != LevelOutcome.NothingThere;
            }

            _blueprint.RemoveAt(cell);
            _runner.Rebuild(Level, _blueprint);
            return true;
        }

        /// <summary>
        /// Wires two ports if the drag is legal. Structural legality is
        /// <see cref="WiringRules"/>'s call; this adds only the run-state gate and the blueprint
        /// bookkeeping.
        /// </summary>
        public bool TryConnect(PortAddress from, PortAddress to, int delay = 1)
        {
            if (!IsLoaded)
                return false;

            LevelVerdict gate = LevelRules.CanEdit(State);

            if (!gate.IsValid)
            {
                _runner.RejectEdit(gate.Reason);
                return false;
            }

            WiringVerdict wiring = WiringRules.Validate(_runner.View, from, to);

            if (!wiring.IsValid)
            {
                _runner.RejectEdit(wiring.Reason);   // null reason stays silent
                return false;
            }

            // Store by cell, not by node id. WiringRules has already put the ends the right way round,
            // so From is always the output regardless of which end the player grabbed first.
            if (!TryCellPort(wiring.Source.Owner.Id, false, wiring.Source.Index, out CellPort source) ||
                !TryCellPort(wiring.Target.Owner.Id, true, wiring.Target.Index, out CellPort target))
            {
                return false;   // a node vanished between the drag starting and ending
            }

            _blueprint.AddWire(new BlueprintWire(source, target, delay));
            _runner.Rebuild(Level, _blueprint);
            return true;
        }

        /// <summary>
        /// Deletes the wire nearest a world point. Bits travelling it are destroyed and are not counted
        /// as corruption -- an edit is not a collision.
        /// </summary>
        public bool TryDeleteWireAt(Vector2 world)
        {
            if (!IsLoaded)
                return false;

            LevelVerdict gate = LevelRules.CanEdit(State);

            if (!gate.IsValid)
            {
                _runner.RejectEdit(gate.Reason);
                return false;
            }

            Edge edge = _runner.NearestEdge(world);

            if (edge == null)
                return false;   // clicked nothing; stay silent

            if (!TryWireIndex(edge, out int index))
                return false;

            _blueprint.RemoveWireAt(index);
            _runner.Rebuild(Level, _blueprint);
            return true;
        }

        /// <summary>
        /// Re-times the wire nearest a world point by <paramref name="delta"/> ticks. Returns whether
        /// anything changed.
        /// </summary>
        /// <remarks>
        /// Delay is edited in place, so the rebuild reissues the same edge ids in the same order and a
        /// caller tracking a hovered wire by id keeps pointing at the same wire across the change it
        /// just caused.
        /// </remarks>
        public bool TryChangeWireDelay(Vector2 world, int delta)
        {
            if (!IsLoaded || delta == 0)
                return false;

            Edge edge = _runner.NearestEdge(world);

            if (edge == null)
                return false;   // nothing under the cursor; stay silent

            if (!TryWireIndex(edge, out int index))
                return false;

            int current = _blueprint.Wires[index].Delay;
            int target = current + delta;

            LevelVerdict verdict = LevelRules.CanSetDelay(Level, _blueprint, State, current, target);

            if (!verdict.IsValid)
            {
                _runner.RejectEdit(verdict.Reason);   // null stays silent
                return false;
            }

            _blueprint.SetDelayAt(index, target);
            _runner.Rebuild(Level, _blueprint);
            return true;
        }

        /// <summary>
        /// The blueprint wire a built edge came from, found by the cells at its two ends. Delay is
        /// deliberately not part of the match, so this keeps working once wires can be re-timed.
        /// </summary>
        private bool TryWireIndex(Edge edge, out int index)
        {
            index = -1;

            if (!TryCellPort(edge.Source.Owner.Id, false, edge.Source.Index, out CellPort source) ||
                !TryCellPort(edge.Target.Owner.Id, true, edge.Target.Index, out CellPort target))
            {
                return false;
            }

            index = _blueprint.IndexOfWire(source, target);
            return index >= 0;
        }

        private bool TryCellPort(int nodeId, bool isInput, int index, out CellPort port)
        {
            if (!_runner.TryCellOf(nodeId, out Vector2Int cell))
            {
                port = default;
                return false;
            }

            port = new CellPort(cell, isInput, index);
            return true;
        }
    }
}
