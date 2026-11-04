using System.Globalization;

namespace AdamE.Firestore.Peerless;

/// <summary>The kind of a <see cref="FirestoreValue"/>, matching Firestore's own value types.</summary>
public enum FirestoreValueKind : byte
{
    /// <summary>Firestore <c>null</c>.</summary>
    Null,

    /// <summary><see langword="true"/> or <see langword="false"/>.</summary>
    Boolean,

    /// <summary>A 64-bit integer. Never coerced to or from <see cref="Double"/>.</summary>
    Integer,

    /// <summary>A 64-bit floating-point number. Never coerced to or from <see cref="Integer"/>.</summary>
    Double,

    /// <summary>A string.</summary>
    String,

    /// <summary>A byte array (Firestore <c>Blob</c> / <c>Bytes</c>).</summary>
    Bytes,

    /// <summary>A timestamp with nanosecond precision.</summary>
    Timestamp,

    /// <summary>A latitude/longitude pair.</summary>
    GeoPoint,

    /// <summary>A reference to another document, by path.</summary>
    Reference,

    /// <summary>An ordered list of values.</summary>
    Array,

    /// <summary>A map of string keys to values.</summary>
    Map,

    /// <summary>
    /// A value the native encoder did not recognise, such as a type added in a newer Firestore SDK. Its native type
    /// name is in <see cref="FirestoreValue.UnsupportedTypeName"/>.
    /// </summary>
    Unsupported,
}

