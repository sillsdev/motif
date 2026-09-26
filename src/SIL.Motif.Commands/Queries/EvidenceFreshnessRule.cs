namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The one rule for whether a project's numbers are still current. The command line's stored read and the window
/// both apply it: the stored read to the Assessment that matches the current Baseline, the window to the
/// Assessment it shows, which can be an older one just after a Refresh.
/// </summary>
public static class EvidenceFreshnessRule
{
    /// <summary>
    /// Whether numbers measured against <paramref name="measuredSaveUtc"/> are current: stale when FieldWorks has
    /// saved since, otherwise current, or no Baseline when none has been captured.
    /// </summary>
    /// <param name="baselineSaveUtc">The FieldWorks save the current Baseline copies, or none before a capture.</param>
    /// <param name="measuredSaveUtc">The FieldWorks save the numbers were measured against.</param>
    /// <param name="latestSaveUtc">The latest FieldWorks save known, from <see cref="LatestSave"/>.</param>
    public static EvidenceFreshness Of(
        DateTimeOffset? baselineSaveUtc, DateTimeOffset? measuredSaveUtc, DateTimeOffset? latestSaveUtc) =>
        measuredSaveUtc is { } measured && latestSaveUtc is { } latest && latest > measured
            ? EvidenceFreshness.Stale
            : baselineSaveUtc is null ? EvidenceFreshness.NoBaseline : EvidenceFreshness.Current;

    /// <summary>
    /// The latest FieldWorks save known: the project file's last write as last read, or the Baseline's save when
    /// that is later or the file could not be read.
    /// </summary>
    public static DateTimeOffset? LatestSave(DateTimeOffset? projectLastWriteUtc, DateTimeOffset? baselineSaveUtc) =>
        projectLastWriteUtc is { } written && (baselineSaveUtc is not { } baseline || written > baseline)
            ? written
            : baselineSaveUtc;
}
