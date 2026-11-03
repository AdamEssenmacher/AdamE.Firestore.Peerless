using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>A Firestore timestamp as plain .NET data.</summary>
internal readonly record struct PlainTimestamp(long Seconds, int Nanoseconds);

/// <summary>A Firestore document reference as plain .NET data: just its path.</summary>
internal readonly record struct PlainReference(string Path);

/// <summary>A document converted eagerly into plain .NET values.</summary>
internal sealed record PlainDocument(string Path, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// The naive, peered conversion an ordinary app does, unchanged from step 1 so the baseline stays comparable: walk
/// every snapshot through the binding and copy each value into plain .NET data (string, long, double, bool,
/// <see cref="PlainTimestamp"/>, <see cref="PlainReference"/>, lists and dictionaries). Every Java object touched on
/// the way gets a managed peer. Modelled on the shape of Scavos's FirestoreValues. (<see cref="PeeredConversion"/>
/// builds the Peerless model instead; it is used to check both paths agree, not for timing.)
/// </summary>
internal static class PlainValues
{
    /// <summary>
    /// Converts every document. <paramref name="peakGlobalReferences"/> is raised to the highest JNI global reference
    /// count seen after each document, because GCs during a large conversion release references before it ends.
    /// </summary>
    public static List<PlainDocument> Query(QuerySnapshot snapshot, ref int peakGlobalReferences)
    {
        var documents = new List<PlainDocument>();
        foreach (DocumentSnapshot document in snapshot.Documents)
        {
            documents.Add(Document(document));
            peakGlobalReferences = Math.Max(peakGlobalReferences, Java.Interop.JniEnvironment.Runtime.GlobalReferenceCount);
        }

        return documents;
    }

    public static PlainDocument Document(DocumentSnapshot snapshot) =>
        new(snapshot.Reference.Path, Fields(snapshot.Data));

    private static Dictionary<string, object?> Fields(IDictionary<string, Java.Lang.Object>? data)
    {
        if (data is null)
            return [];

        var fields = new Dictionary<string, object?>(data.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, Java.Lang.Object> pair in data)
            fields[pair.Key] = From(pair.Value);
        return fields;
    }

    // The binding hands a nested list or map over either as a Java collection or as its own .NET wrapper of one, and
    // a wrapper's items as .NET primitives, so both shapes are read.
    private static object? From(object? value) => value switch
    {
        null => null,
        Java.Lang.Boolean flag => flag.BooleanValue(),
        bool flag => flag,
        Java.Lang.Long number => number.LongValue(),
        long number => number,
        Java.Lang.Double number => number.DoubleValue(),
        double number => number,
        Java.Lang.String text => text.ToString(),
        string text => text,
        Firebase.Timestamp time => new PlainTimestamp(time.Seconds, time.Nanoseconds),
        DocumentReference reference => new PlainReference(reference.Path),
        Java.Util.IList list => List(list),
        Java.Util.IMap map => Map(map),
        System.Collections.IList list => list.Cast<object?>().Select(From).ToList(),
        System.Collections.IDictionary map => map.Keys.Cast<object>().ToDictionary(
            static key => key.ToString() ?? string.Empty,
            key => From(map[key]),
            StringComparer.Ordinal),
        Java.Lang.Object other => throw new NotSupportedException($"Unexpected Java value {other.Class.Name}."),
        _ => throw new NotSupportedException($"Unexpected .NET value {value.GetType().Name}."),
    };

    private static List<object?> List(Java.Util.IList list)
    {
        int size = list.Size();
        var items = new List<object?>(size);
        for (int index = 0; index < size; index++)
            items.Add(From(list.Get(index)));
        return items;
    }

    private static Dictionary<string, object?> Map(Java.Util.IMap map)
    {
        var entries = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (object? key in map.KeySet())
        {
            using var name = new Java.Lang.String(key?.ToString() ?? string.Empty);
            entries[name.ToString()] = From(map.Get(name));
        }

        return entries;
    }

    /// <summary>Counts every value in a converted document, so the run can prove the conversion was complete.</summary>
    public static int CountValues(object? value) => value switch
    {
        PlainDocument document => document.Fields.Values.Sum(CountValues),
        IReadOnlyDictionary<string, object?> map => 1 + map.Values.Sum(CountValues),
        List<object?> list => 1 + list.Sum(CountValues),
        _ => 1,
    };
}
