using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The grammar check stored for a project's current Baseline by the parser installed now, or
/// <see langword="null"/> <see cref="Check"/> when there is none and the grammar has not been checked yet.
/// </summary>
public sealed record StoredGrammarCheckResponse(GrammarCheckResponse? Check);

/// <summary>
/// Reads the grammar check <see cref="GrammarCheckQuery"/> last stored for the current Baseline, without ever
/// running the parser: opening a project shows what is known and leaves checking to the person. Read-only and
/// outside the command catalog, like the other front-end queries.
/// </summary>
public static class StoredGrammarCheckQuery
{
    /// <summary>Reads the stored check made by the parser the real installation uses.</summary>
    public static CommandOutcome<StoredGrammarCheckResponse> Query(GrammarCheckRequest request) =>
        Query(request, ParserStamp());

    /// <summary>
    /// Reads the check stored under <paramref name="parserStamp"/>, which names the parser build exactly as
    /// <see cref="GrammarCheckQuery"/> stamps it; <see langword="null"/> means no parser, so nothing stored.
    /// </summary>
    internal static CommandOutcome<StoredGrammarCheckResponse> Query(GrammarCheckRequest request, string? parserStamp)
    {
        ArgumentNullException.ThrowIfNull(request);
        var neverRuns = new StoredOnlyInvoker();
        var outcome = GrammarCheckQuery.Query(request, neverRuns, CancellationToken.None, parserStamp);
        if (neverRuns.WasAsked) return CommandOutcome<StoredGrammarCheckResponse>.Success(new(null));
        return outcome.Succeeded
            ? CommandOutcome<StoredGrammarCheckResponse>.Success(new(outcome.Value))
            : CommandOutcome<StoredGrammarCheckResponse>.Refused(outcome.Refusal!);
    }

    // The stamp GrammarCheckQuery writes beside a stored check; a different stamp only means "not stored".
    private static string? ParserStamp()
    {
        if (PanGlossExecutable.TryLocate() is not { } exe || !File.Exists(exe)) return null;
        var info = new FileInfo(exe);
        return string.Create(CultureInfo.InvariantCulture, $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}");
    }

    // Stands in for the parser so a missing stored check ends the read instead of starting a run.
    private sealed class StoredOnlyInvoker : IPanGlossInvoker
    {
        public bool WasAsked { get; private set; }

        public Task<PanGlossOutcome> RunAsync(
            PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            WasAsked = true;
            return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Cancelled());
        }
    }
}
