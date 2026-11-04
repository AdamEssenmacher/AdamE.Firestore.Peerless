using CsCheck;
using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

/// <summary>Property tests: anything the spec can express survives encode then decode, and garbage never crashes.</summary>
public sealed class RoundTripProperties
{
    // Well-formed UTF-16 only (a lone surrogate is legitimately replaced with U+FFFD on the way through UTF-8), but
    // otherwise anything: NUL, every BMP plane outside the surrogates, and supplementary-plane characters as pairs.
    private static readonly Gen<string> CodePoint = Gen.OneOf(
        Gen.Int[0, 0xD7FF],
        Gen.Int[0xE000, 0xFFFF],
        Gen.Int[0x10000, 0x10FFFF]).Select(char.ConvertFromUtf32);

    private static readonly Gen<string> Text = Gen.OneOf(
        Gen.String[Gen.Char.AlphaNumeric, 0, 12],
        CodePoint.Array[0, 12].Select(static parts => string.Concat(parts)),
        Gen.Const("😀 émoji ✓"));

    // Keys long enough to need a multi-byte varint length (128+ bytes) as well as short ones.
    private static readonly Gen<string> Key = Gen.OneOf(
        Text,
        Gen.String[Gen.Char.AlphaNumeric, 128, 300],
        CodePoint.Array[50, 120].Select(static parts => string.Concat(parts)));

    private static readonly Gen<string> Path = Gen.String[Gen.Char.AlphaNumeric, 1, 8]
        .Array[1, 4]
        .Select(static segments => string.Join('/', segments));

    private static readonly Gen<FirestoreValue> Scalar = Gen.OneOf(
        Gen.Const(FirestoreValue.Null),
        Gen.Bool.Select(FirestoreValue.FromBoolean),
        Gen.Long.Select(FirestoreValue.FromInteger),
        Gen.Long.Select(static bits => FirestoreValue.FromDouble(BitConverter.Int64BitsToDouble(bits))),
        Text.Select(FirestoreValue.FromString),
        Gen.Byte.Array[0, 16].Select(static bytes => FirestoreValue.FromBytes(bytes)),
        Gen.Select(Gen.Long[SnapshotDecoder.MinTimestampSeconds, SnapshotDecoder.MaxTimestampSeconds], Gen.Int[0, 999_999_999], static (seconds, nanos) =>
            FirestoreValue.FromTimestamp(new FirestoreTimestamp(seconds, nanos))),
        Gen.Select(Gen.Double[-90, 90], Gen.Double[-180, 180], static (latitude, longitude) =>
            FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(latitude, longitude))),
        Path.Select(FirestoreValue.FromReference),
        Text.Select(FirestoreValue.Unsupported));

    private static readonly Gen<FirestoreValue> Value = Gen.Recursive<FirestoreValue>((depth, value) => depth >= 4
        ? Scalar
        : Gen.Frequency(
            (6, Scalar),
            (1, value.Array[0, 5].Select(static items => FirestoreValue.FromArray(new FirestoreArray(items)))),
            (1, Fields(value).Select(FirestoreValue.FromMap))));

    private static readonly Gen<SnapshotMetadata> Metadata =
        Gen.Select(Gen.Bool, Gen.Bool, static (cache, pending) => new SnapshotMetadata(cache, pending));

    private static readonly Gen<FirestoreDocument> ExistingDocument =
        Gen.Select(Path, Metadata, Fields(Value), static (path, metadata, fields) =>
            new FirestoreDocument(path, true, metadata, fields));

    private static readonly Gen<FirestoreSnapshot> Snapshot = Gen.OneOf<FirestoreSnapshot>(
        Gen.Select(Metadata, ExistingDocument.Array[0, 6], static (metadata, documents) =>
            new FirestoreQuerySnapshot(metadata, documents)),
        Gen.Select(Metadata, ExistingDocument, static (metadata, document) =>
            new FirestoreDocumentSnapshot(metadata, document)),
        Gen.Select(Metadata, Path, Metadata, static (metadata, path, documentMetadata) =>
            new FirestoreDocumentSnapshot(metadata, new FirestoreDocument(path, false, documentMetadata, FirestoreMap.Empty))));

    [Fact]
    public void Every_snapshot_round_trips() =>
        Snapshot.Sample(static snapshot =>
            SnapshotAssert.Equal(snapshot, SnapshotDecoder.Decode(TestFrameEncoder.Encode(snapshot))));

    [Fact]
    public void Every_value_round_trips_with_its_kind_intact() =>
        Value.Sample(static value =>
        {
            var snapshot = new FirestoreDocumentSnapshot(
                new SnapshotMetadata(false, false),
                new FirestoreDocument("a/1", true, new SnapshotMetadata(false, false), new FirestoreMap([KeyValuePair.Create("v", value)])));

            FirestoreValue decoded = SnapshotDecoder.DecodeDocument(TestFrameEncoder.Encode(snapshot)).Document.Fields["v"];

            Assert.Equal(value.Kind, decoded.Kind);
            Assert.True(value.Equals(decoded), $"{value} became {decoded}");
        });

    [Fact]
    public void A_truncated_frame_is_always_a_format_exception() =>
        Gen.Select(Snapshot, Gen.Double[0, 1]).Sample(static (snapshot, fraction) =>
        {
            byte[] frame = TestFrameEncoder.Encode(snapshot);
            int length = (int)(fraction * (frame.Length - 1));

            Assert.Throws<PeerlessFormatException>(() => SnapshotDecoder.Decode(frame.AsSpan(0, length)));
        });

    [Fact]
    public void A_corrupted_frame_decodes_or_is_a_format_exception_but_never_anything_else() =>
        Gen.Select(Snapshot, Gen.Int.Array[1, 8], Gen.Byte.Array[8]).Sample(static (snapshot, positions, values) =>
        {
            byte[] frame = TestFrameEncoder.Encode(snapshot);
            for (int index = 0; index < positions.Length; index++)
                frame[(int)((uint)positions[index] % (uint)frame.Length)] = values[index];

            try
            {
                _ = SnapshotDecoder.Decode(frame);
            }
            catch (PeerlessFormatException)
            {
            }
        });

    private static Gen<FirestoreMap> Fields(Gen<FirestoreValue> value) =>
        Gen.Select(Key, value).Array[0, 6].Select(static entries =>
        {
            var unique = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
            foreach ((string key, FirestoreValue item) in entries)
                unique.TryAdd(key, item);
            return new FirestoreMap(unique);
        });
}
