namespace SIL.Motif.Contract.Responses;

/// <summary>One question for a speaker. The predicted answer is the author's private guess, never shown to a speaker.</summary>
/// <param name="Question">A word judgement or minimal pair, with no linguistic terms.</param>
/// <param name="Predicted">The answer the hypothesis predicts, or null.</param>
/// <param name="Answer">The speaker's answer, or null while the question is unanswered.</param>
public sealed record HypothesisQuestionItem(string Question, string? Predicted, string? Answer);

/// <summary>One recorded test of a hypothesis: the output of <c>rule-hypothesis-test</c> plus any Trial numbers.</summary>
/// <param name="TestNumber">1 for the first test of the hypothesis, then up.</param>
/// <param name="Verdict"><c>PASS</c>, <c>FAIL</c> or <c>UNDECIDED</c>.</param>
/// <param name="PredictedForms">How many forms the rule predicted beyond the ones that suggested it.</param>
/// <param name="RuleHits">Predicted forms attested in held-out books and absent from discovery.</param>
/// <param name="Controls">Hits for each control letter or environment, by name.</param>
/// <param name="Margin">Rule hits minus the best control's hits.</param>
/// <param name="TrialAttestedLosses">Attested words the rule's Trial lost, or null when no Trial was recorded.</param>
/// <param name="TrialNewReadings">New readings on words that already parsed, or null.</param>
/// <param name="TrialWorkSteps">The Trial's change in work steps, or null.</param>
/// <param name="Command">The command that produced the numbers, or null.</param>
/// <param name="RecordedUtc">When the test was recorded.</param>
public sealed record HypothesisTestItem(
    int TestNumber, string Verdict, int PredictedForms, int RuleHits, IReadOnlyDictionary<string, int> Controls, int Margin,
    int? TrialAttestedLosses, int? TrialNewReadings, int? TrialWorkSteps, string? Command, string RecordedUtc);

/// <summary>The four numbers issue 185 records for each hypothesis, once it has been graded.</summary>
/// <param name="SealedVerdict"><c>match</c> when the sealed loop reached the verdict the answer key gives, <c>mismatch</c> when not, null before grading.</param>
/// <param name="VerdictSource">Who gave the verdict the loop was graded against, for example Wolff or Ron.</param>
/// <param name="WordQuestionsAsked">How many word questions the loop needed.</param>
/// <param name="ObjectsBefore">Objects the grammar held for the pattern before the hypothesis.</param>
/// <param name="TypesBefore">Attested held-out types the pattern parsed before.</param>
/// <param name="ObjectsAfter">Objects after.</param>
/// <param name="TypesAfter">Attested held-out types after.</param>
/// <param name="ObjectsPerTypeBefore">Objects per attested type before, or null when either count is missing or the types are zero.</param>
/// <param name="ObjectsPerTypeAfter">Objects per attested type after, or null.</param>
/// <param name="AgentHours">Agent hours the hypothesis took.</param>
/// <param name="AlternativeHours">Agent hours the list-package alternative would take.</param>
public sealed record HypothesisGradeItem(
    string? SealedVerdict, string? VerdictSource, int? WordQuestionsAsked, int? ObjectsBefore, int? TypesBefore,
    int? ObjectsAfter, int? TypesAfter, double? ObjectsPerTypeBefore, double? ObjectsPerTypeAfter, double? AgentHours,
    double? AlternativeHours);

