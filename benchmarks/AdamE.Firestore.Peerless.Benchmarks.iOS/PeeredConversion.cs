using Firebase.CloudFirestore;
using PeerlessMetadata = AdamE.Firestore.Peerless.SnapshotMetadata;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// The peered conversion into the Peerless value model, so the peered binding and Peerless can be compared exactly
/// on the same snapshot. Used only for that check, not for timing (timed peered mode uses <see cref="PlainValues"/>,
/// as Android does).
/// </summary>
internal static class PeeredConversion
{
    public static FirestoreQuerySnapshot Query(QuerySnapshot snapshot)
    {
        QueryDocumentSnapshot[] source = snapshot.Documents;
        var documents = new FirestoreDocument[source.Length];
        for (int index = 0; index < documents.Length; index++)
            documents[index] = Document(source[index]);
        return new FirestoreQuerySnapshot(Metadata(snapshot.Metadata), Array.AsReadOnly(documents));
    }

    public static FirestoreDocumentSnapshot DocumentSnapshot(DocumentSnapshot snapshot) =>
        new(Metadata(snapshot.Metadata), Document(snapshot));

    private static FirestoreDocument Document(DocumentSnapshot snapshot)
    {
        bool exists = snapshot.Exists;
        return new FirestoreDocument(
            snapshot.Reference.Path,
            exists,
            Metadata(snapshot.Metadata),
            exists ? Map(snapshot.Data) : FirestoreMap.Empty);
    }

    private static PeerlessMetadata Metadata(Firebase.CloudFirestore.SnapshotMetadata metadata) =>
        new(metadata.IsFromCache, metadata.HasPendingWrites);

    private static FirestoreMap Map(NSDictionary? map)
    {
        if (map is null || map.Count == 0)
            return FirestoreMap.Empty;
        var keys = new string[(int)map.Count];
        var values = new FirestoreValue[keys.Length];
        int index = 0;
        foreach (KeyValuePair<NSObject, NSObject> pair in map)
        {
            keys[index] = pair.Key.ToString() ?? string.Empty;
            values[index++] = From(pair.Value);
        }

        return FirestoreMap.Owning(keys, values);
    }

    private static FirestoreValue From(NSObject? value) => value switch
    {
        null or NSNull => FirestoreValue.Null,
        NSNumber { ObjCType: "c" or "B" } flag => FirestoreValue.FromBoolean(flag.BoolValue),
        NSNumber { ObjCType: "q" or "i" or "s" or "l" } number => FirestoreValue.FromInteger(number.Int64Value),
        NSNumber { ObjCType: "d" or "f" } number => FirestoreValue.FromDouble(number.DoubleValue),
        NSString text => FirestoreValue.FromString(text.ToString()),
        Firebase.Core.Timestamp time => FirestoreValue.FromTimestamp(new FirestoreTimestamp(time.Seconds, time.Nanoseconds)),
        DocumentReference reference => FirestoreValue.FromReference(reference.Path),
        GeoPoint point => FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(point.Latitude, point.Longitude)),
        NSData bytes => FirestoreValue.FromBytes(bytes.ToArray()),
        NSArray array => List(array),
        NSDictionary map => FirestoreValue.FromMap(Map(map)),
        _ => FirestoreValue.Unsupported(value.Class.Name ?? value.GetType().Name),
    };

    private static FirestoreValue List(NSArray array)
    {
        var items = new FirestoreValue[(int)array.Count];
        for (int index = 0; index < items.Length; index++)
            items[index] = From(array.GetItem<NSObject>((nuint)index));
        return FirestoreValue.FromArray(FirestoreArray.Owning(items));
    }

    /// <summary>Counts every value, so a run can show the conversion was complete.</summary>
    public static int CountValues(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => query.Documents.Sum(static document => CountMap(document.Fields)),
        FirestoreDocumentSnapshot document => CountMap(document.Document.Fields),
        _ => 0,
    };

    private static int CountMap(FirestoreMap map) => map.Values.Sum(CountValue);

    private static int CountValue(FirestoreValue value) => value.Kind switch
    {
        FirestoreValueKind.Map => 1 + CountMap(value.AsMap()),
        FirestoreValueKind.Array => 1 + value.AsArray().Sum(CountValue),
        _ => 1,
    };
}
