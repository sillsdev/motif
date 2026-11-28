using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SIL.Motif.RecordStore;

public sealed class ResourceContentStore(string storePath)
{
    private readonly string root = RecordStore.ResolvePath(storePath) + ".resources";

    public string Put(byte[] bytes)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, hash);
        // A leftover blob that fails verification is rewritten from the correct bytes.
        if (!File.Exists(target) || !Verifies(target, hash))
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // The bytes reach the disk before the name appears, so a published reference never meets a torn file.
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes);
                    file.Flush(flushToDisk: true);
                }
                try { File.Move(temporary, target, overwrite: true); }
                catch (IOException) when (Verifies(target, hash)) { }
                SyncDirectory(root);
            }
            finally { File.Delete(temporary); }
        }
        Read(hash);
        return hash;
    }

    private static bool Verifies(string target, string hash)
    {
        try { return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target))) == hash; }
        catch (IOException) { return false; }
    }

    // The new name must survive a power cut too; Windows has no directory handle to flush.
    private static void SyncDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try
        {
            var descriptor = Open(directory, 0);
            if (descriptor < 0) return;
            try { Sync(descriptor); }
            finally { Close(descriptor); }
        }
        catch (DllNotFoundException) { } // best effort: no libc to call, so no directory flush
    }

    // LibraryImport needs project-wide unsafe code; these calls take only ints and a path.
#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "open", CharSet = CharSet.Ansi)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync")]
    private static extern int Sync(int descriptor);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int descriptor);
#pragma warning restore SYSLIB1054

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
