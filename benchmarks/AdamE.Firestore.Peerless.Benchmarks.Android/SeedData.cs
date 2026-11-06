using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Turns <see cref="SeedSpec"/>'s plain values into the Java objects the Android binding writes.</summary>
internal static class SeedData
{
    public static Java.Util.HashMap Document(FirebaseFirestore firestore, int index) =>
        Map(firestore, SeedSpec.Document(index));

    public static Java.Util.HashMap EveryKind(FirebaseFirestore firestore) => Map(firestore, SeedSpec.EveryKind());

    private static Java.Util.HashMap Map(FirebaseFirestore firestore, IReadOnlyDictionary<string, object?> fields)
    {
        var map = new Java.Util.HashMap();
        foreach ((string key, object? value) in fields)
            _ = map.Put(new Java.Lang.String(key), ToJava(firestore, value));
        return map;
    }

    private static Java.Lang.Object? ToJava(FirebaseFirestore firestore, object? value) => value switch
    {
        null => null,
        string text => new Java.Lang.String(text),
        long number => Java.Lang.Long.ValueOf(number),
        double number => Java.Lang.Double.ValueOf(number),
        bool flag => Java.Lang.Boolean.ValueOf(flag),
        byte[] bytes => Blob.FromBytes(bytes),
        PlainTimestamp time => new Firebase.Timestamp(time.Seconds, time.Nanoseconds),
        PlainReference reference => firestore.Document(reference.Path),
        PlainGeoPoint point => new GeoPoint(point.Latitude, point.Longitude),
        List<object?> items => List(firestore, items),
        IReadOnlyDictionary<string, object?> fields => Map(firestore, fields),
        _ => throw new NotSupportedException($"SeedSpec has no Java form for {value.GetType().Name}."),
    };

    private static Java.Util.ArrayList List(FirebaseFirestore firestore, List<object?> items)
    {
        var list = new Java.Util.ArrayList();
        foreach (object? item in items)
            _ = list.Add(ToJava(firestore, item));
        return list;
    }
}
