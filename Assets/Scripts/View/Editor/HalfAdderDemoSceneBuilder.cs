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
    /// Builds the play scene and everything it needs, so none of it has to be wired by hand. Safe to
    /// re-run: it overwrites the generated assets and rebuilds the scene.
    /// </summary>
    /// <remarks>
    /// The scene no longer contains a circuit. <see cref="LevelSession"/> loads one from
    /// Assets/Resources/Levels at Start, and its _levelName field is the only thing that decides
    /// which. The file name and menu item still say half adder for the sake of the git history; the
    /// half adder is now level 2, in half-adder.json.
    /// </remarks>
    public static class HalfAdderDemoSceneBuilder
    {
        /// <summary>
        /// The level a scene built from nothing opens on. Any file name from Assets/Resources/Levels.
        /// </summary>
        /// <remarks>
        /// Only used when there is no existing scene to take the choice from. Rebuilding preserves
        /// whatever the scene was already set to -- see <see cref="StartingLevelFor"/>. Forcing this
        /// value on every rebuild silently threw away the level you had selected, which is exactly the
        /// kind of thing that reads as "the inspector is broken".
        /// </remarks>
        private const string DefaultStartingLevel = "route-the-bit";

        private const string ScenesFolder = "Assets/Scenes";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string ArtFolder = "Assets/Art/Generated";

        private const string ScenePath = ScenesFolder + "/HalfAdderDemo.unity";
        private const string BloomProfilePath = "Assets/Settings/DemoBloomProfile.asset";
        private const string SquareTexturePath = ArtFolder + "/WhiteSquare.png";
        private const string NodePrefabPath = PrefabsFolder + "/NodeSquare.prefab";
        private const string BitPrefabPath = PrefabsFolder + "/BitSquare.prefab";

        [MenuItem("BitSorter/Build Play Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // Read before NewScene throws the current one away.
            string startingLevel = StartingLevelFor();

            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ArtFolder);

            Sprite square = CreateSquareSprite();
            GameObject nodePrefab = CreateSquarePrefab(NodePrefabPath, "NodeSquare", square, 0);
            GameObject bitPrefab = CreateSquarePrefab(BitPrefabPath, "BitSquare", square, 2);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera camera = CreateCamera();
            CreateBloomVolume();
            Canvas canvas = CreateUi();

            var host = new GameObject("Simulation");

            // The grid is added first: SimulationRunner finds it during Awake, and both the level
            // loader and the placement rules read its extents to know where the board ends.
            PlacementGrid grid = host.AddComponent<PlacementGrid>();
            SimulationRunner runner = host.AddComponent<SimulationRunner>();
            LevelSession session = host.AddComponent<LevelSession>();
            NodeRenderer nodes = host.AddComponent<NodeRenderer>();
            EdgeRenderer edges = host.AddComponent<EdgeRenderer>();
            BitRenderer bits = host.AddComponent<BitRenderer>();
            PortRenderer ports = host.AddComponent<PortRenderer>();
            SimulationInput input = host.AddComponent<SimulationInput>();
            PlacementController placement = host.AddComponent<PlacementController>();
            WiringController wiring = host.AddComponent<WiringController>();
            WireDelayController wireDelay = host.AddComponent<WireDelayController>();
            SparkEffects sparks = host.AddComponent<SparkEffects>();
            ScorchMarks scorch = host.AddComponent<ScorchMarks>();
            GridPulse pulse = host.AddComponent<GridPulse>();
            BoardBackground board = host.AddComponent<BoardBackground>();
            PointerGate pointer = host.AddComponent<PointerGate>();

            // The interface builds its own hierarchy under the canvas at runtime, the same way the
            // grid builds its dots. Only the component and its references live in the scene.
            GatePaletteView palette = host.AddComponent<GatePaletteView>();
            RunControls runControls = host.AddComponent<RunControls>();
            StatusBanner banner = host.AddComponent<StatusBanner>();
            BitsLostMeter bitsLost = host.AddComponent<BitsLostMeter>();

            DiagnosticsPanel diagnostics = host.AddComponent<DiagnosticsPanel>();
            ProgressTracker progress = host.AddComponent<ProgressTracker>();
            LevelSelectPanel levelSelect = host.AddComponent<LevelSelectPanel>();
            HelpPanel help = host.AddComponent<HelpPanel>();
            WinPanel winPanel = host.AddComponent<WinPanel>();
            SinkCelebration celebration = host.AddComponent<SinkCelebration>();
            MainMenu mainMenu = host.AddComponent<MainMenu>();
            EndingPanel ending = host.AddComponent<EndingPanel>();
            SandboxPanel sandbox = host.AddComponent<SandboxPanel>();
            SinkReadout sinkReadout = host.AddComponent<SinkReadout>();

            // AddComponent brings its own AudioSource along, via RequireComponent.
            GameAudio audio = host.AddComponent<GameAudio>();

            Assign(board, "_camera", camera);

            // Added here rather than in CreateCamera because the grid it measures does not exist
            // yet at that point.
            CameraFit fit = camera.gameObject.AddComponent<CameraFit>();
            Assign(fit, "_grid", grid);
            Assign(bits, "_sparks", sparks);
            Assign(scorch, "_runner", runner);

            Assign(pulse, "_grid", grid);
            Assign(pulse, "_runner", runner);
            Assign(ports, "_runner", runner);
            Assign(ports, "_stubPrefab", bitPrefab);
            Assign(grid, "_dotPrefab", bitPrefab);
            Assign(runner, "_grid", grid);
            Assign(nodes, "_runner", runner);
            Assign(nodes, "_nodePrefab", nodePrefab);
            Assign(edges, "_runner", runner);
            Assign(edges, "_delay", wireDelay);
            Assign(edges, "_sparks", sparks);
            Assign(bits, "_runner", runner);
            Assign(bits, "_bitPrefab", bitPrefab);

            // The session owns every edit, so the two editing controllers and the input component all
            // talk to it. WiringController keeps the runner as well, for layout and the read-only view
            // its hit testing and preview need.
            Assign(session, "_runner", runner);
            AssignString(session, "_levelName", startingLevel);
            Assign(wiring, "_session", session);
            Assign(wiring, "_runner", runner);
            Assign(wiring, "_camera", camera);
            Assign(placement, "_session", session);
            Assign(placement, "_grid", grid);
            Assign(placement, "_camera", camera);

            // Holds the wiring controller so re-timing stays suppressed while a new wire is dragged.
            Assign(wireDelay, "_session", session);
            Assign(wireDelay, "_runner", runner);
            Assign(wireDelay, "_wiring", wiring);
            Assign(wireDelay, "_camera", camera);
            // One gate, three consumers. Every mouse reader asks it before acting, which is what stops
            // a single press being handled by two of them at once.
            Assign(pointer, "_wiring", wiring);
            Assign(placement, "_pointer", pointer);
            Assign(wiring, "_pointer", pointer);
            Assign(wireDelay, "_pointer", pointer);

            Assign(input, "_session", session);
            Assign(input, "_runner", runner);
            Assign(palette, "_session", session);
            Assign(palette, "_placement", placement);
            Assign(palette, "_canvas", canvas);
            Assign(palette, "_pointer", pointer);
            Assign(palette, "_grid", grid);
            Assign(palette, "_camera", camera);

            Assign(runControls, "_session", session);
            Assign(runControls, "_runner", runner);
            Assign(runControls, "_canvas", canvas);

            Assign(banner, "_session", session);
            Assign(banner, "_runner", runner);
            Assign(banner, "_canvas", canvas);

            Assign(bitsLost, "_session", session);
            Assign(bitsLost, "_runner", runner);
            Assign(bitsLost, "_canvas", canvas);

            Assign(diagnostics, "_runner", runner);
            Assign(diagnostics, "_session", session);
            Assign(diagnostics, "_canvas", canvas);

            Assign(progress, "_session", session);
            Assign(progress, "_runner", runner);

            Assign(levelSelect, "_session", session);
            Assign(levelSelect, "_progress", progress);
            Assign(levelSelect, "_sandbox", sandbox);
            Assign(levelSelect, "_canvas", canvas);

            Assign(sandbox, "_session", session);
            Assign(sandbox, "_progress", progress);
            Assign(sandbox, "_runner", runner);
            Assign(sandbox, "_canvas", canvas);

            Assign(sinkReadout, "_session", session);
            Assign(sinkReadout, "_runner", runner);
            Assign(sinkReadout, "_canvas", canvas);

            Assign(help, "_session", session);
            Assign(help, "_canvas", canvas);

            Assign(winPanel, "_session", session);
            Assign(winPanel, "_runner", runner);
            Assign(winPanel, "_progress", progress);
            Assign(winPanel, "_canvas", canvas);

            Assign(ending, "_session", session);
            Assign(ending, "_progress", progress);
            Assign(ending, "_menu", mainMenu);
            Assign(ending, "_canvas", canvas);

            Assign(celebration, "_session", session);
            Assign(celebration, "_runner", runner);

            Assign(mainMenu, "_session", session);
            Assign(mainMenu, "_progress", progress);
            Assign(mainMenu, "_levels", levelSelect);
            Assign(mainMenu, "_sandbox", sandbox);
            Assign(mainMenu, "_canvas", canvas);

            Assign(audio, "_runner", runner);
            Assign(audio, "_session", session);
            Assign(audio, "_bits", bits);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Says which level, because this used to reset it without a word.
            Debug.Log($"Built {ScenePath} starting on '{startingLevel}'. It is open now -- press Play. " +
                      "Q and E change level while playing.");
        }

        /// <summary>
        /// The level the rebuilt scene should open on: whatever the scene being replaced was set to, or
        /// <see cref="DefaultStartingLevel"/> when building from nothing.
        /// </summary>
        /// <remarks>
        /// Only reads the scene already open, and does not go opening the file to find out. A rebuild is
        /// nearly always done with the scene in front of you, and opening scenes behind the user's back
        /// to salvage one string is a worse trade than falling back to the default.
        /// </remarks>
        private static string StartingLevelFor()
        {
            LevelSession existing = Object.FindFirstObjectByType<LevelSession>();

            if (existing == null || string.IsNullOrWhiteSpace(existing.LevelName))
                return DefaultStartingLevel;

            return existing.LevelName;
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            // The framing the board was designed at. CameraFit treats this as a floor and zooms out
            // on anything narrower, so it is no longer the only shape the game is playable at.
            camera.orthographicSize = 5.5f;   // shows roughly x -9..9 at 16:9
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
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
        /// SMAA, and a camera-space canvas would be post-processed along with the board -- readable
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
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Halfway between matching width and height, so neither axis is favoured when the window
            // is not 16:9. The board is wider than it is tall, so a width-only match would push the
            // interface off a tall window.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystemObject = new GameObject("Event System");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();

            return canvas;
        }

        /// <summary>
        /// A global Volume carrying a Bloom override, which is what makes the glow sprites read as
        /// light rather than as pale blobs.
        /// </summary>
        private static void CreateBloomVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, BloomProfilePath);

            var bloom = profile.Add<Bloom>(true);

            // Colours here are LDR, so the threshold has to sit below 1 or nothing would ever
            // qualify and the effect would appear to do nothing.
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.62f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.15f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.72f;

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

        /// <summary>
        /// Writes a white PNG and imports it at 16 pixels per unit, so the sprite is exactly one
        /// world unit square and scale values in the renderers read as world units.
        /// </summary>
        private static Sprite CreateSquareSprite()
        {
            const int size = 16;

            var texture = new Texture2D(size, size);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(SquareTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(SquareTexturePath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(SquareTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(SquareTexturePath);
        }

        private static GameObject CreateSquarePrefab(string path, string name, Sprite sprite, int sortingOrder)
        {
            var temporary = new GameObject(name);
            var renderer = temporary.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            Object.DestroyImmediate(temporary);

            return prefab;
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
        /// The same for a string field. objectReferenceValue would silently do nothing here, leaving
        /// the level name empty and the scene loading no level at all.
        /// </summary>
        private static void AssignString(Object target, string fieldName, string value)
        {
            SerializedObject serialized = Find(target, fieldName, out SerializedProperty property);

            if (serialized == null)
                return;

            property.stringValue = value;
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
