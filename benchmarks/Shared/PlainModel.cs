namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>A Firestore timestamp as plain .NET data.</summary>
internal readonly record struct PlainTimestamp(long Seconds, int Nanoseconds);

/// <summary>A Firestore document reference as plain .NET data: just its path.</summary>
internal readonly record struct PlainReference(string Path);

/// <summary>A Firestore geo point as plain .NET data.</summary>
internal readonly record struct PlainGeoPoint(double Latitude, double Longitude);

/// <summary>A document converted eagerly into plain .NET values.</summary>
internal sealed record PlainDocument(string Path, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// The plain .NET values the peered modes produce, on every platform: null, string, long, double, bool, byte[],
/// <see cref="PlainTimestamp"/>, <see cref="PlainReference"/>, <see cref="PlainGeoPoint"/>, <c>List&lt;object?&gt;</c> and
/// <c>Dictionary&lt;string, object?&gt;</c>. <see cref="SeedSpec"/> writes the same shapes.
/// </summary>
internal static class PlainModel
{
    /// <summary>Counts every value in a converted document, so the run can prove the conversion was complete.</summary>
    public static int CountValues(object? value) => value switch
    {
        PlainDocument document => document.Fields.Values.Sum(CountValues),
        IReadOnlyDictionary<string, object?> map => 1 + map.Values.Sum(CountValues),
        List<object?> list => 1 + list.Sum(CountValues),
        _ => 1,
    };

    /// <summary>
    /// Describes the first difference between what was written and what a listener read back, or returns null when
    /// they agree. Firestore keeps timestamps to the microsecond, so <paramref name="expected"/>'s are truncated first.
    /// Doubles compare bit for bit (so −0.0 is not 0.0), except that any NaN equals any NaN.
    /// </summary>
    public static string? Difference(object? expected, object? actual, string path = "")
    {
        switch (expected, actual)
        {
            case (null, null):
                return null;
            case (double left, double right):
                return (double.IsNaN(left) && double.IsNaN(right))
                    || BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right)
                    ? null
                    : $"{path}: {left:R} vs {right:R}";
            case (PlainTimestamp left, PlainTimestamp right):
                var truncated = left with { Nanoseconds = left.Nanoseconds - (left.Nanoseconds % 1000) };
                return truncated == right ? null : $"{path}: {truncated} vs {right}";
            case (byte[] left, byte[] right):
                return left.AsSpan().SequenceEqual(right) ? null : $"{path}: bytes differ";
            case (List<object?> left, List<object?> right):
                if (left.Count != right.Count)
                    return $"{path}: {left.Count} vs {right.Count} items";
                for (int index = 0; index < left.Count; index++)
                {
                    if (Difference(left[index], right[index], $"{path}[{index}]") is { } difference)
                        return difference;
                }

                return null;
            case (IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right):
                if (left.Count != right.Count)
                    return $"{path}: keys [{string.Join(',', left.Keys.Order(StringComparer.Ordinal))}] vs "
                        + $"[{string.Join(',', right.Keys.Order(StringComparer.Ordinal))}]";
                foreach ((string key, object? value) in left)
                {
                    if (!right.TryGetValue(key, out object? other))
                        return $"{path}.{key}: missing";
                    if (Difference(value, other, $"{path}.{key}") is { } difference)
                        return difference;
                }

                return null;
            default:
                return Equals(expected, actual)
                    ? null
                    : $"{path}: {Describe(expected)} vs {Describe(actual)}";
        }
    }

    private static string Describe(object? value) => value is null ? "null" : $"{value.GetType().Name} {value}";
}
