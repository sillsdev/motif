using System.Text;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Reads newline-terminated lines. <c>pangloss serve</c> exits at once on end of input and can leave a partial last
/// line; <see cref="StreamReader.ReadLineAsync()"/> would return that fragment as a line, so this reader returns
/// <see langword="null"/> at end of stream and discards any unterminated tail.
/// </summary>
/// <remarks>
/// It reads bytes and splits on <c>\n</c> itself rather than through a <see cref="StreamReader"/>:
/// <c>StreamReader.ReadAsync</c> keeps reading while its last read filled the byte buffer, and a pipe that has been
/// drained answers that extra read with "nothing yet", so a reply ending on a full read was never returned
/// and the session hung. The encoding must keep <c>\n</c> as the single byte 0x0A, as UTF-8 does.
/// </remarks>
internal sealed class TerminatedLineReader(Stream stream, Encoding encoding)
{
    private readonly byte[] _buffer = new byte[8192];
    private readonly MemoryStream _line = new();
    private int _position;
    private int _length;

    /// <summary>The next complete line without its <c>\n</c>, or <see langword="null"/> when the stream ended first.</summary>
    public async Task<string?> ReadLineAsync()
    {
        while (true)
        {
            if (_position == _length)
            {
                _length = await stream.ReadAsync(_buffer.AsMemory()).ConfigureAwait(false);
                _position = 0;
                if (_length == 0) return null;
            }
            var end = Array.IndexOf(_buffer, (byte)'\n', _position, _length - _position);
            if (end < 0)
            {
                _line.Write(_buffer, _position, _length - _position);
                _position = _length;
                continue;
            }
            _line.Write(_buffer, _position, end - _position);
            _position = end + 1;
            var text = encoding.GetString(_line.GetBuffer(), 0, (int)_line.Length);
            _line.SetLength(0);
            return text;
        }
    }
}
