using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>What an edit came to: done, with the id of anything it made, or refused, with the reason.</summary>
    public readonly struct EditResult
    {
        private EditResult(bool done, string refusal, int id)
        {
            Done = done;
            Refusal = refusal;
            Id = id;
        }

        public readonly bool Done;

        /// <summary>Why it was refused, in words the player can act on; null when it was done.</summary>
        public readonly string Refusal;

        /// <summary>The id of the part or wire the edit made, or 0.</summary>
        public readonly int Id;

        public static EditResult Ok(int id = 0) => new EditResult(true, null, id);
        public static EditResult Refused(string why) => new EditResult(false, why, 0);

        public override string ToString() => Done ? $"done ({Id})" : "refused: " + Refusal;
    }

    /// <summary>
    /// The one way anything changes a document (invariant 3): every edit is a command here, checked,
    /// and kept for undo.
    /// </summary>
    /// <remarks>
    /// **An edit may not add an error.** Each command builds the document it would make and checks it
    /// with <see cref="BoardRules"/>; an error the old one did not have refuses the edit, with that
    /// error as the reason, and nothing changes. Warnings -- an input left unconnected -- never refuse.
    /// One rule, rather than a check written into each command, so no command can forget one.
    ///
    /// **Undo keeps whole documents.** A document is immutable and an edit shares everything it did not
    /// touch, so keeping the one before costs a reference: correct by construction, as 4.x's board
    /// snapshots were, and without copying. A run of moves of one part, or of bends of one wire, is one
    /// step -- a drag should undo in one go. The history is capped at <see cref="HistoryLimit"/>.
    ///
    /// **Several edits can be one** (Part 10): <see cref="MoveParts"/> and <see cref="RemoveParts"/> take a
    /// selection in one step, and <see cref="Group"/> makes any run of edits one step, all of them or none --
    /// a clock is an input placed and its clock given, a paste is parts and the wires between them.
    /// </remarks>
    public sealed class BoardEditor
    {
        public const int HistoryLimit = 200;

        private readonly List<Project> _undo = new List<Project>();
        private readonly List<Project> _redo = new List<Project>();
        private string _continuing;
        private List<BoardDiagnostic> _diagnostics;

        /// <summary>Whether a <see cref="Group"/> is being made: its edits keep no step of their own and say nothing until it ends.</summary>
        private bool _grouping;

        public BoardEditor(Project project)
        {
            Project = project ?? throw new ArgumentNullException(nameof(project));
            _diagnostics = BoardRules.Check(project);
        }

        public Project Project { get; private set; }

        /// <summary>What is wrong with the document now, and what is worth a warning.</summary>
        public IReadOnlyList<BoardDiagnostic> Diagnostics => _diagnostics;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Raised after every change: an edit, an undo or a redo.</summary>
        public event Action Changed;

        public Netlist NetlistOf(string module)
        {
            Module m = Project.Find(module);
            return m == null ? null : Netlist.Of(Project, m);
        }

        // -----------------------------------------------------------------
        // Parts
        // -----------------------------------------------------------------

        /// <summary>
        /// Places <paramref name="part"/>, giving it the module's next id and, if it has none, a name of
        /// its kind: g1, r1, a, y ...
        /// </summary>
        public EditResult Add(string module, Part part, Placement at)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            if (part == null)
                return EditResult.Refused("There is nothing to place.");

            // Under a library a gate takes its cell's delays, wherever it comes from -- a paste of one copied before the
            // library changed included (the 14d review).
            if (part is GatePart gate && Project.Settings?.Library?.CellFor(gate.Kind, gate.Inputs) is LibraryCell cell)
                part = gate with { Rise = cell.Rise, Fall = cell.Fall };

            int id = m.NextId;
            Part placed = part with { Id = id, Name = part.Name ?? DefaultName(m, part) };
            Module next = m with
            {
                Parts = Append(m.Parts, placed),
                NextId = id + 1,
                Layout = m.Layout.WithPart(id, at),
            };

            return Commit(Project.WithModule(next), id);
        }

        /// <summary>
        /// Takes a part away, and every wire on it, and every wire that tapped those -- and, for an input of the
        /// top module, what the stimulus gave it, as renaming one renames its stimulus.
        /// </summary>
        public EditResult Remove(string module, int part)
        {
            if (!TryPart(module, part, out Module m, out _, out EditResult refused))
                return refused;

            var gone = new HashSet<int>();
            foreach (Wire wire in m.Wires)
                if (wire.Touches(part))
                    gone.Add(wire.Id);

            Module unwired = WithoutWires(m, gone);
            Module next = unwired with
            {
                Parts = Without(unwired.Parts, p => p.Id == part),
                Layout = unwired.Layout.WithoutPart(part),
            };

            return Commit(WithoutStimulusOf(Project.WithModule(next), m, new HashSet<int> { part }));
        }

        /// <summary>
        /// Takes several parts and wires away at once (Part 10): a selection deleted, one step. Every wire on
        /// a part taken goes too, and every wire that tapped one taken.
        /// </summary>
        public EditResult RemoveParts(string module, IEnumerable<int> parts, IEnumerable<int> wires = null)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;

            var partsGone = new HashSet<int>();
            foreach (int part in parts ?? Array.Empty<int>())
            {
                if (m.FindPart(part) == null)
                    return EditResult.Refused($"There is no part {part} in {module}.");
                partsGone.Add(part);
            }

            var gone = new HashSet<int>();
            foreach (int wire in wires ?? Array.Empty<int>())
            {
                if (m.FindWire(wire) == null)
                    return EditResult.Refused($"There is no wire {wire} in {module}.");
                gone.Add(wire);
            }

            if (partsGone.Count == 0 && gone.Count == 0)
                return EditResult.Refused("There is nothing to take away.");

            foreach (Wire wire in m.Wires)
                foreach (int part in partsGone)
                    if (wire.Touches(part))
                        gone.Add(wire.Id);

            Module unwired = WithoutWires(m, gone);
            Layout layout = unwired.Layout;
            foreach (int part in partsGone)
                layout = layout.WithoutPart(part);

            Project next = Project.WithModule(unwired with { Parts = Without(unwired.Parts, p => partsGone.Contains(p.Id)), Layout = layout });
            return Commit(WithoutStimulusOf(next, m, partsGone));
        }

        /// <summary>The project without what its stimulus gave the top module's inputs among <paramref name="parts"/> of <paramref name="module"/>.</summary>
        private static Project WithoutStimulusOf(Project project, Module module, HashSet<int> parts)
        {
            Stimulus stimulus = project.Stimulus;
            if (stimulus == null || project.Top != module.Name)
                return project;

            var ports = new HashSet<string>(StringComparer.Ordinal);
            foreach (int id in parts)
                if (module.FindPart(id) is PortPart port && port.Direction == PortDirection.Input)
                    ports.Add(port.Name);
            if (ports.Count == 0)
                return project;

            var tracks = new List<Track>();
            foreach (Track track in stimulus.Tracks)
                if (!ports.Contains(track.Port))
                    tracks.Add(track);
            var clocks = new List<ClockTrack>();
            foreach (ClockTrack clock in stimulus.Clocks)
                if (!ports.Contains(clock.Port))
                    clocks.Add(clock);

            return project with { Stimulus = stimulus with { Tracks = tracks, Clocks = clocks } };
        }

        /// <summary>
        /// Makes every edit <paramref name="body"/> makes one step (Part 10): undone in one go, and made in full
        /// or not at all -- when the body's answer is a refusal, every edit it made is taken back, and the
        /// document is as it was before. <see cref="Changed"/> is raised once, at the end, if anything changed.
        /// </summary>
        /// <param name="continuing">As for <see cref="MoveParts"/>: a group of the same name straight after another is part of its step.</param>
        public EditResult Group(Func<EditResult> body, string continuing = null)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (_grouping)
                return body();

            Project before = Project;
            List<BoardDiagnostic> diagnostics = _diagnostics;
            string wasContinuing = _continuing;
            EditResult result;

            _grouping = true;
            try
            {
                result = body();
            }
            catch
            {
                Project = before;
                _diagnostics = diagnostics;
                throw;
            }
            finally
            {
                _grouping = false;
            }

            if (!result.Done)
            {
                Project = before;
                _diagnostics = diagnostics;
                _continuing = wasContinuing;
                return result;
            }

            if (ReferenceEquals(Project, before))
                return result;

            if (continuing == null || continuing != wasContinuing)
            {
                _undo.Add(before);
                if (_undo.Count > HistoryLimit)
                    _undo.RemoveAt(0);
            }

            _redo.Clear();
            _continuing = continuing;
            Changed?.Invoke();
            return result;
        }

        /// <summary>
        /// Puts <paramref name="changed"/> in place of the part with its id: a new delay, width, name,
        /// number of inputs. It must be the same kind of part. A port renamed is renamed wherever the
        /// module is used as a block, and in the stimulus.
        /// </summary>
        public EditResult Change(string module, Part changed)
        {
            if (changed == null)
                return EditResult.Refused("There is nothing to change.");
            if (!TryPart(module, changed.Id, out Module m, out Part old, out EditResult refused))
                return refused;
            if (old.GetType() != changed.GetType())
                return EditResult.Refused($"{old.Name} cannot become another kind of part; take it away and place the other.");
            if (old.Equals(changed))
                return EditResult.Ok(changed.Id);

            Project next = Project.WithModule(m with { Parts = Replace(m.Parts, changed) });

            if (old is PortPart oldPort && changed is PortPart newPort && oldPort.Name != newPort.Name)
                next = RenamePort(next, m.Name, oldPort.Name, newPort.Name);

            return Commit(next, changed.Id);
        }

        /// <summary>
        /// Moves a part. Only the layout changes, so it cannot be refused for what it does to the
        /// circuit. With <paramref name="continuing"/>, a move of the same part straight after another
        /// is part of the same step for undo, as a drag is.
        /// </summary>
        public EditResult Move(string module, int part, Placement to, bool continuing = false)
        {
            if (!TryPart(module, part, out Module m, out _, out EditResult refused))
                return refused;

            Module next = m with { Layout = m.Layout.WithPart(part, to) };
            return Commit(Project.WithModule(next), part, continuing ? $"move {module} {part}" : null, check: false);
        }

        /// <summary>
        /// Moves -- or turns, or mirrors -- several parts at once (Part 10): a selection dragged, one step for
        /// undo. Layout only, as <see cref="Move"/> is. With <paramref name="continuing"/>, a move of the same
        /// parts straight after another is part of the same step.
        /// </summary>
        public EditResult MoveParts(string module, IReadOnlyDictionary<int, Placement> to, bool continuing = false)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            if (to == null || to.Count == 0)
                return EditResult.Refused("There is nothing to move.");

            var ids = new List<int>(to.Keys);
            ids.Sort();
            Layout layout = m.Layout;
            foreach (int part in ids)
            {
                if (m.FindPart(part) == null)
                    return EditResult.Refused($"There is no part {part} in {module}.");
                layout = layout.WithPart(part, to[part]);
            }

            string key = continuing ? $"move {module} {string.Join(",", ids)}" : null;
            return Commit(Project.WithModule(m with { Layout = layout }), ids[0], key, check: false);
        }

        // -----------------------------------------------------------------
        // Wires
        // -----------------------------------------------------------------

        /// <summary>Draws a wire between two ends, each a pin or a tap on another wire.</summary>
        public EditResult Connect(string module, Endpoint from, Endpoint to, Route route = null)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            if (from.Equals(to))
                return EditResult.Refused("A wire needs two different ends.");

            Netlist before = Netlist.Of(Project, m);
            Net a = NetAt(before, from), b = NetAt(before, to);
            if (a != null && a == b)
                return EditResult.Refused("Those are already joined.");

            int id = m.NextId;
            var wire = new Wire { Id = id, From = from, To = to };
            Module next = m with
            {
                Wires = Append(m.Wires, wire),
                NextId = id + 1,
                Layout = route == null ? m.Layout : m.Layout.WithRoute(id, route),
            };

            return Commit(Project.WithModule(next), id);
        }

        /// <summary>Takes a wire away, and every wire that tapped it.</summary>
        public EditResult Disconnect(string module, int wire)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            if (m.FindWire(wire) == null)
                return EditResult.Refused($"There is no wire {wire} in {module}.");

            return Commit(Project.WithModule(WithoutWires(m, new HashSet<int> { wire })));
        }

        /// <summary>Names the net a wire is on; null takes the name away.</summary>
        public EditResult NameWire(string module, int wire, string name)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            Wire old = m.FindWire(wire);
            if (old == null)
                return EditResult.Refused($"There is no wire {wire} in {module}.");

            Wire named = old with { NetName = string.IsNullOrEmpty(name) ? null : name };
            return Commit(Project.WithModule(m with { Wires = Replace(m.Wires, named) }), wire);
        }

        /// <summary>Changes how a wire is drawn. Layout only, like <see cref="Move"/>.</summary>
        public EditResult Reroute(string module, int wire, Route route, bool continuing = false)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            if (m.FindWire(wire) == null)
                return EditResult.Refused($"There is no wire {wire} in {module}.");

            Module next = m with { Layout = m.Layout.WithRoute(wire, route ?? new Route()) };
            return Commit(Project.WithModule(next), wire, continuing ? $"route {module} {wire}" : null, check: false);
        }

        // -----------------------------------------------------------------
        // Modules and the project
        // -----------------------------------------------------------------

        public EditResult AddModule(string name)
        {
            string why = BoardRules.WhyNotAName(name);
            if (why != null)
                return EditResult.Refused(why);
            if (Project.Find(name) != null)
                return EditResult.Refused($"There is already a module called {name}.");

            return Commit(Project.WithModule(new Module { Name = name }));
        }

        /// <summary>
        /// Adds a module written as SystemVerilog (Part 12d), starting from <paramref name="source"/>, or from
        /// <see cref="WrittenModules.Template"/> when none is given.
        /// </summary>
        public EditResult AddWrittenModule(string name, string source = null)
        {
            string why = BoardRules.WhyNotAName(name);
            if (why != null)
                return EditResult.Refused(why);
            if (Project.Find(name) != null)
                return EditResult.Refused($"There is already a module called {name}.");

            return Commit(Project.WithModule(new Module { Name = name, Source = source ?? WrittenModules.Template(name) }));
        }

        /// <summary>
        /// A written module's text, all of it in one step. Refused, with its first error, when the text does not read as
        /// the module or would break a block of it -- a pin taken away under a wire, say.
        /// </summary>
        public EditResult SetSource(string module, string source)
        {
            if (!TryModule(module, out Module m, out EditResult refused))
                return refused;
            if (!m.IsWritten)
                return EditResult.Refused($"{module} is drawn, so it has no text to set.");
            if (source == null)
                return EditResult.Refused("A written module needs some text.");
            if (source == m.Source)
                return EditResult.Ok();

            return Commit(Project.WithModule(m with { Source = source }));
        }

        /// <summary>Renames a module, and every block of it, and the top if it was the top.</summary>
        public EditResult RenameModule(string from, string to)
        {
            if (!TryModule(from, out Module m, out EditResult refused))
                return refused;
            string why = BoardRules.WhyNotAName(to);
            if (why != null)
                return EditResult.Refused(why);
            if (from == to)
                return EditResult.Ok();
            if (Project.Find(to) != null)
                return EditResult.Refused($"There is already a module called {to}.");

            var modules = new List<Module>();
            foreach (Module each in Project.Modules)
            {
                Module renamed = each == m ? each with { Name = to } : each;

                // Written text says the name too (Part 12d): in its own header, and wherever it instantiates the module.
                if (each.IsWritten)
                    renamed = renamed with { Source = WrittenModules.Renamed(Project, each, from, to) };

                var parts = new List<Part>(renamed.Parts.Count);
                bool changed = false;
                foreach (Part part in renamed.Parts)
                {
                    if (part is BlockPart block && block.Module == from)
                    {
                        parts.Add(block with { Module = to });
                        changed = true;
                    }
                    else
                    {
                        parts.Add(part);
                    }
                }

                modules.Add(changed ? renamed with { Parts = parts } : renamed);
            }

            return Commit(Project with
            {
                Modules = modules,
                Top = Project.Top == from ? to : Project.Top,
                Bench = Project.Bench == from ? to : Project.Bench,
            });
        }

        /// <summary>
        /// A module taken out of the project, its drawing or its code with it: one step, which undo puts back. Refused while
        /// anything uses it (<see cref="WhyNotRemoveModule"/>). Taking out the written testbench (Part 13b) gives the inputs
        /// back to the stimulus.
        /// </summary>
        public EditResult RemoveModule(string name)
        {
            if (!TryModule(name, out Module m, out EditResult refused))
                return refused;
            string why = WhyNotRemoveModule(name);
            if (why != null)
                return EditResult.Refused(why);

            var modules = new List<Module>(Project.Modules);
            modules.Remove(m);
            return Commit(Project with { Modules = modules, Bench = Project.Bench == name ? null : Project.Bench });
        }

        /// <summary>
        /// Why the module <paramref name="name"/> cannot be taken out, or null when it can: the top cannot, nor a module a
        /// block of which is placed, nor one another module's code instantiates -- each said with where, so the player knows
        /// what to delete first. Deleting a block leaves its module in the project, as its code is the player's work: this is
        /// asked before the workbench asks whether to delete one (the 14g report: code1 stayed after its block went).
        /// </summary>
        public string WhyNotRemoveModule(string name)
        {
            if (Project.Find(name) == null)
                return $"There is no module called {name ?? "(none)"}.";
            if (Project.Top == name)
                return $"{name} is the top module; make another the top first.";

            foreach (Module other in Project.Modules)
            {
                if (other.Name == name)
                    continue;
                if (other.IsWritten)
                {
                    foreach (Module used in WrittenModules.UsesOf(Project, other))
                        if (used.Name == name)
                            return $"{other.Name}'s code makes a {name}: take that out of its code first.";
                    continue;
                }

                foreach (Part part in other.Parts)
                    if (part is BlockPart block && block.Module == name)
                        return $"{name} is in use: {other.Name} has a block of it, {block.Name}. Delete that block first" +
                            (other.Name == Project.Top ? "." : $", inside {other.Name}: double-click a block of {other.Name} to step in.");
            }

            return null;
        }

        public EditResult SetTop(string name)
        {
            if (!TryModule(name, out _, out EditResult refused))
                return refused;
            return Commit(Project with { Top = name });
        }

        /// <summary>Sets what the testbench does; null for no testbench.</summary>
        public EditResult SetStimulus(Stimulus stimulus) => Commit(Project with { Stimulus = stimulus });

        /// <summary>
        /// The written testbench turned into the stimulus (Part 13c, <see cref="BenchConverter"/>): what it drove into the
        /// inputs, recorded from its run, as one step. Refused, with why, when there is none or it cannot be run.
        /// </summary>
        public EditResult ConvertBench()
        {
            Conversion conversion = BenchConverter.Convert(Project);
            return conversion.Succeeded ? Commit(conversion.Project) : EditResult.Refused(conversion.Refusal);
        }

        /// <summary>
        /// Gives an input of the top module <paramref name="value"/> from <paramref name="from"/> until
        /// <paramref name="until"/> (<see cref="StimulusEdits.TrySet"/>); one of the wrong width is refused
        /// by the rules, as any edit is.
        /// </summary>
        public EditResult SetInput(string port, long from, long until, LogicVec value)
        {
            if (Project.Bench != null)
                return EditResult.Refused(BoardRules.BenchDrivesTheInputs(Project));
            if (!StimulusEdits.TrySet(Project.Stimulus, port, from, until, value, out Stimulus edited, out string refusal))
                return EditResult.Refused(refusal);
            return Commit(Project with { Stimulus = edited });
        }

        /// <summary>Flips a one-bit input over the stretch <paramref name="at"/> is in (<see cref="StimulusEdits.TryInvert"/>).</summary>
        public EditResult InvertInput(string port, long at) => InvertInput(port, at, out _);

        /// <inheritdoc cref="InvertInput(string, long)"/>
        /// <param name="from">Where the flipped stretch starts: from when the run changed.</param>
        public EditResult InvertInput(string port, long at, out long from)
        {
            from = at;
            if (Project.Bench != null)
                return EditResult.Refused(BoardRules.BenchDrivesTheInputs(Project));
            if (!StimulusEdits.TryInvert(Project.Stimulus, port, at, out Stimulus edited, out from, out string refusal))
                return EditResult.Refused(refusal);
            return Commit(Project with { Stimulus = edited });
        }

        // -----------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------

        /// <summary>
        /// Sets the project's settings: the time unit its time is counted in, which relabels time and
        /// rescales no number, and the delay a newly placed gate takes.
        /// </summary>
        public EditResult SetSettings(ProjectSettings settings) =>
            settings == null ? EditResult.Refused("A project needs its settings.") : Commit(Project with { Settings = settings });

        /// <summary>
        /// The project held to <paramref name="library"/> (Part 14d), or to none for null -- and every drawn gate the library
        /// has a cell for given that cell's delays in the same step, so a delay changed in the table is every gate of the
        /// cell changed, and undone together. Refused, in the rules' words, when what is drawn breaks it: a gate it has no
        /// cell for, a MUX, a constant where it takes none.
        /// </summary>
        public EditResult SetLibrary(GateLibrary library)
        {
            var modules = new List<Module>(Project.Modules.Count);
            foreach (Module module in Project.Modules)
            {
                List<Part> parts = null;
                for (int i = 0; library != null && !module.IsWritten && i < module.Parts.Count; i++)
                    if (module.Parts[i] is GatePart gate && library.CellFor(gate.Kind, gate.Inputs) is LibraryCell cell
                        && (gate.Rise != cell.Rise || gate.Fall != cell.Fall))
                    {
                        parts ??= new List<Part>(module.Parts);
                        parts[i] = gate with { Rise = cell.Rise, Fall = cell.Fall };
                    }
                modules.Add(parts == null ? module : module with { Parts = parts });
            }

            // The library it already has, every gate already its cell's: nothing to do, and no step to undo (the 14d review).
            bool retimed = false;
            for (int i = 0; i < modules.Count && !retimed; i++)
                retimed = !ReferenceEquals(modules[i], Project.Modules[i]);
            if (!retimed && GateLibrary.Alike(library, Project.Settings?.Library))
                return EditResult.Ok();

            ProjectSettings settings = (Project.Settings ?? new ProjectSettings()) with { Library = library };
            return Commit(Project with { Modules = modules, Settings = settings });
        }

        // -----------------------------------------------------------------
        // Undo
        // -----------------------------------------------------------------

        public bool Undo()
        {
            if (_undo.Count == 0)
                return false;

            _redo.Add(Project);
            Restore(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0)
                return false;

            _undo.Add(Project);
            Restore(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            return true;
        }

        private void Restore(Project project)
        {
            Project = project;
            _diagnostics = BoardRules.Check(project);
            _continuing = null;
            Changed?.Invoke();
        }

        // -----------------------------------------------------------------
        // Doing it
        // -----------------------------------------------------------------

        private EditResult Commit(Project next, int id = 0, string continuing = null, bool check = true)
        {
            if (check)
            {
                List<BoardDiagnostic> after = BoardRules.Check(next);
                List<BoardDiagnostic> added = BoardRules.NewErrors(_diagnostics, after);
                if (added.Count > 0)
                    return EditResult.Refused(added[0].Message);
                _diagnostics = after;
            }

            if (_grouping)
            {
                // Inside a group: the step is the group's, kept when it ends.
                Project = next;
                return EditResult.Ok(id);
            }

            if (continuing == null || continuing != _continuing)
            {
                _undo.Add(Project);
                if (_undo.Count > HistoryLimit)
                    _undo.RemoveAt(0);
            }

            _redo.Clear();
            _continuing = continuing;
            Project = next;
            Changed?.Invoke();
            return EditResult.Ok(id);
        }

        private bool TryModule(string name, out Module module, out EditResult refused)
        {
            module = Project.Find(name);
            refused = module == null ? EditResult.Refused($"There is no module called {name ?? "(none)"}.") : default;
            return module != null;
        }

        private bool TryPart(string module, int id, out Module m, out Part part, out EditResult refused)
        {
            part = null;
            if (!TryModule(module, out m, out refused))
                return false;

            part = m.FindPart(id);
            if (part == null)
                refused = EditResult.Refused($"There is no part {id} in {module}.");
            return part != null;
        }

        private static Net NetAt(Netlist netlist, Endpoint end) =>
            end.IsTap ? netlist.NetOfWire(end.TapOf) : netlist.NetOf(end.Pin);

        /// <summary>The module without <paramref name="wires"/>, nor any wire that taps one of them, however many deep.</summary>
        private static Module WithoutWires(Module m, HashSet<int> wires)
        {
            for (bool more = true; more;)
            {
                more = false;
                foreach (Wire wire in m.Wires)
                {
                    if (wires.Contains(wire.Id))
                        continue;
                    if ((wire.From.IsTap && wires.Contains(wire.From.TapOf)) || (wire.To.IsTap && wires.Contains(wire.To.TapOf)))
                    {
                        wires.Add(wire.Id);
                        more = true;
                    }
                }
            }

            Layout layout = m.Layout;
            foreach (int wire in wires)
                layout = layout.WithoutRoute(wire);

            return m with { Wires = Without(m.Wires, w => wires.Contains(w.Id)), Layout = layout };
        }

        /// <summary>A port renamed: the wires on that pin of every block of the module, and the stimulus if it is the top.</summary>
        private static Project RenamePort(Project project, string module, string from, string to)
        {
            var modules = new List<Module>();
            foreach (Module m in project.Modules)
            {
                var blocks = new HashSet<int>();
                foreach (Part part in m.Parts)
                    if (part is BlockPart block && block.Module == module)
                        blocks.Add(part.Id);

                if (blocks.Count == 0)
                {
                    modules.Add(m);
                    continue;
                }

                Endpoint Renamed(Endpoint end) =>
                    !end.IsTap && blocks.Contains(end.Pin.Part) && end.Pin.Pin == from ? Endpoint.At(end.Pin.Part, to) : end;

                var wires = new List<Wire>(m.Wires.Count);
                foreach (Wire wire in m.Wires)
                    wires.Add(wire with { From = Renamed(wire.From), To = Renamed(wire.To) });
                modules.Add(m with { Wires = wires });
            }

            Stimulus stimulus = project.Stimulus;
            if (stimulus != null && project.Top == module)
            {
                var tracks = new List<Track>();
                foreach (Track track in stimulus.Tracks)
                    tracks.Add(track.Port == from ? track with { Port = to } : track);
                var clocks = new List<ClockTrack>();
                foreach (ClockTrack clock in stimulus.Clocks)
                    clocks.Add(clock.Port == from ? clock with { Port = to } : clock);
                stimulus = stimulus with { Tracks = tracks, Clocks = clocks };
            }

            return project with { Modules = modules, Stimulus = stimulus };
        }

        /// <summary>The first free name of the part's kind: g1, r1 ...; for ports, a, b, c ... and y, z.</summary>
        private static string DefaultName(Module m, Part part)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part each in m.Parts)
                if (each.Name != null)
                    taken.Add(each.Name);
            foreach (Wire wire in m.Wires)
                if (wire.NetName != null)
                    taken.Add(wire.NetName);

            bool Free(string name) => !taken.Contains(name);

            if (part is PortPart port)
            {
                string[] first = port.Direction == PortDirection.Input
                    ? new[] { "a", "b", "c", "d", "e", "f", "g", "h" }
                    : new[] { "y", "z" };
                foreach (string name in first)
                    if (Free(name) && BoardRules.IsValidName(name))
                        return name;
            }

            string prefix = part switch
            {
                PortPart p => p.Direction == PortDirection.Input ? "in" : "out",
                GatePart _ => "g",
                RegisterPart _ => "r",
                ConstantPart _ => "k",
                MuxPart _ => "m",
                SplitterPart _ => "sp",
                JoinerPart _ => "j",
                LampPart _ => "lamp",
                BlockPart _ => "u",
                _ => "p",
            };

            for (int n = 1; ; n++)
                if (Free(prefix + n))
                    return prefix + n;
        }

        private static IReadOnlyList<T> Append<T>(IReadOnlyList<T> list, T item)
        {
            var copy = new List<T>(list.Count + 1);
            copy.AddRange(list);
            copy.Add(item);
            return copy;
        }

        private static IReadOnlyList<T> Without<T>(IReadOnlyList<T> list, Predicate<T> gone)
        {
            var copy = new List<T>(list.Count);
            foreach (T item in list)
                if (!gone(item))
                    copy.Add(item);
            return copy;
        }

        private static IReadOnlyList<Part> Replace(IReadOnlyList<Part> parts, Part changed)
        {
            var copy = new List<Part>(parts.Count);
            foreach (Part part in parts)
                copy.Add(part.Id == changed.Id ? changed : part);
            return copy;
        }

        private static IReadOnlyList<Wire> Replace(IReadOnlyList<Wire> wires, Wire changed)
        {
            var copy = new List<Wire>(wires.Count);
            foreach (Wire wire in wires)
                copy.Add(wire.Id == changed.Id ? changed : wire);
            return copy;
        }
    }
}
