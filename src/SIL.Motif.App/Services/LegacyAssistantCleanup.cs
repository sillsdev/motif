namespace SIL.Motif.App.Services;

/// <summary>
/// On the first start after the window dropped its assistant features, removes the OpenAI key earlier versions
/// kept in the operating system credential store, so no secret stays on disk. A marker file records that it ran.
/// </summary>
public sealed class LegacyAssistantCleanup(ILegacyCredentialStore credentials, string markerPath)
{
    /// <summary>The installation's marker file, beside the per-user preferences.</summary>
    public static string DefaultMarkerPath => Path.Combine(
        SIL.Motif.Host.UserDataHome.Path, "Motif", "assistant-key-removed");

    /// <summary>The cleanup the installed window runs.</summary>
    public static LegacyAssistantCleanup ForInstallation() => new(new LegacyCredentialStore(), DefaultMarkerPath);

    /// <summary>Deletes the stored key once; a failure leaves no marker, so the next start tries again.</summary>
    public void RunOnce()
    {
        try
        {
            if (File.Exists(markerPath)) return;
            credentials.Delete();
            var directory = Path.GetDirectoryName(Path.GetFullPath(markerPath));
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllText(markerPath, "The retired OpenAI key was removed from this computer's credential store.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning("Motif could not remove the retired assistant key: {0}", exception.Message);
        }
    }
}
