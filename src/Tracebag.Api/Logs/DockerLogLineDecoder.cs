using System.Text;

namespace Tracebag.Api.Logs;

public sealed class DockerLogLineDecoder
{
    private readonly int _maximumLineBytes;
    private readonly Dictionary<string, StreamState> _states = new(StringComparer.Ordinal)
    {
        ["stdout"] = new(),
        ["stderr"] = new()
    };

    public DockerLogLineDecoder(int maximumLineBytes)
    {
        _maximumLineBytes = Math.Max(1, maximumLineBytes);
    }

    public IReadOnlyList<DecodedLogLine> Append(string stream, ReadOnlySpan<byte> bytes)
    {
        var state = State(stream);
        var lines = new List<DecodedLogLine>();
        foreach (var value in bytes)
        {
            if (value == (byte)'\n')
            {
                lines.Add(ToLine(stream, state));
            }
            else if (state.Bytes.Count < _maximumLineBytes)
            {
                state.Bytes.Add(value);
            }
            else
            {
                state.Truncated = true;
            }
        }

        return lines;
    }

    public IReadOnlyList<DecodedLogLine> Complete()
    {
        var completed = new List<DecodedLogLine>();
        foreach (var (stream, state) in _states)
        {
            if (state.Bytes.Count > 0 || state.Truncated)
            {
                completed.Add(ToLine(stream, state));
            }
        }

        return completed;
    }

    private static DecodedLogLine ToLine(string stream, StreamState state)
    {
        if (state.Bytes.Count > 0 && state.Bytes[^1] == (byte)'\r')
        {
            state.Bytes.RemoveAt(state.Bytes.Count - 1);
        }

        var text = Encoding.UTF8.GetString(state.Bytes.ToArray());
        if (state.Truncated)
        {
            text += " …[truncated]";
        }

        state.Bytes.Clear();
        var truncated = state.Truncated;
        state.Truncated = false;
        return new DecodedLogLine(stream, text, truncated);
    }

    private StreamState State(string stream)
    {
        return _states.TryGetValue(stream, out var state) ? state : _states["stdout"];
    }

    private sealed class StreamState
    {
        public List<byte> Bytes { get; } = new();
        public bool Truncated { get; set; }
    }
}

public sealed record DecodedLogLine(string Stream, string Text, bool Truncated);
