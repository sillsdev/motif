using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>The frozen manifest, summary and estimated index cost for one report publication.</summary>
public sealed record ReviewReportPublication(string ProjectKey, string ProposalId, string TrialJobId,
    string ManifestDigest, ReportView Summary, IReadOnlyList<string> InputReferences, long EstimatedDetailBytes)
{
    /// <summary>Sections whose complete rows the producer captured, including explicitly empty sections.</summary>
    public IReadOnlyList<string> AvailableSections { get; init; } = [];

    /// <summary>Rows from retained report evidence in their deterministic section order.</summary>
    public IAsyncEnumerable<ReviewSectionItem>? SectionItems { get; init; }
}

/// <summary>One retained section row and the scalar selectors used to find it.</summary>
public sealed record ReviewSectionItem(string Section, ReviewRow Row, string? Set = null, string? Outcome = null,
    IReadOnlyList<ReviewFlagId>? Flags = null, bool Changed = false, bool Incomplete = false, string? Group = null);

/// <summary>The stored summary and retained input references for a report.</summary>
public sealed record StoredReviewReport(ReportView Summary, bool DetailAvailable, IReadOnlyList<string> InputReferences);

/// <summary>Publishes immutable review summaries and complete detail indexes in one transaction.</summary>
public sealed partial class ReviewReportRepository(MotifDatabase database)
{
    private const long Budget = 100_000_000;
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Stores a complete streamed index or an explicitly unavailable detail summary.</summary>
    public async Task<ReviewReportId> PublishAsync(ReviewReportPublication publication,
        IAsyncEnumerable<ReviewCaseDelta> cases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentOutOfRangeException.ThrowIfNegative(publication.EstimatedDetailBytes);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var existing = Command(connection, transaction,
            "SELECT ReportId FROM ReviewReports WHERE ProjectKey=$project AND ManifestDigest=$digest;",
            ("$project", publication.ProjectKey), ("$digest", publication.ManifestDigest)))
        {
            if (existing.ExecuteScalar() is string id) return new ReviewReportId(id);
        }
        string status;
        using (var proposal = Command(connection, transaction, "SELECT Status FROM Proposals WHERE ProposalId=$id;",
            ("$id", publication.ProposalId)))
            status = proposal.ExecuteScalar() as string ?? throw new InvalidOperationException("Proposal does not exist.");
        var released = !ProposalDifferenceRetention.KeepsWordDetail(status);
        var reportId = new ReviewReportId("review-report/" + Guid.NewGuid().ToString("N"));
        var committed = false;
        try
        {
            var remaining = Budget - Math.Min(Budget, AssessmentRetention.BytesInUse(connection, transaction));
            var available = !released && publication.EstimatedDetailBytes <= remaining;
            var summary = publication.Summary with { ReportId = reportId.Value, ReportDigest = publication.ManifestDigest };
            var summaryJson = JsonSerializer.Serialize(summary, ReviewJson.Options);
            Execute(connection, transaction, "INSERT INTO ReviewReports (ReportId,ProjectKey,ProposalId,TrialJobId,ManifestDigest,SummaryJson,SummaryDigest,InputReferencesJson,DetailAvailable,DetailBytes,CreatedUtc) VALUES ($id,$project,$proposal,$job,$manifest,$summary,$hash,$refs,$available,0,$created);",
                ("$id", reportId.Value), ("$project", publication.ProjectKey), ("$proposal", publication.ProposalId),
                ("$job", publication.TrialJobId), ("$manifest", publication.ManifestDigest), ("$summary", summaryJson),
                ("$hash", Hash(summaryJson)), ("$refs", JsonSerializer.Serialize(released ? [] : publication.InputReferences)),
                ("$available", available ? 1 : 0), ("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            long detailBytes = 0;
            if (available)
            {
                await foreach (var item in cases.WithCancellation(cancellationToken))
                {
                    if (item.CaseKey.StartsWith("\u001f", StringComparison.Ordinal))
                        throw new InvalidDataException("The review case key uses a reserved prefix.");
                    var referenceIds = item.Memberships.Where(member => member.Role is "reference" or "regression")
                        .SelectMany(member => member.ExpectationIds).ToHashSet(StringComparer.Ordinal);
                    var beforeCounts = CountSide(item, item.Before, referenceIds);
                    var afterCounts = CountSide(item, item.After, referenceIds);
                    var facts = JsonSerializer.Serialize(new { item.NewNoParse, item.NewApprovedLoss, item.NewlyAcceptedNegative,
                        item.Flags, item.Limitations, item.Before, item.After, BeforeCounts = beforeCounts, AfterCounts = afterCounts,
                        ReadingsAvailable = item.Readings is not null,
                        ExpectationsAvailable = item.AssertionsCaptured || item.Assertions.Count > 0,
                        SourcesAvailable = item.Sources is not null,
                        Changed = item.Changed ?? (item.NewNoParse || item.NewApprovedLoss || item.NewlyAcceptedNegative ||
                            item.ReadingLosses.Count > 0 ? true : null) }, ReviewJson.Options);
                    detailBytes = checked(detailBytes + Cost(item.CaseKey, item.WordKey, item.Word, item.WritingSystemId, facts));
                    if (detailBytes > publication.EstimatedDetailBytes || detailBytes > remaining) { available = false; break; }
                    Execute(connection, transaction, "INSERT INTO ReviewReportCases VALUES ($id,$case,$wordkey,$word,$ws,$wordSort,$wsSort,$facts);",
                        ("$id", reportId.Value), ("$case", item.CaseKey), ("$wordkey", item.WordKey),
                        ("$word", item.Word), ("$ws", item.WritingSystemId),
                        ("$wordSort", Encoding.BigEndianUnicode.GetBytes(item.Word.Normalize(NormalizationForm.FormD))),
                        ("$wsSort", Encoding.BigEndianUnicode.GetBytes(item.WritingSystemId ?? "")), ("$facts", facts));
                    var ordinal = 0;
                    foreach (var membership in item.Memberships)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var json = JsonSerializer.Serialize(membership, ReviewJson.Options);
                        detailBytes = checked(detailBytes + Cost(json));
                        if (detailBytes > publication.EstimatedDetailBytes || detailBytes > remaining) { available = false; break; }
                        Execute(connection, transaction, "INSERT INTO ReviewReportMemberships VALUES ($id,$case,$ordinal,$set,$role,$cell,$variant,$facts);",
                            ("$id", reportId.Value), ("$case", item.CaseKey), ("$ordinal", ordinal++),
                            ("$set", membership.SetId), ("$role", membership.Role), ("$cell", membership.CellId),
                            ("$variant", membership.VariantId), ("$facts", json));
                    }
                    if (!available) break;
                    ordinal = 0;
                    foreach (var reading in item.ReadingLosses)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var json = JsonSerializer.Serialize(reading, ReviewJson.Options);
                        detailBytes = checked(detailBytes + Cost(json));
                        if (detailBytes > publication.EstimatedDetailBytes || detailBytes > remaining) { available = false; break; }
                        Execute(connection, transaction, "INSERT INTO ReviewReportEvents VALUES ($id,$case,$ordinal,$facts);",
                            ("$id", reportId.Value), ("$case", item.CaseKey), ("$ordinal", ordinal++), ("$facts", json));
                    }
                    if (!available) break;
                    bool AddArtifact(string kind, ReviewRow row)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var digest = ReviewRowArtifacts.Write(_database, reportId, row);
                        var id = row switch { ExpectationRow expectation => expectation.Id, SourceRow source => source.Id, _ => null };
                        var side = row is ReadingRow observed ? observed.Side : (Side?)null;
                        var json = JsonSerializer.Serialize(new { kind, digest, id, side }, ReviewJson.Options);
                        detailBytes = checked(detailBytes + Cost(json));
                        if (detailBytes > publication.EstimatedDetailBytes || detailBytes > remaining) return false;
                        Execute(connection, transaction, "INSERT INTO ReviewReportEvents VALUES ($id,$case,$ordinal,$facts);",
                            ("$id", reportId.Value), ("$case", item.CaseKey), ("$ordinal", ordinal++), ("$facts", json));
                        return true;
                    }
                    foreach (var assertion in item.Assertions)
                    {
                        var row = new ExpectationRow(assertion.Id,
                            assertion.WholeWordNegative ? ExpectationAssertion.WholeWordNegative : ExpectationAssertion.CorrectReading,
                            assertion.Authority, assertion.ActorId, assertion.RevisionId,
                            string.IsNullOrEmpty(assertion.ReadingIdentity) ? null : assertion.ReadingIdentity, assertion.SourceRefs,
                            ExpectationState(assertion, item.Before), ExpectationState(assertion, item.After))
                        { Context = assertion.Context };
                        if (!AddArtifact("expectation", row)) { available = false; break; }
                    }
                    if (!available) break;
                    if (item.Sources is not null)
                        foreach (var source in item.Sources)
                            if (!AddArtifact("source", source)) { available = false; break; }
                    if (!available) break;
                    if (item.Readings is not null)
                        foreach (var reading in item.Readings)
                            if (!AddArtifact("reading", reading)) { available = false; break; }
                    if (!available) break;
                }
            }
            if (available)
            {
                var sectionOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var section in publication.AvailableSections)
                {
                    if (!ReviewReportPages.IsEventSection(section) || !sectionOrdinals.TryAdd(section, 0))
                        throw new ArgumentException("Unknown or duplicate retained review section.", nameof(publication));
                    var key = ReviewReportPages.SectionKey(section);
                    detailBytes = checked(detailBytes + Cost(key));
                    if (detailBytes > publication.EstimatedDetailBytes || detailBytes > remaining) { available = false; break; }
                    Execute(connection, transaction, "INSERT INTO ReviewReportEvents VALUES ($id,$case,-1,$facts);",
                        ("$id", reportId.Value), ("$case", key), ("$facts", "{}"));
                }
                if (available && publication.SectionItems is not null)
                    await foreach (var item in publication.SectionItems.WithCancellation(cancellationToken))
                    {
                        if (!sectionOrdinals.TryGetValue(item.Section, out var ordinal) || !ReviewReportPages.IsSectionRow(item.Section, item.Row))
                            throw new InvalidDataException("A section row does not match its captured section.");
                        var digest = ReviewRowArtifacts.Write(_database, reportId, item.Row);
                        var json = JsonSerializer.Serialize(new { kind = "section-row", digest, item.Set, item.Outcome,
                            Flags = item.Flags ?? [], item.Changed, item.Incomplete, item.Group,
                            Sets = item.Row is WordRow word ? word.SetIds : [],
                            WorkPercent = item.Row is WordRow workWord ? workWord.Work.PercentChange : null }, ReviewJson.Options);
                        detailBytes = checked(detailBytes + Cost(json));
                        if (detailBytes > publication.EstimatedDetailBytes || detailBytes > remaining) { available = false; break; }
                        Execute(connection, transaction, "INSERT INTO ReviewReportEvents VALUES ($id,$case,$ordinal,$facts);",
                            ("$id", reportId.Value), ("$case", ReviewReportPages.SectionKey(item.Section)),
                            ("$ordinal", ordinal), ("$facts", json));
                        sectionOrdinals[item.Section] = checked(ordinal + 1);
                    }
            }
            if (available && publication.AvailableSections.Contains("parser-work"))
            {
                var key = ReviewReportPages.SectionKey("parser-work");
                Execute(connection, transaction, "UPDATE ReviewReportEvents SET Ordinal=-Ordinal-2 WHERE ReportId=$id AND CaseKey=$case AND Ordinal>=0;",
                    ("$id", reportId.Value), ("$case", key));
                Execute(connection, transaction, """
                    WITH ranked AS MATERIALIZED (
                      SELECT Ordinal,ROW_NUMBER() OVER (ORDER BY
                        CASE json_extract(FactsJson,'$.outcome')
                          WHEN 'work-increase' THEN 0 WHEN 'work-decrease' THEN 1
                          WHEN 'work-unavailable' THEN 2 WHEN 'work-unchanged' THEN 3 ELSE 4 END,
                        CASE WHEN json_extract(FactsJson,'$.outcome')='work-decrease'
                          THEN json_extract(FactsJson,'$.workPercent') ELSE -json_extract(FactsJson,'$.workPercent') END,
                        -Ordinal) - 1 AS NewOrdinal
                      FROM ReviewReportEvents WHERE ReportId=$id AND CaseKey=$case AND Ordinal < -1
                    )
                    UPDATE ReviewReportEvents SET Ordinal=(SELECT NewOrdinal FROM ranked WHERE ranked.Ordinal=ReviewReportEvents.Ordinal)
                    WHERE ReportId=$id AND CaseKey=$case AND Ordinal < -1;
                    """, ("$id", reportId.Value), ("$case", key));
            }
            if (!available)
            {
                ReviewReportRetention.DeleteRows(connection, transaction, reportId.Value);
                summary = summary with { DetailAvailability = DetailAvailability.Unavailable, Page = null, Word = null, Trace = null,
                    Limitations = summary.Limitations.Concat([new Limitation(released ? "detail-released" : "store-budget", [],
                        released ? "Report detail was released after the Proposal decision." : "Complete report detail exceeds the store budget.", [])]).ToArray() };
                summaryJson = JsonSerializer.Serialize(summary, ReviewJson.Options);
                Execute(connection, transaction, "UPDATE ReviewReports SET SummaryJson=$summary,SummaryDigest=$hash,DetailAvailable=0,DetailBytes=0 WHERE ReportId=$id;",
                    ("$summary", summaryJson), ("$hash", Hash(summaryJson)), ("$id", reportId.Value));
            }
            else Execute(connection, transaction, "UPDATE ReviewReports SET DetailBytes=$bytes WHERE ReportId=$id;",
                ("$bytes", detailBytes), ("$id", reportId.Value));
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            committed = true;
            if (!available) ReviewRowArtifacts.Delete(_database, reportId.Value);
            return reportId;
        }
        finally
        {
            if (!committed) ReviewRowArtifacts.Delete(_database, reportId.Value);
        }
    }

    /// <summary>Reads a frozen summary, rejecting damaged stored summary bytes.</summary>
    public StoredReviewReport? ReadManifest(ReviewReportId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SummaryJson,SummaryDigest,DetailAvailable,InputReferencesJson FROM ReviewReports WHERE ReportId=$id;";
        command.Parameters.AddWithValue("$id", id.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var json = reader.GetString(0);
        if (Hash(json) != reader.GetString(1)) throw new InvalidDataException("Review summary digest does not match.");
        return new StoredReviewReport(JsonSerializer.Deserialize<ReportView>(json, ReviewJson.Options)!, reader.GetInt64(2) != 0,
            JsonSerializer.Deserialize<string[]>(reader.GetString(3))!);
    }

    private sealed record SideTotals(long ApprovedMatched, long ApprovedMissing, long ApprovedUnknown,
        long ReferenceMatched, long ReferenceMissing, long ReferenceUnknown);

    private static SideTotals? CountSide(ReviewCaseDelta item, ReviewCaseSideFacts side, HashSet<string> referenceIds)
    {
        if (!item.AssertionsCaptured && item.Assertions.Count == 0) return null;
        static (long Matched, long Missing, long Unknown) Count(IEnumerable<ReviewAssertion> assertions, ReviewCaseSideFacts side)
        {
            long matched = 0, missing = 0, unknown = 0;
            foreach (var group in assertions.GroupBy(assertion => string.IsNullOrWhiteSpace(assertion.ReadingIdentity)
                         ? "assertion:" + assertion.Id : assertion.ReadingIdentity, StringComparer.Ordinal))
            {
                if (group.Any(assertion => assertion.Eligible && assertion.Morphology is not null && !string.IsNullOrWhiteSpace(assertion.ReadingIdentity) &&
                        side.MatchedExpectationIds.Contains(assertion.Id))) matched++;
                else if (group.All(assertion => assertion.Eligible && assertion.Morphology is not null && !string.IsNullOrWhiteSpace(assertion.ReadingIdentity) &&
                             side.MissingExpectationIds.Contains(assertion.Id))) missing++;
                else unknown++;
            }
            return (matched, missing, unknown);
        }
        var eligible = item.Assertions.Where(assertion => !assertion.WholeWordNegative);
        var approved = Count(eligible.Where(assertion => assertion.Approved), side);
        var reference = Count(eligible.Where(assertion => !assertion.Approved && referenceIds.Contains(assertion.Id)), side);
        return new(approved.Matched, approved.Missing, approved.Unknown, reference.Matched, reference.Missing, reference.Unknown);
    }

    private static ExpectationOutcome ExpectationState(ReviewAssertion assertion, ReviewCaseSideFacts side)
    {
        if (!assertion.Eligible) return ExpectationOutcome.Unknown;
        if (assertion.WholeWordNegative)
            return side.Parsed == true ? ExpectationOutcome.Missing : side.Completion == Completion.Complete && side.Parsed == false
                ? ExpectationOutcome.Matched : ExpectationOutcome.Unknown;
        if (side.MatchedExpectationIds.Contains(assertion.Id)) return ExpectationOutcome.Matched;
        if (side.MissingExpectationIds.Contains(assertion.Id)) return ExpectationOutcome.Missing;
        return ExpectationOutcome.Unknown;
    }

    private static long Cost(params string?[] values) => 512 + values.Sum(value => (long)Encoding.UTF8.GetByteCount(value ?? "") * 4);
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string, object?)[] args)
    {
        using var command = Command(connection, transaction, sql, args);
        command.ExecuteNonQuery();
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string, object?)[] args)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
