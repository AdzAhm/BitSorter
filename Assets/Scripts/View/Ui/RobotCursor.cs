using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Replaces the system cursor with a small robot hand, drawn at runtime.
    /// </summary>
    /// <remarks>
    /// Generated rather than imported, like every other sprite in the game. It is a stylised
    /// two-finger claw with a lit knuckle joint -- readable at 32 pixels, which is the real
    /// constraint on a cursor -- rather than an attempt at a rendered 3D hand. A painted asset would
    /// be the first imported art in the project and would have to come from somewhere.
    ///
    /// The hotspot is the fingertip, not the centre. A cursor whose click point is not where it
    /// visibly points makes every port on the board feel slightly off.
    /// </remarks>
    public sealed class RobotCursor : MonoBehaviour
    {
        private const int Size = 32;

        [Tooltip("Turn off to get the ordinary system cursor back.")]
        [SerializeField] private bool _enabled = true;

        [SerializeField] private Color _metal = new Color(0.72f, 0.76f, 0.82f);
        [SerializeField] private Color _shadow = new Color(0.28f, 0.31f, 0.36f);
        [SerializeField] private Color _joint = new Color(0.46f, 0.94f, 0.90f);

        private static Texture2D _texture;

        private void Start()
        {
            if (!_enabled)
                return;

            if (_texture == null)
                _texture = Build();

            // Auto, not ForceSoftware: the hardware cursor is drawn by the OS and so never lags the
            // pointer, which matters on a board where a click has to land on a specific port.
            Cursor.SetCursor(_texture, new Vector2(6f, 2f), CursorMode.Auto);
        }

        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private Texture2D Build()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Texture rows run bottom-up; flip so the shape is written the way it reads.
                    var p = new Vector2(x, Size - 1 - y);
                    pixels[y * Size + x] = Shade(p);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        /// <summary>One pixel of the claw.</summary>
        private Color32 Shade(Vector2 p)
        {
            // Pointing finger: a tapered bar from the wrist up to the tip at the top left.
            bool finger = InCapsule(p, new Vector2(7f, 3f), new Vector2(12f, 15f), 3.1f);

            // Thumb, shorter and angled off to the right, which is what makes it read as a hand
            // rather than as an arrow.
            bool thumb = InCapsule(p, new Vector2(13f, 14f), new Vector2(20f, 19f), 2.6f);

            // Palm block behind both.
            bool palm = InCapsule(p, new Vector2(12f, 17f), new Vector2(16f, 24f), 5.2f);

            bool body = finger || thumb || palm;

            if (!body)
                return new Color32(0, 0, 0, 0);

            // A lit joint where the fingers meet the palm, in the same teal the interface uses for
            // its accent, so the cursor belongs to this game rather than to any game.
            if (Vector2.Distance(p, new Vector2(13.5f, 17f)) < 2.6f)
                return _joint;

            // Edge darkening, so the claw stays legible over the bright green sources and the glow.
            bool edge =
                !InCapsule(p, new Vector2(7f, 3f), new Vector2(12f, 15f), 2.2f) &&
                !InCapsule(p, new Vector2(13f, 14f), new Vector2(20f, 19f), 1.7f) &&
                !InCapsule(p, new Vector2(12f, 17f), new Vector2(16f, 24f), 4.3f);

            return edge ? _shadow : _metal;
        }

        /// <summary>Distance from a point to a segment, thickened -- a rounded bar.</summary>
        private static bool InCapsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;

            float t = lengthSquared <= 1e-5f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);

            return Vector2.Distance(p, a + ab * t) <= radius;
        }
    }
}
