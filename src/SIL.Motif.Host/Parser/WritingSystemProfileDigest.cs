using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Host.Parser;

/// <summary>Identifies the LDML sidecars of a saved FieldWorks writing-system profile.</summary>
public static class WritingSystemProfileDigest
{
    /// <summary>Hashes the names and bytes of the LDML files in a project snapshot.</summary>
    public static string Read(string fwDataPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(fwDataPath)!, "WritingSystemStore");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.ldml").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                using var stream = File.OpenRead(path);
                Append(hash, "WritingSystemStore/" + Path.GetFileName(path), SHA256.HashData(stream));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Adds one project-relative LDML name and its content digest to the profile digest.</summary>
    public static void Append(IncrementalHash hash, string name, byte[] contentDigest)
    {
        var encodedName = Encoding.UTF8.GetBytes(name);
        hash.AppendData(BitConverter.GetBytes(encodedName.Length));
        hash.AppendData(encodedName);
        hash.AppendData(contentDigest);
    }
}
