using System.Globalization;
using System.Text;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Writes the run as it goes, flushing every line, so a crash part-way through still leaves everything measured so far:
/// <c>{mode}-samples.csv</c> (one row per callback), <c>{mode}-settled.csv</c> (peer counts after forced GCs) and
/// <c>{mode}-run.txt</c> (environment and outcome), plus <c>{mode}-console.txt</c> (progress and settled peer kinds,
/// for the host's log; not committed). Android's columns, with NSObject peers in place of JNI references.
/// </summary>
internal sealed class ResultWriter : IDisposable
{
    private readonly StreamWriter samples;
    private readonly StreamWriter settled;
    private readonly StreamWriter run;
    private readonly StreamWriter console;

    public ResultWriter(string directory, string mode)
    {
        _ = Directory.CreateDirectory(directory);
        samples = Open(Path.Combine(directory, $"{mode}-samples.csv"));
        settled = Open(Path.Combine(directory, $"{mode}-settled.csv"));
        run = Open(Path.Combine(directory, $"{mode}-run.txt"));
        console = Open(Path.Combine(directory, $"{mode}-console.txt"));
        Line(samples,
            "phase,size,seq,kind,from_cache,documents,values,main_thread,wall_us,thread_cpu_us,alloc_bytes,"
            + "peers_before,peers_after,peers_delta,peers_peak,peers_peak_delta,peers_created,gen0,gen1,gen2,gc_pause_us,write_to_callback_ms,"
            + "native_to_dotnet_us,native_encode_us,native_encode_cpu_us,native_data_us,dotnet_us,frame_bytes");
        Line(settled, "phase,size,point,peers,peers_alive,heap_bytes,largest_kinds");
    }

    public void Note(string key, string value) => Line(run, $"{key}: {value}");

    /// <summary>A progress line, timestamped, for the console log.</summary>
    public void Log(string message) => Line(console, $"{DateTime.UtcNow:HH:mm:ss.fff} {message}");

    public void Sample(string phase, int size, int sequence, string kind, Sample sample, double? writeToDoneMilliseconds) =>
        Line(samples, string.Join(',',
            phase,
            Invariant(size),
            Invariant(sequence),
            kind,
            sample.FromCache ? "1" : "0",
            Invariant(sample.Documents),
            Invariant(sample.Values),
            sample.OnMainThread ? "1" : "0",
            Invariant(sample.WallMicroseconds),
            Invariant(sample.ThreadCpuMicroseconds),
            Invariant(sample.AllocatedBytes),
            Invariant(sample.PeersBefore),
            Invariant(sample.PeersAfter),
            Invariant(sample.PeersAfter - sample.PeersBefore),
            Invariant(sample.PeersPeak),
            Invariant(sample.PeersPeak - sample.PeersBefore),
            Invariant(sample.PeersCreated),
            Invariant(sample.Gen0Collections),
            Invariant(sample.Gen1Collections),
            Invariant(sample.Gen2Collections),
            Invariant(sample.GcPauseMicroseconds),
            writeToDoneMilliseconds is { } latency ? Invariant(latency) : string.Empty,
            sample.Native is { } toDotnet ? Invariant(toDotnet.ToDotnetMicroseconds) : string.Empty,
            sample.Native is { } encode ? Invariant(encode.EncodeMicroseconds) : string.Empty,
            sample.Native is { } encodeCpu ? Invariant(encodeCpu.EncodeCpuMicroseconds) : string.Empty,
            sample.Native is { } data ? Invariant(data.DataMicroseconds) : string.Empty,
            sample.Native is { } dotnet ? Invariant(dotnet.DotnetMicroseconds) : string.Empty,
            sample.Native is { } frame ? Invariant(frame.FrameBytes) : string.Empty));

    public void Settled(string phase, int size, string point, SettledReading reading) =>
        Line(settled, string.Join(',',
            phase,
            Invariant(size),
            point,
            Invariant(reading.Peers),
            Invariant(reading.AlivePeers),
            Invariant(reading.HeapBytes),
            reading.LargestKinds.Replace(',', ' ')));

    public void Dispose()
    {
        samples.Dispose();
        settled.Dispose();
        run.Dispose();
        console.Dispose();
    }

    private static StreamWriter Open(string path) => new(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void Line(StreamWriter writer, string line)
    {
        lock (this)
            writer.WriteLine(line);
    }
}
