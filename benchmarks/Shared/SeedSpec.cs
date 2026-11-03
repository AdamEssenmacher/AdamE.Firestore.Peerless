namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// The seeded documents, as plain .NET values (<see cref="PlainModel"/>), so every platform writes exactly the same
/// data and can check what it reads back against it. Each platform's SeedData turns these values into its binding's
/// native objects.
/// </summary>
internal static class SeedSpec
{
    private const long BaseSeconds = 1_767_225_600; // 2026-01-01T00:00:00Z

    private static readonly string[] Statuses = ["draft", "active", "paused", "archived"];
    private static readonly string[] TagPool = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];

    /// <summary>
    /// A deterministic, realistic document: 16 top-level fields covering string, long, double, bool, timestamp,
    /// reference, arrays of strings, arrays of maps and maps nested two deep (44 values in all).
    /// </summary>
    /// <remarks>The random draws happen in step 1's order, so the seeded values are step 1's.</remarks>
    public static Dictionary<string, object?> Document(int index)
    {
        var random = new Random(index);
        var document = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = $"Item {index:D5}",
            ["description"] =
                $"Item {index} is a realistic benchmark document with a description long enough to look like user text.",
            ["ownerId"] = $"user-{index % 50:D3}",
            ["status"] = Statuses[index % Statuses.Length],
            ["createdAt"] = new PlainTimestamp(BaseSeconds + index * 60L, index * 1000),
            ["updatedAt"] = new PlainTimestamp(BaseSeconds + index * 120L, index * 7),
        };
        document["score"] = random.NextDouble() * 100;
        document["rating"] = Math.Round(random.NextDouble() * 5, 1);
        document["count"] = random.NextInt64(0, 1_000_000);
        document["version"] = 1L;
        document["active"] = index % 3 != 0;
        document["owner"] = new PlainReference($"users/user-{index % 50:D3}");

        var tags = new List<object?>();
        for (int tag = 0; tag < 5; tag++)
            tags.Add(TagPool[(index + tag) % TagPool.Length]);
        document["tags"] = tags;

        var location = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["lat"] = 42.0 + random.NextDouble(),
            ["lng"] = -83.0 - random.NextDouble(),
            ["geohash"] = $"dpsb{index % 1000:D3}",
        };
        document["location"] = location;

        var history = new List<object?>();
        for (int entry = 0; entry < 3; entry++)
        {
            history.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["at"] = new PlainTimestamp(BaseSeconds + index * 60L + entry * 3600, 0),
                ["value"] = random.NextDouble(),
            });
        }

        var stats = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["views"] = random.NextInt64(0, 100_000),
            ["likes"] = random.NextInt64(0, 10_000),
            ["history"] = history,
        };
        document["stats"] = stats;

        var attachments = new List<object?>();
        for (int attachment = 0; attachment < 2; attachment++)
        {
            var file = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = $"file-{index}-{attachment}.jpg",
            };
            file["size"] = random.NextInt64(10_000, 5_000_000);
            file["url"] = $"https://example.invalid/{index}/{attachment}.jpg";
            attachments.Add(file);
        }

        document["attachments"] = attachments;
        return document;
    }

    /// <summary>
    /// One document with every value kind the wire format has, for checking that paths agree: null, bytes, geo
    /// points, unicode, extreme numbers, a whole-number double, empty and deeply nested containers.
    /// </summary>
    public static Dictionary<string, object?> EveryKind() => new(StringComparer.Ordinal)
    {
        ["null"] = null,
        ["true"] = true,
        ["int-min"] = long.MinValue,
        ["int-max"] = long.MaxValue,
        ["double-whole"] = 3.0,
        ["double-negative-zero"] = -0.0,
        ["double-nan"] = double.NaN,
        ["double-infinity"] = double.NegativeInfinity,
        ["string-empty"] = "",
        ["string-unicode"] = "héllo wörld ✓ 😀 日本語",
        ["bytes"] = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF },
        ["timestamp"] = new PlainTimestamp(-1, 999_999_999),
        ["geopoint"] = new PlainGeoPoint(42.3314, -83.0458),
        ["reference"] = new PlainReference("users/user-001"),
        ["empty-array"] = new List<object?>(),
        ["empty-map"] = new Dictionary<string, object?>(StringComparer.Ordinal),
        // Firestore rejects an array directly inside an array, so the innermost array sits in a map.
        ["nested"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["deeper"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["list"] = new List<object?>
                {
                    1L,
                    2.5,
                    "three",
                    null,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["inner"] = new List<object?>() },
                },
            },
        },
    };
}
