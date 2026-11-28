using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Which batch of exact-synthesis rows to generate, and where the full rows go.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="BatchPath">A JSONL file: one row per line with <c>entry</c> and/or <c>sense</c> and an optional <c>cell</c> (see <see cref="GenerateBatchQuery"/>).</param>
/// <param name="OutPath">The artifact file the full rows are written to, one JSON row per line; replaced if present.</param>
/// <param name="At"><c>baseline</c> (the default) for the current published Baseline, or a Proposal id for its latest Trial copy.</param>
/// <param name="TimeoutMs">The wall-clock deadline for each input row, 1 through 240000; omitted sends none and PanGloss's own default applies.</param>
/// <param name="MaxWork">Lowers PanGloss's generation work cap for each input row (shared by the row's cell combinations); omitted sends none.</param>
public sealed record GenerateBatchRequest(
    string ProjectPath, string BatchPath, string OutPath, string? At = null, int? TimeoutMs = null, long? MaxWork = null);

/// <summary>
/// Batch exact synthesis with provenance: reads one row per line, asks one PanGloss <c>generate --requests --out</c> run
/// for every surface of each (entry or sense, cell), and writes one row per root analysis and cell combination to the
/// artifact file with the IDs and rule path that made each surface, or <c>none</c> with its cause. It prints only a
/// bounded summary. It never reasons about the grammar itself: a row with no surface says what PanGloss said.
/// </summary>
/// <remarks>
/// An input row is <c>{"id":"r1","entry":"&lt;entry guid or Motif id, or entry:msa&gt;","sense":"&lt;sense guid or Motif id&gt;","cell":[...]}</c>.
/// At least one of <c>entry</c> and <c>sense</c> is given. <c>cell</c> names affix analyses like <c>forms explain --with</c>:
/// an array of strings is one combination in generation order, an array of arrays one combination each (at most 10,000).
/// Without a <c>cell</c> the root is generated alone. A row that cannot be sent is an <c>error</c> row in the artifact and
/// never stops the batch.
/// </remarks>
public static class GenerateBatchQuery
{
    /// <summary>The most input lines one batch reads.</summary>
    internal const int MaxLines = 100_000;

    /// <summary>The longest input line PanGloss reads; a longer one is an error row.</summary>
    private const int MaxLineBytes = 1024 * 1024;

    private const int MaxCombinations = 10_000;
    private const int MaxTimeoutMs = 240000;
    private const int Quoted = 5;
    private const int MaxQuotedMessage = 300;
    private const int DefaultRowAllowanceMs = 10_000;
    private static readonly TimeSpan BaseWallClock = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxWallClock = TimeSpan.FromHours(24);

