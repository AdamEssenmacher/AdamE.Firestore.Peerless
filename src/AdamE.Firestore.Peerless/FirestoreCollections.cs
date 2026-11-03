using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace AdamE.Firestore.Peerless;

/// <summary>An immutable, ordered Firestore array.</summary>
public sealed class FirestoreArray : IReadOnlyList<FirestoreValue>, IEquatable<FirestoreArray>
{
    private readonly FirestoreValue[] items;

    /// <summary>Creates an array from <paramref name="items"/>, which are copied.</summary>
    public FirestoreArray(IEnumerable<FirestoreValue> items) => this.items = [.. items];

    private FirestoreArray(FirestoreValue[] items, bool _) => this.items = items;

    /// <summary>The empty array.</summary>
    public static FirestoreArray Empty { get; } = new([], false);

    // Takes ownership of an array nothing else holds.
    internal static FirestoreArray Owning(FirestoreValue[] items) => items.Length == 0 ? Empty : new(items, false);

    /// <inheritdoc />
    public int Count => items.Length;

    /// <inheritdoc />
    public FirestoreValue this[int index] => items[index];

    /// <summary>The items as a span, without copying.</summary>
    public ReadOnlySpan<FirestoreValue> AsSpan() => items;

    /// <summary>Returns an allocation-free enumerator.</summary>
    public ReadOnlySpan<FirestoreValue>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();

    IEnumerator<FirestoreValue> IEnumerable<FirestoreValue>.GetEnumerator() => ((IEnumerable<FirestoreValue>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

    /// <inheritdoc />
    public bool Equals(FirestoreArray? other) =>
        other is not null && (ReferenceEquals(this, other) || items.AsSpan().SequenceEqual(other.items));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FirestoreArray);

    /// <inheritdoc />
    public override int GetHashCode() => items.Length;

    /// <inheritdoc />
    public override string ToString() => "[" + string.Join(", ", items) + "]";
}

/// <summary>
/// An immutable Firestore map (also used for a document's fields). Key order is whatever the native SDK produced and
/// carries no meaning; equality ignores it.
/// </summary>
public sealed class FirestoreMap : IReadOnlyDictionary<string, FirestoreValue>, IEquatable<FirestoreMap>
{
    // Small maps are searched linearly; larger ones get an index built on first lookup.
    private const int LinearSearchLimit = 8;

    private readonly string[] keys;
    private readonly FirestoreValue[] values;
    private Dictionary<string, int>? index;

    /// <summary>Creates a map from <paramref name="entries"/>. Duplicate keys throw.</summary>
    public FirestoreMap(IEnumerable<KeyValuePair<string, FirestoreValue>> entries)
    {
        var copy = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
        foreach ((string key, FirestoreValue value) in entries)
            copy.Add(key ?? throw new ArgumentException("A map key cannot be null.", nameof(entries)), value);
        keys = [.. copy.Keys];
        values = [.. copy.Values];
    }

    private FirestoreMap(string[] keys, FirestoreValue[] values)
    {
        this.keys = keys;
        this.values = values;
    }

    /// <summary>The empty map.</summary>
    public static FirestoreMap Empty { get; } = new([], []);

    // Takes ownership of arrays nothing else holds; keys are assumed unique (see docs/wire-format.md).
    internal static FirestoreMap Owning(string[] keys, FirestoreValue[] values) =>
        keys.Length == 0 ? Empty : new(keys, values);

    /// <inheritdoc />
    public int Count => keys.Length;

    /// <inheritdoc />
    public FirestoreValue this[string key] => TryGetValue(key, out FirestoreValue value)
        ? value
        : throw new KeyNotFoundException($"The map has no field \"{key}\".");

    /// <summary>The keys, in the order the native SDK produced them.</summary>
    public IEnumerable<string> Keys => keys;

    /// <summary>The values, in the same order as <see cref="Keys"/>.</summary>
    public IEnumerable<FirestoreValue> Values => values;

    /// <inheritdoc />
    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    /// <inheritdoc />
    public bool TryGetValue(string key, out FirestoreValue value)
    {
        int position = IndexOf(key);
        value = position >= 0 ? values[position] : default;
        return position >= 0;
    }

    /// <summary>
    /// Gets the value at a dotted field path such as <c>stats.views</c>, walking nested maps. Returns
    /// <see langword="false"/> if any segment is missing or is not a map.
    /// </summary>
    /// <remarks>
    /// The path is split on every <c>.</c>, so this overload can't reach a key that itself contains a dot; use
    /// <see cref="TryGetPath(ReadOnlySpan{string}, out FirestoreValue)"/> for those.
    /// </remarks>
    public bool TryGetPath(string path, out FirestoreValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        FirestoreMap current = this;
        int start = 0;
        while (true)
        {
            int dot = path.IndexOf('.', start);
            string segment = dot < 0 ? path[start..] : path[start..dot];
            if (!current.TryGetValue(segment, out value))
                return false;
            if (dot < 0)
                return true;
            if (!value.TryGetMap(out FirestoreMap? next))
            {
                value = default;
                return false;
            }

            current = next;
            start = dot + 1;
        }
    }

    /// <summary>
    /// Gets the value at a field path given as separate segments, such as <c>["stats", "views"]</c>, walking nested
    /// maps. Segments may contain any character, including <c>.</c>. Returns <see langword="false"/> if any segment is
    /// missing or is not a map, or if there are no segments.
    /// </summary>
    public bool TryGetPath(ReadOnlySpan<string> segments, out FirestoreValue value)
    {
        value = default;
        FirestoreMap current = this;
        for (int index = 0; index < segments.Length; index++)
        {
            if (!current.TryGetValue(segments[index], out value))
                return false;
            if (index == segments.Length - 1)
                return true;
            if (!value.TryGetMap(out FirestoreMap? next))
            {
                value = default;
                return false;
            }

            current = next;
        }

        return false;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, FirestoreValue>> GetEnumerator()
    {
        for (int position = 0; position < keys.Length; position++)
            yield return new KeyValuePair<string, FirestoreValue>(keys[position], values[position]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(FirestoreMap? other)
    {
        if (other is null || other.Count != Count)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        for (int position = 0; position < keys.Length; position++)
        {
            if (!other.TryGetValue(keys[position], out FirestoreValue value) || !values[position].Equals(value))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FirestoreMap);

    /// <inheritdoc />
    public override int GetHashCode() => keys.Length;

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new StringBuilder("{");
        for (int position = 0; position < keys.Length; position++)
            text.Append(position == 0 ? "" : ", ").Append(keys[position]).Append(": ").Append(values[position]);
        return text.Append('}').ToString();
    }

    private int IndexOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (keys.Length <= LinearSearchLimit)
        {
            for (int position = 0; position < keys.Length; position++)
            {
                if (string.Equals(keys[position], key, StringComparison.Ordinal))
                    return position;
            }

            return -1;
        }

        return (index ??= BuildIndex()).TryGetValue(key, out int found) ? found : -1;
    }

    // Races are harmless: every thread builds an identical index.
    private Dictionary<string, int> BuildIndex()
    {
        var built = new Dictionary<string, int>(keys.Length, StringComparer.Ordinal);
        for (int position = 0; position < keys.Length; position++)
            built.TryAdd(keys[position], position);
        return built;
    }
}
