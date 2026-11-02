using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FitsMetadataForCr2 {

    public enum EmbedMethod {
        /// <summary>Written into the XMP space the camera reserved; no other byte of the file changed</summary>
        InPlace,
        /// <summary>XMP and a copy of the first directory (IFD0) appended at the end of the file</summary>
        Appended
    }

    /// <summary>
    /// Writes an XMP packet into a TIFF-based raw file (CR2, NEF, ARW, DNG, PEF) without moving any existing data.
    /// </summary>
    public static class TiffXmpWriter {
        private const ushort XmpTag = 700;
        private const ushort TypeByte = 1;
        private const int AppendPadding = 2048;

        public static bool IsTiffBased(string path) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[4];
            return fs.Read(header, 0, 4) == 4 && IsTiffHeader(header);
        }

        /// <summary>
        /// <paramref name="buildXmpMeta"/> gets the file's current XMP (null if none) and returns the new x:xmpmeta.
        /// The new XMP replaces the old one in place when it fits into the space reserved for it (Canon reserves 8 KB);
        /// otherwise it is appended, and the header is switched to the new IFD0 last, so an interrupted write leaves the
        /// original file readable.
        /// </summary>
        public static EmbedMethod Embed(string path, Func<string, string> buildXmpMeta, bool allowInPlace = true) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var header = Read(fs, 0, 8);
            if (!IsTiffHeader(header)) throw new InvalidDataException("Not a TIFF-based raw file.");
            var bigEndian = header[0] == 'M';

            var ifd0 = U32(header, 4, bigEndian);
            var count = U16(Read(fs, ifd0, 2), 0, bigEndian);
            if (count == 0 || count > 1000 || ifd0 + 6 + count * 12L > fs.Length) throw new InvalidDataException("First image directory (IFD0) is invalid.");
            var entries = Enumerable.Range(0, count).Select(i => Read(fs, ifd0 + 2 + i * 12L, 12)).ToList();
            var nextIfd = Read(fs, ifd0 + 2 + count * 12L, 4);

            var xmpEntry = entries.FirstOrDefault(e => U16(e, 0, bigEndian) == XmpTag);
            string existing = null;
            uint xmpLength = 0, xmpOffset = 0;
            if (xmpEntry != null) {
                xmpLength = U32(xmpEntry, 4, bigEndian);
                xmpOffset = U32(xmpEntry, 8, bigEndian);
                if (xmpLength > 4 && xmpOffset + (long)xmpLength <= fs.Length) {
                    existing = Encoding.UTF8.GetString(Read(fs, xmpOffset, (int)xmpLength));
                }
            }

            var xmpMeta = buildXmpMeta(existing);

            if (allowInPlace && existing != null) {
                var packet = XmpPacket.ToPacket(xmpMeta, (int)xmpLength);
                if (packet != null) {
                    fs.Position = xmpOffset;
                    fs.Write(packet);
                    fs.Flush(true);
                    return EmbedMethod.InPlace;
                }
            }

            var newPacket = XmpPacket.ToPacket(xmpMeta, XmpPacket.MinimumLength(xmpMeta) + AppendPadding);
            var packetOffset = AlignEnd(fs);
            fs.Write(newPacket);
            var ifdOffset = AlignEnd(fs);
            if (ifdOffset + 6 + (count + 1) * 12L > uint.MaxValue) throw new InvalidDataException("File too large for TIFF offsets.");

            var newEntry = new byte[12];
            W16(newEntry, 0, XmpTag, bigEndian);
            W16(newEntry, 2, TypeByte, bigEndian);
            W32(newEntry, 4, (uint)newPacket.Length, bigEndian);
            W32(newEntry, 8, (uint)packetOffset, bigEndian);
            var newEntries = entries.Where(e => e != xmpEntry).Append(newEntry).OrderBy(e => U16(e, 0, bigEndian)).ToList();

            var ifd = new byte[2 + newEntries.Count * 12 + 4];
            W16(ifd, 0, (ushort)newEntries.Count, bigEndian);
            for (var i = 0; i < newEntries.Count; i++) newEntries[i].CopyTo(ifd, 2 + i * 12);
            nextIfd.CopyTo(ifd, ifd.Length - 4);
            fs.Write(ifd);
            fs.Flush(true);

            var pointer = new byte[4];
            W32(pointer, 0, (uint)ifdOffset, bigEndian);
            fs.Position = 4;
            fs.Write(pointer);
            fs.Flush(true);
            return EmbedMethod.Appended;
        }

        /// <summary>Reads the XMP packet from IFD0, or null.</summary>
        public static string ReadXmp(string path) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = Read(fs, 0, 8);
            if (!IsTiffHeader(header)) return null;
            var bigEndian = header[0] == 'M';
            var ifd0 = U32(header, 4, bigEndian);
            var count = U16(Read(fs, ifd0, 2), 0, bigEndian);
            for (var i = 0; i < count; i++) {
                var e = Read(fs, ifd0 + 2 + i * 12L, 12);
                if (U16(e, 0, bigEndian) == XmpTag) {
                    return Encoding.UTF8.GetString(Read(fs, U32(e, 8, bigEndian), (int)U32(e, 4, bigEndian)));
                }
            }
            return null;
        }

        private static bool IsTiffHeader(byte[] h) =>
            (h[0] == 'I' && h[1] == 'I' && h[2] == 42 && h[3] == 0) || (h[0] == 'M' && h[1] == 'M' && h[2] == 0 && h[3] == 42);

        // Moves to the end of the file, padded to a 4-byte boundary (TIFF offsets must be word aligned)
        private static long AlignEnd(FileStream fs) {
            var end = fs.Length;
            fs.Position = end;
            var pad = (int)((4 - end % 4) % 4);
            if (pad > 0) fs.Write(new byte[pad]);
            return end + pad;
        }

        private static byte[] Read(FileStream fs, long offset, int length) {
            var buffer = new byte[length];
            fs.Position = offset;
            fs.ReadExactly(buffer);
            return buffer;
        }

        private static ushort U16(byte[] b, int i, bool be) =>
            be ? BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i)) : BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i));

        private static uint U32(byte[] b, int i, bool be) =>
            be ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i)) : BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i));

        private static void W16(byte[] b, int i, ushort v, bool be) {
            if (be) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(i), v); else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i), v);
        }

        private static void W32(byte[] b, int i, uint v, bool be) {
            if (be) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(i), v); else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i), v);
        }
    }
}
