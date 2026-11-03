using System.Buffers.Binary;
using System.Text;

namespace AdamE.Firestore.Peerless;

/// <summary>
/// Decodes one Peerless frame (see <c>docs/wire-format.md</c>) into plain .NET values. The decoder is stateless and
/// thread-safe, and the result does not reference the input buffer, so the caller may reuse or free it afterwards.
/// </summary>
public static class SnapshotDecoder
{
    /// <summary>Decodes a query or document snapshot frame.</summary>
    /// <exception cref="PeerlessFormatException">The frame is malformed or uses an unsupported version.</exception>
    public static FirestoreSnapshot Decode(ReadOnlySpan<byte> frame)
    {
        var reader = new FrameReader(frame);
        return reader.ReadFrame();
    }

    /// <summary>Decodes a query snapshot frame.</summary>
    /// <exception cref="PeerlessFormatException">The frame is malformed or is not a query snapshot.</exception>
    public static FirestoreQuerySnapshot DecodeQuery(ReadOnlySpan<byte> frame) =>
        Decode(frame) as FirestoreQuerySnapshot
        ?? throw new PeerlessFormatException("The frame is not a query snapshot.", Wire.HeaderLength - 2);

    /// <summary>Decodes a document snapshot frame.</summary>
    /// <exception cref="PeerlessFormatException">The frame is malformed or is not a document snapshot.</exception>
    public static FirestoreDocumentSnapshot DecodeDocument(ReadOnlySpan<byte> frame) =>
        Decode(frame) as FirestoreDocumentSnapshot
        ?? throw new PeerlessFormatException("The frame is not a document snapshot.", Wire.HeaderLength - 2);

