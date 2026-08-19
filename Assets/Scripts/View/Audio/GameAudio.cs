using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Plays the game's cues by watching the things that already happen, rather than by being told.
    /// </summary>
    /// <remarks>
    /// Every source here is a counter or a state that some other component already maintains, polled
    /// against a cached copy -- the idiom the renderers all use. Nothing needed an event, and nothing
    /// in LogicCore or SimulationRunner had to change to make a sound.
    ///
    /// Needs an AudioListener in the scene or every cue plays to nobody, silently and with no
    /// warning. The scene builder puts one on the camera.
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudio : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;
        [SerializeField] private BitRenderer _bits;

        [Tooltip("Scales every cue. Zero is silence.")]
        [Range(0f, 1f)]
        [SerializeField] private float _masterVolume = 0.8f;

        [Tooltip("Most gate cues in one frame. A wide circuit can fire many at once.")]
        [SerializeField] private int _gateBurstLimit = 3;

        [Tooltip("Background loop. On by default; the player's choice is remembered.")]
        [SerializeField] private bool _music = true;

        private const string MutedKey = "bitsorter.music.muted";

        private AudioSource _source;
        private AudioSource _musicSource;

        /// <summary>Whether the background loop is currently silenced.</summary>
        /// <remarks>
        /// Kept in PlayerPrefs rather than in the progress file. It describes this machine's
        /// speakers, not the player's circuits, and someone who copies a save to another computer
        /// should not carry a mute across with it.
        /// </remarks>
        public static bool MusicMuted
        {
            get => PlayerPrefs.GetInt(MutedKey, 0) != 0;
            private set
            {
                PlayerPrefs.SetInt(MutedKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Silences or restores the background loop, and remembers which.</summary>
        public void ToggleMusic() => SetMuted(!MusicMuted);

        public void SetMuted(bool muted)
        {
            MusicMuted = muted;

            if (_musicSource != null)
                _musicSource.mute = muted;
        }

        private int _tick = -1;
        private int _gatesFired;
        private int _binsLanded;
        private int _corrupted;
        private RunState _state = RunState.Editing;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_bits == null) _bits = FindFirstObjectByType<BitRenderer>();

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D; the board is not a place
        }

        private void Start()
        {
            if (!_music)
                return;

            // Its own source, not PlayOneShot. The loop needs to hold a playback position and be
            // stoppable, and mixing it into the cue source would have every collision duck it.
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.clip = ProceduralAudio.Clip(Cue.Music);
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * _masterVolume;

            // Muted rather than not started, so the loop keeps its playback position and unmuting
            // does not restart the phrase from the top every time.
            _musicSource.mute = MusicMuted;
            _musicSource.Play();
        }

        private void Update()
        {
            // Before the readiness check: muting should work on the menu, where there is no graph.
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.nKey.wasPressedThisFrame)
                ToggleMusic();

            if (_runner == null || !_runner.IsReady)
                return;

            SimulationView view = _runner.View;

            ReadClock(view);
            ReadCollisions(view);
            ReadBits();
            ReadVerdict();
        }

        /// <summary>
        /// One tick, one click.
        /// </summary>
        /// <remarks>
        /// Reads CurrentTick rather than hooking the tick loop, so the runner keeps knowing nothing
        /// about audio. A rebuild resets the tick to zero, which shows up here as the count going
        /// backwards and is simply re-baselined rather than played.
        /// </remarks>
        private void ReadClock(SimulationView view)
        {
            int now = view.CurrentTick;

            if (now == _tick)
                return;

            bool advanced = now > _tick && _tick >= 0;
            _tick = now;

            if (advanced)
                Play(Cue.Tick);
        }

        private void ReadCollisions(SimulationView view)
        {
            int now = view.CorruptedCount;

            if (now > _corrupted)
                Play(Cue.Collide);   // same frame as the meter's punch and the spark burst

            _corrupted = now;
        }

        private void ReadBits()
        {
            if (_bits == null)
                return;

            // Capped. A wide circuit can fire six gates on one tick, and six copies of the same clip
            // in one frame is a click, not six sounds.
            int gates = Mathf.Min(_bits.GateFiredCount - _gatesFired, _gateBurstLimit);
            for (int i = 0; i < gates; i++)
                Play(Cue.Gate);

            _gatesFired = _bits.GateFiredCount;

            int landed = Mathf.Min(_bits.BinLandedCount - _binsLanded, _gateBurstLimit);
            for (int i = 0; i < landed; i++)
                Play(Cue.Land);

            _binsLanded = _bits.BinLandedCount;
        }

        private void ReadVerdict()
        {
            if (_session == null)
                return;

            RunState now = _session.State;

            if (now != _state && now == RunState.Passed)
                Play(Cue.Win);

            _state = now;
        }

        private void Play(Cue cue)
        {
            if (_source == null || _masterVolume <= 0f)
                return;

            _source.PlayOneShot(ProceduralAudio.Clip(cue), ProceduralAudio.VolumeOf(cue) * _masterVolume);
        }
    }
}
