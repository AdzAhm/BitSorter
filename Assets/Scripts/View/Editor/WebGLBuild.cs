using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace BitSorter.View.Editor
{
    /// <summary>
    /// Produces the browser build, with the settings both itch.io and Unity Play accept.
    /// </summary>
    /// <remarks>
    /// The compression choice is the part that matters and the part that goes wrong.
    ///
    /// Brotli gives much the smallest download, but a compressed Unity build only loads if the
    /// server answers with a matching Content-Encoding header. Unity Play sets those headers.
    /// A plain static host generally does not, and the failure is not subtle: the loader aborts
    /// with a console error about unexpected content and the player sees a blank page.
    ///
    /// Decompression fallback is the answer to that. It ships a decompressor inside the loader so
    /// the build works whether or not the host cooperates, at the cost of a slightly larger loader
    /// and a little CPU at startup. Enabled here, because one build that runs everywhere is worth
    /// more than two builds that each run in one place.
    ///
    /// Data caching keeps the downloaded data in IndexedDB, so a returning player does not fetch
    /// the whole thing again.
    /// </remarks>
    public static class WebGLBuild
    {
        private const string Scene = "Assets/Scenes/HalfAdderDemo.unity";
        private const string OutputFolder = "Build/WebGL";

        [MenuItem("BitSorter/Build WebGL Player")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) == null)
            {
                Debug.LogError($"BitSorter: cannot build, {Scene} is missing.");
                return;
            }

            string root = Directory.GetParent(Application.dataPath).FullName;
            string target = Path.Combine(root, OutputFolder);

            Directory.CreateDirectory(target);
            Configure();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = target,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"BitSorter: WebGL build {summary.result}, {summary.totalErrors} errors.");
                return;
            }

            Debug.Log($"BitSorter: WebGL built to {target} " +
                      $"({summary.totalSize / (1024 * 1024)} MB before server compression)");
        }

        /// <summary>
        /// The browser build's settings, shared with <see cref="BenchmarkBuild"/> so the engine is
        /// measured as it ships.
        /// </summary>
        public static void Configure()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;

            // The project's own template, which exists for one line: it sets
            // autoSyncPersistentDataPath. Unity's stock template leaves that commented out, and
            // without it every save the game makes is thrown away when the tab closes.
            PlayerSettings.WebGL.template = "PROJECT:BitSorter";

            // Thrown exceptions only. Full stack support costs size and speed, and nothing here
            // relies on catching engine-level faults.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            // WebAssembly 2023 (2026-10-15): native WebAssembly exceptions, bulk memory, BigInt,
            // SIMD and the rest, where the original feature set did them with JavaScript trampolines.
            // Smaller code, and it needs Chrome 91, Firefox 89 or Safari 16.4 or newer (2021, 2021
            // and March 2023), which every browser a course's students use has long passed.
            PlayerSettings.WebGL.wasm2023 = true;

            // Built for a small download rather than a quick build (2026-10-06, before 4.0.0): the
            // wasm was half of it. Set here because the code optimization lives in Library/, per
            // machine, and would otherwise be whatever this editor last had -- it was BuildTimes.
            // Stripping is Medium (2026-10-15; it was Low, the conservative level): no runtime script
            // uses reflection, and at Minimal every package's code went into the wasm whole, used or
            // not. The risks are in packages -- Unity Services finds its initialisers by type name,
            // the Input System builds layouts by reflection, JsonUtility reads the level and save
            // classes -- so the browser build was smoke-tested at Medium before it was kept: levels,
            // a save and reload, input, Settings, the workbench. WebGL only, so the Windows build,
            // which is Mono, is untouched.
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.WebGL,
                UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);

            // The three the download also rests on, held here so a build cannot inherit them from a box last ticked in the
            // dialog (2026-11-03, the optimize-web pass): engine code nothing calls left out of the wasm, no debug symbols in
            // it or beside it, and no diagnostics overlay in the page. Each was already so in the project's settings.
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.showDiagnostics = false;

            LeaveOutTheBuiltInPipelinesShaders();
            LeaveOutShadersNothingDrawsWith();
        }

        /// <summary>
        /// The built-in render pipeline's seven shaders for its own passes, which a project rendering with URP never draws
        /// with: deferred shading and reflections, screen-space shadows, depth-normals, motion vectors, the light halo and
        /// the lens flare. At "built-in" each went into every build -- about 25 KB of the download -- and URP has its own
        /// pass for every one of them that it makes at all.
        /// </summary>
        /// <remarks>
        /// GraphicsSettings.asset says "no support" for all seven, for every build; set here as well so the browser build
        /// does not rest on an open editor having read that file again since it changed (2026-11-03, the optimize-web
        /// pass). <c>RenderSettingsTests</c> holds the file.
        /// </remarks>
        private static void LeaveOutTheBuiltInPipelinesShaders()
        {
            BuiltinShaderType[] unused =
            {
                BuiltinShaderType.DeferredShading, BuiltinShaderType.DeferredReflections,
                BuiltinShaderType.ScreenSpaceShadows, BuiltinShaderType.DepthNormals, BuiltinShaderType.MotionVectors,
                BuiltinShaderType.LightHalo, BuiltinShaderType.LensFlare,
            };

            foreach (BuiltinShaderType type in unused)
                GraphicsSettings.SetShaderMode(type, BuiltinShaderMode.Disabled);
        }

        /// <summary>
        /// Two of the shaders the graphics settings put in every build whether anything uses them or not, which nothing in
        /// BitSorter draws with: the built-in pipeline's Legacy Diffuse (and its fallback, VertexLit, with it), and the UI
        /// shader for a sprite whose alpha is split into a texture of its own, which only Android's ETC1 does. About 12 KB.
        /// </summary>
        /// <remarks>
        /// The rest of the list stays: UI/Default is every uGUI graphic's, Sprites/Default the default sprite material's, and
        /// the three cubemap shaders are the engine's own for reflections, which nothing here asks for but which are not
        /// worth the doubt at 5 KB. GraphicsSettings.asset leaves the two out for every build; taken out here by name as well
        /// so the browser build does not rest on an open editor having read that file again (2026-11-03, the optimize-web
        /// pass). <c>RenderSettingsTests</c> holds the file.
        /// </remarks>
        private static void LeaveOutShadersNothingDrawsWith()
        {
            string[] neverDrawnWith = { "Legacy Shaders/Diffuse", "UI/DefaultETC1" };

            var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty included = settings.FindProperty("m_AlwaysIncludedShaders");

            for (int i = included.arraySize - 1; i >= 0; i--)
            {
                var shader = included.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader == null || System.Array.IndexOf(neverDrawnWith, shader.name) < 0)
                    continue;

                // An object reference's element was once only cleared by the first delete, and removed by the second.
                int before = included.arraySize;
                included.DeleteArrayElementAtIndex(i);
                if (included.arraySize == before)
                    included.DeleteArrayElementAtIndex(i);
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
