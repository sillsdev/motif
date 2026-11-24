using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
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

    /// <summary>Reads the delta and writes the two manifests into <paramref name="manifestDirectory"/>.</summary>
    /// <remarks>A manifest must enumerate the whole delta, so any unmeasured form is a refusal.</remarks>
    public static CommandOutcome<ReadingDelta> ReadAndWrite(ReadTrialDeltaRequest request, string manifestDirectory) =>
        Run(request, manifestDirectory);

    private static CommandOutcome<ReadingDelta> Run(ReadTrialDeltaRequest request, string? manifestDirectory)
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
                return Build(database, receipt, request, classes, manifestDirectory);
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
        ReadTrialDeltaRequest request, IReadOnlyList<string> classes, string? manifestDirectory)
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
            try { manifests = ManifestWriter.Write(manifestDirectory, forms); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
                return CommandOutcome<ReadingDelta>.Refused(new Refusal(RefusalCodes.TrialDeltaManifestUnwritable,
                    FailureReason.InvalidArgument, $"The manifests could not be written to '{manifestDirectory}': {exception.Message}"));
            }
        }
        return CommandOutcome<ReadingDelta>.Success(new ReadingDelta(ReadingDelta.Version, receipt.JobId,
            receipt.ProposalRevision, counts, shown, manifests, [.. limitations]));
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
