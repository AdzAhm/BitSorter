using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BitSorter.Board
{
    /// <summary>One file of a zip: its path inside it, with <c>/</c> between folders, and its bytes.</summary>
    public readonly struct ZipEntry
    {
        public ZipEntry(string path, byte[] bytes)
        {
            Path = path;
            Bytes = bytes;
        }

        public string Path { get; }
        public byte[] Bytes { get; }
    }

    /// <summary>
    /// A zip of a few small files (Part 13a), written in memory: each file <em>stored</em>, not compressed, so nothing
    /// here needs a deflater -- which a browser build has none of to rely on -- and every unzip tool reads it. Names are
    /// UTF-8 (the zip format's flag 11). The same files and moment always make the same bytes.
    /// </summary>
    public static class ZipWriter
    {
        private static readonly uint[] Table = MakeTable();

        /// <summary>The zip of <paramref name="files"/>, each dated <paramref name="when"/> (to the two seconds a zip keeps).</summary>
        public static byte[] Write(IReadOnlyList<ZipEntry> files, DateTime when)
        {
            ushort time = (ushort)((when.Hour << 11) | (when.Minute << 5) | (when.Second / 2));
            ushort date = (ushort)(((Math.Max(1980, when.Year) - 1980) << 9) | (when.Month << 5) | when.Day);
            const ushort Version = 20, Utf8Names = 0x0800, Stored = 0;

            using var zip = new MemoryStream();
            using var central = new MemoryStream();
            var output = new BinaryWriter(zip);
            var directory = new BinaryWriter(central);

            foreach (ZipEntry file in files)
            {
                byte[] name = Encoding.UTF8.GetBytes(file.Path);
                byte[] bytes = file.Bytes ?? Array.Empty<byte>();
                uint crc = Crc32(bytes);
                uint offset = (uint)zip.Position;

                output.Write(0x04034b50u);
                output.Write(Version);
                output.Write(Utf8Names);
                output.Write(Stored);
                output.Write(time);
                output.Write(date);
                output.Write(crc);
                output.Write((uint)bytes.Length);
                output.Write((uint)bytes.Length);
                output.Write((ushort)name.Length);
                output.Write((ushort)0);
                output.Write(name);
                output.Write(bytes);

                directory.Write(0x02014b50u);
                directory.Write(Version);   // made by: MS-DOS, version 2.0
                directory.Write(Version);
                directory.Write(Utf8Names);
                directory.Write(Stored);
                directory.Write(time);
                directory.Write(date);
                directory.Write(crc);
                directory.Write((uint)bytes.Length);
                directory.Write((uint)bytes.Length);
                directory.Write((ushort)name.Length);
                directory.Write((ushort)0);   // extra
                directory.Write((ushort)0);   // comment
                directory.Write((ushort)0);   // disk
                directory.Write((ushort)0);   // internal attributes
                directory.Write(0u);          // external attributes
                directory.Write(offset);
                directory.Write(name);
            }

            directory.Flush();
            uint centralOffset = (uint)zip.Position;
            output.Write(central.ToArray());

            output.Write(0x06054b50u);
            output.Write((ushort)0);
            output.Write((ushort)0);
            output.Write((ushort)files.Count);
            output.Write((ushort)files.Count);
            output.Write((uint)central.Length);
            output.Write(centralOffset);
            output.Write((ushort)0);
            output.Flush();
            return zip.ToArray();
        }

        /// <summary>The CRC-32 a zip keeps of each file: the IEEE polynomial, reflected.</summary>
        public static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] MakeTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }

            return table;
        }
    }
}
