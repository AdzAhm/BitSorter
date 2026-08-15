namespace BitSorter.LogicCore
{
    /// <summary>
    /// A single bit value. Input ports hold a <c>Bit?</c>, where <c>null</c> means empty --
    /// so "0, 1, or nothing" is expressed exactly, with no invalid state to validate against.
    /// </summary>
    public enum Bit : byte
    {
        Zero = 0,
        One = 1,
    }
}
