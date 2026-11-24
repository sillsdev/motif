using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Which word to compare, and the Proposal whose latest Trial copy it is compared on.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Word">The exact word form to parse on both sides.</param>
/// <param name="ProposalId">The Proposal whose latest Trial copy is read.</param>
/// <param name="MaxPaths">The most terminal paths to read per side, 1 through 64; omitted reads 64.</param>
public sealed record WhyLostRequest(string ProjectPath, string Word, string ProposalId, int? MaxPaths = null);

/// <summary>
/// Compares one word's parse at the Baseline with its parse on a Proposal's Trial copy under one parse lease, lists the
/// Readings lost, gained and kept by canonical morph id, and for each lost Reading reports the stop PanGloss recorded
/// and the Proposal operation that names the object it stopped at. It reads and changes nothing, and it infers no cause.
/// </summary>
public static class WhyLostQuery
{
    private const int DefaultPaths = RejectionTraceLimits.MaximumPaths;

    /// <summary>Compares a word with the located parser.</summary>
    public static CommandOutcome<WhyLostResponse> Compare(WhyLostRequest request, CancellationToken cancellationToken = default) =>
        Compare(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Compares a word with an explicitly selected parser.</summary>
    public static CommandOutcome<WhyLostResponse> Compare(
        WhyLostRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request) is { } invalid) return invalid;
        using var invoker = new PanGlossInvoker(parserPath);
        return Compare(request, invoker, cancellationToken);
    }

    internal static CommandOutcome<WhyLostResponse> Compare(
        WhyLostRequest request, IPanGlossInvoker invoker, CancellationToken cancellationToken)
    {
        if (Check(request) is { } invalid) return invalid;
        var maxPaths = request.MaxPaths ?? DefaultPaths;
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var parseLease = ProjectParseLease.TryAcquire(database);
            if (parseLease is null) return CommandOutcome<WhyLostResponse>.Refused(ProjectParseLease.BusyRefusal());
            var baselineGrammar = TracedGrammar.Resolve(database, project, null);
            if (!baselineGrammar.Succeeded) return CommandOutcome<WhyLostResponse>.Refused(baselineGrammar.Refusal!);
            var trialGrammar = TracedGrammar.Resolve(database, project, request.ProposalId);
            if (!trialGrammar.Succeeded) return CommandOutcome<WhyLostResponse>.Refused(trialGrammar.Refusal!);

            var baseline = RejectionTraceQuery.RunDescribed(invoker, baselineGrammar.Value!.FwDataPath, request.ProjectPath,
                request.Word, maxPaths, baselineGrammar.Value.TracedAgainst, cancellationToken);
            if (!baseline.Result.Succeeded) return CommandOutcome<WhyLostResponse>.Refused(baseline.Result.Refusal!);
            var trial = RejectionTraceQuery.RunDescribed(invoker, trialGrammar.Value!.FwDataPath, request.ProjectPath,
                request.Word, maxPaths, trialGrammar.Value.TracedAgainst, cancellationToken);
            if (!trial.Result.Succeeded) return CommandOutcome<WhyLostResponse>.Refused(trial.Result.Refusal!);

            Proposal proposal;
            RegressionContext context;
            try
            {
                proposal = TrialTouchedWords.LoadProposal(database, request.ProposalId);
                context = RegressionContext.Load(database, ProjectWorkspaceKey.Compute(project), request.ProposalId);
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<WhyLostResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
            using (context)
            using (var forms = TrialForms.Open(trialGrammar.Value.FwDataPath))
                return Shape(request, baseline.Result.Value!, baseline.DescribedRejectReasons, trial.Result.Value!,
                    trial.DescribedRejectReasons, trialGrammar.Value.TrialJobId, proposal, context, forms);
        });
    }

    /// <summary>Diffs two traces; the seam tests drive without a project or a parser.</summary>
    internal static CommandOutcome<WhyLostResponse> Shape(WhyLostRequest request, RejectionTraceResponse baseline,
        IReadOnlyList<string>? baselineReasons, RejectionTraceResponse trial, IReadOnlyList<string>? trialReasons,
        string? trialJobId, Proposal proposal, RegressionContext context, WhyLostProjection.IFormReader forms)
    {
        if (baseline.Producer.GrammarHashSemantics is { } left && trial.Producer.GrammarHashSemantics is { } right &&
            !string.Equals(left, right, StringComparison.Ordinal))
            return CommandOutcome<WhyLostResponse>.Refused(new Refusal(RefusalCodes.WhyLostIncomparable,
                FailureReason.Refused,
                $"The Baseline trace hashes its grammar as '{left}' and the Trial copy's as '{right}', so the two are not " +
                "compared."));
        return CommandOutcome<WhyLostResponse>.Success(WhyLostProjection.Project(request.Word, request.ProposalId,
            trialJobId, baseline, trial, Describes(baselineReasons), Describes(trialReasons), proposal, context, forms));
    }

    private static bool Describes(IReadOnlyList<string>? reasons) =>
        reasons is not null && reasons.Contains(WhyLostProjection.BlockedReason);

    private static CommandOutcome<WhyLostResponse>? Check(WhyLostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProposalId) || request.ProposalId == "baseline" ||
            !CanonicalId.TryParse(request.ProposalId, out _))
            return Refuse("Give the Proposal's id with --proposal; 'baseline' has no Trial copy to compare.");
        var word = RejectionTraceQuery.Check(request.Word, request.ProposalId, request.MaxPaths);
        return word is null ? null : Refuse(word.Refusal!.Message);
    }

    private static CommandOutcome<WhyLostResponse> Refuse(string message) =>
        CommandOutcome<WhyLostResponse>.Refused(new Refusal(RefusalCodes.WhyLostRequestInvalid,
            FailureReason.InvalidArgument, message));

    /// <summary>Reads form text and owning entries from the Trial copy the trace ran on.</summary>
    private sealed class TrialForms : WhyLostProjection.IFormReader, IDisposable
    {
        private readonly BaselineReadCache? _reader;

        private TrialForms(BaselineReadCache? reader) => _reader = reader;

        public static TrialForms Open(string fwDataPath)
        {
            try { return new TrialForms(BaselineReadCache.Open(fwDataPath)); }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException)
            {
                return new TrialForms(null);
            }
        }

        public string? TextOf(string formId) => Find(formId)?.Form.BestVernacularAlternative?.Text;

        public string? EntryOf(string formId) =>
            Find(formId)?.OwnerOfClass<ILexEntry>() is { } entry ? CanonicalId.FromGuid(entry.Guid).ToString() : null;

        private IMoForm? Find(string formId) =>
            _reader is not null && RegressionContext.ToGuid(formId) is { } guid &&
            _reader.Cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found) ? found as IMoForm : null;

        public void Dispose() => _reader?.Dispose();
    }
}
