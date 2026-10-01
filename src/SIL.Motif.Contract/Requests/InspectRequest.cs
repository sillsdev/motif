using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Asks what Motif knows about one inspector subject: FieldWorks' facts from the current Baseline, the words that
/// use it and the words it ran in from the stored Parse all words, and the stored grammar check's findings that
/// name it. Each section is read from its own source, and one that can't be read never stops the others.
/// </summary>
/// <param name="ProjectPath">The project whose current Baseline, default Selection and stored check to read.</param>
/// <param name="Subject">The subject, by identity.</param>
public sealed record InspectRequest(string ProjectPath, InspectorSubject Subject);
