using System;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The handful of Unity's <c>Mathf</c> functions the view-model uses, with Unity's own definitions.
    /// </summary>
    /// <remarks>
    /// Not <c>Math.Clamp</c> and friends: <c>Math.Clamp</c> throws when its bounds cross, which
    /// Unity's does not, and <c>Math.Min</c> and <c>Math.Max</c> treat NaN and a signed zero their own
    /// way. These keep every answer the one the workbench gave before it moved here.
    /// </remarks>
    public static class Arith
    {
        public static float Min(float a, float b) => a < b ? a : b;

        public static float Max(float a, float b) => a > b ? a : b;

        public static float Abs(float f) => Math.Abs(f);

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
                value = min;
            else if (value > max)
                value = max;
            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        public static float Pow(float f, float p) => (float)Math.Pow(f, p);

        public static float Sqrt(float f) => (float)Math.Sqrt(f);
    }
}
