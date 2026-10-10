using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Executes documented fixed views over a validated Parsimony bundle.</summary>
public static class ParsimonyViewsQuery
{
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 200;

    /// <summary>The longest one page may run before it reports not-computed.</summary>
    internal static readonly TimeSpan DefaultPageDeadline = TimeSpan.FromSeconds(10);

    /// <summary>Runs one closed, bounded view request over the exact supplied bundle.</summary>
    /// <param name="session">The attached bundle whose reader lease bounds the page.</param>
    /// <param name="request">The fixed view, filters and page position to read.</param>
    /// <param name="report">The stored Report the view is bound to, when the request names one.</param>
    /// <param name="pageDeadline">How long the page may run; a page past it reports not-computed, not zero.</param>
    public static ParsimonyNamedViewResponse Execute(ParsimonyQuerySession session,
        ParsimonyNamedViewRequest request, ParsimonyReportResponse? report = null, TimeSpan? pageDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        var deadline = pageDeadline ?? DefaultPageDeadline;
        using var page = CancellationTokenSource.CreateLinkedTokenSource(session.LeaseCancellationToken);
        page.CancelAfter(deadline);
        var registration = session.BeginPage(page.Token);
        try
        {
            return ExecuteCore(session, request, report);
        }
        // Lease loss is refused as busy; a deadline is not-computed. SQLite's interrupt code names a stopped statement.
        catch (Exception exception) when (page.IsCancellationRequested &&
            (exception is OperationCanceledException or SqliteException { SqliteErrorCode: 9 }))
        {
            session.ThrowIfLeaseLost();
            return NotComputed(request, deadline);
        }
        finally
        {
            session.EndPage(registration);
        }
    }

    private static ParsimonyNamedViewResponse NotComputed(ParsimonyNamedViewRequest request, TimeSpan deadline)
    {
        var definition = ParsimonyViewCatalog.Find(request.View)
            ?? throw new ArgumentException($"Unknown Parsimony view '{request.View}'.", nameof(request));
        var within = deadline.TotalSeconds < 1
            ? $"{deadline.TotalMilliseconds:0} milliseconds"
            : $"{deadline.TotalSeconds:0.###} seconds";
        return new ParsimonyNamedViewResponse(request.BundleId, request.View, definition.Version,
            ParsimonyMeasureStatus.Inconclusive, definition.RequiredCapabilities, null, 0, null, false, [], null,
            $"The view did not finish within {within}; nothing was counted.");
    }

