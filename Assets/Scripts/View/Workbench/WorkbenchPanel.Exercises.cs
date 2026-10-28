using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The exercises on the workbench (Part 15d): EXERCISES in the header lists them, chapter by chapter, solved ones and
    /// the next one marked, and the start page has a column of its own to carry on from. An exercise open is a document
    /// like a circuit, kept under its id -- so a 4.x board of its level opens as it was left -- with its test, its library
    /// and the blocks it stocks locked (<see cref="BoardEditor"/>). The header says its number, title and goal; GOAL opens
    /// its brief, the hint behind an item; CHECK grades it, every failed check a TRANSCRIPT line that goes to its moment,
    /// and once it is solved a card says what it took and the student's best, and offers the next.
    /// </summary>
    /// <remarks>
    /// Only the student's own numbers are shown: what this design took, and what theirs did before (CLAUDE.md, "The line
    /// is ranking, not measurement"). An exercise opened and an exercise solved are reported as a level started and solved,
    /// under its id, once a session -- the same two events and the same parameter as ever.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The milestone that says an exercise's 4.x board has been migrated, its id following.</summary>
        private const string MigratedMilestone = "exercise-migrated/";

        /// <summary>The EXERCISES chip, the page's name, and the names of GOAL, CHECK and WRITE IT (Part 15e).</summary>
        public const string ExercisesButton = "Workbench exercises", ExercisesPageName = "Workbench exercises page",
            GoalButton = "Workbench goal", CheckButton = "Workbench check", CodeButton = "Workbench write it";

        /// <summary>What WRITE IT says while the answer is drawn, and while it is written.</summary>
        public const string WriteItCaption = "WRITE IT", DrawItCaption = "DRAW IT";

        /// <summary>The start page's items for the exercises: carry on with the next, and the whole list.</summary>
        public const string StartContinueItem = StartItem + "continue", StartExercisesItem = StartItem + "exercises";

        /// <summary>The name of the EXERCISES page's item for <paramref name="id"/>.</summary>
        public static string ExerciseItem(string id) => "Workbench exercise " + id;

        /// <summary>What the header's line says while the EXERCISES page is up.</summary>
        public const string ExercisesSays = "Pick an exercise: each is a circuit to build and CHECK.";

        private Exercise _exercise;
        private VisualElement _exercisesPage;
        private Label _exercisesProgress, _exercisesNotice, _startProgress;
        private ShellButton _goalButton, _checkButton, _codeButton, _startContinue;
        private readonly Dictionary<string, (ShellButton Item, Label Status)> _exerciseItems = new Dictionary<string, (ShellButton, Label)>();
        private ExerciseCard _exerciseCard;
        private string _exerciseNote;

        /// <summary>The exercise open, or null for a circuit of the student's own.</summary>
        public Exercise ExerciseOpen => _exercise;

        /// <summary>The exercise's card: its brief, and the solved card.</summary>
        public ExerciseCard ExerciseCard => _exerciseCard;

        /// <summary>The grade the last CHECK gave, or null.</summary>
        public Grade LastGrade { get; private set; }

        /// <summary>What the header's line says now: the goal of the exercise open, a sample's line, or a page's.</summary>
        public string Description => _description?.text;

        /// <summary>Whether the EXERCISES page is up.</summary>
        public bool ExercisesShowing => _exercisesPage != null && _exercisesPage.style.display == DisplayStyle.Flex;

        /// <summary>Whether a page covers the circuit -- the start page or EXERCISES -- so nothing under it takes a key.</summary>
        private bool PageShowing => StartPageShowing || ExercisesShowing || AnotherPageShowing;

        /// <summary>What the EXERCISES page says under its lists: the last notice given while it was up.</summary>
        public string ExercisesNotice => _exercisesNotice != null ? _exercisesNotice.text : null;

        /// <summary>What the start page says of the next exercise, on its CONTINUE item.</summary>
        public string StartContinueCaption => _startContinue != null ? _startContinue.text : null;

        /// <summary>What the EXERCISES page's item for <paramref name="id"/> says beside it: SOLVED, NEXT or nothing.</summary>
        public string ExerciseStatus(string id) => _exerciseItems.TryGetValue(id, out var item) ? item.Status.text : null;

        private static bool Solved(string id)
        {
            ProgressStore store = Store();
            return store != null && store.IsComplete(id);
        }

        /// <summary>Whether an exercise is solved, by a store found once rather than once an exercise.</summary>
        private static System.Func<string, bool> SolvedIn(ProgressStore store) => id => store != null && store.IsComplete(id);

        /// <summary>The header's line: the exercise's goal while one is open in place 0, or the sample's line.</summary>
        private string Says() => _sample == 0 && _exercise != null ? _exercise.Goal : WorkbenchSamples.Chip[_sample].Says;

        /// <summary>How the exercise open is headed: its number in the run and its title.</summary>
        private string ExerciseHeading(Exercise exercise) => ExerciseRun.Heading(ExerciseRun.NumberOf(ExerciseCatalog.Run, exercise.Id), exercise);

        // -----------------------------------------------------------------
        // Opening one
        // -----------------------------------------------------------------

        /// <summary>
        /// Opens the exercise <paramref name="id"/>: the circuit open kept first, under its own key, and the exercise's own
        /// document built -- the student's answer kept from before, made the exercise's again, or the exercise as it starts --
        /// and its brief put up.
        /// </summary>
        public void OpenExercise(string id)
        {
            if (Root == null)
                return;
            Exercise exercise = ExerciseRun.Find(ExerciseCatalog.Run, id);
            if (exercise == null)
            {
                Notice("there is no exercise " + id);
                return;
            }

            if (exercise == _exercise && _sample == 0)
            {
                HideExercises();
                HideOtherPages();   // Settings or the credits, HELP's TUTORIAL chosen over them (the 15g-2 review)
                LeaveHome();   // open already: building it again would lose its selection and undo
                return;
            }

            KeepMyCircuit(now: true);
            if (_session != null)
                _keptProject = _session.Project;   // kept: nothing of it is written under the exercise's key
            _keepAt = -1;
            _exercise = exercise;
            _documentKey = exercise.Id;
            _exerciseNote = null;

            HideExercises();
            ShowSample(0);
            GameAnalytics.ExerciseStarted(exercise.Id);
            OpenGoal();
            if (_exerciseNote != null)
                Notice(_exerciseNote);
        }

        /// <summary>
        /// The exercise's document: the student's answer the save keeps under its id -- or the 4.x board of its level,
        /// migrated now -- made the exercise's again (<see cref="Exercise.Adopt"/>); or, with none, the exercise as it starts.
        /// A kept circuit that is no answer to it is not lost: it is moved to a circuit of its own, and the notice says where.
        /// </summary>
        private Project ExerciseDocument(Exercise exercise)
        {
            var problems = new List<string>();
            ProgressStore store = Store();
            if (store != null)
            {
                // A 4.x board of its level, migrated the first time only: one moved aside below is not made again.
                var notes = new List<string>();
                string migrated = MigratedMilestone + exercise.Id;
                if (!store.HasProject(exercise.Id) && !store.HasMilestone(migrated) && SaveMigration.MigrateOne(store, exercise.Id, notes))
                {
                    store.MarkMilestone(migrated);
                    store.Save();
                }

                if (store.TryGetProject(exercise.Id, out Project kept, problems) && kept.TopModule != null)
                {
                    Project adopted = exercise.Adopt(kept, problems);
                    if (adopted != null)
                        return adopted;

                    // No answer to it now -- the exercise changed under it -- so a circuit of the student's own, unlocked,
                    // with its test as it was (the #88 review: renamed, it kept the exercise's lock and could not be edited).
                    string name = Documents.FreeName(DocumentKeys(), exercise.Title + " old");
                    Project own = kept with
                    {
                        Settings = (kept.Settings ?? new ProjectSettings()) with { Exercise = null, Library = null, RegisterStart = null },
                    };
                    store.SetProject(Documents.KeyOf(name), own);
                    store.RemoveProject(exercise.Id);
                    store.Save();
                    _exerciseNote = "the circuit kept for " + exercise.Title + " no longer fits it, so it opens fresh: yours is kept as " +
                                    name + ", in FILE, OPEN";
                }
            }

            problems.Clear();
            Project fresh = exercise.ToProject(problems);
            if (fresh == null)
                Debug.LogError($"BitSorter: the exercise {exercise.Id} did not open: {string.Join(" ", problems)}");
            return fresh ?? WorkbenchSamples.Blank();
        }

        /// <summary>Back to a circuit of the student's own: what opening one, or making one, does first.</summary>
        private void LeaveTheExercise()
        {
            _exercise = null;
            _goalPending = false;
            _exerciseCard?.Close();
        }

        /// <summary>
        /// GOAL and CHECK on the controls' row while an exercise is open in place 0 -- and WRITE IT, where it may be answered
        /// in code (Part 15e) -- the chip beside them made room for.
        /// </summary>
        private void ShowExerciseControls()
        {
            bool shown = _exercise != null && _sample == 0, code = shown && _exercise.Code;
            DisplayStyle display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (_goalButton != null)
                _goalButton.style.display = display;
            if (_checkButton != null)
                _checkButton.style.display = display;
            if (_codeButton != null)
                _codeButton.style.display = code ? DisplayStyle.Flex : DisplayStyle.None;
            if (Chip != null)
                Chip.style.right = !shown ? 0f : ExerciseControlsWidth + RowGap + (code ? CodeWidth + RowGap : 0f);
            RefreshCodeButton();
        }

        /// <summary>WRITE IT or DRAW IT, as the answer is now: an undo can turn it either way.</summary>
        private void RefreshCodeButton()
        {
            if (_codeButton != null && _session != null)
                SetText(_codeButton, _session.Project.TopModule?.IsWritten == true ? DrawItCaption : WriteItCaption);
        }

        /// <summary>
        /// WRITE IT (Part 15e): the answer's top written as code -- the drawing as it prints, under a header saying the goal and
        /// what it may be built of -- held to the library's cells and the stock as a drawing is. DRAW IT asks first, since the
        /// code is not kept but by undo, and draws the code as gates (Part 15g), or the exercise's own top where it cannot.
        /// </summary>
        public void WriteOrDraw()
        {
            if (_session == null || _exercise == null || _sample != 0 || !_exercise.Code)
                return;
            if (_session.Project.TopModule?.IsWritten == true)
                _ask.Ask(UnderTheChip(CodeButton), DrawItQuestion,
                    DrawItCaption, DrawIt);
            else
                WriteIt();
        }

        /// <summary>The answer written as code, now: what WRITE IT does.</summary>
        public void WriteIt()
        {
            if (_session == null || _exercise == null || _session.Project.TopModule?.IsWritten == true)
                return;
            EditResult wrote = _session.ReplaceModule(_exercise.Written(_session.Project));
            Said(wrote);
            if (wrote.Done)
                Notice("written as code: gates only, as the header says. Ctrl+Enter applies what you type, and CHECK grades it as a drawing");
            RefreshCodeButton();
        }

        /// <summary>What DRAW IT asks first.</summary>
        public const string DrawItQuestion = "Draw it instead? Your code is drawn as its gates, or, where it cannot be, the exercise opens as it starts. Ctrl+Z brings the code back.";

        /// <summary>
        /// The answer drawn again, what DRAW IT does once asked: the code drawn as its gates (Part 15g), or the exercise as it
        /// starts where the code cannot be drawn, saying why.
        /// </summary>
        public void DrawIt()
        {
            if (_session == null || _exercise == null || _session.Project.TopModule?.IsWritten != true)
                return;
            var problems = new List<string>();
            var notDrawn = new List<string>();
            Module drawn = _exercise.Drawn(_session.Project, problems, notDrawn);
            if (drawn == null)
            {
                Notice("it could not be drawn again: " + string.Join(" ", problems));
                return;
            }
            EditResult replaced = _session.ReplaceModule(drawn);
            Said(replaced);
            if (replaced.Done && notDrawn.Count > 0)
                Notice("the code could not be drawn, so the exercise opens as it starts -- Ctrl+Z brings your code back: " + string.Join(" ", notDrawn));
            RefreshCodeButton();
        }

        // -----------------------------------------------------------------
        // GOAL and CHECK
        // -----------------------------------------------------------------

        /// <summary>
        /// The exercise's brief, under GOAL: its heading, its goal, and its hint behind an item. GOAL shown in this frame has no
        /// place yet -- UI Toolkit lays it out the next -- so the brief waits for it (<see cref="OpenPendingGoal"/>): opened
        /// at once, it went up at the window's left edge (the 15d shots).
        /// </summary>
        public void OpenGoal()
        {
            if (_exercise == null || _exerciseCard == null)
                return;
            if (_goalButton == null || float.IsNaN(_goalButton.layout.width) || _goalButton.layout.width <= 0f)
            {
                _goalPending = true;
                return;
            }

            _goalPending = false;
            _exerciseCard.Brief(UnderTheChip(GoalButton), ExerciseHeading(_exercise), _exercise.Goal, _exercise.Hint);
        }

        /// <summary>The brief asked for before GOAL had its place, up once it has; dropped if the exercise was left meanwhile.</summary>
        private void OpenPendingGoal()
        {
            if (_exercise != null && _sample == 0)
                RefreshCodeButton();
            if (!_goalPending)
                return;
            if (_exercise == null || _sample != 0 || PageShowing)
                _goalPending = false;
            else
                OpenGoal();
        }

        /// <summary>Whether the brief is waiting for GOAL to be laid out.</summary>
        public bool GoalPending => _goalPending;

        private bool _goalPending;

        /// <summary>
        /// Grades the exercise open: the verdict on the chip, every reason it is not solved at the head of the TRANSCRIPT --
        /// a failed check at its moment, and the run taken to the first -- or, solved, the solved card.
        /// </summary>
        public void Check()
        {
            if (_session == null || _exercise == null || _sample != 0)
                return;

            KeepMyCircuit(now: true);
            Grade grade = ExerciseGrader.Grade(_exercise, _session.Project);
            LastGrade = grade;
            string unit = _session.Unit;

            _transcript.Notes = ExerciseRun.Lines(_exercise, grade, unit);
            _notesAbout = _session.Project;
            _transcriptDrawn = -1;
            Notice(ExerciseRun.Verdict(_exercise, grade, unit));
            LessonsAfterCheck(grade);

            if (!grade.Solved)
            {
                ShowDockTab(DockTab.Transcript);
                if (grade.Failures.Count > 0)
                {
                    Pause();
                    Seek(grade.Failures[0].Time);
                }
                return;
            }

            ShowSolved(grade, unit);
        }

        /// <summary>The solve kept -- the exercise done, the student's best -- and the solved card up, offering the next.</summary>
        private void ShowSolved(Grade grade, string unit)
        {
            Exercise solved = _exercise;
            ProgressStore store = Store();
            bool hadBest = false, fewer = false, quicker = false;
            int bestGates = 0;
            long? bestSettle = null;
            if (store != null)
            {
                hadBest = store.TryGetExerciseBest(solved.Id, out bestGates, out bestSettle);
                store.MarkComplete(solved.Id);
                (fewer, quicker) = store.RecordExerciseBest(solved.Id, grade.Gates, grade.Settle);
                store.Save();
            }
            GameAnalytics.ExerciseSolved(solved.Id);

            string took = "It took " + ExerciseRun.Took(solved, grade, unit) + ".";
            string best = !hadBest ? "Your first solve of it."
                : fewer || quicker ? "Your best before: " + ExerciseRun.Took(bestGates, bestSettle, unit) + ". This one beats it."
                : "Your best: " + ExerciseRun.Took(bestGates, bestSettle, unit) + ".";
            Exercise next = ExerciseRun.Next(ExerciseCatalog.Run, Solved, after: solved.Id);
            _exerciseCard.Solve(UnderTheChip(GoalButton), ExerciseHeading(solved), took, best,
                next != null ? "NEXT: " + ExerciseHeading(next) : null,
                next != null ? () => OpenExercise(next.Id) : (System.Action)null,
                ShowExercises);
        }

        // -----------------------------------------------------------------
        // The EXERCISES page
        // -----------------------------------------------------------------

        /// <summary>Puts EXERCISES up over what is open, the run under it paused; Escape takes it down again.</summary>
        public void ShowExercises()
        {
            HideOtherPages();   // Settings or the credits, under the header it was chosen from (Part 15g)
            if (Root == null || _exercisesPage == null)
                return;
            _homeWhenBack = false;

            CloseThePopups();
            LetGoOnCanvas();
            _codeView?.Bind(null);   // a written block's code, CodeMirror's box in a browser among it, is under the page
            Pause();
            FillExercises();
            _exercisesNotice.text = "";
            _exercisesPage.style.display = DisplayStyle.Flex;
            _exercisesPage.BringToFront();
            SetText(_description, ExercisesSays);
        }

        /// <summary>Takes EXERCISES down, back to what is under it: the start page, or what is open.</summary>
        private void HideExercises()
        {
            if (!ExercisesShowing)
                return;
            _exercisesPage.style.display = DisplayStyle.None;
            SetText(_description, StartPageShowing ? StartSays : Says());
        }

        private void BuildExercises(SchematicPalette palette)
        {
            _exerciseCard = ExerciseCard.Build(_shell, transform, palette);

            _exercisesPage = new VisualElement { name = ExercisesPageName, pickingMode = PickingMode.Position };
            _exercisesPage.style.position = Position.Absolute;
            _exercisesPage.style.left = 0f;
            _exercisesPage.style.right = 0f;
            _exercisesPage.style.top = TitleTop + HeaderHeight;
            _exercisesPage.style.bottom = 0f;
            _exercisesPage.style.backgroundColor = palette.Ground;
            _exercisesPage.style.flexDirection = FlexDirection.Column;
            _exercisesPage.style.alignItems = Align.Center;
            _exercisesPage.style.paddingTop = 40f;
            _exercisesPage.style.display = DisplayStyle.None;

            Label title = ShellTheme.Label("title", "EXERCISES", UiType.Title, palette.Accent, TextAnchor.MiddleCenter, bold: true);
            _exercisesPage.Add(title);
            _exercisesProgress = ShellTheme.Label("progress", "", UiType.Lead, palette.TextDim, TextAnchor.MiddleCenter);
            _exercisesProgress.style.marginTop = 4f;
            _exercisesPage.Add(_exercisesProgress);

            var columns = new VisualElement { name = "columns", pickingMode = PickingMode.Ignore };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.marginTop = 28f;
            _exercisesPage.Add(columns);

            IReadOnlyList<Exercise> run = ExerciseCatalog.Run;
            var chapters = ExerciseRun.Chapters(run);
            bool firstColumn = true;
            foreach (IReadOnlyList<int> placed in ExerciseRun.Columns(chapters, ExerciseColumns))
            {
                var column = new VisualElement { name = "column", pickingMode = PickingMode.Ignore };
                column.style.width = ExerciseColumnWidth;
                column.style.marginLeft = firstColumn ? 0f : HomeColumnGap;
                columns.Add(column);
                firstColumn = false;

                bool firstChapter = true;
                foreach (int at in placed)
                {
                    Heading(column, chapters[at].Chapter + "  " + ExerciseRun.ChapterName(chapters[at].Chapter), palette, firstChapter ? 0f : 18f);
                    firstChapter = false;
                    foreach (Exercise exercise in chapters[at].Exercises)
                        ExerciseEntry(column, exercise, palette);
                }
            }

            _exercisesNotice = ShellTheme.Label("notice", "", UiType.Body, palette.Accent, TextAnchor.MiddleCenter);
            _exercisesNotice.style.whiteSpace = WhiteSpace.Normal;
            _exercisesNotice.style.marginTop = 24f;
            _exercisesNotice.style.maxWidth = ExerciseColumns * ExerciseColumnWidth + (ExerciseColumns - 1) * HomeColumnGap;
            _exercisesPage.Add(_exercisesNotice);

            _shell.Root.Add(_exercisesPage);
        }

        /// <summary>An exercise's item on the page: its heading, opening it, and beside it whether it is solved or next.</summary>
        private void ExerciseEntry(VisualElement column, Exercise exercise, SchematicPalette palette)
        {
            string id = exercise.Id;
            ShellButton item = ShellTheme.Button(ExerciseItem(id), "", palette, () => OpenExercise(id));
            item.text = UiTheme.Shortened(ExerciseHeading(exercise), ExerciseColumnWidth - 120f, UiType.Label);
            ShellTheme.Text(item, UiType.Label, palette.Text, TextAnchor.MiddleLeft);
            item.style.height = 30f;
            item.style.marginBottom = 4f;
            item.style.paddingLeft = 12f;

            Label status = ShellTheme.Label("status", "", UiType.Caption, palette.TextDim, TextAnchor.MiddleRight, bold: true);
            status.pickingMode = PickingMode.Ignore;
            status.style.position = Position.Absolute;
            status.style.right = 12f;
            status.style.top = 0f;
            status.style.bottom = 0f;
            item.Add(status);

            column.Add(item);
            _exerciseItems[id] = (item, status);
        }

        /// <summary>The page and the start page's column made again from the save: what is solved, and what is next.</summary>
        private void FillExercises()
        {
            IReadOnlyList<Exercise> run = ExerciseCatalog.Run;
            System.Func<string, bool> isSolved = SolvedIn(Store());
            Exercise next = NextToDo(isSolved);
            int solved = ExerciseRun.SolvedCount(run, isSolved);
            string progress = solved + " of " + run.Count + " solved";

            SchematicPalette palette = SchematicPalette.Current;
            foreach (Exercise exercise in run)
            {
                if (!_exerciseItems.TryGetValue(exercise.Id, out var entry))
                    continue;
                bool done = isSolved(exercise.Id), isNext = next != null && exercise.Id == next.Id;
                entry.Status.text = done ? "SOLVED" : isNext ? "NEXT" : "";
                entry.Status.style.color = isNext ? palette.Accent : palette.TextDim;
            }

            if (_exercisesProgress != null)
                _exercisesProgress.text = progress;
            if (_startProgress != null)
                _startProgress.text = progress;
            if (_startContinue != null)
            {
                _startContinue.text = next == null ? "EVERY ONE SOLVED"
                    : UiTheme.Shortened((solved == 0 ? "START: " : "NEXT: ") + ExerciseHeading(next), HomeColumnWidth - 40f, UiType.Body);
                _startContinue.SetEnabled(next != null);
            }
        }

        /// <summary>
        /// The exercise to do next: the one open, while it is unsolved, or the first unsolved after it. It was always the one
        /// after, so the page marked the next exercise while the student was still on this one (the 15f browser smoke test).
        /// </summary>
        private Exercise NextToDo(System.Func<string, bool> solved) =>
            _exercise != null && !solved(_exercise.Id) ? _exercise : ExerciseRun.Next(ExerciseCatalog.Run, solved, after: _exercise?.Id);

        /// <summary>The start page's EXERCISES column: carry on with the next, the whole list, and how far the student is.</summary>
        private void BuildStartExercises(VisualElement columns, SchematicPalette palette)
        {
            VisualElement column = Column(columns, "EXERCISES", palette, first: false);
            _startContinue = Item(column, StartContinueItem, "", ContinueExercises, palette);
            Item(column, StartExercisesItem, "ALL EXERCISES", ShowExercises, palette);
            _startProgress = ShellTheme.Label("progress", "", UiType.Label, palette.Muted);
            column.Add(_startProgress);
        }

        /// <summary>CONTINUE on the start page: the next exercise to do, after the one open last.</summary>
        private void ContinueExercises()
        {
            Exercise next = NextToDo(SolvedIn(Store()));
            if (next != null)
                OpenExercise(next.Id);
        }

        /// <summary>How many columns the EXERCISES page has, and how wide each is.</summary>
        private const int ExerciseColumns = 3;

        /// <inheritdoc cref="ExerciseColumns"/>
        private const float ExerciseColumnWidth = 400f;

        /// <summary>How wide GOAL and CHECK are on the controls' row, together.</summary>
        private const float ExerciseControlsWidth = GoalWidth + RowGap + CheckWidth;

        private const float GoalWidth = 100f, CheckWidth = 120f, CodeWidth = 130f;

        /// <summary>GOAL and CHECK, at the controls' row's right end, shown while an exercise is open.</summary>
        private void BuildExerciseControls(SchematicPalette palette)
        {
            _checkButton = ShellTheme.Button(CheckButton, "CHECK", palette, Check);
            ShellTheme.Outline(_checkButton, palette.Accent, palette.Ground);
            ShellTheme.Text(_checkButton, UiType.Body, palette.Accent, TextAnchor.MiddleCenter, bold: true);
            ShellTheme.PlaceRight(_checkButton, 0f, 0f, CheckWidth, UiTheme.ButtonHeight);
            _controls.Add(_checkButton);

            _goalButton = ShellTheme.Button(GoalButton, "GOAL", palette, OpenGoal);
            ShellTheme.PlaceRight(_goalButton, CheckWidth + RowGap, 0f, GoalWidth, UiTheme.ButtonHeight);
            _controls.Add(_goalButton);

            _codeButton = ShellTheme.Button(CodeButton, WriteItCaption, palette, WriteOrDraw);
            ShellTheme.PlaceRight(_codeButton, ExerciseControlsWidth + RowGap, 0f, CodeWidth, UiTheme.ButtonHeight);
            _controls.Add(_codeButton);

            ShowExerciseControls();
        }
    }
}
