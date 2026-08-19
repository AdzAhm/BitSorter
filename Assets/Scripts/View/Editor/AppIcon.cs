using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BitSorter.View.Editor
{
    /// <summary>
    /// Draws the application icon and assigns it, from a menu item.
    /// </summary>
    /// <remarks>
    /// Generated rather than drawn by hand, for the same reasons the sprites and the sound are:
    /// nothing to license and every choice is a number next to the code that uses it.
    ///
    /// The one concession is that the result is committed as a PNG. An icon has to be a real asset
    /// before <see cref="PlayerSettings"/> will take it, so unlike everything else in the project it
    /// cannot be built at runtime. Committing the generator alongside it is what keeps it honest --
    /// the PNG can be reproduced rather than being a binary nobody can edit.
    ///
    /// An AND gate, because it is the most recognisable shape in the game and reads as logic
    /// immediately. Everything is drawn in bold blocks with a single bright accent: an icon spends
    /// most of its life at 32 pixels or less, where anything finer turns to mush.
    /// </remarks>
    public static class AppIcon
    {
        private const string Path = "Assets/Icon/BitSorterIcon.png";
        private const int Size = 512;

        private static readonly Color Background = new Color(0.055f, 0.065f, 0.085f);
        private static readonly Color Edge = new Color(0.16f, 0.19f, 0.25f);
        private static readonly Color Gate = new Color(0.42f, 0.68f, 1.00f);
        private static readonly Color Wire = new Color(0.30f, 0.72f, 0.80f);
        private static readonly Color Bit = new Color(1.00f, 0.88f, 0.32f);

        [MenuItem("BitSorter/Generate App Icon")]
        public static void Generate()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                    pixels[y * Size + x] = Sample(x, y);
            }

            texture.SetPixels(pixels);
            texture.Apply();

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllBytes(Path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);

            // Every slot gets the same image. Unity scales them down, and one drawing that survives
            // scaling is better than several that drift apart.
            int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            var icons = new Texture2D[sizes.Length];

            for (int i = 0; i < icons.Length; i++)
                icons[i] = icon;

            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
            AssetDatabase.SaveAssets();

            Debug.Log($"BitSorter: icon written to {Path} and assigned to {icons.Length} slots.");
        }

        /// <summary>Colour of one pixel, in coordinates running -1..1 with y upwards.</summary>
        private static Color Sample(int px, int py)
        {
            float x = (px + 0.5f) / Size * 2f - 1f;
            float y = (py + 0.5f) / Size * 2f - 1f;

            // Squircle rather than a circle or a rounded rectangle: it fills a square canvas the way
            // platform icons expect while still reading as soft at the corners.
            float squircle = Mathf.Pow(Mathf.Abs(x), 4f) + Mathf.Pow(Mathf.Abs(y), 4f);

            if (squircle > Mathf.Pow(0.97f, 4f))
                return Color.clear;

            Color colour = squircle > Mathf.Pow(0.90f, 4f) ? Edge : Background;

            // Two input stubs on the left, at the gate's port heights.
            bool stub = x > -0.86f && x < -0.42f
                        && (Mathf.Abs(y - 0.30f) < 0.055f || Mathf.Abs(y + 0.30f) < 0.055f);

            // The output lead, from the gate's nose to the bit.
            bool lead = x > 0.36f && x < 0.66f && Mathf.Abs(y) < 0.055f;

            if (stub || lead)
                colour = Wire;

            // An AND gate: a flat back with a domed front.
            bool body = Mathf.Abs(y) <= 0.46f
                        && ((x >= -0.46f && x <= -0.04f)
                            || (x > -0.04f && (x + 0.04f) * (x + 0.04f) + y * y <= 0.46f * 0.46f));

            if (body)
                colour = Gate;

            // The bit leaving it. The one bright thing, so there is a focal point at any size.
            float dx = x - 0.72f;
            if (dx * dx + y * y <= 0.155f * 0.155f)
                colour = Bit;

            return colour;
        }
    }
}
