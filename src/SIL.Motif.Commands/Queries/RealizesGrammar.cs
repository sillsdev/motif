using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The questions the <c>possible</c> and <c>generated</c> tiers ask the resident PanGloss that holds the grammar: which rules may
/// change a morpheme, what a root and its morphemes generate, and where a rule's pattern matches. A refusal comes as a
/// <see cref="QueryRefusalException"/>; a test replays recorded replies here.
/// </summary>
internal interface IRealizesGrammar
{
    /// <summary>The <c>effects</c> reply for a morpheme.</summary>
    EffectsResult Effects(ServeEffectsRequest request, CancellationToken cancellationToken);

    /// <summary>The raw <c>generate</c> result line for a request; an error PanGloss gives for the request comes back as null.</summary>
    string? Generate(ServeGenerateRequest request, CancellationToken cancellationToken);

    /// <summary>One <c>match</c> request of a pattern over words.</summary>
    PhonBatchResult Match(string pattern, IReadOnlyList<string> words, CancellationToken cancellationToken);
}

/// <summary>The resident <c>pangloss serve</c> that holds the grammar file, shared with <c>phon</c> and the other resident calls.</summary>
internal sealed class ResidentRealizesGrammar : IRealizesGrammar
{
    private readonly WarmPanGlossPool _pool;
    private readonly string _grammarPath;
    private readonly string _stateToken;
    private readonly ResidentPhonMatcher _matcher;

    public ResidentRealizesGrammar(string grammarPath, string? parserPath)
    {
        var executable = parserPath ?? PanGlossExecutable.TryLocate();
        if (executable is null)
            throw new QueryRefusalException(new Refusal(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused,
                PanGlossExecutable.NotFoundMessage, ParserNotFoundFact.Mark(null, true)));
        if (!File.Exists(grammarPath))
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The grammar '{grammarPath}' is not a file."));
        _grammarPath = Path.GetFullPath(grammarPath);
        _stateToken = BatchInvocationEvidence.DigestFile(_grammarPath);
        _pool = TryWordSessions.PoolFor(executable);
        _matcher = new ResidentPhonMatcher(_pool, _grammarPath, _stateToken, _grammarPath, ParserExecutableIdentity.ReadVersion(executable));
    }

    public EffectsResult Effects(ServeEffectsRequest request, CancellationToken cancellationToken)
    {
        var reply = _pool.TryEffectsAsync(_grammarPath, _stateToken, _grammarPath, request, cancellationToken).GetAwaiter().GetResult();
        switch (reply)
        {
            case WarmEffectsResult.Completed completed:
                try { return EffectsResult.Read(completed.Json); }
                catch (FormatException exception) { throw Unavailable("PanGloss wrote an effects answer Motif cannot read: " + exception.Message); }
            case WarmEffectsResult.Unsupported unsupported:
                throw Unavailable("The installed PanGloss cannot report which rules change a morpheme: " + unsupported.Detail);
            case WarmEffectsResult.Rejected rejected:
                throw Unavailable($"PanGloss refused the effects request ({rejected.Code}): {rejected.Message}");
            case WarmEffectsResult.Failed failed:
                throw Unavailable($"The resident PanGloss did not answer the effects request ({failed.Code}): {failed.Message}");
            default:
                throw new OperationCanceledException(cancellationToken);
        }
    }

    public string? Generate(ServeGenerateRequest request, CancellationToken cancellationToken)
    {
        var reply = _pool.TryGenerateAsync(_grammarPath, _stateToken, _grammarPath, request, cancellationToken).GetAwaiter().GetResult();
        switch (reply)
        {
            case WarmGenerateResult.Completed completed:
                return completed.Json;
            case WarmGenerateResult.Rejected:
                return null;
            case WarmGenerateResult.Unsupported unsupported:
                throw Unavailable("The installed PanGloss cannot generate forms with a rule trace: " + unsupported.Detail);
            case WarmGenerateResult.Failed failed:
                throw Unavailable($"The resident PanGloss did not answer the generate request ({failed.Code}): {failed.Message}");
            default:
                throw new OperationCanceledException(cancellationToken);
        }
    }

    public PhonBatchResult Match(string pattern, IReadOnlyList<string> words, CancellationToken cancellationToken) =>
        _matcher.Match(pattern, words, PhonSegmentTest.Default, cancellationToken);

    private static QueryRefusalException Unavailable(string message) =>
        new(new Refusal(RefusalCodes.QueryRealizesGrammarUnavailable, FailureReason.Refused, message));
}
