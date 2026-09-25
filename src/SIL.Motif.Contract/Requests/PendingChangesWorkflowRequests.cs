namespace SIL.Motif.Contract.Requests;

/// <summary>Applies the pending changes that were checked and measured.</summary>
/// <param name="ProjectPath">The FieldWorks project whose pending changes will be applied.</param>
/// <param name="DraftId">The expected Draft identity, or <see langword="null"/> to use the current Draft.</param>
/// <param name="Revision">The expected revision, or <see langword="null"/> to use the current revision.</param>
/// <param name="User">The person recorded as applying the changes.</param>
public sealed record ApplyPendingRequest(
    string ProjectPath, string? DraftId, string? Revision, string User);

/// <summary>Measures the selected words against one exact pending revision.</summary>
/// <param name="ProjectPath">The FieldWorks project whose pending changes will be measured.</param>
/// <param name="DraftId">The identity of the pending Draft.</param>
/// <param name="Revision">The revision that the caller checked before measuring.</param>
/// <param name="Words">The words to include in the Trial.</param>
/// <param name="BeforeCorrectnessAssessmentId">The earlier Correctness Assessment used for comparison, if any.</param>
public sealed record MeasurePendingRequest(
    string ProjectPath,
    string DraftId,
    string Revision,
    IReadOnlyList<string> Words,
    string? BeforeCorrectnessAssessmentId = null);
