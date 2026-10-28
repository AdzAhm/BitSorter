using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>A gate library as the save file holds it (Part 14d): its cells by kind, inputs, rise and fall.</summary>
    [Serializable]
    public sealed class LibraryRecord
    {
        public string name;
        public CellRecord[] cells = new CellRecord[0];
        public bool noConstants;
        public int gateBudget = -1;

        /// <summary>How many of each part (Part 15b); absent, so none, in every older file.</summary>
        public CountRecord[] counts = new CountRecord[0];

        public static LibraryRecord From(GateLibrary library)
        {
            if (library == null)
                return new LibraryRecord();
            var cells = new List<CellRecord>();
            foreach (LibraryCell cell in library.Cells)
                cells.Add(new CellRecord { kind = cell.Kind.ToString(), inputs = cell.Inputs, rise = cell.Rise, fall = cell.Fall });
            var counts = new List<CountRecord>();
            foreach (PartCount count in library.Counts)
                counts.Add(new CountRecord { part = count.Part, most = count.Most });
            return new LibraryRecord
            {
                name = library.Name, cells = cells.ToArray(), noConstants = library.NoConstants, gateBudget = library.GateBudget,
                counts = counts.ToArray(),
            };
        }

        /// <summary>The library, a cell of no gate BitSorter has left out and said so in <paramref name="problems"/>.</summary>
        public GateLibrary ToLibrary(List<string> problems)
        {
            var kept = new List<LibraryCell>();
            foreach (CellRecord cell in cells ?? new CellRecord[0])
            {
                if (cell == null)
                    continue;
                if (Enum.TryParse(cell.kind, out GateKind kind) && GateLibrary.CanHold(kind, cell.inputs))
                    kept.Add(new LibraryCell(kind, cell.inputs, cell.rise, cell.fall));
                else
                    problems.Add($"The library's cell {cell.kind} of {cell.inputs} inputs is no cell BitSorter has; it was left out.");
            }
            var counted = new List<PartCount>();
            foreach (CountRecord count in counts ?? new CountRecord[0])
                if (count != null && !string.IsNullOrEmpty(count.part))
                    counted.Add(new PartCount(count.part, count.most));
            return new GateLibrary
            {
                Name = string.IsNullOrEmpty(name) ? GateLibrary.CustomName : name,
                Cells = kept,
                NoConstants = noConstants,
                GateBudget = gateBudget,
                Counts = counted,
            };
        }
    }

    /// <summary>One part's count, as the save file holds it (Part 15b).</summary>
    [Serializable]
    public sealed class CountRecord
    {
        public string part;
        public int most;
    }

    [Serializable]
    public sealed class CellRecord
    {
        public string kind;
        public int inputs;
        public long rise;
        public long fall;
    }

    /// <summary>
    /// A document as the save file holds it: <c>format: 5</c> (section 8). Plain public fields in the
    /// shape Unity's JSON reader needs -- no dictionaries, no class that can be null, no inheritance --
    /// so a part is one record with a <see cref="PartRecord.kind"/> and the fields its kind uses, and a
    /// value is written as a SystemVerilog literal.
    /// </summary>
    [Serializable]
    public sealed class ProjectRecord
    {
        /// <summary>The save format this was written in. 4.x had no version; 5.0 writes 5.</summary>
        public const int Format = 5;

        public int format = Format;
        public string name;
        public string top;

        /// <summary>The written testbench's module (Part 13b); empty, or absent in an older file, when the stimulus drives the design.</summary>
        public string bench;
        public ModuleRecord[] modules = new ModuleRecord[0];

        /// <summary>Whether there is a stimulus: the reader cannot leave <see cref="stimulus"/> null.</summary>
        public bool hasStimulus;

        public StimulusRecord stimulus = new StimulusRecord();
        public string timeUnit = "ns";
        public long defaultGateDelay = 1;

        /// <summary>Whether the project has a gate library (Part 14d): the reader cannot leave <see cref="library"/> null. Absent in an older file, which had none.</summary>
        public bool hasLibrary;

        public LibraryRecord library = new LibraryRecord();

        /// <summary>The project's other files (Part 13b): includes and memory images. Absent in an older file, which had none.</summary>
        public FileRecord[] files = new FileRecord[0];

        public static ProjectRecord From(Project project)
        {
            var file = new ProjectRecord
            {
                name = project.Name,
                top = project.Top,
                bench = project.Bench,
                hasStimulus = project.Stimulus != null,
                timeUnit = project.Settings.TimeUnit,
                defaultGateDelay = project.Settings.DefaultGateDelay,
                hasLibrary = project.Settings.Library != null,
                library = LibraryRecord.From(project.Settings.Library),
            };

            var modules = new List<ModuleRecord>();
            foreach (Module module in project.Modules)
                modules.Add(ModuleRecord.From(module));
            file.modules = modules.ToArray();

            var files = new List<FileRecord>();
            foreach (ProjectFile each in project.Files)
                files.Add(new FileRecord { name = each.Name, text = each.Text });
            file.files = files.ToArray();

            if (project.Stimulus != null)
                file.stimulus = StimulusRecord.From(project.Stimulus);
            return file;
        }

        /// <summary>The document, and what in the file could not be read, in words; null when nothing could.</summary>
        public Project ToProject(List<string> problems)
        {
            if (format > Format)
                problems.Add($"This was saved by a newer BitSorter (format {format}); some of it may not have come across.");

            var modules = new List<Module>();
            foreach (ModuleRecord module in this.modules ?? new ModuleRecord[0])
                modules.Add(module.ToModule(problems));

            // A unit that is not one -- edited by hand, or written by something else -- would print as a
            // `timescale no simulator reads, so it is counted in ns, and the player is told.
            string unit = timeUnit ?? TimeUnits.Default;
            if (!TimeUnits.IsUnit(unit))
            {
                problems.Add($"The time unit '{unit}' is not one SystemVerilog has, so this project counts in {TimeUnits.Default}.");
                unit = TimeUnits.Default;
            }

            var kept = new List<ProjectFile>();
            foreach (FileRecord each in files ?? new FileRecord[0])
            {
                if (each == null)
                    continue;
                if (ProjectFiles.IsValidName(each.name, out string why))
                    kept.Add(new ProjectFile { Name = each.name, Text = each.text ?? "" });
                else
                    problems.Add(why + " It was left out.");
            }

            return new Project
            {
                Name = name,
                Top = top,
                Bench = string.IsNullOrEmpty(bench) ? null : bench,
                Modules = modules,
                Stimulus = hasStimulus ? stimulus?.ToStimulus(problems) : null,
                Settings = new ProjectSettings
                {
                    TimeUnit = unit,
                    DefaultGateDelay = defaultGateDelay,
                    Library = hasLibrary ? library?.ToLibrary(problems) : null,
                },
                Files = kept,
            };
        }

        internal static string Text(LogicVec? value) => value?.ToLiteral(Radix.Binary) ?? "";

        internal static LogicVec? Value(string text, List<string> problems, string what)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            LiteralResult read = Literal.Parse(text);
            if (read.Ok)
                return read.Value;
            problems.Add($"{what} is \"{text}\", which is not a value: {read.Error}");
            return null;
        }
    }

    /// <summary>One of a project's other files, as saved: its bare name and its text.</summary>
    [Serializable]
    public sealed class FileRecord
    {
        public string name;
        public string text;
    }

    [Serializable]
    public sealed class ModuleRecord
    {
        public string name;
        public string comment;

        /// <summary>A written module's text (Part 12d); empty, or absent in an older file, for a drawn one.</summary>
        public string source;

        /// <summary>
        /// The course template it came from (Part 13e): its file's name and text, and the module's text as the import first
        /// made it of the file; empty, or absent in an older file, for none.
        /// </summary>
        public string templateFile;

        /// <inheritdoc cref="templateFile"/>
        public string template;

        /// <inheritdoc cref="templateFile"/>
        public string templateModule;
        public int nextId = 1;
        public PartRecord[] parts = new PartRecord[0];
        public WireRecord[] wires = new WireRecord[0];
        public PlacementRecord[] placements = new PlacementRecord[0];
        public RouteRecord[] routes = new RouteRecord[0];
        public double panX;
        public double panY;
        public double zoom = 1;

        public static ModuleRecord From(Module module)
        {
            var file = new ModuleRecord
            {
                name = module.Name,
                comment = module.Comment,
                source = module.Source,
                templateFile = module.Template?.File,
                template = module.Template?.Text,
                templateModule = module.Template?.Given,
                nextId = module.NextId,
                panX = module.Layout.Pan.X,
                panY = module.Layout.Pan.Y,
                zoom = module.Layout.Zoom,
            };

            var parts = new List<PartRecord>();
            foreach (Part part in module.Parts)
                parts.Add(PartRecord.From(part));
            file.parts = parts.ToArray();

            var wires = new List<WireRecord>();
            foreach (Wire wire in module.Wires)
                wires.Add(WireRecord.From(wire));
            file.wires = wires.ToArray();

            // In the order of the parts and wires, so a file is the same however the dictionary is kept.
            var placements = new List<PlacementRecord>();
            foreach (Part part in module.Parts)
                if (module.Layout.Parts.TryGetValue(part.Id, out Placement p))
                    placements.Add(new PlacementRecord { part = part.Id, x = p.Position.X, y = p.Position.Y, turns = p.QuarterTurns, flipped = p.Flipped });
            file.placements = placements.ToArray();

            var routes = new List<RouteRecord>();
            foreach (Wire wire in module.Wires)
                if (module.Layout.Routes.TryGetValue(wire.Id, out Route r))
                    routes.Add(RouteRecord.From(wire.Id, r));
            file.routes = routes.ToArray();

            return file;
        }

        public Module ToModule(List<string> problems)
        {
            var parts = new List<Part>();
            foreach (PartRecord part in this.parts ?? new PartRecord[0])
            {
                Part read = part.ToPart(problems, name);
                if (read != null)
                    parts.Add(read);
            }

            var wires = new List<Wire>();
            foreach (WireRecord wire in this.wires ?? new WireRecord[0])
                wires.Add(wire.ToWire());

            var places = new Dictionary<int, Placement>();
            foreach (PlacementRecord p in placements ?? new PlacementRecord[0])
                places[p.part] = new Placement(new Point(p.x, p.y), p.turns, p.flipped);

            var routed = new Dictionary<int, Route>();
            foreach (RouteRecord r in routes ?? new RouteRecord[0])
                routed[r.wire] = r.ToRoute();

            return new Module
            {
                Name = name,
                Comment = string.IsNullOrEmpty(comment) ? null : comment,
                Source = string.IsNullOrEmpty(source) ? null : source,
                Template = string.IsNullOrEmpty(template) ? null : new ModuleTemplate { File = templateFile ?? name + ".sv", Text = template, Given = templateModule ?? "" },
                NextId = nextId,
                Parts = parts,
                Wires = wires,
                Layout = new Layout { Parts = places, Routes = routed, Pan = new Point(panX, panY), Zoom = zoom },
            };
        }
    }

    /// <summary>One part: its <see cref="kind"/> says which of the other fields it uses.</summary>
    [Serializable]
    public sealed class PartRecord
    {
        public int id;
        public string name;

        /// <summary>port, gate, constant, register, mux, splitter, joiner, lamp or block.</summary>
        public string kind;

        /// <summary>A port's: input or output.</summary>
        public string direction;

        public string comment;
        public int width = 1;

        /// <summary>A gate's primitive: and, or, nand, nor, xor, xnor, not, buf.</summary>
        public string gate;

        public int inputs;
        public long rise;
        public long fall;

        /// <summary>A constant's value, as a literal.</summary>
        public string value;

        public bool hasReset;
        public string resetValue;

        /// <summary>A register's starting value, as a literal; empty for x.</summary>
        public string initial;

        /// <summary>A register's clock to Q and setup (Part 14a); absent in an older file, and so 0.</summary>
        public long tcq;

        /// <inheritdoc cref="tcq"/>
        public long tsetup;

        /// <summary>A block's module.</summary>
        public string module;

        /// <summary>How many copies a block is (Part 12c); a file from before arrays has none, read as one.</summary>
        public int copies;

        public static PartRecord From(Part part)
        {
            var file = new PartRecord { id = part.Id, name = part.Name };
            switch (part)
            {
                case PortPart p:
                    file.kind = "port";
                    file.direction = p.Direction == PortDirection.Output ? "output" : "input";
                    file.width = p.Width;
                    file.comment = p.Comment;
                    break;
                case GatePart g:
                    file.kind = "gate";
                    file.gate = g.Kind.ToString().ToLowerInvariant();
                    file.inputs = g.Inputs;
                    file.width = g.Width;
                    file.rise = g.Rise;
                    file.fall = g.Fall;
                    break;
                case ConstantPart c:
                    file.kind = "constant";
                    file.value = ProjectRecord.Text(c.Value);
                    file.width = c.Value.Width;
                    break;
                case RegisterPart r:
                    file.kind = "register";
                    file.width = r.Width;
                    file.hasReset = r.HasReset;
                    file.resetValue = ProjectRecord.Text(r.ResetValue);
                    file.initial = ProjectRecord.Text(r.Initial);
                    file.tcq = r.Tcq;
                    file.tsetup = r.Tsetup;
                    break;
                case MuxPart m:
                    file.kind = "mux";
                    file.inputs = m.Inputs;
                    file.width = m.Width;
                    break;
                case SplitterPart s:
                    file.kind = "splitter";
                    file.width = s.Width;
                    break;
                case JoinerPart j:
                    file.kind = "joiner";
                    file.width = j.Width;
                    break;
                case LampPart l:
                    file.kind = "lamp";
                    file.width = l.Width;
                    break;
                case BlockPart b:
                    file.kind = "block";
                    file.module = b.Module;
                    file.copies = b.Copies;
                    break;
            }

            return file;
        }

        public Part ToPart(List<string> problems, string owner)
        {
            string where = $"{owner}.{name}";
            switch (kind)
            {
                case "port":
                    return new PortPart
                    {
                        Id = id, Name = name, Width = width, Comment = string.IsNullOrEmpty(comment) ? null : comment,
                        Direction = direction == "output" ? PortDirection.Output : PortDirection.Input,
                    };

                case "gate":
                    if (!Enum.TryParse(gate, true, out GateKind primitive))
                    {
                        problems.Add($"{where} is a gate of a kind this BitSorter does not know, \"{gate}\"; it was left out.");
                        return null;
                    }

                    return new GatePart { Id = id, Name = name, Kind = primitive, Inputs = inputs, Width = width, Rise = rise, Fall = fall };

                case "constant":
                    return new ConstantPart { Id = id, Name = name, Value = ProjectRecord.Value(value, problems, where) ?? LogicVec.AllX(Math.Max(1, width)) };

                case "register":
                    return new RegisterPart
                    {
                        Id = id, Name = name, Width = width, HasReset = hasReset,
                        ResetValue = ProjectRecord.Value(resetValue, problems, where + "'s reset value"),
                        Initial = ProjectRecord.Value(initial, problems, where + "'s starting value"),
                        Tcq = tcq,
                        Tsetup = tsetup,
                    };

                case "mux": return new MuxPart { Id = id, Name = name, Inputs = inputs, Width = width };
                case "splitter": return new SplitterPart { Id = id, Name = name, Width = width };
                case "joiner": return new JoinerPart { Id = id, Name = name, Width = width };
                case "lamp": return new LampPart { Id = id, Name = name, Width = width };
                case "block": return new BlockPart { Id = id, Name = name, Module = module, Copies = Math.Max(1, copies) };

                default:
                    problems.Add($"{where} is a part of a kind this BitSorter does not know, \"{kind}\"; it was left out.");
                    return null;
            }
        }
    }

    /// <summary>A wire: each end a pin (part and pin) or a tap on another wire (-1 when not).</summary>
    [Serializable]
    public sealed class WireRecord
    {
        public int id;
        public int fromPart;
        public string fromPin;
        public int fromTap = -1;
        public int toPart;
        public string toPin;
        public int toTap = -1;
        public string netName;

        public static WireRecord From(Wire wire) => new WireRecord
        {
            id = wire.Id,
            fromPart = wire.From.IsTap ? 0 : wire.From.Pin.Part,
            fromPin = wire.From.IsTap ? null : wire.From.Pin.Pin,
            fromTap = wire.From.TapOf,
            toPart = wire.To.IsTap ? 0 : wire.To.Pin.Part,
            toPin = wire.To.IsTap ? null : wire.To.Pin.Pin,
            toTap = wire.To.TapOf,
            netName = wire.NetName,
        };

        public Wire ToWire() => new Wire
        {
            Id = id,
            From = fromTap >= 0 ? Endpoint.OnWire(fromTap) : Endpoint.At(fromPart, fromPin),
            To = toTap >= 0 ? Endpoint.OnWire(toTap) : Endpoint.At(toPart, toPin),
            NetName = string.IsNullOrEmpty(netName) ? null : netName,
        };
    }

    [Serializable]
    public sealed class PlacementRecord
    {
        public int part;
        public double x;
        public double y;
        public int turns;
        public bool flipped;
    }

    [Serializable]
    public sealed class PointRecord
    {
        public double x;
        public double y;
    }

    [Serializable]
    public sealed class RouteRecord
    {
        public int wire;
        public PointRecord[] bends = new PointRecord[0];
        public bool smooth;
        public double tapAt = 0.5;

        public static RouteRecord From(int wire, Route route)
        {
            var bends = new List<PointRecord>();
            foreach (Point p in route.Bends)
                bends.Add(new PointRecord { x = p.X, y = p.Y });
            return new RouteRecord { wire = wire, bends = bends.ToArray(), smooth = route.Smooth, tapAt = route.TapAt };
        }

        public Route ToRoute()
        {
            var points = new List<Point>();
            foreach (PointRecord p in bends ?? new PointRecord[0])
                points.Add(new Point(p.x, p.y));
            return new Route { Bends = points, Smooth = smooth, TapAt = tapAt };
        }
    }

    [Serializable]
    public sealed class StimulusRecord
    {
        public TrackRecord[] tracks = new TrackRecord[0];
        public ClockRecord[] clocks = new ClockRecord[0];
        public long end = 100;

        /// <summary>What the outputs must read (Part 15b); absent, so none, in every older file.</summary>
        public CheckRecord[] checks = new CheckRecord[0];

        public static StimulusRecord From(Stimulus stimulus)
        {
            var tracks = new List<TrackRecord>();
            foreach (Track track in stimulus.Tracks)
            {
                var steps = new List<StepRecord>();
                foreach (Step step in track.Steps)
                    steps.Add(new StepRecord { time = step.Time, value = ProjectRecord.Text(step.Value), late = step.Late });
                tracks.Add(new TrackRecord { port = track.Port, steps = steps.ToArray(), start = ProjectRecord.Text(track.Start) });
            }

            var clocks = new List<ClockRecord>();
            foreach (ClockTrack clock in stimulus.Clocks)
                clocks.Add(new ClockRecord { port = clock.Port, period = clock.Period, startsHigh = clock.StartsHigh, setAtZero = clock.SetAtZero });

            var checks = new List<CheckRecord>();
            foreach (Check check in stimulus.Checks)
                checks.Add(new CheckRecord { port = check.Port, time = check.Time, value = ProjectRecord.Text(check.Expected) });

            return new StimulusRecord { tracks = tracks.ToArray(), clocks = clocks.ToArray(), end = stimulus.End, checks = checks.ToArray() };
        }

        public Stimulus ToStimulus(List<string> problems)
        {
            var tracks = new List<Track>();
            foreach (TrackRecord track in this.tracks ?? new TrackRecord[0])
            {
                var steps = new List<Step>();
                foreach (StepRecord step in track.steps ?? new StepRecord[0])
                {
                    LogicVec? value = ProjectRecord.Value(step.value, problems, $"{track.port} at {step.time}");
                    if (value != null)
                        steps.Add(new Step(step.time, value.Value, step.late));
                }

                tracks.Add(new Track { Port = track.port, Steps = steps, Start = ProjectRecord.Value(track.start, problems, $"{track.port} from the start") });
            }

            var clocks = new List<ClockTrack>();
            foreach (ClockRecord clock in this.clocks ?? new ClockRecord[0])
                clocks.Add(new ClockTrack { Port = clock.port, Period = clock.period, StartsHigh = clock.startsHigh, SetAtZero = clock.setAtZero });

            var checked_ = new List<Check>();
            foreach (CheckRecord check in checks ?? new CheckRecord[0])
            {
                if (check == null)
                    continue;
                LogicVec? value = ProjectRecord.Value(check.value, problems, $"the check of {check.port} at {check.time}");
                if (value != null)
                    checked_.Add(new Check { Port = check.port, Time = check.time, Expected = value.Value });
            }

            return new Stimulus { Tracks = tracks, Clocks = clocks, End = end, Checks = checked_ };
        }
    }

    [Serializable]
    public sealed class TrackRecord
    {
        public string port;
        public StepRecord[] steps = new StepRecord[0];

        /// <summary>What the input holds from before time 0 (Part 13c); empty, or absent in an older file, for unknown.</summary>
        public string start;
    }

    [Serializable]
    public sealed class StepRecord
    {
        public long time;
        public string value;

        /// <summary>Whether it is made after the rest of its moment (Part 13c); absent in an older file, so false.</summary>
        public bool late;
    }

    /// <summary>A check of an output, as the save file holds it (Part 15b).</summary>
    [Serializable]
    public sealed class CheckRecord
    {
        public string port;
        public long time;
        public string value;
    }

    [Serializable]
    public sealed class ClockRecord
    {
        public string port;
        public long period = 10;
        public bool startsHigh;

        /// <summary>Whether its first level is set at time 0, an event (Part 13c); absent in an older file, so false.</summary>
        public bool setAtZero;
    }
}
