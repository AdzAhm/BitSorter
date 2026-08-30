namespace BitSorter.View
{
    /// <summary>
    /// First-run onboarding: when to show it, and what each card says.
    /// </summary>
    public static class OnboardingRules
    {
        public const string SeenKey = "bitsorter.onboarding.v1";
        public const string FirstLevel = "route-the-bit";

        public const int StepCount = 3;

        public static bool ShouldShow(string levelName, bool alreadySeen, bool levelAlreadySolved) =>
            !alreadySeen && !levelAlreadySolved && levelName == FirstLevel;

        public static int ClampStep(int step) =>
            step < 0 ? 0 : step >= StepCount ? StepCount - 1 : step;

        public static bool CanStepBack(int step) => step > 0;

        public static bool CanStepForward(int step) => step < StepCount - 1;

        public static string TitleAt(int step)
        {
            switch (ClampStep(step))
            {
                case 0: return "WELCOME";
                case 1: return "HOW A GATE FIRES";
                default: return "WHEN BITS COLLIDE";
            }
        }

        public static string BodyAt(int step)
        {
            switch (ClampStep(step))
            {
                case 0:
                    return "Place gates from the left palette, then drag from one port to another to wire them. Press ENTER to run, and R to rewind for edits.";
                case 1:
                    return "A gate fires only when ALL input ports are full, then it consumes them. If one input arrives early, it waits in that input port.";
                default:
                    return "If the next bit arrives before the other input does, they collide and bits are destroyed. Red marks name the port that needs path balancing.";
            }
        }
    }
}
