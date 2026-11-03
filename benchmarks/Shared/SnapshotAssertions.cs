namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Compares two snapshots in the Peerless model exactly: how the peered and Peerless paths are checked.</summary>
internal static class SnapshotAssertions
{
    public static int DocumentCount(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => query.Documents.Count,
        _ => 1,
    };

    /// <summary>Describes the first difference between two snapshots, or returns null when they are equal.</summary>
    public static string? Difference(FirestoreSnapshot expected, FirestoreSnapshot actual)
    {
        if (expected.Metadata != actual.Metadata)
            return $"metadata {expected.Metadata} vs {actual.Metadata}";
        IReadOnlyList<FirestoreDocument> left = Documents(expected);
        IReadOnlyList<FirestoreDocument> right = Documents(actual);
        if (left.Count != right.Count)
            return $"{left.Count} vs {right.Count} documents";
        for (int index = 0; index < left.Count; index++)
        {
            FirestoreDocument a = left[index];
            FirestoreDocument b = right[index];
            if (a.Path != b.Path || a.Exists != b.Exists || a.Metadata != b.Metadata)
                return $"document {index}: {a.Path}/{a.Exists}/{a.Metadata} vs {b.Path}/{b.Exists}/{b.Metadata}";
            if (!a.Fields.Equals(b.Fields))
                return $"document {a.Path} fields:\n  peered   {a.Fields}\n  peerless {b.Fields}";
        }

        return null;
    }

    private static IReadOnlyList<FirestoreDocument> Documents(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => query.Documents,
        FirestoreDocumentSnapshot document => [document.Document],
        _ => [],
    };
}
