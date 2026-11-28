using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Gives a corpus word type a disposition, such as proper name or misspelling, in Motif's store. It is word-wide: one
/// type has one disposition for every occurrence. A write is direct and needs no Proposal because it changes only
/// Motif's own authored record, never the FieldWorks project (ADR 0011).
/// </summary>
public static class WordTypeDispositionCommands
{
    /// <summary>The longest form, in characters.</summary>
    public const int MaxFormLength = 256;

    /// <summary>The longest evidence or reason, in characters.</summary>
    public const int MaxTextLength = 2000;

    /// <summary>The most dispositions on a page.</summary>
    public const int MaxLimit = 200;

    /// <summary>The page size when none is given.</summary>
    public const int DefaultLimit = 50;

    private const int ContradictionsNamed = 10;
    private const string WordWide = "word-wide";

    /// <summary>Gives each form's word type a disposition. Every form is written or none is.</summary>
    public static CommandOutcome<DisposeFormsResponse> Record(DisposeWordFormsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (CleanForms(request.Forms, out var forms) is { } formProblem) return Invalid<DisposeFormsResponse>(formProblem);
        if (!DispositionCategories.TryParse(request.Category?.Trim(), out var category))
            return Invalid<DisposeFormsResponse>(
                $"The category must be one of: {string.Join(", ", DispositionCategories.Names)}.");
        var evidence = request.Evidence?.Trim();
        if (string.IsNullOrEmpty(evidence))
            return Invalid<DisposeFormsResponse>("A disposition needs evidence: say what you saw that makes this the disposition.");
        if (evidence.Length > MaxTextLength)
            return Invalid<DisposeFormsResponse>($"The evidence can be at most {MaxTextLength} characters.");
        var intended = request.Intended?.Trim();
        if (category == DispositionCategory.Misspelling)
        {
            if (string.IsNullOrEmpty(intended))
                return Invalid<DisposeFormsResponse>("A misspelling names the word it was meant to be: give --intended <form>.");
            if (CleanIntended(intended) is { } intendedProblem) return Invalid<DisposeFormsResponse>("The intended form: " + intendedProblem);
            if (forms.Any(form => WordTypeKey.Of(form) == WordTypeKey.Of(intended)))
                return Invalid<DisposeFormsResponse>("A misspelling cannot be its own intended form.");
        }
        else if (intended is { Length: > 0 })
            return Invalid<DisposeFormsResponse>("Only a misspelling names an intended form.");
        else intended = null;
        var by = By(request.RecordedBy);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var outcome = new WordTypeDispositionRepository(database).Record(forms, category, evidence, intended, by, request.Replace);
            if (outcome.Contradictions.Count > 0) return Contradicted(outcome.Contradictions);
            var results = outcome.Results;
            return CommandOutcome<DisposeFormsResponse>.Success(new(results,
                results.Count(r => r.Result == "recorded"), results.Count(r => r.Result == "replaced"),
                results.Count(r => r.Result is "already-recorded" or "evidence-added")));
        });
    }

    /// <summary>
    /// Gives the word types in a file a disposition: one transaction, per-row refusals with their line numbers, and
    /// nothing written when any row is refused unless the refused rows are skipped. A dry run reports the same counts and writes nothing.
    /// </summary>
    public static CommandOutcome<DisposeFromFileResponse> RecordFromFile(DisposeFromFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath)) return Invalid<DisposeFromFileResponse>("Name the file with --from-file <path>.");
        var inputFormat = request.InputFormat?.Trim();
        if (inputFormat is { Length: > 0 } && !DispositionFileReader.Formats.Contains(inputFormat))
            return Invalid<DisposeFromFileResponse>($"The file format must be one of: {string.Join(", ", DispositionFileReader.Formats)}.");
        var (file, problem) = DispositionFileReader.Read(request.FilePath, string.IsNullOrEmpty(inputFormat) ? null : inputFormat);
        if (file is null) return Invalid<DisposeFromFileResponse>(problem!);
        if (file.Rows.Count == 0)
            return Invalid<DisposeFromFileResponse>($"The file holds no rows to record ({file.Skipped} {file.Format} rows were not dispositions).");

        var entries = new List<WordTypeDispositionRepository.Entry>(file.Rows.Count);
        var refusals = new List<DisposeRowRefusal>();
        foreach (var row in file.Rows)
        {
            var (entry, why) = CheckRow(row);
            if (entry is not null) entries.Add(entry);
            else refusals.Add(new(row.Row, row.Form, RefusalCodes.DispositionInvalid, why!));
        }
        var by = By(request.RecordedBy);
        CommandOutcome<DisposeFromFileResponse> Summarise(WordTypeDispositionRepository.BulkOutcome outcome) =>
            CommandOutcome<DisposeFromFileResponse>.Success(Summary(request, file, entries, refusals, outcome));
        if (request.DryRun)
            return ProjectStoreCommand.ReadExisting(request.FwDataPath,
                Summarise(WordTypeDispositionRepository.PlanOnEmptyStore(entries, by, request.Replace)),
                (database, _) => Summarise(new WordTypeDispositionRepository(database)
                    .RecordMany(entries, by, request.Replace, refusals.Count, request.SkipRefused, dryRun: true)));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) => Summarise(
            new WordTypeDispositionRepository(database)
                .RecordMany(entries, by, request.Replace, refusals.Count, request.SkipRefused, dryRun: false)));
    }

    private static DisposeFromFileResponse Summary(DisposeFromFileRequest request, ParsedDispositionFile file,
        List<WordTypeDispositionRepository.Entry> entries, List<DisposeRowRefusal> parseRefusals,
        WordTypeDispositionRepository.BulkOutcome outcome)
    {
        var refusals = new List<DisposeRowRefusal>(parseRefusals);
        var results = new List<DisposeRowResult>();
        int added = 0, replaced = 0, unchanged = 0, extended = 0;
        foreach (var row in outcome.Rows)
        {
            var entry = row.Entry;
            switch (row.Status)
            {
                case WordTypeDispositionRepository.BulkStatus.Contradiction:
                    refusals.Add(new(entry.Row, entry.Form, RefusalCodes.DispositionContradiction,
                        $"{entry.Form} is already {row.Standing!.Category}" +
                        (row.Standing.Intended is null ? "" : $" (intended {row.Standing.Intended})") + "; add --replace to end it."));
                    results.Add(new(entry.Row, entry.Form, entry.Key, entry.Category, entry.Intended, "refused"));
                    continue;
                case WordTypeDispositionRepository.BulkStatus.ConflictsInCall:
                    refusals.Add(new(entry.Row, entry.Form, RefusalCodes.DispositionInvalid,
                        $"{entry.Form} appears again with another category or intended form than an earlier row."));
                    results.Add(new(entry.Row, entry.Form, entry.Key, entry.Category, entry.Intended, "refused"));
                    continue;
                case WordTypeDispositionRepository.BulkStatus.Recorded: added++; break;
                case WordTypeDispositionRepository.BulkStatus.Replaced: replaced++; break;
                case WordTypeDispositionRepository.BulkStatus.EvidenceAdded: unchanged++; extended++; break;
                default: unchanged++; break;
            }
            results.Add(new(entry.Row, entry.Form, entry.Key, entry.Category, entry.Intended, row.Status));
        }
        refusals.Sort((a, b) => a.Row.CompareTo(b.Row));
        if (request.Detail)
        {
            foreach (var parsed in parseRefusals)
                results.Add(new(parsed.Row, parsed.Form, WordTypeKey.Of(parsed.Form), "", null, "refused"));
            results.Sort((a, b) => a.Row.CompareTo(b.Row));
        }
        var truncated = file.Rows.Count(row => row.Truncated);
        return new(file.Format, file.Rows.Count, added, replaced, unchanged, extended, refusals.Count, file.Skipped,
            truncated, request.DryRun, outcome.Written, refusals, request.Detail ? results : []);
    }

    private static (WordTypeDispositionRepository.Entry? Entry, string? Why) CheckRow(RawDispositionRow row)
    {
        if (row.Problem is not null) return (null, row.Problem);
        var form = row.Form.Trim();
        if (CleanForm(form) is { } formProblem) return (null, formProblem);
        if (!DispositionCategories.TryParse(row.Category?.Trim(), out var category))
            return (null, $"The category '{row.Category?.Trim()}' must be one of: {string.Join(", ", DispositionCategories.Names)}.");
        var evidence = row.Evidence?.Trim();
        if (string.IsNullOrEmpty(evidence)) return (null, "A disposition needs evidence.");
        if (evidence.Length > MaxTextLength) return (null, $"The evidence can be at most {MaxTextLength} characters.");
        var key = WordTypeKey.Of(form);
        var intended = row.Intended?.Trim();
        if (category == DispositionCategory.Misspelling)
        {
            if (string.IsNullOrEmpty(intended)) return (null, "A misspelling needs the intended form.");
            if (CleanIntended(intended) is { } intendedProblem) return (null, "The intended form: " + intendedProblem);
            if (WordTypeKey.Of(intended) == key) return (null, "A misspelling cannot be its own intended form.");
        }
        else if (intended is { Length: > 0 }) return (null, "Only a misspelling names an intended form.");
        else intended = null;
        return (new(row.Row, form, key, DispositionCategories.Wire(category), intended, evidence), null);
    }

    /// <summary>Clears each form's standing disposition. The cleared rows stay in the type's history.</summary>
    public static CommandOutcome<UndisposeFormsResponse> Clear(UndisposeWordFormsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (CleanForms(request.Forms, out var forms) is { } formProblem) return Invalid<UndisposeFormsResponse>(formProblem);
        var reason = request.Reason?.Trim();
        if (reason is { Length: > MaxTextLength })
            return Invalid<UndisposeFormsResponse>($"The reason can be at most {MaxTextLength} characters.");
        var by = By(request.RecordedBy);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var results = new WordTypeDispositionRepository(database).Clear(forms, by, string.IsNullOrEmpty(reason) ? null : reason);
            return CommandOutcome<UndisposeFormsResponse>.Success(new(results,
                results.Count(r => r.Result == "cleared"), results.Count(r => r.Result == "not-recorded")));
        });
    }

    /// <summary>One word type's standing disposition and its history; a type with none answers with no standing disposition.</summary>
    public static CommandOutcome<ShowDispositionResponse> Show(ShowDispositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var form = request.Form?.Trim() ?? "";
        if (CleanForm(form) is { } problem) return Invalid<ShowDispositionResponse>(problem);
        var empty = new ShowDispositionResponse(form, WordTypeKey.Of(form), null, [], WordWide);
        return ProjectStoreCommand.ReadExisting(request.FwDataPath, CommandOutcome<ShowDispositionResponse>.Success(empty),
            (database, _) =>
            {
                var repository = new WordTypeDispositionRepository(database);
                return CommandOutcome<ShowDispositionResponse>.Success(
                    empty with { Current = repository.Get(form), History = repository.History(form) });
            });
    }

    /// <summary>A page of standing dispositions in word type key order, with how many types stand in each category.</summary>
    public static CommandOutcome<ListDispositionsResponse> List(ListDispositionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DispositionCategory? category = null;
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            if (!DispositionCategories.TryParse(request.Category.Trim(), out var parsed))
                return Invalid<ListDispositionsResponse>(
                    $"The category must be one of: {string.Join(", ", DispositionCategories.Names)}; leave it out for all.");
            category = parsed;
        }
        if (request.Limit is < 1 or > MaxLimit)
            return Invalid<ListDispositionsResponse>($"The limit must be from 1 to {MaxLimit}.");
        var offset = request.CursorOffset();
        if (offset < 0)
            return CommandOutcome<ListDispositionsResponse>.Refused(new Refusal(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument,
                "The cursor must be the nextCursor a previous page returned."));
        var none = DispositionCategories.Names.Select(name => new DispositionCategoryCount(name, 0)).ToArray();
        var absent = CommandOutcome<ListDispositionsResponse>.Success(new([], 0, request.Limit,
            category is { } c ? DispositionCategories.Wire(c) : null, none));
        return ProjectStoreCommand.ReadExisting(request.FwDataPath, absent, (database, _) =>
        {
            var repository = new WordTypeDispositionRepository(database);
            var (page, total) = repository.List(category, offset, request.Limit);
            return CommandOutcome<ListDispositionsResponse>.Success(new(page, total, request.Limit,
                category is { } chosen ? DispositionCategories.Wire(chosen) : null, repository.CountsByCategory(),
                ResultPaging.NextCursor(offset, page.Count, total)));
        });
    }

    private static string By(string? given) =>
        string.IsNullOrWhiteSpace(given) ? Environment.UserName : given.Trim();

    private static string? CleanForms(IReadOnlyList<string>? given, out List<string> forms)
    {
        forms = [];
        if (given is null || given.Count == 0) return "Name at least one word form.";
        foreach (var raw in given)
        {
            var form = raw?.Trim() ?? "";
            if (CleanForm(form) is { } problem) return problem;
            forms.Add(form);
        }
        return null;
    }

    private static string? CleanForm(string form) =>
        form.Length == 0 ? "A word form cannot be empty."
        : form.Length > MaxFormLength ? $"A word form can be at most {MaxFormLength} characters: '{form[..20]}...'."
        : form.Any(char.IsWhiteSpace) ? $"A word form is one word with no spaces: '{form}'."
        : null;

    // A run-together misspelling's intended form is several words; ';' marks several candidates and is refused.
    private static string? CleanIntended(string intended) =>
        intended.Length == 0 ? "An intended form cannot be empty."
        : intended.Length > MaxFormLength ? $"An intended form can be at most {MaxFormLength} characters: '{intended[..20]}...'."
        : intended.Contains(';') ? $"An intended form is one reading, not a list ('{intended}'): pick one candidate."
        : intended.Any(char.IsControl) ? $"An intended form has no tabs or line breaks: '{intended}'."
        : null;

    private static CommandOutcome<DisposeFormsResponse> Contradicted(
        IReadOnlyList<WordTypeDispositionRepository.Contradiction> contradictions)
    {
        var named = string.Join("; ", contradictions.Take(ContradictionsNamed).Select(c =>
            $"{c.Form} is already {c.Standing.Category}" + (c.Standing.Intended is null ? "" : $" (intended {c.Standing.Intended})")));
        var more = contradictions.Count > ContradictionsNamed ? $"; and {contradictions.Count - ContradictionsNamed} more" : "";
        return CommandOutcome<DisposeFormsResponse>.Refused(new Refusal(RefusalCodes.DispositionContradiction, FailureReason.Refused,
            $"{contradictions.Count} word type(s) already stand with another disposition, so nothing was written: {named}{more}. " +
            "Add --replace to end those and record this one, or run undispose first.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["contradictions"] = contradictions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
    }

    private static CommandOutcome<T> Invalid<T>(string message) where T : class =>
        CommandOutcome<T>.Refused(new Refusal(RefusalCodes.DispositionInvalid, FailureReason.InvalidArgument, message));
}
