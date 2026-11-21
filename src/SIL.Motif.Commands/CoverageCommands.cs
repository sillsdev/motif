using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Corpus.Ledger;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>What the project's Motif store adds to a ledger: saved per-type checks and standing word type dispositions.</summary>
internal sealed record StoreInputs(
    IReadOnlyDictionary<string, TypeCheck>? Checks, IReadOnlyDictionary<string, StoreDisposition>? Dispositions)
{
    public static StoreInputs None { get; } = new(null, null);
}

/// <summary>
/// Word-type coverage over a lane ledger, its sidecar and a pinned frequency list: where every type stands and whether
/// the lane is Done. It reads the files and the project's Motif store and writes nothing. It is not the
/// <c>coverage</c> report kind, which measures the share of a scope's words the parser analysed.
/// </summary>
public static class CoverageCommands
{
    /// <summary>The most recurring Open types one response ranks.</summary>
    public const int MaxLimit = 200;

    /// <summary>The most mechanisms <c>open.byMechanism</c> lists; the rest are summed into <c>open.otherMechanisms</c>.</summary>
    public const int MechanismCap = 20;

    /// <summary>The ranked-list size when none is given.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The frequency list formats a request may name.</summary>
    public static readonly IReadOnlyList<string> FrequencyFormats = ["tsv", "tsv-header", "json"];

    /// <summary>Measures the ledger against the frequency list, with the saved checks and dispositions of the project's store when it has them.</summary>
    public static CommandOutcome<CoverageResponse> Coverage(CoverageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Measure(request, sidecar => ReadStore(request, sidecar));
    }

    internal static CommandOutcome<CoverageResponse> Measure(CoverageRequest request,
        Func<LedgerSidecar, CommandOutcome<StoreInputs>> store)
    {
        if (Problem(request) is { } problem) return Refuse(RefusalCodes.CoverageInvalidRequest, FailureReason.InvalidArgument, problem);
        try
        {
            if (Unreachable(request) is { } missing) return missing;
            var ledgerBytes = Read(request.LedgerPath, "ledger");
            var sidecarBytes = File.Exists(request.SidecarPath) ? Read(request.SidecarPath, "sidecar")
                : throw new LedgerRefusalException(LedgerRefusalCode.SidecarMissing, $"The sidecar '{request.SidecarPath}' does not exist.");
            var ledger = LedgerTsvReader.Read(ledgerBytes);
            var sidecar = LedgerSidecarReader.Read(sidecarBytes);
            LedgerSidecarReader.Validate(sidecar, ledger, LedgerSidecarReader.DeclaredString(sidecarBytes, "ledgerSha256"));
            var frequencyBytes = Read(request.FrequencyPath, "frequency list");
            var format = request.FrequencyFormat?.Trim().ToLowerInvariant() is { Length: > 0 } named ? named
                : request.FrequencyPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "tsv";
            var frequency = format == "json" ? FrequencyListReader.ReadWordCountJson(frequencyBytes, sidecar)
                : FrequencyListReader.ReadTsv(frequencyBytes, sidecar, hasHeader: format == "tsv-header");
            var stored = store(sidecar);
            if (!stored.Succeeded) return CommandOutcome<CoverageResponse>.Refused(stored.Refusal!);
            var report = LedgerCoverage.Build(new(ledger, sidecar, frequency, stored.Value!.Checks,
                LedgerSidecarReader.DeclaredString(sidecarBytes, "frequencySha256"),
                Existing(request.AuditDirectory), Existing(request.ProtectedSetPath),
                stored.Value.Dispositions));
            return CommandOutcome<CoverageResponse>.Success(Map(report, request));
        }
        catch (LedgerRefusalException refusal)
        {
            var (stage, path) = Stage(refusal.Code, request);
            var facts = refusal.Row is { } row
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["row"] = row.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : null;
            return CommandOutcome<CoverageResponse>.Refused(new Refusal(CodeFor(refusal.Code), FailureReason.Refused,
                $"The {stage}{path}: {refusal.Message}{(refusal.Row is { } at ? $" (row {at})" : "")} Next: {Hint(refusal.Code)}", facts));
        }
        catch (FileUnreadableException unreadable)
        {
            return Refuse(RefusalCodes.CoverageFileUnreadable, FailureReason.NotFound, unreadable.Message);
        }
    }

