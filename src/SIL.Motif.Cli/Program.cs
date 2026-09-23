using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SIL.Motif.Cli;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;

var commandPolicy = CommandSurfacePolicy.FromEnvironment(
    Environment.GetEnvironmentVariable(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable));

if (args.Length == 0)
{
    PrintUsage(Console.Error, commandPolicy);
    return 1;
}

var commandName = ResolveCommandName(args);
var command = CommandCatalog.All.FirstOrDefault(item => item.Name == commandName);
if (command is not null && !commandPolicy.IsAvailable(command))
    return RefuseUnavailableCommand(commandName, args.Contains("--json", StringComparer.Ordinal));

var verb = args[0];
var rest = args[1..];

try
{
    var (flags, positionals, forwardedArguments) = ParseArgs(rest);

    // Every invocation naming a project upserts it into the machine store (ADR 0041 decision 4).
    if (flags.TryGetValue("project", out var projectForRegistry))
        RecordKnownProject(projectForRegistry);

    // Every migrated read surface renders both ways from one projection (ADR 0021 decision 2).
    var asJson = flags.ContainsKey("json");
    var usage = new UsageLog();

    CommandResult result;
    // RenderProposal below already fully renders text or JSON; the bottom printer must not wrap it again.
    var alreadyRendered = false;

    CommandResult RenderProposal<T>(CommandOutcome<T> outcome, bool successAsJson = true) where T : class
    {
        alreadyRendered = true;
        return ProposalCommandRenderer.Render(outcome, asJson, successAsJson);
    }

    CommandResult RenderCommand<T>(CommandOutcome<T> outcome, bool successAsJson = true) where T : class
    {
        alreadyRendered = true;
        return CommandTextRenderer.Render(outcome, asJson, successAsJson);
    }

    switch (verb)
    {
        case "open":
            if (positionals.Count != 1)
                return Usage("Usage: motif open <path-to-.fwdata> [--json]", asJson);
            result = RenderProposal(ProposalCommands.Open(new OpenRequest(positionals[0]), usage));
            break;

        case "analyses":
            if (!flags.TryGetValue("project", out var analysesProject))
                return Usage(AnalysesUsage(), asJson);
            var hasAssessment = flags.TryGetValue("assessment", out var assessmentId);
            var hasCurrentSelection = flags.TryGetValue("current-selection-sha256", out var currentSelectionSha256);
            var hasCurrentGrammar = flags.TryGetValue("current-grammar-sha256", out var currentGrammarSha256);
            if ((hasAssessment || hasCurrentSelection || hasCurrentGrammar)
                && !(hasAssessment && hasCurrentSelection && hasCurrentGrammar))
            {
                return Usage(AnalysesUsage(), asJson);
            }
            if (hasAssessment
                && (!CanonicalId.TryParse(assessmentId, out _)
                    || !Sha256Value.IsCanonical(currentSelectionSha256)
                    || !Sha256Value.IsCanonical(currentGrammarSha256)))
            {
                return Usage(AnalysesUsage(), asJson);
            }
            result = hasAssessment
                ? RenderProposal(
                    ProposalCommands.Analyses(
                        new AssessmentAnalysesRequest(
                            analysesProject, CliProductVersion(), assessmentId!, currentSelectionSha256!,
                            currentGrammarSha256!),
                        usage))
                : RenderProposal(ProposalCommands.Analyses(new ManualAnalysesRequest(analysesProject), usage));
            break;

        case "new":
            if (!flags.TryGetValue("project", out var newProject) || !flags.TryGetValue("draft", out var newDraftName))
                return Usage("Usage: motif new --project <fwdata> --draft <name> [--label <text>]", asJson);
            result = RenderProposal(
                ProposalCommands.New(new NewDraftRequest(
                    newProject, CliProductVersion(), newDraftName, flags.GetValueOrDefault("label"))),
                successAsJson: false);
            break;

        case "add-set-gloss":
            if (!flags.TryGetValue("project", out var addProject) ||
                !flags.TryGetValue("draft", out var addDraftName) ||
                !flags.TryGetValue("target", out var addTarget) ||
                !flags.TryGetValue("ws", out var addWs) ||
                !flags.TryGetValue("text", out var addText))
            {
                return Usage(
                    "Usage: motif add-set-gloss --project <fwdata> --draft <name> --target <canonicalId> " +
                    "--ws <wsTag> --text <text> [--depends-on <opId>[,<opId>...]]", asJson);
            }
            var addDependsOn = flags.TryGetValue("depends-on", out var addDependsOnRaw)
                ? addDependsOnRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            result = RenderProposal(
                ProposalCommands.AddSetGloss(new AddSetGlossRequest(
                    addProject, CliProductVersion(), addDraftName, addTarget, addWs, addText, addDependsOn)),
                successAsJson: false);
            break;

        case "add-delete-lexeme-form":
            if (!flags.TryGetValue("project", out var addDelProject) ||
                !flags.TryGetValue("draft", out var addDelDraftName) ||
                !flags.TryGetValue("target", out var addDelTarget))
            {
                return Usage(
                    "Usage: motif add-delete-lexeme-form --project <fwdata> --draft <name> --target <canonicalId>",
                    asJson);
            }
            result = RenderProposal(
                ProposalCommands.AddDeleteLexemeForm(
                    new AddDeleteLexemeFormRequest(addDelProject, CliProductVersion(), addDelDraftName, addDelTarget)),
                successAsJson: false);
            break;

        case "compose-author-lexeme-form":
            if (!flags.TryGetValue("draft", out var composeDraftName) ||
                !flags.TryGetValue("project", out var composeProject) ||
                !flags.TryGetValue("intent", out var composeIntent))
            {
                return Usage(
                    "Usage: motif compose-author-lexeme-form --draft <name> --project <fwdata> --intent " +
                    "'{\"entry\":...,\"morphType\":...,\"ws\":...,\"text\":...}'", asJson);
            }
            result = RenderProposal(
                ProposalCommands.ComposeAuthorLexemeForm(new ComposeAuthorLexemeFormRequest(
                    composeProject, CliProductVersion(), composeDraftName, composeIntent)),
                successAsJson: false);
            break;

        case "compose-author-feature-structure":
            if (!flags.TryGetValue("draft", out var composeFsDraftName) ||
                !flags.TryGetValue("project", out var composeFsProject) ||
                !flags.TryGetValue("intent", out var composeFsIntent))
            {
                return Usage(
                    "Usage: motif compose-author-feature-structure --draft <name> --project <fwdata> " +
                    "--intent '{\"msa\":...}'", asJson);
            }
            result = RenderProposal(
                ProposalCommands.ComposeAuthorFeatureStructure(new ComposeAuthorFeatureStructureRequest(
                    composeFsProject, CliProductVersion(), composeFsDraftName, composeFsIntent)),
                successAsJson: false);
            break;

        case "promote-gloss":
            if (!flags.TryGetValue("project", out var promoteProject) ||
                !flags.TryGetValue("draft", out var promoteDraftName) ||
                !flags.TryGetValue("target", out var promoteTarget) ||
                !flags.TryGetValue("ws", out var promoteWs) ||
                !flags.TryGetValue("text", out var promoteText) ||
                !flags.TryGetValue("corpus", out var promoteCorpus))
            {
                return Usage(
                    "Usage: motif promote-gloss --project <fwdata> --draft <name> --target <canonicalId> " +
                    "--ws <wsTag> --text <text> --corpus <corpusId> [--document <docId>]", asJson);
            }
            result = RenderProposal(
                ProposalCommands.PromoteGloss(new PromoteGlossRequest(
                    promoteProject, CliProductVersion(), promoteDraftName, promoteTarget, promoteWs, promoteText,
                    promoteCorpus, flags.GetValueOrDefault("document"))),
                successAsJson: false);
            break;

        case "label":
            if (!flags.TryGetValue("project", out var labelProject) ||
                !flags.TryGetValue("draft", out var labelDraftName) || positionals.Count != 1)
                return Usage("Usage: motif label --project <fwdata> --draft <name> <text>", asJson);
            result = RenderProposal(
                ProposalCommands.Label(new LabelRequest(labelProject, CliProductVersion(), labelDraftName, positionals[0])),
                successAsJson: false);
            break;

        case "comment":
            if (!flags.TryGetValue("project", out var commentProject) ||
                !flags.TryGetValue("draft", out var commentDraftName) || positionals.Count != 1)
                return Usage("Usage: motif comment --project <fwdata> --draft <name> <text>", asJson);
            result = RenderProposal(
                ProposalCommands.Comment(
                    new CommentRequest(commentProject, CliProductVersion(), commentDraftName, positionals[0])),
                successAsJson: false);
            break;

        case "finalize":
            if (!flags.TryGetValue("project", out var finalizeProject) ||
                !flags.TryGetValue("draft", out var finalizeDraftName))
                return Usage("Usage: motif finalize --project <fwdata> --draft <name>", asJson);
            result = RenderProposal(
                ProposalCommands.Finalize(new FinalizeRequest(finalizeProject, CliProductVersion(), finalizeDraftName)),
                successAsJson: false);
            break;

        case "discard-draft":
            if (!flags.TryGetValue("project", out var discardProject) ||
                !flags.TryGetValue("draft", out var discardDraftName))
                return Usage("Usage: motif discard-draft --project <fwdata> --draft <name>", asJson);
            result = RenderProposal(
                ProposalCommands.DiscardDraft(
                    new DiscardDraftRequest(discardProject, CliProductVersion(), discardDraftName)),
                successAsJson: false);
            break;

        case "reopen":
            if (!flags.TryGetValue("project", out var reopenProject) ||
                !flags.TryGetValue("draft", out var reopenDraftName) || positionals.Count != 1)
                return Usage("Usage: motif reopen --project <fwdata> --draft <name> <proposalId>", asJson);
            result = RenderProposal(
                ProposalCommands.Reopen(
                    new ReopenRequest(reopenProject, CliProductVersion(), reopenDraftName, positionals[0])),
                successAsJson: false);
            break;

        case "duplicate":
            if (!flags.TryGetValue("project", out var dupProject) ||
                !flags.TryGetValue("draft", out var dupDraftName) || positionals.Count != 1)
                return Usage("Usage: motif duplicate --project <fwdata> --draft <newName> <proposalId>", asJson);
            result = RenderProposal(
                ProposalCommands.Duplicate(
                    new DuplicateRequest(dupProject, CliProductVersion(), positionals[0], dupDraftName)),
                successAsJson: false);
            break;

        case "remove-operations":
            if (!flags.TryGetValue("project", out var removeProject) ||
                !flags.TryGetValue("draft", out var removeDraftName) || positionals.Count == 0)
            {
                return Usage(
                    "Usage: motif remove-operations --project <fwdata> --draft <name> <operationId> " +
                    "[<operationId>...] [--force]", asJson);
            }
            var removeForce = flags.TryGetValue("force", out var removeForceRaw) && IsTruthyFlag(removeForceRaw);
            result = RenderProposal(
                ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
                    removeProject, CliProductVersion(), removeDraftName, positionals, removeForce)),
                successAsJson: false);
            break;

        case "split":
            if (!flags.TryGetValue("project", out var splitProject) || positionals.Count < 2)
            {
                return Usage(
                    "Usage: motif split --project <fwdata> <proposalId> <draftName>=<opId>[,<opId>...] " +
                    "[<draftName>=<opId>[,<opId>...] ...] [--force]", asJson);
            }
            var splitForce = flags.TryGetValue("force", out var splitForceRaw) && IsTruthyFlag(splitForceRaw);
            var splitGroups = new List<SplitGroup>();
            foreach (var spec in positionals.Skip(1))
            {
                var eq = spec.IndexOf('=');
                if (eq <= 0 || eq == spec.Length - 1)
                {
                    return Usage(
                        $"Invalid split group '{spec}'. Expected '<draftName>=<opId>[,<opId>...]'.", asJson);
                }
                var groupDraftName = spec[..eq];
                var groupOpIds = spec[(eq + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                splitGroups.Add(new SplitGroup(groupDraftName, groupOpIds));
            }
            result = RenderProposal(
                ProposalCommands.Split(new SplitRequest(
                    splitProject, CliProductVersion(), positionals[0], splitGroups, splitForce)),
                successAsJson: false);
            break;

        case "defer":
            if (!flags.TryGetValue("project", out var deferProject) || positionals.Count != 1)
                return Usage("Usage: motif defer --project <fwdata> <proposalId>", asJson);
            result = RenderProposal(
                ProposalCommands.Defer(new DeferRequest(deferProject, CliProductVersion(), positionals[0])),
                successAsJson: false);
            break;

        case "reject":
            if (!flags.TryGetValue("project", out var rejectProject) || positionals.Count != 1)
                return Usage("Usage: motif reject --project <fwdata> <proposalId>", asJson);
            result = RenderProposal(
                ProposalCommands.Reject(new RejectRequest(rejectProject, CliProductVersion(), positionals[0])),
                successAsJson: false);
            break;

        case "supersede":
            if (!flags.TryGetValue("project", out var supersedeProject) || positionals.Count != 2)
                return Usage("Usage: motif supersede --project <fwdata> <proposalId> <supersededByProposalId>", asJson);
            result = RenderProposal(
                ProposalCommands.Supersede(new SupersedeRequest(
                    supersedeProject, CliProductVersion(), positionals[0], positionals[1])),
                successAsJson: false);
            break;

        case "list":
            if (!flags.TryGetValue("project", out var listProject))
                return Usage("Usage: motif list --project <fwdata> [--json]", asJson);
            result = RenderProposal(
                ProposalCommands.List(new ListProposalsRequest(listProject, CliProductVersion()), usage));
            break;

        case "show":
            if (!flags.TryGetValue("project", out var showProject) || positionals.Count != 1)
                return Usage("Usage: motif show --project <fwdata> <proposalId> [--json]", asJson);
            result = RenderProposal(
                ProposalCommands.Show(new ShowProposalRequest(showProject, CliProductVersion(), positionals[0]), usage));
            break;

        case "dry-run":
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var dryRunProject))
            {
                return Usage(
                    "Usage: motif dry-run --project <fwdata> <proposalId> [--wait] [--json]", asJson);
            }
            result = RenderCommand(
                JobCommands.EnqueueDryRun(
                    new EnqueueDryRunRequest(dryRunProject, CliProductVersion(), positionals[0]), usage),
                successAsJson: false);
            // A job just entered the queue: wake the runner before anything below waits on it.
            if (result.ExitCode == 0) RunnerKick.After();
            if (result.ExitCode == 0 && flags.ContainsKey("wait"))
            {
                var dryRunJobId = result.Output.Trim();
                var waitTimeout = flags.TryGetValue("wait-timeout-ms", out var waitTimeoutRaw) &&
                    int.TryParse(waitTimeoutRaw, out var waitTimeoutMs)
                    ? TimeSpan.FromMilliseconds(waitTimeoutMs)
                    : JobCommands.DefaultWaitTimeout;
                result = RenderCommand(JobCommands.WaitForDryRun(new WaitForDryRunRequest(
                    dryRunProject, CliProductVersion(), positionals[0], dryRunJobId, waitTimeout)));
            }
            break;

        case "trial":
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var trialProject))
            {
                return Usage(
                    "Usage: motif trial --project <fwdata> <proposalId> [--scope <name>] [--wait] [--json]", asJson);
            }
            result = RenderCommand(
                JobCommands.EnqueueTrial(
                    new EnqueueTrialRequest(
                        trialProject, CliProductVersion(), positionals[0], flags.GetValueOrDefault("scope")),
                    usage),
                successAsJson: false);
            // A job just entered the queue: wake the runner before anything below waits on it.
            if (result.ExitCode == 0) RunnerKick.After();
            if (result.ExitCode == 0 && flags.ContainsKey("wait"))
            {
                var trialJobId = result.Output.Trim();
                var trialWaitTimeout = flags.TryGetValue("wait-timeout-ms", out var trialWaitTimeoutRaw) &&
                    int.TryParse(trialWaitTimeoutRaw, out var trialWaitTimeoutMs)
                    ? TimeSpan.FromMilliseconds(trialWaitTimeoutMs)
                    : JobCommands.DefaultWaitTimeout;
                result = RenderCommand(JobCommands.WaitForJob(
                    new WaitForJobRequest(trialProject, trialJobId, CliProductVersion(), trialWaitTimeout)));
            }
            break;

        case "apply":
            if (positionals.Count != 1 ||
                !flags.TryGetValue("project", out var applyProject) ||
                !flags.TryGetValue("user", out var applyUser))
            {
                return Usage(
                    "Usage: motif apply <proposalId> --project <fwdata> --user <name> " +
                    "[--force] [--json]", asJson);
            }
            var applyForce = flags.ContainsKey("force");
            result = RenderProposal(
                ProposalCommands.Apply(
                    new ApplyRequest(applyProject, CliProductVersion(), positionals[0], applyUser, applyForce),
                    usage));
            break;

        case "log":
            if (!flags.TryGetValue("project", out var logProject))
                return Usage("Usage: motif log --project <fwdata> [--json]", asJson);
            result = RenderProposal(ProposalCommands.Log(new LogRequest(logProject), usage));
            break;

        case "add-corpus":
            if (!flags.TryGetValue("project", out var addCorpusProject) ||
                !flags.TryGetValue("id", out var corpusId) ||
                !flags.TryGetValue("description", out var corpusDescription) ||
                !flags.TryGetValue("tokeniser", out var corpusTokeniser) ||
                !flags.TryGetValue("tokeniser-version", out var corpusTokeniserVersion))
            {
                return Usage(
                    "Usage: motif add-corpus --project <fwdata> --id <id> --description <text> --tokeniser <name> " +
                    "--tokeniser-version <v> [--uri <url>] [--licence <text>] [--tokeniser-notes <text>] " +
                    "[--may-derive true|false] [--may-redistribute true|false] " +
                    "[--may-use-commercially true|false] [--requires-attribution true|false] " +
                    "[--licence-basis <text>]", asJson);
            }

            result = RenderCommand(CorpusCommands.AddCorpus(new AddCorpusRequest(
                addCorpusProject,
                CliProductVersion(),
                corpusId,
                corpusDescription,
                flags.GetValueOrDefault("uri"),
                flags.GetValueOrDefault("licence"),
                CorpusCommands.CapabilitiesFromFlags(flags),
                corpusTokeniser,
                corpusTokeniserVersion,
                flags.GetValueOrDefault("tokeniser-notes"))),
                successAsJson: false);
            break;

        case "add-document":
            if (!flags.TryGetValue("project", out var addDocumentProject) ||
                !flags.TryGetValue("corpus", out var documentCorpus) ||
                !flags.TryGetValue("doc", out var documentId) ||
                !flags.TryGetValue("source", out var documentPathOrUrl))
            {
                return Usage(
                    "Usage: motif add-document --project <fwdata> --corpus <id> --doc <id> " +
                    "--source <file-or-url> [--title <text>] [--licence <text>] [--may-derive true|false] " +
                    "[--licence-basis <text>]", asJson);
            }

            // No flags means "same as corpus", not "nothing established": pass null, not Unknown, or it overrides one.
            var documentCapabilities = HasAnyLicenceFlag(flags)
                ? CorpusCommands.CapabilitiesFromFlags(flags)
                : null;

            result = RenderCommand(CorpusCommands.AddDocument(new AddDocumentRequest(
                addDocumentProject,
                CliProductVersion(),
                documentCorpus,
                documentId,
                documentPathOrUrl,
                flags.GetValueOrDefault("title"),
                flags.GetValueOrDefault("licence"),
                documentCapabilities)),
                successAsJson: false);
            break;

        case "add-corpus-bundle":
            if (!flags.TryGetValue("project", out var addBundleProject) ||
                !flags.TryGetValue("bundle", out var bundlePath))
                return Usage("Usage: motif add-corpus-bundle --project <fwdata> --bundle <path-to-bundle.json>", asJson);
            result = RenderCommand(
                CorpusCommands.AddBundle(new AddCorpusBundleRequest(addBundleProject, CliProductVersion(), bundlePath)),
                successAsJson: false);
            break;

        case "corpora":
            if (!flags.TryGetValue("project", out var corporaProject))
                return Usage("Usage: motif corpora --project <fwdata> [--json]", asJson);
            result = RenderCommand(
                CorpusCommands.ListCorpora(new ListCorporaRequest(corporaProject, CliProductVersion()), usage));
            break;

        case "show-corpus":
            if (!flags.TryGetValue("project", out var showCorpusProject) || positionals.Count != 1)
                return Usage("Usage: motif show-corpus --project <fwdata> <corpusId> [--json]", asJson);
            result = RenderCommand(CorpusCommands.ShowCorpus(
                new ShowCorpusRequest(showCorpusProject, CliProductVersion(), positionals[0]), usage));
            break;

        case "baseline-refresh":
            if (!flags.TryGetValue("project", out var refreshProject))
                return Usage("Usage: motif baseline-refresh --project <fwdata>", asJson);
            result = RenderCommand(
                JobCommands.EnqueueBaselineRefresh(
                    new EnqueueBaselineRefreshRequest(refreshProject, CliProductVersion())),
                successAsJson: false);
            if (result.ExitCode == 0) RunnerKick.After();
            break;

        case "config":
            if (positionals.Count == 0)
                return Usage(ConfigUsage(), asJson);
            switch (positionals[0])
            {
                case "show":
                    if (positionals.Count != 1 || !flags.TryGetValue("project", out var configProject))
                        return Usage(ConfigUsage(), asJson);
                    result = RenderCommand(
                        ConfigCommands.Show(new ShowConfigRequest(configProject, CliProductVersion())));
                    break;

                default:
                    return Usage(ConfigUsage(), asJson);
            }
            break;

        case "report":
            if (flags.ContainsKey("list-kinds"))
            {
                result = RenderCommand(ReportCommands.ListKinds(new ListReportKindsRequest()));
                break;
            }
            if (!flags.TryGetValue("project", out var reportProject) ||
                !flags.TryGetValue("assessment", out var reportAssessment) ||
                !flags.TryGetValue("kind", out var reportKind))
            {
                return Usage(ReportUsage(), asJson);
            }
            result = RenderCommand(ReportCommands.Produce(new ProduceReportRequest(
                reportProject, CliProductVersion(), reportAssessment, reportKind,
                flags.GetValueOrDefault("word"), flags.GetValueOrDefault("text"))));
            break;

        case "compare":
            if (!flags.TryGetValue("project", out var compareProject) ||
                !flags.TryGetValue("from", out var compareFrom) ||
                !flags.TryGetValue("to", out var compareTo))
            {
                return Usage(CompareUsage(), asJson);
            }
            result = RenderCommand(CompareCommands.Produce(
                new ProduceComparisonRequest(compareProject, CliProductVersion(), compareFrom, compareTo)));
            break;

        case "baseline":
            if (positionals.Count == 0)
                return Usage(BaselineUsage(), asJson);
            switch (positionals[0])
            {
                case "capture":
                    if (positionals.Count != 2)
                        return Usage(BaselineUsage(), asJson);
                    result = RenderCommand(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(positionals[1])));
                    break;

                default:
                    return Usage(BaselineUsage(), asJson);
            }
            break;

        case "assess":
            if (positionals.Count != 1) return Usage(AssessUsage(), asJson);
            var assessRetryFailed = flags.ContainsKey("retry-failed");
            var hasAssessRetrySlowerThan = flags.ContainsKey("retry-slower-than");
            var hasAssessRetrySource = flags.TryGetValue("retry-source-assessment", out var assessRetrySource);
            if (hasAssessRetrySource != (assessRetryFailed || hasAssessRetrySlowerThan))
                return Usage(AssessUsage(), asJson);
            if (hasAssessRetrySource && !CanonicalId.TryParse(assessRetrySource, out _))
                return Usage(AssessUsage(), asJson);
            if (!TryParseGuidList(flags.GetValueOrDefault("texts"), out var assessTextIds))
                return Usage(AssessUsage(), asJson);
            flags.TryGetValue("words", out var assessWordsFile);
            if (assessWordsFile is not null && !File.Exists(assessWordsFile))
                return Usage($"The --words file '{assessWordsFile}' does not exist.", asJson);
            var assessWords = assessWordsFile is not null
                ? File.ReadAllLines(assessWordsFile)
                : Array.Empty<string>();
            TimeSpan? assessRetrySlowerThan = null;
            if (hasAssessRetrySlowerThan)
            {
                var assessRetrySlowerThanRaw = flags["retry-slower-than"];
                if (!long.TryParse(assessRetrySlowerThanRaw, out var assessRetrySlowerThanMs) ||
                    assessRetrySlowerThanMs < 0)
                {
                    return Usage(AssessUsage(), asJson);
                }
                assessRetrySlowerThan = TimeSpan.FromMilliseconds(assessRetrySlowerThanMs);
            }
            int? assessTimeLimitMs = null;
            if (flags.TryGetValue("time-limit-ms", out var assessTimeLimitRaw))
            {
                if (!int.TryParse(assessTimeLimitRaw, out var parsedTimeLimitMs) || parsedTimeLimitMs <= 0)
                    return Usage(AssessUsage(), asJson);
                assessTimeLimitMs = parsedTimeLimitMs;
            }
            var assessSelection = new SelectionRequest(flags.ContainsKey("all-wordforms"), assessTextIds,
                assessWords, assessRetryFailed, assessRetrySlowerThan, assessRetrySource);
            result = RenderCommand(AssessCommand.Assess(
                new AssessRequest(positionals[0], assessSelection, assessTimeLimitMs),
                asJson ? null : progress => Console.Error.WriteLine(progress.Message)));
            break;

        case "stats":
            if (positionals.Count != 1) return Usage(StatsUsage(), asJson);
            var statsOutput = asJson ? StatsOutputKind.JsonRows : StatsOutputKind.Text;
            if (flags.ContainsKey("proposal")) return Usage(StatsUsage(), asJson);
            result = RenderCommand(StatsCommand.Stats(new StatsRequest(
                positionals[0], flags.GetValueOrDefault("assessment"), statsOutput, forwardedArguments)));
            break;

        case "handoff":
            if (positionals.Count != 1 || !flags.TryGetValue("out", out var handoffOut))
                return Usage(HandoffUsage(), asJson);
            var handoffNoAssess = flags.ContainsKey("no-assess");
            var hasHandoffInvocation = flags.TryGetValue("invocation", out var handoffInvocation);
            if (!handoffNoAssess && !hasHandoffInvocation)
                return Usage(HandoffUsage(), asJson);
            if (hasHandoffInvocation && flags.ContainsKey("texts"))
                return Usage(HandoffUsage(), asJson);
            if (!TryParseGuidList(flags.GetValueOrDefault("texts"), out var handoffTextIds))
                return Usage(HandoffUsage(), asJson);
            // No --texts means every wordform and every Text; a chosen list means only those Texts' words.
            var handoffSelection = new SelectionRequest(
                handoffTextIds.Count == 0, handoffTextIds, Array.Empty<string>(), false, null);
            result = RenderCommand(HandoffCommand.Handoff(
                new HandoffRequest(positionals[0], handoffOut, handoffSelection, !handoffNoAssess, handoffInvocation),
                asJson ? null : progress => Console.Error.WriteLine(progress.Message)));
            break;

        case "jobs":
            if (positionals.Count == 0)
                return Usage(JobsUsage(), asJson);
            switch (positionals[0])
            {
                case "show":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsShowProject))
                        return Usage("Usage: motif jobs show <jobId> --project <fwdata> [--json]", asJson);
                    result = RenderCommand(JobCommands.Show(
                        new ShowJobRequest(jobsShowProject, positionals[1], CliProductVersion())));
                    break;

                case "assessments":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsAssessmentsProject))
                        return Usage("Usage: motif jobs assessments <jobId> --project <fwdata> [--json]", asJson);
                    result = RenderCommand(JobCommands.Assessments(
                        new JobAssessmentsRequest(jobsAssessmentsProject, positionals[1], CliProductVersion())));
                    break;

                case "list":
                    if (positionals.Count != 1 || !flags.ContainsKey("all"))
                        return Usage("Usage: motif jobs list --all [--json]", asJson);
                    result = RenderCommand(JobCommands.ListAll(new ListActiveJobsRequest(CliProductVersion())));
                    break;

                case "cancel":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsCancelProject))
                        return Usage("Usage: motif jobs cancel <jobId> --project <fwdata> [--json]", asJson);
                    result = RenderCommand(JobCommands.Cancel(
                        new CancelJobRequest(jobsCancelProject, positionals[1], CliProductVersion())));
                    break;

                case "requeue":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsRequeueProject))
                        return Usage("Usage: motif jobs requeue <jobId> --project <fwdata> [--json]", asJson);
                    result = RenderCommand(JobCommands.Requeue(
                        new RequeueJobRequest(jobsRequeueProject, positionals[1], CliProductVersion())));
                    if (result.ExitCode == 0) RunnerKick.After();
                    break;

                case "move":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsMoveProject))
                        return Usage(JobsMoveUsage(), asJson);
                    var hasBefore = flags.TryGetValue("before", out var jobsMoveBefore);
                    var hasToTop = flags.ContainsKey("to-top");
                    var hasToBottom = flags.ContainsKey("to-bottom");
                    if ((hasBefore ? 1 : 0) + (hasToTop ? 1 : 0) + (hasToBottom ? 1 : 0) != 1)
                        return Usage(JobsMoveUsage(), asJson);
                    var jobsMoveTarget = hasBefore ? JobMoveTarget.Before(jobsMoveBefore!)
                        : hasToTop ? JobMoveTarget.ToTop()
                        : JobMoveTarget.ToBottom();
                    result = RenderCommand(JobCommands.Move(new MoveJobRequest(
                        jobsMoveProject, positionals[1], CliProductVersion(), jobsMoveTarget)));
                    break;

                default:
                    return Usage(JobsUsage(), asJson);
            }
            break;

        default:
            return Usage($"Unknown command '{verb}'.", asJson, withUsageBanner: true);
    }

    // One process is one call; the machine store is what accumulates a session (ADR 0021 decision 4).
    if (usage.Entries.Count > 0)
    {
        using var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot());
        var machineUsage = new MachineUsageLog(machine);
        foreach (var entry in usage.Entries)
            machineUsage.Append(entry);
    }

    if (result.ExitCode == 0)
    {
        Console.Out.Write(result.Output);
        return result.ExitCode;
    }
    // A caller that asked for JSON gets JSON when it goes wrong too; a Proposal verb already did this itself.
    Console.Error.Write(!alreadyRendered && asJson && result.Reason is { } reason
        ? ProjectionJson.Serialize(new FailureEnvelope(reason, result.Output.Trim())) + Environment.NewLine
        : result.Output);
    return result.ExitCode;
}
catch (Exception ex)
{
    // Nothing decided this; it escaped. That is a bug, not a refusal, and it gets its own code.
    Console.Error.WriteLine($"error: {ex.Message}");
    return FailureEnvelope.ExitCodeFor(FailureReason.StoreInconsistent);
}

