using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

/// <summary>
/// Pins the checked-in golden frames. Set PEERLESS_REGENERATE_FIXTURES=1 to rewrite them after a deliberate format
/// change; the diff then shows exactly which bytes moved, and the native encoders' tests must follow.
/// </summary>
public sealed class GoldenFixtureTests
{
    public static TheoryData<string> Names => [.. GoldenFixtures.All.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    public void The_test_encoder_reproduces_the_checked_in_frame(string name)
    {
        byte[] encoded = TestFrameEncoder.Encode(GoldenFixtures.All[name]);
        string path = Path.Combine(FixturesDirectoryAttribute.Resolve(), name + ".bin");

        if (Environment.GetEnvironmentVariable("PEERLESS_REGENERATE_FIXTURES") == "1")
            File.WriteAllBytes(path, encoded);

        Assert.True(File.Exists(path), $"Missing fixture {path}; run with PEERLESS_REGENERATE_FIXTURES=1.");
        Assert.Equal(Convert.ToHexString(File.ReadAllBytes(path)), Convert.ToHexString(encoded));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_decoder_reads_the_checked_in_frame(string name)
    {
        byte[] frame = File.ReadAllBytes(Path.Combine(FixturesDirectoryAttribute.Resolve(), name + ".bin"));

        SnapshotAssert.Equal(GoldenFixtures.All[name], SnapshotDecoder.Decode(frame));
    }

    [Fact]
    public void The_scalars_fixture_keeps_integers_and_doubles_apart()
    {
        byte[] frame = File.ReadAllBytes(Path.Combine(FixturesDirectoryAttribute.Resolve(), "document-scalars.bin"));
        FirestoreMap fields = SnapshotDecoder.DecodeDocument(frame).Document.Fields;

        Assert.Equal(FirestoreValueKind.Double, fields["double-whole"].Kind);
        Assert.False(fields["double-whole"].TryGetInteger(out _));
        Assert.Equal(FirestoreValueKind.Integer, fields["zero"].Kind);
        Assert.False(fields["zero"].TryGetDouble(out _));
        Assert.Equal(0x7FF8_0000_0000_0000, BitConverter.DoubleToInt64Bits(fields["double-nan"].AsDouble()));
        Assert.True(double.IsNegative(fields["double-negative-zero"].AsDouble()));
        Assert.Equal("com.google.firebase.firestore.VectorValue", fields["unsupported"].UnsupportedTypeName);
    }
}
