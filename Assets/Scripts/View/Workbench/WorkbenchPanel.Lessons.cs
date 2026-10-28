using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// What the workbench teaches by itself (Part 15f), on a strip along the circuit window's foot: the guided tutorial's
    /// steps, on the first exercise, from the start page and HELP; and the first-time hints, each once ever, as the run
    /// first shows what it explains (<see cref="SimulatorHints"/>). The tutorial's step is read off the document every
    /// frame (<see cref="GuidedTutorial"/>), so nothing blocks input and an undo takes a step back by itself; it lights the
    /// control a step means, and SKIP stops it. A hint is marked seen as it goes up, and GOT IT takes it down.
    /// </summary>
    /// <remarks>
    /// The tutorial has the strip while it runs; a hint that comes due meanwhile waits for it, since it is earned by the run
    /// and the run will earn it again.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The strip's name, its words' and its button's; and the start page's TUTORIAL.</summary>
        public const string LessonName = "Workbench lesson", LessonTextName = "Workbench lesson text", LessonButtonName = "Workbench lesson button",
            StartTutorialItem = StartItem + "tutorial";

        /// <summary>What a hint is dismissed with, the tutorial skipped with, and its end closed with.</summary>
        public const string GotIt = "GOT IT", Skip = "SKIP", CloseLesson = "CLOSE";

        /// <summary>How many frames apart a run is looked at for a first-time hint it has earned.</summary>
        private const int HintEvery = 30;

        private VisualElement _lesson;
        private Label _lessonText;
        private ShellButton _lessonButton;
        private bool _tutorial, _tutorialSolved;
        private GuidedTutorial.Step _tutorialStep = NoStep;
        private string _hintShown;
        private int _hintTick;
        private VisualElement _litControl;
        private StyleColor _litControlEdge;
        private StyleFloat _litControlWidth;

        /// <summary>The document the slowest path was worked out for, and it: what bounds a glitch, reckoned once a document.</summary>
        private Project _slowestFor;
        private long? _slowest;

        private const GuidedTutorial.Step NoStep = (GuidedTutorial.Step)(-1);

        /// <summary>Whether the guided tutorial is running, and the step it is at.</summary>
        public bool TutorialRunning => _tutorial;

        /// <inheritdoc cref="TutorialRunning"/>
        public GuidedTutorial.Step TutorialStep => _tutorialStep;

        /// <summary>What the strip says now; null while it is down.</summary>
        public string LessonText => _lesson != null && _lesson.style.display == DisplayStyle.Flex ? _lessonText.text : null;

        /// <summary>The first-time hint up now, or null.</summary>
        public string HintShown => _hintShown;

        /// <summary>The control a tutorial step lights now, or null.</summary>
        public VisualElement Lit => _litControl;

        private void BuildLesson(SchematicPalette palette)
        {
            _lesson = ShellTheme.Chip(LessonName, palette);
            _lesson.pickingMode = PickingMode.Position;
            _lesson.AddToClassList(ShellTheme.ControlClass);   // a click on it is the strip's, never the circuit's under it
            _lesson.style.position = Position.Absolute;
            _lesson.style.left = 12f;
            _lesson.style.right = 12f;
            _lesson.style.bottom = 12f;
            _lesson.style.flexDirection = FlexDirection.Row;
            _lesson.style.alignItems = Align.Center;
            _lesson.style.paddingLeft = 14f;
            _lesson.style.paddingRight = 8f;
            _lesson.style.paddingTop = _lesson.style.paddingBottom = 8f;
            _lesson.style.display = DisplayStyle.None;

            _lessonText = ShellTheme.Label(LessonTextName, "", UiType.Body, palette.Text);
            _lessonText.style.whiteSpace = WhiteSpace.Normal;
            _lessonText.style.flexGrow = 1f;
            _lessonText.style.flexShrink = 1f;
            _lesson.Add(_lessonText);

            _lessonButton = ShellTheme.Button(LessonButtonName, GotIt, palette, LessonButtonPressed);
            _lessonButton.style.width = 120f;
            _lessonButton.style.height = 34f;
            _lessonButton.style.marginLeft = 12f;
            _lessonButton.style.flexShrink = 0f;
            _lesson.Add(_lessonButton);

            _circuitPanel.Add(_lesson);
        }

        private void ShowLesson(string text, string button)
        {
            if (_lesson == null)
                return;
            _lessonText.text = text;
            _lessonButton.text = button;
            _lesson.style.display = DisplayStyle.Flex;
            _lesson.BringToFront();
        }

        private void HideLesson()
        {
            if (_lesson != null)
                _lesson.style.display = DisplayStyle.None;
        }

        // -----------------------------------------------------------------
        // The guided tutorial
        // -----------------------------------------------------------------

        /// <summary>The guided tutorial, from its first step, on its exercise.</summary>
        public void StartTutorial()
        {
            if (Root == null)
                return;
            if (_hintShown != null)
            {
                _hintShown = null;
                HideLesson();
            }

            OpenExercise(GuidedTutorial.ExerciseId);
            if (_exercise == null || _exercise.Id != GuidedTutorial.ExerciseId)
                return;
            HideExercises();
            if (StartPageShowing)
                LeaveHome();
            _exerciseCard?.Close();   // the strip says what to do; GOAL still opens the brief
            _goalPending = false;
            _tutorial = true;
            _tutorialSolved = false;   // its own CHECK says so: the last grade may be another exercise's (the #90 review)
            _tutorialStep = NoStep;
            TickLessons();
        }

        private void StopTutorial()
        {
            _tutorial = false;
            _tutorialStep = NoStep;
            Unlight();
            HideLesson();
        }

        private void LessonButtonPressed()
        {
            if (_tutorial)
            {
                StopTutorial();
                return;
            }
            _hintShown = null;
            HideLesson();
        }

        /// <summary>
        /// Once a frame: the tutorial's step read off the document, and the strip and the lit control made to say it; or,
        /// with no tutorial and no hint up, every <see cref="HintEvery"/> frames, a first-time hint the run has earned.
        /// </summary>
        private void TickLessons()
        {
            if (_tutorial)
            {
                if (_exercise == null || _exercise.Id != GuidedTutorial.ExerciseId || _sample != 0 || _session == null)
                {
                    StopTutorial();   // it was left for something else
                    return;
                }

                GuidedTutorial.Step step = GuidedTutorial.At(_session.Project, _editor?.InHand, _tutorialSolved);
                if (step != _tutorialStep)
                {
                    _tutorialStep = step;
                    ShowLesson(GuidedTutorial.TextFor(step), step == GuidedTutorial.Step.Done ? CloseLesson : Skip);
                    Unlight();
                }
                if (_litControl == null || _litControl.panel == null)
                    Light(step);   // PARTS made again takes its buttons with it
                return;
            }

            if (_hintShown != null || PageShowing || _session == null || _model == null || ++_hintTick % HintEvery != 0)
                return;
            ProgressStore store = Store();
            if (store == null)
                return;
            if (!store.HasSeenHint(SimulatorHints.Glitch) && !ReferenceEquals(_slowestFor, _session.Project))
            {
                _slowestFor = _session.Project;
                _slowest = SimulatorHints.Slowest(TimingAnalysis.Of(_session.Project));
            }
            string due = SimulatorHints.Due(_session.Project, _model.Session.Design, _model.Session.History, _model.Layout.DesignPath,
                _slowest, store.HasSeenHint);
            if (due != null)
                ShowHint(due);
        }

        /// <summary>A CHECK's part in the lessons: the tutorial's last step met, or the hint an x at a check earns.</summary>
        private void LessonsAfterCheck(Grade grade)
        {
            if (_tutorial)
            {
                _tutorialSolved |= grade.Solved;
                return;
            }
            ProgressStore store = Store();
            string due = store != null ? SimulatorHints.AfterCheck(grade, store.HasSeenHint) : null;
            if (due != null && _hintShown == null)
                ShowHint(due);
        }

        /// <summary>Puts first-time hint <paramref name="id"/> up, marking it seen as it goes: shown once, ever.</summary>
        private void ShowHint(string id)
        {
            ProgressStore store = Store();
            if (store == null || !store.MarkHintSeen(id))
                return;
            _hintShown = id;
            ShowLesson(SimulatorHints.TextFor(id), GotIt);
        }

        /// <summary>The control <paramref name="step"/> means, lit in the accent: the NOT in PARTS, or CHECK.</summary>
        private void Light(GuidedTutorial.Step step)
        {
            VisualElement control = step == GuidedTutorial.Step.TakeTheNot ? (_shell != null ? UQueryExtensions.Q(_shell.Root, PartButtonName("not")) : null)
                : step == GuidedTutorial.Step.Check ? _checkButton
                : null;
            if (control == null || control == _litControl)
                return;
            Unlight();
            _litControl = control;

            // Its own inline style, put back as it was: a button made this frame has no resolved style yet, and putting that
            // back left it edged in nothing (the #90 review).
            _litControlEdge = control.style.borderLeftColor;
            _litControlWidth = control.style.borderLeftWidth;
            Color accent = SchematicPalette.Current.Accent;
            control.style.borderLeftColor = control.style.borderRightColor = control.style.borderTopColor = control.style.borderBottomColor = accent;
            control.style.borderLeftWidth = control.style.borderRightWidth = control.style.borderTopWidth = control.style.borderBottomWidth = 3f;
        }

        private void Unlight()
        {
            if (_litControl == null)
                return;
            _litControl.style.borderLeftColor = _litControl.style.borderRightColor = _litControl.style.borderTopColor = _litControl.style.borderBottomColor = _litControlEdge;
            _litControl.style.borderLeftWidth = _litControl.style.borderRightWidth = _litControl.style.borderTopWidth = _litControl.style.borderBottomWidth = _litControlWidth;
            _litControl = null;
        }
    }
}
