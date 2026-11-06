using Firebase.CloudFirestore;
using FirebaseFirestore = Firebase.CloudFirestore.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Turns <see cref="SeedSpec"/>'s plain values into the Objective-C objects the iOS binding writes.</summary>
internal static class SeedData
{
    public static NSDictionary<NSString, NSObject> Document(FirebaseFirestore firestore, int index) =>
        Map(firestore, SeedSpec.Document(index));

    public static NSDictionary<NSString, NSObject> EveryKind(FirebaseFirestore firestore) => Map(firestore, SeedSpec.EveryKind());

    private static NSDictionary<NSString, NSObject> Map(FirebaseFirestore firestore, IReadOnlyDictionary<string, object?> fields)
    {
        KeyValuePair<string, object?>[] pairs = [.. fields];
        return NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
            [.. pairs.Select(pair => ToNative(firestore, pair.Value))],
            [.. pairs.Select(static pair => new NSString(pair.Key))],
            pairs.Length);
    }

    private static NSObject ToNative(FirebaseFirestore firestore, object? value) => value switch
    {
        null => NSNull.Null,
        string text => new NSString(text),
        long number => NSNumber.FromInt64(number),
        double number => NSNumber.FromDouble(number),
        bool flag => NSNumber.FromBoolean(flag),
        byte[] bytes => NSData.FromArray(bytes),
        PlainTimestamp time => Firebase.Core.Timestamp.Create(time.Seconds, time.Nanoseconds),
        PlainReference reference => firestore.GetDocument(reference.Path),
        PlainGeoPoint point => new GeoPoint(point.Latitude, point.Longitude),
        List<object?> items => NSArray.FromNSObjects([.. items.Select(item => ToNative(firestore, item))]),
        IReadOnlyDictionary<string, object?> nested => Map(firestore, nested),
        _ => throw new NotSupportedException($"SeedSpec has no Objective-C form for {value.GetType().Name}."),
    };
}
