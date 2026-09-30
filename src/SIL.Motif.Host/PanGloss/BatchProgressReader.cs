using System.Text;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Host.PanGloss;

internal sealed class BatchProgressReader(IReadOnlyList<string> words)
{
    private readonly byte[] _readBuffer = new byte[8192];
    private readonly MemoryStream _partialLine = new();
    private long _position;
    private int _completed;
    private int? _started;
    private bool _sawRow;

    internal TrialWordProgress? Read(string path)
    {
        if (!File.Exists(path)) return Current;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _position)
            {
                _position = 0;
                _partialLine.SetLength(0);
            }
            stream.Position = _position;
            int read;
            while ((read = stream.Read(_readBuffer, 0, _readBuffer.Length)) > 0)
            {
                _position += read;
                Process(_readBuffer.AsSpan(0, read));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Current;
        }
        return Current;
    }

    private TrialWordProgress? Current => _sawRow
        ? new TrialWordProgress(_completed, words.Count,
            _started is { } current && current >= _completed ? words[current] : null)
        : null;

    private void Process(ReadOnlySpan<byte> bytes)
    {
        var start = 0;
        while (start < bytes.Length)
        {
            var remaining = bytes[start..];
            var newline = remaining.IndexOf((byte)'\n');
            if (newline < 0)
            {
                _partialLine.Write(remaining);
                return;
            }
            _partialLine.Write(remaining[..newline]);
            ProcessLine(_partialLine.GetBuffer().AsSpan(0, (int)_partialLine.Length));
            _partialLine.SetLength(0);
            start += newline + 1;
        }
    }

    private void ProcessLine(ReadOnlySpan<byte> bytes)
    {
        var cells = Encoding.UTF8.GetString(bytes).TrimEnd('\r').Split('\t');
        if (cells.Length < 3 || !int.TryParse(cells[0], out var index) ||
            index < 0 || index >= words.Count ||
            !string.Equals(cells[1], words[index].Trim(), StringComparison.Ordinal))
            return;
        if (cells.Length == 3 && cells[2] == "STARTED")
        {
            if (index >= _completed && (_started is null || index >= _started.Value))
                _started = index;
            _sawRow = true;
        }
        else if (cells.Length >= 5 && index == _completed)
        {
            _completed++;
            _started = null;
            _sawRow = true;
        }
    }
}
