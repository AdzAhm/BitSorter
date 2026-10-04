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
    ///
    /// The list is kept in the order the holders were opened, last at the end, because Escape
    /// closes the last thing opened, as undo undoes the last thing done (<see cref="TryTake"/>).
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

        /// <summary>
        /// Says a holder has just come up over the board, which makes it the first that Escape
        /// closes. Called where each one opens, not where it joins.
        /// </summary>
        /// <remarks>A holder that has not joined is left off: joining is what makes it one.</remarks>
        public static void Opened(IHoldsEscape holder)
        {
            int at = Holders.IndexOf(holder);

            if (at < 0 || at == Holders.Count - 1)
                return;

            Holders.RemoveAt(at);
            Holders.Add(holder);
        }

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

        /// <summary>The frame whose Escape a holder has taken, or -1.</summary>
        private static int _takenOnFrame = -1;

        /// <summary>
        /// Takes this frame's Escape for the holder about to close on it, if it is the last thing
        /// opened that still holds the press, and if nothing has taken it already. One press closes
        /// one thing, and it is the thing on top.
        /// </summary>
        /// <remarks>
        /// Each holder used to close itself on Escape independently, so with the help panel open
        /// over the solved card, or a hint up over either, one press took them all. Then the first
        /// to ask took it, and which one asked first was up to the order Unity updates them in,
        /// which it leaves to chance.
        ///
        /// Now a holder is refused while anything opened after it still holds the press. Every
        /// holder works that out the same way in any order: asked before the one on top has closed,
        /// that one still holds Escape; asked after, it still holds it for the rest of the frame. The
        /// rest stay up, still holding Escape, for the next press.
        ///
        /// Asked last, after a holder has decided it would close, so a holder that would not close
        /// takes nothing. Derived from the list and the frame number, so it cannot leak, and it
        /// allocates nothing.
        /// </remarks>
        public static bool TryTake(IHoldsEscape taker)
        {
            int frame = UnityEngine.Time.frameCount;

            if (_takenOnFrame == frame)
                return false;

            for (int i = Holders.Count - 1; i >= 0; i--)
            {
                IHoldsEscape holder = Holders[i];

                if (ReferenceEquals(holder, taker))
                    break;

                if (holder is UnityEngine.Object unity && unity == null)
                    continue;

                if (holder.HoldsEscapeNow)
                    return false;
            }

            _takenOnFrame = frame;
            return true;
        }
    }
}
