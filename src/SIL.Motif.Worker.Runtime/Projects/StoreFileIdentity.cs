using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace SIL.Motif.Worker.Projects;

/// <summary>
/// Names the physical file behind a path: device and inode on Linux and macOS, volume serial and file index on
/// Windows. Deleting and recreating a store gives a new identity even when the timestamps match.
/// </summary>
internal readonly record struct StoreFileIdentity(ulong Volume, ulong File, long CreatedTicks)
{
    /// <summary>Reads the identity of the file at a path, or null when it does not exist.</summary>
    public static StoreFileIdentity? Read(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        var created = System.IO.File.GetCreationTimeUtc(path).Ticks;
        try
        {
            var (volume, file) = ReadPhysical(path);
            return new StoreFileIdentity(volume, file, created);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or IOException or
            Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            return new StoreFileIdentity(0, 0, created);
        }
    }

    /// <summary>Whether the file now at a path is not the one this identity was read from.</summary>
    public bool IsReplacedBy(StoreFileIdentity? current) => current is not { } now ||
        (File != 0 && now.File != 0 ? now.Volume != Volume || now.File != File : now.CreatedTicks != CreatedTicks);

    private static (ulong Volume, ulong File) ReadPhysical(string path)
    {
        if (OperatingSystem.IsLinux()) return ReadLinux(path);
        if (OperatingSystem.IsMacOS()) return ReadMac(path);
        if (OperatingSystem.IsWindows()) return ReadWindows(path);
        throw new PlatformNotSupportedException();
    }

    [SupportedOSPlatform("linux")]
    private static (ulong, ulong) ReadLinux(string path)
    {
        if (LinuxStatx(-100, path, 0, 0x100, out var result) != 0)
            throw new IOException("Could not read file identity.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if ((result.Mask & 0x100) == 0) throw new PlatformNotSupportedException();
        return (((ulong)result.DeviceMajor << 32) | result.DeviceMinor, result.Inode);
    }

    [SupportedOSPlatform("macos")]
    private static (ulong, ulong) ReadMac(string path)
    {
        MacStatBuffer result;
        var status = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? MacStatArm64(path, out result)
            : MacStatIntel(path, out result);
        if (status != 0)
            throw new IOException("Could not read file identity.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return ((uint)result.Device, result.Inode);
    }

    [SupportedOSPlatform("windows")]
    private static (ulong, ulong) ReadWindows(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.None);
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var result))
            throw new IOException("Could not read file identity.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return (result.VolumeSerialNumber, ((ulong)result.FileIndexHigh << 32) | result.FileIndexLow);
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatxBuffer
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacStatBuffer
    {
        [FieldOffset(0)] public int Device;
        [FieldOffset(8)] public ulong Inode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinuxStatx(int directory, string path, int flags, uint mask,
        out LinuxStatxBuffer result);

    [DllImport("libc", EntryPoint = "stat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int MacStatArm64(string path, out MacStatBuffer result);

    [DllImport("libc", EntryPoint = "stat$INODE64", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int MacStatIntel(string path, out MacStatBuffer result);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsFileInformation result);
}
