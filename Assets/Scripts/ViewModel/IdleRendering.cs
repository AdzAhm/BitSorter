namespace BitSorter.ViewModel
{
    /// <summary>
    /// What happened this frame that the window has to show at once (<see cref="IdleRendering"/>): the student doing something,
    /// or the workbench moving by itself.
    /// </summary>
    public readonly struct IdleFacts
    {
        public IdleFacts(bool input = false, bool resized = false, bool playing = false, bool making = false, bool working = false,
            bool moving = false, bool redrawn = false)
        {
            Input = input;
            Resized = resized;
            Playing = playing;
            Making = making;
            Working = working;
            Moving = moving;
            Redrawn = redrawn;
        }

        /// <summary>The pointer moved, a button or a key is down, the wheel turned, or text was typed.</summary>
        public bool Input { get; }

        /// <summary>The window changed size.</summary>
        public bool Resized { get; }

        /// <summary>The run is playing: its moment moves, and changes travel their wires.</summary>
        public bool Playing { get; }

        /// <summary>The run is still being made, a slice a frame, and the waves grow as it is.</summary>
        public bool Making { get; }

        /// <summary>LOGIC, STATES or TIMING is still working its answer out a slice a frame.</summary>
        public bool Working { get; }

        /// <summary>Something moves by itself: a panel fading in, the credits rising.</summary>
        public bool Moving { get; }

        /// <summary>The drawing was drawn again.</summary>
        public bool Redrawn { get; }

        /// <summary>Whether anything did.</summary>
        public bool Any => Input || Resized || Playing || Making || Working || Moving || Redrawn;
    }

    /// <summary>
    /// How often the window is drawn (5.1, Ahmad: "we need a good optimization for that", at about 56% of the GPU with
    /// nothing moving): every frame while anything happens and for <see cref="Linger"/> after, else one frame in
    /// <see cref="IdleInterval"/>. Input is read every frame either way, so the first frame of anything done is drawn at once.
    /// </summary>
    /// <remarks>
    /// While idle a desktop build also runs fewer frames, twenty a second (the view's <c>FrameRate.IdleRate</c>): skipping the
    /// drawing alone still handed every frame to the screen, and a laptop's display chip copied each one. One in four of
    /// those is five drawn a second, which still shows a caret blinking; it was one in ten of the screen's rate.
    /// </remarks>
    public sealed class IdleRendering
    {
        /// <summary>One frame in this many drawn while nothing happens.</summary>
        public const int IdleInterval = 4;

        /// <summary>How long, in seconds, every frame is drawn after the last thing that happened: a toast, a flash, a stage made again once a window stands still.</summary>
        public const double Linger = 1.0;

        private double _lastActive = double.NaN;

        /// <summary>One frame in how many is drawn now.</summary>
        public int Interval { get; private set; } = 1;

        /// <summary>
        /// The frame at <paramref name="now"/> (seconds, real time) with <paramref name="facts"/>: how often to draw from it on.
        /// The first frame counts as something happening, as a launch is.
        /// </summary>
        public int Frame(double now, in IdleFacts facts)
        {
            if (facts.Any || double.IsNaN(_lastActive))
                _lastActive = now;
            Interval = now - _lastActive < Linger ? 1 : IdleInterval;
            return Interval;
        }
    }
}
