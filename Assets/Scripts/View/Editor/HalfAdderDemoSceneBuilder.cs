using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BitSorter.View.EditorTools
{
    /// <summary>
    /// Builds the scene and everything it needs, so none of it has to be wired by hand. Safe to
    /// re-run: it rebuilds the scene.
    /// </summary>
    /// <remarks>
    /// The scene is the simulator: the workbench, Settings and the credits, the save, the music and the frame-rate counter,
    /// on one canvas and one shell panel that each builds its own hierarchy at runtime. It built the 4.x game's board and
    /// HUD too until Part 16a deleted the game. The file name and menu item still say half adder for the sake of the git
    /// history.
    /// </remarks>
    public static class HalfAdderDemoSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";

        private const string ScenePath = ScenesFolder + "/HalfAdderDemo.unity";
        private const string BloomProfilePath = "Assets/Settings/DemoBloomProfile.asset";

        /// <summary>
        /// The recordings Settings plays, and how loud each is. Imported, free-licence tracks;
        /// their credit is Credits.MenuMusic, on the credits roll, and the README says where each
        /// came from.
        /// </summary>
        /// <remarks>
        /// The loudness is the file's RMS in dBFS once imported -- mixed down to mono and normalised
        /// to its peak, as its .meta says -- measured by decoding the file. Settings turns each
        /// track to the generated music's loudness with it. AudioPlayTests decodes both files again and
        /// fails if Settings does not then sound as loud as the simulator, so a track added or re-exported
        /// needs measuring before it goes in.
        /// </remarks>
        private static readonly (string Path, float LoudnessDb)[] MenuTrackFiles =
        {
            ("Assets/Audio/Music/jkjkke-dream.mp3", -13.6f),
            ("Assets/Audio/Music/matthew-pablo-woodland-fantasy.mp3", -18.5f),
        };

        [MenuItem("BitSorter/Build Play Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(ScenesFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateBloomVolume();
            Canvas canvas = CreateUi();

            var host = new GameObject("Simulation");

            ProgressTracker progress = host.AddComponent<ProgressTracker>();
            SettingsPanel settings = host.AddComponent<SettingsPanel>();

            // The simulator itself, which BitSorter opens on (Part 15a); Settings and the credits are pages of it.
            WorkbenchPanel workbench = host.AddComponent<WorkbenchPanel>();

            // The shell's typeface (Part 11a): UI Toolkit makes its own font asset from these, so they ship in the build.
            ShellFonts shellFonts = host.AddComponent<ShellFonts>();
            Assign(shellFonts, "_regular", AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IBMPlexSans/IBMPlexSans-Regular.ttf"));
            Assign(shellFonts, "_bold", AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IBMPlexSans/IBMPlexSans-Bold.ttf"));
            Assign(shellFonts, "_mono", AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IBMPlexMono/IBMPlexMono-Regular.ttf"));
            CreditsPanel credits = host.AddComponent<CreditsPanel>();

            // Over everything, on a canvas of its own, so its place in this list does not matter.
            FrameRateCounter frameCounter = host.AddComponent<FrameRateCounter>();

            // AddComponent brings its own AudioSource along, via RequireComponent.
            GameAudio audio = host.AddComponent<GameAudio>();

            Assign(settings, "_progress", progress);
            Assign(settings, "_credits", credits);
            Assign(settings, "_workbench", workbench);   // pages of the workbench's own since Part 15g

            Assign(workbench, "_canvas", canvas);

            Assign(credits, "_settings", settings);
            Assign(credits, "_workbench", workbench);

            Assign(frameCounter, "_canvas", canvas);

            Assign(audio, "_settings", settings);
            Assign(audio, "_credits", credits);
            AssignMenuTracks(audio);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {ScenePath}. It is open now -- press Play.");
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Taken from the palette, so nothing in the game names a colour of its own.
            camera.backgroundColor = Palette.Current.Ground;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            // Without this the camera renders no post-processing at all, so the Volume below would
            // be silently ignored and nothing would bloom. URP adds the component on demand, but
            // renderPostProcessing defaults to false.
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // Without a listener every AudioSource in the scene plays to nobody, silently and with no
            // warning. The scene had none, so this is the difference between the game having sound
            // and appearing to have none.
            cameraObject.AddComponent<AudioListener>();

            return camera;
        }

        /// <summary>
        /// The canvas and the event system that feeds it.
        /// </summary>
        /// <remarks>
        /// Screen Space Overlay deliberately, not Screen Space Camera. The camera runs bloom and
        /// SMAA, and a camera-space canvas would be post-processed with it -- readable
        /// text is not improved by being bloomed.
        ///
        /// The input module must be <see cref="InputSystemUIInputModule"/>. The project is set to the
        /// new Input System exclusively (activeInputHandler 1), so the legacy StandaloneInputModule
        /// would compile, sit in the scene, and quietly deliver no events at all.
        /// </remarks>
        private static Canvas CreateUi()
        {
            var canvasObject = new GameObject("Game UI");

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // From UiTheme, which every panel offset in the interface is measured against.
            scaler.referenceResolution = UiTheme.ReferenceResolution;

            // Halfway between matching width and height, so neither axis is favoured when the window
            // is not 16:9.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystemObject = new GameObject("Event System");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();

            return canvas;
        }

        /// <summary>
        /// A global Volume carrying a Bloom override, which <see cref="LookBoot"/> sets to the look's on every load. The 4.x
        /// board bloomed its bits in flight; whether any look still wants bloom is Part 16b's question.
        /// </summary>
        private static void CreateBloomVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, BloomProfilePath);

            var bloom = profile.Add<Bloom>(true);

            // At 1, so nothing drawn in LDR can cross it.
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;

            AssetDatabase.AddObjectToAsset(bloom, profile);
            EditorUtility.SetDirty(profile);

            var host = new GameObject("Global Volume");
            Volume volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            // sharedProfile, not profile. Volume.profile returns an instantiated runtime copy for
            // per-instance tweaking and never writes the serialized field, so assigning it leaves
            // sharedProfile at null and the saved scene blooms nothing.
            volume.sharedProfile = profile;
        }

        /// <summary>Sets a [SerializeField] private field, which is not reachable directly.</summary>
        private static void Assign(Object target, string fieldName, Object value)
        {
            SerializedObject serialized = Find(target, fieldName, out SerializedProperty property);

            if (serialized == null)
                return;

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Gives GameAudio the recordings, each loaded from its path with its loudness beside it.
        /// </summary>
        /// <remarks>
        /// An error rather than a quiet gap for any track that is missing: Settings would simply play
        /// the generated music, and nobody would know why.
        /// </remarks>
        private static void AssignMenuTracks(Object audio)
        {
            SerializedObject serialized = Find(audio, "_menuTracks", out SerializedProperty property);

            if (serialized == null)
                return;

            property.arraySize = MenuTrackFiles.Length;

            for (int i = 0; i < MenuTrackFiles.Length; i++)
            {
                (string path, float loudness) = MenuTrackFiles[i];
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

                if (clip == null)
                    Debug.LogError($"BitSorter: menu track missing at {path}.");

                SerializedProperty track = property.GetArrayElementAtIndex(i);
                track.FindPropertyRelative("Clip").objectReferenceValue = clip;
                track.FindPropertyRelative("LoudnessDb").floatValue = loudness;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedObject Find(Object target, string fieldName, out SerializedProperty property)
        {
            var serialized = new SerializedObject(target);
            property = serialized.FindProperty(fieldName);

            if (property != null)
                return serialized;

            // Loud, because a renamed field otherwise shows up much later as a null reference in a
            // scene that looked like it built correctly.
            Debug.LogError($"{target.GetType().Name} has no serialized field '{fieldName}'.");
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "Assets" || AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
