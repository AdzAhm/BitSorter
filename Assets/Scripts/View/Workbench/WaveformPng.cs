using System.Collections.Generic;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The waveform as a picture (Part 9b-4): drawn again offscreen, in Schematic Light whatever look is
    /// on screen, <see cref="Width"/> pixels wide and as tall as its rows, and encoded as a PNG.
    /// </summary>
    /// <remarks>
    /// **Not a screenshot** (Ahmad's decision 4, 2026-10-16): a PNG goes into a report, which is printed,
    /// so it is always the paper look, and the same width whoever makes it. The rows, their names and
    /// radixes, the moment, the cursors, the breakpoints and the window in time are the workbench's --
    /// every row of them, whatever the page is scrolled to -- and they are drawn again on a canvas of
    /// their own: a camera on a layer nothing else draws (<see cref="Layer"/>), into a texture, for the
    /// one call this takes. The toolbar's buttons are left out; a picture has nothing to press.
    ///
    /// **The drawing reads <see cref="SchematicPalette.Current"/>**, so while it is drawn the current
    /// palette is Schematic Light (<see cref="SchematicPalette.Overriding"/>), and everything made for it
    /// is destroyed before the call returns, so nothing draws in that palette afterwards. The texture is
    /// sRGB, as the screen is, so a colour in the file is the palette's own: a test reads the ground's.
    /// </remarks>
    public static class WaveformPng
    {
        /// <summary>The picture's width in pixels, whoever makes it.</summary>
        public const int Width = 1800;

        /// <summary>
        /// Pixels to a unit of the drawing: it is laid out half as wide as the picture and drawn twice the
        /// size, so a wave's line is about three pixels with a solid middle, and the words are sharp when
        /// printed. At one to one a line straddled two rows of pixels and filled neither.
        /// </summary>
        public const float Scale = 2f;

        /// <summary>The room round the waveform, in units of the drawing.</summary>
        public const float Margin = 16f;

        /// <summary>The layer the picture is drawn on: one no camera of the game's looks at.</summary>
        public const int Layer = 31;

        /// <summary>The tallest a picture is drawn: a texture's own limit, past about three hundred rows.</summary>
        private const int MostHeight = 8192;

        /// <summary>How tall the picture of <paramref name="rows"/> rows is.</summary>
        public static int HeightFor(int rows) =>
            Mathf.Min(MostHeight, Mathf.CeilToInt((WorkbenchDrawing.WaveHeight(rows) + 2f * Margin) * Scale));

        /// <summary>
        /// <paramref name="model"/>'s rows, named <paramref name="names"/>, at the moment it shows, over
        /// the time <paramref name="window"/> shows, with <paramref name="cursors"/> and
        /// <paramref name="breakpoints"/>, as a PNG in Schematic Light.
        /// </summary>
        public static byte[] Render(WorkbenchModel model, IReadOnlyList<string> names, IReadOnlyList<bool> headers, WaveNames namesStyle,
            WaveView window, string playheadText, WaveCursors cursors, Breakpoints breakpoints)
        {
            SchematicPalette palette = SchematicPalette.Light;
            int height = HeightFor(model.RowCount);
            var texture = new RenderTexture(new RenderTextureDescriptor(Width, height, RenderTextureFormat.ARGB32, 24) { sRGB = true });
            GameObject cameraObject = null, canvasObject = null;
            Texture2D image = null;
            RenderTexture before = RenderTexture.active;
            SchematicPalette overriding = SchematicPalette.Overriding;

            try
            {
                SchematicPalette.Overriding = palette;

                cameraObject = new GameObject("waveform png camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = palette.Ground;
                camera.cullingMask = 1 << Layer;
                camera.targetTexture = texture;

                canvasObject = new GameObject("waveform png canvas");
                Canvas canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = Scale;

                RectTransform page = UiTheme.Rect("page", canvas.transform);
                UiTheme.Stretch(page);

                var layers = new WorkbenchLayers(model, cursors, breakpoints);
                var wave = new WaveformView(model, layers, page, new Vector2(Margin, Margin), Width / Scale - 2f * Margin, palette, names,
                    headers: headers, namesStyle: namesStyle, toolbar: false);
                if (window != null)
                    wave.View.Match(window);

                foreach (Transform each in canvasObject.GetComponentsInChildren<Transform>(true))
                    each.gameObject.layer = Layer;

                layers.Refresh(all: true);
                wave.Place(playheadText, cursors);
                Canvas.ForceUpdateCanvases();

                var request = new RenderPipeline.StandardRequest { destination = texture };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                    RenderPipeline.SubmitRenderRequest(camera, request);
                else
                    camera.Render();

                RenderTexture.active = texture;
                image = new Texture2D(Width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, height), 0, 0);
                image.Apply();
                return image.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = before;
                SchematicPalette.Overriding = overriding;
                if (canvasObject != null)
                    Object.DestroyImmediate(canvasObject);
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
                if (image != null)
                    Object.DestroyImmediate(image);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }
    }
}
