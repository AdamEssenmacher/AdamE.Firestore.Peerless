using Firebase.CloudFirestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// The naive, peered conversion an ordinary app does on iOS: walk every snapshot through the binding and copy each
/// value into the same plain .NET data as Android's peered mode (<see cref="PlainModel"/>). Every Objective-C object
/// touched on the way (dictionary keys, NSNumbers, NSStrings, timestamps, references, arrays) gets a managed peer,
/// which is the cost being measured. Modelled on Scavos's iOS FirestoreValues.
/// </summary>
internal static class PlainValues
{
    /// <summary>
    /// Converts every document. <paramref name="peakPeers"/> is raised to the highest peer-table count seen after each
    /// document. (On the main queue a GC can't lower that count mid-callback, since collected peers are unregistered
    /// by NSObject_Disposer on the main queue, but explicit disposal can, so the peak is sampled rather than assumed.)
    /// </summary>
    public static List<PlainDocument> Query(QuerySnapshot snapshot, ref int peakPeers)
    {
        var documents = new List<PlainDocument>();
        foreach (QueryDocumentSnapshot document in snapshot.Documents)
        {
            documents.Add(Document(document));
            peakPeers = Math.Max(peakPeers, PeerCensus.Count);
        }

        return documents;
    }

    public static PlainDocument Document(DocumentSnapshot snapshot) =>
        new(snapshot.Reference.Path, Fields(snapshot.Data));

    private static Dictionary<string, object?> Fields(NSDictionary<NSString, NSObject>? data)
    {
        if (data is null)
            return [];

        var fields = new Dictionary<string, object?>((int)data.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<NSObject, NSObject> pair in data)
            fields[pair.Key.ToString() ?? string.Empty] = From(pair.Value);
        return fields;
    }

    // The SDK hands every number over as an NSNumber, so its Objective-C type tells them apart: c (or B) a boolean,
    // q/i/s/l a signed whole number, d/f a double.
    private static object? From(NSObject? value) => value switch
    {
        null or NSNull => null,
        NSNumber { ObjCType: "c" or "B" } flag => flag.BoolValue,
        NSNumber { ObjCType: "q" or "i" or "s" or "l" } number => number.Int64Value,
        NSNumber { ObjCType: "d" or "f" } number => number.DoubleValue,
        NSString text => text.ToString(),
        Firebase.Core.Timestamp time => new PlainTimestamp(time.Seconds, time.Nanoseconds),
        DocumentReference reference => new PlainReference(reference.Path),
        GeoPoint point => new PlainGeoPoint(point.Latitude, point.Longitude),
        NSData bytes => bytes.ToArray(),
        NSArray array => List(array),
        NSDictionary map => Map(map),
        NSNumber number => throw new NotSupportedException($"Unexpected NSNumber type '{number.ObjCType}'."),
        _ => throw new NotSupportedException($"Unexpected Objective-C value {value.GetType().Name} ({value.Class.Name})."),
    };

    private static List<object?> List(NSArray array)
    {
        int count = (int)array.Count;
        var items = new List<object?>(count);
        for (nuint index = 0; index < (nuint)count; index++)
            items.Add(From(array.GetItem<NSObject>(index)));
        return items;
    }

    private static Dictionary<string, object?> Map(NSDictionary map)
    {
        var entries = new Dictionary<string, object?>((int)map.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<NSObject, NSObject> pair in map)
            entries[pair.Key.ToString() ?? string.Empty] = From(pair.Value);
        return entries;
    }
}
