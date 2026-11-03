using System.Buffers.Binary;
using System.Text;

namespace AdamE.Firestore.Peerless.Tests;

/// <summary>
/// A test-only encoder written straight from docs/wire-format.md. It produces the golden fixtures and drives the round
/// trip properties. The shipping encoders are native (Java on Android); this one only checks the spec and the decoder.
/// </summary>
internal sealed class TestFrameEncoder
{
    private readonly List<byte> bytes = [];

    public static byte[] Encode(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => Encode(Wire.FrameKind.Query, query.Metadata, query.Documents),
        FirestoreDocumentSnapshot document => Encode(Wire.FrameKind.Document, document.Metadata, [document.Document]),
        _ => throw new ArgumentException("Unknown snapshot type.", nameof(snapshot)),
    };

    public static byte[] Encode(byte kind, SnapshotMetadata metadata, IReadOnlyList<FirestoreDocument> documents)
    {
        var encoder = new TestFrameEncoder();
        encoder.Header(kind);
        encoder.Section(Wire.Section.Metadata, section => section.Byte(MetadataFlags(metadata)));
        encoder.Section(Wire.Section.Documents, section =>
        {
            section.Varint(documents.Count);
            foreach (FirestoreDocument document in documents)
                section.Document(document);
        });
        return [.. encoder.bytes];
    }

    public static byte MetadataFlags(SnapshotMetadata metadata) => (byte)(
        (metadata.IsFromCache ? Wire.MetadataFlags.FromCache : 0)
        | (metadata.HasPendingWrites ? Wire.MetadataFlags.HasPendingWrites : 0));

    public TestFrameEncoder Header(byte kind, byte major = Wire.MajorVersion, byte minor = Wire.MinorVersion)
    {
        bytes.AddRange(Wire.Magic);
        bytes.Add(major);
        bytes.Add(minor);
        bytes.Add(kind);
        bytes.Add(0);
        return this;
    }

    public TestFrameEncoder Section(byte id, Action<TestFrameEncoder> payload)
    {
        var body = new TestFrameEncoder();
        payload(body);
        bytes.Add(id);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)body.bytes.Count);
        bytes.AddRange(length);
        bytes.AddRange(body.bytes);
        return this;
    }

    public TestFrameEncoder Document(FirestoreDocument document)
    {
        String(document.Path);
        Byte((byte)(
            (document.Exists ? Wire.DocumentFlags.Exists : 0)
            | (document.Metadata.IsFromCache ? Wire.DocumentFlags.FromCache : 0)
            | (document.Metadata.HasPendingWrites ? Wire.DocumentFlags.HasPendingWrites : 0)));
        if (document.Exists)
            MapBody(document.Fields);
        return this;
    }

    public TestFrameEncoder Value(FirestoreValue value)
    {
        switch (value.Kind)
        {
            case FirestoreValueKind.Null:
                Byte(Wire.Tag.Null);
                break;
            case FirestoreValueKind.Boolean:
                Byte(value.AsBoolean() ? Wire.Tag.True : Wire.Tag.False);
                break;
            case FirestoreValueKind.Integer:
                Byte(Wire.Tag.Integer).Int64(value.AsInteger());
                break;
            case FirestoreValueKind.Double:
                Byte(Wire.Tag.Double).Int64(BitConverter.DoubleToInt64Bits(value.AsDouble()));
                break;
            case FirestoreValueKind.String:
                Byte(Wire.Tag.String).String(value.AsString());
                break;
            case FirestoreValueKind.Bytes:
                Byte(Wire.Tag.Bytes).Varint(value.AsBytes().Length);
                bytes.AddRange(value.AsBytes().Span);
                break;
            case FirestoreValueKind.Timestamp:
                Byte(Wire.Tag.Timestamp).Int64(value.AsTimestamp().Seconds).Int32(value.AsTimestamp().Nanoseconds);
                break;
            case FirestoreValueKind.GeoPoint:
                Byte(Wire.Tag.GeoPoint)
                    .Int64(BitConverter.DoubleToInt64Bits(value.AsGeoPoint().Latitude))
                    .Int64(BitConverter.DoubleToInt64Bits(value.AsGeoPoint().Longitude));
                break;
            case FirestoreValueKind.Reference:
                Byte(Wire.Tag.Reference).String(value.AsReferencePath());
                break;
            case FirestoreValueKind.Array:
                Byte(Wire.Tag.Array).Varint(value.AsArray().Count);
                foreach (FirestoreValue item in value.AsArray())
                    Value(item);
                break;
            case FirestoreValueKind.Map:
                Byte(Wire.Tag.Map).MapBody(value.AsMap());
                break;
            case FirestoreValueKind.Unsupported:
                Byte(Wire.Tag.Unsupported).String(value.UnsupportedTypeName ?? string.Empty);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unknown kind.");
        }

        return this;
    }

    public TestFrameEncoder MapBody(FirestoreMap map)
    {
        Varint(map.Count);
        foreach ((string key, FirestoreValue item) in map)
            String(key).Value(item);
        return this;
    }

    public TestFrameEncoder Byte(byte value)
    {
        bytes.Add(value);
        return this;
    }

    public TestFrameEncoder Raw(ReadOnlySpan<byte> value)
    {
        bytes.AddRange(value);
        return this;
    }

    public TestFrameEncoder Int32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        bytes.AddRange(buffer);
        return this;
    }

    public TestFrameEncoder Int64(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        bytes.AddRange(buffer);
        return this;
    }

    public TestFrameEncoder Varint(int value)
    {
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            bytes.Add((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        bytes.Add((byte)remaining);
        return this;
    }

    public TestFrameEncoder String(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        Varint(utf8.Length);
        bytes.AddRange(utf8);
        return this;
    }

    public byte[] ToArray() => [.. bytes];
}
