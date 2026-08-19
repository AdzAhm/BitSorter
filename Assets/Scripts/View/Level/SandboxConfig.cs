using System;
using System.Text;

namespace BitSorter.View
{
    /// <summary>
    /// The part of free play the player configures: how many sources, what each emits, and how many
    /// sinks.
    /// </summary>
    /// <remarks>
    /// Mutable, unlike everything else in the level layer, and deliberately so. A
    /// <see cref="LevelDefinition"/> is immutable because a half-built level is the thing the format's
    /// two-layer split exists to make unrepresentable -- so free play does not mutate one. It edits
    /// this instead and rebuilds a whole new definition through <see cref="SandboxLevel.Build"/>.
    ///
    /// Plain fields and a string[] because this is stored inside <see cref="SavedBoard"/> and goes
    /// through JsonUtility, which needs serialisable fields rather than properties or generic lists.
    ///
    /// Streams are strings of '0' and '1' rather than Bit[], matching how a level file writes them.
    /// The player types this shape, the save file stores it, and <see cref="SandboxLevel"/> is the one
    /// place it becomes bits -- so there is a single conversion rather than one per layer.
    /// </remarks>
    [Serializable]
    public sealed class SandboxConfig
    {
        /// <summary>
        /// Test vectors every source emits. Capped because each vector is a tick of run time and a
        /// column of the sink readout, and a stream nobody can read is not a longer experiment.
        /// </summary>
        public const int MinVectors = 1;

        /// <inheritdoc cref="MinVectors"/>
        public const int MaxVectors = 8;

        /// <summary>One character per vector, '0' or '1'. One entry per source.</summary>
        public string[] sources;

        public int sinks;

        /// <summary>
        /// Held rather than derived from the first stream, so the count survives a config with no
        /// sources and stays the single thing every stream is normalised against.
        /// </summary>
        public int vectors;

        /// <summary>
        /// Brings the config back inside its own rules: counts clamped, and every stream exactly
        /// <see cref="vectors"/> characters of '0' or '1'.
        /// </summary>
        /// <remarks>
        /// Called before every build and after every load, because both are places a config can
        /// arrive malformed -- an edited save, or a board saved by a build with different limits.
        /// Padding with '0' rather than refusing: free play has nothing to be wrong about, and
        /// silently shortening a stream is friendlier than an error the player cannot act on.
        /// </remarks>
        public void Normalise(int maxSources, int maxSinks)
        {
            vectors = Clamp(vectors <= 0 ? SandboxLevel.DefaultVectors : vectors, MinVectors, MaxVectors);
            sinks = Clamp(sinks, 0, maxSinks);

            if (sources == null)
                sources = Array.Empty<string>();

            if (sources.Length > maxSources)
                Array.Resize(ref sources, maxSources);

            for (int i = 0; i < sources.Length; i++)
                sources[i] = NormaliseStream(sources[i], vectors);
        }

        /// <summary>A stream of exactly <paramref name="length"/> characters, padded with '0'.</summary>
        public static string NormaliseStream(string stream, int length)
        {
            var text = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                char c = stream != null && i < stream.Length ? stream[i] : '0';
                text.Append(c == '1' ? '1' : '0');
            }

            return text.ToString();
        }

        /// <summary>A copy, so a config can be edited without disturbing the one already built.</summary>
        public SandboxConfig Clone()
        {
            var copy = new SandboxConfig { sinks = sinks, vectors = vectors };

            if (sources != null)
            {
                copy.sources = new string[sources.Length];
                Array.Copy(sources, copy.sources, sources.Length);
            }

            return copy;
        }

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;
    }
}
