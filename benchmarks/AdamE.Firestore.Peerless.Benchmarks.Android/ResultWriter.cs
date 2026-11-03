using System.Globalization;
using System.Text;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Writes the run as it goes, flushing every line, so a crash part-way through still leaves everything measured so far:
/// <c>{mode}-samples.csv</c> (one row per callback), <c>{mode}-settled.csv</c> (reference counts after forced GCs)
/// and <c>{mode}-run.txt</c> (environment and outcome).
/// </summary>
internal sealed class ResultWriter : IDisposable
{
    private readonly StreamWriter samples;
    private readonly StreamWriter settled;
    private readonly StreamWriter run;

    public ResultWriter(string directory, string mode)
    {
        samples = Open(Path.Combine(directory, $"{mode}-samples.csv"));
        settled = Open(Path.Combine(directory, $"{mode}-settled.csv"));
        run = Open(Path.Combine(directory, $"{mode}-run.txt"));
        Line(samples,
            "phase,size,seq,kind,from_cache,documents,values,main_thread,wall_us,thread_cpu_us,alloc_bytes,"
            + "gref_before,gref_after,gref_delta,gref_peak,gref_peak_delta,wgref_before,wgref_after,gen0,gen1,gen2,gc_pause_us,write_to_callback_ms,"
            + "java_to_dotnet_us,java_encode_us,java_encode_cpu_us,dotnet_us,frame_bytes");
        Line(settled, "phase,size,point,gref,wgref,surfaced_peers");
    }

    public void Note(string key, string value) => Line(run, $"{key}: {value}");

    public void Sample(string phase, int size, int sequence, string kind, Sample sample, double? writeToCallbackMilliseconds) =>
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
            Invariant(sample.GlobalReferencesBefore),
            Invariant(sample.GlobalReferencesAfter),
            Invariant(sample.GlobalReferencesAfter - sample.GlobalReferencesBefore),
            Invariant(sample.GlobalReferencesPeak),
            Invariant(sample.GlobalReferencesPeak - sample.GlobalReferencesBefore),
            Invariant(sample.WeakGlobalReferencesBefore),
            Invariant(sample.WeakGlobalReferencesAfter),
            Invariant(sample.Gen0Collections),
            Invariant(sample.Gen1Collections),
            Invariant(sample.Gen2Collections),
            Invariant(sample.GcPauseMicroseconds),
            writeToCallbackMilliseconds is { } latency ? Invariant(latency) : string.Empty,
            sample.Java is { } java ? Invariant(java.ToDotnetMicroseconds) : string.Empty,
            sample.Java is { } encode ? Invariant(encode.EncodeMicroseconds) : string.Empty,
            sample.Java is { } encodeCpu ? Invariant(encodeCpu.EncodeCpuMicroseconds) : string.Empty,
            sample.Java is { } dotnet ? Invariant(dotnet.DotnetMicroseconds) : string.Empty,
            sample.Java is { } frame ? Invariant(frame.FrameBytes) : string.Empty));

    public void Settled(string phase, int size, string point, int global, int weak, int peers) =>
        Line(settled, string.Join(',', phase, Invariant(size), point, Invariant(global), Invariant(weak), Invariant(peers)));

    public void Dispose()
    {
        samples.Dispose();
        settled.Dispose();
        run.Dispose();
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