/// <summary>One hypothesis as a front end shows it: the record, with its tests, Proposals and graded numbers.</summary>
/// <param name="HypothesisId">The permanent id, <c>H-n</c>.</param>
/// <param name="Pattern">The regularity, stated over forms.</param>
/// <param name="Statement">The broadest statement that fits.</param>
/// <param name="Mechanism">The mechanism chosen on the ladder.</param>
/// <param name="Change">The change, such as <c>d&gt;r</c>.</param>
/// <param name="Environment">The environment the change fires in; empty when it is not conditioned.</param>
/// <param name="HostClass">The host class; empty when none.</param>
/// <param name="Slot">The morphological slot; empty when none.</param>
/// <param name="Neighbour">The neighbour query and its result.</param>
/// <param name="Predictions">The predicted forms beyond the ones that suggested the hypothesis.</param>
/// <param name="Falsifier">The observation that would refute it.</param>
/// <param name="Exceptions">The expected exceptions, with polarity and the number of objects tagged.</param>
/// <param name="ObjectsAdded">Objects the encoding adds.</param>
/// <param name="AttestedTypesGained">Attested held-out types the encoding gains.</param>
/// <param name="Questions">Speaker questions asked or queued.</param>
/// <param name="Status"><c>proposed</c>, <c>passed</c>, <c>failed</c> or <c>superseded</c>.</param>
/// <param name="Decision">The decision text; null while proposed.</param>
/// <param name="SupersededBy">The hypothesis that replaces this one, or null.</param>
/// <param name="GdrId">The Grammar Design Record this hypothesis changed, as <c>GDR-n</c>, or null.</param>
/// <param name="GdrSection">The section of that GDR.</param>
/// <param name="GdrNone">Why no GDR changed, when none did.</param>
/// <param name="Proposals">The Proposals that carry the hypothesis's rule change, oldest link first.</param>
/// <param name="Tests">Every recorded test, oldest first.</param>
/// <param name="Grade">The four numbers, all null before grading.</param>
/// <param name="CreatedUtc">When it was stated.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
public sealed record HypothesisItem(
    string HypothesisId, string? Pattern, string Statement, string Mechanism, string Change, string Environment,
    string HostClass, string Slot, string? Neighbour, IReadOnlyList<string> Predictions, string? Falsifier,
    string? Exceptions, int? ObjectsAdded, int? AttestedTypesGained, IReadOnlyList<HypothesisQuestionItem> Questions,
    string Status, string? Decision, string? SupersededBy, string? GdrId, string? GdrSection, string? GdrNone,
    IReadOnlyList<string> Proposals, IReadOnlyList<HypothesisTestItem> Tests, HypothesisGradeItem Grade,
    string CreatedUtc, string UpdatedUtc);

/// <summary>A hypothesis in a neighbour list: enough to decide whether to widen it.</summary>
/// <param name="HypothesisId">The permanent id.</param>
/// <param name="Status">Its status.</param>
/// <param name="Mechanism">The mechanism it chose.</param>
/// <param name="Change">Its change.</param>
/// <param name="Environment">Its environment.</param>
/// <param name="Statement">Its statement.</param>
/// <param name="Decision">Its decision, or null.</param>
/// <param name="LatestVerdict">Its latest test verdict, or null when untested.</param>
public sealed record HypothesisNeighbour(
    string HypothesisId, string Status, string Mechanism, string Change, string Environment, string Statement,
    string? Decision, string? LatestVerdict);

/// <summary>A hypothesis changed, with the neighbours on its shape.</summary>
/// <param name="Hypothesis">The hypothesis after the change.</param>
/// <param name="Neighbours">Other hypotheses on the same shape; empty when none.</param>
public sealed record HypothesisResponse(HypothesisItem Hypothesis, IReadOnlyList<HypothesisNeighbour> Neighbours);

/// <summary>The neighbour check: what the ledger already says about a change, environment, host class and slot.</summary>
/// <param name="Query">The query as the ledger read it.</param>
/// <param name="Neighbours">Every hypothesis on that shape, in id order.</param>
/// <param name="HasPassed">Whether a passed hypothesis stands on the shape: widen or merge it before authoring anything.</param>
/// <param name="HasFailedAttempt">Whether a rule attempt is on record as failed for the shape, which permits a list.</param>
public sealed record HypothesisNeighboursResponse(
    string Query, IReadOnlyList<HypothesisNeighbour> Neighbours, bool HasPassed, bool HasFailedAttempt);

/// <summary>One page of hypotheses in id order.</summary>
/// <param name="Hypotheses">The hypotheses on this page.</param>
/// <param name="Total">How many match the status asked for.</param>
/// <param name="Offset">How many were skipped.</param>
public sealed record HypothesisListResponse(IReadOnlyList<HypothesisItem> Hypotheses, int Total, int Offset);

/// <summary>A record generated from the ledger.</summary>
/// <param name="Kind"><c>decisions</c> or <c>questions</c>.</param>
/// <param name="Text">The record as Markdown.</param>
/// <param name="Count">How many hypotheses (decisions) or questions the record lists.</param>
public sealed record HypothesisReportResponse(string Kind, string Text, int Count);
