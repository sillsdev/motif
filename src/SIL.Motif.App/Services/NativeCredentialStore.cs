using System.Runtime.InteropServices;
using System.Text;

namespace SIL.Motif.App.Services;

/// <summary>Uses the operating system credential store, falling back to process memory.</summary>
public sealed partial class NativeCredentialStore : ICredentialStore
{
    private readonly string _target;
    private string? _sessionValue;
    private bool _sessionOnly;

    /// <summary>Creates the store for the Motif OpenAI credential.</summary>
    public NativeCredentialStore() : this("SIL.Motif/OpenAI") { }

    internal NativeCredentialStore(string target) => _target = target;

    /// <inheritdoc />
    public string? Read()
    {
        if (_sessionOnly) return _sessionValue;
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsMacOS()) return ReadMac();
            if (OperatingSystem.IsLinux()) return ReadLinux();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _sessionOnly = true;
        }
        return _sessionValue;
    }

    /// <inheritdoc />
    public bool Write(string credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _sessionValue = credential;
        try
        {
            var saved = OperatingSystem.IsWindows() ? WriteWindows(credential)
                : OperatingSystem.IsMacOS() ? WriteMac(credential)
                : OperatingSystem.IsLinux() && WriteLinux(credential);
            _sessionOnly = !saved;
            return saved;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _sessionOnly = true;
            return false;
        }
    }

    /// <inheritdoc />
    public void Delete()
    {
        _sessionValue = null;
        try
        {
            if (OperatingSystem.IsWindows()) CredDelete(_target, 1, 0);
            else if (OperatingSystem.IsMacOS()) DeleteMac();
            else if (OperatingSystem.IsLinux()) DeleteLinux();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsCredential
    {
        internal uint Flags, Type;
        internal IntPtr TargetName, Comment;
        internal long LastWritten;
        internal uint CredentialBlobSize;
        internal IntPtr CredentialBlob;
        internal uint Persist, AttributeCount;
        internal IntPtr Attributes, TargetAlias, UserName;
    }

    private bool WriteWindows(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var target = Marshal.StringToCoTaskMemUni(_target);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new WindowsCredential { Type = 1, TargetName = target, CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length, Persist = 2 };
            return CredWrite(ref credential, 0) != 0;
        }
        finally
        {
            Array.Clear(bytes);
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeCoTaskMem(target);
        }
    }

    private string? ReadWindows()
    {
        if (CredRead(_target, 1, 0, out var pointer) == 0) return null;
        try
        {
            var credential = Marshal.PtrToStructure<WindowsCredential>(pointer);
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    private static partial int CredWrite(ref WindowsCredential credential, uint flags);
    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredRead(string target, uint type, uint flags, out IntPtr credential);
    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredDelete(string target, uint type, uint flags);
    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr credential);
}
