using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's toolbar, in the shell (Part 11a): the title and SAMPLE, the run's controls and the chip that says
    /// what the run is doing, and the timeline -- the slider, the speed and RUN TO. The first of the shell's panels, and
    /// the one its bridges are proved on (<see cref="ShellPanel"/>). Across the window, over the docked panels (Part 11b),
    /// the header stopping short of BACK.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The shell's panel object's name.</summary>
        public const string ShellName = "Workbench shell";

        private ShellPanel _shell;
        private VisualElement _header, _controls, _timeline;
        private Label _description, _chipLabel, _timeLabel, _speedLabel;
        private ShellButton _playButton, _sampleButton;
        private ShellSlider _slider;
        private TextField _runToField;

        /// <summary>The shell's panel: the toolbar, and in time the rest of the workbench's interface.</summary>
        public ShellPanel Shell => _shell;

        /// <summary>What the chip by the controls says now.</summary>
        public string ChipText => _chipLabel?.text;

        /// <summary>The chip by the controls, which a test measures.</summary>
        public VisualElement Chip => _chipLabel?.parent;

        /// <summary>What the speed between SLOWER and FASTER says now.</summary>
        public string SpeedText => _speedLabel?.text;

        /// <summary>What the time label beside the slider says now: <c>t = 11.0 ns</c>.</summary>
        public string MomentText => _timeLabel?.text;

        private void BuildToolbar(SchematicPalette palette)
        {
            _shell = ShellPanel.Create(ShellName, transform);
            _shell.Visible = false;

            // The timeline first, so the controls are drawn over it: a long notice on the chip runs down over its row.
            BuildHeader(palette);
            BuildTimeline(palette);
            BuildControls(palette);

            ShellButton back = ShellTheme.Button(BackButton, "BACK", palette, Back);
            ShellTheme.PlaceRight(back, UiTheme.Margin, UiTheme.Margin, BackWidth, UiTheme.ButtonHeight);
            _shell.Root.Add(back);
        }

        /// <summary>A strip across the top of the window, in from its edges (<see cref="FitToolbar"/>).</summary>
        private VisualElement Strip(string name, float top, float height)
        {
            VisualElement strip = ShellTheme.Box(name, UiTheme.Margin, top, 0f, height);
            _shell.Root.Add(strip);
            return strip;
        }

        /// <summary>
        /// The strips made as wide as a window <paramref name="window"/> wide leaves them: the controls and the timeline its
        /// whole width in from the edges, the header short of BACK, which stands beside it.
        /// </summary>
        private void FitToolbar(float window)
        {
            float width = Mathf.Max(0f, window - 2f * UiTheme.Margin);
            if (_header != null)
                _header.style.width = Mathf.Max(0f, width - BackWidth - UiTheme.Margin);
            if (_controls != null)
                _controls.style.width = width;
            if (_timeline != null)
                _timeline.style.width = width;
        }

        private void BuildHeader(SchematicPalette palette)
        {
            _header = Strip("header", TitleTop, HeaderHeight);

            Label title = ShellTheme.Label("title", "WORKBENCH", UiType.Heading, palette.Text, TextAnchor.MiddleLeft, bold: true);
            ShellTheme.Place(title, 0f, 0f, 240f, HeaderHeight);
            _header.Add(title);
            float titleWidth = UiTheme.TextWidth("WORKBENCH", UiType.Heading) * 1.08f;

            VisualElement chip = ShellTheme.Chip("preview chip", palette);
            ShellTheme.Place(chip, titleWidth + 14f, (HeaderHeight - 24f) * 0.5f, 86f, 24f);
            Label chipText = ShellTheme.Label("text", "PREVIEW", UiType.Micro, palette.Muted, TextAnchor.MiddleCenter);
            chipText.style.flexGrow = 1f;
            chip.Add(chipText);
            _header.Add(chip);

            // SAMPLE, at the header's right, with its key beside it.
            float sampleWidth = 0f;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                sampleWidth = Mathf.Max(sampleWidth, UiTheme.TextWidth(SampleCaption(i), UiType.Body));
            float width = sampleWidth + 40f;
            _sampleButton = ShellTheme.Button(SampleButton, ChipCaption(_sample), palette, () => ShowSample(_sample + 1));
            ShellTheme.Ellipsis(_sampleButton);   // a circuit's name can be longer than any sample's (Part 15a)
            ShellTheme.PlaceRight(_sampleButton, 0f, (HeaderHeight - SampleHeight) * 0.5f, width, SampleHeight);
            ShellTheme.KeyBeside(_sampleButton, SampleHeight, "S", palette);
            _header.Add(_sampleButton);

            // FILE, left of SAMPLE's key (Part 13).
            BuildFileChip(_header, palette, width + KeyRoom + 12f);

            // The line describing the sample, beside the title, as far as SAMPLE's key; cut short where it runs longer.
            float left = titleWidth + 14f + 86f + 20f;
            _description = ShellTheme.Label("description", WorkbenchSamples.Chip[_sample].Says, UiType.Label, palette.TextDim);
            ShellTheme.Place(_description, left, 0f, -1f, HeaderHeight);
            _description.style.right = width + KeyRoom + 12f + FileWidth + 16f;
            ShellTheme.Ellipsis(_description);
            _header.Add(_description);
        }

        /// <summary>How tall the header's row is: the title, its line and SAMPLE.</summary>
        private const float HeaderHeight = 40f;

        /// <summary>
        /// The first row of the controls: RESTART, PLAY, STEP BACK and STEP, each with its key over it, and the chip saying
        /// what the run is doing, or why playing stopped.
        /// </summary>
        private void BuildControls(SchematicPalette palette)
        {
            _controls = Strip("controls", ControlsTop, UiTheme.ButtonHeight);

            ShellButton Control(string name, string caption, string key, float x, float width, System.Action chosen)
            {
                ShellButton button = ShellTheme.Button(name, caption, palette, chosen);
                ShellTheme.Place(button, x, 0f, width, UiTheme.ButtonHeight);
                ShellTheme.KeyOver(button, width, key, palette);
                _controls.Add(button);
                return button;
            }

            Control(RestartButton, "RESTART", "R", 0f, 120f, Restart);
            _playButton = Control(PlayButton, "PAUSE", "SPACE", 132f, 110f, TogglePlay);
            Control(StepBackButton, "STEP BACK", ",", 254f, 120f, () => StepEdge(forward: false));
            Control(StepButton, "STEP", ".", 386f, 90f, () => StepEdge(forward: true));

            // The chip takes the row's room right of STEP, as far as a sentence needs, one line high. A notice longer than
            // that -- a refusal naming every cell of the library -- takes the lines it needs, down over the timeline's row
            // until the next thing is done, where it was cut short with an ellipsis after NAND2 (the Part 14g smoke test).
            // It takes no clicks, so the slider under it still does.
            VisualElement chip = ShellTheme.Chip("chip", palette);
            ShellTheme.Place(chip, ChipLeft, 9f, -1f, ChipLineHeight);
            chip.style.height = StyleKeyword.Auto;
            chip.style.minHeight = ChipLineHeight;
            chip.style.right = 0f;
            chip.style.maxWidth = ChipMostWidth;
            _chipLabel = ShellTheme.Label("text", "", UiType.Caption, palette.TextDim, TextAnchor.MiddleCenter);
            _chipLabel.style.flexGrow = 1f;
            _chipLabel.style.whiteSpace = WhiteSpace.Normal;
            _chipLabel.style.paddingTop = _chipLabel.style.paddingBottom = 2f;   // a line and these within the chip's one line
            _chipLabel.style.paddingLeft = _chipLabel.style.paddingRight = 6f;
            chip.Add(_chipLabel);
            _controls.Add(chip);
        }

        /// <summary>
        /// The second row of the controls: the slider through the run, the speed between SLOWER and FASTER with their keys
        /// beside them, the RUN TO field, and the moment it is at.
        /// </summary>
        private void BuildTimeline(SchematicPalette palette)
        {
            _timeline = Strip("timeline", TimelineTop, UiTheme.ButtonHeight);

            // Right to left: the moment, RUN TO, the speed; the slider takes what is left.
            _timeLabel = ShellTheme.Label("time", "", UiType.Body, palette.Text, TextAnchor.MiddleRight);
            ShellTheme.PlaceRight(_timeLabel, 0f, 0f, TimeWidth, UiTheme.ButtonHeight);
            _timeline.Add(_timeLabel);
            float right = TimeWidth + RowGap;

            // A time with a unit or without -- 40, 40 ns, 1.5 us -- read in the run's unit (TimeText).
            _runToField = ShellTheme.Field(RunToField, _model.Unit, SchematicPalette.Current, SetRunTo);
            ShellTheme.PlaceRight(_runToField, right, 4f, RunToFieldWidth, UiTheme.ButtonHeight - 8f);
            _timeline.Add(_runToField);

            Label runToCaption = ShellTheme.Label("run to caption", "RUN TO", UiType.Caption, palette.TextDim, TextAnchor.MiddleRight);
            ShellTheme.PlaceRight(runToCaption, right + RunToFieldWidth + 6f, 0f, RunToCaptionWidth, UiTheme.ButtonHeight);
            _timeline.Add(runToCaption);
            right += RunToFieldWidth + 6f + RunToCaptionWidth + RowGap * 2f;

            float small = UiTheme.ButtonHeight - 8f;
            ShellButton faster = ShellTheme.Button(FasterButton, "+", palette, () => ChangeSpeed(1));
            ShellTheme.PlaceRight(faster, right + KeyRoom, 4f, SpeedButtonWidth, small);
            ShellTheme.KeyAfter(faster, SpeedButtonWidth, small, "]", palette);
            _timeline.Add(faster);
            right += KeyRoom + SpeedButtonWidth;

            _speedLabel = ShellTheme.Label("speed", SpeedCaption(_speed, _model.Unit), UiType.Caption, palette.Text, TextAnchor.MiddleCenter);
            ShellTheme.PlaceRight(_speedLabel, right, 0f, SpeedLabelWidth, UiTheme.ButtonHeight);
            _timeline.Add(_speedLabel);
            right += SpeedLabelWidth;

            ShellButton slower = ShellTheme.Button(SlowerButton, "-", palette, () => ChangeSpeed(-1));
            ShellTheme.PlaceRight(slower, right, 4f, SpeedButtonWidth, small);
            ShellTheme.KeyBeside(slower, small, "[", palette);
            _timeline.Add(slower);
            right += SpeedButtonWidth + KeyRoom + RowGap;

            _slider = ShellTheme.Slider(TimeSlider, palette);
            _slider.highValue = (float)_model.End;
            ShellTheme.Place(_slider, 0f, 0f, -1f, UiTheme.ButtonHeight);
            _slider.style.right = right;
            _slider.Changed += value => { Pause(); Seek(value); };
            _timeline.Add(_slider);
        }

        // -----------------------------------------------------------------
        // What the rest of the panel says through the toolbar
        // -----------------------------------------------------------------

        private static void SetText(TextElement label, string text)
        {
            if (label != null && !ReferenceEquals(label.text, text))
                label.text = text;
        }

        /// <summary>RUN TO shows the moment playing stops at, from its start.</summary>
        private void ShowRunTo(string text) => _runToField?.SetValueWithoutNotify(text);

        /// <summary>The slider made for a run <paramref name="end"/> long.</summary>
        private void SliderEnd(double end)
        {
            if (_slider != null)
                _slider.highValue = (float)end;
        }

        /// <summary>The words carrying the unit that are not made again with the stage: the speed, and RUN TO's.</summary>
        private void Relabel()
        {
            SetText(_speedLabel, SpeedCaption(_speed, _model.Unit));
            ShellTheme.Placeholder(_runToField, _model.Unit);
        }
    }
}