    private static ParsimonyNamedViewResponse ExecuteCore(ParsimonyQuerySession session,
        ParsimonyNamedViewRequest request, ParsimonyReportResponse? report)
    {
        session.ThrowIfLeaseLost();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BundleId);
        if (!StringComparer.Ordinal.Equals(request.BundleId, session.BundleId))
            throw new ArgumentException("The named-view request identifies a different bundle than the attached artifacts.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Filters);
        var definition = ParsimonyViewCatalog.Find(request.View)
            ?? throw new ArgumentException($"Unknown Parsimony view '{request.View}'.", nameof(request));
        var limit = request.Limit is 0 ? DefaultLimit : request.Limit;
        if (limit is < 1 or > MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(request), $"View page size must be from 1 to {MaximumLimit}.");
        ValidateFilters(request.View, request.Filters);
        var filterDigest = Digest(JsonSerializer.Serialize(request.Filters, MotifJson.CreateOptions()));
        var offset = ReadOffset(request, definition, filterDigest);
        if (report is not null && (report.ReportId != request.Filters.ReportId ||
            report.Inputs.BundleId != request.BundleId))
            throw new ArgumentException("The stored Report does not match the named-view Report and bundle.", nameof(report));
        if (request.View == "parser-cases" && !session.HasParserOverlay)
            return Unavailable(request, definition, "No parser Assessment overlay is attached to this bundle.");

        IReadOnlyList<ParsimonyViewRow> allRows;
        ParsimonyDispositionProjection? dispositionProjection = null;
        ParsimonyMeasureStatus? computedStatus = null;
        string? computedDetail = null;
        try
        {
            if (request.View is "parsimony-active-findings" or "parsimony-suppressed" or
                "parsimony-suppression-history")
            {
                dispositionProjection = ParsimonyDispositionQuery.Project(session, report?.Findings ?? [],
                    request.Filters.ReportId, report is not null);
                allRows = ReadDispositionRows(request, dispositionProjection, session);
            }
            else if (request.View == "parser-cases")
            {
                var parserCases = session.ReadParserCases(request.Filters.CaseKey);
                allRows = parserCases.Cast<ParsimonyViewRow>().ToArray();
                if (parserCases.Count == 0)
                {
                    computedStatus = ParsimonyMeasureStatus.NotAvailable;
                    computedDetail = "No validated parser cases are available for this bundle.";
                }
                else if (parserCases.Any(item => item.Status != "complete"))
                {
                    computedStatus = ParsimonyMeasureStatus.Inconclusive;
                    computedDetail = "Some parser cases are incomplete or lack an exact wordform identity.";
                }
            }
            else if (request.View == "alternation-families")
            {
                var discovery = session.ReadAlternationFamilies();
                var limitations = discovery.Limitations.Concat(
                [
                    "This static evidence does not establish that a sound change is productive.",
                    "Text and parser witness counts were unavailable for this static measure run.",
                ]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                allRows = discovery.Families.Select(family => (ParsimonyViewRow)new ParsimonyAlternationFamilyViewRow(
                    family.Key, family.Description, family.Lane, family.Directed, family.ContextKey, family.GateKey,
                    family.SideDescription, family.InputPhonemeGuids, family.OutputPhonemeGuids,
                    family.ChangedFeatures.Select(item => new ParsimonyAlternationFeatureChange(item.FeatureGuid,
                        item.InputValueGuid, item.OutputValueGuid)).ToArray(),
                    family.SharedFeatures.Select(item => new ParsimonyAlternationFeatureChange(item.FeatureGuid,
                        item.InputValueGuid, item.OutputValueGuid)).ToArray(),
                    family.Members.Select(item => new ParsimonyAlternationMemberView(item.EntryGuid,
                        item.EntryDescription, item.AllomorphGuids, item.WritingSystems, item.Forms,
                        item.LocalContexts, item.AlignmentEvidence)).ToArray(), limitations)).ToArray();
                computedStatus = discovery.Inconclusive
                    ? ParsimonyMeasureStatus.Inconclusive
                    : ParsimonyMeasureStatus.Computed;
                computedDetail = discovery.Inconclusive
                    ? string.Join(" ", discovery.Limitations)
                    : null;
            }
            else if (request.View == "environment-excess")
            {
                var discovery = session.ReadEnvironmentExcess();
                allRows = discovery.Sites.Where(site => request.Filters.ObjectGuid is null ||
                        site.AllomorphGuid == request.Filters.ObjectGuid ||
                        site.EnvironmentGuids.Contains(request.Filters.ObjectGuid, StringComparer.Ordinal))
                    .Select(site => (ParsimonyViewRow)new ParsimonyEnvironmentExcessViewRow(site.AllomorphGuid,
                        site.OwnerKey, site.MsaGuid, site.Bucket, site.EnvironmentGuids, site.EnvironmentNames,
                        site.Side, Names(site.Universe, site.ContextNames), Names(site.Licensed, site.ContextNames),
                        Names(site.Observed, site.ContextNames), Names(site.Extras, site.ContextNames),
                        Names(site.MissingObserved, site.ContextNames), site.Denominator, site.WordTypes,
                        site.Stems, site.Witnesses,
                        site.ExistingClassesCoveringObserved, site.FeatureIntersection,
                        Names(site.IntersectionExtension, site.ContextNames),
                        Names(site.UnknownFeatureMembers, site.ContextNames), "one-neighbor/v1"))
                    .ToArray();
                var missingSites = discovery.Sites.Count(site => site.MissingObserved.Count > 0);
                computedStatus = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 && missingSites == 0
                    ? ParsimonyMeasureStatus.Computed
                    : ParsimonyMeasureStatus.Inconclusive;
                computedDetail = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 && missingSites == 0
                    ? null
                    : $"Abstained on {discovery.UnsupportedSites} unsupported site(s) and " +
                      $"{discovery.AmbiguousAlignments} ambiguous alignment(s); {missingSites} site(s) miss attested triggers.";
            }
            else if (request.View == "natural-class-excess")
            {
                var discovery = session.ReadNaturalClassExcess();
                allRows = discovery.Sites.Where(site => request.Filters.ObjectGuid is null ||
                        site.ClassGuid == request.Filters.ObjectGuid)
                    .Select(site => (ParsimonyViewRow)new ParsimonyNaturalClassExcessViewRow(site.ClassGuid,
                        site.ClassName, site.UsageSite, site.EnvironmentGuid, site.AllomorphGuid, site.Side,
                        Names(site.Universe, site.ContextNames), Names(site.Observed, site.ContextNames),
                        Names(site.Extras, site.ContextNames), Names(site.MissingObserved, site.ContextNames),
                        site.Denominator, site.FeatureIntersection,
                        Names(site.IntersectionExtension, site.ContextNames),
                        Names(site.UnknownFeatureMembers, site.ContextNames), site.WordTypes, site.Stems,
                        site.Witnesses, site.UnknownClassMembers))
                    .ToArray();
                computedStatus = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0
                    ? ParsimonyMeasureStatus.Computed
                    : ParsimonyMeasureStatus.Inconclusive;
                computedDetail = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 ? null :
                    $"Abstained on {discovery.UnsupportedSites} unsupported usage site(s) and " +
                    $"{discovery.AmbiguousAlignments} ambiguous alignment(s).";
            }
            else
                allRows = ReadRows(session, request);
        }
        catch (ParsimonyQueryUnavailableException exception)
        {
            return Unavailable(request, definition,
                $"Required facts sections are unavailable: {string.Join(", ", exception.Sections)}.");
        }
        catch (ParsimonyScopeUnavailableException exception)
        {
            return Unavailable(request, definition, exception.Message);
        }
        if (offset > allRows.Count)
            throw new ArgumentException("The view cursor is beyond the end of this result.", nameof(request));
        if ((request.View is "allomorph-context" or "affix-context" or "adhoc-context" or
                "natural-class-context" or "slot-context") ||
            (request.View is "approved-morph-sequences" or "unslotted-affixes" or "null-optional" &&
             request.Filters.ObjectGuid is not null))
        {
            if (allRows.Count == 0)
                throw new KeyNotFoundException($"The requested '{request.View}' identity was not found.");
        }
        var digest = Digest(JsonSerializer.Serialize(allRows, MotifJson.CreateOptions()));
        var page = allRows.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var truncated = nextOffset < allRows.Count;
        var cursor = truncated ? EncodeCursor(new ParsimonyViewCursor(request.BundleId, request.View,
            definition.Version, filterDigest, nextOffset)) : null;
        var status = computedStatus ?? Status(request.View, allRows);
        return new ParsimonyNamedViewResponse(request.BundleId, request.View, definition.Version, status,
            definition.RequiredCapabilities, allRows.Count, page.Length, cursor, truncated,
            Array.AsReadOnly(page), digest, computedDetail)
        {
            ActiveCount = dispositionProjection?.ActiveCount,
            SuppressedCount = dispositionProjection?.SuppressedCount,
            ResurfacedCount = dispositionProjection?.ResurfacedCount,
            UnresolvedJudgmentCount = dispositionProjection?.UnresolvedJudgmentCount,
            JudgmentProjectionDigest = dispositionProjection?.JudgmentProjectionDigest,
        };
    }

    /// <summary>Lists saved decisions when their finding artifact or Report cannot be read.</summary>
    public static ParsimonyNamedViewResponse ExecuteUnavailableEvidence(ParsimonyNamedViewRequest request,
        SIL.Motif.Projection.HumanJudgments.HumanJudgmentLineageProjection judgments)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(judgments);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BundleId);
        ArgumentNullException.ThrowIfNull(request.Filters);
        var definition = ParsimonyViewCatalog.Find(request.View)
            ?? throw new ArgumentException($"Unknown Parsimony view '{request.View}'.", nameof(request));
        if (request.View is not ("parsimony-suppressed" or "parsimony-suppression-history"))
            throw new ArgumentException("Saved decisions can be read without artifacts only in suppression views.",
                nameof(request));
        var limit = request.Limit is 0 ? DefaultLimit : request.Limit;
        if (limit is < 1 or > MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(request), $"View page size must be from 1 to {MaximumLimit}.");
        ValidateFilters(request.View, request.Filters);
        var filterDigest = Digest(JsonSerializer.Serialize(request.Filters, MotifJson.CreateOptions()));
        var offset = ReadOffset(request, definition, filterDigest);
        var projection = ParsimonyDispositionQuery.Project(request.BundleId, request.Filters.ReportId, null,
            [], judgments, reportAvailable: false);
        var allRows = ReadDispositionRows(request, projection);
        if (offset > allRows.Count)
            throw new ArgumentException("The view cursor is beyond the end of this result.", nameof(request));
        var digest = Digest(JsonSerializer.Serialize(allRows, MotifJson.CreateOptions()));
        var page = allRows.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var truncated = nextOffset < allRows.Count;
        var cursor = truncated ? EncodeCursor(new ParsimonyViewCursor(request.BundleId, request.View,
            definition.Version, filterDigest, nextOffset)) : null;
        return new ParsimonyNamedViewResponse(request.BundleId, request.View, definition.Version,
            ParsimonyMeasureStatus.Inconclusive, definition.RequiredCapabilities, allRows.Count, page.Length,
            cursor, truncated, Array.AsReadOnly(page), digest,
            "Finding evidence or its Report is unavailable; saved decisions are listed without claiming a current match.")
        {
            UnresolvedJudgmentCount = projection.UnresolvedJudgmentCount,
            JudgmentProjectionDigest = projection.JudgmentProjectionDigest,
        };
    }

    private static IReadOnlyList<ParsimonyViewRow> ReadDispositionRows(ParsimonyNamedViewRequest request,
        ParsimonyDispositionProjection projection, ParsimonyQuerySession? session = null)
    {
        var filters = request.Filters;
        IEnumerable<ParsimonyViewRow> rows = request.View switch
        {
            "parsimony-active-findings" => projection.Findings
                .Where(item => item.State is "active" or "resurfaced"),
            "parsimony-suppressed" => projection.History
                .Where(item => item.State is not ("superseded" or "retracted")),
            "parsimony-suppression-history" => projection.History.Cast<ParsimonyViewRow>()
                .Concat(projection.Unresolved),
            _ => throw new ArgumentException($"Unknown disposition view '{request.View}'.", nameof(request)),
        };
        if (filters.MeasureId is { } measureId)
            rows = rows.Where(row => row switch
            {
                ParsimonyFindingDispositionViewRow finding => finding.Finding.MeasureId == measureId,
                ParsimonySuppressionHistoryViewRow history => history.MeasureId == measureId,
                _ => false,
            });
        if (filters.SubjectKey is { } subjectKey)
            rows = rows.Where(row => row switch
            {
                ParsimonyFindingDispositionViewRow finding =>
                    (finding.Finding.GroupKey ?? finding.Finding.AttachesTo.Identity) == subjectKey,
                ParsimonySuppressionHistoryViewRow history => history.SubjectKey == subjectKey,
                _ => false,
            });
        if (filters.Disposition is { } disposition)
            rows = rows.Where(row => row switch
            {
                ParsimonyFindingDispositionViewRow finding => finding.Disposition == disposition,
                ParsimonySuppressionHistoryViewRow history => history.Disposition == disposition,
                _ => false,
            });
        if (filters.State is { } state)
            rows = rows.Where(row => row switch
            {
                ParsimonyFindingDispositionViewRow finding => finding.State == state,
                ParsimonySuppressionHistoryViewRow history => history.State == state,
                ParsimonyUnresolvedJudgmentViewRow unresolved => unresolved.State == state,
                _ => false,
            });
        if (filters.Search is { } search)
            rows = rows.Where(row => SearchText(row, session).Contains(search, StringComparison.OrdinalIgnoreCase));
        return rows.ToArray();
    }

    private static string SearchText(ParsimonyViewRow row, ParsimonyQuerySession? session) => row switch
    {
        ParsimonyFindingDispositionViewRow finding => string.Join('\n', finding.Finding.MeasureId,
            finding.Finding.FindingId, finding.Finding.AttachesTo.Identity,
            session is null ? null : ParsimonyReportProducer.DescribeFinding(session, finding.Finding),
            finding.Reason, finding.Finding.RecipeLink),
        ParsimonySuppressionHistoryViewRow history => string.Join('\n', history.MeasureId, history.SubjectKey,
            history.SubjectCaption, history.MeasureCaption, history.Disposition, history.Reason, history.State),
        ParsimonyUnresolvedJudgmentViewRow unresolved => string.Join('\n', unresolved.RecordId,
            unresolved.JudgmentId, unresolved.Reason),
        ParsimonyEnvironmentExcessViewRow environment => string.Join('\n', environment.AllomorphGuid,
            environment.EnvironmentNames, environment.Side, environment.Bucket, environment.Observed,
            environment.LicensedExtras, environment.Witnesses),
        ParsimonyNaturalClassExcessViewRow naturalClass => string.Join('\n', naturalClass.ClassGuid,
            naturalClass.ClassName, naturalClass.UsageSite, naturalClass.Observed,
            naturalClass.CurrentClassExtras, naturalClass.FeatureIntersection, naturalClass.Witnesses),
        _ => string.Empty,
    };

    private static IReadOnlyList<ParsimonyViewRow> ReadRows(ParsimonyQuerySession session,
        ParsimonyNamedViewRequest request)
    {
        var filters = request.Filters;
        return request.View switch
        {
            "join-quality" => ReadJoinQuality(session, filters),
            "allomorph-context" => [RequireRow(session.ReadAllomorphContext(filters.ObjectGuid!), request.View)],
            "approved-morph-sequences" => session.ReadApprovedMorphSequences(filters.ObjectGuid)
                .Cast<ParsimonyViewRow>().ToArray(),
            "unslotted-affixes" => session.ReadUnslottedAffixes(filters.ObjectGuid)
                .Cast<ParsimonyViewRow>().ToArray(),
            "null-optional" => session.ReadNullOptionalAffixes(filters.ObjectGuid)
                .Cast<ParsimonyViewRow>().ToArray(),
            "statement-usage" => FilterStatementUsage(session, filters),
            "adhoc-context" => [RequireRow(session.ReadAdhocContext(filters.ObjectGuid!), request.View)],
            "affix-context" => [RequireRow(session.ReadAffixContext(filters.ObjectGuid!), request.View)],
            "slot-context" => [RequireRow(session.ReadSlotContext(filters.ObjectGuid!), request.View)],
            "template-order" => session.ReadTemplateOrder(filters.ObjectGuid, filters.CategoryGuid)
                .Cast<ParsimonyViewRow>().ToArray(),
            "natural-class-context" => [RequireRow(session.ReadNaturalClassContext(filters.ObjectGuid!), request.View)],
            _ => throw new ArgumentException($"Unknown Parsimony view '{request.View}'.", nameof(request)),
        };
    }

    private static T RequireRow<T>(T? row, string view) where T : ParsimonyViewRow =>
        row ?? throw new KeyNotFoundException($"The requested '{view}' identity was not found.");

    private static IReadOnlyList<string> Names(IEnumerable<string> identities,
        IReadOnlyDictionary<string, string> names) => identities
        .Select(identity => names.GetValueOrDefault(identity, identity)).ToArray();

    private static IReadOnlyList<ParsimonyViewRow> ReadJoinQuality(ParsimonyQuerySession session,
        ParsimonyViewFilters filters)
    {
        var scope = filters.Scope switch
        {
            null or "project-approved" => ParsimonyEvidenceScopeKind.ProjectApproved,
            "default-selection" => ParsimonyEvidenceScopeKind.DefaultSelection,
            _ => throw new ArgumentException("Join-quality scope must be 'project-approved' or 'default-selection'."),
        };
        return [new ParsimonyJoinQualityViewRow(session.ReadJoinQuality(scope))];
    }

    private static IReadOnlyList<ParsimonyViewRow> FilterStatementUsage(ParsimonyQuerySession session,
        ParsimonyViewFilters filters) => session.ReadStatementUsage(filters.StatementKind)
        .Where(row => filters.ObjectGuid is null || row.StatementGuid == filters.ObjectGuid)
        .Cast<ParsimonyViewRow>().ToArray();

    private static ParsimonyMeasureStatus Status(string view, IReadOnlyList<ParsimonyViewRow> rows) => view switch
    {
        "join-quality" when rows.Single() is ParsimonyJoinQualityViewRow { Quality.ScopeAvailable: false } =>
            ParsimonyMeasureStatus.Inconclusive,
        "allomorph-context" when rows.Cast<ParsimonyAllomorphViewRow>()
            .Any(row => row.Environments.Any(environment => environment.Compiled is null)) =>
            ParsimonyMeasureStatus.Inconclusive,
        "approved-morph-sequences" when rows.Cast<ParsimonyApprovedMorphSequenceViewRow>()
            .Any(row => row.Morphs.Any(morph => morph.MorphGuid is null || morph.MsaGuid is null ||
                morph.MorphType is null || morph.MsaKind is null)) => ParsimonyMeasureStatus.Inconclusive,
        "adhoc-context" when rows.Cast<ParsimonyAdhocViewRow>()
            .Any(row => row.Prohibition.Loaded is null || !row.GroupedFactsAvailable) =>
            ParsimonyMeasureStatus.Inconclusive,
        "natural-class-context" when rows.Cast<ParsimonyNaturalClassViewRow>().Any(row => row.Loaded is null) =>
            ParsimonyMeasureStatus.Inconclusive,
        _ => ParsimonyMeasureStatus.Computed,
    };

    private static void ValidateFilters(string view, ParsimonyViewFilters filters)
    {
        static void RequireOnly(ParsimonyViewFilters filters, string allowed)
        {
            var values = new Dictionary<string, string?>
            {
                [nameof(filters.ObjectGuid)] = filters.ObjectGuid,
                [nameof(filters.CategoryGuid)] = filters.CategoryGuid,
                [nameof(filters.StatementKind)] = filters.StatementKind,
                [nameof(filters.Scope)] = filters.Scope,
                [nameof(filters.CaseKey)] = filters.CaseKey,
                [nameof(filters.ReportId)] = filters.ReportId,
                [nameof(filters.MeasureId)] = filters.MeasureId,
                [nameof(filters.Disposition)] = filters.Disposition,
                [nameof(filters.State)] = filters.State,
                [nameof(filters.Search)] = filters.Search,
                [nameof(filters.SubjectKey)] = filters.SubjectKey,
            };
            var invalid = values.Where(pair => pair.Value is not null && pair.Key != allowed)
                .Select(pair => pair.Key).ToArray();
            if (invalid.Length != 0)
                throw new ArgumentException($"Filter(s) are not valid for this view: {string.Join(", ", invalid)}.");
        }

        switch (view)
        {
            case "join-quality":
                RequireOnly(filters, nameof(filters.Scope));
                break;
            case "allomorph-context":
            case "adhoc-context":
            case "affix-context":
            case "natural-class-context":
            case "environment-excess":
            case "natural-class-excess":
            case "slot-context":
                RequireOnly(filters, nameof(filters.ObjectGuid));
                if (view is "environment-excess" or "natural-class-excess")
                {
                    if (filters.ObjectGuid is not null) RequireId(filters.ObjectGuid);
                }
                else RequireId(filters.ObjectGuid);
                break;
            case "unslotted-affixes":
            case "null-optional":
                RequireOnly(filters, nameof(filters.ObjectGuid));
                if (filters.ObjectGuid is not null) RequireId(filters.ObjectGuid);
                break;
            case "approved-morph-sequences":
                RequireOnly(filters, nameof(filters.ObjectGuid));
                if (filters.ObjectGuid is not null) RequireId(filters.ObjectGuid);
                break;
            case "template-order":
                RequireOnlyTemplateFilter(filters);
                break;
            case "statement-usage":
                if (filters.StatementKind is not (null or "environment" or "natural-class"))
                    throw new ArgumentException("Statement kind must be 'environment' or 'natural-class'.");
                if (filters.ObjectGuid is not null)
                    RequireId(filters.ObjectGuid);
                RequireNoFilters(filters, nameof(filters.StatementKind), nameof(filters.ObjectGuid));
                break;
            case "parser-cases":
                RequireNoFilters(filters, nameof(filters.CaseKey), nameof(filters.ReportId));
                if (filters.CaseKey is { Length: > 256 })
                    throw new ArgumentException("Parser case keys are limited to 256 characters.");
                if (filters.ReportId is { Length: > 128 } || (filters.ReportId is not null &&
                    !SIL.Motif.Contract.Ids.CanonicalId.TryParse(filters.ReportId, out _)))
                    throw new ArgumentException("Report id must be a canonical id of at most 128 characters.", nameof(filters));
                break;
            case "parsimony-active-findings":
                RequireDispositionFilters(filters, requireReport: true, allowDisposition: false);
                break;
            case "parsimony-suppressed":
            case "parsimony-suppression-history":
                RequireDispositionFilters(filters, requireReport: false, allowDisposition: true);
                break;
            case "alternation-families":
                RequireNoFilters(filters);
                break;
        }
    }

    private static void RequireOnlyTemplateFilter(ParsimonyViewFilters filters)
    {
        if ((filters.ObjectGuid is null) == (filters.CategoryGuid is null))
            throw new ArgumentException("Template-order needs exactly one object GUID or category GUID.");
        RequireNoFilters(filters, nameof(filters.ObjectGuid), nameof(filters.CategoryGuid));
        RequireId(filters.ObjectGuid ?? filters.CategoryGuid);
    }

    private static void RequireNoFilters(ParsimonyViewFilters filters, params string[] allowed)
    {
        var nonempty = new Dictionary<string, string?>
        {
            [nameof(filters.ObjectGuid)] = filters.ObjectGuid,
            [nameof(filters.CategoryGuid)] = filters.CategoryGuid,
            [nameof(filters.StatementKind)] = filters.StatementKind,
            [nameof(filters.Scope)] = filters.Scope,
            [nameof(filters.CaseKey)] = filters.CaseKey,
            [nameof(filters.ReportId)] = filters.ReportId,
            [nameof(filters.MeasureId)] = filters.MeasureId,
            [nameof(filters.Disposition)] = filters.Disposition,
            [nameof(filters.State)] = filters.State,
            [nameof(filters.Search)] = filters.Search,
            [nameof(filters.SubjectKey)] = filters.SubjectKey,
        };
        var invalid = nonempty.Where(pair => pair.Value is not null && !allowed.Contains(pair.Key, StringComparer.Ordinal))
            .Select(pair => pair.Key).ToArray();
        if (invalid.Length != 0)
            throw new ArgumentException($"Filter(s) are not valid for this view: {string.Join(", ", invalid)}.");
    }

    private static void RequireDispositionFilters(ParsimonyViewFilters filters, bool requireReport,
        bool allowDisposition)
    {
        var allowed = allowDisposition
            ? new[] { nameof(filters.ReportId), nameof(filters.MeasureId), nameof(filters.Disposition),
                nameof(filters.State), nameof(filters.Search), nameof(filters.SubjectKey) }
            : new[] { nameof(filters.ReportId), nameof(filters.MeasureId), nameof(filters.State),
                nameof(filters.Search), nameof(filters.SubjectKey) };
        RequireNoFilters(filters, allowed);
        if (requireReport && filters.ReportId is null)
            throw new ArgumentException("This view requires an exact stored Report id.", nameof(filters));
        if (filters.ReportId is { Length: > 128 } || (filters.ReportId is not null &&
            !SIL.Motif.Contract.Ids.CanonicalId.TryParse(filters.ReportId, out _)))
            throw new ArgumentException("Report id must be a canonical id of at most 128 characters.", nameof(filters));
        if (filters.MeasureId is { Length: > 256 } ||
            (filters.MeasureId is not null && string.IsNullOrWhiteSpace(filters.MeasureId)))
            throw new ArgumentException("Measure id is invalid.", nameof(filters));
        if (filters.SubjectKey is { Length: > 512 } ||
            (filters.SubjectKey is not null && string.IsNullOrWhiteSpace(filters.SubjectKey)))
            throw new ArgumentException("Subject key is invalid.", nameof(filters));
        if (allowDisposition && filters.Disposition is not (null or "keep" or "defer" or "fix" or "ask"))
            throw new ArgumentException("Disposition must be keep, defer, fix, or ask.", nameof(filters));
        if (filters.State is { } state && state is not ("active" or "suppressed" or "resurfaced" or
            "no-current-finding" or "evidence-unavailable" or "conflict" or "unavailable" or "retracted" or
            "superseded"))
            throw new ArgumentException("State is not a supported Parsimony judgment state.", nameof(filters));
        if (filters.Search is { } search && (search.Length > 256 || search.Any(char.IsControl)))
            throw new ArgumentException("Search text is limited to 256 printable characters.", nameof(filters));
    }

    private static void RequireId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("A nonblank authored-object identity of at most 128 characters is required.");
    }

    private static int ReadOffset(ParsimonyNamedViewRequest request, ParsimonyViewDefinition definition,
        string filterDigest)
    {
        if (request.Cursor is null) return 0;
        ParsimonyViewCursor cursor;
        try
        {
            var bytes = Base64UrlDecode(request.Cursor);
            cursor = JsonSerializer.Deserialize<ParsimonyViewCursor>(bytes, MotifJson.CreateOptions())
                ?? throw new JsonException("The cursor is empty.");
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException("The view cursor is malformed.", nameof(request), exception);
        }
        if (cursor.BundleId != request.BundleId || cursor.View != request.View || cursor.Version != definition.Version ||
            cursor.FilterDigest != filterDigest || cursor.Offset < 0)
            throw new ArgumentException("The view cursor belongs to a different bundle, view, version, or filter.",
                nameof(request));
        return cursor.Offset;
    }

    private static ParsimonyNamedViewResponse Unavailable(ParsimonyNamedViewRequest request,
        ParsimonyViewDefinition definition, string detail) => new(request.BundleId, request.View, definition.Version,
        ParsimonyMeasureStatus.NotAvailable, definition.RequiredCapabilities, null, 0, null, false, [], null, detail);

    private static string EncodeCursor(ParsimonyViewCursor cursor) => Base64UrlEncode(
        JsonSerializer.SerializeToUtf8Bytes(cursor, MotifJson.CreateOptions()));

    private static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        if (value.Length > 4096 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new FormatException("The cursor is not base64url text.");
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }
}
