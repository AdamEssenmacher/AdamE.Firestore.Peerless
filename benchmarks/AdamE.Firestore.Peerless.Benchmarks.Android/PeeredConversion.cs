using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// The naive, peered conversion an ordinary app does: walk each snapshot through the binding and copy every value into
/// the Peerless value model, so both modes produce identical output and only the transport differs. Every Java object
/// touched on the way gets a managed peer, which is the cost being measured. Modelled on Scavos's FirestoreValues.
/// </summary>
internal static class PeeredConversion
{
    /// <summary>
    /// Converts every document. <paramref name="peakGlobalReferences"/> is raised to the highest JNI global reference
    /// count seen after each document, because GCs during a large conversion release references before it ends.
    /// </summary>
    public static FirestoreQuerySnapshot Query(QuerySnapshot snapshot, ref int peakGlobalReferences)
    {
        IList<DocumentSnapshot> source = snapshot.Documents;
        var documents = new FirestoreDocument[source.Count];
        for (int index = 0; index < documents.Length; index++)
        {
            documents[index] = Document(source[index]);
            peakGlobalReferences = Math.Max(peakGlobalReferences, Java.Interop.JniEnvironment.Runtime.GlobalReferenceCount);
        }

        return new FirestoreQuerySnapshot(Metadata(snapshot.Metadata), Array.AsReadOnly(documents));
    }

    public static FirestoreDocumentSnapshot DocumentSnapshot(DocumentSnapshot snapshot) =>
        new(Metadata(snapshot.Metadata), Document(snapshot));

    private static FirestoreDocument Document(DocumentSnapshot snapshot)
    {
        bool exists = snapshot.Exists();
        return new FirestoreDocument(
            snapshot.Reference.Path,
            exists,
            Metadata(snapshot.Metadata),
            exists ? Fields(snapshot.Data) : FirestoreMap.Empty);
    }

    private static SnapshotMetadata Metadata(Firebase.Firestore.SnapshotMetadata metadata) =>
        new(metadata.IsFromCache, metadata.HasPendingWrites);

    private static FirestoreMap Fields(IDictionary<string, Java.Lang.Object>? data)
    {
        if (data is null || data.Count == 0)
            return FirestoreMap.Empty;

        var keys = new string[data.Count];
        var values = new FirestoreValue[data.Count];
        int index = 0;
        foreach (KeyValuePair<string, Java.Lang.Object> pair in data)
        {
            keys[index] = pair.Key;
            values[index++] = From(pair.Value);
        }

        return FirestoreMap.Owning(keys, values);
    }

    // The binding hands a nested list or map over either as a Java collection or as its own .NET wrapper of one, and
    // a wrapper's items as .NET primitives, so both shapes are read.
    private static FirestoreValue From(object? value) => value switch
    {
        null => FirestoreValue.Null,
        Java.Lang.Boolean flag => FirestoreValue.FromBoolean(flag.BooleanValue()),
        bool flag => FirestoreValue.FromBoolean(flag),
        Java.Lang.Long number => FirestoreValue.FromInteger(number.LongValue()),
        long number => FirestoreValue.FromInteger(number),
        Java.Lang.Double number => FirestoreValue.FromDouble(number.DoubleValue()),
        double number => FirestoreValue.FromDouble(number),
        Java.Lang.String text => FirestoreValue.FromString(text.ToString()),
        string text => FirestoreValue.FromString(text),
        Firebase.Timestamp time => FirestoreValue.FromTimestamp(new FirestoreTimestamp(time.Seconds, time.Nanoseconds)),
        DocumentReference reference => FirestoreValue.FromReference(reference.Path),
        GeoPoint point => FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(point.Latitude, point.Longitude)),
        Blob blob => FirestoreValue.FromBytes(blob.ToBytes()),
        Java.Util.IList list => List(list),
        Java.Util.IMap map => Map(map),
        System.Collections.IList list => FirestoreValue.FromArray(
            FirestoreArray.Owning([.. list.Cast<object?>().Select(From)])),
        System.Collections.IDictionary map => FirestoreValue.FromMap(FirestoreMap.Owning(
            [.. map.Keys.Cast<object>().Select(static key => key.ToString() ?? string.Empty)],
            [.. map.Keys.Cast<object>().Select(key => From(map[key]))])),
        Java.Lang.Object other => FirestoreValue.Unsupported(other.Class.Name),
        _ => FirestoreValue.Unsupported(value.GetType().FullName ?? value.GetType().Name),
    };

    private static FirestoreValue List(Java.Util.IList list)
    {
        int size = list.Size();
        var items = new FirestoreValue[size];
        for (int index = 0; index < size; index++)
            items[index] = From(list.Get(index));
        return FirestoreValue.FromArray(FirestoreArray.Owning(items));
    }

    private static FirestoreValue Map(Java.Util.IMap map)
    {
        int size = map.Size();
        var keys = new string[size];
        var values = new FirestoreValue[size];
        int index = 0;
        foreach (object? key in map.KeySet())
        {
            using var name = new Java.Lang.String(key?.ToString() ?? string.Empty);
            keys[index] = name.ToString();
            values[index++] = From(map.Get(name));
        }

        return FirestoreValue.FromMap(FirestoreMap.Owning(keys, values));
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
