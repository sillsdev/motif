using System.Globalization;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands.Queries;

/// <summary>Which word to explain and the morph sequences it was expected to parse as.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Word">The exact word form.</param>
/// <param name="Expect">One to 16 candidates, each <c>morph,morph|morph,…</c>; see the why-not Help page for the spelling.</param>
/// <param name="At"><c>baseline</c> (the default) for the current published Baseline, or a Proposal id for its latest Trial copy.</param>
public sealed record WhyNotRequest(string ProjectPath, string Word, IReadOnlyList<string> Expect, string? At = null);

/// <summary>
/// Explains why a word did not parse as the morphs a person expected by asking the PanGloss that parses to judge each
/// candidate. It reads the project's Baseline or Trial copy and changes nothing, and it never reasons about a
/// grammar itself: a failure the parser does not name comes back unknown.
/// </summary>
public static class WhyNotQuery
{
    private const int MaxWordScalars = 256;

    /// <summary>Explains a word with the located parser.</summary>
    public static CommandOutcome<WhyNotResponse> Explain(WhyNotRequest request, CancellationToken cancellationToken = default) =>
        Explain(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Explains a word with an explicitly selected parser.</summary>
    public static CommandOutcome<WhyNotResponse> Explain(
        WhyNotRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request) is { Candidates: null } invalid) return invalid.Refusal!;
        using var invoker = new PanGlossInvoker(parserPath);
        return Explain(request, invoker, cancellationToken);
    }

    internal static CommandOutcome<WhyNotResponse> Explain(
        WhyNotRequest request, IPanGlossInvoker invoker, CancellationToken cancellationToken)
    {
        var checkedRequest = Check(request);
        if (checkedRequest.Candidates is not { } candidates) return checkedRequest.Refusal!;
        var proposalId = string.IsNullOrWhiteSpace(request.At) || request.At == "baseline" ? null : request.At;
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var parseLease = ProjectParseLease.TryAcquire(database);
            if (parseLease is null) return CommandOutcome<WhyNotResponse>.Refused(ProjectParseLease.BusyRefusal());
            var resolved = TracedGrammar.Resolve(database, project, proposalId);
            if (!resolved.Succeeded) return CommandOutcome<WhyNotResponse>.Refused(resolved.Refusal!);
            return Run(invoker, resolved.Value!.FwDataPath, request.ProjectPath, request.Word, candidates,
                resolved.Value.TracedAgainst, cancellationToken);
        });
    }

    /// <summary>Asks the parser about an exact grammar and projects its answer; the seam tests drive without a project.</summary>
    internal static CommandOutcome<WhyNotResponse> Run(
        IPanGlossInvoker invoker, string grammarPath, string projectPath, string word,
        IReadOnlyList<WhyNotCandidate> candidates, string tracedAgainst, CancellationToken cancellationToken)
    {
        var outcome = invoker.RunAsync(new PanGlossRequest.WhyNot(grammarPath, word, candidates), "why-not:" + word,
            cancellationToken).GetAwaiter().GetResult();
        return Shape(outcome, projectPath, word, candidates, tracedAgainst);
    }

    internal static CommandOutcome<WhyNotResponse> Shape(
        PanGlossOutcome outcome, string projectPath, string word, IReadOnlyList<WhyNotCandidate> candidates, string tracedAgainst)
    {
        switch (outcome)
        {
            case PanGlossOutcome.Completed completed:
                return WhyNotProjection.TryProject(completed.Output, word, tracedAgainst, candidates,
                    completed.WhyNotKinds ?? [], out var response, out var error)
                    ? CommandOutcome<WhyNotResponse>.Success(response!)
                    : Refuse(RefusalCodes.WhyNotMalformed, FailureReason.Refused,
                        "PanGloss's why-not report could not be used: " + error);
            case PanGlossOutcome.Cancelled:
                return Refuse(RefusalCodes.WordTraceCancelled, FailureReason.Cancelled, "The why-not question was cancelled.");
            case PanGlossOutcome.MemoryLimit memory:
                return CommandOutcome<WhyNotResponse>.Refused(ParserMemoryRefusal.From(projectPath, memory.Kill));
            case PanGlossOutcome.Refused refused:
                return CommandOutcome<WhyNotResponse>.Refused(
                    ParserExecutionRefusal.From(RefusalCodes.WhyNotInputRefused, projectPath, refused));
            case PanGlossOutcome.Unavailable { MissingCapability: not null } incapable:
                return Refuse(RefusalCodes.WhyNotParserUnsupported, FailureReason.Refused,
                    incapable.Detail + " Update PanGloss to a release whose --describe declares interfaces.whyNot.");
            case PanGlossOutcome.Unavailable unavailable:
                return CommandOutcome<WhyNotResponse>.Refused(new Refusal(RefusalCodes.WordTraceParserUnavailable,
                    FailureReason.Refused, unavailable.Detail, ParserNotFoundFact.Mark(null, unavailable.ExecutableMissing)));
            default:
                return Refuse(RefusalCodes.WhyNotUnfinished, FailureReason.Refused, outcome.Message);
        }
    }

    private readonly record struct Checked(IReadOnlyList<WhyNotCandidate>? Candidates, CommandOutcome<WhyNotResponse>? Refusal);

    private static Checked Check(WhyNotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Word) || request.Word.EnumerateRunes().Count() > MaxWordScalars ||
            request.Word.StartsWith('-') || request.Word.Any(char.IsWhiteSpace))
            return Invalid($"Give one word of 1 to {MaxWordScalars} characters, with no spaces and not starting with '-'.");
        if (request.Expect is not { Count: > 0 })
            return Invalid("Give at least one candidate with --expect, such as --expect kib,ak,ek.");
        if (request.Expect.Count > WhyNotLimits.MaxCandidates)
            return Invalid($"Give at most {WhyNotLimits.MaxCandidates.ToString(CultureInfo.InvariantCulture)} candidates; there were {request.Expect.Count.ToString(CultureInfo.InvariantCulture)}.");
        if (!string.IsNullOrWhiteSpace(request.At) && request.At != "baseline" && !CanonicalId.TryParse(request.At, out _))
            return Invalid($"'{request.At}' is neither 'baseline' nor a Proposal id.");
        var candidates = new List<WhyNotCandidate>(request.Expect.Count);
        foreach (var spec in request.Expect)
        {
            if (!WhyNotCandidateSpec.TryParse(spec, out var candidate, out var error)) return Invalid(error);
            candidates.Add(candidate!);
        }
        return WhyNotInput.Problem(candidates) is { } problem ? Invalid(problem) : new Checked(candidates, null);
    }

    private static Checked Invalid(string message) =>
        new(null, Refuse(RefusalCodes.WhyNotRequestInvalid, FailureReason.InvalidArgument, message));

    private static CommandOutcome<WhyNotResponse> Refuse(string code, FailureReason reason, string message) =>
        CommandOutcome<WhyNotResponse>.Refused(new Refusal(code, reason, message));
}
