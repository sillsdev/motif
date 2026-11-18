using System.Security.Cryptography;

namespace SIL.Motif.RecordStore;

public sealed class ResourceContentStore(string storePath)
{
    private readonly string root = Path.GetFullPath(storePath) + ".resources";

    public string Put(byte[] bytes)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, hash);
        if (!File.Exists(target))
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target)) { }
            }
            finally { File.Delete(temporary); }
        }
        Read(hash);
        return hash;
    }

    public byte[] Read(string hash)
    {
        if (hash.Length != 64 || hash.Any(c => !"0123456789abcdef".Contains(c)))
            throw new InvalidDataException("Invalid resource content hash.");
        var bytes = File.ReadAllBytes(Path.Combine(root, hash));
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Project resource content hash mismatch: " + hash);
        return bytes;
    }
}
