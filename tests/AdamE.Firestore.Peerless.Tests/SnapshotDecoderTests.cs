using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

public sealed class SnapshotDecoderTests
{
    private static readonly SnapshotMetadata Live = new(false, false);

    [Fact]
    public void A_frame_from_a_newer_minor_version_with_an_unknown_section_decodes()
    {
        byte[] frame = new TestFrameEncoder()
            .Header(Wire.FrameKind.Query, minor: 7)
            .Section(Wire.Section.Metadata, section => section.Byte(Wire.MetadataFlags.FromCache))
            .Section(9, section => section.Raw([1, 2, 3, 4, 5]))
            .Section(Wire.Section.Documents, section => section.Varint(1).Document(Doc("a/1", ("n", FirestoreValue.FromInteger(5)))))
            .ToArray();

        FirestoreQuerySnapshot snapshot = SnapshotDecoder.DecodeQuery(frame);

        Assert.True(snapshot.Metadata.IsFromCache);
        Assert.Equal(5, Assert.Single(snapshot.Documents).Fields["n"].AsInteger());
    }

    [Fact]
    public void Sections_may_come_in_any_order()
    {
        byte[] frame = new TestFrameEncoder()
            .Header(Wire.FrameKind.Document)
            .Section(Wire.Section.Documents, section => section.Varint(1).Document(Doc("a/1")))
            .Section(Wire.Section.Metadata, section => section.Byte(Wire.MetadataFlags.HasPendingWrites))
            .ToArray();

        FirestoreDocumentSnapshot snapshot = SnapshotDecoder.DecodeDocument(frame);

        Assert.True(snapshot.Metadata.HasPendingWrites);
        Assert.Equal("1", snapshot.Document.Id);
    }

    public static TheoryData<string, byte[]> MalformedFrames => new()
    {
        { "empty", [] },
        { "bad magic", [.. "PLSX"u8, 1, 0, 1, 0] },
        { "major version 2", new TestFrameEncoder().Header(Wire.FrameKind.Query, major: 2).ToArray() },
        { "reserved changes kind", ValidSections(new TestFrameEncoder().Header(3)) },
        { "unknown kind", ValidSections(new TestFrameEncoder().Header(42)) },
        { "no sections", new TestFrameEncoder().Header(Wire.FrameKind.Query).ToArray() },
        {
            "no documents section", new TestFrameEncoder().Header(Wire.FrameKind.Query)
                .Section(Wire.Section.Metadata, section => section.Byte(0)).ToArray()
        },
        {
            "duplicate metadata", ValidSections(new TestFrameEncoder().Header(Wire.FrameKind.Query)
                .Section(Wire.Section.Metadata, section => section.Byte(0)))
        },
        {
            "section longer than frame", new TestFrameEncoder().Header(Wire.FrameKind.Query)
                .Byte(Wire.Section.Metadata).Int32(1000).Byte(0).ToArray()
        },
        {
            "section shorter than its content", new TestFrameEncoder().Header(Wire.FrameKind.Query)
                .Section(Wire.Section.Metadata, section => section.Byte(0).Byte(0))
                .Section(Wire.Section.Documents, section => section.Varint(0)).ToArray()
        },
        { "unknown value tag", DocumentWith(raw => raw.Byte(13)) },
        { "truncated integer", DocumentWith(raw => raw.Byte(Wire.Tag.Integer).Int32(7)) },
        { "nanoseconds too large", DocumentWith(raw => raw.Byte(Wire.Tag.Timestamp).Int64(0).Int32(1_000_000_000)) },
        { "negative nanoseconds", DocumentWith(raw => raw.Byte(Wire.Tag.Timestamp).Int64(0).Int32(-1)) },
        { "huge string length", DocumentWith(raw => raw.Byte(Wire.Tag.String).Varint(int.MaxValue)) },
        { "varint longer than 5 bytes", DocumentWith(raw => raw.Byte(Wire.Tag.String).Raw([0x80, 0x80, 0x80, 0x80, 0x80, 0x01])) },
        { "huge array count", DocumentWith(raw => raw.Byte(Wire.Tag.Array).Varint(1_000_000)) },
        { "nesting too deep", DocumentWith(raw => Nest(raw, Wire.MaxDepth + 1)) },
        {
            "document frame with two documents", Frame(Wire.FrameKind.Document, section => section.Varint(2)
                .Document(Doc("a/1")).Document(Doc("a/2")))
        },
        {
            "query with a missing document", Frame(Wire.FrameKind.Query, section => section.Varint(1)
                .Document(new FirestoreDocument("a/1", false, Live, FirestoreMap.Empty)))
        },
        { "empty document path", Frame(Wire.FrameKind.Query, section => section.Varint(1).String("").Byte(0)) },
    };

