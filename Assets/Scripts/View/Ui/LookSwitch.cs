using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// Changes the look the game is drawn in: the choice written down, then the game loaded again
    /// under a cover, and Settings put back up where the choice was made.
    /// </summary>
    /// <remarks>
    /// **A reload, because a palette is never changed in place** (ADR-8): every renderer takes its
    /// colours as it builds, and repainting each one where it stands would be a second way of
    /// building every one of them. Nothing in the scene outlives a load, so a reload builds the whole
    /// game again in the new look -- the music starts again with it.
    ///
    /// The open board is written first. A reload is not a quit, so nothing else would write it, and
    /// an edit made in the half second before Settings was opened is still waiting for its save.
    ///
    /// The cover is the only thing that outlives the load: a canvas of its own above everything,
    /// which fades in over the old game, stays through the load, and fades out over the new one once
    /// Settings is back. It takes clicks while it is up, so nothing is pressed under it. It counts
    /// game time, as <see cref="UiFade"/> does, so a test's driven clock drives it.
    /// </remarks>
    public sealed class LookSwitch : MonoBehaviour
    {
        /// <summary>How long the cover takes to come up over the old game, and to go from over the new one.</summary>
        public const float CoverSeconds = 0.18f;

        /// <inheritdoc cref="CoverSeconds"/>
        public const float RevealSeconds = 0.25f;

        private static LookSwitch _running;

        /// <summary>Whether a switch is under way: from the click until the cover has gone.</summary>
        public static bool Busy => _running != null;

        /// <summary>Starts a switch to <paramref name="look"/>, unless it is the look already or a switch is under way.</summary>
        public static void To(Look look)
        {
            if (look == null || look == Look.Current || Busy)
                return;

            Preferences.SetInt(Look.PreferenceKey, look.Code);

            var holder = new GameObject("Look switch");
            DontDestroyOnLoad(holder);
            _running = holder.AddComponent<LookSwitch>();
            _running._look = look;
        }

        private Look _look;
        private Image _cover;

        private IEnumerator Start()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            gameObject.AddComponent<GraphicRaycaster>();

            _cover = new GameObject("cover", typeof(RectTransform)).AddComponent<Image>();
            _cover.transform.SetParent(transform, false);
            UiTheme.Stretch(_cover.rectTransform);
            _cover.raycastTarget = true;

            Color ground = Palette.Current.Ground;
            yield return Fade(ground, 0f, 1f, CoverSeconds);

            ProgressTracker tracker = FindFirstObjectByType<ProgressTracker>();
            if (tracker != null)
                tracker.SaveNow();

            Look.Use(_look);
            yield return SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name, LoadSceneMode.Single);

            // Every Start has run once a frame has gone by: the menu has opened itself, as it does on
            // every load, and Settings has been built, hidden.
            yield return null;
            yield return null;

            MainMenu menu = FindFirstObjectByType<MainMenu>();
            SettingsPanel settings = FindFirstObjectByType<SettingsPanel>();
            if (menu != null && settings != null)
            {
                menu.Show(false);
                settings.Open();
            }

            yield return Fade(Palette.Current.Ground, 1f, 0f, RevealSeconds);
            Destroy(gameObject);
        }

        private IEnumerator Fade(Color colour, float from, float to, float seconds)
        {
            for (float t = 0f; ; t += Time.deltaTime)
            {
                // The last frame lands on the end, whatever the step (CLAUDE.md: an animation that
                // ends ends on its final value).
                float k = Mathf.Clamp01(t / seconds);
                _cover.color = new Color(colour.r, colour.g, colour.b, Mathf.Lerp(from, to, k));
                if (k >= 1f)
                    yield break;
                yield return null;
            }
        }

        private void OnDestroy()
        {
            if (_running == this)
                _running = null;
        }
    }
}
