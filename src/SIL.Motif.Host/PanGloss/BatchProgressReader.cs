using System.Text;
using System.Globalization;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Host.PanGloss;

internal sealed class BatchProgressReader(IReadOnlyList<string> words)
{
    private readonly byte[] _readBuffer = new byte[8192];
    private readonly MemoryStream _partialLine = new();
    private readonly HashSet<int> _completedIndexes = [];
    private readonly List<int> _activeIndexes = [];
    private long _position;
    private int _completed;
    private readonly SortedDictionary<int, StoppedParseWord> _stoppedWords = new();
    private ParseWordTiming? _slowest;
    private int? _slowestIndex;
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
            _activeIndexes.Count == 0 ? null : words[_activeIndexes[^1]])
            {
                StoppedWords = _stoppedWords.Count == 0 ? Array.Empty<StoppedParseWord>() : _stoppedWords.Values.ToArray(),
                SlowestWord = _slowest,
            }
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
        if (cells.Length < 3 ||
            !int.TryParse(cells[PanGlossInterfaceVersions.BatchTsvIndexColumn], out var index) ||
            index < 0 || index >= words.Count ||
            !string.Equals(cells[PanGlossInterfaceVersions.BatchTsvWordColumn], words[index].Trim(), StringComparison.Ordinal))
            return;
        if (cells.Length == 3 && cells[PanGlossInterfaceVersions.BatchTsvElapsedMsColumn] == "STARTED")
        {
            if (!_completedIndexes.Contains(index))
            {
                if (!_activeIndexes.Contains(index)) _activeIndexes.Add(index);
                _sawRow = true;
            }
        }
        else if (cells.Length == PanGlossInterfaceVersions.BatchTsvCompletionColumnCount &&
                 !_completedIndexes.Contains(index))
        {
            if (double.TryParse(cells[PanGlossInterfaceVersions.BatchTsvElapsedMsColumn], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var elapsed) &&
                double.IsFinite(elapsed) && elapsed >= 0)
            {
                _completedIndexes.Add(index);
                _activeIndexes.Remove(index);
                _completed++;
                if (_slowest is null || elapsed > _slowest.ElapsedMs ||
                    elapsed == _slowest.ElapsedMs && index < (_slowestIndex ?? int.MaxValue))
                {
                    _slowest = new ParseWordTiming(words[index], elapsed);
                    _slowestIndex = index;
                }
                var status = cells[PanGlossInterfaceVersions.BatchTsvStatusColumn];
                if (status is "TIMEOUT" or "CAP")
                    _stoppedWords[index] = new StoppedParseWord(words[index], status, elapsed);
                _sawRow = true;
            }
        }
    }
}