    /// <summary>The registered refusal code a ledger refusal surfaces as.</summary>
    internal static string CodeFor(LedgerRefusalCode code) => code switch
    {
        LedgerRefusalCode.NotUtf8 => RefusalCodes.CoverageNotUtf8,
        LedgerRefusalCode.EmptyFile => RefusalCodes.CoverageEmptyFile,
        LedgerRefusalCode.BadHeader => RefusalCodes.CoverageBadHeader,
        LedgerRefusalCode.WrongFieldCount => RefusalCodes.CoverageWrongFieldCount,
        LedgerRefusalCode.EmptyWord => RefusalCodes.CoverageEmptyWord,
        LedgerRefusalCode.EmptyGrammarRevision => RefusalCodes.CoverageEmptyGrammarRevision,
        LedgerRefusalCode.BadTokens => RefusalCodes.CoverageBadTokens,
        LedgerRefusalCode.BadState => RefusalCodes.CoverageBadState,
        LedgerRefusalCode.BadExpectedReadings => RefusalCodes.CoverageBadExpectedReadings,
        LedgerRefusalCode.RowCountMismatch => RefusalCodes.CoverageRowCountMismatch,
        LedgerRefusalCode.TokenSumMismatch => RefusalCodes.CoverageTokenSumMismatch,
        LedgerRefusalCode.DigestMismatch => RefusalCodes.CoverageDigestMismatch,
        LedgerRefusalCode.SidecarMissing => RefusalCodes.CoverageSidecarMissing,
        LedgerRefusalCode.SidecarInvalidJson => RefusalCodes.CoverageSidecarInvalidJson,
        LedgerRefusalCode.SidecarDuplicateKey => RefusalCodes.CoverageSidecarDuplicateKey,
        LedgerRefusalCode.SidecarMissingKey => RefusalCodes.CoverageSidecarMissingKey,
        LedgerRefusalCode.SidecarInvalidValue => RefusalCodes.CoverageSidecarInvalidValue,
        LedgerRefusalCode.FrequencyMalformed => RefusalCodes.CoverageFrequencyMalformed,
        LedgerRefusalCode.CheckMalformed => RefusalCodes.CoverageCheckMalformed,
        LedgerRefusalCode.CheckPinMismatch => RefusalCodes.CoverageCheckPinMismatch,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A ledger refusal code has no coverage refusal code."),
    };

    private static (string Stage, string Path) Stage(LedgerRefusalCode code, CoverageRequest request) => code switch
    {
        LedgerRefusalCode.SidecarMissing or LedgerRefusalCode.SidecarInvalidJson or LedgerRefusalCode.SidecarDuplicateKey
            or LedgerRefusalCode.SidecarMissingKey or LedgerRefusalCode.SidecarInvalidValue or LedgerRefusalCode.RowCountMismatch
            or LedgerRefusalCode.TokenSumMismatch or LedgerRefusalCode.DigestMismatch => ("sidecar", $" '{request.SidecarPath}'"),
        LedgerRefusalCode.FrequencyMalformed => ("frequency list", $" '{request.FrequencyPath}'"),
        LedgerRefusalCode.CheckMalformed or LedgerRefusalCode.CheckPinMismatch => ("saved check", " in the project's Motif store"),
        _ => ("ledger", $" '{request.LedgerPath}'"),
    };

    private static string Hint(LedgerRefusalCode code) => code switch
    {
        LedgerRefusalCode.RowCountMismatch or LedgerRefusalCode.TokenSumMismatch or LedgerRefusalCode.DigestMismatch =>
            "regenerate the sidecar from this ledger, or pass the sidecar that was written with it.",
        LedgerRefusalCode.SidecarMissing or LedgerRefusalCode.SidecarInvalidJson or LedgerRefusalCode.SidecarDuplicateKey
            or LedgerRefusalCode.SidecarMissingKey or LedgerRefusalCode.SidecarInvalidValue =>
            "check the --sidecar path and its JSON keys, or regenerate the sidecar.",
        LedgerRefusalCode.FrequencyMalformed =>
            "check the frequency list is word<TAB>count (name --frequency-format tsv-header or json if it is not), then pass it again.",
        LedgerRefusalCode.CheckMalformed or LedgerRefusalCode.CheckPinMismatch =>
            "re-run the saved check at the ledger's grammar revision so the store holds a current one.",
        _ => "check the --ledger path and that the file is the eight-column ledger TSV, or regenerate the ledger.",
    };

