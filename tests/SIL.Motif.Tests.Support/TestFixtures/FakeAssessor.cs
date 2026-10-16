using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A pure, in-process <see cref="IAssessor"/> that declares a fixed set of kinds and honours the same
/// all-or-nothing refusal contract a real Assessor must: nothing is produced when a requested kind is not
/// among the declared ones.
/// </summary>
internal sealed class FakeAssessor : IAssessor
{
    private const string FakeGrammarSha256 = "sha256:" + "ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff";

    private readonly IReadOnlyList<AssessmentKind> _declaredKinds;
    private readonly Func<AssessmentKind, AssessmentRaw>? _rawFor;
    private readonly Func<AssessmentScope.Words, string, AssessmentKind, AssessmentRaw>? _rawForScope;

    public FakeAssessor(
        string name, IReadOnlyList<AssessmentKind> declaredKinds, Func<AssessmentKind, AssessmentRaw>? rawFor = null,
        Func<AssessmentScope.Words, string, AssessmentKind, AssessmentRaw>? rawForScope = null)
    {
        Name = name;
        _declaredKinds = declaredKinds;
        _rawFor = rawFor;
        _rawForScope = rawForScope;
    }

    public string Name { get; }

    public IReadOnlyList<AssessmentKind> SupportedKinds => _declaredKinds;

    public Func<AssessmentScope.Words, string, BatchInvocationEvidence>? CaptureEvidence { get; init; }

    public Action<Action<TrialWordProgress>>? EmitProgress { get; init; }

    public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, Action<TrialWordProgress> onProgress,
        CancellationToken cancellationToken)
    {
        EmitProgress?.Invoke(onProgress);
        return ProduceAsync(scope, exportedCandidate, cancellationToken);
    }

    public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
    {
        var words = scope as AssessmentScope.Words
            ?? throw new AssessorRefusalException(Name, AssessmentKind.PhonologyExamples, "This fixture measures words.");
        var wanted = words.Collect.Count == 0 ? _declaredKinds : words.Collect;
        foreach (var kind in wanted)
        {
            if (!_declaredKinds.Contains(kind))
                throw new AssessorRefusalException(Name, kind, "the fake Assessor was not configured to declare this kind.");
        }

        var evidence = CaptureEvidence?.Invoke(words, exportedCandidate);
        var grammarSourceSha256 = evidence?.SourceBytesSha256 ?? FakeGrammarSha256;
        IReadOnlyList<ProducedAssessment> produced = wanted
            .Select(kind => new ProducedAssessment(kind, grammarSourceSha256, "sha256:" + new string('0', 64),
                "sha256:" + new string('0', 64), "fake-model", "fake-pipeline", 0,
                _rawForScope?.Invoke(words, exportedCandidate, kind) ?? _rawFor?.Invoke(kind)
                    ?? new AssessmentRaw.WordMeasurements([])) { Invocation = evidence })
            .ToList();
        return Task.FromResult(produced);
    }
}
