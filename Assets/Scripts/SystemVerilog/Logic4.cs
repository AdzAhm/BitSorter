namespace BitSorter.SystemVerilog
{
    /// <summary>One bit of a SystemVerilog 4-state value.</summary>
    /// <remarks>
    /// The numeric value is the bit's two planes, as the simulators' own interface (VPI) stores
    /// them: bit 0 is the a-plane, bit 1 the b-plane. 0 is (0, 0), 1 is (1, 0), z is (0, 1) and x is
    /// (1, 1) -- so "is this bit known" is "is its b-plane bit clear", one test however many bits a
    /// <see cref="LogicVec"/> holds.
    /// </remarks>
    public enum Logic4 : byte
    {
        /// <summary>A known 0.</summary>
        Zero = 0,

        /// <summary>A known 1.</summary>
        One = 1,

        /// <summary>Nothing is driving it: high impedance.</summary>
        Z = 2,

        /// <summary>Unknown: it could be 0 or 1, or two drivers disagree.</summary>
        X = 3,
    }

    /// <summary>Converting a <see cref="Logic4"/> to and from the characters SystemVerilog writes it with.</summary>
    public static class Logic4Text
    {
        /// <summary>'0', '1', 'z' or 'x', as a binary literal or <c>$display("%b")</c> writes the bit.</summary>
        public static char ToChar(this Logic4 bit)
        {
            switch (bit)
            {
                case Logic4.Zero: return '0';
                case Logic4.One: return '1';
                case Logic4.Z: return 'z';
                default: return 'x';
            }
        }

        /// <summary>
        /// Reads one binary digit: 0, 1, x or X, z or Z, or '?', which a literal may write for z.
        /// </summary>
        public static bool TryParse(char c, out Logic4 bit)
        {
            switch (c)
            {
                case '0': bit = Logic4.Zero; return true;
                case '1': bit = Logic4.One; return true;
                case 'x': case 'X': bit = Logic4.X; return true;
                case 'z': case 'Z': case '?': bit = Logic4.Z; return true;
                default: bit = Logic4.X; return false;
            }
        }

        /// <summary>Whether the bit is a known 0 or 1.</summary>
        public static bool IsKnown(this Logic4 bit) => bit == Logic4.Zero || bit == Logic4.One;
    }
}
