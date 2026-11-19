using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>Keys an existing file by its filesystem identity across alternate names.</summary>
/// <remarks>Unix uses .NET's shared Linux and macOS FileStatus layout from
/// https://github.com/dotnet/runtime/blob/v10.0.0/src/native/libs/System.Native/pal_io.h.</remarks>
internal readonly partial record struct ProjectFileIdentity(ulong Volume, ulong Low, ulong High = 0,
    string? CanonicalPath = null)
{
    internal string MutexName => CanonicalPath is null
        ? $"Motif.ProjectStartup.{Volume:X16}.{Low:X16}.{High:X16}"
        : "Motif.ProjectStartup.Path." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalPath)));

    internal static ProjectFileIdentity? Read(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) return ReadWindows(path);

        if (Stat(path, out var status) == 0) return new(status.Device, status.Inode);
        var error = Marshal.GetLastPInvokeError();
        return error is 2 or 20 ? null : throw MetadataError(path, error, denied: error is 1 or 13);
    }

    private static ProjectFileIdentity? ReadWindows(string path)
    {
        const uint readAttributes = 0x80;
        const uint openExisting = 3;
        const int fileIdInfo = 18;

        // Attribute access can query a file ID even while LibLCM denies data sharing.
        using var handle = CreateFile(ExtendedWindowsPath(path), readAttributes,
            FileShare.ReadWrite | FileShare.Delete, 0, openExisting, 0, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            return error is 2 or 3 ? null : WindowsPathIdentity(path);
        }

        return ReadWindowsIdentity(path, () =>
            GetFileInformation(handle, fileIdInfo, out var id, Marshal.SizeOf<WindowsFileId>())
                ? (0, new(id.Volume, id.Low, id.High)) : (Marshal.GetLastPInvokeError(), default),
            () => GetLegacyFileInformation(handle, out var legacy)
                ? (0, new(legacy.VolumeSerial, ((ulong)legacy.IndexHigh << 32) | legacy.IndexLow))
                : (Marshal.GetLastPInvokeError(), default),
            () => ReadCanonicalWindowsPath(handle));
    }

    internal static ProjectFileIdentity ReadWindowsIdentity(string path,
        Func<(int Error, ProjectFileIdentity Identity)> readExtended,
        Func<(int Error, ProjectFileIdentity Identity)> readLegacy, Func<string?> readCanonicalPath)
    {
        var (error, identity) = readExtended();
        if (error == 0) return identity;
        (error, identity) = readLegacy();
        return error == 0 ? identity : WindowsPathIdentity(readCanonicalPath() ?? path);
    }

    private static ProjectFileIdentity WindowsPathIdentity(string path)
    {
        const string extendedUnc = @"\\?\UNC\";
        if (path.StartsWith(extendedUnc, StringComparison.OrdinalIgnoreCase))
            path = @"\\" + path[extendedUnc.Length..];
        else if (path.Length >= 6 && path[5] == ':' &&
            (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)))
            path = path[4..];
        return new(0, 0, CanonicalPath: path.ToUpperInvariant());
    }

    private static unsafe string? ReadCanonicalWindowsPath(SafeFileHandle handle)
    {
        Span<char> path = stackalloc char[260];
        fixed (char* buffer = path)
        {
            var length = GetFinalPathName(handle, buffer, (uint)path.Length, 0);
            if (length == 0 || length > 32768) return null;
            if (length < path.Length) return new string(path[..(int)length]);
            path = new char[length];
        }
        fixed (char* buffer = path)
        {
            var length = GetFinalPathName(handle, buffer, (uint)path.Length, 0);
            return length == 0 || length >= path.Length ? null : new string(path[..(int)length]);
        }
    }

    private static string ExtendedWindowsPath(string path)
    {
        if (path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    private static Exception MetadataError(string path, int error, bool denied)
    {
        var cause = new Win32Exception(error);
        var message = $"Motif could not identify project file '{path}'. {cause.Message}";
        return denied ? new UnauthorizedAccessException(message, cause) : new IOException(message, cause);
    }

    [StructLayout(LayoutKind.Explicit, Size = 120)]
    private struct UnixFileStatus
    {
        [FieldOffset(88)] public ulong Device;
        [FieldOffset(104)] public ulong Inode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileId
    {
        public ulong Volume;
        public ulong Low;
        public ulong High;
    }

    [StructLayout(LayoutKind.Explicit, Size = 52)]
    private struct WindowsLegacyFileId
    {
        [FieldOffset(28)] public uint VolumeSerial;
        [FieldOffset(44)] public uint IndexHigh;
        [FieldOffset(48)] public uint IndexLow;
    }

    [LibraryImport("System.Native", EntryPoint = "SystemNative_Stat", StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true)]
    private static partial int Stat(string path, out UnixFileStatus status);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, FileShare share, nint security,
        uint creation, uint attributes, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformation(SafeFileHandle handle, int kind, out WindowsFileId id, int size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLegacyFileInformation(SafeFileHandle handle, out WindowsLegacyFileId id);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW")]
    private static unsafe partial uint GetFinalPathName(SafeFileHandle handle, char* buffer, uint length, uint flags);
}
