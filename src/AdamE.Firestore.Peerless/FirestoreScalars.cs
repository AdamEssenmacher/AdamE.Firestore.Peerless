using System.Globalization;

namespace AdamE.Firestore.Peerless;

/// <summary>A Firestore timestamp: seconds since the Unix epoch plus nanoseconds, exactly as stored.</summary>
/// <remarks>
/// Only Firestore's range is accepted: 0001-01-01T00:00:00Z to 9999-12-31T23:59:59.999999999Z, the same range the
/// decoder enforces. <c>default</c> is the Unix epoch.
/// </remarks>
public readonly record struct FirestoreTimestamp
{
    /// <summary>The earliest second Firestore stores: 0001-01-01T00:00:00Z.</summary>
    public const long MinSeconds = -62_135_596_800;

    /// <summary>The latest second Firestore stores: 9999-12-31T23:59:59Z.</summary>
    public const long MaxSeconds = 253_402_300_799;

    /// <summary>Creates a timestamp.</summary>
    /// <param name="seconds">Seconds since 1970-01-01T00:00:00Z, from <see cref="MinSeconds"/> to <see cref="MaxSeconds"/>.</param>
    /// <param name="nanoseconds">Nanoseconds within the second, from 0 to 999,999,999.</param>
    /// <exception cref="ArgumentOutOfRangeException">The timestamp is outside Firestore's range.</exception>
    public FirestoreTimestamp(long seconds, int nanoseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, MinSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, MaxSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(nanoseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nanoseconds, 999_999_999);
        Seconds = seconds;
        Nanoseconds = nanoseconds;
    }

    /// <summary>Seconds since 1970-01-01T00:00:00Z.</summary>
    public long Seconds { get; }

    /// <summary>Nanoseconds within the second, from 0 to 999,999,999.</summary>
    public int Nanoseconds { get; }

    internal static bool IsValid(long seconds, int nanoseconds) =>
        seconds is >= MinSeconds and <= MaxSeconds && nanoseconds is >= 0 and <= 999_999_999;

    /// <summary>Deconstructs into seconds and nanoseconds.</summary>
    public void Deconstruct(out long seconds, out int nanoseconds)
    {
        seconds = Seconds;
        nanoseconds = Nanoseconds;
    }

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
