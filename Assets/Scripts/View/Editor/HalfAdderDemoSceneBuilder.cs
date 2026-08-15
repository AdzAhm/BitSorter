using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BitSorter.View.EditorTools
{
    /// <summary>
    /// Builds the half adder demo scene and everything it needs, so none of it has to be wired by
    /// hand. Safe to re-run: it overwrites the generated assets and rebuilds the scene.
    /// </summary>
    public static class HalfAdderDemoSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string ArtFolder = "Assets/Art/Generated";

        private const string ScenePath = ScenesFolder + "/HalfAdderDemo.unity";
        private const string SquareTexturePath = ArtFolder + "/WhiteSquare.png";
        private const string NodePrefabPath = PrefabsFolder + "/NodeSquare.prefab";
        private const string BitPrefabPath = PrefabsFolder + "/BitSquare.prefab";

        [MenuItem("BitSorter/Build Half Adder Demo Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ArtFolder);

            Sprite square = CreateSquareSprite();
            GameObject nodePrefab = CreateSquarePrefab(NodePrefabPath, "NodeSquare", square, 0);
            GameObject bitPrefab = CreateSquarePrefab(BitPrefabPath, "BitSquare", square, 2);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();

            var host = new GameObject("Simulation");
            SimulationRunner runner = host.AddComponent<SimulationRunner>();
            NodeRenderer nodes = host.AddComponent<NodeRenderer>();
            EdgeRenderer edges = host.AddComponent<EdgeRenderer>();
            BitRenderer bits = host.AddComponent<BitRenderer>();
            SimulationHud hud = host.AddComponent<SimulationHud>();
            SimulationInput input = host.AddComponent<SimulationInput>();

            Assign(nodes, "_runner", runner);
            Assign(nodes, "_nodePrefab", nodePrefab);
            Assign(edges, "_runner", runner);
            Assign(bits, "_runner", runner);
            Assign(bits, "_bitPrefab", bitPrefab);
            Assign(hud, "_runner", runner);
            Assign(input, "_runner", runner);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {ScenePath}. It is open now -- press Play.");
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;   // shows roughly x -9..9 at 16:9
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
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
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{fieldName}'.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
