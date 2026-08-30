using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Player visual accessibility choices shared by renderers and the menu.
    /// </summary>
    public static class VisualAccessibilitySettings
    {
        private const string ColorblindKey = "bitsorter.visuals.colorblind";
        private const string ShapeCuesKey = "bitsorter.visuals.shapeCues";

        private static int _version;

        public static int Version => _version;

        public static bool Colorblind
        {
            get => PlayerPrefs.GetInt(ColorblindKey, 0) != 0;
            private set
            {
                PlayerPrefs.SetInt(ColorblindKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static bool ShapeCues
        {
            get => PlayerPrefs.GetInt(ShapeCuesKey, 0) != 0;
            private set
            {
                PlayerPrefs.SetInt(ShapeCuesKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void SetColorblind(bool enabled)
        {
            if (Colorblind == enabled)
                return;

            Colorblind = enabled;
            _version++;
        }

        public static void SetShapeCues(bool enabled)
        {
            if (ShapeCues == enabled)
                return;

            ShapeCues = enabled;
            _version++;
        }

        public static Color ZeroBitColor() =>
            Colorblind
                ? new Color(0.10f, 0.62f, 0.98f)
                : new Color(0.42f, 0.48f, 0.58f);

        public static Color OneBitColor() =>
            Colorblind
                ? new Color(0.98f, 0.56f, 0.06f)
                : new Color(1.00f, 0.88f, 0.32f);

        public static Color WaitingZeroColor() =>
            Colorblind
                ? new Color(0.24f, 0.78f, 1.00f)
                : new Color(0.60f, 0.72f, 0.92f);

        public static Color WaitingOneColor() =>
            Colorblind
                ? new Color(1.00f, 0.62f, 0.12f)
                : new Color(1.00f, 0.88f, 0.32f);

        public static Color CorruptionColor() =>
            Colorblind
                ? new Color(0.98f, 0.15f, 0.86f)
                : new Color(0.95f, 0.30f, 0.28f);
    }
}
