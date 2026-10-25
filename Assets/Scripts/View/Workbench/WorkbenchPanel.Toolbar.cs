using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's toolbar, in the shell (Part 11a): the title and SAMPLE, the run's controls and the chip that says
    /// what the run is doing, and the timeline -- the slider, the speed and RUN TO. The first of the shell's panels, and
    /// the one its bridges are proved on (<see cref="ShellPanel"/>). Until the docked layout (Part 11b) it stands where
    /// the uGUI rows stood, as wide as the stage is drawn.
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

        /// <summary>What the speed between SLOWER and FASTER says now.</summary>
        public string SpeedText => _speedLabel?.text;

        /// <summary>What the time label beside the slider says now: <c>t = 11.0 ns</c>.</summary>
        public string MomentText => _timeLabel?.text;

        private void BuildToolbar(SchematicPalette palette)
        {
            _shell = ShellPanel.Create(ShellName, transform);
            _shell.Visible = false;

            BuildHeader(palette);
            BuildControls(palette);
            BuildTimeline(palette);

            ShellButton back = ShellTheme.Button(BackButton, "BACK", palette, Back);
            ShellTheme.PlaceRight(back, UiTheme.Margin, UiTheme.Margin, BackWidth, UiTheme.ButtonHeight);
            _shell.Root.Add(back);
        }

        /// <summary>A strip across the top of the screen, centred, as wide as the stage is drawn (<see cref="FitToolbar"/>).</summary>
        private VisualElement Strip(string name, float top, float height)
        {
            VisualElement strip = ShellTheme.Box(name, 0f, top, StageWidth, height);
            strip.style.left = new Length(50f, LengthUnit.Percent);
            strip.style.marginLeft = -StageWidth * 0.5f;
            _shell.Root.Add(strip);
            return strip;
        }

        /// <summary>The strips made as wide as the stage is drawn at <paramref name="width"/>, as the uGUI rows were.</summary>
        private void FitToolbar(float width)
        {
            foreach (VisualElement strip in new[] { _header, _controls, _timeline })
            {
                if (strip == null)
                    continue;
                strip.style.width = width;
                strip.style.marginLeft = -width * 0.5f;
            }
        }

        private void BuildHeader(SchematicPalette palette)
        {
            _header = Strip("header", TitleTop, 80f);

            Label title = ShellTheme.Label("title", "WORKBENCH", UiType.Heading, palette.Text, TextAnchor.MiddleLeft, bold: true);
            ShellTheme.Place(title, 0f, 0f, 240f, 36f);
            _header.Add(title);
            float titleWidth = UiTheme.TextWidth("WORKBENCH", UiType.Heading) * 1.08f;

            VisualElement chip = ShellTheme.Chip("preview chip", palette);
            ShellTheme.Place(chip, titleWidth + 14f, 8f, 86f, 24f);
            Label chipText = ShellTheme.Label("text", "PREVIEW", UiType.Micro, palette.Muted, TextAnchor.MiddleCenter);
            chipText.style.flexGrow = 1f;
            chip.Add(chipText);
            _header.Add(chip);

            // As wide as the header, which is as wide as the stage is drawn.
            _description = ShellTheme.Label("description", WorkbenchSamples.Chip[_sample].Says, UiType.Label, palette.TextDim);
            ShellTheme.Place(_description, 0f, DescriptionTop - TitleTop, -1f, 24f);
            _header.Add(_description);

            // SAMPLE, at the header's right, over the line that describes the sample, with its key beside it.
            float sampleWidth = 0f;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                sampleWidth = Mathf.Max(sampleWidth, UiTheme.TextWidth(SampleCaption(i), UiType.Body));
            float width = sampleWidth + 40f;
            _sampleButton = ShellTheme.Button(SampleButton, SampleCaption(_sample), palette, () => ShowSample(_sample + 1));
            ShellTheme.PlaceRight(_sampleButton, 0f, 0f, width, SampleHeight);
            ShellTheme.KeyBeside(_sampleButton, SampleHeight, "S", palette);
            _header.Add(_sampleButton);
        }

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

            // The chip takes the row's room right of STEP: a refusal can run long -- "clk is a clock" -- and is cut
            // short with an ellipsis rather than spilling.
            VisualElement chip = ShellTheme.Chip("chip", palette);
            ShellTheme.Place(chip, ChipLeft, 9f, -1f, 26f);
            chip.style.right = 0f;
            _chipLabel = ShellTheme.Label("text", "", UiType.Caption, palette.TextDim, TextAnchor.MiddleCenter);
            _chipLabel.style.flexGrow = 1f;
            ShellTheme.Ellipsis(_chipLabel);
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