    private static CommandOutcome<CoverageResponse>? Unreachable(CoverageRequest request)
    {
        if (Existing(request.AuditDirectory) is { } audit && !Directory.Exists(audit))
            return Refuse(RefusalCodes.CoverageFileUnreadable, FailureReason.NotFound,
                $"The audit directory '{audit}' does not exist. Next: correct --audit-dir, or leave it out to leave the Correct check unmeasured.");
        if (Existing(request.ProtectedSetPath) is { } protectedSet && !File.Exists(protectedSet))
            return Refuse(RefusalCodes.CoverageFileUnreadable, FailureReason.NotFound,
                $"The protected set '{protectedSet}' does not exist. Next: correct --protected, or leave it out to leave the Stable check unmeasured.");
        return null;
    }

    private static CommandOutcome<StoreInputs> ReadStore(CoverageRequest request, LedgerSidecar sidecar) =>
        ProjectStoreCommand.ReadExisting(request.FwDataPath, CommandOutcome<StoreInputs>.Success(StoreInputs.None), (database, _) =>
            CommandOutcome<StoreInputs>.Success(new(
                TypeCheckProducer.FromStore(new AssessmentRepository(database), sidecar.GrammarRevision),
                StoreDispositionReader.Read(new WordTypeDispositionRepository(database)))));

    private static string? Problem(CoverageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LedgerPath)) return "Name the ledger with --ledger <tsv>.";
        if (string.IsNullOrWhiteSpace(request.SidecarPath)) return "Name the ledger's sidecar with --sidecar <json>.";
        if (string.IsNullOrWhiteSpace(request.FrequencyPath)) return "Name the frequency list with --frequency <file>.";
        if (request.FrequencyFormat is { Length: > 0 } format && !FrequencyFormats.Contains(format.Trim().ToLowerInvariant()))
            return $"The frequency format must be one of: {string.Join(", ", FrequencyFormats)}.";
        return request.Limit is < 1 or > MaxLimit ? $"The limit must be from 1 to {MaxLimit}." : null;
    }

    private static string? Existing(string? path) => string.IsNullOrWhiteSpace(path) ? null : path;

    private static byte[] Read(string path, string what)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FileUnreadableException($"The {what} '{path}' cannot be read: {exception.Message}");
        }
    }

    private static CommandOutcome<CoverageResponse> Refuse(string code, FailureReason reason, string message) =>
        CommandOutcome<CoverageResponse>.Refused(new Refusal(code, reason, message));

    private static CoverageResponse Map(LedgerCoverageReport report, CoverageRequest request)
    {
        static CoverageTally Tally(Tally tally) => new(tally.Types, tally.Tokens);
        static CoverageCurrentStale Split(CurrentStale split) => new(Tally(split.Current), Tally(split.Stale));
        CoverageCurrentStale State(ReportState state) => Split(report.ByState[state]);
        static CoverageDoneTest Test(DoneTest test) => new(test.Verdict.ToString().ToLowerInvariant(), test.Reasons, test.Counts);
        var stamp = report.Stamp;
        var done = report.Done;
        var ranked = report.RankedRecurringOpen;
        var mechanisms = report.OpenByMechanism
            .Select(item => KeyValuePair.Create(item.Key,
                new CoverageMechanismCount(item.Value.Total.Types, item.Value.Current.Types, item.Value.Stale.Types)))
            .OrderByDescending(item => item.Value.Types).ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
        return new(1,
            new(stamp.GrammarRevision, stamp.LedgerSha256, stamp.FrequencySha256, stamp.TokenizerId),
            new(done.Verdict.ToString().ToLowerInvariant(), Test(done.Accounted), Test(done.Correct), Test(done.Stable)),
            new(State(ReportState.Expected), State(ReportState.Unverified), State(ReportState.Disposition), State(ReportState.Open),
                State(ReportState.Lost)),
            new(Tally(new(report.RecurringOpenCurrent.Types + report.RecurringOpenStale.Types,
                    report.RecurringOpenCurrent.Tokens + report.RecurringOpenStale.Tokens)),
                Tally(report.OpenSingletons),
                mechanisms.Take(MechanismCap).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                ranked.Take(request.Limit).Select(item => new CoverageRankedOpen(item.Key, item.Weight, item.Mechanism, item.Stale)).ToArray(),
                ranked.Count, mechanisms.Length,
                mechanisms.Length > MechanismCap
                    ? new CoverageMechanismCount(mechanisms.Skip(MechanismCap).Sum(item => item.Value.Types),
                        mechanisms.Skip(MechanismCap).Sum(item => item.Value.Current), mechanisms.Skip(MechanismCap).Sum(item => item.Value.Stale))
                    : null),
            request.Tokens ? report.OpenSingletonTokenShare : null);
    }

    private sealed class FileUnreadableException(string message) : Exception(message);
}
