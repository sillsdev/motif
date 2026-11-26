using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Reads a Trial's retained evidence export and states which words lost, gained, changed or doubled their Readings.
/// The export already holds both sides' Readings for every measured form, so this measures nothing and changes nothing.
/// </summary>
public static class TrialReadingDelta
{
    /// <summary>The delta of the requested Trial, with a page of rows for each requested class.</summary>
    public static CommandOutcome<ReadingDelta> Read(ReadTrialDeltaRequest request) => Run(request, null);

    /// <summary>
    /// Reads the delta and holds it to <paramref name="expectations"/>, as the lane gate does: the delta's
    /// <see cref="ReadingDelta.Expectations"/> says green, mismatch, doubled-over-cap or unmeasured.
    /// </summary>
    /// <remarks>
    /// With <paramref name="receiptPath"/>, a green state also writes the receipt that <c>apply --receipt</c> honours;
    /// any other state writes none, and a file left by an earlier run is removed so it cannot be mistaken for this one.
    /// </remarks>
    public static CommandOutcome<ReadingDelta> Check(ReadTrialDeltaRequest request, TrialExpectations expectations,
        ManifestOptions? options = null, string? receiptPath = null)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        if (TrialExpectationCheck.Invalid(expectations) is { } invalid) return Invalid(invalid);
        if (receiptPath is not null && !expectations.Any)
            return Invalid("A receipt records what the Trial was held to: give --expect-retired, --expect-added, --max-doubled or --max-doubled-frac.");
        return Run(request, null, options ?? new ManifestOptions(), expectations.Any ? expectations : null, receiptPath);
    }

    /// <summary>Reads the delta and writes the two manifests into <paramref name="manifestDirectory"/>.</summary>
    /// <remarks>A manifest must enumerate the whole delta, so any unmeasured form is a refusal.</remarks>
    public static CommandOutcome<ReadingDelta> ReadAndWrite(ReadTrialDeltaRequest request, string manifestDirectory,
        ManifestOptions? options = null) => Run(request, manifestDirectory, options ?? new ManifestOptions());

    private static CommandOutcome<ReadingDelta> Run(ReadTrialDeltaRequest request, string? manifestDirectory,
        ManifestOptions? options = null, TrialExpectations? expectations = null, string? receiptPath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var classes = request.Classes is { Count: > 0 } ? request.Classes : ReadingDeltaClasses.Default;
        if ((request.JobId is null) == (request.ProposalId is null))
            return Invalid("Name one Trial job or one Proposal.");
        if (classes.Any(name => !ReadingDeltaClasses.All.Contains(name)))
            return Invalid("A class is one of " + string.Join(", ", ReadingDeltaClasses.All) + ".");
        if (request.Offset < 0 || request.Limit is < 1 or > ResultPaging.MaxLimit)
            return Invalid($"Use an offset of zero or more and a limit from 1 to {ResultPaging.MaxLimit}.");
        if (request.ProposalId is not null && !CanonicalId.TryParse(request.ProposalId, out _))
            return Invalid("Name the Proposal by its Proposal ID.");

        var absent = Unavailable("The project has no Motif store, so no Trial evidence to read.");
        return ProjectStoreCommand.ReadExisting(request.ProjectPath, absent, (database, project) =>
        {
            try
            {
                var receipt = Locate(database, project, request);
                if (receipt is null) return Unavailable("No retained evidence export matches this Trial.");
                return Build(database, receipt, request, classes, manifestDirectory, options ?? new ManifestOptions(), expectations, receiptPath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
            {
                return Unavailable("The retained Trial evidence export could not be read.");
            }
        });
    }

    private static TrialExportReceipt? Locate(MotifDatabase database, SIL.Motif.Contract.Projects.ProjectLocator project,
        ReadTrialDeltaRequest request)
    {
        if (request.JobId is not null) return TrialExportArtifact.Read(database, request.JobId);
        foreach (var job in Trials.NewestFirst(Trials.JobsFor(database, project, CanonicalId.Parse(request.ProposalId!))))
            if (job.Status == JobStatus.Completed && TrialExportArtifact.Read(database, job.JobId) is { } receipt)
                return receipt;
        return null;
    }

    private static CommandOutcome<ReadingDelta> Build(MotifDatabase database, TrialExportReceipt receipt,
        ReadTrialDeltaRequest request, IReadOnlyList<string> classes, string? manifestDirectory, ManifestOptions options,
        TrialExpectations? expectations, string? receiptPath)
    {
        var inputs = new List<ReadingDeltaInput>();
        var unmeasured = new HashSet<string>(StringComparer.Ordinal);
        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        for (var number = 1; number <= receipt.Pages; number++)
        {
            var words = TrialExportArtifact.Read(database, receipt.JobId, page: number)?.Page?.Words
                ?? throw new InvalidDataException("A retained Trial export page is missing.");
            foreach (var word in words) Collect(word, inputs, unmeasured, limitations);
        }
        var measuredWords = inputs.Select(item => item.Word).ToHashSet(StringComparer.Ordinal);
        unmeasured.ExceptWith(measuredWords);

        var forms = ReadingDeltaBuilder.Compute(inputs);
        var counts = new ReadingDeltaCounts(forms.Count, forms.Count(form => form.BeforeCount > 0),
            Count(forms, ReadingDeltaClasses.Lost), Count(forms, ReadingDeltaClasses.Gained),
            Count(forms, ReadingDeltaClasses.Changed), Count(forms, ReadingDeltaClasses.Doubled),
            Count(forms, ReadingDeltaClasses.Rejected), forms.Sum(form => form.Retired.Sum(item => item.Count)),
            forms.Sum(form => form.Added.Sum(item => item.Count)), unmeasured.Count);
        var shown = classes.Distinct(StringComparer.Ordinal).Select(name =>
        {
            var members = forms.Where(form => form.Classes.Contains(name)).ToArray();
            return new ReadingDeltaClass(name, members.Length, request.Offset,
                [.. members.Skip(request.Offset).Take(request.Limit).Select(Row)]);
        }).ToArray();

        IReadOnlyList<ReadingManifestSummary> manifests = [];
        if (manifestDirectory is not null)
        {
            if (unmeasured.Count > 0)
                return CommandOutcome<ReadingDelta>.Refused(new Refusal(RefusalCodes.TrialDeltaIncomplete, FailureReason.Refused,
                    $"{unmeasured.Count} forms were not measured on both sides or have no complete Reading identity " +
                    "(" + string.Join(", ", limitations) + "), so a manifest would not enumerate the whole delta."));
            var (aliases, aliasRefusal) = Aliases(database, receipt, request, options);
            if (aliasRefusal is not null) return aliasRefusal;
            try { manifests = ManifestWriter.Write(manifestDirectory, forms, aliases, options.KeepCase); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
                return CommandOutcome<ReadingDelta>.Refused(new Refusal(RefusalCodes.TrialDeltaManifestUnwritable,
                    FailureReason.InvalidArgument, $"The manifests could not be written to '{manifestDirectory}': {exception.Message}"));
            }
        }
        var (siblings, siblingsProblem) = SiblingsOf(database, receipt, forms);
        if (siblingsProblem is not null) limitations.Add(siblings is null ? "siblings-unreadable" : "siblings-unmeasured");
        ReadingExpectationResult? held = null;
        if (expectations is not null)
        {
            Dictionary<string, Dictionary<string, long>>? retired = null, added = null;
            if (expectations.RetiredRows is not null)
            {
                var parsed = TrialExpectationCheck.Parse(ReadingExpectationKinds.Retired, expectations.RetiredRows, options.KeepCase);
                if (parsed.Error is not null) return Invalid(parsed.Error);
                retired = parsed.Rows;
            }
            if (expectations.AddedRows is not null)
            {
                var parsed = TrialExpectationCheck.Parse(ReadingExpectationKinds.Added, expectations.AddedRows, options.KeepCase);
                if (parsed.Error is not null) return Invalid(parsed.Error);
                added = parsed.Rows;
            }
            var (expectedAliases, expectedRefusal) = Aliases(database, receipt, request, options);
            if (expectedRefusal is not null) return expectedRefusal;
            held = TrialExpectationCheck.Evaluate(forms, counts, expectations, retired, added, expectedAliases, options.KeepCase,
                NotCurrent(database, receipt, request, counts) ?? siblingsProblem);
        }
        TrialReceiptSummary? written = null;
        if (receiptPath is not null && expectations is not null)
        {
            if (File.Exists(receiptPath)) File.Delete(receiptPath);
            if (held?.State == ReadingExpectationStates.Green)
            {
                var issued = IssueReceipt(database, receipt, expectations, counts);
                if (issued.Refusal is not null) return CommandOutcome<ReadingDelta>.Refused(issued.Refusal);
                if (TrialReceiptFile.Write(receiptPath, issued.Receipt!) is { } unwritable) return CommandOutcome<ReadingDelta>.Refused(unwritable);
                written = new TrialReceiptSummary(Path.GetFullPath(receiptPath), issued.Receipt!.ReceiptDigest, issued.Receipt.GateCodeDigest);
            }
        }
        return CommandOutcome<ReadingDelta>.Success(new ReadingDelta(ReadingDelta.Version, receipt.JobId,
            receipt.ProposalRevision, counts, shown, manifests, [.. limitations], held, written, siblings));
    }

    // The receipt of a green Trial, bound to the revision and Baseline just proved current.
    private static (TrialReceipt? Receipt, Refusal? Refusal) IssueReceipt(MotifDatabase database, TrialExportReceipt receipt,
        TrialExpectations expectations, ReadingDeltaCounts counts)
    {
        try
        {
            var job = new JobRepository(database).Get(receipt.JobId) ?? throw new InvalidDataException("The Trial job is gone.");
            var proposalId = Trials.ProposalOf(job);
            var record = new SIL.Motif.Worker.Store.ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
            var intent = Trials.ReadCurrentIntentDigest(record) ?? throw new InvalidDataException("The Proposal's revision cannot be read.");
            var baseline = TrialExportArtifact.ReadBaselineSemantic(database, receipt.JobId)
                ?? throw new InvalidDataException("The Trial's Baseline cannot be read.");
            return (TrialReceiptFile.Build(proposalId, intent, baseline, receipt.JobId, expectations, counts), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or
            ArgumentException or ContractParseException or InvalidOperationException)
        {
            return (null, new Refusal(RefusalCodes.TrialDeltaIncomplete, FailureReason.Refused,
                "The receipt could not be bound to the Trial: " + exception.Message));
        }
    }

    // The sampled siblings and, when they cannot be held to expectations, why.
    private static (TrialSiblings? Siblings, string? Problem) SiblingsOf(MotifDatabase database, TrialExportReceipt receipt,
        IReadOnlyList<ReadingDeltaForm> forms)
    {
        try
        {
            if (new JobRepository(database).Get(receipt.JobId) is not { } job ||
                TrialJobInput.Parse(job.InputJson).Siblings is not { } input)
                return (null, null);
            var sampled = JobWordLists.Read(database, input.Words)
                .Select(word => word.Normalize(NormalizationForm.FormC)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var mine = forms.Where(form => sampled.Contains(form.Word)).ToArray();
            string[] Sample(string name) => [.. mine.Where(form => form.Classes.Contains(name))
                .Select(form => form.Word).Order(StringComparer.Ordinal).Take(20)];
            var block = new TrialSiblings(input.Pattern, input.Seed, input.Max, input.TouchedForms, input.CorpusTypesSha256,
                input.Candidates, input.ParsedCandidates, sampled.Count, mine.Length,
                Count(mine, ReadingDeltaClasses.Lost), Sample(ReadingDeltaClasses.Lost),
                Count(mine, ReadingDeltaClasses.Doubled), Sample(ReadingDeltaClasses.Doubled), input.Warning)
            {
                ReferrerForms = input.ReferrerForms,
                Referrers = input.Referrers,
                SkippedReferrerForms = input.SkippedReferrerForms,
            };
            return (block, mine.Length < sampled.Count
                ? $"{sampled.Count - mine.Length} of {sampled.Count} sampled siblings were not measured on both sides, so the delta cannot be held to the expectations."
                : null);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or
            UnauthorizedAccessException)
        {
            return (null, "The Trial sampled siblings but their record could not be read (" + exception.Message +
                "), so the delta cannot be held to the expectations.");
        }
    }

    // Why this Trial cannot speak for the Proposal and Baseline as they stand now, or null when it can.
    private static string? NotCurrent(MotifDatabase database, TrialExportReceipt receipt, ReadTrialDeltaRequest request,
        ReadingDeltaCounts counts)
    {
        if (counts.Measured == 0 && counts.Unmeasured == 0) return "The Trial measured no form, so it holds nothing to the expectations.";
        try
        {
            var job = new JobRepository(database).Get(receipt.JobId);
            if (job is null) return "The Trial job is gone, so its Proposal revision and Baseline cannot be compared with the current ones.";
            var proposalId = Trials.ProposalOf(job);
            if (request.ProposalId is not null && proposalId != request.ProposalId)
                return $"The Trial belongs to Proposal {proposalId}, not {request.ProposalId}.";
            var used = IntentDigest.Compute(ProposalJsonParser.Parse(TrialJobInput.Parse(job.InputJson).ProposalJson));
            var record = new SIL.Motif.Worker.Store.ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
            var current = Trials.ReadCurrentIntentDigest(record);
            if (current is null) return "The Proposal's current revision cannot be read, so the Trial cannot be compared with it.";
            if (!string.Equals(used, current, StringComparison.Ordinal))
                return $"The Trial measured Proposal revision {used} but the Proposal is now {current}; run a new Trial.";
            var measured = TrialExportArtifact.ReadBaselineSemantic(database, receipt.JobId);
            var baseline = new SIL.Motif.Worker.Baselines.BaselineRepository(database).GetCurrent(job.ProjectKey)?.Token;
            if (baseline is null || measured is null)
                return "The current Baseline or the Trial's Baseline cannot be read, so the two cannot be compared.";
            if (!string.Equals(measured, baseline.SemanticSnapshotDigest, StringComparison.Ordinal))
                return $"The Trial measured Baseline {measured} but the current Baseline is {baseline.SemanticSnapshotDigest}; run a new Trial.";
            return null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or
            ArgumentException or ContractParseException or InvalidOperationException)
        {
            return "The Trial's Proposal revision or Baseline could not be read: " + exception.Message;
        }
    }

    // The ids the Proposal creates, named created:N as the lane gate names them; null when real ids are asked for.
    private static (IReadOnlyDictionary<string, string>? Aliases, CommandOutcome<ReadingDelta>? Refusal) Aliases(
        MotifDatabase database, TrialExportReceipt receipt, ReadTrialDeltaRequest request, ManifestOptions options)
    {
        if (!options.AliasCreated) return (null, null);
        try
        {
            var proposalId = request.ProposalId ?? Trials.ProposalOf(
                new JobRepository(database).Get(receipt.JobId) ?? throw new InvalidDataException("The Trial job is gone."));
            return (ReadingManifestFormat.CreatedAliases(TrialTouchedWords.LoadProposal(database, proposalId).Operations), null);
        }
        catch (Exception exception)
        {
            return (null, CommandOutcome<ReadingDelta>.Refused(new Refusal(RefusalCodes.TrialDeltaIncomplete, FailureReason.Refused,
                "The Proposal's operations could not be read to alias the ids it creates; " +
                "pass --alias-created false to write real ids. " + exception.Message)));
        }
    }

    private static long Count(IEnumerable<ReadingDeltaForm> forms, string name) => forms.Count(form => form.Classes.Contains(name));

    private static ReadingDeltaWord Row(ReadingDeltaForm form) => new(form.Word, form.CaseKey, form.BeforeCount, form.AfterCount,
        [.. form.Retired.Select(Reading)], [.. form.Added.Select(Reading)]);

    private static ReadingDeltaReading Reading((ReadingSignature Signature, long Count) item) =>
        new(item.Signature.Id, item.Count, ReadingManifestFormat.ToElement(item.Signature));

    private static void Collect(JsonElement word, List<ReadingDeltaInput> inputs, HashSet<string> unmeasured,
        SortedSet<string> limitations)
    {
        var caseKey = word.GetProperty("caseKey").GetString()!;
        var negative = word.TryGetProperty("memberships", out var memberships) && memberships.EnumerateArray()
            .Any(item => item.TryGetProperty("role", out var role) && role.GetString() == "negative");
        foreach (var form in word.GetProperty("expandedForms").EnumerateArray())
        {
            var text = form.GetProperty("form").GetString()!.Normalize(NormalizationForm.FormC);
            var before = form.GetProperty("before");
            var after = form.GetProperty("after");
            if (!Finished(before) || !Finished(after)) { unmeasured.Add(text); limitations.Add("form-unfinished"); continue; }
            if (Readings(before) is not { } old || Readings(after) is not { } current)
            {
                unmeasured.Add(text);
                limitations.Add("reading-identity-unavailable");
                continue;
            }
            inputs.Add(new ReadingDeltaInput(text, caseKey, negative, old, current));
        }
    }

    private static bool Finished(JsonElement side) =>
        side.GetProperty("dispatched").GetBoolean() && side.GetProperty("complete").GetBoolean();

    // Guessed Readings never count; a Reading without a complete identity makes the form unmeasured.
    private static List<ReadingCopy>? Readings(JsonElement side)
    {
        var result = new List<ReadingCopy>();
        foreach (var reading in side.GetProperty("readings").EnumerateArray())
        {
            var acceptance = reading.GetProperty("acceptance").GetString();
            if (acceptance == "guessed") continue;
            if (acceptance != "lexicon-backed") return null;
            var signature = JsonSerializer.Deserialize<ReadingSignature>(reading.GetProperty("signature").GetRawText());
            if (signature is null || !ReadingManifestFormat.IsComplete(signature)) return null;
            result.Add(new ReadingCopy(signature.Id, signature));
        }
        return result;
    }

    private static CommandOutcome<ReadingDelta> Invalid(string message) => CommandOutcome<ReadingDelta>.Refused(
        new Refusal(RefusalCodes.TrialDeltaRequestInvalid, FailureReason.InvalidArgument, message));

    private static CommandOutcome<ReadingDelta> Unavailable(string message) => CommandOutcome<ReadingDelta>.Refused(
        new Refusal(RefusalCodes.TrialExportUnavailable, FailureReason.Refused, message));
}

/// <summary>How the manifests spell words and ids.</summary>
/// <param name="AliasCreated">Rename ids the Proposal creates to <c>created:N</c>, as the lane gate does.</param>
/// <param name="KeepCase">Keep each word as the Trial measured it; otherwise lower-case it, as the gate keys words.</param>
public sealed record ManifestOptions(bool AliasCreated = true, bool KeepCase = false);
