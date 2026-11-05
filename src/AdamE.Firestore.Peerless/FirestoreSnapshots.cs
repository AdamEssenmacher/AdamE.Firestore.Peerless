namespace AdamE.Firestore.Peerless;

/// <summary>Where a snapshot's data came from, as Firestore reports it.</summary>
/// <param name="IsFromCache">The data came from the local cache and may not match the server yet.</param>
/// <param name="HasPendingWrites">The data includes local writes the server has not acknowledged.</param>
public readonly record struct SnapshotMetadata(bool IsFromCache, bool HasPendingWrites);

/// <summary>One document, decoded.</summary>
public sealed class FirestoreDocument
{
    /// <summary>Creates a document. A document that does not exist must have empty <paramref name="fields"/>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is not a document path: it is empty, has an empty segment (a leading, trailing or doubled
    /// <c>/</c>), or has an odd number of segments (a collection path).
    /// </exception>
    public FirestoreDocument(string path, bool exists, SnapshotMetadata metadata, FirestoreMap fields)
        : this(path, exists, metadata, fields, validated: false)
    {
    }

    internal FirestoreDocument(string path, bool exists, SnapshotMetadata metadata, FirestoreMap fields, bool validated)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(fields);
        if (!validated && !IsValidPath(path))
            throw new ArgumentException($"\"{path}\" is not a document path: it is empty, has an empty segment or has an odd number of segments.", nameof(path));
        if (!exists && fields.Count != 0)
            throw new ArgumentException("A document that does not exist has no fields.", nameof(fields));

        Path = path;
        Exists = exists;
        Metadata = metadata;
        Fields = fields;
    }

    // A document path: non-empty, no empty segment (no leading or trailing '/', no "//"), and an even number of
    // segments (collection/document pairs), so a collection path such as "rooms" or "rooms/a/messages" is refused.
    internal static bool IsValidPath(string path) =>
        path.Length > 0 && path[0] != '/' && path[^1] != '/' && !path.Contains("//", StringComparison.Ordinal)
        && path.AsSpan().Count('/') % 2 == 1;

    /// <summary>The document's full path, for example <c>rooms/abc/messages/1</c>.</summary>
    public string Path { get; }

    /// <summary>The last segment of <see cref="Path"/>.</summary>
    public string Id => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>Whether the document exists. Only a document snapshot can report one that does not.</summary>
    public bool Exists { get; }

    /// <summary>This document's own metadata.</summary>
    public SnapshotMetadata Metadata { get; }

    /// <summary>The document's fields; empty when it does not exist.</summary>
    public FirestoreMap Fields { get; }

    /// <inheritdoc />
    public override string ToString() => Exists ? $"{Path} {Fields}" : $"{Path} (missing)";
}

/// <summary>A decoded snapshot: either a <see cref="FirestoreQuerySnapshot"/> or a <see cref="FirestoreDocumentSnapshot"/>.</summary>
public abstract class FirestoreSnapshot
{
    private protected FirestoreSnapshot(SnapshotMetadata metadata) => Metadata = metadata;

    /// <summary>The snapshot's metadata.</summary>
    public SnapshotMetadata Metadata { get; }
}

/// <summary>The result of a query listener: every matching document, in query order.</summary>
public sealed class FirestoreQuerySnapshot : FirestoreSnapshot
{
    /// <summary>Creates a query snapshot from <paramref name="documents"/>, which are copied.</summary>
    public FirestoreQuerySnapshot(SnapshotMetadata metadata, IEnumerable<FirestoreDocument> documents)
        : this(metadata, [.. documents ?? throw new ArgumentNullException(nameof(documents))])
    {
    }

    // Takes ownership of an array nothing else holds.
    internal FirestoreQuerySnapshot(SnapshotMetadata metadata, FirestoreDocument[] documents)
        : base(metadata) =>
        Documents = Array.AsReadOnly(documents);

    /// <summary>The documents, in query order.</summary>
    public IReadOnlyList<FirestoreDocument> Documents { get; }
}

/// <summary>The result of a document listener.</summary>
public sealed class FirestoreDocumentSnapshot : FirestoreSnapshot
{
    /// <summary>Creates a document snapshot.</summary>
    public FirestoreDocumentSnapshot(SnapshotMetadata metadata, FirestoreDocument document)
        : base(metadata) =>
        Document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>The document; check <see cref="FirestoreDocument.Exists"/>.</summary>
    public FirestoreDocument Document { get; }
}

/// <summary>A frame that does not follow the Peerless wire format (see <c>docs/wire-format.md</c>).</summary>
public sealed class PeerlessFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    public PeerlessFormatException(string message, int offset)
        : base($"{message} (at byte {offset})") => Offset = offset;

    /// <summary>The byte offset in the frame where decoding failed.</summary>
    public int Offset { get; }
}
