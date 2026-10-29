using System.IO.Compression;
using System.Text;

namespace SIL.Motif.Host.Store;

/// <summary>Encodes grammar-check JSON for compact project-store persistence.</summary>
public static class GrammarCheckStorage
{
    /// <summary>Compresses UTF-8 JSON into a gzip payload.</summary>
    public static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false))) writer.Write(json);
        return output.ToArray();
    }

    /// <summary>Reads a gzip payload as UTF-8 JSON.</summary>
    public static string Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }
}
