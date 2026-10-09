using System.Buffers.Binary;

namespace LoupixDeck.Plugin.Wacom.Driver;

/// <summary>
/// A request to the Wacom driver's message port: little-endian, a fixed 0x94-byte header and an
/// optional data section. Matches what Wacom Tablet 6.4.14 accepts.
/// </summary>
internal static class EventPacket
{
    /// <summary>msgid for an event packet ("WEPK"). 0 is the legacy TransitRecord path.</summary>
    public const int MsgId = 0x5745504B;

    private const uint Magic = 0x5754534D; // "MSTW"
    private const int HeaderSize = 0x94;
    private const int KeysOffset = 0x12;
    private const int KeySize = 10;
    private const int MaxKeys = 10;
    private const int DataLengthOffset = 0x80;
    private const int FixupCountOffset = 0x8C;
    private const int FixupSize = 16;
    private const uint ByString = 6;
    private const uint StringKeyFixup = 0;
    private const uint DataFixup = 1;

    public enum Command : uint
    {
        Get = 1,
        Set = 2
    }

    /// <summary>Object and parameter identifiers used by this plugin.</summary>
    public static class Id
    {
        public const ushort Driver = 0x01;
        public const ushort Tablet = 0x02;
        public const ushort ContextManager = 0x0c;
        public const ushort DisplayToggleObject = 0x1a;
        public const ushort DisplayToggleParameter = 0x10b;
        public const ushort DisplayToggleTarget = 0x311;
        public const ushort TabletName = 0xbc;
        public const ushort TabletConnected = 0xba;
        public const ushort TabletHasPen = 0xb9;
        public const ushort FinePoint = 0x119;
    }

    /// <summary>Reply status: OK, a router found no matching child, a bad addressing mode.</summary>
    public const ushort StatusOk = 0x0000;

    /// <summary>
    /// One step of an object path, addressed by index (mode 0: reference 0 picks the first, or for
    /// the context manager the active, object) or, when <see cref="Name"/> is set, by that string.
    /// </summary>
    public readonly record struct Key(ushort Identifier, uint Mode = 0, uint Reference = 0, string? Name = null);

    /// <summary>A packet with no data, as used for a Get.</summary>
    public static byte[] Build(Command command, IReadOnlyList<Key> keys) => Build(command, keys, []);

    /// <summary>A packet carrying one bool (one data byte).</summary>
    public static byte[] Build(Command command, IReadOnlyList<Key> keys, bool value) =>
        Build(command, keys, [value ? (byte)1 : (byte)0]);

    /// <summary>A packet carrying one u16.</summary>
    public static byte[] Build(Command command, IReadOnlyList<Key> keys, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return Build(command, keys, bytes);
    }

    /// <summary>
    /// Header, then a fixup table at 0x94 (16 bytes each: kind, index, length, absolute offset)
    /// with one entry per by-string key (kind 0, index = the key's position) and one for the data
    /// (kind 1), then the NUL-terminated strings and the data bytes in that order. 0x80 holds the
    /// data length and 0x8C the number of fixups.
    /// </summary>
    private static byte[] Build(Command command, IReadOnlyList<Key> keys, ReadOnlySpan<byte> data)
    {
        if (keys.Count is 0 or > MaxKeys) throw new ArgumentOutOfRangeException(nameof(keys));

        var strings = keys
            .Select((key, index) => (index, bytes: key.Name is null ? null : System.Text.Encoding.UTF8.GetBytes(key.Name + "\0")))
            .Where(s => s.bytes is not null)
            .ToList();
        var fixups = strings.Count + (data.IsEmpty ? 0 : 1);
        var payloadOffset = HeaderSize + fixups * FixupSize;
        var dataOffset = payloadOffset + strings.Sum(s => s.bytes!.Length);
        var buffer = new byte[dataOffset + data.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x00..], Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x04..], 1); // version
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x08..], (uint)buffer.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0E..], (uint)command);

        for (var i = 0; i < keys.Count; i++)
        {
            var key = span[(KeysOffset + i * KeySize)..];
            BinaryPrimitives.WriteUInt32LittleEndian(key, keys[i].Name is null ? keys[i].Mode : ByString);
            BinaryPrimitives.WriteUInt16LittleEndian(key[4..], keys[i].Identifier);
            BinaryPrimitives.WriteUInt32LittleEndian(key[6..], keys[i].Reference);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span[DataLengthOffset..], (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[FixupCountOffset..], (uint)fixups);

        var fixup = HeaderSize;
        var offset = payloadOffset;
        foreach (var (index, bytes) in strings)
        {
            WriteFixup(span[fixup..], StringKeyFixup, (uint)index, bytes!.Length, offset);
            bytes.CopyTo(span[offset..]);
            fixup += FixupSize;
            offset += bytes.Length;
        }

        if (!data.IsEmpty)
        {
            WriteFixup(span[fixup..], DataFixup, 0, data.Length, dataOffset);
            data.CopyTo(span[dataOffset..]);
        }

        return buffer;
    }

    private static void WriteFixup(Span<byte> entry, uint kind, uint index, int length, int offset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(entry, kind);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], index);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
    }

    /// <summary>The status the driver wrote into its reply, or null when the reply isn't a packet.</summary>
    public static ushort? ReplyStatus(ReadOnlySpan<byte> reply) =>
        reply.Length >= HeaderSize && BinaryPrimitives.ReadUInt32LittleEndian(reply) == Magic
            ? BinaryPrimitives.ReadUInt16LittleEndian(reply[0x0C..])
            : null;

    /// <summary>
    /// True when a handler answered with data. The driver echoes an unrouted packet back with
    /// status 0 and no data, so for a Get only a data section proves the packet was handled.
    /// </summary>
    public static bool HasData(ReadOnlySpan<byte> reply) =>
        ReplyStatus(reply) == StatusOk && reply.Length > HeaderSize;

    /// <summary>The first byte of a reply's first data item, or null when there is none.</summary>
    public static byte? ReadByte(ReadOnlySpan<byte> reply) =>
        Item(reply) is { Length: > 0 } bytes ? bytes[0] : null;

    /// <summary>The first data item of a reply as text (NUL-terminated), or null when there is none.</summary>
    public static string? ReadString(ReadOnlySpan<byte> reply)
    {
        if (Item(reply) is not { } bytes) return null;
        var end = Array.IndexOf(bytes, (byte)0);
        return System.Text.Encoding.UTF8.GetString(bytes, 0, end >= 0 ? end : bytes.Length);
    }

    private static byte[]? Item(ReadOnlySpan<byte> reply)
    {
        if (!HasData(reply) || reply.Length < HeaderSize + FixupSize) return null;
        var record = reply[HeaderSize..];
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        if (offset < 0 || length < 0 || offset + length > reply.Length) return null;
        return reply.Slice(offset, length).ToArray();
    }
}
