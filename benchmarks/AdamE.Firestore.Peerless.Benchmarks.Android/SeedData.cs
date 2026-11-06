using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Deterministic, realistic documents: 16 top-level fields covering string, long, double, bool, timestamp, reference,
/// arrays of strings, arrays of maps and maps nested two deep.
/// </summary>
internal static class SeedData
{
    private const long BaseSeconds = 1_767_225_600; // 2026-01-01T00:00:00Z

    private static readonly string[] Statuses = ["draft", "active", "paused", "archived"];
    private static readonly string[] TagPool = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];

    public static Java.Util.HashMap Document(FirebaseFirestore firestore, int index)
    {
        var random = new Random(index);
        var document = new Java.Util.HashMap();
        Put(document, "name", Text($"Item {index:D5}"));
        Put(document, "description", Text(
            $"Item {index} is a realistic benchmark document with a description long enough to look like user text."));
        Put(document, "ownerId", Text($"user-{index % 50:D3}"));
        Put(document, "status", Text(Statuses[index % Statuses.Length]));
        Put(document, "createdAt", new Firebase.Timestamp(BaseSeconds + index * 60L, index * 1000));
        Put(document, "updatedAt", new Firebase.Timestamp(BaseSeconds + index * 120L, index * 7));
        Put(document, "score", Java.Lang.Double.ValueOf(random.NextDouble() * 100));
        Put(document, "rating", Java.Lang.Double.ValueOf(Math.Round(random.NextDouble() * 5, 1)));
        Put(document, "count", Java.Lang.Long.ValueOf(random.NextInt64(0, 1_000_000)));
        Put(document, "version", Java.Lang.Long.ValueOf(1));
        Put(document, "active", Java.Lang.Boolean.ValueOf(index % 3 != 0));
        Put(document, "owner", firestore.Document($"users/user-{index % 50:D3}"));

        var tags = new Java.Util.ArrayList();
        for (int tag = 0; tag < 5; tag++)
            _ = tags.Add(Text(TagPool[(index + tag) % TagPool.Length]));
        Put(document, "tags", tags);

        var location = new Java.Util.HashMap();
        Put(location, "lat", Java.Lang.Double.ValueOf(42.0 + random.NextDouble()));
        Put(location, "lng", Java.Lang.Double.ValueOf(-83.0 - random.NextDouble()));
        Put(location, "geohash", Text($"dpsb{index % 1000:D3}"));
        Put(document, "location", location);

        var history = new Java.Util.ArrayList();
        for (int entry = 0; entry < 3; entry++)
        {
            var point = new Java.Util.HashMap();
            Put(point, "at", new Firebase.Timestamp(BaseSeconds + index * 60L + entry * 3600, 0));
            Put(point, "value", Java.Lang.Double.ValueOf(random.NextDouble()));
            _ = history.Add(point);
        }

        var stats = new Java.Util.HashMap();
        Put(stats, "views", Java.Lang.Long.ValueOf(random.NextInt64(0, 100_000)));
        Put(stats, "likes", Java.Lang.Long.ValueOf(random.NextInt64(0, 10_000)));
        Put(stats, "history", history);
        Put(document, "stats", stats);

        var attachments = new Java.Util.ArrayList();
        for (int attachment = 0; attachment < 2; attachment++)
        {
            var file = new Java.Util.HashMap();
            Put(file, "name", Text($"file-{index}-{attachment}.jpg"));
            Put(file, "size", Java.Lang.Long.ValueOf(random.NextInt64(10_000, 5_000_000)));
            Put(file, "url", Text($"https://example.invalid/{index}/{attachment}.jpg"));
            _ = attachments.Add(file);
        }

        Put(document, "attachments", attachments);
        return document;
    }

    private static Java.Lang.String Text(string value) => new(value);

    private static void Put(Java.Util.HashMap map, string key, Java.Lang.Object value) =>
        _ = map.Put(new Java.Lang.String(key), value);
}
