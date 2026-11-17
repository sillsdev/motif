using System.Runtime.InteropServices;

namespace SIL.Motif.App.Services;

/// <summary>Removes the OpenAI key earlier Motif versions kept in the operating system credential store.</summary>
public interface ILegacyCredentialStore
{
    /// <summary>Removes the stored key if the store is available; it is not an error when none exists.</summary>
    void Delete();
}

/// <summary>Deletes the retired credential from Windows Credential Manager, the macOS Keychain or libsecret.</summary>
public sealed partial class LegacyCredentialStore : ILegacyCredentialStore
{
    private const string Target = "SIL.Motif/OpenAI";

    /// <inheritdoc />
    public void Delete()
    {
        try
        {
            if (OperatingSystem.IsWindows()) CredDelete(Target, 1, 0);
            else if (OperatingSystem.IsMacOS()) DeleteMac();
            else if (OperatingSystem.IsLinux()) DeleteLinux();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredDelete(string target, uint type, uint flags);
}
