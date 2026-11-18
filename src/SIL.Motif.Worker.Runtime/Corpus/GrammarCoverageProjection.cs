using System.Text;
using SIL.Motif.Worker.Corpus.Adapter;

namespace SIL.Motif.Worker.Corpus;

internal sealed class GrammarCoverageProjection
{
    internal const string Policy = "grammar-only-exact-then-fallback-v4";
    internal static readonly CoverageLimitation JudgementsUnavailable = new("judgement-evidence-unavailable");
    internal static readonly CoverageLimitation PackUnavailable = new("pack-evidence-unavailable");

    private static readonly (CheckLimitation Limitation, string Code)[] Reasons =
    [
        (CheckLimitation.Missing, "missing-check"),
        (CheckLimitation.Stopped, "stopped-check"),
        (CheckLimitation.Capped, "capped-check"),
        (CheckLimitation.Incomplete, "incomplete-check"),
        (CheckLimitation.ReadingIdentity, "reading-identity-unavailable"),
        (CheckLimitation.OtherWritingSystem, "other-writing-system"),
        (CheckLimitation.InvalidShape, "invalid-shape"),
        (CheckLimitation.Unavailable, "check-evidence-unavailable"),
        (CheckLimitation.UnsupportedGrammar, "unsupported-grammar"),
        (CheckLimitation.RetryDiagnostics, "retry-diagnostics-unavailable"),
    ];

    private readonly Tallies _occurrences = new();
    private readonly Tallies _wordforms = new();
    private readonly SortedDictionary<string, long> _limitations = new(StringComparer.Ordinal);
    private long _digits;
    private long _otherWritingSystems;

    public List<RankedWordform> Ranked { get; } = [];

    public void Add(FrequencyRow row, WordEvidence evidence, string writingSystem)
    {
        var form = row.CasedForm ?? row.ParserKey;
        if (form.EnumerateRunes().All(Rune.IsDigit)) _digits += row.Frequency;
        if (row.WritingSystemId != writingSystem) _otherWritingSystems += row.Frequency;

        _occurrences.Add(evidence, row.Frequency);
        _wordforms.Add(evidence, 1);
        foreach (var (limitation, code) in Reasons)
            if (evidence.Limitations.HasFlag(limitation))
                _limitations[code] = _limitations.GetValueOrDefault(code) + row.Frequency;
        if (evidence.Analysed != false) return;

        Ranked.Add(new(row.WordformId, row.WritingSystemId, form, row.Frequency));
    }

    public GrammarCoverageSummary Summarize(GrammarCoverageInventory inventory, CorpusStamp stamp, string assessmentId,
        AssessmentInputBinding inputBinding)
    {
        var aggregate = new CoverageAggregate(_occurrences.Total, _wordforms.Total, _occurrences.Exact(),
            _wordforms.Exact(), new(), _occurrences.Reach(), _wordforms.Reach(), new(), new(), _digits,
            _otherWritingSystems, _occurrences.UnknownShape == 0 ? _occurrences.InvalidShape : null,
            inventory.Provenance.Exclusions.Values.Sum(), null, null);
        List<CoverageLimitation> limitations = [JudgementsUnavailable, PackUnavailable];
        limitations.AddRange(_limitations.Select(item => new CoverageLimitation(item.Key, item.Value)));
        if (_occurrences.UnknownShape != 0) limitations.Add(new("invalid-shape-unmeasured", _occurrences.UnknownShape));
        return new(stamp, assessmentId, inventory.Provenance, inventory.MappingDigest, inventory.WeightDigest,
            aggregate, inputBinding, _occurrences.Proved(), _wordforms.Proved(), _occurrences.Unknown(), _wordforms.Unknown(),
            _occurrences.InvalidShape, _wordforms.InvalidShape, Ranked.Count,
            _occurrences.Unmeasured == 0 ? "complete" : _occurrences.UnknownAnalysis == _occurrences.Total
                ? "unmeasured" : "partial", limitations);
    }

    private sealed class Tallies
    {
        public long Total { get; private set; }
        public long Unmeasured { get; private set; }
        public long UnknownAnalysis { get; private set; }
        public long InvalidShape { get; private set; }
        public long UnknownShape { get; private set; }
        private long _analysed;
        private long _noAnalysis;
        private long _ambiguous;
        private long _unknownAmbiguity;

        public void Add(WordEvidence evidence, long weight)
        {
            Total = checked(Total + weight);
            if (evidence.Analysed == true) _analysed += weight;
            if (evidence.Analysed == false) _noAnalysis += weight;
            if (evidence.Analysed is null) UnknownAnalysis += weight;
            if (evidence.Ambiguous == true) _ambiguous += weight;
            if (evidence.Ambiguous is null) _unknownAmbiguity += weight;
            if (evidence.InvalidShape == true) InvalidShape += weight;
            if (evidence.InvalidShape is null) UnknownShape += weight;
            if (evidence.Limitations != 0 || evidence is { Analysed: null } or { Ambiguous: null } or { InvalidShape: null })
                Unmeasured += weight;
        }

        public BucketCounts Exact() => new(UnknownAnalysis == 0 ? _analysed : null,
            UnknownAnalysis == 0 ? _noAnalysis : null, null, _unknownAmbiguity == 0 ? _ambiguous : null, null, Unmeasured);

        public BucketCounts Proved() => new(_analysed, _noAnalysis, null, _ambiguous, null, Unmeasured);

        public GrammarCoverageUnknownCounts Unknown() =>
            new(UnknownAnalysis, UnknownAnalysis, _unknownAmbiguity, UnknownShape, Total, Total, Total, Total, Total, Total - _analysed);

        public ReachCounts Reach() => new(UnknownAnalysis == 0 ? _analysed : null, null,
            _analysed == Total ? Total : null, Total - UnknownAnalysis);
    }
}

internal sealed record RankedWordform(string Id, string WritingSystem, string FormNfd, long Frequency)
{
    public WordformCoverage ToRow() => new(new(WritingSystem, FormNfd), Id, Frequency,
        new(0, Frequency, null, 0, null, 0), new(), new(0, null, null, Frequency), new(), new(),
        [GrammarCoverageWire.ToContract(GrammarCoverageProjection.JudgementsUnavailable),
         GrammarCoverageWire.ToContract(GrammarCoverageProjection.PackUnavailable)]);
}
