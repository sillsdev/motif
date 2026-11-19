using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Which parser an enqueuer expects a job to run on, and which one a Worker is: the SHA-256 of its bytes.
/// </summary>
/// <remarks>
/// The bytes fully determine the parser, and a development build keeps its version across rebuilds, so
/// <see cref="Key"/> is the hash alone and a job and a Worker match only when their keys are equal. The version
/// and path are information: the version is recorded only when already known, else "unknown", so reading an
/// identity never runs the parser. Two copies of one build are one parser, and a rebuild in place is another.
/// </remarks>
public sealed record ParserIdentity(string Version, string Sha256, string Path)
{
    private static readonly ConcurrentDictionary<(string Path, long Stamp, long Length), string> Hashes = new();

    /// <summary>The value a job and a Worker compare: the content hash alone.</summary>
    [JsonIgnore]
    public string Key => Sha256;

    /// <summary>Reads the identity of the executable at <paramref name="path"/>, or null when there is none.</summary>
    public static ParserIdentity? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var sha = Hashes.GetOrAdd((file.FullName, file.LastWriteTimeUtc.Ticks, file.Length), key => HashFile(key.Path));
            return new ParserIdentity(ParserExecutableIdentity.CachedVersion(file.FullName) ?? "unknown", sha, file.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The key of the executable at <paramref name="path"/>, or null when there is none.</summary>
    public static string? KeyOf(string? path) => Read(path)?.Key;

    /// <summary>
    /// Reads a launchable executable's hash now, even when a replacement preserved its file metadata.
    /// Checks the platform's executable format and permissions without running the parser.
    /// </summary>
    public static string? ReadFreshKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var file = File.OpenRead(path);
            if (!LooksExecutable(file, path)) return null;
            file.Position = 0;
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or BadImageFormatException)
        {
            return null;
        }
    }

    private static bool LooksExecutable(FileStream file, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var executable = OpenExecutable(path, 0x20, FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (executable.IsInvalid) return false;
            using var reader = new PEReader(file, PEStreamOptions.LeaveOpen);
            var headers = reader.PEHeaders;
            var flags = headers.CoffHeader.Characteristics;
            return headers.PEHeader is { AddressOfEntryPoint: > 0 } && flags.HasFlag(Characteristics.ExecutableImage) &&
                !flags.HasFlag(Characteristics.Dll) && headers.CoffHeader.Machine is Machine.Amd64 or Machine.Arm64;
        }

        if ((!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) || Access(path, 1) != 0) return false;
        Span<byte> header = stackalloc byte[64];
        var read = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (read >= 2 && header[0] == '#' && header[1] == '!')
        {
            file.Position = 2;
            using var script = new StreamReader(file, leaveOpen: true);
            Span<char> line = stackalloc char[256];
            var length = script.ReadBlock(line);
            var end = line[..length].IndexOf('\n');
            if (end < 0 && length == line.Length) return false;
            ReadOnlySpan<char> text = end < 0 ? line[..length] : line[..end];
            var interpreter = text.Trim(" \t");
            var space = interpreter.IndexOfAny(' ', '\t');
            if (space >= 0) interpreter = interpreter[..space];
            return !interpreter.IsEmpty && interpreter[0] == '/' && Access(interpreter.ToString(), 1) == 0;
        }

        if (OperatingSystem.IsMacOS())
            return read >= 32 && BinaryPrimitives.ReadUInt32BigEndian(header) is 0xfeedfacf or 0xcffaedfe or
                0xcafebabe or 0xbebafeca or 0xcafebabf or 0xbfbafeca;

        return read == header.Length && header[0] == 0x7f && header[1] == 'E' && header[2] == 'L' && header[3] == 'F' &&
            header[4] == 2 && header[5] == 1 && header[6] == 1 &&
            BinaryPrimitives.ReadUInt64LittleEndian(header[24..]) != 0 &&
            BinaryPrimitives.ReadUInt16LittleEndian(header[16..]) is 2 or 3 &&
            BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) ==
                (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 183 : 62);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle OpenExecutable(string path, uint access, FileShare sharing,
        IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("libc", EntryPoint = "access", CharSet = CharSet.Ansi)]
    private static extern int Access(string path, int mode);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>A short human form for warnings: the version and the start of the hash.</summary>
    public static string Describe(string? version, string? sha256) =>
        (string.IsNullOrEmpty(version) ? "unknown version" : version) + " (" +
        (string.IsNullOrEmpty(sha256) ? "no hash" : sha256[..Math.Min(sha256.Length, 19)]) + ")";
}
