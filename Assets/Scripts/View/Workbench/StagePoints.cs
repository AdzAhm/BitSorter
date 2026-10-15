using BitSorter.ViewModel;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A point crossing between the view-model and Unity: a stage position handed to a
    /// <c>RectTransform</c> or a label, or a pointer or a window's size handed to the view-model.
    /// </summary>
    /// <remarks>
    /// Two floats copied either way, nothing more: <see cref="Vec2"/> does Unity's arithmetic, so a
    /// point is the same number on both sides of the line. Written out where it crosses rather than
    /// converted implicitly, because the view-model knows nothing of Unity and cannot declare the
    /// conversion, and because the crossings are worth seeing -- they are where the view-model ends.
    /// </remarks>
    public static class StagePoints
    {
        public static Vector2 ToUnity(this Vec2 point) => new Vector2(point.x, point.y);

        public static Vec2 ToStage(this Vector2 point) => new Vec2(point.x, point.y);
    }
}
