using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// One drill-down view kept for a stored bundle: the whole ordered result of a view a finding points to, written
/// while the grammar facts still existed and served as it was after they are discarded.
/// </summary>
/// <param name="BundleId">The bundle the view belongs to.</param>
/// <param name="View">The fixed view code.</param>
/// <param name="FilterDigest">The digest of the filters the view was computed for.</param>
/// <param name="Version">The view definition version the rows were computed under.</param>
/// <param name="ItemsDigest">The digest of the complete ordered rows, as a live read would report it.</param>
/// <param name="RowCount">How many rows the view holds.</param>
/// <param name="ResultJson">The rows, status and detail.</param>
public sealed record FrozenParsimonyView(string BundleId, string View, string FilterDigest, int Version,
    string ItemsDigest, int RowCount, string ResultJson)
{
    internal ParsimonyViewResult ToResult()
    {
        var stored = JsonSerializer.Deserialize<FrozenResult>(ResultJson, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("A kept Parsimony view is empty.");
        return new ParsimonyViewResult(stored.Rows, ItemsDigest, null, stored.Status, stored.Detail);
    }

    internal sealed record FrozenResult(IReadOnlyList<ParsimonyViewRow> Rows, ParsimonyMeasureStatus? Status,
        string? Detail);
}

public static partial class ParsimonyViewsQuery
{
    /// <summary>What a person is told when a view needs grammar facts that Motif no longer holds.</summary>
    public const string NotKeptDetail = "This view is not kept: Motif discards a stored bundle's grammar facts once " +
        "its Reports are written, and keeps only the views that a finding points to. Run the measures again to see it.";

    /// <summary>The most one kept view may hold.</summary>
    public const int MaximumFrozenViewBytes = 1024 * 1024;

    /// <summary>The most all the kept views of one bundle may hold, so a Proposal stays far inside its budget.</summary>
    public const int MaximumFrozenBundleBytes = 3 * 1024 * 1024;

    private static readonly TimeSpan FreezeDeadline = TimeSpan.FromMinutes(2);

    // Views that read the evidence file and the stored Reports alone, so they open with no grammar facts.
    private static readonly HashSet<string> EvidenceOnlyViews = new(StringComparer.Ordinal)
    {
        ParsimonyViews.JoinQuality, ParsimonyViews.ParserCases, ParsimonyViews.ParsimonyActiveFindings,
        ParsimonyViews.ParsimonySuppressed, ParsimonyViews.ParsimonySuppressionHistory,
    };

    /// <summary>Whether the view reads grammar facts, which a stored bundle no longer has.</summary>
    public static bool NeedsFacts(string view) => !EvidenceOnlyViews.Contains(view);

    /// <summary>
    /// Computes and keeps every view that a finding of the given Reports points to, while the session still holds
    /// the grammar facts. A view that cannot be computed, or is too large to keep, is left out and later reads say it is
    /// not kept.
    /// </summary>
    /// <param name="session">The session over the facts and evidence of the bundle just evaluated.</param>
    /// <param name="findings">The findings of every Report written for the bundle.</param>
    public static IReadOnlyList<FrozenParsimonyView> Freeze(ParsimonyQuerySession session,
        IEnumerable<ParsimonyFinding> findings) => Freeze(session, findings, null);

    internal static IReadOnlyList<FrozenParsimonyView> Freeze(ParsimonyQuerySession session,
        IEnumerable<ParsimonyFinding> findings,
        Func<ParsimonyQuerySession, ParsimonyNamedViewRequest, ParsimonyViewResult>? compute)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(findings);
        var kept = new List<FrozenParsimonyView>();
        var seen = new HashSet<(string View, string FilterDigest)>();
        long total = 0;
        foreach (var reference in findings.SelectMany(finding => finding.EvidenceRefs))
        {
            if (!StringComparer.Ordinal.Equals(reference.BundleId, session.BundleId) ||
                !NeedsFacts(reference.View) || ParsimonyViewCatalog.Find(reference.View) is null) continue;
            var filters = FiltersOf(reference.Arguments);
            var digest = FilterDigestOf(filters);
            if (!seen.Add((reference.View, digest))) continue;
            session.ThrowIfLeaseLost();
            var view = Freeze(session, new ParsimonyNamedViewRequest(session.BundleId, reference.View, filters, 200),
                compute ?? ((open, request) => Compute(open, request, report: null)));
            if (view is null || total + Encoding.UTF8.GetByteCount(view.ResultJson) > MaximumFrozenBundleBytes) continue;
            total += Encoding.UTF8.GetByteCount(view.ResultJson);
            kept.Add(view);
        }
        return kept;
    }

    private static FrozenParsimonyView? Freeze(ParsimonyQuerySession session, ParsimonyNamedViewRequest request,
        Func<ParsimonyQuerySession, ParsimonyNamedViewRequest, ParsimonyViewResult> compute)
    {
        var definition = ParsimonyViewCatalog.Find(request.View)!;
        using var page = CancellationTokenSource.CreateLinkedTokenSource(session.LeaseCancellationToken);
        page.CancelAfter(FreezeDeadline);
        var registration = session.BeginPage(page.Token);
        try
        {
            ValidateFilters(request.View, request.Filters);
            var computed = compute(session, request);
            RequireFound(request, computed.Rows);
            var json = JsonSerializer.Serialize(new FrozenParsimonyView.FrozenResult(computed.Rows,
                computed.ComputedStatus, computed.ComputedDetail), MotifJson.CreateOptions());
            if (Encoding.UTF8.GetByteCount(json) > MaximumFrozenViewBytes) return null;
            return new FrozenParsimonyView(request.BundleId, request.View, FilterDigestOf(request.Filters),
                definition.Version, ItemsDigest(computed.Rows), computed.Rows.Count, json);
        }
        // Keeping a view is best effort: any failure but the job's own cancellation leaves it not kept.
        catch (Exception exception) when (!session.LeaseCancellationToken.IsCancellationRequested)
        {
            session.ThrowIfLeaseLost();
            Console.Error.WriteLine($"Parsimony view {request.View} was not kept: {exception.GetType().Name}.");
            return null;
        }
        finally
        {
            session.EndPage(registration);
        }
    }

    private static string FilterDigestOf(ParsimonyViewFilters filters) =>
        Digest(JsonSerializer.Serialize(filters, MotifJson.CreateOptions()));

    // The reference arguments are an object of the filters the view takes; a null value is no filter.
    private static ParsimonyViewFilters FiltersOf(JsonElement arguments)
    {
        string? Read(string name) => arguments.ValueKind == JsonValueKind.Object &&
            arguments.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, name,
                StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.String } property
            ? property.Value.GetString() : null;
        return new ParsimonyViewFilters(ObjectGuid: Read("objectGuid"), CategoryGuid: Read("categoryGuid"),
            StatementKind: Read("statementKind"), CaseKey: Read("caseKey"));
    }

    /// <summary>
    /// The digest a kept view is looked up under. The Report and measure a reader names only say which stored Report
    /// to read beside the view; they do not change the facts rows, so they are left out of the key.
    /// </summary>
    public static string FilterDigest(ParsimonyViewFilters filters) =>
        FilterDigestOf(filters with { ReportId = null, MeasureId = null });
}
