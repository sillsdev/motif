namespace SIL.Motif.Commands.Requests;

/// <summary>A question for a speaker as it arrives in a hypothesis record.</summary>
/// <param name="Question">A word judgement or minimal pair.</param>
/// <param name="Predicted">The answer the hypothesis predicts, kept private.</param>
public sealed record HypothesisQuestionDraft(string Question, string? Predicted = null);

/// <summary>
/// A hypothesis as <c>hypothesis add --file</c> reads it; the JSON names are the snake_case of these properties and match
/// the fields of the hypothesis record in the discovery skill.
/// </summary>
/// <param name="Statement">The broadest statement that fits.</param>
/// <param name="Mechanism">The mechanism chosen on the ladder.</param>
/// <param name="Change">The change the hypothesis is about, such as <c>d&gt;r</c>; with the environment, host class and slot it is the shape the neighbour check queries.</param>
/// <param name="Neighbour">The neighbour query and what it returned, or the rule widened; required, so the check is on record.</param>
/// <param name="Pattern">The regularity, stated over forms.</param>
/// <param name="Environment">The environment the change fires in.</param>
/// <param name="HostClass">The host class.</param>
/// <param name="Slot">The morphological slot.</param>
/// <param name="Predictions">The predicted forms beyond the ones that suggested the hypothesis.</param>
/// <param name="Falsifier">The observation that would refute it.</param>
/// <param name="Exceptions">The expected exceptions and the polarity.</param>
/// <param name="ObjectsAdded">Objects the encoding adds.</param>
/// <param name="AttestedTypesGained">Attested held-out types the encoding gains.</param>
/// <param name="Questions">Speaker questions asked or queued.</param>
public sealed record HypothesisDraft(
    string Statement, string Mechanism, string Change, string Neighbour, string? Pattern = null,
    string? Environment = null, string? HostClass = null, string? Slot = null, IReadOnlyList<string>? Predictions = null,
    string? Falsifier = null, string? Exceptions = null, int? ObjectsAdded = null, int? AttestedTypesGained = null,
    IReadOnlyList<HypothesisQuestionDraft>? Questions = null);

/// <summary>A test result as <c>hypothesis test --file</c> reads it: the JSON <c>control_test.py --json</c> prints, plus optional Trial numbers.</summary>
/// <param name="Verdict"><c>PASS</c>, <c>FAIL</c> or <c>UNDECIDED</c>.</param>
/// <param name="Predicted">How many forms the rule predicted.</param>
/// <param name="RuleHits">Rule hits.</param>
/// <param name="Controls">Hits for each control, by name.</param>
/// <param name="Margin">Rule hits minus the best control; recomputed when left out.</param>
/// <param name="TrialAttestedLosses">Attested words lost in the Trial.</param>
/// <param name="TrialNewReadings">New readings on words that already parsed.</param>
/// <param name="TrialWorkSteps">The Trial's change in work steps.</param>
/// <param name="Command">The command that produced the numbers.</param>
public sealed record HypothesisTestResult(
    string Verdict, int Predicted, int RuleHits, IReadOnlyDictionary<string, int>? Controls = null, int? Margin = null,
    int? TrialAttestedLosses = null, int? TrialNewReadings = null, int? TrialWorkSteps = null, string? Command = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Hypothesis">The hypothesis to state.</param>
public sealed record AddHypothesisRequest(string FwDataPath, string ProductVersion, HypothesisDraft Hypothesis);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="HypothesisId">The hypothesis tested.</param>
/// <param name="Result">The test result.</param>
public sealed record RecordHypothesisTestRequest(
    string FwDataPath, string ProductVersion, string HypothesisId, HypothesisTestResult Result);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="HypothesisId">The hypothesis decided.</param>
/// <param name="Status"><c>passed</c>, <c>failed</c> or <c>superseded</c>.</param>
/// <param name="Decision">The decision, in words; required.</param>
/// <param name="SupersededBy">For <c>superseded</c>: the hypothesis that replaces this one.</param>
/// <param name="Supersedes">For <c>passed</c>: earlier passed hypotheses on the same shape that this one replaces.</param>
public sealed record DecideHypothesisRequest(
    string FwDataPath, string ProductVersion, string HypothesisId, string Status, string Decision,
    string? SupersededBy = null, IReadOnlyList<string>? Supersedes = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="HypothesisId">The hypothesis linked.</param>
/// <param name="ProposalIds">Proposals that carry the rule change.</param>
/// <param name="GdrId">The GDR changed, as <c>GDR-n</c>.</param>
/// <param name="GdrSection">The section of that GDR changed.</param>
/// <param name="GdrNone">Why no GDR changed; excludes <paramref name="GdrId"/>.</param>
public sealed record LinkHypothesisRequest(
    string FwDataPath, string ProductVersion, string HypothesisId, IReadOnlyList<string>? ProposalIds = null,
    string? GdrId = null, string? GdrSection = null, string? GdrNone = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="HypothesisId">The hypothesis graded.</param>
/// <param name="SealedVerdict"><c>match</c> or <c>mismatch</c> against the answer key.</param>
/// <param name="VerdictSource">Who gave the verdict the loop was graded against.</param>
/// <param name="WordQuestionsAsked">How many word questions the loop needed.</param>
/// <param name="ObjectsBefore">Objects for the pattern before.</param>
/// <param name="TypesBefore">Attested held-out types before.</param>
/// <param name="ObjectsAfter">Objects after.</param>
/// <param name="TypesAfter">Attested held-out types after.</param>
/// <param name="AgentHours">Agent hours the hypothesis took.</param>
/// <param name="AlternativeHours">Agent hours of the list-package alternative.</param>
public sealed record GradeHypothesisRequest(
    string FwDataPath, string ProductVersion, string HypothesisId, string? SealedVerdict = null,
    string? VerdictSource = null, int? WordQuestionsAsked = null, int? ObjectsBefore = null, int? TypesBefore = null,
    int? ObjectsAfter = null, int? TypesAfter = null, double? AgentHours = null, double? AlternativeHours = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="HypothesisId">The hypothesis the question belongs to.</param>
/// <param name="Question">The 1-based number of the question in the hypothesis's list.</param>
/// <param name="Answer">The speaker's answer.</param>
public sealed record AnswerHypothesisQuestionRequest(
    string FwDataPath, string ProductVersion, string HypothesisId, int Question, string Answer);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Change">The change, such as <c>d&gt;r</c>; required, the index is keyed on it.</param>
/// <param name="Environment">The environment; leave out to match any.</param>
/// <param name="HostClass">The host class; leave out to match any.</param>
/// <param name="Slot">The slot; leave out to match any.</param>
public sealed record HypothesisNeighboursRequest(
    string FwDataPath, string ProductVersion, string? Change = null, string? Environment = null, string? HostClass = null,
    string? Slot = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Status">One status; leave out for all.</param>
/// <param name="HypothesisId">One hypothesis by id; leave out for a page of them.</param>
/// <param name="Offset">How many to skip.</param>
/// <param name="Limit">The most to return, from 1 to 200.</param>
public sealed record ListHypothesesRequest(
    string FwDataPath, string ProductVersion, string? Status = null, string? HypothesisId = null, int Offset = 0, int Limit = 50);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Kind"><c>decisions</c> for the decision record, <c>questions</c> for the speaker-question log.</param>
public sealed record HypothesisReportRequest(string FwDataPath, string ProductVersion, string Kind);
