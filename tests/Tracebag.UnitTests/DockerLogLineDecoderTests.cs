using System.Text;
using Tracebag.Api.Logs;

namespace Tracebag.UnitTests;

public sealed class DockerLogLineDecoderTests
{
    [Fact]
    public void PreservesFragmentedUtf8AndIndependentStreams()
    {
        var decoder = new DockerLogLineDecoder(1024);
        var bytes = Encoding.UTF8.GetBytes("hello 🌍\n");

        Assert.Empty(decoder.Append("stdout", bytes.AsSpan(0, 8)));
        var stdout = Assert.Single(decoder.Append("stdout", bytes.AsSpan(8)));
        var stderr = Assert.Single(decoder.Append("stderr", Encoding.UTF8.GetBytes("failure\n")));

        Assert.Equal("hello 🌍", stdout.Text);
        Assert.Equal("stderr", stderr.Stream);
    }

    [Fact]
    public void ReplacesMalformedUtf8WithoutStoppingTheStream()
    {
        var decoder = new DockerLogLineDecoder(1024);

        var line = Assert.Single(decoder.Append("stdout", [0x66, 0x80, 0x0a]));

        Assert.StartsWith("f", line.Text, StringComparison.Ordinal);
        Assert.Contains('�', line.Text);
    }

    [Fact]
    public void TruncatesAnOversizedLineAndRecoversAtTheNextNewline()
    {
        var decoder = new DockerLogLineDecoder(5);

        var lines = decoder.Append("stdout", Encoding.UTF8.GetBytes("123456789\nok\n"));

        Assert.Equal(2, lines.Count);
        Assert.True(lines[0].Truncated);
        Assert.StartsWith("12345", lines[0].Text, StringComparison.Ordinal);
        Assert.Equal("ok", lines[1].Text);
    }
}
