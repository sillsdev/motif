using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Review;
using ReviewWordRow = SIL.Motif.Contract.Review.WordRow;

namespace SIL.Motif.Worker.Store;

/// <summary>Requests one bounded child list belonging to an exact report case.</summary>
public sealed record ReviewChildPageRequest(string ProjectKey, ReviewReportId ReportId, string Kind,
    string CaseKey, string? Cursor = null, int Limit = 50)
{
    /// <summary>The named set that owns a requested cell's variants.</summary>
    public string? SetId { get; init; }
    /// <summary>Restricts captured readings to the selected side.</summary>
    public Side? Side { get; init; }
}

public sealed partial class ReviewReportRepository
{
    private const int PageBytes = 192 * 1024;
    private static readonly HashSet<string> WordSections = new(StringComparer.Ordinal)
        { "wordforms", "controls", "negative-controls", "incomplete" };

    /// <summary>Reads a deterministic, report-bound page without recomputing the Trial.</summary>
    public ReviewPage ReadPage(ReviewPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Validate() is { } problem) throw new ArgumentException(problem, nameof(request));
        if (!WordSections.Contains(request.Section) && request.Section is not ("sampled-tokens" or "paradigm-cells") &&
            !ReviewReportPages.IsEventSection(request.Section))
            throw new ArgumentException("Unknown review section.", nameof(request));
        var digest = ReviewReportCursor.FilterDigest(request);
        var last = request.Cursor is null ? null : ReviewReportCursor.Decode(request.Cursor, request.ProjectKey,
            request.ReportId.Value, request.Section, digest).Last;
        using var lease = ReviewRowArtifacts.Acquire(_database, request.ReportId.Value);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RequireDetail(connection, transaction, request.ProjectKey, request.ReportId);
        var result = request.Section switch
        {
            "sampled-tokens" => ReadTokens(connection, transaction, request, digest, last),
            "controls" or "negative-controls" => ReadControlMemberships(connection, transaction, request, digest, last),
            "paradigm-cells" => ReadCells(connection, transaction, request, digest, last),
            _ when ReviewReportPages.IsEventSection(request.Section) => ReadSectionRows(connection, transaction, request, digest, last),
            _ => ReadWords(connection, transaction, request, digest, last),
        };
        transaction.Commit();
        return result;
    }

    /// <summary>Reads a bounded shortlist ordered by captured sample weight and membership count.</summary>
    public IReadOnlyList<ReviewWordRow> ReadFailureSamples(ReviewPageRequest request, int limit)
    {
        if (limit is < 0 or > 30 || !WordSections.Contains(request.Section))
            throw new ArgumentException("Invalid failure sample request.", nameof(request));
        if (limit == 0) return [];
        var keys = new List<string>();
        using (var lease = ReviewRowArtifacts.Acquire(_database, request.ReportId.Value))
        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            RequireDetail(connection, transaction, request.ProjectKey, request.ReportId);
            var args = FilterArguments(request).ToList();
            args.Add(("$report", request.ReportId.Value));
            args.Add(("$limit", limit));
            var sql = "SELECT c.CaseKey FROM ReviewReportCases c WHERE c.ReportId=$report" + WordFilter(request) +
                " ORDER BY (SELECT SUM(json_extract(m.FactsJson,'$.weight')) FROM ReviewReportMemberships m " +
                "WHERE m.ReportId=c.ReportId AND m.CaseKey=c.CaseKey AND m.Role='sampled-tokens') DESC, " +
                "(SELECT COUNT(*) FROM ReviewReportMemberships m WHERE m.ReportId=c.ReportId AND m.CaseKey=c.CaseKey) DESC, " +
                "c.WordSort,c.WritingSystemSort,c.CaseKey LIMIT $limit;";
            using var command = PageCommand(connection, transaction, sql, args.ToArray());
            using var reader = command.ExecuteReader();
            while (reader.Read()) keys.Add(reader.GetString(0));
        }
        return keys.Select(key => ReadWord(request.ProjectKey, request.ReportId, key, 1).Row).ToArray();
    }

    /// <summary>Counts retained new warning or finding rows by their producer-backed group.</summary>
    public IReadOnlyList<(string Group, long Count)> ReadFailureEventGroups(string project, ReviewReportId report, string section)
    {
        if (section is not ("warnings" or "parsimony")) throw new ArgumentException("Invalid event section.", nameof(section));
        using var lease = ReviewRowArtifacts.Acquire(_database, report.Value);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RequireDetail(connection, transaction, project, report);
        var key = ReviewReportPages.SectionKey(section);
        using var available = PageCommand(connection, transaction,
            "SELECT 1 FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND Ordinal=-1;",
            ("$report", report.Value), ("$case", key));
        if (available.ExecuteScalar() is null) throw new ReviewDetailUnavailableException();
        using var command = PageCommand(connection, transaction,
            "SELECT json_extract(FactsJson,'$.group'),COUNT(*) FROM ReviewReportEvents " +
            "WHERE ReportId=$report AND CaseKey=$case AND Ordinal>=0 " +
            "AND EXISTS (SELECT 1 FROM json_each(FactsJson,'$.flags') WHERE value='new-warnings-or-parsimony') " +
            "GROUP BY json_extract(FactsJson,'$.group') ORDER BY 1;", ("$report", report.Value), ("$case", key));
        using var reader = command.ExecuteReader();
        var result = new List<(string, long)>();
        while (reader.Read()) if (!reader.IsDBNull(0)) result.Add((reader.GetString(0), reader.GetInt64(1)));
        return result;
    }

    /// <summary>Finds exact case identities for a word or case selector in one retained report.</summary>
    public IReadOnlyList<string> FindCases(string project, ReviewReportId report, string? word, string? caseKey)
    {
        if ((word is null) == (caseKey is null)) throw new ArgumentException("Select exactly one word or case.");
        using var lease = ReviewRowArtifacts.Acquire(_database, report.Value);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RequireDetail(connection, transaction, project, report);
        using var command = PageCommand(connection, transaction,
            "SELECT CaseKey FROM ReviewReportCases WHERE ReportId=$report AND " +
            (caseKey is null ? "WordSort=$value" : "CaseKey=$value") + " ORDER BY CaseKey;",
            ("$report", report.Value), ("$value", caseKey is null
                ? Encoding.BigEndianUnicode.GetBytes(word!.Normalize(NormalizationForm.FormD)) : caseKey));
        using var reader = command.ExecuteReader();
        var cases = new List<string>();
        while (reader.Read()) cases.Add(reader.GetString(0));
        return cases;
    }

    /// <summary>Reads one exact case and its first bounded child pages.</summary>
    public WordView ReadWord(string project, ReviewReportId report, string caseKey, int limit = 50)
    {
        if (limit is < 1 or > 200 || string.IsNullOrWhiteSpace(caseKey)) throw new ArgumentException("Invalid case request.");
        ReviewWordRow row;
        using (var lease = ReviewRowArtifacts.Acquire(_database, report.Value))
        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            RequireDetail(connection, transaction, project, report);
            var request = new ReviewPageRequest(project, report, "wordforms", Limit: 1);
            var page = ReadWords(connection, transaction, request, ReviewReportCursor.FilterDigest(request), null, caseKey);
            row = page.Items.OfType<ReviewWordRow>().SingleOrDefault() ?? throw new KeyNotFoundException("The report case was not found.");
        }
        ReviewPage Child(string kind) => ReadChildren(new ReviewChildPageRequest(project, report, kind, caseKey, Limit: limit));
        return new WordView(row, Child("memberships"), Child("expectations"), Child("readings"), Child("sources"), null);
    }

    /// <summary>Reads one case's retained indexed child references in a separate bounded page.</summary>
    public ReviewPage ReadChildren(ReviewChildPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 200 || string.IsNullOrWhiteSpace(request.ProjectKey) ||
            string.IsNullOrWhiteSpace(request.ReportId.Value) || string.IsNullOrWhiteSpace(request.CaseKey) ||
            request.Kind is not ("readings" or "expectations" or "sources" or "memberships" or "variants") ||
            request.Kind == "variants" && string.IsNullOrWhiteSpace(request.SetId) ||
            request.Side is not null && (request.Kind != "readings" || !Enum.IsDefined(request.Side.Value)))
            throw new ArgumentException("Invalid child page request.", nameof(request));
        var digest = request.Side is null ? "children-v1" : "children-v1/" + request.Side.Value.ToString().ToLowerInvariant();
        var parent = request.Kind == "variants" ? JsonSerializer.Serialize(new[] { request.SetId, request.CaseKey }) : request.CaseKey;
        var lastText = request.Cursor is null ? null : ReviewReportCursor.Decode(request.Cursor, request.ProjectKey,
            request.ReportId.Value, request.Kind, digest, request.Kind, parent).Last;
        if (request.Kind != "variants" && lastText is not null &&
            (!int.TryParse(lastText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0))
            throw new ReviewCursorInvalidException();
        var last = request.Kind == "variants" || lastText is null ? -1 : int.Parse(lastText, CultureInfo.InvariantCulture);
        using var lease = ReviewRowArtifacts.Acquire(_database, request.ReportId.Value);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RequireDetail(connection, transaction, request.ProjectKey, request.ReportId);
        if (request.Kind is "memberships" or "variants")
        {
            var page = request.Kind == "variants"
                ? ReadVariantChildren(connection, transaction, request, digest, lastText, parent)
                : ReadMembershipChildren(connection, transaction, request, digest, last);
            transaction.Commit();
            return page;
        }
        var (availabilityField, eventKind) = request.Kind switch
        {
            "readings" => ("readingsAvailable", "reading"), "expectations" => ("expectationsAvailable", "expectation"),
            "sources" => ("sourcesAvailable", "source"), _ => throw new ArgumentException("Invalid child kind.")
        };
        using var availability = PageCommand(connection, transaction,
            $"SELECT json_extract(FactsJson,'$.{availabilityField}') FROM ReviewReportCases WHERE ReportId=$report AND CaseKey=$case;",
            ("$report", request.ReportId.Value), ("$case", request.CaseKey));
        var hasReadings = availability.ExecuteScalar();
        if (hasReadings is null) throw new KeyNotFoundException("The review case was not found.");
        if (hasReadings is not long captured || captured == 0)
            return new ReviewPage(request.Kind, digest, request.Kind, null, null, 0, request.Limit, null, ListAvailability.Unavailable, []);
        using var count = PageCommand(connection, transaction, "SELECT COUNT(*) FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND json_extract(FactsJson,'$.kind')=$kind AND ($side IS NULL OR json_extract(FactsJson,'$.side')=$side);",
            ("$report", request.ReportId.Value), ("$case", request.CaseKey), ("$kind", eventKind),
            ("$side", request.Side?.ToString().ToLowerInvariant()));
        var total = (long)count.ExecuteScalar()!;
        using var command = PageCommand(connection, transaction,
            "SELECT Ordinal,FactsJson FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND json_extract(FactsJson,'$.kind')=$kind AND ($side IS NULL OR json_extract(FactsJson,'$.side')=$side) AND Ordinal>$last ORDER BY Ordinal LIMIT $limit;",
            ("$report", request.ReportId.Value), ("$case", request.CaseKey), ("$kind", eventKind), ("$side", request.Side?.ToString().ToLowerInvariant()),
            ("$last", last), ("$limit", request.Limit + 1));
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        var current = last;
        var more = false;
        var bytes = 16 * 1024;
        var partial = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            using var reference = JsonDocument.Parse(reader.GetString(1));
            var artifact = reference.RootElement.GetProperty("digest").GetString() ?? throw new InvalidDataException("Invalid child reference.");
            var row = ReviewRowArtifacts.Read(_database, request.ReportId, artifact);
            if (request.Kind == "readings" && row is not ReadingRow || request.Kind == "expectations" && row is not ExpectationRow ||
                request.Kind == "sources" && row is not SourceRow) throw new InvalidDataException("A retained child has the wrong type.");
            var size = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            current = reader.GetInt32(0);
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Kind, digest, 1, current.ToString(CultureInfo.InvariantCulture), request.Kind, request.CaseKey)) : null;
        reader.Close();
        transaction.Commit();
        return new ReviewPage(request.Kind, digest, request.Kind, total, total, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static ReviewPage ReadVariantChildren(SqliteConnection connection, SqliteTransaction transaction,
        ReviewChildPageRequest request, string digest, string? last, string parent)
    {
        var args = new List<(string, object?)> { ("$report", request.ReportId.Value), ("$set", request.SetId), ("$cell", request.CaseKey) };
        const string source = " FROM ReviewReportMemberships m JOIN ReviewReportCases c ON c.ReportId=m.ReportId AND c.CaseKey=m.CaseKey " +
            "WHERE m.ReportId=$report AND m.Role='paradigm-cells' AND m.SetId=$set AND m.CellId=$cell";
        var total = Count(connection, transaction, "SELECT COUNT(*)" + source, args);
        var tuple = last is null ? null : ReadTuple(last, 2);
        if (tuple is not null && (!int.TryParse(tuple[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 0))
            throw new ReviewCursorInvalidException();
        args.Add(("$case", tuple?[0]));
        args.Add(("$ordinal", tuple is null ? -1 : int.Parse(tuple[1], CultureInfo.InvariantCulture)));
        args.Add(("$limit", request.Limit + 1));
        using var command = PageCommand(connection, transaction,
            "SELECT m.CaseKey,m.Ordinal,m.FactsJson,c.WordKey,c.Word,c.WritingSystemId,c.FactsJson" + source +
            " AND ($case IS NULL OR (m.CaseKey,m.Ordinal)>($case,$ordinal)) ORDER BY m.CaseKey,m.Ordinal LIMIT $limit;", args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        string? anchor = null;
        var bytes = 16 * 1024;
        var partial = false;
        var more = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            var key = reader.GetString(0);
            var member = JsonSerializer.Deserialize<ReviewMembership>(reader.GetString(2), ReviewJson.Options) ?? throw new InvalidDataException("Invalid variant index.");
            var facts = JsonSerializer.Deserialize<CaseIndexFacts>(reader.GetString(6), ReviewJson.Options) ?? throw new InvalidDataException("Invalid case index.");
            var link = facts.ReadingsAvailable ? ChildLink(request.ProjectKey, request.ReportId, key, "readings") : null;
            var memberLink = ChildLink(request.ProjectKey, request.ReportId, key, "memberships/" + reader.GetInt32(1).ToString(CultureInfo.InvariantCulture));
            var (beforeReadings, afterReadings) = ReadingTotals(connection, transaction, request.ReportId.Value, key, facts.ReadingsAvailable);
            ReviewRow row = new ReviewWordRow(key, reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                SideFacts(facts.Before, link, beforeReadings, facts.BeforeCounts), SideFacts(facts.After, link, afterReadings, facts.AfterCounts), facts.Flags,
                CaseCauses(connection, transaction, request.ReportId.Value, key, facts), [member.SetId], 1,
                member.ExpectationIds.Distinct(StringComparer.Ordinal).LongCount(),
                new WorkChange(null, 0, "", WorkComparison.NotComparable, ["not-measured"]), Attribution.NotAttributed,
                link is null ? [memberLink] : [memberLink, link]);
            var size = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            anchor = JsonSerializer.Serialize(new[] { key, reader.GetInt32(1).ToString(CultureInfo.InvariantCulture) });
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Kind, digest, 1, anchor!, request.Kind, parent)) : null;
        return new ReviewPage(request.Kind, digest, "variants", total, total, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static ReviewPage ReadMembershipChildren(SqliteConnection connection, SqliteTransaction transaction,
        ReviewChildPageRequest request, string digest, int last)
    {
        using var exists = PageCommand(connection, transaction,
            "SELECT 1 FROM ReviewReportCases WHERE ReportId=$report AND CaseKey=$case;",
            ("$report", request.ReportId.Value), ("$case", request.CaseKey));
        if (exists.ExecuteScalar() is null) throw new KeyNotFoundException("The review case was not found.");
        var args = new List<(string, object?)> { ("$report", request.ReportId.Value), ("$case", request.CaseKey) };
        const string source = " FROM ReviewReportMemberships m JOIN ReviewReportCases c ON c.ReportId=m.ReportId AND c.CaseKey=m.CaseKey " +
            "WHERE m.ReportId=$report AND m.CaseKey=$case";
        var total = Count(connection, transaction, "SELECT COUNT(*)" + source, args);
        args.Add(("$last", last));
        args.Add(("$limit", request.Limit + 1));
        using var command = PageCommand(connection, transaction,
            "SELECT m.Ordinal,m.FactsJson,c.WordKey,c.Word,c.WritingSystemId,c.FactsJson" + source +
            " AND m.Ordinal>$last ORDER BY m.Ordinal LIMIT $limit;", args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        var current = last;
        var bytes = 16 * 1024;
        var partial = false;
        var more = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            var member = JsonSerializer.Deserialize<ReviewMembership>(reader.GetString(1), ReviewJson.Options) ?? throw new InvalidDataException("Invalid membership index.");
            var facts = JsonSerializer.Deserialize<CaseIndexFacts>(reader.GetString(5), ReviewJson.Options) ?? throw new InvalidDataException("Invalid case index.");
            var readingLink = facts.ReadingsAvailable ? ChildLink(request.ProjectKey, request.ReportId, request.CaseKey, "readings") : null;
            var (beforeReadings, afterReadings) = ReadingTotals(connection, transaction, request.ReportId.Value, request.CaseKey, facts.ReadingsAvailable);
            var before = SideFacts(facts.Before, readingLink, beforeReadings, facts.BeforeCounts);
            var after = SideFacts(facts.After, readingLink, afterReadings, facts.AfterCounts);
            ReviewRow row;
            if (member.Role == "sampled-tokens")
                row = new TokenRow(member.CaseId, reader.GetString(2), member.OccurrenceId ?? member.CaseId, member.Weight,
                    member.SourceLocator ?? "", before, after, readingLink is null ? [] : [readingLink]);
            else if (member.Role == "paradigm-cells" && member.CellId is not null)
                row = new CellRow(member.SetId, member.CellId, member.LexemeId ?? "", member.Features, 1,
                    VariantState(member.ExpectationIds, facts.Before), VariantState(member.ExpectationIds, facts.After), []);
            else
                row = new ReviewWordRow(request.CaseKey, reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), before, after, facts.Flags,
                    CaseCauses(connection, transaction, request.ReportId.Value, request.CaseKey, facts), [member.SetId], 1,
                    member.ExpectationIds.Distinct(StringComparer.Ordinal).LongCount(),
                    new WorkChange(null, 0, "", WorkComparison.NotComparable, ["not-measured"]), Attribution.NotAttributed,
                    readingLink is null ? [] : [readingLink]);
            var size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            current = reader.GetInt32(0);
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Kind, digest, 1, current.ToString(CultureInfo.InvariantCulture), request.Kind, request.CaseKey)) : null;
        return new ReviewPage(request.Kind, digest, "memberships", total, total, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static CellOutcome VariantState(IReadOnlyList<string> ids, ReviewCaseSideFacts side)
    {
        if (ids.Any(side.MatchedExpectationIds.Contains)) return CellOutcome.Covered;
        return ids.Count > 0 && side.Completion == Completion.Complete && ids.All(side.MissingExpectationIds.Contains)
            ? CellOutcome.Uncovered : CellOutcome.Unknown;
    }

    private ReviewPage ReadSectionRows(SqliteConnection connection, SqliteTransaction transaction,
        ReviewPageRequest request, string digest, string? last)
    {
        var key = ReviewReportPages.SectionKey(request.Section);
        using (var available = PageCommand(connection, transaction,
            "SELECT 1 FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND Ordinal=-1;",
            ("$report", request.ReportId.Value), ("$case", key)))
            if (available.ExecuteScalar() is null) throw new ReviewDetailUnavailableException();
        if (last is not null && (!int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0))
            throw new ReviewCursorInvalidException();
        var args = new List<(string, object?)> { ("$report", request.ReportId.Value), ("$case", key) };
        const string source = " FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND Ordinal>=0";
        var total = Count(connection, transaction, "SELECT COUNT(*)" + source, args);
        var filter = "";
        if (request.Set is not null) { filter += " AND json_extract(FactsJson,'$.set') IN (SELECT value FROM json_each($set))"; args.Add(("$set", SelectorValues(request.Set))); }
        if (request.Outcome is not null)
        {
            var prefix = request.Section == "warnings" ? "warning/" : request.Section == "parsimony" ? "parsimony/" : null;
            if (prefix is not null && request.Outcome.StartsWith(prefix, StringComparison.Ordinal))
            {
                filter += " AND EXISTS (SELECT 1 FROM json_each(FactsJson,'$.flags') WHERE value='new-warnings-or-parsimony') AND json_extract(FactsJson,'$.group')=$group";
                args.Add(("$group", request.Outcome[prefix.Length..]));
            }
            else { filter += " AND json_extract(FactsJson,'$.outcome') IN (SELECT value FROM json_each($outcome))"; args.Add(("$outcome", SelectorValues(request.Outcome))); }
        }
        if (request.Flag is { } flag)
        {
            filter += " AND EXISTS (SELECT 1 FROM json_each(FactsJson,'$.flags') f WHERE f.value=$flag)";
            args.Add(("$flag", JsonSerializer.SerializeToElement(flag, ReviewJson.Options).GetString()));
        }
        if (request.Filter is "changed" or "review") filter += " AND json_extract(FactsJson,'$.changed')=1";
        if (request.Filter == "unchanged") filter += " AND json_extract(FactsJson,'$.changed')=0";
        if (request.Filter == "incomplete") filter += " AND json_extract(FactsJson,'$.incomplete')=1";
        var matching = Count(connection, transaction, "SELECT COUNT(*)" + source + filter, args);
        args.Add(("$last", last is null ? -1 : int.Parse(last, CultureInfo.InvariantCulture)));
        args.Add(("$limit", request.Limit + 1));
        using var command = PageCommand(connection, transaction, "SELECT Ordinal,FactsJson" + source + filter +
            " AND Ordinal>$last ORDER BY Ordinal LIMIT $limit;", args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        var anchor = -1;
        var bytes = 16 * 1024;
        var partial = false;
        var more = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            using var reference = JsonDocument.Parse(reader.GetString(1));
            var artifact = reference.RootElement.GetProperty("digest").GetString() ?? throw new InvalidDataException("Invalid section reference.");
            var row = ReviewRowArtifacts.Read(_database, request.ReportId, artifact);
            if (!ReviewReportPages.IsSectionRow(request.Section, row)) throw new InvalidDataException("A retained section row has the wrong type.");
            var size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            anchor = reader.GetInt32(0);
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Section, digest, 1, anchor.ToString(CultureInfo.InvariantCulture), null, null)) : null;
        return new ReviewPage(request.Section, digest, ReviewReportPages.Unit(request.Section), total, matching,
            items.Count, request.Limit, next, partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static ReviewPage ReadWords(SqliteConnection connection, SqliteTransaction transaction, ReviewPageRequest request,
        string digest, string? last, string? exactCase = null)
    {
        var exact = exactCase is null ? "" : " AND c.CaseKey=$exact";
        var filter = WordFilter(request) + exact;
        var args = FilterArguments(request).ToList();
        if (exactCase is not null) args.Add(("$exact", exactCase));
        args.Add(("$report", request.ReportId.Value));
        var sectionFilter = WordFilter(request with { Set = null, Flag = null, Filter = null, Outcome = null }) + exact;
        var total = Count(connection, transaction, "SELECT COUNT(*) FROM ReviewReportCases c WHERE c.ReportId=$report" + sectionFilter, args);
        var matching = Count(connection, transaction, "SELECT COUNT(*) FROM ReviewReportCases c WHERE c.ReportId=$report" + filter, args);
        var tuple = last is null ? null : ReadTuple(last, 3);
        args.Add(("$word", tuple is null ? null : ReadSortKey(tuple[0])));
        args.Add(("$ws", tuple is null ? null : ReadSortKey(tuple[1])));
        args.Add(("$case", tuple?[2]));
        args.Add(("$limit", request.Limit + 1));
        var sql = "SELECT c.CaseKey,c.WordKey,c.Word,c.WritingSystemId,c.FactsJson,c.WordSort,c.WritingSystemSort FROM ReviewReportCases c " +
            "WHERE c.ReportId=$report" + filter + " AND ($case IS NULL OR (c.WordSort,c.WritingSystemSort,c.CaseKey) > ($word,$ws,$case)) " +
            "ORDER BY c.WordSort,c.WritingSystemSort,c.CaseKey LIMIT $limit;";
        using var command = PageCommand(connection, transaction, sql, args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        string? anchor = null;
        var bytes = 16 * 1024;
        var partial = false;
        var more = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            var key = reader.GetString(0);
            var word = reader.GetString(2);
            var ws = reader.IsDBNull(3) ? null : reader.GetString(3);
            var facts = JsonSerializer.Deserialize<CaseIndexFacts>(reader.GetString(4), ReviewJson.Options) ?? throw new InvalidDataException("Invalid case index.");
            var (memberships, expectations, sets) = MembershipSummary(connection, transaction, request.ReportId.Value, key, facts.ExpectationsAvailable);
            var link = facts.ReadingsAvailable ? ChildLink(request.ProjectKey, request.ReportId, key, "readings") : null;
            var (beforeReadings, afterReadings) = ReadingTotals(connection, transaction, request.ReportId.Value, key, facts.ReadingsAvailable);
            var causes = CaseCauses(connection, transaction, request.ReportId.Value, key, facts);
            var links = new List<ReadTarget>();
            if (link is not null) links.Add(link);
            if (memberships > 0) links.Add(ChildLink(request.ProjectKey, request.ReportId, key, "memberships"));
            if (facts.ExpectationsAvailable) links.Add(ChildLink(request.ProjectKey, request.ReportId, key, "expectations"));
            if (facts.SourcesAvailable) links.Add(ChildLink(request.ProjectKey, request.ReportId, key, "sources"));
            ReviewRow row = new ReviewWordRow(key, reader.GetString(1), word, ws,
                SideFacts(facts.Before, link, beforeReadings, facts.BeforeCounts), SideFacts(facts.After, link, afterReadings, facts.AfterCounts),
                facts.Flags, causes, sets, memberships, expectations,
                new WorkChange(null, 0, "", WorkComparison.NotComparable, ["not-measured"]), Attribution.NotAttributed, links);
            var size = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            anchor = JsonSerializer.Serialize(new[] { Convert.ToBase64String(reader.GetFieldValue<byte[]>(5)),
                Convert.ToBase64String(reader.GetFieldValue<byte[]>(6)), key });
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Section, digest, 1, anchor!, null, null)) : null;
        return new ReviewPage(request.Section, digest, "words", total, matching, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static ReviewPage ReadTokens(SqliteConnection connection, SqliteTransaction transaction, ReviewPageRequest request,
        string digest, string? last)
    {
        var args = FilterArguments(request).ToList();
        args.Add(("$report", request.ReportId.Value));
        const string source = " FROM ReviewReportMemberships m JOIN ReviewReportCases c ON c.ReportId=m.ReportId AND c.CaseKey=m.CaseKey " +
            "WHERE m.ReportId=$report AND m.Role='sampled-tokens'";
        var filter = WordFilter(request);
        if (request.Set is not null) filter += " AND m.SetId IN (SELECT value FROM json_each($set))";
        var total = Count(connection, transaction, "SELECT COUNT(*)" + source, args);
        var matching = Count(connection, transaction, "SELECT COUNT(*)" + source + filter, args);
        var tuple = last is null ? null : ReadTuple(last, 2);
        args.Add(("$case", tuple?[0]));
        if (tuple is not null && (!int.TryParse(tuple[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 0))
            throw new ReviewCursorInvalidException();
        args.Add(("$ordinal", tuple is null ? -1 : int.Parse(tuple[1], CultureInfo.InvariantCulture)));
        args.Add(("$limit", request.Limit + 1));
        using var command = PageCommand(connection, transaction, "SELECT m.CaseKey,m.Ordinal,m.FactsJson,c.WordKey,c.FactsJson" + source + filter +
            " AND ($case IS NULL OR (m.CaseKey,m.Ordinal)>($case,$ordinal)) ORDER BY m.CaseKey,m.Ordinal LIMIT $limit;", args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        string? anchor = null;
        var more = false;
        var bytes = 16 * 1024;
        var partial = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            var member = JsonSerializer.Deserialize<ReviewMembership>(reader.GetString(2), ReviewJson.Options) ?? throw new InvalidDataException("Invalid membership index.");
            var facts = JsonSerializer.Deserialize<CaseIndexFacts>(reader.GetString(4), ReviewJson.Options) ?? throw new InvalidDataException("Invalid case index.");
            var key = reader.GetString(0);
            var link = facts.ReadingsAvailable ? ChildLink(request.ProjectKey, request.ReportId, key, "readings") : null;
            var (beforeReadings, afterReadings) = ReadingTotals(connection, transaction, request.ReportId.Value, key, facts.ReadingsAvailable);
            var locator = member.SourceLocator ?? "";
            ReviewRow row = new TokenRow(member.CaseId, reader.GetString(3), member.OccurrenceId ?? member.CaseId,
                member.Weight, locator, SideFacts(facts.Before, link, beforeReadings, facts.BeforeCounts), SideFacts(facts.After, link, afterReadings, facts.AfterCounts),
                link is null ? [] : [link]);
            var size = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            anchor = JsonSerializer.Serialize(new[] { reader.GetString(0), reader.GetInt32(1).ToString(CultureInfo.InvariantCulture) });
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Section, digest, 1, anchor!, null, null)) : null;
        return new ReviewPage(request.Section, digest, "positions", total, matching, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static ReviewPage ReadControlMemberships(SqliteConnection connection, SqliteTransaction transaction,
        ReviewPageRequest request, string digest, string? last)
    {
        var args = FilterArguments(request).ToList();
        args.Add(("$report", request.ReportId.Value));
        var roles = request.Section == "controls" ? "m.Role IN ('reference','regression')" : "m.Role='negative'";
        var source = " FROM ReviewReportMemberships m JOIN ReviewReportCases c ON c.ReportId=m.ReportId AND c.CaseKey=m.CaseKey " +
            "WHERE m.ReportId=$report AND " + roles;
        var filter = WordFilter(request);
        if (request.Set is not null) filter += " AND m.SetId IN (SELECT value FROM json_each($set))";
        var total = Count(connection, transaction, "SELECT COUNT(*)" + source, args);
        var matching = Count(connection, transaction, "SELECT COUNT(*)" + source + filter, args);
        var tuple = last is null ? null : ReadTuple(last, 2);
        if (tuple is not null && (!int.TryParse(tuple[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 0))
            throw new ReviewCursorInvalidException();
        args.Add(("$case", tuple?[0]));
        args.Add(("$ordinal", tuple is null ? -1 : int.Parse(tuple[1], CultureInfo.InvariantCulture)));
        args.Add(("$limit", request.Limit + 1));
        using var command = PageCommand(connection, transaction, "SELECT m.CaseKey,m.Ordinal,m.FactsJson,c.WordKey,c.Word,c.WritingSystemId,c.FactsJson" +
            source + filter + " AND ($case IS NULL OR (m.CaseKey,m.Ordinal)>($case,$ordinal)) ORDER BY m.CaseKey,m.Ordinal LIMIT $limit;", args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        string? anchor = null;
        var more = false;
        var bytes = 16 * 1024;
        var partial = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            var key = reader.GetString(0);
            var member = JsonSerializer.Deserialize<ReviewMembership>(reader.GetString(2), ReviewJson.Options) ?? throw new InvalidDataException("Invalid membership index.");
            var facts = JsonSerializer.Deserialize<CaseIndexFacts>(reader.GetString(6), ReviewJson.Options) ?? throw new InvalidDataException("Invalid case index.");
            var readingLink = facts.ReadingsAvailable ? ChildLink(request.ProjectKey, request.ReportId, key, "readings") : null;
            var memberLink = ChildLink(request.ProjectKey, request.ReportId, key, "memberships/" + reader.GetInt32(1).ToString(CultureInfo.InvariantCulture));
            var (beforeReadings, afterReadings) = ReadingTotals(connection, transaction, request.ReportId.Value, key, facts.ReadingsAvailable);
            var causes = CaseCauses(connection, transaction, request.ReportId.Value, key, facts);
            ReviewRow row = new ReviewWordRow(key, reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                SideFacts(facts.Before, readingLink, beforeReadings, facts.BeforeCounts), SideFacts(facts.After, readingLink, afterReadings, facts.AfterCounts),
                facts.Flags, causes, [member.SetId], 1, member.ExpectationIds.Distinct(StringComparer.Ordinal).LongCount(),
                new WorkChange(null, 0, "", WorkComparison.NotComparable, ["not-measured"]), Attribution.NotAttributed,
                readingLink is null ? [memberLink] : [memberLink, readingLink]);
            var size = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            anchor = JsonSerializer.Serialize(new[] { key, reader.GetInt32(1).ToString(CultureInfo.InvariantCulture) });
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Section, digest, 1, anchor!, null, null)) : null;
        return new ReviewPage(request.Section, digest, "memberships", total, matching, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static ReviewPage ReadCells(SqliteConnection connection, SqliteTransaction transaction, ReviewPageRequest request,
        string digest, string? last)
    {
        const string query = """
            WITH variants AS (
              SELECT m.SetId, m.CellId AS CellId, m.FactsJson,
                CASE WHEN $flag IS NULL OR EXISTS (SELECT 1 FROM json_each(c.FactsJson,'$.flags') f WHERE f.value=$flag) THEN 1 ELSE 0 END AS FlagHit,
                CASE WHEN EXISTS (SELECT 1 FROM json_each(m.FactsJson,'$.expectationIds') e
                       JOIN json_each(c.FactsJson,'$.before.matchedExpectationIds') a ON e.value=a.value) THEN 1
                     WHEN json_array_length(m.FactsJson,'$.expectationIds')>0
                       AND json_extract(c.FactsJson,'$.before.completion')='complete'
                       AND NOT EXISTS (SELECT 1 FROM json_each(m.FactsJson,'$.expectationIds') e
                         WHERE e.value NOT IN (SELECT value FROM json_each(c.FactsJson,'$.before.missingExpectationIds'))) THEN 0 END AS BeforeValue,
                CASE WHEN EXISTS (SELECT 1 FROM json_each(m.FactsJson,'$.expectationIds') e
                       JOIN json_each(c.FactsJson,'$.after.matchedExpectationIds') a ON e.value=a.value) THEN 1
                     WHEN json_array_length(m.FactsJson,'$.expectationIds')>0
                       AND json_extract(c.FactsJson,'$.after.completion')='complete'
                       AND NOT EXISTS (SELECT 1 FROM json_each(m.FactsJson,'$.expectationIds') e
                         WHERE e.value NOT IN (SELECT value FROM json_each(c.FactsJson,'$.after.missingExpectationIds'))) THEN 0 END AS AfterValue
              FROM ReviewReportMemberships m JOIN ReviewReportCases c ON c.ReportId=m.ReportId AND c.CaseKey=m.CaseKey
              WHERE m.ReportId=$report AND m.Role='paradigm-cells' AND m.CellId IS NOT NULL
            ), cells AS (
              SELECT SetId,CellId,MIN(FactsJson) AS FactsJson,COUNT(*) AS VariantCount,MAX(FlagHit) AS FlagHit,
                CASE WHEN MAX(BeforeValue)=1 THEN 1 WHEN COUNT(BeforeValue)=COUNT(*) THEN 0 END AS BeforeValue,
                CASE WHEN MAX(AfterValue)=1 THEN 1 WHEN COUNT(AfterValue)=COUNT(*) THEN 0 END AS AfterValue
              FROM variants GROUP BY SetId,CellId
            )
            """;
        var flag = request.Flag is null ? null : JsonSerializer.SerializeToElement(request.Flag.Value, ReviewJson.Options).GetString();
        var args = new List<(string, object?)> { ("$report", request.ReportId.Value), ("$flag", flag) };
        var total = Count(connection, transaction, query + " SELECT COUNT(*) FROM cells", args);
        var conditions = new List<string>();
        if (request.Set is not null)
        {
            conditions.Add("SetId IN (SELECT value FROM json_each($set))");
            args.Add(("$set", SelectorValues(request.Set)));
        }
        if (request.Flag is not null) conditions.Add("FlagHit=1");
        if (request.Filter == "changed") conditions.Add("BeforeValue IS NOT NULL AND AfterValue IS NOT NULL AND BeforeValue!=AfterValue");
        if (request.Filter == "unchanged") conditions.Add("BeforeValue IS NOT NULL AND AfterValue IS NOT NULL AND BeforeValue=AfterValue");
        if (request.Filter == "review") conditions.Add("AfterValue IS NULL OR AfterValue=0 OR (BeforeValue IS NOT NULL AND BeforeValue!=AfterValue)");
        if (request.Filter == "incomplete") conditions.Add("BeforeValue IS NULL OR AfterValue IS NULL");
        if (request.Outcome is not null)
        {
            var choices = request.Outcome.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (choices.Length == 0) throw new ArgumentException("Unknown cell outcome.");
            conditions.Add(string.Join(" OR ", choices.Select(choice => choice switch
            {
                "covered" => "AfterValue=1", "uncovered" => "AfterValue=0", "unknown" => "AfterValue IS NULL",
                _ => throw new ArgumentException("Unknown cell outcome.")
            })));
        }
        var selection = conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions.Select(condition => "(" + condition + ")"));
        var matching = Count(connection, transaction, query + " SELECT COUNT(*) FROM cells" + selection, args);
        var tuple = last is null ? null : ReadTuple(last, 2);
        args.Add(("$lastSet", tuple?[0]));
        args.Add(("$lastCell", tuple?[1]));
        args.Add(("$limit", request.Limit + 1));
        using var command = PageCommand(connection, transaction, query + " SELECT SetId,CellId,FactsJson,VariantCount,BeforeValue,AfterValue FROM cells" +
            (selection.Length == 0 ? " WHERE " : selection + " AND ") +
            "($lastSet IS NULL OR (SetId,CellId)>($lastSet,$lastCell)) ORDER BY SetId,CellId LIMIT $limit;", args.ToArray());
        using var reader = command.ExecuteReader();
        var items = new List<ReviewRow>();
        string? anchor = null;
        var more = false;
        var bytes = 16 * 1024;
        var partial = false;
        while (reader.Read())
        {
            if (items.Count == request.Limit) { more = true; break; }
            var member = JsonSerializer.Deserialize<ReviewMembership>(reader.GetString(2), ReviewJson.Options) ?? throw new InvalidDataException("Invalid cell index.");
            var set = reader.GetString(0);
            var cell = reader.GetString(1);
            var link = CellLink(request.ProjectKey, request.ReportId, set, cell);
            ReviewRow row = new CellRow(set, cell, member.LexemeId ?? "", member.Features, reader.GetInt64(3),
                CellState(reader, 4), CellState(reader, 5), [link]);
            var size = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(row, ReviewJson.Options).Length;
            if (items.Count > 0 && bytes + size > PageBytes) { more = true; break; }
            if (bytes + size > PageBytes)
            {
                row = StubRow(row);
                size = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
                if (bytes + size > PageBytes) throw new ReviewDetailUnavailableException("row-over-budget");
                partial = true;
            }
            bytes += size;
            items.Add(row);
            anchor = JsonSerializer.Serialize(new[] { set, cell });
        }
        var next = more ? ReviewReportCursor.Encode(new(1, request.ProjectKey, request.ReportId.Value,
            request.Section, digest, 1, anchor!, null, null)) : null;
        return new ReviewPage(request.Section, digest, "cells", total, matching, items.Count, request.Limit, next,
            partial ? ListAvailability.Partial : ListAvailability.Complete, items);
    }

    private static CellOutcome CellState(SqliteDataReader reader, int column) => reader.IsDBNull(column)
        ? CellOutcome.Unknown : reader.GetInt64(column) == 1 ? CellOutcome.Covered : CellOutcome.Uncovered;

    private static string WordFilter(ReviewPageRequest request)
    {
        var filter = request.Section switch
        {
            "controls" => " AND EXISTS (SELECT 1 FROM ReviewReportMemberships x WHERE x.ReportId=c.ReportId AND x.CaseKey=c.CaseKey AND x.Role IN ('reference','regression'))",
            "negative-controls" => " AND EXISTS (SELECT 1 FROM ReviewReportMemberships x WHERE x.ReportId=c.ReportId AND x.CaseKey=c.CaseKey AND x.Role='negative')",
            "incomplete" => " AND (json_extract(c.FactsJson,'$.before.completion') != 'complete' OR json_extract(c.FactsJson,'$.after.completion') != 'complete')",
            _ => ""
        };
        if (request.Set is not null) filter += " AND EXISTS (SELECT 1 FROM ReviewReportMemberships x WHERE x.ReportId=c.ReportId AND x.CaseKey=c.CaseKey AND x.SetId IN (SELECT value FROM json_each($set)))";
        if (request.Flag is not null) filter += " AND EXISTS (SELECT 1 FROM json_each(c.FactsJson,'$.flags') f WHERE f.value=$flag)";
        if (request.Filter == "changed") filter += " AND json_extract(c.FactsJson,'$.changed')=1";
        if (request.Filter == "unchanged") filter += " AND json_extract(c.FactsJson,'$.changed')=0";
        if (request.Filter == "review") filter += " AND (json_extract(c.FactsJson,'$.changed')=1 OR json_array_length(c.FactsJson,'$.flags')>0" +
            " OR (json_extract(c.FactsJson,'$.after.completion')='complete' AND json_extract(c.FactsJson,'$.after.parsed')=0)" +
            " OR json_array_length(c.FactsJson,'$.after.missingExpectationIds')>0" +
            " OR json_extract(c.FactsJson,'$.before.completion')!='complete' OR json_extract(c.FactsJson,'$.after.completion')!='complete')";
        if (request.Filter == "incomplete") filter += " AND (json_extract(c.FactsJson,'$.before.completion') != 'complete' OR json_extract(c.FactsJson,'$.after.completion') != 'complete')";
        if (request.Outcome is not null)
        {
            var choices = request.Outcome.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (choices.Length == 0) throw new ArgumentException("Unknown outcome filter.");
            var predicates = choices.Select(choice => choice switch
            {
                "no-parse" => "(json_extract(c.FactsJson,'$.after.completion')='complete' AND json_extract(c.FactsJson,'$.after.parsed')=0)",
                "new-no-parse" => "json_extract(c.FactsJson,'$.newNoParse')=1",
                "approved-reading-lost" => "json_extract(c.FactsJson,'$.newApprovedLoss')=1",
                "reference-reading-lost" => "EXISTS (SELECT 1 FROM ReviewReportEvents e WHERE e.ReportId=c.ReportId AND e.CaseKey=c.CaseKey AND json_extract(e.FactsJson,'$.referenceConfirmed')=1)",
                "negative-now-parsing" => "json_extract(c.FactsJson,'$.newlyAcceptedNegative')=1",
                "parsed" => "json_extract(c.FactsJson,'$.after.parsed')=1",
                "unknown" => "json_extract(c.FactsJson,'$.after.parsed') IS NULL",
                "incomplete" => "(json_extract(c.FactsJson,'$.before.completion')!='complete' OR json_extract(c.FactsJson,'$.after.completion')!='complete')",
                "incomplete/not-measured" => "(json_extract(c.FactsJson,'$.before.completion')='not-measured' OR json_extract(c.FactsJson,'$.after.completion')='not-measured')",
                "incomplete/work-limit" => StopReason("work-limit"),
                "incomplete/time-limit" => StopReason("time-limit"),
                "incomplete/invalid-evidence" => StopReason("invalid-evidence"),
                _ => throw new ArgumentException("Unknown outcome filter.")
            });
            filter += " AND (" + string.Join(" OR ", predicates) + ")";
        }
        return filter;
    }

    private static string StopReason(string reason) =>
        "(EXISTS (SELECT 1 FROM json_each(c.FactsJson,'$.before.stopReasons') WHERE value='" + reason + "') OR " +
        "EXISTS (SELECT 1 FROM json_each(c.FactsJson,'$.after.stopReasons') WHERE value='" + reason + "'))";

    private static IEnumerable<(string, object?)> FilterArguments(ReviewPageRequest request)
    {
        if (request.Set is not null) yield return ("$set", SelectorValues(request.Set));
        if (request.Flag is { } flag) yield return ("$flag", JsonSerializer.SerializeToElement(flag, ReviewJson.Options).GetString());
    }

    private static string SelectorValues(string value)
    {
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0) throw new ArgumentException("A selector requires at least one value.");
        return JsonSerializer.Serialize(values);
    }

    private static (long Memberships, long Expectations, string[] Sets) MembershipSummary(SqliteConnection connection,
        SqliteTransaction transaction, string report, string key, bool expectationsAvailable)
    {
        using var count = PageCommand(connection, transaction,
            "SELECT COUNT(*) FROM ReviewReportMemberships WHERE ReportId=$report AND CaseKey=$case;",
            ("$report", report), ("$case", key));
        using var reader = count.ExecuteReader();
        reader.Read();
        var memberships = reader.GetInt64(0);
        using var expected = PageCommand(connection, transaction, expectationsAvailable
            ? "SELECT COUNT(DISTINCT json_extract(FactsJson,'$.id')) FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND json_extract(FactsJson,'$.kind')='expectation';"
            : "SELECT COUNT(DISTINCT j.value) FROM ReviewReportMemberships m, json_each(m.FactsJson,'$.expectationIds') j WHERE m.ReportId=$report AND m.CaseKey=$case;",
            ("$report", report), ("$case", key));
        var expectations = (long)expected.ExecuteScalar()!;
        using var sets = PageCommand(connection, transaction,
            "SELECT DISTINCT SetId FROM ReviewReportMemberships WHERE ReportId=$report AND CaseKey=$case ORDER BY SetId LIMIT 1000;",
            ("$report", report), ("$case", key));
        using var setReader = sets.ExecuteReader();
        var ids = new List<string>();
        while (setReader.Read()) ids.Add(setReader.GetString(0));
        return (memberships, expectations, ids.ToArray());
    }

    private static ReviewRow StubRow(ReviewRow row) => row switch
    {
        RuleWorkRow rule => rule with { Name = Clip(rule.Name) },
        ReviewWordRow word => word with { Word = Clip(word.Word), Causes = [], SetIds = [] },
        TokenRow token => token with { SourceLocator = Clip(token.SourceLocator) },
        CellRow cell => cell with { Features = [] },
        DiagnosticRow warning => warning with { SubjectIds = [], Description = Clip(warning.Description) },
        FindingRow finding => finding with { Group = Clip(finding.Group), Tier = Clip(finding.Tier) },
        EffectRow effect => effect with { Summary = Clip(effect.Summary) },
        EvidenceRow evidence => evidence with { Limitations = [] },
        ReadingRow reading => reading with { Morphs = [], ExpectationIds = [], SourceRefs = [] },
        ExpectationRow expectation => expectation with { SourceRefs = [] },
        SourceRow source => source with { Excerpt = source.Excerpt is null ? null : Clip(source.Excerpt), ExcerptComplete = false },
        SetRow set => set with { Name = Clip(set.Name), Links = [] },
        _ => throw new InvalidDataException("Unknown review row.")
    };

    private static string Clip(string value)
    {
        if (value.Length <= 1024) return value;
        var length = char.IsHighSurrogate(value[1023]) ? 1023 : 1024;
        return value[..length];
    }

    private static WordSide SideFacts(ReviewCaseSideFacts facts, ReadTarget? link, long? readings, SideTotals? totals) =>
        new(facts.Completion, facts.StopReasons, readings, totals?.ApprovedMatched, totals?.ApprovedMissing,
            totals?.ApprovedUnknown, totals?.ReferenceMatched, totals?.ReferenceMissing, totals?.ReferenceUnknown, null, link);

    private static (long? Before, long? After) ReadingTotals(SqliteConnection connection, SqliteTransaction transaction,
        string report, string key, bool available)
    {
        if (!available) return (null, null);
        using var command = PageCommand(connection, transaction,
            "SELECT COALESCE(SUM(CASE WHEN json_extract(FactsJson,'$.side')='before' THEN 1 ELSE 0 END),0)," +
            "COALESCE(SUM(CASE WHEN json_extract(FactsJson,'$.side')='after' THEN 1 ELSE 0 END),0) " +
            "FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND json_extract(FactsJson,'$.kind')='reading';",
            ("$report", report), ("$case", key));
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static IReadOnlyList<string> CaseCauses(SqliteConnection connection, SqliteTransaction transaction,
        string report, string key, CaseIndexFacts facts)
    {
        var causes = new List<string>();
        if (facts.NewNoParse) causes.Add("no-parse");
        if (facts.NewApprovedLoss) causes.Add("approved-reading-lost");
        using var command = PageCommand(connection, transaction,
            "SELECT 1 FROM ReviewReportEvents WHERE ReportId=$report AND CaseKey=$case AND json_extract(FactsJson,'$.referenceConfirmed')=1 LIMIT 1;",
            ("$report", report), ("$case", key));
        if (command.ExecuteScalar() is not null) causes.Add("reference-reading-lost");
        if (facts.NewlyAcceptedNegative) causes.Add("negative-now-parsing");
        foreach (var reason in facts.Before.StopReasons.Concat(facts.After.StopReasons).Distinct(StringComparer.Ordinal))
            causes.Add("incomplete/" + reason);
        if ((facts.Before.Completion != Completion.Complete || facts.After.Completion != Completion.Complete) &&
            facts.Before.StopReasons.Count == 0 && facts.After.StopReasons.Count == 0) causes.Add("incomplete/not-measured");
        return causes;
    }
    private static ReadTarget CellLink(string project, ReviewReportId report, string set, string cell) =>
        new(TargetKind.Resource, "variants", $"motif://projects/{Uri.EscapeDataString(project)}/trial-reports/{Uri.EscapeDataString(report.Value)}/sets/{Uri.EscapeDataString(set)}/cells/{Uri.EscapeDataString(cell)}/variants", null, null);
    private static ReadTarget ChildLink(string project, ReviewReportId report, string key, string kind) =>
        new(TargetKind.Resource, kind, $"motif://projects/{Uri.EscapeDataString(project)}/trial-reports/{Uri.EscapeDataString(report.Value)}/cases/{Uri.EscapeDataString(key)}/{kind}", null, null);
    private static void RequireDetail(SqliteConnection connection, SqliteTransaction transaction, string project, ReviewReportId report)
    {
        using var command = PageCommand(connection, transaction,
            "SELECT DetailAvailable FROM ReviewReports WHERE ProjectKey=$project AND ReportId=$report;", ("$project", project), ("$report", report.Value));
        if (command.ExecuteScalar() is not long available || available == 0)
            throw new ReviewDetailUnavailableException("not-retained",
                $"motif://projects/{Uri.EscapeDataString(project)}/trial-reports/{Uri.EscapeDataString(report.Value)}");
    }
    private static long Count(SqliteConnection connection, SqliteTransaction transaction, string sql, List<(string, object?)> args)
    {
        using var command = PageCommand(connection, transaction, sql + ";", args.ToArray());
        return (long)command.ExecuteScalar()!;
    }
    private static byte[] ReadSortKey(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new ReviewCursorInvalidException(); }
    }
    private static string[] ReadTuple(string json, int length)
    {
        try
        {
            var tuple = JsonSerializer.Deserialize<string[]>(json);
            return tuple is not null && tuple.Length == length && tuple.All(value => value is not null)
                ? tuple : throw new ReviewCursorInvalidException();
        }
        catch (JsonException) { throw new ReviewCursorInvalidException(); }
    }
    private static SqliteCommand PageCommand(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string, object?)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private sealed record CaseIndexFacts(bool NewNoParse, bool NewApprovedLoss, bool NewlyAcceptedNegative,
        IReadOnlyList<ReviewFlagId> Flags, IReadOnlyList<string> Limitations, ReviewCaseSideFacts Before, ReviewCaseSideFacts After,
        bool ReadingsAvailable, bool ExpectationsAvailable, bool SourcesAvailable, bool? Changed,
        SideTotals? BeforeCounts, SideTotals? AfterCounts);
}

internal static class ReviewReportPages
{
    internal static bool IsEventSection(string section) => section is "parser-work" or "warnings" or "parsimony" or "effects" or "evidence";
    internal static string SectionKey(string section) => "\u001fsection:" + section;
    internal static string Unit(string section) => section switch
    {
        "parser-work" => "rows", "warnings" => "diagnostics", "parsimony" => "findings",
        "effects" => "operations", "evidence" => "bindings", _ => throw new ArgumentException("Unknown section.")
    };
    internal static bool IsSectionRow(string section, ReviewRow row) => section switch
    {
        "parser-work" => row is ReviewWordRow or RuleWorkRow, "warnings" => row is DiagnosticRow,
        "parsimony" => row is FindingRow, "effects" => row is EffectRow,
        "evidence" => row is EvidenceRow or SetRow, _ => false
    };
}