int Usage(string message, bool asJson = false, bool withUsageBanner = false)
{
    if (asJson)
    {
        Console.Error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.InvalidArgument, message)));
        return FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument);
    }
    Console.Error.WriteLine(message);
    if (withUsageBanner) PrintUsage(Console.Error, commandPolicy);
    return FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument);
}

static string ConfigUsage() => UsageLineFor("config show");

static string ReportUsage() => UsageLineFor("report");

static string CompareUsage() => UsageLineFor("compare");

static string BaselineUsage() => "Usage: motif " + UsageLineFor("baseline capture");

static string AssessUsage() => "Usage: motif " + UsageLineFor("assess");

static string StatsUsage() => "Usage: motif " + UsageLineFor("stats");

static string HandoffUsage() => "Usage: motif " + UsageLineFor("handoff");

/// <summary>Parses a comma-separated GUID list; an absent flag is an empty list, not a failure.</summary>
static bool TryParseGuidList(string? raw, out List<Guid> guids)
{
    guids = new List<Guid>();
    if (string.IsNullOrEmpty(raw)) return true;

    foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Guid.TryParse(token, out var guid)) return false;
        guids.Add(guid);
    }
    return true;
}

static string JobsUsage() =>
    "Usage: motif jobs show <jobId> --project <fwdata> [--json] OR " +
    "motif jobs assessments <jobId> --project <fwdata> [--json] OR motif jobs list --all [--json] OR " +
    "motif jobs cancel <jobId> --project <fwdata> [--json] OR motif jobs requeue <jobId> --project <fwdata> " +
    "[--json] OR " + JobsMoveUsage();