/// <summary>
/// One Firestore value, decoded into plain .NET data. A small struct: scalars live inline, so an array or map of them
/// is a single allocation.
/// </summary>
/// <remarks>
/// Equality is structural. Doubles compare by their bits, so a decoded NaN equals the NaN it came from and <c>0.0</c>
/// differs from <c>-0.0</c>. Maps compare without regard to key order.
/// </remarks>
public readonly struct FirestoreValue : IEquatable<FirestoreValue>
{
    private readonly object? reference;
    private readonly long bits;
    private readonly long extra;

    private FirestoreValue(FirestoreValueKind kind, long bits = 0, long extra = 0, object? reference = null)
    {
        Kind = kind;
        this.bits = bits;
        this.extra = extra;
        this.reference = reference;
    }

    /// <summary>The value's kind. <c>default(FirestoreValue)</c> is <see cref="FirestoreValueKind.Null"/>.</summary>
    public FirestoreValueKind Kind { get; }

    /// <summary>Whether this is Firestore <c>null</c>.</summary>
    public bool IsNull => Kind == FirestoreValueKind.Null;

    /// <summary>Firestore <c>null</c>.</summary>
    public static FirestoreValue Null => default;

    /// <summary>A boolean value.</summary>
    public static FirestoreValue FromBoolean(bool value) => new(FirestoreValueKind.Boolean, value ? 1 : 0);

    /// <summary>A 64-bit integer value.</summary>
    public static FirestoreValue FromInteger(long value) => new(FirestoreValueKind.Integer, value);

    /// <summary>A double value.</summary>
    public static FirestoreValue FromDouble(double value) =>
        new(FirestoreValueKind.Double, BitConverter.DoubleToInt64Bits(value));

    /// <summary>A string value.</summary>
    public static FirestoreValue FromString(string value) =>
        new(FirestoreValueKind.String, reference: value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>A bytes value. The bytes are copied.</summary>
    public static FirestoreValue FromBytes(ReadOnlySpan<byte> value) => OwningBytes(value.ToArray());

    /// <summary>A timestamp value.</summary>
    public static FirestoreValue FromTimestamp(FirestoreTimestamp value) =>
        new(FirestoreValueKind.Timestamp, value.Seconds, value.Nanoseconds);

    /// <summary>A geo point value.</summary>
    public static FirestoreValue FromGeoPoint(FirestoreGeoPoint value) => new(
        FirestoreValueKind.GeoPoint,
        BitConverter.DoubleToInt64Bits(value.Latitude),
        BitConverter.DoubleToInt64Bits(value.Longitude));

    /// <summary>A reference to the document at <paramref name="path"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or has an empty segment.</exception>
    public static FirestoreValue FromReference(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!FirestoreDocument.IsValidPath(path))
            throw new ArgumentException($"\"{path}\" is not a document path: it is empty or has an empty segment.", nameof(path));
        return new(FirestoreValueKind.Reference, reference: path);
    }

    /// <summary>An array value.</summary>
    public static FirestoreValue FromArray(FirestoreArray value) =>
        new(FirestoreValueKind.Array, reference: value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>A map value.</summary>
    public static FirestoreValue FromMap(FirestoreMap value) =>
        new(FirestoreValueKind.Map, reference: value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>A value whose native type the encoder did not recognise.</summary>
    public static FirestoreValue Unsupported(string typeName) =>
        new(FirestoreValueKind.Unsupported, reference: typeName ?? throw new ArgumentNullException(nameof(typeName)));

    internal static FirestoreValue OwningBytes(byte[] value) => new(FirestoreValueKind.Bytes, reference: value);

    /// <summary>The boolean. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Boolean"/>.</summary>
    public bool AsBoolean() => Expect(FirestoreValueKind.Boolean).bits != 0;

    /// <summary>The integer. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Integer"/>.</summary>
    public long AsInteger() => Expect(FirestoreValueKind.Integer).bits;

    /// <summary>The double. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Double"/>.</summary>
    public double AsDouble() => BitConverter.Int64BitsToDouble(Expect(FirestoreValueKind.Double).bits);

    /// <summary>The string. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.String"/>.</summary>
    public string AsString() => (string)Expect(FirestoreValueKind.String).Reference();

    /// <summary>The bytes. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Bytes"/>.</summary>
    public ReadOnlyMemory<byte> AsBytes() => (byte[])Expect(FirestoreValueKind.Bytes).Reference();

    /// <summary>The timestamp. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Timestamp"/>.</summary>
    public FirestoreTimestamp AsTimestamp()
    {
        FirestoreValue value = Expect(FirestoreValueKind.Timestamp);
        return new FirestoreTimestamp(value.bits, (int)value.extra);
    }

    /// <summary>The geo point. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.GeoPoint"/>.</summary>
    public FirestoreGeoPoint AsGeoPoint()
    {
        FirestoreValue value = Expect(FirestoreValueKind.GeoPoint);
        return new FirestoreGeoPoint(BitConverter.Int64BitsToDouble(value.bits), BitConverter.Int64BitsToDouble(value.extra));
    }

    /// <summary>The referenced document's path. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Reference"/>.</summary>
    public string AsReferencePath() => (string)Expect(FirestoreValueKind.Reference).Reference();

    /// <summary>The array. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Array"/>.</summary>
    public FirestoreArray AsArray() => (FirestoreArray)Expect(FirestoreValueKind.Array).Reference();

    /// <summary>The map. Throws unless <see cref="Kind"/> is <see cref="FirestoreValueKind.Map"/>.</summary>
    public FirestoreMap AsMap() => (FirestoreMap)Expect(FirestoreValueKind.Map).Reference();

    /// <summary>The native type name of an <see cref="FirestoreValueKind.Unsupported"/> value; otherwise <see langword="null"/>.</summary>
    public string? UnsupportedTypeName => Kind == FirestoreValueKind.Unsupported ? (string?)reference : null;

    /// <summary>Gets the integer if this is one.</summary>
    public bool TryGetInteger(out long value)
    {
        value = Kind == FirestoreValueKind.Integer ? bits : 0;
        return Kind == FirestoreValueKind.Integer;
    }

    /// <summary>Gets the double if this is one. An integer is not a double.</summary>
    public bool TryGetDouble(out double value)
    {
        value = Kind == FirestoreValueKind.Double ? BitConverter.Int64BitsToDouble(bits) : 0;
        return Kind == FirestoreValueKind.Double;
    }

    /// <summary>Gets the boolean if this is one.</summary>
    public bool TryGetBoolean(out bool value)
    {
        value = Kind == FirestoreValueKind.Boolean && bits != 0;
        return Kind == FirestoreValueKind.Boolean;
    }

    /// <summary>Gets the bytes if this is a bytes value.</summary>
    public bool TryGetBytes(out ReadOnlyMemory<byte> value)
    {
        value = Kind == FirestoreValueKind.Bytes ? (byte[]?)reference : default;
        return Kind == FirestoreValueKind.Bytes;
    }

    /// <summary>Gets the timestamp if this is one.</summary>
    public bool TryGetTimestamp(out FirestoreTimestamp value)
    {
        value = Kind == FirestoreValueKind.Timestamp ? AsTimestamp() : default;
        return Kind == FirestoreValueKind.Timestamp;
    }

    /// <summary>Gets the geo point if this is one.</summary>
    public bool TryGetGeoPoint(out FirestoreGeoPoint value)
    {
        value = Kind == FirestoreValueKind.GeoPoint ? AsGeoPoint() : default;
        return Kind == FirestoreValueKind.GeoPoint;
    }

    /// <summary>Gets the referenced document's path if this is a reference.</summary>
    public bool TryGetReferencePath([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        value = Kind == FirestoreValueKind.Reference ? (string?)reference : null;
        return value is not null;
    }

    /// <summary>Gets the string if this is one.</summary>
    public bool TryGetString([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        value = Kind == FirestoreValueKind.String ? (string?)reference : null;
        return value is not null;
    }

    /// <summary>Gets the map if this is one.</summary>
    public bool TryGetMap([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FirestoreMap? value)
    {
        value = Kind == FirestoreValueKind.Map ? (FirestoreMap?)reference : null;
        return value is not null;
    }

    /// <summary>Gets the array if this is one.</summary>
    public bool TryGetArray([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FirestoreArray? value)
    {
        value = Kind == FirestoreValueKind.Array ? (FirestoreArray?)reference : null;
        return value is not null;
    }

    /// <inheritdoc />
    public bool Equals(FirestoreValue other)
    {
        if (Kind != other.Kind || bits != other.bits || extra != other.extra)
            return false;

        return Kind switch
        {
            FirestoreValueKind.String or FirestoreValueKind.Reference or FirestoreValueKind.Unsupported =>
                string.Equals((string?)reference, (string?)other.reference, StringComparison.Ordinal),
            FirestoreValueKind.Bytes => ((byte[]?)reference).AsSpan().SequenceEqual((byte[]?)other.reference),
            FirestoreValueKind.Array or FirestoreValueKind.Map => Equals(reference, other.reference),
            _ => true,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FirestoreValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        FirestoreValueKind.String or FirestoreValueKind.Reference or FirestoreValueKind.Unsupported =>
            HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode((string)Reference())),
        FirestoreValueKind.Bytes => BytesHash((byte[])Reference()),
        FirestoreValueKind.Array or FirestoreValueKind.Map => HashCode.Combine(Kind, Reference()),
        _ => HashCode.Combine(Kind, bits, extra),
    };

    // Length plus up to the first 32 bytes: enough to spread values without hashing a large blob in full.
    private static int BytesHash(byte[] bytes)
    {
        var combined = new HashCode();
        combined.Add(FirestoreValueKind.Bytes);
        combined.Add(bytes.Length);
        combined.AddBytes(bytes.AsSpan(0, Math.Min(bytes.Length, 32)));
        return combined.ToHashCode();
    }

    /// <summary>Whether two values are structurally equal.</summary>
    public static bool operator ==(FirestoreValue left, FirestoreValue right) => left.Equals(right);

    /// <summary>Whether two values differ.</summary>
    public static bool operator !=(FirestoreValue left, FirestoreValue right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        FirestoreValueKind.Null => "null",
        FirestoreValueKind.Boolean => AsBoolean() ? "true" : "false",
        FirestoreValueKind.Integer => bits.ToString(CultureInfo.InvariantCulture),
        FirestoreValueKind.Double => AsDouble().ToString("R", CultureInfo.InvariantCulture) + "d",
        FirestoreValueKind.String => $"\"{reference}\"",
        FirestoreValueKind.Bytes => $"bytes[{((byte[])Reference()).Length}]",
        FirestoreValueKind.Timestamp => AsTimestamp().ToString(),
        FirestoreValueKind.GeoPoint => AsGeoPoint().ToString(),
        FirestoreValueKind.Reference => $"ref({reference})",
        FirestoreValueKind.Array or FirestoreValueKind.Map => Reference().ToString() ?? string.Empty,
        _ => $"unsupported({reference})",
    };

    private FirestoreValue Expect(FirestoreValueKind kind) => Kind == kind
        ? this
        : throw new InvalidOperationException($"The value is {Kind}, not {kind}.");

    // Only called after Expect for a kind that always carries a reference.
    private object Reference() => reference ?? throw new InvalidOperationException("The value has no payload.");
}
