using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BitSorter.View.EditorTools
{
    /// <summary>
    /// Builds the scene and everything it needs, so none of it has to be wired by hand. Safe to
    /// re-run: it rebuilds the scene.
    /// </summary>
    /// <remarks>
    /// The scene is the simulator: the workbench, Settings and the credits, the save and the frame-rate counter,
    /// on one canvas and one shell panel that each builds its own hierarchy at runtime. It built the 4.x game's board and
    /// HUD too until Part 16a deleted the game. The file name and menu item still say half adder for the sake of the git
    /// history.
    /// </remarks>
    public static class HalfAdderDemoSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";

        private const string ScenePath = ScenesFolder + "/HalfAdderDemo.unity";

        [MenuItem("BitSorter/Build Play Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(ScenesFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
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

            Assign(settings, "_progress", progress);
            Assign(settings, "_credits", credits);
            Assign(settings, "_workbench", workbench);   // pages of the workbench's own since Part 15g

            Assign(workbench, "_canvas", canvas);

            Assign(credits, "_settings", settings);
            Assign(credits, "_workbench", workbench);

            Assign(frameCounter, "_canvas", canvas);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {ScenePath}. It is open now -- press Play.");
        }

        /// <summary>
        /// The camera, which draws nothing but its ground colour: the workbench is on an overlay canvas and a UI Toolkit
        /// panel, both drawn over whatever the camera made.
        /// </summary>
        /// <remarks>
        /// No post-processing, so none is asked for: <c>renderPostProcessing</c> stays at URP's default, off. Until Part
        /// 16b the camera ran bloom and SMAA on this empty image every frame -- an overlay is drawn after
        /// post-processing, so neither ever touched anything on screen.
        ///
        /// **And no HDR**, which was there for the bloom. With it, URP's 2D renderer drew this camera into screen-sized
        /// float targets of its own -- two colour, one depth -- and copied the result to the screen every frame; without
        /// it the camera clears the screen itself, and the workbench's opaque ground covers what it cleared either way.
        /// The URP asset keeps HDR: <see cref="WaveformPng"/>'s camera draws the PNG through it, and
        /// leaving it there leaves the picture as it was.
        /// </remarks>
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
            camera.allowHDR = false;

            return camera;
        }

        /// <summary>
        /// The canvas and the event system that feeds it.
        /// </summary>
        /// <remarks>
        /// Screen Space Overlay deliberately, not Screen Space Camera: it is drawn straight to the screen, sharp at any
        /// scale, and never touched by the camera's own rendering.
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

        /// <summary>Sets a [SerializeField] private field, which is not reachable directly.</summary>
        private static void Assign(Object target, string fieldName, Object value)
        {
            SerializedObject serialized = Find(target, fieldName, out SerializedProperty property);

            if (serialized == null)
                return;

            property.objectReferenceValue = value;
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