static string JobsMoveUsage() => UsageLineFor("jobs move");

/// <summary>The one printed usage line CliVerbCatalog holds for a catalogued command name.</summary>
static string UsageLineFor(string commandName)
{
    foreach (var verb in CliVerbCatalog.All)
    {
        if (verb.CommandName == commandName && verb.UsageLines.Count > 0)
            return verb.UsageLines[0];
    }
    throw new InvalidOperationException($"No usage line catalogued for '{commandName}'.");
}

static string AnalysesUsage() =>
    "Usage: motif analyses --project <fwdata> [--json] OR motif analyses --project <fwdata> " +
    "--assessment <assessmentId> --current-selection-sha256 <sha256> " +
    "--current-grammar-sha256 <sha256> [--json]";

static string ResolveCommandName(string[] invocation)
{
    if (invocation.Length == 0) return string.Empty;

    var first = invocation[0];
    if (first is "config" or "baseline" or "jobs")
    {
        var candidate = invocation.Length > 1 ? first + " " + invocation[1] : first;
        if (CommandCatalog.All.Any(command => command.Name == candidate)) return candidate;
        return first;
    }

    if (first is "report" && invocation.Contains("--list-kinds", StringComparer.Ordinal))
        return "report --list-kinds";
    if (first is "dry-run" or "trial" && invocation.Contains("--wait", StringComparer.Ordinal))
        return first + " --wait";
    return first;
}

