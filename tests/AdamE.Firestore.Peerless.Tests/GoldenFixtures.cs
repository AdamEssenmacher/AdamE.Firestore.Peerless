namespace AdamE.Firestore.Peerless.Tests;

/// <summary>
/// The golden frames in fixtures/wire. Native encoders' tests build the same inputs (see fixtures/wire/README.md) and
/// must produce these bytes exactly, so map keys are listed in the order the native tests insert them.
/// </summary>
internal static class GoldenFixtures
{
    // Java's Double.NaN (0x7FF8000000000000). .NET's double.NaN has the sign bit set, so it is spelled out.
    public static readonly double JavaNaN = BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0000);

    public static IReadOnlyDictionary<string, FirestoreSnapshot> All { get; } = new Dictionary<string, FirestoreSnapshot>
    {
        ["document-scalars"] = DocumentScalars(),
        ["document-missing"] = new FirestoreDocumentSnapshot(
            new SnapshotMetadata(IsFromCache: true, HasPendingWrites: false),
            new FirestoreDocument("fixtures/missing", exists: false, new SnapshotMetadata(true, false), FirestoreMap.Empty)),
        ["query-nested"] = QueryNested(),
        ["query-empty"] = new FirestoreQuerySnapshot(new SnapshotMetadata(IsFromCache: true, HasPendingWrites: false), []),
    };

    private static FirestoreDocumentSnapshot DocumentScalars() => new(
        new SnapshotMetadata(IsFromCache: false, HasPendingWrites: false),
        new FirestoreDocument("fixtures/scalars", exists: true, new SnapshotMetadata(false, false), Map(
            ("null", FirestoreValue.Null),
            ("false", FirestoreValue.FromBoolean(false)),
            ("true", FirestoreValue.FromBoolean(true)),
            ("zero", FirestoreValue.FromInteger(0)),
            ("int-min", FirestoreValue.FromInteger(long.MinValue)),
            ("int-max", FirestoreValue.FromInteger(long.MaxValue)),
            ("int-negative", FirestoreValue.FromInteger(-42)),
            ("double-whole", FirestoreValue.FromDouble(3.0)),
            ("double-fraction", FirestoreValue.FromDouble(-1.5)),
            ("double-negative-zero", FirestoreValue.FromDouble(-0.0)),
            ("double-nan", FirestoreValue.FromDouble(JavaNaN)),
            ("double-infinity", FirestoreValue.FromDouble(double.PositiveInfinity)),
            ("string-empty", FirestoreValue.FromString("")),
            ("string-ascii", FirestoreValue.FromString("hello")),
            ("string-unicode", FirestoreValue.FromString("héllo wörld ✓ 😀")),
            ("bytes", FirestoreValue.FromBytes([0x00, 0x01, 0x7F, 0x80, 0xFF])),
            ("bytes-empty", FirestoreValue.FromBytes([])),
            ("timestamp", FirestoreValue.FromTimestamp(new FirestoreTimestamp(1_767_225_600, 123_456_789))),
            ("timestamp-pre-epoch", FirestoreValue.FromTimestamp(new FirestoreTimestamp(-1, 999_999_999))),
            ("geopoint", FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(42.3314, -83.0458))),
            ("reference", FirestoreValue.FromReference("users/user-001")),
            ("unsupported", FirestoreValue.Unsupported("com.google.firebase.firestore.VectorValue")))));

    private static FirestoreQuerySnapshot QueryNested()
    {
        FirestoreValue list = Array(
            FirestoreValue.FromInteger(1),
            FirestoreValue.FromDouble(2.5),
            FirestoreValue.FromString("three"),
            FirestoreValue.Null,
            FirestoreValue.FromBoolean(true),
            FirestoreValue.FromMap(Map(("k", FirestoreValue.FromString("v")))),
            Array(Array()));
        FirestoreValue nested = FirestoreValue.FromMap(Map(
            ("level", FirestoreValue.FromInteger(1)),
            ("deeper", FirestoreValue.FromMap(Map(("list", list))))));

        return new FirestoreQuerySnapshot(
            new SnapshotMetadata(IsFromCache: false, HasPendingWrites: true),
            [
                new FirestoreDocument("items/a", exists: true, new SnapshotMetadata(false, true), Map(
                    ("tags", Array(FirestoreValue.FromString("x"), FirestoreValue.FromString("y"))),
                    ("empty-array", Array()),
                    ("empty-map", FirestoreValue.FromMap(FirestoreMap.Empty)),
                    ("nested", nested))),
                new FirestoreDocument("items/b", exists: true, new SnapshotMetadata(false, false), Map(
                    ("count", FirestoreValue.FromInteger(128)),
                    ("long-string", FirestoreValue.FromString(new string('a', 200))))),
            ]);
    }

    private static FirestoreMap Map(params (string Key, FirestoreValue Value)[] entries) =>
        new(entries.Select(static entry => KeyValuePair.Create(entry.Key, entry.Value)));

    private static FirestoreValue Array(params FirestoreValue[] items) => FirestoreValue.FromArray(new FirestoreArray(items));
}
