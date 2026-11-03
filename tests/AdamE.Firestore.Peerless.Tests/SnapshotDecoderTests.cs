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
            "repeated unknown section", new TestFrameEncoder().Header(Wire.FrameKind.Query)
                .Section(9, section => section.Byte(1))
                .Section(Wire.Section.Metadata, section => section.Byte(0))
                .Section(9, section => section.Byte(2))
                .Section(Wire.Section.Documents, section => section.Varint(0)).ToArray()
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
        { "a container at exactly depth 100", DocumentWith(raw => Nest(raw, Wire.MaxDepth)) },
        { "varint fifth byte above 0x07", DocumentWith(raw => raw.Byte(Wire.Tag.String).Raw([0xFF, 0xFF, 0xFF, 0xFF, 0x08])) },
        { "varint with a sixth byte", DocumentWith(raw => raw.Byte(Wire.Tag.String).Raw([0x80, 0x80, 0x80, 0x80, 0x80, 0x00])) },
        { "non-shortest varint", DocumentWith(raw => raw.Byte(Wire.Tag.String).Raw([0x81, 0x00]).Raw("a"u8)) },
        { "non-shortest varint zero", DocumentWith(raw => raw.Byte(Wire.Tag.String).Raw([0x80, 0x00])) },
        { "modified UTF-8 NUL", DocumentWith(raw => raw.Byte(Wire.Tag.String).Varint(2).Raw([0xC0, 0x80])) },
        { "CESU-8 surrogate pair", DocumentWith(raw => raw.Byte(Wire.Tag.String).Varint(6).Raw([0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80])) },
        { "invalid UTF-8 byte", DocumentWith(raw => raw.Byte(Wire.Tag.String).Varint(1).Raw([0xFF])) },
        { "truncated UTF-8 sequence", DocumentWith(raw => raw.Byte(Wire.Tag.String).Varint(2).Raw([0xE2, 0x9C])) },
        {
            "invalid UTF-8 in a key", Frame(Wire.FrameKind.Document, section => section.Varint(1).String("a/1")
                .Byte(Wire.DocumentFlags.Exists).Varint(1).Varint(2).Raw([0xC0, 0x80]).Byte(Wire.Tag.Null))
        },
        { "invalid UTF-8 in a path", Frame(Wire.FrameKind.Query, section => section.Varint(1).Varint(1).Raw([0xFF]).Byte(Wire.DocumentFlags.Exists).Varint(0)) },
        { "duplicate field", Frame(Wire.FrameKind.Document, section => section.Varint(1).String("a/1").Byte(Wire.DocumentFlags.Exists)
            .Varint(2).String("x").Byte(Wire.Tag.Null).String("x").Byte(Wire.Tag.True)) },
        { "duplicate key in a nested map", DocumentWith(raw => raw.Byte(Wire.Tag.Map).Varint(2)
            .String("k").Byte(Wire.Tag.Null).String("k").Byte(Wire.Tag.Null)) },
        { "duplicate key in a large map", DocumentWith(raw => LargeMapWithDuplicate(raw)) },
        { "timestamp after 9999", DocumentWith(raw => raw.Byte(Wire.Tag.Timestamp).Int64(SnapshotDecoder.MaxTimestampSeconds + 1).Int32(0)) },
        { "timestamp before 0001", DocumentWith(raw => raw.Byte(Wire.Tag.Timestamp).Int64(SnapshotDecoder.MinTimestampSeconds - 1).Int32(0)) },
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
    public void Timestamps_at_the_edges_of_firestores_range_decode()
    {
        byte[] frame = DocumentWith(raw => raw.Byte(Wire.Tag.Array).Varint(2)
            .Byte(Wire.Tag.Timestamp).Int64(SnapshotDecoder.MinTimestampSeconds).Int32(0)
            .Byte(Wire.Tag.Timestamp).Int64(SnapshotDecoder.MaxTimestampSeconds).Int32(999_999_999));

        FirestoreArray items = SnapshotDecoder.DecodeDocument(frame).Document.Fields["field"].AsArray();

        Assert.Equal(new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero), items[0].AsTimestamp().ToDateTimeOffset());
        Assert.Equal(9999, items[1].AsTimestamp().ToDateTimeOffset().Year);
    }

    [Fact]
    public void Supplementary_plane_keys_and_nul_characters_round_trip()
    {
        var snapshot = new FirestoreDocumentSnapshot(Live, Doc("a/1",
            ("😀 key", FirestoreValue.FromString("nul\0inside")),
            (new string('k', 200), FirestoreValue.FromString("𝄞 music"))));
        byte[] frame = TestFrameEncoder.Encode(snapshot);

        FirestoreMap fields = SnapshotDecoder.DecodeDocument(frame).Document.Fields;

        Assert.Equal("nul\0inside", fields["😀 key"].AsString());
        Assert.Equal("𝄞 music", fields[new string('k', 200)].AsString());
        Assert.Contains((byte)0x00, frame); // NUL is the single byte 00, not modified UTF-8's C0 80.
    }

    // The real worst case: a legitimate frame can't cost more than about 40 bytes of .NET memory per frame byte (a
    // one-item array or map in 3-4 bytes costs ~120-160 bytes of objects), plus the decoder's fixed working memory.
    private const int BytesPerFrameByte = 48;
    private const int FixedOverhead = 16 * 1024;

    public static TheoryData<string, int> AllocationShapes => new()
    {
        { "nulls", 1 }, { "nulls", 16 }, { "nulls", 17 }, { "nulls", 1_000 }, { "nulls", 131_073 },
        { "one-item arrays", 1 }, { "one-item arrays", 17 }, { "one-item arrays", 20_000 },
        { "one-entry maps", 1 }, { "one-entry maps", 17 }, { "one-entry maps", 20_000 },
        { "hostile nested counts", 98 },
    };

    [Theory]
    [MemberData(nameof(AllocationShapes))]
    public void Allocation_stays_within_a_fixed_multiple_of_the_frame(string shape, int size)
    {
        byte[] frame = DocumentWith(field =>
        {
            switch (shape)
            {
                case "nulls":
                    field.Byte(Wire.Tag.Array).Varint(size);
                    for (int index = 0; index < size; index++)
                        field.Byte(Wire.Tag.Null);
                    break;
                case "one-item arrays":
                    field.Byte(Wire.Tag.Array).Varint(size);
                    for (int index = 0; index < size; index++)
                        field.Byte(Wire.Tag.Array).Varint(1).Byte(Wire.Tag.Null);
                    break;
                case "one-entry maps":
                    field.Byte(Wire.Tag.Array).Varint(size);
                    for (int index = 0; index < size; index++)
                        field.Byte(Wire.Tag.Map).Varint(1).Varint(0).Byte(Wire.Tag.Null);
                    break;
                default:
                    // `size` arrays, each claiming almost the whole frame as its item count, then garbage. Allocating
                    // every declared count up front would cost about size x 1 MB x 32 bytes, roughly 3 GB.
                    const int padding = 1_000_000;
                    for (int level = 0; level < size; level++)
                        field.Byte(Wire.Tag.Array).Varint(padding - 1);
                    field.Raw(Enumerable.Repeat((byte)0xFF, padding).ToArray()); // 0xFF: an unknown tag
                    break;
            }
        });

        _ = Record.Exception(() => SnapshotDecoder.Decode(frame)); // warm the per-thread scratch
        long before = GC.GetAllocatedBytesForCurrentThread();
        Exception? failure = Record.Exception(() => SnapshotDecoder.Decode(frame));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        if (shape == "hostile nested counts")
        {
            // It got all the way down to the innermost array's first item, so every declared count was accepted.
            Assert.Contains("tag 255", Assert.IsType<PeerlessFormatException>(failure).Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(failure);
        }

        long bound = (long)BytesPerFrameByte * frame.Length + FixedOverhead;
        Assert.True(allocated <= bound, $"{shape} x {size}: {allocated:N0} bytes for a {frame.Length:N0}-byte frame (bound {bound:N0}).");
    }

    // A fixed budget for a typical frame, so decode allocation can't regress silently. 100 documents x 20 fields
    // allocated 188 KB when this was written (192 KB before the bounded-allocation work); the budget leaves ~5%.
    [Fact]
    public void A_typical_frame_decodes_within_its_allocation_budget()
    {
        byte[] frame = TestFrameEncoder.Encode(TypicalSnapshot());
        _ = SnapshotDecoder.Decode(frame);

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = SnapshotDecoder.Decode(frame);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 198 * 1024, $"Decoding the typical frame allocated {allocated:N0} bytes (budget 198 KB).");
    }

    internal static FirestoreQuerySnapshot TypicalSnapshot() => new(Live, [.. Enumerable.Range(0, 100).Select(document =>
        new FirestoreDocument($"items/doc-{document:D5}", true, Live, new FirestoreMap(Enumerable.Range(0, 20).Select(field =>
            KeyValuePair.Create($"field-{field:D2}", (field % 4) switch
            {
                0 => FirestoreValue.FromString($"value {document} {field}"),
                1 => FirestoreValue.FromInteger(document * 1000 + field),
                2 => FirestoreValue.FromDouble(document + field / 10.0),
                _ => FirestoreValue.FromArray(new FirestoreArray([FirestoreValue.FromString("a"), FirestoreValue.FromInteger(field)])),
            })))))]);

    [Fact]
    public void Many_long_distinct_keys_decode_correctly()
    {
        (string, FirestoreValue)[] fields = [.. Enumerable.Range(0, 2_000)
            .Select(index => ($"{new string('k', 200)}-{index}", FirestoreValue.FromInteger(index)))];
        byte[] frame = TestFrameEncoder.Encode(new FirestoreDocumentSnapshot(Live, Doc("a/1", fields)));

        FirestoreMap decoded = SnapshotDecoder.DecodeDocument(frame).Document.Fields;

        Assert.Equal(2_000, decoded.Count);
        Assert.Equal(1_999, decoded[$"{new string('k', 200)}-1999"].AsInteger());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/a/b")]
    [InlineData("a/b/")]
    [InlineData("a//b")]
    public void Document_paths_with_an_empty_segment_are_rejected(string path)
    {
        byte[] frame = Frame(Wire.FrameKind.Query, section => section.Varint(1).String(path).Byte(Wire.DocumentFlags.Exists).Varint(0));

        Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame));
        Assert.Throws<ArgumentException>(() => new FirestoreDocument(path, true, Live, FirestoreMap.Empty));
    }

    [Fact]
    public void Timestamps_outside_firestores_range_cannot_be_built()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreTimestamp(FirestoreTimestamp.MaxSeconds + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreTimestamp(FirestoreTimestamp.MinSeconds - 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreTimestamp(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreTimestamp(0, 1_000_000_000));
        Assert.Equal(new FirestoreTimestamp(0, 0), default);
    }

    [Fact]
    public void Field_paths_can_be_given_as_segments_to_reach_keys_containing_dots()
    {
        var fields = new FirestoreMap([KeyValuePair.Create("a.b", FirestoreValue.FromMap(new FirestoreMap(
            [KeyValuePair.Create("c", FirestoreValue.FromInteger(7))])))]);

        Assert.False(fields.TryGetPath("a.b.c", out _));
        Assert.True(fields.TryGetPath(["a.b", "c"], out FirestoreValue value));
        Assert.Equal(7, value.AsInteger());
        Assert.False(fields.TryGetPath(ReadOnlySpan<string>.Empty, out _));
    }

    [Fact]
    public void Geo_points_compare_by_their_bits()
    {
        Assert.Equal(new FirestoreGeoPoint(double.NaN, 1), new FirestoreGeoPoint(double.NaN, 1));
        Assert.NotEqual(new FirestoreGeoPoint(0.0, 1), new FirestoreGeoPoint(-0.0, 1));
        Assert.Equal(
            new FirestoreGeoPoint(double.NaN, 1).GetHashCode(),
            new FirestoreGeoPoint(double.NaN, 1).GetHashCode());
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

    private static void LargeMapWithDuplicate(TestFrameEncoder raw)
    {
        raw.Byte(Wire.Tag.Map).Varint(20);
        for (int index = 0; index < 19; index++)
            raw.String($"key-{index}").Byte(Wire.Tag.Null);
        raw.String("key-7").Byte(Wire.Tag.Null);
    }

    // Arrays nested `levels` deep, the innermost empty.
    private static void Nest(TestFrameEncoder field, int levels)
    {
        for (int level = 0; level < levels - 1; level++)
            field.Byte(Wire.Tag.Array).Varint(1);
        field.Byte(Wire.Tag.Array).Varint(0);
    }
}
