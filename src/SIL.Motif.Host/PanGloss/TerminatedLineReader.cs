using System.Text;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Reads newline-terminated lines. <c>pangloss serve</c> exits at once on end of input and can leave a partial last
/// line; <see cref="StreamReader.ReadLineAsync()"/> would return that fragment as a line, so this reader returns
/// <see langword="null"/> at end of stream and discards any unterminated tail.
/// </summary>
internal sealed class TerminatedLineReader(Stream stream, Encoding encoding)
{
    private readonly StreamReader _reader = new(stream, encoding);
    private readonly char[] _buffer = new char[8192];
    private readonly StringBuilder _line = new();
    private int _position;
    private int _length;

    /// <summary>The next complete line without its <c>\n</c>, or <see langword="null"/> when the stream ended first.</summary>
    public async Task<string?> ReadLineAsync()
    {
        while (true)
        {
            if (_position == _length)
            {
                _length = await _reader.ReadAsync(_buffer.AsMemory()).ConfigureAwait(false);
                _position = 0;
                if (_length == 0) return null;
            }
            var end = Array.IndexOf(_buffer, '\n', _position, _length - _position);
            if (end < 0)
            {
                _line.Append(_buffer, _position, _length - _position);
                _position = _length;
                continue;
            }
            _line.Append(_buffer, _position, end - _position);
            _position = end + 1;
            var text = _line.ToString();
            _line.Clear();
            return text;
        }
    }
}
