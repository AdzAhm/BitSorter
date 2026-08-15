using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Fallback visuals, so the scene still draws if a prefab reference is missing. The editor
    /// scene builder assigns real prefabs; this exists so a broken reference degrades to a plain
    /// white square instead of an empty screen.
    /// </summary>
    internal static class ViewSprites
    {
        private static Sprite _square;

        /// <summary>A white square exactly one world unit across, generated once and shared.</summary>
        public static Sprite Square()
        {
            if (_square != null)
                return _square;

            const int size = 16;
            var texture = new Texture2D(size, size) { filterMode = FilterMode.Point };
            var pixels = new Color32[size * size];

            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply();

            // pixelsPerUnit == size, so the sprite measures 1x1 world units.
            _square = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return _square;
        }

        /// <summary>
        /// Instantiates <paramref name="prefab"/>, or builds a plain square if it is missing.
        /// The result always carries a <see cref="SpriteRenderer"/>.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, string name)
        {
            GameObject instance = prefab != null
                ? Object.Instantiate(prefab, parent)
                : new GameObject(name, typeof(SpriteRenderer));

            if (instance.transform.parent != parent)
                instance.transform.SetParent(parent, false);

            instance.name = name;

            var renderer = instance.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = instance.AddComponent<SpriteRenderer>();

            if (renderer.sprite == null)
                renderer.sprite = Square();

            return instance;
        }
    }
}
