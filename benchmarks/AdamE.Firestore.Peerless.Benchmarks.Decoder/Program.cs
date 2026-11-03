using System.Diagnostics;
using AdamE.Firestore.Peerless;
using AdamE.Firestore.Peerless.Tests;

// Best-of-5 time per decode and allocation per decode, for frames shaped like the review's regression cases. Host
// load moves the timings a lot on a shared machine: compare runs taken back to back, not across days.
var live = new SnapshotMetadata(false, false);

static FirestoreMap Fields(int count, Func<int, FirestoreValue> value) =>
    new(Enumerable.Range(0, count).Select(index => KeyValuePair.Create($"field-{index:D2}", value(index))));

FirestoreDocument[] typical = [.. Enumerable.Range(0, 100).Select(document => new FirestoreDocument(
    $"items/doc-{document:D5}", true, live, Fields(20, field => (field % 4) switch
    {
        0 => FirestoreValue.FromString($"value {document} {field}"),
        1 => FirestoreValue.FromInteger(document * 1000 + field),
        2 => FirestoreValue.FromDouble(document + field / 10.0),
        _ => FirestoreValue.FromArray(new FirestoreArray([FirestoreValue.FromString("a"), FirestoreValue.FromInteger(field)])),
    })))];
FirestoreValue nulls = FirestoreValue.FromArray(new FirestoreArray(Enumerable.Repeat(FirestoreValue.Null, 131_072)));
FirestoreValue wide = FirestoreValue.FromMap(Fields(4097, static index => FirestoreValue.FromInteger(index)));

(string Name, byte[] Frame, int Iterations)[] cases =
[
    ("100 docs x 20 fields", TestFrameEncoder.Encode(new FirestoreQuerySnapshot(live, typical)), 4000),
    ("131k nulls", Single("f", nulls), 400),
    ("map of 4097 keys", Single("m", wide), 2000),
];

foreach ((string name, byte[] frame, int iterations) in cases)
{
    for (int warm = 0; warm < iterations / 4; warm++)
        _ = SnapshotDecoder.Decode(frame);

    double best = double.MaxValue;
    long allocated = 0;
    for (int round = 0; round < 5; round++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int iteration = 0; iteration < iterations; iteration++)
            _ = SnapshotDecoder.Decode(frame);
        clock.Stop();
        allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        best = Math.Min(best, clock.Elapsed.TotalMicroseconds / iterations);
    }

    Console.WriteLine($"{name,-22} frame {frame.Length,9:N0} B  {best,10:N1} us  {allocated / 1024.0,10:N1} KB");
}

byte[] Single(string key, FirestoreValue value) => TestFrameEncoder.Encode(new FirestoreDocumentSnapshot(live,
    new FirestoreDocument("a/1", true, live, new FirestoreMap([KeyValuePair.Create(key, value)]))));
