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

    private const string Text = "Check the FieldWorks project before trying Apply again.";

    /// <summary>Writes a marker of unknown boundary without relying on the proposal store.</summary>
    public static void Mark(string fwDataPath) => File.WriteAllText(PathFor(fwDataPath), Text);

    /// <summary>Adds one Proposal and the boundary that failed to the marker, keeping what it already holds.</summary>
    public static void Mark(string fwDataPath, ReconciliationBoundary boundary, string proposalId)
    {
        var lines = new List<string>();
        if (File.Exists(PathFor(fwDataPath))) lines.AddRange(File.ReadAllLines(PathFor(fwDataPath)));
        if (lines.Count == 0) lines.Add(Text);
        var entry = $"{boundary}:{proposalId}";
        if (!lines.Contains(entry, StringComparer.Ordinal)) lines.Add(entry);
        File.WriteAllLines(PathFor(fwDataPath), lines);
    }

    /// <summary>The Proposals the marker names with their boundaries, and whether any of it has no known boundary.</summary>
    public static (IReadOnlyList<(ReconciliationBoundary Boundary, string ProposalId)> Entries, bool HasUnknown)
        Read(string fwDataPath)
    {
        var entries = new List<(ReconciliationBoundary, string)>();
        var unknown = false;
        string[] lines;
        try { lines = File.ReadAllLines(PathFor(fwDataPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (entries, true);
        }
        foreach (var line in lines.Skip(1))
        {
            var split = line.IndexOf(':');
            if (split > 0 && Enum.TryParse<ReconciliationBoundary>(line[..split], out var boundary) &&
                Enum.IsDefined(boundary) && line[(split + 1)..].Length > 0)
                entries.Add((boundary, line[(split + 1)..]));
            else if (line.Length > 0) unknown = true;
        }
        return (entries, unknown || entries.Count == 0);
    }

    /// <summary>
    /// Whether the marker stands only for Applies that were saved and merely not recorded, so a command may clear it
    /// once they are; a Save failure or an old marker with no boundary needs a person to look first.
    /// </summary>
    public static bool ClearableByCommand(string fwDataPath, out IReadOnlyList<string> proposalIds)
    {
        var (entries, unknown) = Read(fwDataPath);
        proposalIds = [.. entries.Select(entry => entry.ProposalId)];
        return !unknown && entries.All(entry => entry.Boundary == ReconciliationBoundary.ReceiptRecording);
    }

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
