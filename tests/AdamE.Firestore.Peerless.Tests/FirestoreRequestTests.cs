using System.Text.Json;
using AdamE.Firestore.Peerless.macOS;
using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

public sealed class FirestoreRequestTests
{
    public static TheoryData<FirestoreWrite> InvalidWrites => new()
    {
        new FirestoreWrite("items/a", FirestoreWriteKind.Update, [Field("count")], [Increment("count")]),
        new FirestoreWrite("items/a", FirestoreWriteKind.Update, [Field("count")], ServerTimestamps: ["count"]),
        new FirestoreWrite("items/a", FirestoreWriteKind.Update, Increments: [Increment("count")], ServerTimestamps: ["count"]),
        new FirestoreWrite("items/a", FirestoreWriteKind.Update, [Field("count"), Field("count")]),
        new FirestoreWrite("items/a", FirestoreWriteKind.Delete, [Field("count")]),
        new FirestoreWrite("items/a", FirestoreWriteKind.Delete, Increments: [Increment("count")]),
        new FirestoreWrite("items/a", FirestoreWriteKind.Delete, ServerTimestamps: ["count"]),
    };

    [Theory]
    [MemberData(nameof(InvalidWrites), DisableDiscoveryEnumeration = true)]
    public void Rejects_ambiguous_or_discarded_fields(FirestoreWrite write) =>
        Assert.Throws<ArgumentException>(() => FirestoreRequest.Batch([write]));

    [Fact]
    public void Encodes_distinct_fields_and_transforms_and_allows_an_empty_delete()
    {
        var update = new FirestoreWrite("items/a", FirestoreWriteKind.Update,
            [Field("address.score")], [Increment("count")], ["seen"]);
        var delete = new FirestoreWrite("items/b", FirestoreWriteKind.Delete);

        using var json = JsonDocument.Parse(FirestoreRequest.Batch([update, delete]));
        var fields = json.RootElement[0].GetProperty("fields");
        Assert.Equal(5, fields.GetProperty("address.score").GetInt64());
        Assert.Equal(1, fields.GetProperty("count").GetProperty("increment").GetInt64());
        Assert.True(fields.GetProperty("seen").GetProperty("serverTimestamp").GetBoolean());
        Assert.Equal("delete", json.RootElement[1].GetProperty("kind").GetString());
        Assert.Empty(json.RootElement[1].GetProperty("fields").EnumerateObject());
    }

    [Fact]
    public void Rejects_descending_order_without_an_order_field() =>
        Assert.Throws<ArgumentException>(() => FirestoreRequest.Query(new FirestoreQuery([], Descending: true)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_nonpositive_limits(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FirestoreRequest.Query(new FirestoreQuery([], Limit: limit)));

    [Fact]
    public void Encodes_descending_order_and_the_smallest_valid_limit()
    {
        using var json = JsonDocument.Parse(FirestoreRequest.Query(
            new FirestoreQuery([], OrderBy: "address.score", Descending: true, Limit: 1)));

        Assert.Equal("address.score", json.RootElement.GetProperty("order").GetString());
        Assert.True(json.RootElement.GetProperty("descending").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void Encodes_a_query_with_neither_order_nor_limit()
    {
        using var json = JsonDocument.Parse(FirestoreRequest.Query(new FirestoreQuery([])));

        Assert.False(json.RootElement.TryGetProperty("order", out _));
        Assert.False(json.RootElement.TryGetProperty("descending", out _));
        Assert.False(json.RootElement.TryGetProperty("limit", out _));
    }

    private static KeyValuePair<string, FirestoreValue> Field(string name) =>
        KeyValuePair.Create(name, FirestoreValue.FromInteger(5));

    private static KeyValuePair<string, long> Increment(string name) => KeyValuePair.Create(name, 1L);
}
