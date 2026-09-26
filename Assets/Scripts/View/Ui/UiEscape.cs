using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>
    /// Something drawn over the board -- not a full-screen panel -- that closes on Escape.
    /// </summary>
    public interface IHoldsEscape
    {
        /// <summary>
        /// Whether this frame's Escape is this thing's: true while a press would close it, and still
        /// true for the rest of the frame once Escape has.
        /// </summary>
        /// <remarks>
        /// Both halves, so the answer is the same whichever of this and the main menu Unity updates
        /// first: asked before it closes, it is still open; asked after, it closed this frame.
        /// </remarks>
        bool HoldsEscapeNow { get; }
    }

    /// <summary>
    /// The things over the board that take Escape before the main menu does.
    /// </summary>
    /// <remarks>
    /// Escape closes whatever is on top, and only with nothing on top does it open the main menu. A
    /// full-screen panel says it is on top through <see cref="UiModal"/>. Three things are on top
    /// without being modal -- the solved card, a first-time hint, the help panel -- and each was
    /// found the same way: one Escape closed it and opened the menu as well. The menu then asked
    /// each by name, which held until a fourth thing would have been forgotten. Now each joins this
    /// list and the menu asks the list; <c>UiEscapeTests</c> refuses a script that reads Escape
    /// without being a full-screen panel or joining.
    ///
    /// Joined in Awake and left in OnDestroy rather than on enable and disable: a test that drives
    /// an Update by hand disables the component, and the thing it draws has not gone anywhere.
    /// </remarks>
    public static class UiEscape
    {
        private static readonly List<IHoldsEscape> Holders = new List<IHoldsEscape>();

        public static void Join(IHoldsEscape holder)
        {
            if (holder != null && !Holders.Contains(holder))
                Holders.Add(holder);
        }

        public static void Leave(IHoldsEscape holder) => Holders.Remove(holder);

        /// <summary>Whether anything over the board holds this frame's Escape.</summary>
        /// <remarks>
        /// A holder destroyed without leaving -- a scene unloaded around it -- is skipped rather than
        /// asked: a destroyed Unity object still answers C# calls, with whatever it last knew.
        /// </remarks>
        public static bool AnyHolds
        {
            get
            {
                for (int i = 0; i < Holders.Count; i++)
                {
                    IHoldsEscape holder = Holders[i];

                    if (holder is UnityEngine.Object unity && unity == null)
                        continue;

                    if (holder.HoldsEscapeNow)
                        return true;
                }

                return false;
            }
        }

        /// <summary>How many things have joined, for the tests.</summary>
        public static int Count => Holders.Count;
    }
}