static int RefuseUnavailableCommand(string commandName, bool asJson)
{
    const string code = "command.not-in-release";
    var message = $"Command '{commandName}' is not part of Motif 0.1.0.";
    if (asJson)
    {
        Console.Error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.Refused, message, code: code)));
    }
    else
    {
        Console.Error.WriteLine("error: " + message);
    }
    return FailureEnvelope.ExitCodeFor(FailureReason.Refused);
}

static void PrintUsage(TextWriter writer, CommandSurfacePolicy policy)
{
    writer.WriteLine("Usage: motif <command> [options]");
    writer.WriteLine();
    PrintSection(writer, "Commands", "Commands:", policy);
    PrintSection(
        writer, "Configuration",
        "Configuration (the declared Assessment scopes and policy beside the project):", policy);
    PrintSection(
        writer, "Reports", "Reports (a presentation of an Assessment's stored evidence; --kind is a registry):", policy);
    PrintSection(
        writer, "Comparison", "Comparison (joins two Assessments on the word; stores and prints the difference):", policy);
    PrintSection(writer, "Corpus", "Corpus (text Motif measures against; never part of the FieldWorks project):", policy);
    PrintSection(
        writer, "Baseline",
        "Baseline (a saved-file capture of a project FieldWorks may hold open, synchronous, no queue):", policy);
    PrintSection(
        writer, "Assess",
        "Assess (a synchronous PanGloss run over a Selection, stored as Assessments; no queue):", policy);
    PrintSection(
        writer, "Handoff",
        "Handoff (the self-explaining AI Handoff folder, written atomically):", policy);
    PrintSection(
        writer, "Jobs", "Jobs (the durable queue; --project selects which project's queue, except list --all):", policy);
    var jsonVerbs = CliVerbCatalog.All
        .Where(verb => verb.UsageLines.Any(line => line.Contains("[--json]", StringComparison.Ordinal)))
        .Where(verb => policy.IsAvailable(CommandCatalog.All.Single(command => command.Name == verb.CommandName)))
        .Select(verb => verb.Verb)
        .Distinct(StringComparer.Ordinal);
    writer.WriteLine("Global options: --json  (structured output; supported by " +
        string.Join('/', jsonVerbs) + ")");
}

