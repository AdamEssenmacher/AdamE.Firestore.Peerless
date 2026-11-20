using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AdamE.Firestore.Peerless.macOS;

public sealed record FirestoreQuery(
    IReadOnlyList<KeyValuePair<string, FirestoreValue>> Filters,
    string? OrderBy = null,
    bool Descending = false,
    int? Limit = null);

public enum FirestoreWriteKind { Set, Merge, Update, Delete }

public sealed record FirestoreWrite(
    string DocumentPath,
    FirestoreWriteKind Kind,
    IReadOnlyList<KeyValuePair<string, FirestoreValue>>? Fields = null,
    IReadOnlyList<KeyValuePair<string, long>>? Increments = null,
    IReadOnlyList<string>? ServerTimestamps = null);

internal static class FirestoreRequest
{
    public static string Query(FirestoreQuery query) => Encode(writer =>
    {
        writer.WriteStartObject();
        writer.WriteStartArray("filters");
        foreach (var (field, value) in query.Filters)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(field);
            WriteValue(writer, value);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        if (query.OrderBy is { } order)
        {
            writer.WriteString("order", order);
            writer.WriteBoolean("descending", query.Descending);
        }
        if (query.Limit is { } limit)
            writer.WriteNumber("limit", limit);
        writer.WriteEndObject();
    });

    public static string Batch(IReadOnlyList<FirestoreWrite> writes) => Encode(writer =>
    {
        writer.WriteStartArray();
        foreach (var write in writes)
        {
            writer.WriteStartObject();
            writer.WriteString("path", write.DocumentPath);
            writer.WriteString("kind", write.Kind switch
            {
                FirestoreWriteKind.Set => "set",
                FirestoreWriteKind.Merge => "merge",
                FirestoreWriteKind.Update => "update",
                FirestoreWriteKind.Delete => "delete",
                _ => throw new ArgumentOutOfRangeException(nameof(writes)),
            });
            writer.WriteStartObject("fields");
            foreach (var (name, value) in write.Fields ?? [])
            {
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }
            foreach (var (name, increment) in write.Increments ?? [])
            {
                writer.WriteStartObject(name);
                writer.WriteNumber("increment", increment);
                writer.WriteEndObject();
            }
            foreach (var name in write.ServerTimestamps ?? [])
            {
                writer.WriteStartObject(name);
                writer.WriteBoolean("serverTimestamp", true);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    });

    private static string Encode(Action<Utf8JsonWriter> write)
    {
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes))
            write(writer);
        return Encoding.UTF8.GetString(bytes.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, FirestoreValue value)
    {
        switch (value.Kind)
        {
            case FirestoreValueKind.Null:
                writer.WriteNullValue();
                break;
            case FirestoreValueKind.Boolean:
                writer.WriteBooleanValue(value.AsBoolean());
                break;
            case FirestoreValueKind.Integer:
                writer.WriteNumberValue(value.AsInteger());
                break;
            case FirestoreValueKind.String:
                writer.WriteStringValue(value.AsString());
                break;
            case FirestoreValueKind.Double:
                TaggedText(writer, "double", value.AsDouble().ToString("R", CultureInfo.InvariantCulture));
                break;
            case FirestoreValueKind.Reference:
                TaggedText(writer, "reference", value.AsReferencePath());
                break;
            case FirestoreValueKind.Timestamp:
                var timestamp = value.AsTimestamp();
                writer.WriteStartObject();
                writer.WriteStartArray("timestamp");
                writer.WriteNumberValue(timestamp.Seconds);
                writer.WriteNumberValue(timestamp.Nanoseconds);
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case FirestoreValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.AsArray())
                    WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                throw new NotSupportedException($"Cannot send a Firestore {value.Kind} value.");
        }
    }

    private static void TaggedText(Utf8JsonWriter writer, string name, string text)
    {
        writer.WriteStartObject();
        writer.WriteString(name, text);
        writer.WriteEndObject();
    }
}
