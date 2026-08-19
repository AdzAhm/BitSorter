#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Writes numbered PNG frames of the game view, for making the README animation.
    /// </summary>
    /// <remarks>
    /// Behind UNITY_EDITOR, so it compiles in the editor and cannot reach a build. It lives in the
    /// runtime assembly rather than the Editor one because it has to be a component on a live
    /// GameObject, and Unity will not add an Editor-assembly MonoBehaviour to an object in play
    /// mode -- AddComponent simply returns null, which is how this was first written and why it
    /// failed.
    ///
    /// Drives <see cref="Time.captureFramerate"/>, which makes Unity advance time in fixed steps
    /// instead of by the wall clock. That is the whole trick: encoding a frame takes far longer
    /// than a frame is meant to last, so without it the recording would be a stutter of whatever
    /// the machine kept up with. With it every frame is exactly one step apart, however slowly they
    /// are actually produced.
    ///
    /// Captures at the end of frame, the only point where the game view holds a complete image
    /// including the interface.
    /// </remarks>
    public sealed class FrameRecorder : MonoBehaviour
    {
        public int Frames = 90;
        public int Fps = 20;
        public int Width = 800;
        public string Folder = "Recording";

        /// <summary>Set when the last frame has been written, for the caller to poll.</summary>
        public static bool Finished;

        /// <summary>How many frames have been written so far.</summary>
        public static int Written;

        private IEnumerator Start()
        {
            Finished = false;
            Written = 0;

            Directory.CreateDirectory(Folder);
            Time.captureFramerate = Fps;

            for (int i = 0; i < Frames; i++)
            {
                yield return new WaitForEndOfFrame();

                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                Texture2D small = Downscale(shot, Width);

                File.WriteAllBytes(Path.Combine(Folder, $"f{i:0000}.png"), small.EncodeToPNG());

                DestroyImmediate(shot);
                DestroyImmediate(small);

                Written = i + 1;
            }

            Time.captureFramerate = 0;
            Finished = true;
        }

        /// <summary>
        /// Scales a capture down to <paramref name="width"/>, keeping its aspect.
        /// </summary>
        /// <remarks>
        /// Through a RenderTexture so the GPU does the filtering and the result is smooth rather
        /// than aliased. Size matters here beyond looks: an animation is a binary file that stays
        /// in the repository forever.
        /// </remarks>
        private static Texture2D Downscale(Texture2D source, int width)
        {
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)source.height / source.width));

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            source.filterMode = FilterMode.Bilinear;

            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);

            // Undo one sRGB encode.
            //
            // The project renders in Linear colour space, and the captured frame comes back already
            // sRGB-encoded. Writing that straight to a PNG encodes it a second time, which lifts
            // the midtones badly -- the near-black board photographed as washed-out grey-blue.
            // Converting back to linear once cancels the extra pass, so the file matches what the
            // game looks like on screen.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                Color[] pixels = result.GetPixels();

                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = pixels[i].linear;

                result.SetPixels(pixels);
            }

            result.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }
    }
}
#endif