/// <summary>Prints one usage banner section: its header, then every catalogued verb's usage line(s).</summary>
static void PrintSection(TextWriter writer, string section, string header, CommandSurfacePolicy policy)
{
    var verbs = CliVerbCatalog.All
        .Where(verb => verb.Section == section)
        .Where(verb => policy.IsAvailable(CommandCatalog.All.Single(command => command.Name == verb.CommandName)))
        .ToList();
    if (verbs.Count == 0) return;

    writer.WriteLine(header);
    foreach (var verb in verbs)
    {
        foreach (var line in verb.UsageLines)
            writer.WriteLine("  " + line);
    }
    writer.WriteLine();
}

/// <summary>Whether the caller said anything at all about what a licence permits.</summary>
static bool HasAnyLicenceFlag(Dictionary<string, string> flags) =>
    flags.ContainsKey("may-derive")
    || flags.ContainsKey("may-redistribute")
    || flags.ContainsKey("may-use-commercially")
    || flags.ContainsKey("requires-attribution")
    || flags.ContainsKey("licence-basis");

static (Dictionary<string, string> Flags, List<string> Positionals, IReadOnlyList<string> Forwarded) ParseArgs(
    string[] tokens)
{
    var flags = new Dictionary<string, string>(StringComparer.Ordinal);
    var positionals = new List<string>();

    for (var i = 0; i < tokens.Length; i++)
    {
        var token = tokens[i];
        // A standalone "--" ends Motif's own vocabulary; everything after it belongs to a forwarded callee.
        if (token == "--")
            return (flags, positionals, tokens[(i + 1)..]);

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var name = token[2..];
            // No value, or one followed by another flag, is a bare switch (e.g. --force): no value starts with "--".
            if (i + 1 >= tokens.Length || tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
                flags[name] = "true";
            else
                flags[name] = tokens[++i];
        }
        else
        {
            positionals.Add(token);
        }
    }

    return (flags, positionals, Array.Empty<string>());
}

// The version this CLI negotiates with; the worker decides compatibility from the protocol range, not this.
static string CliProductVersion() => MotifProductVersion.CurrentText;

static bool IsTruthyFlag(string value) => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

/// <summary>Upserts a named project into the machine store's <c>KnownProjects</c>.</summary>
static void RecordKnownProject(string fwDataPath)
{
    try
    {
        var fullPath = Path.GetFullPath(fwDataPath);
        if (!File.Exists(fullPath)) return;

        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var failure = KnownProjectRecorder.TryRecord(RunnerOptions.ResolveRoot(), project);
        if (failure is not null)
            Console.Error.WriteLine("warning: this project could not be recorded for background work (" +
                failure.Message + "). Queued jobs will not run until it is.");
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
    {
        // Reported, not thrown: an unregistered project is never swept, so silence would hide lost work.
        Console.Error.WriteLine("warning: this project could not be recorded for background work (" +
            exception.Message + "). Queued jobs will not run until it is.");
    }
}
