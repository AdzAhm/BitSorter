using System;

namespace BitSorter.Board
{
    /// <summary>A pin of one part, by the part's id and the pin's name.</summary>
    public readonly struct PinRef : IEquatable<PinRef>
    {
        public PinRef(int part, string pin)
        {
            Part = part;
            Pin = pin;
        }

        public readonly int Part;
        public readonly string Pin;

        public bool Equals(PinRef other) => Part == other.Part && string.Equals(Pin, other.Pin, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is PinRef other && Equals(other);
        public override int GetHashCode() => Part * 397 ^ (Pin == null ? 0 : StringComparer.Ordinal.GetHashCode(Pin));
        public override string ToString() => $"{Part}.{Pin}";
    }

    /// <summary>
    /// Where one end of a wire is: on a pin, or on another wire -- a tap, drawn as a junction dot, which
    /// joins whatever that wire is joined to.
    /// </summary>
    public readonly struct Endpoint : IEquatable<Endpoint>
    {
        private Endpoint(PinRef pin, int tapOf)
        {
            Pin = pin;
            TapOf = tapOf;
        }

        public static Endpoint At(int part, string pin) => new Endpoint(new PinRef(part, pin), -1);
        public static Endpoint At(PinRef pin) => new Endpoint(pin, -1);
        public static Endpoint OnWire(int wire) => new Endpoint(default, wire);

        /// <summary>The pin, when this end is on one.</summary>
        public readonly PinRef Pin;

        /// <summary>The wire this end taps, or -1 when it is on a pin.</summary>
        public readonly int TapOf;

        public bool IsTap => TapOf >= 0;

        public bool Equals(Endpoint other) => TapOf == other.TapOf && (IsTap || Pin.Equals(other.Pin));
        public override bool Equals(object obj) => obj is Endpoint other && Equals(other);
        public override int GetHashCode() => IsTap ? ~TapOf : Pin.GetHashCode();
        public override string ToString() => IsTap ? $"tap on wire {TapOf}" : Pin.ToString();
    }

    /// <summary>
    /// A wire between two ends. Wires are drawn; nets are worked out from them (<see cref="Netlist"/>).
    /// A wire may carry the name its net should have.
    /// </summary>
    public sealed record Wire
    {
        public int Id { get; init; }
        public Endpoint From { get; init; }
        public Endpoint To { get; init; }

        /// <summary>The name the player gave this wire's net, or null.</summary>
        public string NetName { get; init; }

        public bool Touches(int part) =>
            (!From.IsTap && From.Pin.Part == part) || (!To.IsTap && To.Pin.Part == part);
    }
}
