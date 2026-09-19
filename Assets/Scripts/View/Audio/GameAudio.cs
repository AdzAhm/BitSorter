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
        [SerializeField] private MainMenu _menu;

        [Tooltip("Played while the main menu is showing, one after another. Imported, not generated: " +
                 "see MenuMusicCredit, which the menu shows.")]
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

        /// <summary>
        /// The credit the main menu shows for <see cref="_menuTracks"/>. One copy, here beside them.
        /// </summary>
        /// <remarks>
        /// Woodland Fantasy is CC BY 3.0, which requires the author, the title, the licence and a note
        /// of any change -- it is played in mono -- wherever the work is used. Dream is CC0 and owed
        /// nothing, and is credited anyway. Both came from OpenGameArt, where each licence was read
        /// before either was downloaded; the README's credits say where.
        /// </remarks>
        public const string MenuMusicCredit =
            "Menu music: \"Dream\" by jkjkke (CC0)  ·  " +
            "\"Woodland Fantasy\" by Matthew Pablo, matthewpablo.com (CC BY 3.0, played in mono)";

        [Tooltip("Scales every cue. Zero is silence.")]
        [Range(0f, 1f)]
        [SerializeField] private float _masterVolume = 0.8f;

        [Tooltip("Most gate cues in one frame. A wide circuit can fire many at once.")]
        [SerializeField] private int _gateBurstLimit = 3;

        [Tooltip("Seconds to fade down and back up when the track changes at a level boundary.")]
        [SerializeField] private float _switchSeconds = 0.7f;

        [Tooltip("Milliseconds per frame spent building the next track while this one plays.")]
        [SerializeField] private float _bakeMillisecondsPerFrame = 2f;

        /// <summary>Samples rendered between checks of the clock. Small enough to stop near the budget.</summary>
        private const int BakeSlice = 1024;

        /// <summary>
        /// The per-frame budget while a level change is waiting on its track. Half a frame at 60 Hz:
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

        /// <summary>
        /// The source the music plays on, as opposed to the one firing cues. Read-only to everyone
        /// else; exposed so a test can ask what is playing, as <see cref="CuesPlayed"/> is.
        /// </summary>
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
            if (_menu == null) _menu = FindFirstObjectByType<MainMenu>();

            // The seed is the only random thing about the music: a different shuffle per session,
            // so two evenings on the same levels are not the same evening, and which of the menu's
            // tracks the game opens on. Everything after it is deterministic.
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            _bag = new MusicBag(ProceduralAudio.MusicTracks, seed);
            _track = _wanted = _bag.Current;
            _menuTrack = MusicRules.FirstMenuTrack(seed, _menuTracks?.Length ?? 0);

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
            //
            // Nothing is put on it yet. Whether the first thing heard is a menu track or a level
            // track depends on whether the main menu is up, and that is only known once every Start
            // has run -- the menu opens itself in its own. The first DriveMusic decides.
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * _masterVolume;
            _musicSource.mute = Muted;
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
        /// Builds the next track a little each frame, so it is ready before it is wanted.
        /// </summary>
        /// <remarks>
        /// The next track is the one being switched to if a switch is under way, and otherwise the
        /// one the shuffle will deal next. Building one takes a few hundred milliseconds; spread at a
        /// couple of milliseconds a frame it is done in a few seconds, long before a player finishes
        /// a level, and nobody sees it happen.
        ///
        /// At most two tracks are held: the one playing and the one ready -- plus, while it is being
        /// built, the buffer it is built in. Every track used to stay for the session, which at two
        /// dozen tracks is more browser heap than the whole game otherwise uses.
        /// </remarks>
        private void BakeAhead(bool urgent)
        {
            if (_bag == null)
                return;

            // The level track still to be built comes first: none yet, at boot under the menu, or
            // the one a level change is switching to. Otherwise, the one the shuffle deals next.
            int next = _levelClip == null || _wanted != _track ? _wanted : _bag.PeekNext();

            // A bag of one: the next track is this one, already built, and there is nothing to do.
            if (_levelClip != null && next == _track)
                return;

            if (_ready != null)
            {
                if (_readyTrack == next)
                    return;

                // The shuffle moved on past it -- two level changes inside one build.
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
        /// switch until the track is built. The one exception is the very first frame, when there
        /// is no main menu to play over the wait: the build is finished on the spot, at startup,
        /// as it always was.
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
            // the player reaching for mute.
            _musicSource.mute = Muted;

            bool menu = MenuWanted;
            bool switching = _begun && (menu != _onMenu || (!menu && _wanted != _track));

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
            _musicSource.volume = ProceduralAudio.VolumeOf(Cue.Music) * _masterVolume * _gain *
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
        /// Whether the menu's own music should be playing: the main menu is up, and there is some.
        /// </summary>
        /// <remarks>
        /// Polled, the house pattern, rather than told. Without menu tracks -- a scene built without
        /// them -- the menu simply plays the level music, as it did before it had any of its own.
        /// </remarks>
        private bool MenuWanted =>
            _menu != null && _menu.IsOpen &&
            _menuTracks != null && _menuTracks.Length > 0 && _menuTracks[_menuTrack].Clip != null;

        /// <summary>
        /// Puts the right thing on the source: the current menu track, or the level's track.
        /// </summary>
        /// <remarks>
        /// The level's clip is kept while the menu plays, so going back to the level resumes the
        /// same track -- the track changes only when the level does. Any other level clip this
        /// replaces is destroyed: nothing caches tracks, so one left behind would be three megabytes
        /// held until the game closed. A menu track is an imported asset and is never destroyed;
        /// leaving it unloads its decoded audio instead, which in a browser is most of its cost.
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
        /// Puts the menu's next track on the source: the session's random pick the first time, and
        /// the other one every time after -- when one ends, and every time the menu is opened again.
        /// </summary>
        /// <remarks>
        /// Opening the menu used to start the same track from the top every time, and the other was
        /// only heard if the menu stayed up until the first had finished. Moving on at every start
        /// means a player who dips into the menu hears both, and never the same opening twice
        /// running.
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
