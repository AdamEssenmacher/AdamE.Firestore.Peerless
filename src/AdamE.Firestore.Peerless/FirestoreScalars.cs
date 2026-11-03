using System.Globalization;

namespace AdamE.Firestore.Peerless;

/// <summary>A Firestore timestamp: seconds since the Unix epoch plus nanoseconds, exactly as stored.</summary>
/// <param name="Seconds">Seconds since 1970-01-01T00:00:00Z.</param>
/// <param name="Nanoseconds">Nanoseconds within the second, from 0 to 999,999,999.</param>
public readonly record struct FirestoreTimestamp(long Seconds, int Nanoseconds)
{
    /// <summary>
    /// Converts to a <see cref="DateTimeOffset"/> in UTC. Precision below 100 ns (one tick) is truncated.
    /// </summary>
    public DateTimeOffset ToDateTimeOffset() =>
        DateTimeOffset.UnixEpoch.AddSeconds(Seconds).AddTicks(Nanoseconds / 100);

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Seconds}s+{Nanoseconds}ns");
}

/// <summary>A Firestore geo point.</summary>
/// <param name="Latitude">Latitude in degrees, from -90 to 90.</param>
/// <param name="Longitude">Longitude in degrees, from -180 to 180.</param>
/// <remarks>
/// Equality compares the doubles' bits, like <see cref="FirestoreValue"/>: NaN equals the same NaN, and 0.0 differs
/// from -0.0.
/// </remarks>
public readonly record struct FirestoreGeoPoint(double Latitude, double Longitude)
{
    /// <inheritdoc />
    public bool Equals(FirestoreGeoPoint other) =>
        BitConverter.DoubleToInt64Bits(Latitude) == BitConverter.DoubleToInt64Bits(other.Latitude)
        && BitConverter.DoubleToInt64Bits(Longitude) == BitConverter.DoubleToInt64Bits(other.Longitude);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(BitConverter.DoubleToInt64Bits(Latitude), BitConverter.DoubleToInt64Bits(Longitude));

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({Latitude}, {Longitude})");
}
