namespace SIL.Motif.Contract.Responses;

/// <summary>The verified result of preparing a FieldWorks project for Motif human judgments.</summary>
/// <param name="Status"><c>initialized</c> when Motif created the definition, or <c>already-initialized</c> when it reused one.</param>
/// <param name="FieldName">The reserved internal field name.</param>
/// <param name="RecoveryCopyPath">A retained original project copy when cleanup could not be completed.</param>
public sealed record ProjectInitializationResponse(string Status, string FieldName, string? RecoveryCopyPath);
