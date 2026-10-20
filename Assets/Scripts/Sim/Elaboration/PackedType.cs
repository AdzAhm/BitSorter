using System;
using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>
    /// What the bits of a packed value are (Part L4): a vector of bits, a packed array of elements --
    /// <c>logic [3:0][7:0]</c> is four bytes -- or a struct or a union of members. Every packed value is
    /// one vector in the engine; this is the elaborator's map of it, so a select or a member access finds
    /// its bits, and the type of what it found.
    /// </summary>
    /// <remarks>
    /// The outermost dimension is <see cref="Msb"/> to <see cref="Lsb"/>, as declared; one step of it
    /// holds an <see cref="Element"/>, or a single bit when that is null. A struct's bits are its members,
    /// the first at the top (IEEE 1800, 7.2.1); a union's members all start at bit 0 (7.3.1). A struct or a
    /// union reads as a vector of bits too: <c>s[3]</c> is a bit.
    /// </remarks>
    internal sealed class PackedType
    {
        public PackedType(int msb, int lsb, bool signed, PackedType element = null, StructLayout layout = null, EnumInfo @enum = null, bool twoState = false, bool isString = false, bool isReal = false)
        {
            Msb = msb;
            Lsb = lsb;
            Signed = signed;
            Element = element;
            Struct = layout;
            Enum = @enum;
            TwoState = twoState;
            IsString = isString;
            IsReal = isReal;
        }

        /// <summary>A <c>real</c> (Part L7; see <see cref="Reals"/>): 64 bits, an IEEE 754 double's.</summary>
        public static readonly PackedType Real = new PackedType(63, 0, false, null, null, null, twoState: true, isReal: true);

        /// <summary>Whether it is a <c>real</c>, whose bits are a double (Part L7).</summary>
        public bool IsReal { get; }

        /// <summary>The characters a <c>string</c> holds at most (Part L6; see <see cref="Strings"/>).</summary>
        public const int StringCharacters = 256;

        /// <summary>A <c>string</c>: <see cref="StringCharacters"/> bytes, 2-state, its characters at the bottom.</summary>
        public static readonly PackedType String = new PackedType(StringCharacters * 8 - 1, 0, false, null, null, null, twoState: true, isString: true);

        /// <summary>Whether it is a <c>string</c>, whose bits are characters (Part L6).</summary>
        public bool IsString { get; }

        /// <summary>The outermost dimension, as declared: <c>[3:0]</c> is 3 and 0.</summary>
        public int Msb { get; }

        /// <inheritdoc cref="Msb"/>
        public int Lsb { get; }

        /// <summary>How many steps the outermost dimension has.</summary>
        public int Count => Math.Abs(Msb - Lsb) + 1;

        /// <summary>What one step of the outermost dimension holds; null for a bit.</summary>
        public PackedType Element { get; }

        public int ElementWidth => Element?.Width ?? 1;

        public int Width => Count * ElementWidth;

        public bool Signed { get; }
        public bool TwoState { get; }
        public EnumInfo Enum { get; }

        /// <summary>The members, when it is a struct or a union.</summary>
        public StructLayout Struct { get; }

        /// <summary>A plain vector of bits, <c>[msb:lsb]</c>.</summary>
        public static PackedType Bits(int msb, int lsb, bool signed = false, bool twoState = false, EnumInfo @enum = null) =>
            new PackedType(msb, lsb, signed, null, null, @enum, twoState);

        /// <summary>A single bit: what a bit-select gives, unsigned (IEEE 1800, 11.8.1).</summary>
        public static readonly PackedType Bit = Bits(0, 0);

        /// <summary>The bit position, from 0, of index <paramref name="index"/> of the outermost dimension's first bit.</summary>
        public long Position(long index) => (Msb >= Lsb ? index - Lsb : Lsb - index) * ElementWidth;

        /// <summary>This type with another signedness: what a part-select or an element of a signed array reads as.</summary>
        public PackedType WithSigned(bool signed) =>
            signed == Signed ? this : new PackedType(Msb, Lsb, signed, Element, Struct, Enum, TwoState, IsString, IsReal);

        public override string ToString() =>
            Struct != null ? (Struct.IsUnion ? "union" : "struct") + $" [{Width}]" : $"[{Msb}:{Lsb}]{(Element != null ? Element.ToString() : "")}";
    }

    /// <summary>A struct's or a union's members, each where its bits start.</summary>
    internal sealed class StructLayout
    {
        public StructLayout(bool isUnion, IReadOnlyList<StructField> fields)
        {
            IsUnion = isUnion;
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public bool IsUnion { get; }

        /// <summary>The members in declaration order: the first is the most significant of a struct.</summary>
        public IReadOnlyList<StructField> Fields { get; }

        public StructField Find(string name)
        {
            foreach (StructField field in Fields)
                if (field.Name == name)
                    return field;
            return null;
        }
    }

    internal sealed class StructField
    {
        public StructField(string name, int lowest, PackedType type)
        {
            Name = name;
            Lowest = lowest;
            Type = type;
        }

        public string Name { get; }

        /// <summary>Its lowest bit, counted from bit 0 of the struct.</summary>
        public int Lowest { get; }

        public PackedType Type { get; }
    }
}
