using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// The music: one of the generated tracks while the simulator is up, and the recordings while Settings or the credits are
    /// -- off until the player switches it on (Part 15a). Polled, as everything that watches the screen here is.
    /// </summary>
    /// <remarks>
    /// The 4.x game's cues -- a tick, a gate firing, a bit landing, a collision, a solve -- went with its board in Part 16a,
    /// and with them the EFFECTS switch, which had nothing left to switch. The track no longer changes as a level loads, since
    /// none does: a session plays the one its shuffle deals first.
    ///
    /// Needs an AudioListener in the scene or the music plays to nobody, silently and with no warning. The scene builder puts
    /// one on the camera.
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudio : MonoBehaviour
    {
        [Tooltip("The settings, which carry the menu's music. Found by type when empty.")]
        [SerializeField] private SettingsPanel _settings;

        [Tooltip("The credits, reached from the settings, which carry it too. Found by type when empty.")]
        [SerializeField] private CreditsPanel _credits;

        [Tooltip("Played while Settings or the credits are showing, one after another. Imported, not generated: " +
                 "see Credits.MenuMusic, which the credits roll shows.")]
        [SerializeField] private MenuTrack[] _menuTracks = System.Array.Empty<MenuTrack>();

        /// <summary>One of the main menu's recordings, and how loud it is.</summary>
        /// <remarks>
        /// The loudness is measured, not chosen: the file's RMS in dBFS once imported -- mixed to
        /// mono and normalised to its peak. The scene builder carries the figure beside each file's
        /// path, and AudioPlayTests decodes the files again to check what the menu then sounds like.
        /// The menu plays each at <see cref="ProceduralAudio.MusicLoudnessDb"/>, the level music's
        /// loudness, whatever it was mastered at.
        /// </remarks>
        [System.Serializable]
        public struct MenuTrack
        {
            public AudioClip Clip;
            public float LoudnessDb;
        }

        [Tooltip("Scales the music. Zero is silence.")]
        [Range(0f, 1f)]
        [SerializeField] private float _masterVolume = 0.8f;

        [Tooltip("Seconds to fade down and back up when the music changes between the simulator and Settings.")]
        [SerializeField] private float _switchSeconds = 0.7f;

        [Tooltip("Milliseconds per frame spent building the next track while this one plays.")]
        [SerializeField] private float _bakeMillisecondsPerFrame = 2f;

        /// <summary>Samples rendered between checks of the clock. Small enough to stop near the budget.</summary>
        private const int BakeSlice = 1024;

        /// <summary>
        /// The per-frame budget while the music is waiting on its track. Half a frame at 60 Hz:
        /// fast enough that the wait is a second or two, and still no frame anyone would see drop.
        /// </summary>
        private const float UrgentBakeMilliseconds = 8f;

        /// <summary>Whether the level's wanted track is built, on the source or ready beside it.</summary>
        private bool LevelTrackReady =>
            (_levelClip != null && _wanted == _track) || (_ready != null && _readyTrack == _wanted);

        /// <summary>The next track, part-built. Null when there is nothing left to build.</summary>
        private ProceduralAudio.MusicBake _bake;

        /// <summary>The next track, built and waiting, and which track it is.</summary>
        private AudioClip _ready;

        private int _readyTrack = -1;

        /// <summary>
        /// The level track's clip, whether or not it is the one on the source. Kept while the menu
        /// plays, so leaving the menu returns to the same track rather than building it again.
        /// </summary>
        private AudioClip _levelClip;

        /// <summary>The source the music plays on. Read-only to everyone else; exposed so a test can ask what is playing.</summary>
        public AudioSource MusicSource => _musicSource;

        /// <summary>Whether the source holds a menu track.</summary>
        private bool _onMenu;

        /// <summary>
        /// The menu track playing, or the last one played -- or, before any has, the session's
        /// random pick.
        /// </summary>
        private int _menuTrack;

        /// <summary>Whether any menu track has started yet this session.</summary>
        private bool _menuHeard;

        /// <summary>Whether the menu track has been seen playing, so that its stopping is its end.</summary>
        private bool _menuStarted;

        /// <summary>Whether the source has been given anything to play yet.</summary>
        private bool _begun;

        private const string MutedKey = "bitsorter.music.muted";

        private AudioSource _source;
        private AudioSource _musicSource;

        /// <summary>The shuffle the session's track is dealt from. Seeded once, in Awake.</summary>
        private MusicBag _bag;

        /// <summary>Track currently loaded into the source.</summary>
        private int _track;

        /// <summary>Track that should be playing: the shuffle's first, all session.</summary>
        private int _wanted;

        /// <summary>Fade multiplier, 0 to 1. Rides down and back up across a track change.</summary>
        private float _gain = 1f;

        /// <summary>Whether the game is silenced.</summary>
        /// <remarks>
        /// Kept in <see cref="Preferences"/> -- PlayerPrefs behind a redirectable seam -- rather
        /// than in the progress file. It describes this machine's
        /// speakers, not the player's circuits, and someone who copies a save to another computer
        /// should not carry a mute across with it.
        ///
        /// The switch for the whole game, which N reaches from anywhere; the music has its own switch under it
        /// (<see cref="MusicOn"/>).
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
        /// does not restart the phrase from the top.
        /// </remarks>
        public void SetMuted(bool muted) => Muted = muted;

        /// <summary>Where <see cref="MusicOn"/> is kept: the audio tests switch it on, since it starts off (Part 15a).</summary>
        public const string MusicKey = "bitsorter.music.on";

        /// <summary>Whether the music plays when the sound is on. Off until the player switches it on (Part 15a).</summary>
        /// <remarks>
        /// Off by default since BitSorter opens as a simulator (Ahmad's choice, 2026-10-27). A player who had switched it on
        /// keeps it on; one who never touched it, whose preference was the default, hears none.
        ///
        /// Under <see cref="Muted"/>, not beside it: the sound switch silences everything whatever
        /// this says, and switching the sound back on brings the music back only if this is on.
        /// Kept in <see cref="Preferences"/> with the mute, and a reset keeps it.
        /// </remarks>
        public static bool MusicOn
        {
            get => Preferences.GetInt(MusicKey, 0) != 0;
            private set => Preferences.SetInt(MusicKey, value ? 1 : 0);
        }

        /// <summary>Switches the music, and remembers it. Applied by the next frame's DriveMusic.</summary>
        public void SetMusic(bool on) => MusicOn = on;

        /// <summary>Whether the music source should be silent: the sound off, or the music.</summary>
        private static bool MusicSilent => Muted || !MusicOn;

        private const string VolumeKey = "bitsorter.volume";

        /// <summary>The volume a machine starts at: all of the game's own mix.</summary>
        public const int DefaultVolume = 100;

        /// <summary>The player's volume, 0 to 100 percent.</summary>
        /// <remarks>
        /// It scales the game's own mix rather than replacing it, so 100 is exactly how the game sounded before
        /// there was a slider. Kept in <see cref="Preferences"/> beside the mute, and like the mute
        /// it describes this machine's speakers, not the player's progress -- a reset keeps it.
        /// </remarks>
        public static int Volume => Mathf.Clamp(Preferences.GetInt(VolumeKey, DefaultVolume), 0, 100);

        /// <summary>
        /// Sets the volume and applies it at once. <paramref name="keep"/> writes it out; a drag
        /// passes false for every step and keeps it once, on release.
        /// </summary>
        public void SetVolume(int percent, bool keep)
        {
            percent = Mathf.Clamp(percent, 0, 100);

            if (keep)
                Preferences.SetInt(VolumeKey, percent);
            else
                Preferences.Stage(VolumeKey, percent);

            if (_musicSource != null)
                ApplyVolume();
        }

        /// <summary>Writes out a volume staged during a drag.</summary>
        public static void KeepVolume() => Preferences.Save();

        /// <summary>The scale the music is played at: the game's own mix, times the player's volume.</summary>
        private float Master => _masterVolume * Volume / 100f;

        private void Awake()
        {
            if (_settings == null) _settings = FindFirstObjectByType<SettingsPanel>();
            if (_credits == null) _credits = FindFirstObjectByType<CreditsPanel>();

            // The seed is the only random thing about the music: which track a session plays, and which of the
            // recordings Settings opens on. Everything after it is deterministic.
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            _bag = new MusicBag(ProceduralAudio.MusicTracks, seed);
            _track = _wanted = _bag.Current;
            _menuTrack = MusicRules.FirstMenuTrack(seed, _menuTracks?.Length ?? 0);

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D; the board is not a place
        }

        /// <summary>
        /// Frees the track this owns. A clip built at runtime belongs to no scene, so unloading the
        /// scene would otherwise leave three megabytes behind every time.
        /// </summary>
        /// <remarks>
        /// Only the clips built here. The menu tracks are imported assets, which Unity owns, and
        /// destroying one would break it for the rest of the session.
        /// </remarks>
        private void OnDestroy()
        {
            if (_levelClip != null)
                Destroy(_levelClip);

            if (_ready != null)
                Destroy(_ready);
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
            // Its own source: the loop needs to hold a playback position and be stoppable.
            //
            // Nothing is put on it yet. Whether the first thing heard is a recording or the generated track depends on
            // whether Settings is up, and that is only known once every Start has run. The first DriveMusic decides.
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * Master;
            _musicSource.mute = MusicSilent;
        }

        private void Update()
        {
            Keyboard keyboard = UiText.Keyboard;

            if (keyboard != null && keyboard.nKey.wasPressedThisFrame)
                ToggleMute();

            DriveMusic();
        }

        /// <summary>
        /// Builds the session's track a little each frame, so it is ready before it is wanted.
        /// </summary>
        /// <remarks>
        /// Building one takes a few hundred milliseconds; spread at a couple of milliseconds a frame it is done in a few
        /// seconds, and nobody sees it happen. Only the one track a session plays is built: in 4.x the next level's was built
        /// ahead too, and with no level changes it would be a second track held for nothing.
        /// </remarks>
        private void BakeAhead(bool urgent)
        {
            if (_bag == null)
                return;

            int next = _wanted;
            if (_levelClip != null && next == _track)
                return;   // built and on the source

            if (_ready != null)
            {
                if (_readyTrack == next)
                    return;

                Destroy(_ready);
                _ready = null;
                _readyTrack = -1;
            }

            if (_bake == null || _bake.Index != next)
                _bake = ProceduralAudio.BakeMusic(next);

            float budget = urgent ? UrgentBakeMilliseconds : _bakeMillisecondsPerFrame;
            float until = Time.realtimeSinceStartup + budget / 1000f;

            while (!_bake.Step(BakeSlice) && Time.realtimeSinceStartup < until) { }

            if (_bake.IsDone)
            {
                _ready = _bake.ToClip();
                _readyTrack = _bake.Index;
                _bake = null;
            }
        }

        /// <summary>
        /// The clip for a track, taken from what <see cref="BakeAhead"/> prepared if it is there.
        /// </summary>
        /// <remarks>
        /// It always is by the time a switch reaches here, because <see cref="DriveMusic"/> holds a
        /// switch until the track is built. The one exception is the very first frame: the build is finished on the spot,
        /// at startup, as it always was.
        /// </remarks>
        private AudioClip TakeClip(int track)
        {
            if (_ready != null && _readyTrack == track)
            {
                AudioClip clip = _ready;
                _ready = null;
                _readyTrack = -1;
                return clip;
            }

            ProceduralAudio.MusicBake bake =
                _bake != null && _bake.Index == track ? _bake : ProceduralAudio.BakeMusic(track);

            _bake = null;
            bake.Step(int.MaxValue);
            return bake.ToClip();
        }

        /// <summary>Whether the music is part-way through reacting to a change.</summary>
        /// <remarks>
        /// Lifted out of <see cref="DriveMusic"/> so <see cref="IsSettled"/> and the fade itself
        /// cannot disagree about what counts as a change.
        /// </remarks>
        private bool Switching
        {
            get
            {
                bool menu = MenuWanted;
                return _begun && (menu != _onMenu || (!menu && _wanted != _track));
            }
        }

        /// <summary>
        /// Whether the music has finished reacting to whatever last changed: nothing is switching
        /// and the fade is back up.
        /// </summary>
        /// <remarks>
        /// For tests, and deliberately neutral -- it says the system has stopped moving, not that
        /// any particular track is playing, so a test still has to assert which one and cannot pass
        /// by having waited for the answer it wanted.
        ///
        /// It exists because waiting a fixed number of seconds does not work. A track is built at
        /// two milliseconds a *frame*, and an editor running in the background renders few enough
        /// frames that five seconds of wall clock was not enough to finish one -- so seven audio
        /// tests failed, reproducibly, whenever the window was behind something else. Waiting for
        /// the condition instead of for the clock is the same lesson the cue tests already learned.
        /// </remarks>
        public bool IsSettled => _begun && !Switching && _gain >= 1f;

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
        /// The track switched to is normally built already, by <see cref="BakeAhead"/>. This used
        /// to say building it on the spot cost "a few milliseconds"; measured, it was 260 to 550,
        /// and the frame it landed on froze.
        /// </remarks>
        private void DriveMusic()
        {
            if (_musicSource == null)
                return;

            // Ahead of the fade, and ahead of its early-out: a settled track still has to notice
            // the player reaching for mute, or for the music switch.
            _musicSource.mute = MusicSilent;

            bool menu = MenuWanted;
            bool switching = Switching;

            // A level track not built yet is waited for, never stalled on. The music already
            // playing carries on at full volume while the build finishes at a faster pace, and the
            // change comes a second or two late -- which nobody hears, where a frozen frame is seen.
            bool waiting = switching && !menu && !LevelTrackReady;

            BakeAhead(urgent: waiting);

            // The first frame starts whatever belongs on screen, at full volume. A fade in from
            // silence would read as the game being slow to start.
            if (!_begun)
            {
                _begun = true;
                _gain = 1f;
                SwapTo(menu);
                ApplyVolume();
                return;
            }

            float rate = Time.unscaledDeltaTime / Mathf.Max(0.05f, _switchSeconds);

            if (waiting && !LevelTrackReady)
            {
                if (_gain < 1f)
                {
                    _gain = Mathf.Min(1f, _gain + rate);
                    ApplyVolume();
                }

                return;
            }

            if (switching)
            {
                _gain -= rate;

                if (_gain <= 0f)
                {
                    _gain = 0f;
                    SwapTo(menu);
                }
            }
            else if (_onMenu && MenuTrackEnded())
            {
                NextMenuTrack();
                return;
            }
            else if (_gain < 1f)
            {
                _gain = Mathf.Min(1f, _gain + rate);
            }
            else
            {
                return;   // settled; no reason to touch the volume every frame
            }

            ApplyVolume();
        }

        private void ApplyVolume() =>
            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * Master * _gain *
                                  (_onMenu ? MenuTrackGain : 1f);

        /// <summary>
        /// How far the menu track playing is turned down to sit at the level music's loudness.
        /// </summary>
        /// <remarks>
        /// The recordings were mastered 6 to 11 dB louder than the generated tracks, and played at
        /// the same volume the music dropped away every time a level started. Applied only while a
        /// menu track is on the source, and the swap between the two kinds of music happens at the
        /// bottom of the fade, so the change of level is never heard as a step.
        /// </remarks>
        private float MenuTrackGain =>
            Mathf.Pow(10f, (ProceduralAudio.MusicLoudnessDb - _menuTracks[_menuTrack].LoudnessDb) / 20f);

        /// <summary>
        /// Whether the recordings should be playing: Settings or the credits are up, and there are some.
        /// </summary>
        /// <remarks>
        /// Polled, the house pattern, rather than told. Without menu tracks -- a scene built without
        /// them -- Settings simply plays the generated music.
        /// </remarks>
        private bool MenuWanted =>
            ((_settings != null && _settings.IsShowing) || (_credits != null && _credits.IsShowing)) &&
            _menuTracks != null && _menuTracks.Length > 0 && _menuTracks[_menuTrack].Clip != null;

        /// <summary>
        /// Puts the right thing on the source: the current menu track, or the session's generated track.
        /// </summary>
        /// <remarks>
        /// The generated clip is kept while a recording plays, so going back resumes the same track. Any other
        /// clip this replaces is destroyed: nothing caches tracks, so one left behind would be three megabytes
        /// held until the game closed. A menu track is an imported asset and is never destroyed;
        /// leaving it unloads its audio data instead -- compressed, never decoded: in a browser the
        /// file the <c>audio</c> element plays, which is most of its cost there.
        /// </remarks>
        private void SwapTo(bool menu)
        {
            AudioClip leaving = _musicSource.clip;
            bool leavingMenu = _onMenu;

            if (menu)
            {
                PutMenuTrackOn();
            }
            else
            {
                if (_levelClip == null || _wanted != _track)
                {
                    AudioClip previous = _levelClip;

                    _levelClip = TakeClip(_wanted);
                    _track = _wanted;

                    if (previous != null)
                        Destroy(previous);
                }

                _musicSource.clip = _levelClip;
                _musicSource.loop = true;
                _onMenu = false;
            }

            _musicSource.Play();

            if (leavingMenu && leaving != null && leaving != _musicSource.clip)
                leaving.UnloadAudioData();
        }

        /// <summary>
        /// Whether the menu track has played to its end.
        /// </summary>
        /// <remarks>
        /// Stopped only counts once it has been seen playing: a track loaded in the background is
        /// not playing yet on the frame it is started, and that is not its end.
        /// </remarks>
        private bool MenuTrackEnded()
        {
            if (_musicSource.isPlaying)
            {
                _menuStarted = true;
                return false;
            }

            return _menuStarted;
        }

        /// <summary>The next menu track, straight after the last -- each ends on its own.</summary>
        private void NextMenuTrack()
        {
            AudioClip ended = _musicSource.clip;

            PutMenuTrackOn();
            _musicSource.Play();
            ApplyVolume();   // each menu track has its own loudness to be brought to

            if (ended != null && ended != _musicSource.clip)
                ended.UnloadAudioData();
        }

        /// <summary>
        /// Puts the next recording on the source: the session's random pick the first time, and
        /// the other one every time after -- when one ends, and every time Settings is opened again.
        /// </summary>
        /// <remarks>
        /// Moving on at every start means a player who dips into Settings hears both, and never the same opening twice running.
        /// </remarks>
        private void PutMenuTrackOn()
        {
            if (_menuHeard)
                _menuTrack = (_menuTrack + 1) % _menuTracks.Length;

            _menuHeard = true;

            AudioClip clip = _menuTracks[_menuTrack].Clip;
            clip.LoadAudioData();

            _musicSource.clip = clip;
            _musicSource.loop = false;   // they take turns rather than looping
            _onMenu = true;
            _menuStarted = false;
        }
    }
}
