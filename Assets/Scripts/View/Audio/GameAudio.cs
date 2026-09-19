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

        [Tooltip("Seconds to fade down and back up when the track changes at a level boundary.")]
        [SerializeField] private float _switchSeconds = 0.7f;

        private const string MutedKey = "bitsorter.music.muted";

        private AudioSource _source;
        private AudioSource _musicSource;

        /// <summary>The shuffle the level tracks are drawn from. Seeded once, in Awake.</summary>
        private MusicBag _bag;

        /// <summary>Track currently loaded into the source.</summary>
        private int _track;

        /// <summary>Track that should be playing. Differs from <see cref="_track"/> mid-switch.</summary>
        private int _wanted;

        /// <summary>Level the current track was chosen for. Null until the first one loads.</summary>
        private string _playingUnder;

        /// <summary>Fade multiplier, 0 to 1. Rides down and back up across a track change.</summary>
        private float _gain = 1f;

        /// <summary>Whether the game is silenced -- the music and every cue.</summary>
        /// <remarks>
        /// Kept in <see cref="Preferences"/> -- PlayerPrefs behind a redirectable seam -- rather
        /// than in the progress file. It describes this machine's
        /// speakers, not the player's circuits, and someone who copies a save to another computer
        /// should not carry a mute across with it.
        ///
        /// One switch for the whole game rather than one for music and one for effects. Every cue
        /// has something on screen that says the same thing -- the scorch mark and the bits-lost
        /// meter for a collision, the win panel for a pass, the bits themselves for a gate firing
        /// or a landing -- so silence costs the player no information, and a second setting would
        /// be two switches and four states for a game with five cues and one loop.
        ///
        /// This used to silence only the music while the clock carried on ticking twice a second,
        /// which is the one sound somebody reaching for mute most wants gone.
        ///
        /// The PlayerPrefs key still says music. Renaming it would reset the preference of anyone
        /// who had already turned the sound off, which is a worse trade than a stale key name.
        /// </remarks>
        public static bool Muted
        {
            get => Preferences.GetInt(MutedKey, 0) != 0;
            set => Preferences.SetInt(MutedKey, value ? 1 : 0);
        }

        /// <summary>Silences or restores the game, and remembers which.</summary>
        public void ToggleMute() => SetMuted(!Muted);

        /// <remarks>
        /// Only the stored answer is written here. The source is brought into line with it in
        /// <see cref="DriveMusic"/> every frame, the way every other readout in this project
        /// polls rather than being pushed to -- so the setting is the single source of truth and
        /// cannot be changed by a route that forgets to update the source.
        ///
        /// The loop is muted rather than stopped, so it keeps its playback position and unmuting
        /// does not restart the phrase from the top. The cues need no equivalent: they are fired
        /// one at a time and <see cref="Play"/> simply declines to fire them.
        /// </remarks>
        public void SetMuted(bool muted) => Muted = muted;

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

            // The seed is the only random thing about the music: a different shuffle per session,
            // so two evenings on the same levels are not the same evening, and everything after it
            // deterministic.
            _bag = new MusicBag(ProceduralAudio.MusicTracks, UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            _track = _wanted = _bag.Current;

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D; the board is not a place
        }

        /// <summary>
        /// A level load is the only thing that moves the music on.
        /// </summary>
        /// <remarks>
        /// Subscribed in OnEnable, which runs before every Start -- including
        /// <see cref="LevelSession"/>'s, where the first level loads. So the first event can
        /// arrive before <see cref="Start"/> below has built the music source at all, and the
        /// handler has to be safe with none. <see cref="MusicRules.ChangesTrack"/> is what makes
        /// it so: with nothing playing under a level yet, the first load changes nothing.
        /// </remarks>
        private void OnEnable()
        {
            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;
        }

        /// <summary>
        /// Frees the track this owns. A clip built at runtime belongs to no scene, so unloading the
        /// scene would otherwise leave three megabytes behind every time.
        /// </summary>
        private void OnDestroy()
        {
            if (_musicSource != null && _musicSource.clip != null)
                Destroy(_musicSource.clip);
        }

        private void OnLevelLoaded(LevelDefinition level)
        {
            string key = _session != null ? _session.LevelName : null;

            if (MusicRules.ChangesTrack(_playingUnder, key))
                _wanted = _bag.Advance();

            _playingUnder = key;
        }

        /// <summary>
        /// Builds the music source. Always -- whether the player wants to hear it is
        /// <see cref="Muted"/>'s business, not this method's.
        /// </summary>
        /// <remarks>
        /// There used to be a serialized bool here as well, and it was the wrong shape: unticking
        /// it meant no source was ever built, so <see cref="SetMuted"/> had nothing to act on and
        /// the menu toggle and the N key both silently did nothing. Two switches for one question,
        /// and the one the player could not reach won.
        /// </remarks>
        private void Start()
        {
            // Its own source, not PlayOneShot. The loop needs to hold a playback position and be
            // stoppable, and mixing it into the cue source would have every collision duck it.
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.clip = ProceduralAudio.MusicClip(_track);
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * _masterVolume;

            _musicSource.mute = Muted;
            _musicSource.Play();
        }

        private void Update()
        {
            // Before the readiness check: muting should work on the menu, where there is no graph.
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.nKey.wasPressedThisFrame)
                ToggleMute();

            // Before the readiness check too: a track change is triggered by a level load, and a
            // level load is exactly the moment the runner is briefly not ready.
            DriveMusic();

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

        /// <summary>
        /// Fades the track down, swaps it at the bottom, and fades back up.
        /// </summary>
        /// <remarks>
        /// One source rather than two crossfading. The music is quiet and sparse enough that a
        /// brief dip reads as a breath rather than as a gap, and a second source would need its
        /// own copy of the mute handling -- which is the setting most likely to end up applying
        /// to one source and not the other.
        ///
        /// Unscaled time, so a fade cannot stall if the game is ever paused by timescale.
        ///
        /// The clip is built on the frame it is first needed, which costs a few milliseconds
        /// during a level transition -- less than the board rebuild happening beside it, and
        /// less than the game already spent building the one track at boot.
        /// </remarks>
        private void DriveMusic()
        {
            if (_musicSource == null)
                return;

            // Ahead of the fade, and ahead of its early-out: a settled track still has to notice
            // the player reaching for mute.
            _musicSource.mute = Muted;

            float rate = Time.unscaledDeltaTime / Mathf.Max(0.05f, _switchSeconds);

            if (_wanted != _track)
            {
                _gain -= rate;

                if (_gain <= 0f)
                {
                    _gain = 0f;
                    _track = _wanted;

                    // The clip this source owned is freed on the way out. Nothing caches tracks any
                    // more, so a track left behind here would be three megabytes held until the game
                    // closed.
                    AudioClip leaving = _musicSource.clip;

                    _musicSource.clip = ProceduralAudio.MusicClip(_track);
                    _musicSource.Play();

                    if (leaving != null)
                        Destroy(leaving);
                }
            }
            else if (_gain < 1f)
            {
                _gain = Mathf.Min(1f, _gain + rate);
            }
            else
            {
                return;   // settled; no reason to touch the volume every frame
            }

            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * _masterVolume * _gain;
        }

        /// <summary>
        /// How many cues have actually been played this session.
        /// </summary>
        /// <remarks>
        /// A counter to watch, exactly like the `GateFiredCount` this class already polls off
        /// <see cref="BitRenderer"/>. A one-shot is fired and forgotten, so there is otherwise
        /// nothing to ask about whether the game made a sound -- and "mute actually silences the
        /// cues, not only the music" is the one thing about this class worth proving.
        /// </remarks>
        public int CuesPlayed { get; private set; }

        private void Play(Cue cue)
        {
            if (Muted || _source == null || _masterVolume <= 0f)
                return;

            _source.PlayOneShot(ProceduralAudio.Clip(cue), ProceduralAudio.VolumeOf(cue) * _masterVolume);
            CuesPlayed++;
        }
    }
}
