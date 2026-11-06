using System.Text;
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

    [Fact]
    public void A_document_count_cannot_claim_bytes_from_the_next_section()
    {
        const int count = 128 * 1024;
        byte[] frame = new TestFrameEncoder()
            .Header(Wire.FrameKind.Query)
            .Section(Wire.Section.Metadata, section => section.Byte(0))
            .Section(Wire.Section.Documents, section => section.Varint(count))
            .Section(9, section => section.Raw(new byte[2 * count]))
            .ToArray();

        (Exception? failure, long allocated) = MeasureDecode(frame);

        var exception = Assert.IsType<PeerlessFormatException>(failure);
        Assert.Contains($"Length {count} runs past the end of the section.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(Wire.HeaderLength + 6 + 5, exception.Offset); // after the header, metadata section and documents header
        Assert.True(allocated <= FixedOverhead, $"Decoding allocated {allocated:N0} bytes for a count with no documents.");
    }

    // Each frame is followed by a 64 KiB unknown section, so a bound on the bytes left in the frame (instead of the
    // section) would let the content claim or read those bytes and fail differently, or not at all.
    public static TheoryData<string, byte[], string> SectionBoundFrames => new()
    {
        {
            // Fields map of 502 entries, the first an array of 1,000 nulls, then the section ends: the map's 501
            // unread entries and the array's items don't both fit in the section.
            "nested counts", FollowedByUnknownSection(section =>
            {
                section.Varint(1).String("a/1").Byte(Wire.DocumentFlags.Exists).Varint(502).Varint(0).Byte(Wire.Tag.Array).Varint(1_000);
                section.Raw(new byte[1_000]);
            }),
            "left in the section"
        },
        {
            // 1,000 documents in 2,000 bytes; the first one's fields map claims 900 entries, which the other 999
            // documents' minimum two bytes each leave no room for.
            "a fields map and the documents after it", FollowedByUnknownSection(section =>
                section.Varint(1_000).String("a/1").Byte(Wire.DocumentFlags.Exists).Varint(900).Raw(new byte[1_993])),
            "left in the section"
        },
        {
            "a value", FollowedByUnknownSection(section =>
                section.Varint(1).String("a/1").Byte(Wire.DocumentFlags.Exists).Varint(1).Varint(0).Byte(Wire.Tag.Integer).Int32(7)),
            "past the end of its section"
        },
        {
            // The same overrun with no section after it reports the section, not the frame, ending.
            "a value in the last section", Frame(Wire.FrameKind.Query, section =>
                section.Varint(1).String("a/1").Byte(Wire.DocumentFlags.Exists).Varint(1).Varint(0).Byte(Wire.Tag.Integer).Int32(7)),
            "past the end of its section"
        },
        {
            "section shorter than its content", new TestFrameEncoder().Header(Wire.FrameKind.Query)
                .Section(Wire.Section.Metadata, _ => { })
                .Section(Wire.Section.Documents, section => section.Varint(0)).ToArray(),
            "past the end of its section"
        },
    };

    [Theory]
    [MemberData(nameof(SectionBoundFrames))]
    public void Content_is_bounded_by_its_section(string because, byte[] frame, string expected)
    {
        PeerlessFormatException exception = Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(because));
    }

    private static byte[] FollowedByUnknownSection(Action<TestFrameEncoder> documents) =>
        Frame(Wire.FrameKind.Query, documents, unknownSectionBytes: 64 * 1024);

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
            "section longer than its content", new TestFrameEncoder().Header(Wire.FrameKind.Query)
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
        { "timestamp after 9999", DocumentWith(raw => raw.Byte(Wire.Tag.Timestamp).Int64(FirestoreTimestamp.MaxSeconds + 1).Int32(0)) },
        { "timestamp before 0001", DocumentWith(raw => raw.Byte(Wire.Tag.Timestamp).Int64(FirestoreTimestamp.MinSeconds - 1).Int32(0)) },
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
            .Byte(Wire.Tag.Timestamp).Int64(FirestoreTimestamp.MinSeconds).Int32(0)
            .Byte(Wire.Tag.Timestamp).Int64(FirestoreTimestamp.MaxSeconds).Int32(999_999_999));

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

    // The real worst case (docs/wire-format.md): a frame that decodes costs at most about 43 bytes of .NET memory per
    // frame byte on CoreCLR, which is what these tests run on (a chain of one-entry maps, 3 bytes and ~128 bytes of
    // objects each; Mono's larger array headers would add about 5). A malformed frame is rejected as soon as its
    // declared counts exceed the bytes left in the section; it keeps what its valid prefix cost (up to the same bound) plus at most
    // one slot per byte its open containers claimed (32 for arrays, 20 for maps). Plus about 5 KB of working memory
    // on a thread's first decode.
    private const int BytesPerFrameByte = 48;
    private const int FixedOverhead = 16 * 1024;

    public static TheoryData<string, int> AllocationShapes => new()
    {
        { "nulls", 1 }, { "nulls", 16 }, { "nulls", 17 }, { "nulls", 1_000 }, { "nulls", 131_073 },
        { "one-item arrays", 1 }, { "one-item arrays", 17 }, { "one-item arrays", 20_000 },
        { "one-entry maps", 1 }, { "one-entry maps", 17 }, { "one-entry maps", 20_000 },
        { "hostile nested counts", 98 },
        { "map claiming one more entry than the frame holds", 1_000 },
        { "map claiming one more entry than the frame holds", 200_000 },
        { "outer array reserved but never filled", 200_000 },
        { "one-entry maps nested 97 deep", 1 },
        { "one-entry maps nested 97 deep", 300 },
        { "map claiming half the frame, holding a map claiming the rest", 2_000_000 },
    };

    [Theory]
    [MemberData(nameof(AllocationShapes))]
    public void Allocation_stays_within_a_fixed_multiple_of_the_frame(string shape, int size)
    {
        byte[] frame = shape switch
        {
            // Fields map count 2, key "", then an array of `size` nulls and nothing after: the map's unread second
            // entry stays reserved, so the array's count no longer fits and it is rejected before its array is allocated.
            "map claiming one more entry than the frame holds" => Frame(Wire.FrameKind.Document, section =>
            {
                section.Varint(1).String("a/1").Byte(Wire.DocumentFlags.Exists).Varint(2).String("")
                    .Byte(Wire.Tag.Array).Varint(size);
                for (int index = 0; index < size; index++)
                    section.Byte(Wire.Tag.Null);
            }),
            _ => [],
        };
        if (frame.Length == 0)
            frame = DocumentWith(field =>
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
                case "one-entry maps nested 97 deep":
                    // An array of `size` chains, each 97 one-entry maps deep with empty keys (0B 01 00 per level):
                    // the densest objects-per-byte shape that decodes.
                    field.Byte(Wire.Tag.Array).Varint(size);
                    for (int chain = 0; chain < size; chain++)
                    {
                        for (int level = 0; level < 97; level++)
                            field.Byte(Wire.Tag.Map).Varint(1).Varint(0);
                        field.Byte(Wire.Tag.Null);
                    }

                    break;
                case "map claiming half the frame, holding a map claiming the rest":
                    // 0B <L/2-40> 00 0B <L/2-60>, then 0xFF up to `size` bytes: two map counts that together claim more
                    // entries than the frame can hold at two bytes each.
                    field.Byte(Wire.Tag.Map).Varint(size / 2 - 40).Varint(0).Byte(Wire.Tag.Map).Varint(size / 2 - 60);
                    field.Raw(Enumerable.Repeat((byte)0xFF, size - 30).ToArray());
                    break;
                case "outer array reserved but never filled":
                    // An outer array claims `size` items, which fits, so its array is allocated; its first item is an
                    // array that really holds `size - 4` nulls, which no longer fits beside the outer array's reserved
                    // items, so it is rejected before its own array is allocated.
                    field.Byte(Wire.Tag.Array).Varint(size).Byte(Wire.Tag.Array).Varint(size - 4);
                    for (int index = 0; index < size - 4; index++)
                        field.Byte(Wire.Tag.Null);
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

        (Exception? failure, long allocated) = MeasureDecode(frame);

        if (shape is "map claiming one more entry than the frame holds" or "outer array reserved but never filled"
            or "hostile nested counts" or "map claiming half the frame, holding a map claiming the rest")
        {
            // Rejected as soon as the declared counts stop fitting in the bytes left in the section, before allocating for them.
            Assert.Contains("Declared counts need", Assert.IsType<PeerlessFormatException>(failure).Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(failure);
        }

        long bound = (long)BytesPerFrameByte * frame.Length + FixedOverhead;
        TestContext.Current.SendDiagnosticMessage($"{shape} x {size}: {(double)allocated / frame.Length:F1} B per frame byte");
        Assert.True(allocated <= bound, $"{shape} x {size}: {allocated:N0} bytes for a {frame.Length:N0}-byte frame (bound {bound:N0}).");
    }

    // Decodes once to warm the per-thread scratch, then measures a second decode's allocation and outcome.
    private static (Exception? Failure, long Allocated) MeasureDecode(byte[] frame)
    {
        _ = Record.Exception(() => SnapshotDecoder.Decode(frame));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Exception? failure = Record.Exception(() => SnapshotDecoder.Decode(frame));
        return (failure, GC.GetAllocatedBytesForCurrentThread() - before);
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

    [Fact]
    public void Colliding_map_keys_keep_their_wire_order_and_values()
    {
        string[] keys = CollidingKeys();
        using IDisposable colliding = SnapshotDecoder.CollideKeyHashesForTesting();

        FirestoreMap decoded = SnapshotDecoder.DecodeDocument(CollidingMapFrame(keys)).Document.Fields["field"].AsMap();

        Assert.Equal(keys, decoded.Keys);
        for (int index = 0; index < keys.Length; index++)
            Assert.Equal(index, decoded[keys[index]].AsInteger());
    }

    [Fact]
    public void A_duplicate_among_colliding_map_keys_is_rejected()
    {
        string[] keys = CollidingKeys();
        using IDisposable colliding = SnapshotDecoder.CollideKeyHashesForTesting();

        var error = Assert.Throws<PeerlessFormatException>(() =>
            SnapshotDecoder.DecodeDocument(CollidingMapFrame([.. keys, keys[^1]])));

        Assert.Contains("appears twice", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    // SipHash-1-3 with key 00..0f over the message 00, 01, ... (the reference implementation's vectors; also checked
    // against an independent implementation that reproduces SipHash-2-4's published vector).
    [InlineData(0, 0xabac0158050fc4dcUL)]
    [InlineData(1, 0xc9f49bf37d57ca93UL)]
    [InlineData(7, 0xd3927d989bb11140UL)]
    [InlineData(8, 0x369095118d299a8eUL)]
    [InlineData(15, 0xd320d86d2a519956UL)]
    [InlineData(16, 0xcc4fdd1a7d908b66UL)]
    [InlineData(63, 0x9d199062b7bbb3a8UL)]
    public void The_key_hash_is_SipHash_1_3(int length, ulong expected)
    {
        byte[] message = [.. Enumerable.Range(0, length).Select(static value => (byte)value)];

        Assert.Equal(expected, SnapshotDecoder.SipHash13(message, 0x0706050403020100UL, 0x0f0e0d0c0b0a0908UL));
    }

    [Fact]
    public void Keys_crafted_to_collide_under_FNV_1a_spread_under_the_keyed_hash()
    {
        // These keys collide under FNV-1a for every seed, which is why the decoder no longer uses it. (A sanity check
        // that they aren't special to SipHash; the folded hash and slot choice aren't tested here.)
        string[] keys = CollidingKeys();
        var random = new Random(7);
        for (int trial = 0; trial < 20; trial++)
        {
            ulong key0 = (ulong)random.NextInt64(), key1 = (ulong)random.NextInt64();
            int distinct = keys.Select(key => SnapshotDecoder.SipHash13(Encoding.UTF8.GetBytes(key), key0, key1)).Distinct().Count();
            Assert.Equal(keys.Length, distinct);
        }
    }

    [Fact]
    public void A_large_map_with_random_keys_and_one_late_duplicate_is_rejected()
    {
        var random = new Random(11);
        string[] keys = [.. Enumerable.Range(0, 40_000).Select(index => $"{index}-{random.Next():x8}")];

        var error = Assert.Throws<PeerlessFormatException>(() =>
            SnapshotDecoder.DecodeDocument(CollidingMapFrame([.. keys, keys[12_345]])));

        Assert.Contains($"\"{keys[12_345]}\" appears twice", error.Message, StringComparison.Ordinal);
    }

    private static string[] CollidingKeys()
    {
        // These blocks of U+0000 and U+0001 have the same FNV-1a transition for every initial hash state, so under FNV-1a
        // (the decoder's old hash) all 64 combinations collide for any seed. The keyed hash spreads them, so the tests
        // that need collisions force them with CollideKeyHashesForTesting.
        string left = new("00001111111010011101011".Select(static bit => (char)(bit - '0')).ToArray());
        string right = new("10001001100000111101010".Select(static bit => (char)(bit - '0')).ToArray());
        const int Blocks = 6;
        return [.. Enumerable.Range(0, 1 << Blocks).Select(index =>
            string.Concat(Enumerable.Range(0, Blocks).Select(bit => (index & (1 << bit)) == 0 ? left : right)))];
    }

    private static byte[] CollidingMapFrame(string[] keys) => DocumentWith(raw =>
    {
        raw.Byte(Wire.Tag.Map).Varint(keys.Length);
        for (int index = 0; index < keys.Length; index++)
            raw.String(keys[index]).Byte(Wire.Tag.Integer).Int64(index);
    });

    [Theory]
    [InlineData("/")]
    [InlineData("a")]
    [InlineData("rooms/a/messages")]
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
    public void Geo_points_compare_by_their_bits_and_reject_impossible_coordinates()
    {
        Assert.Equal(new FirestoreGeoPoint(1, 2), new FirestoreGeoPoint(1, 2));
        Assert.NotEqual(new FirestoreGeoPoint(0.0, 1), new FirestoreGeoPoint(-0.0, 1));
        Assert.Equal(new FirestoreGeoPoint(-90, 180).GetHashCode(), new FirestoreGeoPoint(-90, 180).GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreGeoPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreGeoPoint(90.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirestoreGeoPoint(0, -180.5));
    }

    [Fact]
    public void Map_keys_and_values_are_read_only_views()
    {
        var map = new FirestoreMap([KeyValuePair.Create("a", FirestoreValue.FromInteger(1))]);

        Assert.IsNotType<string[]>(map.Keys);
        Assert.IsNotType<FirestoreValue[]>(map.Values);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)map.Keys)[0] = "x");
        Assert.Equal(1, map["a"].AsInteger());
    }

    [Fact]
    public void Hash_codes_depend_on_content_not_only_on_size()
    {
        FirestoreValue Bytes(byte first) => FirestoreValue.FromBytes([first, 2, 3]);
        FirestoreArray Array(long first) => new([FirestoreValue.FromInteger(first)]);
        FirestoreMap Map(string key) => new([KeyValuePair.Create(key, FirestoreValue.Null)]);

        Assert.NotEqual(Bytes(1).GetHashCode(), Bytes(9).GetHashCode());
        Assert.NotEqual(Array(1).GetHashCode(), Array(9).GetHashCode());
        Assert.NotEqual(Map("a").GetHashCode(), Map("b").GetHashCode());
        Assert.Equal(
            new FirestoreMap([KeyValuePair.Create("a", FirestoreValue.Null), KeyValuePair.Create("b", FirestoreValue.Null)]).GetHashCode(),
            new FirestoreMap([KeyValuePair.Create("b", FirestoreValue.Null), KeyValuePair.Create("a", FirestoreValue.Null)]).GetHashCode());
    }

    [Fact]
    public void Every_kind_has_a_try_get()
    {
        Assert.True(FirestoreValue.FromBoolean(true).TryGetBoolean(out bool flag) && flag);
        Assert.True(FirestoreValue.FromBytes([7]).TryGetBytes(out ReadOnlyMemory<byte> bytes) && bytes.Span[0] == 7);
        Assert.True(FirestoreValue.FromTimestamp(new FirestoreTimestamp(5, 6)).TryGetTimestamp(out FirestoreTimestamp time) && time.Seconds == 5);
        Assert.True(FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(1, 2)).TryGetGeoPoint(out FirestoreGeoPoint point) && point.Longitude == 2);
        Assert.True(FirestoreValue.FromReference("a/b").TryGetReferencePath(out string? path) && path == "a/b");
        Assert.False(FirestoreValue.Null.TryGetBoolean(out _));
        Assert.False(FirestoreValue.FromInteger(1).TryGetReferencePath(out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("a//b")]
    [InlineData("users")]
    public void References_that_are_not_document_paths_are_rejected(string path)
    {
        byte[] frame = DocumentWith(raw => raw.Byte(Wire.Tag.Reference).String(path));

        Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame));
        Assert.Throws<ArgumentException>(() => FirestoreValue.FromReference(path));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    public void Impossible_geo_points_are_rejected(double latitude, double longitude)
    {
        byte[] frame = DocumentWith(raw => raw.Byte(Wire.Tag.GeoPoint)
            .Int64(BitConverter.DoubleToInt64Bits(latitude)).Int64(BitConverter.DoubleToInt64Bits(longitude)));

        Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame));
    }

    [Fact]
    public void Asking_for_the_wrong_snapshot_kind_is_a_format_exception()
    {
        byte[] document = TestFrameEncoder.Encode(new FirestoreDocumentSnapshot(Live, Doc("a/1")));

        Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.DecodeQuery(document));
    }

    [Fact]
    public void The_wrong_snapshot_kind_is_refused_before_decoding()
    {
        // A query frame whose documents section is garbage: refused by its kind byte, not by its content.
        byte[] query = Frame(Wire.FrameKind.Query, section => section.Raw([0xFF, 0xFF]));

        PeerlessFormatException exception = Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.DecodeDocument(query));

        Assert.Contains("not a document snapshot", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_document_frame_with_other_than_one_document_is_refused_at_its_count(int count)
    {
        byte[] frame = Frame(Wire.FrameKind.Document, section =>
        {
            section.Varint(count);
            for (int index = 0; index < count; index++)
                section.String($"a/{index}").Byte(Wire.DocumentFlags.Exists).Varint(0);
        });

        PeerlessFormatException exception = Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame));

        Assert.Contains($"exactly one document, not {count}", exception.Message, StringComparison.Ordinal);
        Assert.Equal(Wire.HeaderLength + 6 + 5, exception.Offset); // the count, after the metadata and documents headers
    }

    [Theory]
    [InlineData(Wire.Tag.Bytes)]
    [InlineData(Wire.Tag.String)]
    public void A_value_cannot_claim_bytes_reserved_for_the_items_after_it(byte tag)
    {
        // An array of 1,000 items whose first is a value claiming all but 500 of the bytes left: the other 999 items'
        // minimum byte each no longer fit, so it is refused before its bytes are copied.
        const int length = 100_000;
        byte[] frame = DocumentWith(raw =>
            raw.Byte(Wire.Tag.Array).Varint(1_000).Byte(tag).Varint(length).Raw(new byte[length + 500]));

        (Exception? failure, long allocated) = MeasureDecode(frame);

        Assert.Contains("Declared counts need", Assert.IsType<PeerlessFormatException>(failure).Message, StringComparison.Ordinal);
        Assert.True(allocated < length, $"Decoding allocated {allocated:N0} bytes before refusing a {length:N0}-byte value.");
    }

    [Fact]
    public void A_query_snapshot_copies_its_documents()
    {
        List<FirestoreDocument> documents = [Doc("a/1")];
        var snapshot = new FirestoreQuerySnapshot(Live, documents);

        documents.Clear();

        Assert.Single(snapshot.Documents);
    }

    [Fact]
    public void A_query_snapshot_refuses_null_and_missing_documents()
    {
        Assert.Throws<ArgumentException>(() => new FirestoreQuerySnapshot(Live, [null!]));
        Assert.Throws<ArgumentException>(() =>
            new FirestoreQuerySnapshot(Live, [new FirestoreDocument("a/1", false, Live, FirestoreMap.Empty)]));
    }

    [Fact]
    public async Task Arrays_and_maps_can_be_enumerated_with_await_in_the_loop_body()
    {
        var array = new FirestoreArray([FirestoreValue.FromInteger(1), FirestoreValue.FromInteger(2)]);
        var map = new FirestoreMap([KeyValuePair.Create("a", FirestoreValue.FromInteger(3))]);
        long sum = 0;

        foreach (FirestoreValue value in array)
        {
            await Task.Yield();
            sum += value.AsInteger();
        }

        foreach ((string _, FirestoreValue value) in map)
        {
            await Task.Yield();
            sum += value.AsInteger();
        }

        Assert.Equal(6, sum);
    }

    [Fact]
    public void Foreach_over_an_array_or_a_map_does_not_allocate()
    {
        var array = new FirestoreArray([FirestoreValue.FromInteger(1), FirestoreValue.FromInteger(2)]);
        var map = new FirestoreMap([KeyValuePair.Create("a", FirestoreValue.FromInteger(3))]);
        long sum = Sum(array, map); // warm up

        long before = GC.GetAllocatedBytesForCurrentThread();
        sum += Sum(array, map);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(12, sum);
        Assert.Equal(0, allocated);

        static long Sum(FirestoreArray array, FirestoreMap map)
        {
            long total = 0;
            foreach (FirestoreValue value in array)
                total += value.AsInteger();
            foreach (KeyValuePair<string, FirestoreValue> entry in map)
                total += entry.Value.AsInteger();
            return total;
        }
    }

    [Fact]
    public void A_built_map_keeps_insertion_order_and_refuses_duplicate_keys()
    {
        string[] keys = [.. Enumerable.Range(0, 50).Select(index => $"k{(index * 37) % 50}")];
        var map = new FirestoreMap(keys.Select(key => KeyValuePair.Create(key, FirestoreValue.Null)));

        Assert.Equal(keys, map.Keys);
        Assert.Throws<ArgumentException>(() =>
            new FirestoreMap([KeyValuePair.Create("a", FirestoreValue.Null), KeyValuePair.Create("a", FirestoreValue.Null)]));
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

    // A frame with plain metadata and the given documents section, optionally followed by an unknown section 9.
    private static byte[] Frame(byte kind, Action<TestFrameEncoder> documents, int? unknownSectionBytes = null)
    {
        TestFrameEncoder encoder = new TestFrameEncoder().Header(kind)
            .Section(Wire.Section.Metadata, section => section.Byte(0))
            .Section(Wire.Section.Documents, documents);
        if (unknownSectionBytes is int bytes)
            encoder.Section(9, section => section.Raw(new byte[bytes]));
        return encoder.ToArray();
    }

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
