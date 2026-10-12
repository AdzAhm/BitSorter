namespace BitSorter.SystemVerilog
{
    /// <summary>How a value is written out: the radixes ModelSim's waveform offers, and octal for literals.</summary>
    /// <remarks>
    /// Display, not a lesson in number representation (decision D11): a signal reads in binary, hex,
    /// or unsigned or signed decimal, as a student would set it in ModelSim's Radix menu.
    /// </remarks>
    public enum Radix
    {
        Binary,
        Octal,
        Hex,

        /// <summary>The bits read as an unsigned number, which the course's handouts ask for.</summary>
        Unsigned,

        /// <summary>The bits read as a two's complement number.</summary>
        Signed,
    }
}
