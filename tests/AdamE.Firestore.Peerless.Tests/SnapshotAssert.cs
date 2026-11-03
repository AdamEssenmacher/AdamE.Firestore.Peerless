using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class FixturesDirectoryAttribute(string path) : Attribute
{
    public string Path { get; } = path;

    public static string Resolve() =>
        typeof(FixturesDirectoryAttribute).Assembly
            .GetCustomAttributes(typeof(FixturesDirectoryAttribute), false)
            .OfType<FixturesDirectoryAttribute>()
            .Single()
            .Path;
}

internal static class SnapshotAssert
{
    public static void Equal(FirestoreSnapshot expected, FirestoreSnapshot actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Metadata, actual.Metadata);
        IReadOnlyList<FirestoreDocument> expectedDocuments = Documents(expected);
        IReadOnlyList<FirestoreDocument> actualDocuments = Documents(actual);
        Assert.Equal(expectedDocuments.Count, actualDocuments.Count);
        for (int index = 0; index < expectedDocuments.Count; index++)
        {
            FirestoreDocument want = expectedDocuments[index];
            FirestoreDocument got = actualDocuments[index];
            Assert.Equal(want.Path, got.Path);
            Assert.Equal(want.Exists, got.Exists);
            Assert.Equal(want.Metadata, got.Metadata);
            Assert.True(want.Fields.Equals(got.Fields), $"Fields differ.\nExpected: {want.Fields}\nActual:   {got.Fields}");
        }
    }

    public static IReadOnlyList<FirestoreDocument> Documents(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => query.Documents,
        FirestoreDocumentSnapshot document => [document.Document],
        _ => throw new ArgumentException("Unknown snapshot type.", nameof(snapshot)),
    };
}
