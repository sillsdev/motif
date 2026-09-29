namespace SIL.Motif.Commands;

/// <summary>Records an Apply whose project save could not be matched to a stored Receipt.</summary>
public static class ProjectReconciliationMarker
{
    private const string FileSuffix = ".motif-reconciliation-needed";

    /// <summary>Returns the project-adjacent marker path for a FieldWorks project.</summary>
    public static string PathFor(string fwDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        return Path.GetFullPath(fwDataPath) + FileSuffix;
    }

    /// <summary>Whether a previous Apply still needs a person to check the project.</summary>
    public static bool Exists(string fwDataPath) => File.Exists(PathFor(fwDataPath));

    /// <summary>Writes the marker without relying on the proposal store.</summary>
    public static void Mark(string fwDataPath) =>
        File.WriteAllText(PathFor(fwDataPath), "Check the FieldWorks project before trying Apply again.");

    /// <summary>Removes the marker after a successful project refresh.</summary>
    public static bool Clear(string fwDataPath)
    {
        try
        {
            File.Delete(PathFor(fwDataPath));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
