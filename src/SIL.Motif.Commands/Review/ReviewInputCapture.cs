using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Config;
using SIL.Motif.Projection.Review;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands.Review;

/// <summary>A configured set whose membership has already been read from its source.</summary>
public sealed record ReviewCaptureSet(string Id, string Name, string Role, string SourceDigest,
    IReadOnlyList<ReviewWordSetMember> Members);

/// <summary>Builds the immutable scope and expectation inputs for both sides of a Trial.</summary>
public static class ReviewInputCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Freezes the selected sets, touched words and available Baseline assertions.</summary>
    public static Task<FrozenReviewInputs> CaptureAsync(string projectKey, string proposalId, string? parserDigest,
        IReadOnlyList<ReviewCaptureSet> sets, IReadOnlyList<string>? selectedSetIds,
        IReadOnlyList<string> touchedWords, IReadOnlyList<ReviewProjectWordIdentity> identities,
        IReadOnlyList<ReviewAssertion> assertions, IReadOnlyList<ReviewNegativeExpectation> negatives,
        CancellationToken cancellationToken = default, string? policyDigest = null)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(touchedWords);
        var declared = sets.ToDictionary(set => set.Id, StringComparer.Ordinal);
        var selected = selectedSetIds is null ? declared.Keys.ToHashSet(StringComparer.Ordinal)
            : selectedSetIds.ToHashSet(StringComparer.Ordinal);
        if (selectedSetIds is not null && selected.Count != selectedSetIds.Count)
            throw new ArgumentException("A review set cannot be selected more than once.", nameof(selectedSetIds));
        if (selected.Any(id => !declared.ContainsKey(id)))
            throw new ArgumentException("A selected review set is not declared.", nameof(selectedSetIds));
        var omitted = declared.Keys.Except(selected, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var cases = new Dictionary<string, CaseBuilder>(StringComparer.Ordinal);
        var sourceCases = new Dictionary<string, (string Word, string? WritingSystemId, string? WordformId)>(StringComparer.Ordinal);
        var bindings = new List<ReviewSetBinding>();
        foreach (var set in sets.Where(set => selected.Contains(set.Id)).OrderBy(set => set.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memberships = new List<(string Word, string WritingSystemId, string? WordformId, ReviewMembership Value)>();
            var setAssertions = new List<ReviewAssertion>();
            foreach (var member in set.Members.OrderBy(member => member.CaseId, StringComparer.Ordinal))
            {
                var word = member.Word.Normalize(NormalizationForm.FormD);
                var writingSystemId = string.IsNullOrWhiteSpace(member.WritingSystemId) ? null : member.WritingSystemId;
                var sourceIdentity = (word, writingSystemId, member.WordformId);
                if (sourceCases.TryGetValue(member.CaseId, out var existing) && existing != sourceIdentity)
                    throw new InvalidOperationException($"Review case '{member.CaseId}' names conflicting identities.");
                sourceCases[member.CaseId] = sourceIdentity;
                var identity = member.WordformId is null ? null : identities.SingleOrDefault(item =>
                    item.WordformId == member.WordformId && item.WritingSystemId == writingSystemId && item.Word == word);
                if (member.WordformId is not null && identity is null)
                    throw new InvalidOperationException($"Review case '{member.CaseId}' has an unresolved project word identity.");
                var wordKey = identity?.WordKey ?? Hash(new { writingSystemId, word });
                var caseKey = Hash(new { wordKey, member.CaseId });
                if (!cases.TryGetValue(wordKey, out var item))
                    cases[wordKey] = item = new(caseKey, wordKey, word, writingSystemId, member.WordformId);
                var membership = new ReviewMembership(set.Id, member.CaseId, set.Role, member.Token?.Weight ?? 1,
                    member.Cell?.CellId, member.Cell?.VariantId, Array.AsReadOnly(member.ExpectationRefs.ToArray()))
                {
                    OccurrenceId = member.Token?.OccurrenceId,
                    TextId = member.Token?.TextId,
                    SourceLocator = member.Token?.ExternalSentenceLocator ?? member.Source.Locator,
                    Position = member.Token?.Position,
                    LexemeId = member.Cell?.LexemeId,
                    Features = Array.AsReadOnly(member.Cell?.Features.Select(feature =>
                        new FeatureValue(feature.FeatureId, feature.ValueId)).ToArray() ?? []),
                    SourceRefs = Array.AsReadOnly(new[] { member.Source.Citation, member.Source.Locator }
                        .OfType<string>().ToArray()),
                };
                item.Memberships.Add(membership);
                memberships.Add((word, member.WritingSystemId, member.WordformId, membership));
                var matchingWordforms = identities.Where(value => value.Word == word &&
                    value.WritingSystemId == member.WritingSystemId &&
                    (member.WordformId is null || member.WordformId == value.WordformId))
                    .Select(value => value.WordformId).Distinct(StringComparer.Ordinal).ToArray();
                foreach (var assertion in assertions.Where(value => References(member, value) &&
                             matchingWordforms.Length == 1 && SourceWordform(value) == matchingWordforms[0]))
                {
                    var frozen = FreezeAssertion(assertion);
                    item.Assertions.TryAdd(frozen.Id, frozen);
                    setAssertions.Add(frozen);
                }
                foreach (var negative in negatives.Where(value => value.CaseId == member.CaseId &&
                             value.WritingSystem == member.WritingSystemId && value.Word == word &&
                             References(member, value.Assertion)))
                {
                    var frozen = FreezeAssertion(negative.Assertion);
                    item.Assertions.TryAdd(frozen.Id, frozen);
                    setAssertions.Add(frozen);
                }
            }
            bindings.Add(new(set.Id, set.Name, set.Role, Hash(memberships.Select(value => new
                { value.Word, value.WritingSystemId, value.WordformId, value.Value })),
                Hash(setAssertions.OrderBy(value => value.Id, StringComparer.Ordinal)
                    .ThenBy(value => value.RevisionId, StringComparer.Ordinal)), set.SourceDigest,
                Array.AsReadOnly(set.Members.Select(member => member.Source.Citation).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray())));
        }
        foreach (var word in touchedWords.Select(word => word.Normalize(NormalizationForm.FormD)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = identities.Where(item => item.Word == word).ToArray();
            if (matches.Length == 0)
            {
                if (cases.Values.Any(item => item.Word == word)) continue;
                var unresolvedKey = Hash(new { unresolved = word });
                cases.Add(unresolvedKey, new CaseBuilder(Hash(new { wordKey = unresolvedKey, source = "touched" }),
                    unresolvedKey, word, null, null));
                continue;
            }
            foreach (var match in matches)
            {
                var wordKey = match.WordKey ?? Hash(new { unresolved = word, match.WordformId, match.WritingSystemTag });
                if (cases.ContainsKey(wordKey)) continue;
                var item = new CaseBuilder(Hash(new { wordKey, source = "touched" }), wordKey, word,
                    match.WritingSystemId, match.WordformId);
                foreach (var assertion in assertions.Where(value => SourceWordform(value) == match.WordformId))
                    item.Assertions.TryAdd(assertion.Id, FreezeAssertion(assertion));
                cases.Add(wordKey, item);
            }
        }
        var rows = cases.Values.Select(item => item.Freeze()).OrderBy(item => item.CaseKey, StringComparer.Ordinal).ToArray();
        var scopeDigest = Hash(new
        {
            policyDigest,
            cases = rows.Select(item => new { item.CaseKey, item.WordKey, item.Word, item.WritingSystemId }),
        });
        var evidenceDigest = Hash(new { scopeDigest, sets = bindings, cases = rows, omitted });
        return Task.FromResult(new FrozenReviewInputs(projectKey, proposalId, scopeDigest, evidenceDigest, parserDigest,
            Array.AsReadOnly(bindings.ToArray()), Array.AsReadOnly(omitted), Array.AsReadOnly(rows)));
    }

    /// <summary>Separates parser inputs whose writing-system identities differ.</summary>
    public static IReadOnlyList<ReviewParsePartition> Partitions(FrozenReviewInputs inputs)
    {
        return ReviewInputPartitions.Create(inputs);
    }

    private static string Hash<T>(T value) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private static bool References(ReviewWordSetMember member, ReviewAssertion assertion) =>
        member.ExpectationRefs.Any(reference => reference == assertion.Id || reference == assertion.RevisionId ||
            assertion.SourceRefs.Contains(reference, StringComparer.Ordinal));

    private static string? SourceWordform(ReviewAssertion assertion) =>
        Guid.TryParse(assertion.Morphology?.SourceWordformGuid, out var guid)
            ? SIL.Motif.Contract.Ids.CanonicalId.FromGuid(guid).Value : null;

    private static ReviewAssertion FreezeAssertion(ReviewAssertion assertion) => assertion with
    {
        SourceRefs = Array.AsReadOnly(assertion.SourceRefs.ToArray()),
        Morphology = assertion.Morphology is not { } morphology ? null : morphology with
        {
            Morphs = Array.AsReadOnly(morphology.Morphs.Select(morph => morph with
                { Forms = Array.AsReadOnly(morph.Forms.ToArray()) }).ToArray()),
        },
    };

    private sealed record CaseBuilder(string CaseKey, string WordKey, string Word, string? WritingSystemId, string? WordformId)
    {
        public List<ReviewMembership> Memberships { get; } = [];
        public Dictionary<string, ReviewAssertion> Assertions { get; } = new(StringComparer.Ordinal);
        public ReviewPlannedCase Freeze() => new(CaseKey, WordKey, Word, WritingSystemId, WordformId,
            Array.AsReadOnly(Memberships.ToArray()), Array.AsReadOnly(Assertions.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()));
    }
}
