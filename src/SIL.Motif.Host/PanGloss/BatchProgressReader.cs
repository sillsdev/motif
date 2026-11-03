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
    private int? _parserWorkColumn;

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

    // Parser work rank the slowest word when both words carry them, because time depends on this computer.
    private bool IsSlower(double elapsed, long? work, int index)
    {
        var (first, second) = _slowest!.ParserWork is { } held && work is { } now
            ? ((double)now, (double)held) : (elapsed, _slowest.ElapsedMs);
        return first > second || first == second && index < (_slowestIndex ?? int.MaxValue);
    }

    // The headerless layout appends the parser work to the five result columns, so a 6 column row ends with it.
    private long? ParserWorkOf(string[] cells)
    {
        var column = _parserWorkColumn ?? (cells.Length is 6 ? 5 : (int?)null);
        return column is { } at && at < cells.Length &&
            long.TryParse(cells[at], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private void ProcessLine(ReadOnlySpan<byte> bytes)
    {
        var cells = Encoding.UTF8.GetString(bytes).TrimEnd('\r').Split('\t');
        if (cells is ["index" or "idx", "word", ..])
        {
            _parserWorkColumn = Array.IndexOf(cells, "work_steps") is var named and >= 0 ? named : null;
            return;
        }
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
        else if (cells.Length >= PanGlossInterfaceVersions.BatchTsvCompletionColumnCount &&
                 !_completedIndexes.Contains(index))
        {
            if (double.TryParse(cells[PanGlossInterfaceVersions.BatchTsvElapsedMsColumn], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var elapsed) &&
                double.IsFinite(elapsed) && elapsed >= 0)
            {
                _completedIndexes.Add(index);
                _activeIndexes.Remove(index);
                _completed++;
                var work = ParserWorkOf(cells);
                if (_slowest is null || IsSlower(elapsed, work, index))
                {
                    _slowest = new ParseWordTiming(words[index], elapsed) { ParserWork = work };
                    _slowestIndex = index;
                }
                var status = cells[PanGlossInterfaceVersions.BatchTsvStatusColumn];
                if (status is "TIMEOUT" or "CAP")
                    _stoppedWords[index] = new StoppedParseWord(words[index], status, elapsed) { ParserWork = work };
                _sawRow = true;
            }
        }
    }
}
