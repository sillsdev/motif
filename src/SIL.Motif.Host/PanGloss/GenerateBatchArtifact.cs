using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>The artifact file of <c>generate --batch</c>: one JSON <see cref="GenerateBatchRow"/> per line.</summary>
public static class GenerateBatchArtifact
{
    /// <summary>
    /// Writes rows one at a time to a temporary file next to <paramref name="path"/>, so memory does not grow with the batch;
    /// <see cref="Commit"/> moves it into place and a writer disposed without it leaves no file, never half of one.
    /// </summary>
    public sealed class Writer : IDisposable
    {
        private readonly string _path;
        private readonly string _temporary;
        private StreamWriter? _stream;

        /// <summary>Opens the temporary file, creating the folder.</summary>
        public Writer(string path)
        {
            _path = path;
            _temporary = path + ".tmp";
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            _stream = new StreamWriter(_temporary, false, new UTF8Encoding(false));
        }

        /// <summary>Appends one row as one JSON line.</summary>
        public void Add(GenerateBatchRow row) => _stream!.WriteLine(ProjectionJson.SerializeLine(row));

        /// <summary>Closes the file and replaces the artifact with it.</summary>
        public void Commit()
        {
            _stream!.Dispose();
            _stream = null;
            File.Move(_temporary, _path, overwrite: true);
        }

        /// <summary>Removes the temporary file when the rows were never committed.</summary>
        public void Dispose()
        {
            if (_stream is null) return;
            _stream.Dispose();
            _stream = null;
            try { File.Delete(_temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