    private ref struct FrameReader(ReadOnlySpan<byte> frame)
    {
        private readonly ReadOnlySpan<byte> frame = frame;
        private int position;
        private KeyCache keys = new();

        public FirestoreSnapshot ReadFrame()
        {
            if (frame.Length < Wire.HeaderLength || !frame[..4].SequenceEqual(Wire.Magic))
                throw Fail("The frame does not start with the Peerless header.");
            if (frame[4] != Wire.MajorVersion)
                throw Fail($"Wire format major version {frame[4]} is not supported (expected {Wire.MajorVersion}).", 4);

            byte kind = frame[6];
            if (kind is not (Wire.FrameKind.Query or Wire.FrameKind.Document))
                throw Fail($"Frame kind {kind} is not supported.", 6);

            position = Wire.HeaderLength;
            SnapshotMetadata? metadata = null;
            FirestoreDocument[]? documents = null;
            while (position < frame.Length)
            {
                int sectionStart = position;
                byte id = ReadByte();
                uint declared = ReadUInt32();
                if (declared > (uint)(frame.Length - position))
                    throw Fail("A section runs past the end of the frame.", sectionStart);

                int length = (int)declared;

                int end = position + length;
                switch (id)
                {
                    case Wire.Section.Metadata when metadata is null:
                        metadata = ReadMetadata();
                        break;
                    case Wire.Section.Documents when documents is null:
                        documents = ReadDocuments(kind);
                        break;
                    case Wire.Section.Metadata or Wire.Section.Documents:
                        throw Fail($"Section {id} appears twice.", sectionStart);
                    default:
                        position = end; // A section from a newer minor version.
                        break;
                }

                if (position != end)
                    throw Fail($"Section {id} declared {length} bytes but its content used {position - (end - length)}.", sectionStart);
            }

            if (metadata is not { } snapshotMetadata)
                throw Fail("The frame has no metadata section.");
            if (documents is null)
                throw Fail("The frame has no documents section.");

            if (kind == Wire.FrameKind.Query)
                return new FirestoreQuerySnapshot(snapshotMetadata, Array.AsReadOnly(documents));
            if (documents.Length != 1)
                throw Fail($"A document snapshot frame must hold exactly one document, not {documents.Length}.");
            return new FirestoreDocumentSnapshot(snapshotMetadata, documents[0]);
        }

        private SnapshotMetadata ReadMetadata()
        {
            byte flags = ReadByte();
            return new SnapshotMetadata(
                (flags & Wire.MetadataFlags.FromCache) != 0,
                (flags & Wire.MetadataFlags.HasPendingWrites) != 0);
        }

        private FirestoreDocument[] ReadDocuments(byte kind)
        {
            int count = ReadCount(minimumBytesEach: 2);
            var documents = new FirestoreDocument[count];
            for (int index = 0; index < count; index++)
            {
                int start = position;
                string path = ReadString();
                if (path.Length == 0)
                    throw Fail("A document has an empty path.", start);

                byte flags = ReadByte();
                bool exists = (flags & Wire.DocumentFlags.Exists) != 0;
                if (!exists && kind == Wire.FrameKind.Query)
                    throw Fail("A query snapshot holds a document that does not exist.", start);

                var metadata = new SnapshotMetadata(
                    (flags & Wire.DocumentFlags.FromCache) != 0,
                    (flags & Wire.DocumentFlags.HasPendingWrites) != 0);
                FirestoreMap fields = exists ? ReadMapBody(depth: 1) : FirestoreMap.Empty;
                documents[index] = new FirestoreDocument(path, exists, metadata, fields);
            }

            return documents;
        }

        private FirestoreValue ReadValue(int depth)
        {
            int start = position;
            byte tag = ReadByte();
            switch (tag)
            {
                case Wire.Tag.Null:
                    return FirestoreValue.Null;
                case Wire.Tag.False:
                    return FirestoreValue.FromBoolean(false);
                case Wire.Tag.True:
                    return FirestoreValue.FromBoolean(true);
                case Wire.Tag.Integer:
                    return FirestoreValue.FromInteger(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
                case Wire.Tag.Double:
                    return FirestoreValue.FromDouble(BinaryPrimitives.ReadDoubleLittleEndian(Take(8)));
                case Wire.Tag.String:
                    return FirestoreValue.FromString(ReadString());
                case Wire.Tag.Bytes:
                    return FirestoreValue.OwningBytes(Take(ReadCount(minimumBytesEach: 1)).ToArray());
                case Wire.Tag.Timestamp:
                {
                    long seconds = BinaryPrimitives.ReadInt64LittleEndian(Take(8));
                    int nanoseconds = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
                    if (nanoseconds is < 0 or > 999_999_999)
                        throw Fail($"Timestamp nanoseconds {nanoseconds} are out of range.", start);
                    return FirestoreValue.FromTimestamp(new FirestoreTimestamp(seconds, nanoseconds));
                }

                case Wire.Tag.GeoPoint:
                {
                    double latitude = BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
                    double longitude = BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
                    return FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(latitude, longitude));
                }

                case Wire.Tag.Reference:
                    return FirestoreValue.FromReference(ReadString());
                case Wire.Tag.Array:
                {
                    CheckDepth(depth, start);
                    int count = ReadCount(minimumBytesEach: 1);
                    FirestoreValue[] items = count == 0 ? [] : new FirestoreValue[count];
                    for (int index = 0; index < count; index++)
                        items[index] = ReadValue(depth + 1);
                    return FirestoreValue.FromArray(FirestoreArray.Owning(items));
                }

                case Wire.Tag.Map:
                    CheckDepth(depth, start);
                    return FirestoreValue.FromMap(ReadMapBody(depth + 1));
                case Wire.Tag.Unsupported:
                    return FirestoreValue.Unsupported(ReadString());
                default:
                    throw Fail($"Value tag {tag} is not supported.", start);
            }
        }

        private FirestoreMap ReadMapBody(int depth)
        {
            int count = ReadCount(minimumBytesEach: 2);
            if (count == 0)
                return FirestoreMap.Empty;

            var names = new string[count];
            var values = new FirestoreValue[count];
            for (int index = 0; index < count; index++)
            {
                names[index] = ReadKey();
                values[index] = ReadValue(depth);
            }

            return FirestoreMap.Owning(names, values);
        }

        private readonly void CheckDepth(int depth, int start)
        {
            if (depth >= Wire.MaxDepth)
                throw Fail($"Values are nested deeper than {Wire.MaxDepth}.", start);
        }

        // Field names repeat across every document of a snapshot, so they are decoded once per frame.
        private string ReadKey()
        {
            ReadOnlySpan<byte> bytes = Take(ReadCount(minimumBytesEach: 1));
            return keys.GetOrAdd(bytes);
        }

        private string ReadString()
        {
            ReadOnlySpan<byte> bytes = Take(ReadCount(minimumBytesEach: 1));
            return bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
        }

        // A count can never exceed the bytes left, which stops a corrupt count from forcing a huge allocation.
        private int ReadCount(int minimumBytesEach)
        {
            int start = position;
            uint value = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (shift > 28)
                    throw Fail("A varint is longer than 5 bytes.", start);
                byte next = ReadByte();
                value |= (uint)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                    break;
            }

            if (value > int.MaxValue || value > (uint)(frame.Length - position) / (uint)minimumBytesEach)
                throw Fail($"Length {value} runs past the end of the frame.", start);
            return (int)value;
        }

        private uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        private byte ReadByte() => Take(1)[0];

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > frame.Length - position)
                throw Fail("The frame ends in the middle of a value.");
            ReadOnlySpan<byte> taken = frame.Slice(position, count);
            position += count;
            return taken;
        }

        private readonly PeerlessFormatException Fail(string message) => new(message, position);

        private static PeerlessFormatException Fail(string message, int offset) => new(message, offset);
    }

    // A small open-addressing table from UTF-8 bytes to the string decoded from them, for one frame.
    private struct KeyCache()
    {
        private const int Capacity = 256;

        private readonly byte[]?[] encoded = new byte[Capacity][];
        private readonly string?[] decoded = new string[Capacity];
        private int count;

        public string GetOrAdd(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                return string.Empty;

            uint hash = 2166136261;
            foreach (byte value in bytes)
                hash = (hash ^ value) * 16777619;

            for (int probe = 0; probe < Capacity; probe++)
            {
                int slot = (int)((hash + (uint)probe) % Capacity);
                if (encoded[slot] is not { } existing)
                {
                    string text = Encoding.UTF8.GetString(bytes);
                    if (count >= Capacity / 2)
                        return text; // Full enough: stop caching, keep decoding.
                    encoded[slot] = bytes.ToArray();
                    decoded[slot] = text;
                    count++;
                    return text;
                }

                if (bytes.SequenceEqual(existing))
                    return decoded[slot] ?? string.Empty;
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
