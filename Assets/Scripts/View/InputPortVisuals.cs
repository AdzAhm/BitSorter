using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Pure visual rules for occupied input ports.
    /// </summary>
    public static class InputPortVisuals
    {
        public static bool IsWaiting(InputPort port) =>
            port != null && port.Pending.HasValue && !port.Owner.IsReadyToEvaluate;

        public static float Pulse01(float time, float cycleSeconds)
        {
            if (cycleSeconds <= 0f)
                return 1f;

            float phase = Mathf.Repeat(time, cycleSeconds) / cycleSeconds;
            return 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
        }

        public static Color WaitingColour(Bit pending, Color zero, Color one, float pulse)
        {
            Color baseColor = pending == Bit.One ? one : zero;
            return Color.Lerp(baseColor, Color.white, 0.2f * pulse);
        }
    }
}