    /// <summary>Generates with the located parser.</summary>
    public static CommandOutcome<GenerateBatchResponse> Run(
        GenerateBatchRequest request, CancellationToken cancellationToken = default) =>
        Run(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Generates with an explicitly selected parser.</summary>
    public static CommandOutcome<GenerateBatchResponse> Run(
        GenerateBatchRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request) is { } invalid) return invalid;
        if (parserPath is null)
            return CommandOutcome<GenerateBatchResponse>.Refused(new Refusal(RefusalCodes.WordTraceParserUnavailable,
                FailureReason.Refused, PanGlossExecutable.NotFoundMessage, ParserNotFoundFact.Mark(null, true)));
        using var invoker = new PanGlossInvoker(parserPath);
        return Run(request, invoker, ParserExecutableIdentity.ReadVersion(parserPath), cancellationToken);
    }

    internal static CommandOutcome<GenerateBatchResponse> Run(
        GenerateBatchRequest request, IPanGlossInvoker invoker, string parserVersion, CancellationToken cancellationToken)
    {
        var proposalId = string.IsNullOrWhiteSpace(request.At) || request.At == "baseline" ? null : request.At;
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var parseLease = ProjectParseLease.TryAcquire(database);
            if (parseLease is null) return CommandOutcome<GenerateBatchResponse>.Refused(ProjectParseLease.BusyRefusal());
            var resolved = TracedGrammar.Resolve(database, project, proposalId);
            if (!resolved.Succeeded) return CommandOutcome<GenerateBatchResponse>.Refused(resolved.Refusal!);
            var grammarPath = resolved.Value!.FwDataPath;

            Planned planned;
            try
            {
                using var reader = BaselineReadCache.Open(grammarPath);
                planned = Plan(reader.Cache, File.ReadLines(request.BatchPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Io("Motif could not read the batch file or its saved copy of the project: " + exception.Message);
            }
            if (planned.TooMany) return Invalid($"The batch has more than {MaxLines.ToString(CultureInfo.InvariantCulture)} lines.");

            var grammarHash = BatchInvocationEvidence.DigestFile(grammarPath);
            return Generate(request, invoker, parserVersion, grammarPath, grammarHash, resolved.Value.TracedAgainst, planned,
                cancellationToken);
        });
    }

    /// <summary>
    /// The wall-clock cap for one PanGloss run: ten minutes plus the per-row allowance (<c>--timeout-ms</c>, or ten seconds
    /// when none is given) for every row sent, so a row that uses its whole deadline never starves the rest, at most a day.
    /// </summary>
    internal static TimeSpan WallClockFor(int sentLines, int? timeoutMs)
    {
        var perLine = TimeSpan.FromMilliseconds(timeoutMs ?? DefaultRowAllowanceMs);
        var scaled = BaseWallClock + TimeSpan.FromTicks(perLine.Ticks * Math.Max(sentLines, 0));
        return scaled > MaxWallClock || scaled < BaseWallClock ? MaxWallClock : scaled;
    }

    internal static CommandOutcome<GenerateBatchResponse> Generate(GenerateBatchRequest request, IPanGlossInvoker invoker,
        string parserVersion, string grammarPath, string grammarHash, string tracedAgainst, Planned planned,
        CancellationToken cancellationToken)
    {
        var outPath = Path.GetFullPath(request.OutPath);
        var tally = new Tally();
        string? stopReason = null, stopMessage = null;
        GenerateBatchArtifact.Writer writer;
        try { writer = new GenerateBatchArtifact.Writer(outPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Io($"Motif could not write the rows to '{outPath}': {exception.Message}");
        }
        using (writer)
        {
            void Emit(GenerateBatchRow row)
            {
                writer.Add(row);
                tally.Add(row);
            }

            try
            {
                foreach (var error in planned.Errors) Emit(error);
                if (planned.Sent > 0)
                {
                    var scratch = Directory.CreateTempSubdirectory("motif-generate-batch").FullName;
                    try
                    {
                        var parserRows = Path.Combine(scratch, "rows.jsonl");
                        var lines = planned.Lines.Select(line => line is null ? string.Empty : line.ToLine(request.TimeoutMs, request.MaxWork)).ToArray();
                        var outcome = invoker.RunAsync(new PanGlossRequest.GenerateRequests(grammarPath, lines, parserRows),
                            "generate-batch", cancellationToken, WallClockFor(planned.Sent, request.TimeoutMs)).GetAwaiter().GetResult();
                        var stopped = outcome is not PanGlossOutcome.Completed;
                        if (stopped && StopReasonOf(outcome) is not { } reason) return Failed(outcome, request.ProjectPath, parserVersion);
                        else if (stopped) (stopReason, stopMessage) = (StopReasonOf(outcome), outcome.Message);

                        var seen = new bool[planned.Lines.Count + 1];
                        var read = 0;
                        string? held = null;
                        if (File.Exists(parserRows))
                            foreach (var text in File.ReadLines(parserRows))
                            {
                                if (string.IsNullOrWhiteSpace(text)) continue;
                                if (held is not null) read += Take(held);
                                held = text;
                            }
                        // A killed parser can leave a half-written last line; it is no row, so it is dropped, never guessed at.
                        if (held is not null && (!stopped || IsJson(held))) read += Take(held);
                        if (stopped && read == 0) return Failed(outcome, request.ProjectPath, parserVersion);

                        for (var number = 1; number <= planned.Lines.Count; number++)
                            if (planned.Lines[number - 1] is { } sent && !seen[number])
                                Emit(NoResult(sent, stopped));

                        int Take(string text)
                        {
                            var row = Project(text, planned);
                            if (row.Line is { } at && at >= 1 && at <= planned.Lines.Count) seen[at] = true;
                            Emit(row);
                            return 1;
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(scratch, true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                    }
                }
                writer.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Io($"Motif could not write the rows to '{outPath}' or read PanGloss's rows: {exception.Message}");
            }
        }
        return CommandOutcome<GenerateBatchResponse>.Success(
            tally.ToResponse(outPath, grammarHash, parserVersion, tracedAgainst, stopReason, stopMessage));
    }

    /// <summary>Why a run that stopped early still keeps its rows, or null when nothing is kept.</summary>
    private static string? StopReasonOf(PanGlossOutcome outcome) => outcome switch
    {
        PanGlossOutcome.TimedOut => "timed_out",
        PanGlossOutcome.MemoryLimit => "memory_limit",
        PanGlossOutcome.Refused { FailureCategory: JobFailureCategory.ParserKilled } => "parser_killed",
        PanGlossOutcome.Refused => "parser_failed",
        PanGlossOutcome.Incomplete => "incomplete",
        _ => null,
    };

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GenerateBatchRow NoResult(PlannedLine sent, bool stopped) =>
        new(GenerateBatchSchema.RowId, sent.Line, sent.Id, "error",
            new GenerateBatchCause("no-result", stopped
                ? "PanGloss stopped before it wrote a row for this line."
                : "PanGloss finished but wrote no row for this line."),
            new GenerateBatchTarget(sent.EntryGuid, sent.SenseGuid, sent.MsaGuid), null, null, null, null, null, []);

    private static CommandOutcome<GenerateBatchResponse> Io(string message) =>
        Refuse(RefusalCodes.GenerateBatchIo, FailureReason.Refused, message);

    /// <summary>Counts every row as it streams past and keeps only the first few none and error rows to quote.</summary>
    internal sealed class Tally
    {
        private readonly SortedDictionary<string, int> _status = new(StringComparer.Ordinal) { ["ok"] = 0, ["none"] = 0, ["error"] = 0 };
        private readonly SortedDictionary<string, int> _causes = new(StringComparer.Ordinal);
        private readonly List<GenerateBatchNone> _firstNone = [];
        private readonly List<GenerateBatchNone> _firstErrors = [];
        private int _rows, _surfaces, _incomplete;

        internal void Add(GenerateBatchRow row)
        {
            _rows++;
            _status[row.Status]++;
            _surfaces += row.Surfaces.Count;
            if (row.Complete == false) _incomplete++;
            if (row.Cause is { } cause) _causes[cause.Code] = _causes.GetValueOrDefault(cause.Code) + 1;
            var quoted = row.Status == "none" ? _firstNone : row.Status == "error" ? _firstErrors : null;
            if (quoted is not null && quoted.Count < Quoted)
                quoted.Add(new GenerateBatchNone(row.Line, row.Id, row.Target, row.ComboKey, Shorten(row.Cause!)));
        }

        internal GenerateBatchResponse ToResponse(string outPath, string grammarHash, string parserVersion, string tracedAgainst,
            string? stopReason = null, string? stopMessage = null) =>
            new(GenerateBatchSchema.Id, outPath, _rows, _status, _causes, _surfaces, _incomplete, _firstNone, _firstErrors,
                _incomplete == 0 && _status["error"] == 0 && stopReason is null, grammarHash, parserVersion, tracedAgainst,
                stopReason, stopMessage is null ? null : Truncate(stopMessage));

        private static GenerateBatchCause Shorten(GenerateBatchCause cause) =>
            cause.Message is { } message ? cause with { Message = Truncate(message) } : cause;

        private static string Truncate(string text) =>
            text.Length <= MaxQuotedMessage ? text : text[..MaxQuotedMessage] + "...";
    }

    internal static GenerateBatchResponse Summarize(string outPath, IEnumerable<GenerateBatchRow> rows, string grammarHash,
        string parserVersion, string tracedAgainst)
    {
        var tally = new Tally();
        foreach (var row in rows) tally.Add(row);
        return tally.ToResponse(outPath, grammarHash, parserVersion, tracedAgainst);
    }

    /// <summary>One input line that can be sent: its target, its row id and its resolved cell.</summary>
    internal sealed record PlannedLine(
        int Line, string Id, string EntryGuid, string? SenseGuid, string? MsaGuid, IReadOnlyList<IReadOnlyList<ResolvedMorpheme>>? Cell)
    {
        /// <summary>The <c>generate --requests</c> row for this line, with a rule trace on every row.</summary>
        internal string ToLine(int? timeoutMs, long? maxWork)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("id", Id);
                writer.WriteString("op", "generate");
                writer.WriteStartObject("target");
                writer.WriteString("entryGuid", EntryGuid);
                if (SenseGuid is not null) writer.WriteString("senseGuid", SenseGuid);
                if (MsaGuid is not null) writer.WriteString("msaGuid", MsaGuid);
                writer.WriteEndObject();
                if (Cell is not null)
                {
                    writer.WriteStartObject("cell");
                    writer.WriteStartArray("msas");
                    foreach (var combination in Cell)
                    {
                        writer.WriteStartArray();
                        foreach (var morpheme in combination)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("entryGuid", morpheme.EntryGuid);
                            writer.WriteString("msaGuid", morpheme.MsaGuid);
                            writer.WriteString("msaClass", morpheme.MsaClass);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteBoolean("trace", true);
                if (maxWork is { } work) writer.WriteNumber("maxWork", work);
                if (timeoutMs is { } timeout) writer.WriteNumber("wordTimeoutMs", timeout);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>The input read against the project: lines to send (null where a line is blank or failed) and the error rows.</summary>
    internal sealed record Planned(IReadOnlyList<PlannedLine?> Lines, IReadOnlyList<GenerateBatchRow> Errors, bool TooMany)
    {
        internal int Sent => Lines.Count(line => line is not null);
    }

    internal static Planned Plan(LcmCache cache, IEnumerable<string> input)
    {
        var lines = new List<PlannedLine?>();
        var errors = new List<GenerateBatchRow>();
        foreach (var text in input)
        {
            var number = lines.Count + 1;
            if (number > MaxLines) return new Planned(lines, errors, true);
            if (string.IsNullOrWhiteSpace(text))
            {
                lines.Add(null);
                continue;
            }
            var planned = PlanLine(cache, number, text, out var failure);
            lines.Add(planned);
            if (failure is not null) errors.Add(failure);
        }
        return new Planned(lines, errors, false);
    }

    private static PlannedLine? PlanLine(LcmCache cache, int number, string text, out GenerateBatchRow? failure)
    {
        var id = number.ToString(CultureInfo.InvariantCulture);
        GenerateBatchRow Fail(string code, string message, string? entry = null, string? sense = null) =>
            new(GenerateBatchSchema.RowId, number, id, "error", new GenerateBatchCause(code, message),
                new GenerateBatchTarget(entry, sense, null), null, null, null, null, null, []);
        failure = null;
        if (Encoding.UTF8.GetByteCount(text) > MaxLineBytes)
        {
            failure = Fail("bad_row", "The line is over 1 MiB.");
            return null;
        }
        JsonDocument document;
        try { document = JsonDocument.Parse(text); }
        catch (JsonException exception)
        {
            failure = Fail("bad_row", "The line is not JSON: " + exception.Message);
            return null;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = Fail("bad_row", "The line is not a JSON object.");
                return null;
            }
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("id" or "entry" or "sense" or "cell"))
                {
                    failure = Fail("bad_row", $"Unknown field '{property.Name}'; a row has id, entry, sense and cell.");
                    return null;
                }
            if (root.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElement.GetString()))
                {
                    failure = Fail("bad_row", "'id' must be a non-empty string.");
                    return null;
                }
                id = idElement.GetString()!;
            }
            var entryText = Text(root, "entry", out var entryBad);
            var senseText = Text(root, "sense", out var senseBad);
            if (entryBad || senseBad || entryText is null && senseText is null)
            {
                failure = Fail("bad_row", "A row names an 'entry' or a 'sense' (or both) as a string guid or Motif id.");
                return null;
            }

            string? entryGuid = null, msaGuid = null, senseGuid = null;
            if (entryText is not null)
            {
                if (FormsMorphemeResolver.TryPair(entryText, out var pairEntry, out var pairMsa))
                    (entryGuid, msaGuid) = (pairEntry, pairMsa);
                else if (FormsMorphemeResolver.TryGuid(entryText, out var guid)) entryGuid = guid;
                else
                {
                    failure = Fail("bad_row", $"'entry' is '{entryText}'; give an entry guid or Motif id, or entry:msa.");
                    return null;
                }
            }
            if (senseText is not null)
            {
                if (!FormsMorphemeResolver.TryGuid(senseText, out var guid))
                {
                    failure = Fail("bad_row", $"'sense' is '{senseText}'; give a sense guid or Motif id.", entryGuid);
                    return null;
                }
                senseGuid = guid;
                if (cache.ServiceLocator.ObjectRepository.TryGetObject(Guid.Parse(guid), out var found) && found is ILexSense sense)
                {
                    var owner = sense.Entry.Guid.ToString("D", CultureInfo.InvariantCulture);
                    if (entryGuid is not null && !string.Equals(entryGuid, owner, StringComparison.Ordinal))
                    {
                        failure = Fail("sense_not_owned_by_entry", $"Sense {guid} belongs to entry {owner}, not {entryGuid}.", entryGuid, guid);
                        return null;
                    }
                    entryGuid ??= owner;
                }
                else
                {
                    failure = Fail("unknown_sense", $"No sense has the id '{senseText}' in the project.", entryGuid, guid);
                    return null;
                }
            }

            IReadOnlyList<IReadOnlyList<ResolvedMorpheme>>? cell = null;
            if (root.TryGetProperty("cell", out var cellElement))
            {
                var read = ReadCell(cache, cellElement, out var cellFailure);
                if (cellFailure is not null)
                {
                    failure = Fail(cellFailure.Value.Code, cellFailure.Value.Message, entryGuid, senseGuid);
                    return null;
                }
                cell = read;
            }
            return new PlannedLine(number, id, entryGuid!, senseGuid, msaGuid, cell);
        }
    }

    private static string? Text(JsonElement root, string name, out bool bad)
    {
        bad = false;
        if (!root.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
            return element.GetString()!.Trim();
        bad = true;
        return null;
    }

    private static IReadOnlyList<IReadOnlyList<ResolvedMorpheme>>? ReadCell(
        LcmCache cache, JsonElement cell, out (string Code, string Message)? failure)
    {
        failure = null;
        if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() == 0)
        {
            failure = ("bad_row", "'cell' is a non-empty array of morpheme names, or an array of such arrays; omit it to generate the root alone.");
            return null;
        }
        var combinations = new List<IReadOnlyList<ResolvedMorpheme>>();
        var nested = cell[0].ValueKind == JsonValueKind.Array;
        if (cell.EnumerateArray().Any(item => (item.ValueKind == JsonValueKind.Array) != nested))
        {
            failure = ("bad_row", "'cell' mixes names and arrays; give one combination as names, or several as arrays of names.");
            return null;
        }
        var groups = nested ? cell.EnumerateArray().ToArray() : new[] { cell };
        if (groups.Length > MaxCombinations)
        {
            failure = ("bad_row", $"'cell' has more than {MaxCombinations.ToString(CultureInfo.InvariantCulture)} combinations.");
            return null;
        }
        var memo = new Dictionary<string, ResolvedMorpheme>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group.GetArrayLength() == 0 || group.GetArrayLength() >= FormsMorphemeResolver.MaxMorphemes)
            {
                failure = ("bad_row", $"A combination names 1 through {FormsMorphemeResolver.MaxMorphemes - 1} morphemes.");
                return null;
            }
            var combination = new List<ResolvedMorpheme>();
            foreach (var item in group.EnumerateArray())
            {
                var spec = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (string.IsNullOrWhiteSpace(spec))
                {
                    failure = ("bad_row", "A morpheme in 'cell' is not a non-empty string.");
                    return null;
                }
                if (!memo.TryGetValue(spec, out var morpheme))
                {
                    var found = FormsMorphemeResolver.Resolve(cache, spec, asRoot: false);
                    if (found.Refusal is { } refusal)
                    {
                        failure = (refusal.Code == RefusalCodes.FormsExplainMorphemeAmbiguous ? "morpheme_ambiguous" : "morpheme_unresolved",
                            refusal.Message);
                        return null;
                    }
                    memo[spec] = morpheme = found.Morpheme!;
                }
                combination.Add(morpheme);
            }
            combinations.Add(combination);
        }
        return combinations;
    }

    /// <summary>Reads one row PanGloss wrote into the Motif row; a row Motif cannot read is an error row, never a guess.</summary>
    internal static GenerateBatchRow Project(string json, Planned planned)
    {
        int? line = null;
        string? id = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            line = root.TryGetProperty("line", out var lineElement) && lineElement.TryGetInt32(out var number) ? number : null;
            id = Property(root, "id");
            var source = line is { } at && at >= 1 && at <= planned.Lines.Count ? planned.Lines[at - 1] : null;
            var target = Target(root);
            var status = Property(root, "status") ?? (Property(root, "type") == "error" ? "error" : null);
            if (status is not ("ok" or "none" or "error")) throw new FormatException("the row has no status of ok, none or error");
            var cause = Cause(root);
            if (status != "ok" && cause is null) throw new FormatException("a row with no surface has no cause");
            if (status == "error")
                return new(GenerateBatchSchema.RowId, line, id, "error", cause, target, null, null, null, null, null, []);
            var comboKey = Property(root, "comboKey");
            var morphemes = (source?.Cell?.SelectMany(combination => combination).DistinctBy(morpheme => morpheme.MsaGuid).ToList() ?? [])
                .Append(new ResolvedMorpheme(target.Entry ?? string.Empty, target.Msa ?? string.Empty, ResolvedMorpheme.StemClass, null))
                .ToArray();
            if (!FormsExplainProjection.TryProject(json, morphemes, out var context, out var error))
                throw new FormatException(error);
            var cell = comboKey is null ? null : CellOf(source, comboKey);
            var surfaces = status == "ok" ? context!.Surfaces : [];
            if (status == "ok" && surfaces.Count == 0) throw new FormatException("an ok row has no surface");
            return new(GenerateBatchSchema.RowId, line, id, status, status == "ok" ? null : cause, target, cell, comboKey,
                root.TryGetProperty("cellIndex", out var index) && index.TryGetInt32(out var cellIndex) ? cellIndex : null,
                context!.Complete, context.StopReason, surfaces);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return new(GenerateBatchSchema.RowId, line, id, "error",
                new GenerateBatchCause("parser_row_unreadable",
                    "PanGloss's generate row is malformed (" + exception.Message + "). This is a PanGloss reply-shape problem; report it to PanGloss."),
                new GenerateBatchTarget(null, null, null), null, null, null, null, null, []);
        }
    }

    private static IReadOnlyList<FormsExplainMorpheme>? CellOf(PlannedLine? source, string comboKey) =>
        source?.Cell?.FirstOrDefault(combination =>
                string.Equals(comboKey == "-" ? "-" : string.Join('+', combination.Select(morpheme => morpheme.MsaGuid)), comboKey,
                    StringComparison.Ordinal))
            ?.Select(morpheme => morpheme.ToResponse()).ToArray();

    private static GenerateBatchTarget Target(JsonElement root)
    {
        if (!root.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
            return new GenerateBatchTarget(null, null, null);
        return new GenerateBatchTarget(Property(target, "entryGuid"), Property(target, "senseGuid"), Property(target, "msaGuid"));
    }

    // A none row's cause carries a detail sentence; an error row's carries a message. Both are PanGloss's words.
    private static GenerateBatchCause? Cause(JsonElement root)
    {
        if (!root.TryGetProperty("cause", out var cause) || cause.ValueKind != JsonValueKind.Object) return null;
        var code = Property(cause, "code") ?? throw new FormatException("a cause has no code");
        return new GenerateBatchCause(code, Property(cause, "message") ?? Property(cause, "detail"));
    }

    private static string? Property(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static CommandOutcome<GenerateBatchResponse> Failed(PanGlossOutcome outcome, string projectPath, string parserVersion) =>
        outcome switch
        {
            PanGlossOutcome.Cancelled => Refuse(RefusalCodes.WordTraceCancelled, FailureReason.Cancelled, "The batch was cancelled."),
            PanGlossOutcome.MemoryLimit memory => CommandOutcome<GenerateBatchResponse>.Refused(ParserMemoryRefusal.From(projectPath, memory.Kill)),
            PanGlossOutcome.Refused refused => CommandOutcome<GenerateBatchResponse>.Refused(
                ParserExecutionRefusal.From(RefusalCodes.FormsExplainParserRefused, projectPath, refused)),
            PanGlossOutcome.Unavailable { ExecutableMissing: true } missing => CommandOutcome<GenerateBatchResponse>.Refused(
                new Refusal(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused, missing.Detail,
                    ParserNotFoundFact.Mark(null, true))),
            PanGlossOutcome.Unavailable unavailable => Refuse(RefusalCodes.FormsExplainParserUnsupported, FailureReason.Refused,
                $"The installed PanGloss ({parserVersion}) cannot run a batch of target and cell rows: {unavailable.Detail} " +
                $"Motif pins {PanGlossInterfaceVersions.SourceTag}, which does.",
                new Dictionary<string, string> { ["parserVersion"] = parserVersion }),
            _ => Refuse(RefusalCodes.FormsExplainUnfinished, FailureReason.Refused, outcome.Message),
        };

    private static CommandOutcome<GenerateBatchResponse>? Check(GenerateBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BatchPath)) return Invalid("Name the batch file with --batch.");
        if (string.IsNullOrWhiteSpace(request.OutPath)) return Invalid("Name the artifact file the full rows go to with --out.");
        if (!File.Exists(request.BatchPath)) return Invalid($"The batch file '{request.BatchPath}' does not exist.");
        if (request.TimeoutMs is { } timeout && timeout is < 1 or > MaxTimeoutMs)
            return Invalid($"--timeout-ms is 1 through {MaxTimeoutMs} milliseconds.");
        if (request.MaxWork is < 1) return Invalid("--max-work must be at least 1.");
        var proposal = string.IsNullOrWhiteSpace(request.At) || request.At == "baseline" ? null : request.At;
        if (proposal is not null && !CanonicalId.TryParse(proposal, out _))
            return Invalid($"'{request.At}' is neither 'baseline' nor a Proposal id.");
        return null;
    }

    private static CommandOutcome<GenerateBatchResponse> Invalid(string message) =>
        Refuse(RefusalCodes.FormsExplainRequestInvalid, FailureReason.InvalidArgument, message);

    private static CommandOutcome<GenerateBatchResponse> Refuse(
        string code, FailureReason reason, string message, IReadOnlyDictionary<string, string>? facts = null) =>
        CommandOutcome<GenerateBatchResponse>.Refused(new Refusal(code, reason, message, facts));
}
