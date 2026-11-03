using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// BitSorter opens here (Part 15a), as a simulator does: on the last thing that was open -- a circuit or a sample -- and
    /// with nothing, on the start page, an IDE's home: START makes, opens or imports a circuit, RECENT reopens one, SAMPLES
    /// opens a sample, and HELP has the keyboard map and Settings. HOME goes back to it. HELP in the toolbar has the keyboard
    /// map, the start page and the credits; SETTINGS opens Settings over the workbench, and coming back finds it as it was.
    /// Escape closes what is on top and nothing more: nothing is behind the workbench to go back to.
    /// </summary>
    /// <remarks>
    /// The start page is the shell's, covering everything under the header, whose menus stay working; while it is up the
    /// workbench reads no keys and no pointer of its own, so nothing hidden under it is played, scrolled or deleted.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The start page's name, and its items': this, and what each does.</summary>
        public const string StartPageName = "Workbench start page", StartItem = "Workbench start ";

        /// <inheritdoc cref="StartPageName"/>
        public const string StartNewItem = StartItem + "new", StartOpenItem = StartItem + "open", StartImportItem = StartItem + "import",
            StartKeysItem = StartItem + "keys", StartSettingsItem = StartItem + "settings";

        /// <summary>The name of RECENT's item <paramref name="index"/>, the latest first.</summary>
        public static string RecentItem(int index) => StartItem + "recent " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The name of SAMPLES' item for <see cref="WorkbenchSamples.All"/>[<paramref name="index"/>].</summary>
        public static string SampleItem(int index) => StartItem + "sample " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The name of RISC-V PROCESSORS' button that opens the processor <see cref="WorkbenchSamples.All"/>[<paramref name="index"/>]
        /// running <paramref name="program"/> (Part 15h-6c).
        /// </summary>
        public static string ProcessorItem(int index, string program) =>
            StartItem + "processor " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + program;

        /// <summary>
        /// The three processors, each a section's card of its own on the start page (Ahmad, 2026-11-02: "a main section ...
        /// for the 3 RISC-V samples"), with what each shows in a line, rather than three more rows of SAMPLES.
        /// </summary>
        private static readonly (string Name, string Line)[] Processors =
        {
            ("Single-cycle RISC-V", "every instruction in one long clock cycle"),
            ("Multicycle RISC-V", "three to five short cycles, a state machine its control"),
            ("Pipelined RISC-V", "five stages at once, forwarding, stalls and flushes"),
        };

        /// <summary>The programs every processor runs, as its PROGRAM names them, and as its buttons say them.</summary>
        private static readonly (string Member, string Caption)[] Programs =
        {
            ("SUM", "SUM"), ("FIBONACCI", "FIBONACCI"), ("ISA", "ISA"),
        };

        private static bool IsProcessor(string sample)
        {
            foreach ((string name, string _) in Processors)
                if (name == sample)
                    return true;
            return false;
        }

        /// <summary>How many circuits RECENT lists: OPEN has the rest.</summary>
        public const int RecentShown = 8;

        /// <summary>The line under the app's name on the start page.</summary>
        public const string Tagline = "A digital logic simulator: draw a circuit or write it in SystemVerilog, and watch every signal change.";

        /// <summary>What the header's line says while the start page is up.</summary>
        public const string StartSays = "Start a circuit, open one, or look round a sample.";

        private VisualElement _home;
        private Label _homeNotice;
        private readonly ShellButton[] _recentItems = new ShellButton[RecentShown];
        private readonly List<string> _recentKeys = new List<string>();
        private Label _recentNone;
        private HelpMenu _helpMenu;
        private SettingsPanel _settings;
        private CreditsPanel _credits;

        /// <summary>The sample the first build opens on at launch (<see cref="WorkbenchSamples.Chip"/>); -1 to choose as the 4.x boot did.</summary>
        private int _launchSample = -1;

        /// <summary>Whether the start page is up.</summary>
        public bool StartPageShowing => _home != null && _home.style.display == DisplayStyle.Flex;

        /// <summary>What the start page says under its lists: the last notice given while it was up.</summary>
        public string StartPageNotice => _homeNotice != null ? _homeNotice.text : null;

        /// <summary>A notice, on the start page, where the chip that says it is hidden.</summary>
        private void ShowOnStartPage(string words)
        {
            if (_homeNotice != null)
                _homeNotice.text = words ?? "";
        }

        /// <summary>The circuits RECENT lists, the latest first.</summary>
        public IReadOnlyList<string> RecentListed => _recentKeys;

        /// <summary>HELP's menu.</summary>
        public HelpMenu HelpMenu => _helpMenu;

        private void Start() => Launch();

        /// <summary>
        /// Set while a launch opens what was open last, until it has stayed up <see cref="SurvivedFrames"/> frames (Part 16b).
        /// Found still set, the last launch stopped while opening it -- a browser out of memory stops the page -- and this one
        /// opens the start page instead: BitSorter opens on the last thing open, so it would stop again on every launch, as
        /// the pipelined processor once did (15h-6d).
        /// </summary>
        public const string OpeningKey = "BitSorter.Launch.Opening";

        /// <summary>What the chip says when the last launch stopped while opening what was open.</summary>
        public const string StoppedOpening =
            "BitSorter stopped while opening what was open last, so it opened on the start page. RECENT opens it again.";

        /// <summary>How many frames what a launch opened must stay up for the launch to count as over: half a second.</summary>
        private const int SurvivedFrames = 30;

        /// <summary>The frames left until what this launch opened counts as opened; 0 once it has, or with nothing opened.</summary>
        private int _launchWatch;

        /// <summary>The launch's mark taken off once what it opened has stayed up: called every frame.</summary>
        private void WatchTheLaunch()
        {
            if (_launchWatch > 0 && --_launchWatch == 0)
                Preferences.SetInt(OpeningKey, 0);
        }

        private void OnApplicationQuit()
        {
            if (_launchWatch > 0)
                Preferences.SetInt(OpeningKey, 0);   // closed on purpose, not stopped
        }

        // OnDestroy (WorkbenchPanel.cs) takes the mark off too: a scene unloaded -- a look changed, a test done -- is no stop.

        /// <summary>
        /// Opens the workbench on what was open last (<see cref="Documents.LaunchKey"/>): that circuit, that sample, or with
        /// nothing, the start page over MY CIRCUIT, which Escape leaves it for.
        /// </summary>
        private void Launch()
        {
            if (_canvas == null)
                return;

            ProgressStore store = Store();
            string key = Documents.LaunchKey(store != null ? store.LastOpen : null, DocumentKeys());

            if (key != null && Preferences.GetInt(OpeningKey, 0) == 1)
            {
                Preferences.SetInt(OpeningKey, 0);
                _documentKey = WorkbenchSamples.MyCircuitKey;
                _launchSample = 0;
                Open();
                ShowHome();
                Notice(StoppedOpening);
                return;
            }

            // Marked before it opens: written out at once, so in a browser the mark is on its way before the open can stop it.
            if (key != null)
            {
                Preferences.SetInt(OpeningKey, 1);
                _launchWatch = SurvivedFrames;
            }

            // An exercise open last opens again, while it is one of the run (Part 15d); one since taken out, the start page.
            string exerciseId = Documents.ExerciseOf(key);
            if (exerciseId != null)
            {
                Exercise exercise = ExerciseRun.Find(ExerciseCatalog.Run, exerciseId);
                if (exercise != null)
                {
                    _exercise = exercise;
                    _documentKey = exercise.Id;
                    _exerciseNote = null;
                    _launchSample = 0;
                    Open();
                    GameAnalytics.ExerciseStarted(exercise.Id);
                    if (_exerciseNote != null)
                        Notice(_exerciseNote);   // a kept circuit moved aside as it opened
                    return;
                }
                key = null;
            }

            int sample = Documents.SampleOf(key);
            if (sample > 0)
            {
                _launchSample = sample;

                // The circuit the SAMPLE chip goes back to: the one opened last, as it was before the sample (the #84 review).
                string circuit = store != null && store.Recent.Count > 0 ? Documents.LaunchKey(store.Recent[0], DocumentKeys()) : null;
                if (Documents.IsDocument(circuit))
                    _documentKey = circuit;
            }
            else
            {
                _documentKey = key ?? WorkbenchSamples.MyCircuitKey;
                _launchSample = 0;
            }

            Open();
            if (key == null)
                ShowHome();
        }

        /// <summary>
        /// The workbench back from what was over it or it went under -- Settings or the credits over it, pages of its own since
        /// Part 15g, the 4.x game under it -- as it was left: the circuit, the
        /// moment and whether it was playing. The first time, it opens as <see cref="Open"/> does.
        /// </summary>
        public void Resume()
        {
            if (_canvas == null)
                return;
            if (Root == null)
            {
                Open();
                return;
            }

            SetShowing(true);
            Fit();
            if (_forgotten)
            {
                _forgotten = false;
                OpenAsNew();
            }
            else if (_homeWhenBack && !AnotherPageShowing)
            {
                ShowHome();   // RESET PROGRESS took the exercise from under Settings
            }
            if (StartPageShowing)
            {
                // A reset made under it is shown: its CONTINUE and its count (the 15g-1 review -- a reset with the start
                // page up left it saying what had been solved, until HOME).
                FillRecent();
                FillExercises();
                _home.BringToFront();
            }
            if (ExercisesShowing)
            {
                FillExercises();   // a solve made under it, or a reset, is shown
                _exercisesPage.BringToFront();
            }
        }

        /// <summary>After RESET PROGRESS, as on a first launch: MY CIRCUIT, blank, with the start page over it.</summary>
        private void OpenAsNew()
        {
            ShowSample(0);
            ShowHome();
        }

        /// <summary>Settings, a page of the workbench's own over what is open, which BACK takes down again (Part 15g).</summary>
        public void OpenSettings()
        {
            if (_settings == null || Root == null)
                return;
            _settings.OpenOver(this);
        }

        /// <summary>Out of sight under a full-screen panel that will <see cref="Resume"/> it, the circuit kept first.</summary>
        public void GoUnder()
        {
            if (Root == null)
                return;
            KeepMyCircuit(now: true);
            SetShowing(false);
        }

        /// <summary>HELP's menu, under its chip.</summary>
        public void OpenHelpMenu() => _helpMenu?.Open(UnderTheChip(HelpButton));

        private void HelpChosen(HelpAction action)
        {
            switch (action)
            {
                case HelpAction.KeyboardMap:
                    if (!_keyMap.IsOpen)
                        _keyMap.Toggle();
                    break;
                case HelpAction.StartPage:
                    ShowHome();
                    break;
                case HelpAction.Tutorial:
                    StartTutorial();
                    break;
                case HelpAction.About:
                    ShowAbout();   // what BitSorter is, its credits at its foot (Part 15h: it opened the credits roll)
                    break;
            }
        }

        /// <summary>Where a menu opened from the header's chip <paramref name="name"/> goes: under it.</summary>
        private Vector2 UnderTheChip(string name)
        {
            VisualElement chip = _shell != null ? UQueryExtensions.Q(_shell.Root, name) : null;
            if (chip != null && chip.panel != null)
            {
                Rect at = ShellPanel.ScreenRectOf(chip);
                return new Vector2(at.xMin, at.yMin);
            }
            return Vector2.zero;
        }

        // -----------------------------------------------------------------
        // The start page
        // -----------------------------------------------------------------

        /// <summary>Puts the start page up, the run under it paused, and remembers it as what is open.</summary>
        public void ShowHome()
        {
            if (Root == null || _home == null)
                return;

            CloseThePopups();
            HideExercises();
            HideOtherPages();   // Settings or the credits, under the header HOME is on (Part 15g)
            _homeWhenBack = false;
            LetGoOnCanvas();
            _codeView?.Bind(null);   // a written block's code, CodeMirror's box in a browser among it, is under the page
            Pause();
            FillRecent();
            FillExercises();
            _homeNotice.text = "";
            _home.style.display = DisplayStyle.Flex;
            _home.BringToFront();
            SetText(_description, StartSays);
            RememberWhatIsOpen("");
        }

        /// <summary>Takes the start page down, back to the circuit under it.</summary>
        private void HideHome()
        {
            if (!StartPageShowing)
                return;
            _home.style.display = DisplayStyle.None;
            SetText(_description, Says());
        }

        /// <summary>Escape on the start page: down, the circuit under it what is open.</summary>
        private void LeaveHome()
        {
            HideHome();
            RememberWhatIsOpen(OpenKey());
        }

        /// <summary>What is open, as the save remembers it: the circuit's key, the exercise's (Part 15d), or the sample's.</summary>
        private string OpenKey() =>
            _sample != 0 ? Documents.SampleKey(WorkbenchSamples.Chip[_sample].Name)
            : _exercise != null ? Documents.ExerciseKey(_exercise.Id)
            : _documentKey;

        /// <summary>Writes <paramref name="key"/> down as what BitSorter opens on next (<see cref="ProgressStore.LastOpen"/>).</summary>
        private static void RememberWhatIsOpen(string key)
        {
            ProgressStore store = Store();
            if (store == null || store.LastOpen == key)
                return;
            store.LastOpen = key;
            store.Save();
        }

        private void BuildHome(SchematicPalette palette)
        {
            _helpMenu = HelpMenu.Build(_shell, transform, palette, HelpChosen);
            _settings = FindFirstObjectByType<SettingsPanel>();
            _credits = FindFirstObjectByType<CreditsPanel>();

            _home = new VisualElement { name = StartPageName, pickingMode = PickingMode.Position };
            _home.style.position = Position.Absolute;
            _home.style.left = 0f;
            _home.style.right = 0f;
            _home.style.top = TitleTop + HeaderHeight;   // from the header's foot: the controls' keys stand above their row
            _home.style.bottom = 0f;
            _home.style.backgroundColor = palette.Ground;
            _home.style.flexDirection = FlexDirection.Column;
            _home.style.alignItems = Align.Center;
            _home.style.paddingTop = 64f;
            _home.style.display = DisplayStyle.None;

            Label name = ShellTheme.Label("name", AppName, UiType.Display, palette.Accent, TextAnchor.MiddleCenter, bold: true);
            _home.Add(name);
            Label tagline = ShellTheme.Label("tagline", Tagline, UiType.Lead, palette.TextDim, TextAnchor.MiddleCenter);
            tagline.style.marginTop = 6f;
            _home.Add(tagline);

            var columns = new VisualElement { name = "columns", pickingMode = PickingMode.Ignore };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.marginTop = 48f;
            _home.Add(columns);

            VisualElement start = Column(columns, "START", palette, first: true);
            Item(start, StartNewItem, "NEW CIRCUIT", AskNewCircuit, palette);
            Item(start, StartOpenItem, "OPEN A CIRCUIT", OpenOpenMenu, palette);
            Item(start, StartImportItem, "IMPORT A PROJECT", () => FileChosen(FileImport.Picks ? FileAction.ImportFiles : FileAction.ImportFolder), palette);
            Heading(start, "HELP", palette, 28f);
            Item(start, StartTutorialItem, "TUTORIAL", StartTutorial, palette);
            Item(start, StartKeysItem, "KEYBOARD MAP", () => { if (!_keyMap.IsOpen) _keyMap.Toggle(); }, palette);
            Item(start, StartSettingsItem, "SETTINGS", OpenSettings, palette);

            VisualElement recent = Column(columns, "RECENT", palette, first: false);
            for (int i = 0; i < RecentShown; i++)
            {
                int at = i;
                _recentItems[i] = Item(recent, RecentItem(i), "", () => OpenDocument(_recentKeys[at]), palette);
            }
            _recentNone = ShellTheme.Label("none", "", UiType.Label, palette.Muted);
            recent.Add(_recentNone);

            VisualElement samples = Column(columns, "SAMPLES", palette, first: false);
            for (int i = 0; i < WorkbenchSamples.All.Count; i++)
            {
                if (IsProcessor(WorkbenchSamples.All[i].Name))
                    continue;   // a card of its own, under RISC-V PROCESSORS (Part 15h-6c)
                int chip = i + 1;
                Item(samples, SampleItem(i), WorkbenchSamples.All[i].Name.ToUpperInvariant(), () => ShowSample(chip), palette);
            }

            VisualElement exercises = BuildStartExercises(columns, palette);
            BuildStartProcessors(exercises, palette);

            _homeNotice = ShellTheme.Label("notice", "", UiType.Body, palette.Accent, TextAnchor.MiddleCenter);
            _homeNotice.style.whiteSpace = WhiteSpace.Normal;
            _homeNotice.style.marginTop = 32f;
            _homeNotice.style.maxWidth = 4f * HomeColumnWidth + 3f * HomeColumnGap;
            _home.Add(_homeNotice);

            Label version = ShellTheme.Label("version", "v" + Application.version, UiType.Micro, palette.Muted, TextAnchor.MiddleCenter);
            version.style.position = Position.Absolute;
            version.style.bottom = 16f;
            version.style.left = 0f;
            version.style.right = 0f;
            _home.Add(version);

            _shell.Root.Add(_home);
        }

        /// <summary>How wide a column of the start page is, and the room between two.</summary>
        private const float HomeColumnWidth = 340f, HomeColumnGap = 40f;

        private static VisualElement Column(VisualElement columns, string heading, SchematicPalette palette, bool first)
        {
            var column = new VisualElement { name = heading.ToLowerInvariant(), pickingMode = PickingMode.Ignore };
            column.style.width = HomeColumnWidth;
            column.style.marginLeft = first ? 0f : HomeColumnGap;
            columns.Add(column);
            Heading(column, heading, palette, 0f);
            return column;
        }

        private static void Heading(VisualElement column, string heading, SchematicPalette palette, float above)
        {
            Label label = ShellTheme.Label("heading", heading, UiType.Caption, palette.Muted, TextAnchor.MiddleLeft, bold: true);
            label.style.marginTop = above;
            label.style.marginBottom = 10f;
            column.Add(label);
        }

        private static ShellButton Item(VisualElement column, string name, string caption, System.Action chosen, SchematicPalette palette)
        {
            ShellButton item = ShellTheme.Button(name, caption, palette, chosen);
            ShellTheme.Text(item, UiType.Body, palette.Text, TextAnchor.MiddleLeft);
            item.style.height = 38f;
            item.style.marginBottom = 8f;
            item.style.paddingLeft = 14f;
            column.Add(item);
            return item;
        }

        /// <summary>
        /// RISC-V PROCESSORS (Part 15h-6c), under EXERCISES: a card for each processor -- its name, which opens it as SAMPLES
        /// would, what it shows in a line, and its three programs, each opening it running that one, PROGRAM chosen as its chip
        /// chooses it.
        /// </summary>
        private void BuildStartProcessors(VisualElement column, SchematicPalette palette)
        {
            Heading(column, "RISC-V PROCESSORS", palette, 28f);
            for (int i = 0; i < WorkbenchSamples.All.Count; i++)
            {
                string name = WorkbenchSamples.All[i].Name;
                int processor = System.Array.FindIndex(Processors, p => p.Name == name);
                if (processor < 0)
                    continue;
                int chip = i + 1;

                var card = new VisualElement { name = "processor", pickingMode = PickingMode.Ignore };
                card.style.marginBottom = 12f;
                column.Add(card);
                Item(card, SampleItem(i), name.ToUpperInvariant(), () => ShowSample(chip), palette).style.marginBottom = 4f;

                Label line = ShellTheme.Label("says", Processors[processor].Line, UiType.Caption, palette.TextDim);
                line.style.marginLeft = 14f;
                line.style.marginBottom = 6f;
                card.Add(line);

                var row = new VisualElement { name = "programs", pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                card.Add(row);
                const float gap = 8f;
                float width = (HomeColumnWidth - 2f * gap) / Programs.Length;
                for (int p = 0; p < Programs.Length; p++)
                {
                    string member = Programs[p].Member;
                    ShellButton program = ShellTheme.Button(ProcessorItem(i, member), Programs[p].Caption, palette, () => OpenProcessor(chip, member), UiType.Caption);
                    program.style.width = width;
                    program.style.height = 28f;
                    program.style.marginLeft = p == 0 ? 0f : gap;
                    row.Add(program);
                }
            }
        }

        /// <summary>The processor sample <paramref name="chip"/> opened, running <paramref name="program"/>: one step of its document.</summary>
        public void OpenProcessor(int chip, string program)
        {
            ShowSample(chip);
            if (_session != null && _session.Choices.Count > 0 && _session.Choices[0].Chosen != program)
                Choose(_session.Choices[0].Name, program);   // the program it opens on -- the one last chosen, else SUM -- is no step
        }

        /// <summary>RECENT made again from the save: the circuits the latest opened first, as many as it shows.</summary>
        private void FillRecent()
        {
            ProgressStore store = Store();
            IReadOnlyList<string> keys = Documents.InOrder(DocumentKeys(), store != null ? store.Recent : null);
            _recentKeys.Clear();
            for (int i = 0; i < RecentShown; i++)
            {
                bool shown = i < keys.Count;
                if (shown)
                {
                    _recentKeys.Add(keys[i]);
                    _recentItems[i].text = UiTheme.Shortened(Documents.NameOf(keys[i]).ToUpperInvariant(), HomeColumnWidth - 40f, UiType.Body);
                }
                _recentItems[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _recentNone.text = keys.Count > RecentShown ? "and " + (keys.Count - RecentShown) + " more in OPEN A CIRCUIT" : "";
            _recentNone.style.display = keys.Count > RecentShown ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