    [Theory]
    [MemberData(nameof(MalformedFrames))]
    public void A_malformed_frame_is_rejected_with_a_format_exception(string because, byte[] frame)
    {
        PeerlessFormatException exception = Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame));

        Assert.InRange(exception.Offset, 0, Math.Max(0, frame.Length));
        Assert.False(string.IsNullOrEmpty(because));
    }

    [Fact]
    public void Nesting_up_to_the_limit_decodes()
    {
        byte[] frame = DocumentWith(raw => Nest(raw, Wire.MaxDepth - 1));

        FirestoreValue value = SnapshotDecoder.DecodeDocument(frame).Document.Fields["field"];

        int depth = 0;
        while (value.TryGetArray(out FirestoreArray? array) && array.Count > 0)
        {
            value = array[0];
            depth++;
        }

        Assert.Equal(Wire.MaxDepth - 2, depth);
    }

    [Fact]
    public void Asking_for_the_wrong_snapshot_kind_is_a_format_exception()
    {
        byte[] document = TestFrameEncoder.Encode(new FirestoreDocumentSnapshot(Live, Doc("a/1")));

        Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.DecodeQuery(document));
    }

    [Fact]
    public void Repeated_field_names_decode_to_one_shared_string_per_frame()
    {
        FirestoreDocument[] documents = [.. Enumerable.Range(0, 3).Select(index =>
            Doc($"a/{index}", ("shared-name", FirestoreValue.FromInteger(index))))];
        byte[] frame = TestFrameEncoder.Encode(new FirestoreQuerySnapshot(Live, documents));

        FirestoreQuerySnapshot snapshot = SnapshotDecoder.DecodeQuery(frame);

        string first = snapshot.Documents[0].Fields.Keys.Single();
        Assert.All(snapshot.Documents, document => Assert.Same(first, document.Fields.Keys.Single()));
    }

    [Fact]
    public void More_distinct_field_names_than_the_cache_holds_still_decode()
    {
        (string, FirestoreValue)[] fields = [.. Enumerable.Range(0, 1000)
            .Select(index => ($"field-{index}", FirestoreValue.FromInteger(index)))];
        byte[] frame = TestFrameEncoder.Encode(new FirestoreDocumentSnapshot(Live, Doc("a/1", fields)));

        FirestoreMap decoded = SnapshotDecoder.DecodeDocument(frame).Document.Fields;

        Assert.Equal(1000, decoded.Count);
        Assert.Equal(999, decoded["field-999"].AsInteger());
        Assert.Equal(0, decoded["field-0"].AsInteger());
    }

    private static FirestoreDocument Doc(string path, params (string Key, FirestoreValue Value)[] fields) =>
        new(path, true, Live, new FirestoreMap(fields.Select(static field => KeyValuePair.Create(field.Key, field.Value))));

    private static byte[] Frame(byte kind, Action<TestFrameEncoder> documents) =>
        new TestFrameEncoder().Header(kind)
            .Section(Wire.Section.Metadata, section => section.Byte(0))
            .Section(Wire.Section.Documents, documents)
            .ToArray();

    private static byte[] ValidSections(TestFrameEncoder header) =>
        header.Section(Wire.Section.Metadata, section => section.Byte(0))
            .Section(Wire.Section.Documents, section => section.Varint(0))
            .ToArray();

    // A document frame whose single field "field" is written raw by the caller.
    private static byte[] DocumentWith(Action<TestFrameEncoder> field) =>
        Frame(Wire.FrameKind.Document, section =>
        {
            section.Varint(1).String("a/1").Byte(Wire.DocumentFlags.Exists).Varint(1).String("field");
            field(section);
        });

    // Arrays nested `levels` deep, the innermost empty.
    private static void Nest(TestFrameEncoder field, int levels)
    {
        for (int level = 0; level < levels - 1; level++)
            field.Byte(Wire.Tag.Array).Varint(1);
        field.Byte(Wire.Tag.Array).Varint(0);
    }
}
