using System.Runtime.InteropServices;

namespace SIL.Motif.App.Services;

/// <summary>Removes the OpenAI key earlier Motif versions kept in the operating system credential store.</summary>
public interface ILegacyCredentialStore
{
    /// <summary>Removes the stored key. True when the key is gone, including when none was stored; false when the store could not be reached or refused.</summary>
    bool Delete();
}

/// <summary>Deletes the retired credential from Windows Credential Manager, the macOS Keychain or libsecret.</summary>
public sealed partial class LegacyCredentialStore : ILegacyCredentialStore
{
    private const string Target = "SIL.Motif/OpenAI";

    /// <inheritdoc />
    public bool Delete()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return CredDelete(Target, 1, 0) != 0 || Marshal.GetLastPInvokeError() == ErrorNotFound;
            if (OperatingSystem.IsMacOS()) return DeleteMac();
            if (OperatingSystem.IsLinux()) return DeleteLinux();
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private const int ErrorNotFound = 1168;

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredDelete(string target, uint type, uint flags);
}
