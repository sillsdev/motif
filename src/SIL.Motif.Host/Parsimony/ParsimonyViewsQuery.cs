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
        if (request.View == ParsimonyViews.ParserCases && !session.HasParserOverlay)
            return Unavailable(request, definition, "No parser Assessment overlay is attached to this bundle.");

        var cacheKey = new ParsimonyViewCacheKey(session.ProofKey, request.View, definition.Version, filterDigest,
            report?.ReportId);
        var cached = ParsimonyViewResultCache.Shared.Find(cacheKey);
        IReadOnlyList<ParsimonyViewRow> allRows = cached?.Rows ?? [];
        ParsimonyDispositionProjection? dispositionProjection = cached?.DispositionProjection;
        ParsimonyMeasureStatus? computedStatus = cached?.ComputedStatus;
        string? computedDetail = cached?.ComputedDetail;
        if (cached is null)
        try
        {
            if (request.View is ParsimonyViews.ParsimonyActiveFindings or ParsimonyViews.ParsimonySuppressed or
                ParsimonyViews.ParsimonySuppressionHistory)
            {
                dispositionProjection = ParsimonyDispositionQuery.Project(session, report?.Findings ?? [],
                    request.Filters.ReportId, report is not null);
                allRows = ReadDispositionRows(request, dispositionProjection, session);
            }
            else if (request.View == ParsimonyViews.ParserCases)
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
            else if (request.View == ParsimonyViews.AlternationFamilies)
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
            else if (request.View == ParsimonyViews.EnvironmentExcess)
            {
                var discovery = session.ReadEnvironmentExcess();
                allRows = discovery.Sites.Where(site => request.Filters.ObjectGuid is null ||
                        site.AllomorphGuid == request.Filters.ObjectGuid ||
                        site.EnvironmentGuids.Contains(request.Filters.ObjectGuid, StringComparer.Ordinal))
                    .Select(site => EnvironmentRow(site, request.Filters.ObjectGuid is null)).ToArray();
                var missingSites = discovery.Sites.Count(site => site.MissingObserved.Count > 0);
                computedStatus = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 && missingSites == 0
                    ? ParsimonyMeasureStatus.Computed
                    : ParsimonyMeasureStatus.Inconclusive;
                computedDetail = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 && missingSites == 0
                    ? null
                    : $"Abstained on {discovery.UnsupportedSites} unsupported site(s) and " +
                      $"{discovery.AmbiguousAlignments} ambiguous alignment(s); {missingSites} site(s) miss attested triggers.";
            }
            else if (request.View == ParsimonyViews.NaturalClassExcess)
            {
                var discovery = session.ReadNaturalClassExcess();
                allRows = discovery.Sites.Where(site => request.Filters.ObjectGuid is null ||
                        site.ClassGuid == request.Filters.ObjectGuid)
                    .Select(site => NaturalClassRow(site, request.Filters.ObjectGuid is null)).ToArray();
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
        if ((request.View is ParsimonyViews.AllomorphContext or ParsimonyViews.AffixContext or ParsimonyViews.AdhocContext or
                ParsimonyViews.NaturalClassContext or ParsimonyViews.SlotContext) ||
            (request.View is ParsimonyViews.ApprovedMorphSequences or ParsimonyViews.UnslottedAffixes or ParsimonyViews.NullOptional &&
             request.Filters.ObjectGuid is not null))
        {
            if (allRows.Count == 0)
                throw new KeyNotFoundException($"The requested '{request.View}' identity was not found.");
        }
        var digest = cached?.ItemsDigest ?? ItemsDigest(allRows);
        if (cached is null)
            ParsimonyViewResultCache.Shared.Store(cacheKey, new ParsimonyViewResult(allRows, digest, dispositionProjection,
                computedStatus, computedDetail));
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
        if (request.View is not (ParsimonyViews.ParsimonySuppressed or ParsimonyViews.ParsimonySuppressionHistory))
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
        var digest = ItemsDigest(allRows);
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
            ParsimonyViews.ParsimonyActiveFindings => projection.Findings
                .Where(item => item.State is "active" or "resurfaced"),
            ParsimonyViews.ParsimonySuppressed => projection.History
                .Where(item => item.State is not ("superseded" or "retracted")),
            ParsimonyViews.ParsimonySuppressionHistory => projection.History.Cast<ParsimonyViewRow>()
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
            history.SubjectCaption, history.MeasureCaption, history.Disposition, history.Reason, history.State, history.VisibleId, history.RecordTitle),
        ParsimonyUnresolvedJudgmentViewRow unresolved => string.Join('\n', unresolved.RecordId,
            unresolved.JudgmentId, unresolved.Reason, unresolved.Check, unresolved.VisibleId, unresolved.RecordTitle),
        ParsimonyEnvironmentExcessViewRow environment => string.Join('\n', environment.AllomorphGuid,
            environment.EnvironmentNames, environment.Side, environment.Bucket, environment.Observed,
            environment.LicensedExtras, environment.Witnesses),
        ParsimonyNaturalClassExcessViewRow naturalClass => string.Join('\n', naturalClass.ClassGuid,
            naturalClass.ClassName, naturalClass.UsageSite, naturalClass.Observed,
            naturalClass.CurrentClassExtras, naturalClass.FeatureIntersection, naturalClass.Witnesses),
        _ => string.Empty,
    };

    private static readonly IReadOnlyDictionary<string, Func<ParsimonyQuerySession, ParsimonyNamedViewRequest,
        IReadOnlyList<ParsimonyViewRow>>> RowReaders = new Dictionary<string, Func<ParsimonyQuerySession,
        ParsimonyNamedViewRequest, IReadOnlyList<ParsimonyViewRow>>>(StringComparer.Ordinal)
    {
        [ParsimonyViews.JoinQuality] = (session, request) => ReadJoinQuality(session, request.Filters),
        [ParsimonyViews.AllomorphContext] = (session, request) =>
            [RequireRow(session.ReadAllomorphContext(request.Filters.ObjectGuid!), request.View)],
        [ParsimonyViews.ApprovedMorphSequences] = (session, request) =>
            session.ReadApprovedMorphSequences(request.Filters.ObjectGuid).Cast<ParsimonyViewRow>().ToArray(),
        [ParsimonyViews.UnslottedAffixes] = (session, request) =>
            session.ReadUnslottedAffixes(request.Filters.ObjectGuid).Cast<ParsimonyViewRow>().ToArray(),
        [ParsimonyViews.NullOptional] = (session, request) =>
            session.ReadNullOptionalAffixes(request.Filters.ObjectGuid).Cast<ParsimonyViewRow>().ToArray(),
        [ParsimonyViews.StatementUsage] = (session, request) => FilterStatementUsage(session, request.Filters),
        [ParsimonyViews.AdhocContext] = (session, request) =>
            [RequireRow(session.ReadAdhocContext(request.Filters.ObjectGuid!), request.View)],
        [ParsimonyViews.AffixContext] = (session, request) =>
            [RequireRow(session.ReadAffixContext(request.Filters.ObjectGuid!), request.View)],
        [ParsimonyViews.SlotContext] = (session, request) =>
            [RequireRow(session.ReadSlotContext(request.Filters.ObjectGuid!), request.View)],
        [ParsimonyViews.TemplateOrder] = (session, request) =>
            session.ReadTemplateOrder(request.Filters.ObjectGuid, request.Filters.CategoryGuid)
                .Cast<ParsimonyViewRow>().ToArray(),
        [ParsimonyViews.NaturalClassContext] = (session, request) =>
            [RequireRow(session.ReadNaturalClassContext(request.Filters.ObjectGuid!), request.View)],
    };

    // Views whose rows ExecuteCore builds itself, because they also decide the status or detail of the response.
    private static readonly string[] ComputedViews =
    [
        ParsimonyViews.ParsimonyActiveFindings,
        ParsimonyViews.ParsimonySuppressed,
        ParsimonyViews.ParsimonySuppressionHistory,
        ParsimonyViews.ParserCases,
        ParsimonyViews.AlternationFamilies,
        ParsimonyViews.EnvironmentExcess,
        ParsimonyViews.NaturalClassExcess,
    ];

    /// <summary>The view codes this build can execute, which the view catalog must equal.</summary>
    public static IReadOnlySet<string> ImplementedViews { get; } =
        RowReaders.Keys.Concat(ComputedViews).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<ParsimonyViewRow> ReadRows(ParsimonyQuerySession session,
        ParsimonyNamedViewRequest request) =>
        RowReaders.TryGetValue(request.View, out var reader)
            ? reader(session, request)
            : throw new ArgumentException($"Unknown Parsimony view '{request.View}'.", nameof(request));

    private static T RequireRow<T>(T? row, string view) where T : ParsimonyViewRow =>
        row ?? throw new KeyNotFoundException($"The requested '{view}' identity was not found.");

    internal static ParsimonyViewRow EnvironmentRow(EnvironmentExcessSite site, bool capped)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal) { ["universe"] = site.Universe.Count };
        return new ParsimonyEnvironmentExcessViewRow(site.AllomorphGuid,
            site.OwnerKey, site.MsaGuid, site.Bucket, site.EnvironmentGuids, site.EnvironmentNames,
            site.Side, Capped(site.Licensed, site.ContextNames, totals, "licensed", capped),
            Capped(site.Observed, site.ContextNames, totals, "observed", capped),
            Capped(site.Extras, site.ContextNames, totals, "licensedExtras", capped),
            Capped(site.MissingObserved, site.ContextNames, totals, "missingObserved", capped), site.Denominator,
            site.WordTypes, site.Stems, Capped(site.Witnesses, null, totals, "witnesses", capped),
            site.ExistingClassesCoveringObserved, site.FeatureIntersection,
            Capped(site.IntersectionExtension, site.ContextNames, totals, "intersectionExtension", capped),
            Capped(site.UnknownFeatureMembers, site.ContextNames, totals, "unknownFeatureMembers", capped),
            "one-neighbor/v1", totals);
    }

    internal static ParsimonyViewRow NaturalClassRow(NaturalClassExcessSite site, bool capped)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal) { ["universe"] = site.Universe.Count };
        return new ParsimonyNaturalClassExcessViewRow(site.ClassGuid, site.ClassName, site.UsageSite,
            site.EnvironmentGuid, site.AllomorphGuid, site.Side,
            Capped(site.Observed, site.ContextNames, totals, "observed", capped),
            Capped(site.Extras, site.ContextNames, totals, "currentClassExtras", capped),
            Capped(site.MissingObserved, site.ContextNames, totals, "missingObserved", capped),
            site.Denominator, site.FeatureIntersection,
            Capped(site.IntersectionExtension, site.ContextNames, totals, "intersectionExtension", capped),
            Capped(site.UnknownFeatureMembers, site.ContextNames, totals, "unknownFeatureMembers", capped),
            site.WordTypes, site.Stems, Capped(site.Witnesses, null, totals, "witnesses", capped),
            Capped(site.UnknownClassMembers, null, totals, "unknownClassMembers", capped), totals);
    }

    private static IReadOnlyList<string> Capped(IReadOnlyCollection<string> identities,
        IReadOnlyDictionary<string, string>? names, Dictionary<string, int> totals, string key, bool capped)
    {
        totals[key] = identities.Count;
        var kept = capped ? identities.Take(ParsimonyViewLimits.ListCap) : identities;
        return names is null ? kept.ToArray() : Names(kept, names);
    }

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
        ParsimonyViews.JoinQuality when rows.Single() is ParsimonyJoinQualityViewRow { Quality.ScopeAvailable: false } =>
            ParsimonyMeasureStatus.Inconclusive,
        ParsimonyViews.AllomorphContext when rows.Cast<ParsimonyAllomorphViewRow>()
            .Any(row => row.Environments.Any(environment => environment.Compiled is null)) =>
            ParsimonyMeasureStatus.Inconclusive,
        ParsimonyViews.ApprovedMorphSequences when rows.Cast<ParsimonyApprovedMorphSequenceViewRow>()
            .Any(row => row.Morphs.Any(morph => morph.MorphGuid is null || morph.MsaGuid is null ||
                morph.MorphType is null || morph.MsaKind is null)) => ParsimonyMeasureStatus.Inconclusive,
        ParsimonyViews.AdhocContext when rows.Cast<ParsimonyAdhocViewRow>()
            .Any(row => row.Prohibition.Loaded is null || !row.GroupedFactsAvailable) =>
            ParsimonyMeasureStatus.Inconclusive,
        ParsimonyViews.NaturalClassContext when rows.Cast<ParsimonyNaturalClassViewRow>().Any(row => row.Loaded is null) =>
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
            case ParsimonyViews.JoinQuality:
                RequireOnly(filters, nameof(filters.Scope));
                break;
            case ParsimonyViews.AllomorphContext:
            case ParsimonyViews.AdhocContext:
            case ParsimonyViews.AffixContext:
            case ParsimonyViews.NaturalClassContext:
            case ParsimonyViews.EnvironmentExcess:
            case ParsimonyViews.NaturalClassExcess:
            case ParsimonyViews.SlotContext:
                RequireOnly(filters, nameof(filters.ObjectGuid));
                if (view is ParsimonyViews.EnvironmentExcess or ParsimonyViews.NaturalClassExcess)
                {
                    if (filters.ObjectGuid is not null) RequireId(filters.ObjectGuid);
                }
                else RequireId(filters.ObjectGuid);
                break;
            case ParsimonyViews.UnslottedAffixes:
            case ParsimonyViews.NullOptional:
                RequireOnly(filters, nameof(filters.ObjectGuid));
                if (filters.ObjectGuid is not null) RequireId(filters.ObjectGuid);
                break;
            case ParsimonyViews.ApprovedMorphSequences:
                RequireOnly(filters, nameof(filters.ObjectGuid));
                if (filters.ObjectGuid is not null) RequireId(filters.ObjectGuid);
                break;
            case ParsimonyViews.TemplateOrder:
                RequireOnlyTemplateFilter(filters);
                break;
            case ParsimonyViews.StatementUsage:
                if (filters.StatementKind is not (null or "environment" or "natural-class"))
                    throw new ArgumentException("Statement kind must be 'environment' or 'natural-class'.");
                if (filters.ObjectGuid is not null)
                    RequireId(filters.ObjectGuid);
                RequireNoFilters(filters, nameof(filters.StatementKind), nameof(filters.ObjectGuid));
                break;
            case ParsimonyViews.ParserCases:
                RequireNoFilters(filters, nameof(filters.CaseKey), nameof(filters.ReportId));
                if (filters.CaseKey is { Length: > 256 })
                    throw new ArgumentException("Parser case keys are limited to 256 characters.");
                if (filters.ReportId is { Length: > 128 } || (filters.ReportId is not null &&
                    !SIL.Motif.Contract.Ids.CanonicalId.TryParse(filters.ReportId, out _)))
                    throw new ArgumentException("Report id must be a canonical id of at most 128 characters.", nameof(filters));
                break;
            case ParsimonyViews.ParsimonyActiveFindings:
                RequireDispositionFilters(filters, requireReport: true, allowDisposition: false);
                break;
            case ParsimonyViews.ParsimonySuppressed:
            case ParsimonyViews.ParsimonySuppressionHistory:
                RequireDispositionFilters(filters, requireReport: false, allowDisposition: true);
                break;
            case ParsimonyViews.AlternationFamilies:
                RequireNoFilters(filters);
                break;
            default:
                throw new InvalidOperationException($"View '{view}' has no filter rule.");
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

    /// <summary>
    /// Hashes the complete ordered rows as the JSON array a response would carry, streaming each row so the
    /// whole document is never held as one string. The value equals the digest of the serialized array.
    /// </summary>
    internal static string ItemsDigest(IReadOnlyList<ParsimonyViewRow> rows)
    {
        var options = MotifJson.CreateOptions();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sink = new HashingStream(hash);
        using (var writer = new Utf8JsonWriter(sink, new JsonWriterOptions
        {
            Indented = options.WriteIndented,
            Encoder = options.Encoder,
        }))
        {
            writer.WriteStartArray();
            foreach (var row in rows) JsonSerializer.Serialize(writer, row, options);
            writer.WriteEndArray();
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class HashingStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
    }

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
