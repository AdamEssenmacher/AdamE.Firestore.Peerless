namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Records a failure before the run could start (bad launch arguments or intent extras) in the files the run scripts
/// read: a run file with the failure and outcome, and the PEERLESS_DONE line they wait for. This is a contract with
/// scripts/run-android-benchmark.sh and scripts/run-ios-benchmark.sh, which grep the run file for
/// <c>failure: startup:</c> and <c>outcome: failed</c> and look for the files under the name they passed, so both
/// platforms write it from here.
/// </summary>
internal static class StartupFailure
{
    /// <summary>The run-file name used when the one the script passed is missing or not a plain mode name.</summary>
    public const string DefaultRunFiles = "peered";

    /// <summary>
    /// The name the run's files go under: the one the script passed (the mode whose files it watches), even when the
    /// mode itself is what failed to parse, provided it is letters, digits and hyphens; otherwise
    /// <see cref="DefaultRunFiles"/>.
    /// </summary>
    public static string RunFiles(string? requested) =>
        requested is { Length: > 0 } && requested.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? requested
            : DefaultRunFiles;

    /// <summary>
    /// Writes <c>{runFiles}-run.txt</c> (and, when <paramref name="consoleFileTag"/> is given, <c>{runFiles}-console.txt</c>
    /// with the done line after that tag, which is where the iOS script looks for it) into the results directory, then
    /// always hands the PEERLESS_DONE line to <paramref name="logDone"/>. A failure to write the files is not thrown: it
    /// is added to the done line, and the result is false.
    /// </summary>
    public static bool Record(
        Exception exception, string? requestedRunFiles, Func<string> resultsDirectory, Action<string> logDone,
        string? consoleFileTag = null)
    {
        string reason = exception.ToString().ReplaceLineEndings(" | ");
        string doneLine = $"PEERLESS_DONE failed: {reason}";
        bool recorded = false;
        try
        {
            string name = RunFiles(requestedRunFiles);
            string directory = resultsDirectory();
            _ = Directory.CreateDirectory(directory);
            // The run file first: the script stops waiting once it sees the done line, then reads the run file.
            File.WriteAllText(Path.Combine(directory, $"{name}-run.txt"), $"failure: startup: {reason}\noutcome: failed\n");
            if (consoleFileTag is not null)
                File.WriteAllText(Path.Combine(directory, $"{name}-console.txt"), $"{consoleFileTag} {doneLine}\n");
            recorded = true;
        }
        catch (Exception writeFailure)
        {
            doneLine += $" | the startup failure could not be recorded: {writeFailure.ToString().ReplaceLineEndings(" | ")}";
        }
        finally
        {
            logDone(doneLine);
        }

        return recorded;
    }
}
