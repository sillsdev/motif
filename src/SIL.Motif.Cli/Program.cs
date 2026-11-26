using SIL.Motif.Projection;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.Installation;
using SIL.Motif.Host.Parsimony;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Text.RegularExpressions;
using SIL.Motif.Cli;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Records;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Help;
using SIL.Motif.Mcp;

namespace SIL.Motif.Cli;

internal static class Program
{
public static int Main(string[] args)
{
    CrashDialogs.Suppress();
    InterruptSignal.RestoreIfIgnored();
    if (ArtifactInventoryCli.IsInvocation(args))
        return ArtifactInventoryCli.Run(args, Console.Out, Console.Error,
            Environment.GetEnvironmentVariable(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable));
    if (args.Length > 0 && args[0] == "tunnel-supervise")
        return TunnelSuperviseCommand.RunAsync(args[1..]).GetAwaiter().GetResult();
    MotifInstallLifecycle.Initialize(args);

    if (args.Length > 0 && args[0] == "--update-smoke")
        return PackageUpdateSmoke.RunAsync(args).GetAwaiter().GetResult();

    if (args.Length > 0 && args[0] == "mcp")
        return McpCommand.Run(args[1..], Console.Error);

    if (args.Length > 0 && args[0] == "settings")
        return AdvancedAiModeCommand.Run(args[1..], Console.Out, Console.Error);

    if (args.Length > 0 && args[0] == "uninstall")
    {
        Console.WriteLine(MotifInstallLifecycle.RemoveRegistration());
        return 0;
    }

    if (args.Length == 1 && args[0] == "--version")
    {
        Console.WriteLine(MotifProductVersion.DisplayText);
        return 0;
    }

    var formatIndex = Array.LastIndexOf(args, "--format");
    if (args.Length > 0 && args[0] == "guide" && formatIndex >= 0 && formatIndex + 1 < args.Length &&
        args[formatIndex + 1] == "fw-md")
        Console.OutputEncoding = new UTF8Encoding(false);

    var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        environment[(string)entry.Key] = entry.Value?.ToString();
    if (!environment.TryGetValue(FileAdvancedAiModePreferenceStore.PathEnvironmentVariable, out var preferencePath) ||
        string.IsNullOrWhiteSpace(preferencePath))
        environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] =
            FileAdvancedAiModePreferenceStore.DefaultPath;
    return Run(args, Console.Out, Console.Error, environment,
        static (project, reportWarning) => ProcessRunnerLauncher.FromEnvironment().Start(project, reportWarning));
}

/// <summary>The exit code a waited Trial's gate maps to: 0 green, <see cref="FailureEnvelope.TrialLossExitCode"/> loss, the refusal code unmeasured.</summary>
internal static int TrialGateExitCode(TrialGate gate) => gate.State switch
{
    TrialGateState.Green => 0,
    TrialGateState.Loss => FailureEnvelope.TrialLossExitCode,
    _ => FailureEnvelope.ExitCodeFor(FailureReason.Refused),
};

internal static int Run(
    string[] args,
    TextWriter output,
    TextWriter error,
    IReadOnlyDictionary<string, string?> environment,
    Action<string, Action<string>?> startRunner,
    Func<AssessRequest, CommandOutcome<AssessCommandResponse>>? assess = null)
{
    using var ambient = AmbientEnvironment.Push(environment);
    using var storeUpgrades = StoreUpgradeNotices.Begin();
    void NoteStoreUpgrades() => StoreUpgradeReport.Apply(storeUpgrades.Notices, new CommandResult(1, string.Empty), false, error);
    // A leading --autonomous is the global flag; it moves behind the verb so the verb is still args[0].
    if (args is [CommandSurfacePolicy.AutonomousFlag, var leadingVerb, ..var afterVerb])
        args = [leadingVerb, CommandSurfacePolicy.AutonomousFlag, .. afterVerb];
if (ArtifactInventoryCli.IsInvocation(args))
    return ArtifactInventoryCli.Run(args, output, error,
        environment.GetValueOrDefault(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable));

string ResolveRoot() => environment.TryGetValue(RunnerOptions.RootVariable, out var root)
    && !string.IsNullOrWhiteSpace(root) ? root : RunnerOptions.DefaultRoot;

// `<verb> --help` prints the page `help <verb>` does and exits 0, so a probe can trust the code.
if (HelpFlagRewrite(args) is { } helpArguments)
    args = helpArguments;

var usageVerb = args.Length > 0 ? args[0] : "cli";
var usageArguments = args.Length > 0
    ? ParseArgs(args[1..], usageVerb == "query")
    : (new Dictionary<string, string>(StringComparer.Ordinal), new List<string>(),
        (IReadOnlyList<string>)Array.Empty<string>());
usageArguments.Item2 = CliCommandNames.NormalizeQueryWords(usageVerb, usageArguments.Item1, usageArguments.Item2);
var resolvedUsageCommand = args.Length > 0
    ? CliCommandNames.Resolve(usageVerb, usageArguments.Item1, usageArguments.Item2)
    : usageVerb;
var usageCommand = resolvedUsageCommand == "help" ||
    CommandCatalog.All.Any(command => command.Name == resolvedUsageCommand)
        ? resolvedUsageCommand
        : "unknown";
var usageRecorder = UsageRecorder.ForMachineRoot(ResolveRoot());
using var usageAction = usageRecorder.BeginAction(
    usageCommand,
    CliUsageArgumentShapes(usageCommand, usageVerb, usageArguments.Item1,
        usageArguments.Item2, usageArguments.Item3));

var autonomousFlag = usageArguments.Item1.TryGetValue("autonomous", out var autonomousRaw) && IsTruthyFlag(autonomousRaw);
var commandPolicy = CommandSurfacePolicy.FromEnvironment(
    environment.GetValueOrDefault(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable), advancedAiModeEnabled: true,
    autonomousValue: environment.GetValueOrDefault(CommandSurfacePolicy.AutonomousEnvironmentVariable),
    autonomousFlag: autonomousFlag);

if (args.Length > 0 && args[0] == "help")
    return HelpCommand.Run(args[1..], commandPolicy, output, error);

if (args.Length == 0)
{
    PrintUsage(error, commandPolicy);
    return 1;
}

var verb = args[0];
var rest = args[1..];
var asJson = Array.IndexOf(args, "--json") >= 0;
var HypothesisFileOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
};
string? namedProject = null;

try
{
    var (flags, parsedPositionals, forwardedArguments) = ParseArgs(rest, verb == "query");
    // Pinned by `ASuccessfulJsonVerbPrintsExactlyOneDocumentAndPickKeepsTheNamedFields`.
    var pickSpec = flags.Remove("pick", out var pickValue) ? pickValue : null;
    var positionals = CliCommandNames.NormalizeQueryWords(verb, flags, parsedPositionals);
    var waitForTerminal = flags.ContainsKey("wait-terminal");
    if (waitForTerminal && !(verb is "dry-run" or "trial" && flags.ContainsKey("wait")) &&
        !(verb == "jobs" && positionals.FirstOrDefault() == "wait"))
        return Usage("Use --wait-terminal with dry-run --wait, trial --wait, or jobs wait.", asJson);
    if (flags.ContainsKey("latest") && !(verb == "proposal" && positionals.FirstOrDefault() == "trial"))
        return Usage("Use --latest with proposal trial.", asJson);
    namedProject = flags.TryGetValue("project", out var flaggedProject) ? flaggedProject
        : verb == "open" && positionals.Count == 1 ? positionals[0] : null;
    var commandName = CliCommandNames.Resolve(verb, flags, positionals);
    var command = CommandCatalog.All.FirstOrDefault(item => item.Name == commandName);
    // A switch that promises nothing commits after the command returns must never be ignored or half-read.
    if (flags.Keys.Any(key => key.StartsWith("no-background", StringComparison.Ordinal)) &&
        (flags.Keys.Any(key => key.StartsWith("no-background", StringComparison.Ordinal) && key != "no-background") ||
         verb is not ("baseline" or "assess" or "dry-run" or "trial" or "apply") ||
         (verb == "baseline" && positionals.FirstOrDefault() != "capture") ||
         (verb == "trial" && flags.ContainsKey("pending"))))
        return Usage("--no-background is a switch without a value, taken by baseline capture, assess, dry-run, " +
            "trial and apply.", flags.ContainsKey("json") || asJson);
    if (command is not null && !commandPolicy.IsAvailable(command))
        return RefuseUnavailableCommand(commandName, flags.ContainsKey("json"), error, command);

    if ((verb == "apply" || flags.ContainsKey("apply")) &&
        ApplyAttributionCheck.Refusal(ApplyAttributionFromFlags(flags, environment), flags.ContainsKey("force"))
            is { } attributionRefusal)
    {
        var refusal = ProposalCommandRenderer.Render(
            CommandOutcome<ApplyPendingResult>.Refused(attributionRefusal), flags.ContainsKey("json"));
        error.Write(refusal.Output);
        return refusal.ExitCode;
    }

    // Every invocation naming a project upserts it into the machine store (ADR 0023 decision 4).
    if (flags.TryGetValue("project", out var projectForRegistry) &&
        commandName is not ("project initialize" or "project tag" or "project tag --set" or "hand-analysis check"))
    {
        var recordFailure = RecordKnownProject(projectForRegistry, ResolveRoot(), error);
        // An unrecorded project's jobs never run: refuse now.
        if (recordFailure is ProjectTagException tagFailure && QueuesJobs(verb, positionals))
            return RefuseProjectTag(tagFailure, flags.ContainsKey("json"), error);
    }

    // Every migrated read surface renders both ways from one projection (ADR 0018 decision 1).
    asJson = flags.ContainsKey("json");
    CommandResult result;
    // RenderProposal below already fully renders text or JSON; the bottom printer must not wrap it again.
    var alreadyRendered = false;

    CommandResult RenderProposal<T>(CommandOutcome<T> outcome, bool successAsJson = true,
        bool includeEffects = false) where T : class
    {
        alreadyRendered = true;
        return ProposalCommandRenderer.Render(outcome, asJson, successAsJson, includeEffects);
    }

    CommandResult RenderCommand<T>(CommandOutcome<T> outcome, bool successAsJson = true,
        bool includeEffects = false) where T : class?
    {
        alreadyRendered = true;
        return CommandTextRenderer.Render(outcome, asJson, successAsJson, includeEffects);
    }

    // Pinned by `ASuccessfulJsonVerbPrintsExactlyOneDocumentAndPickKeepsTheNamedFields`.
    var pickRefused = false;
    CommandResult Picked(CommandResult printed)
    {
        // A failing command keeps its own failure; projecting its text would replace the real error.
        if (pickSpec is null || !asJson || printed.ExitCode != 0) return printed;
        var picked = PickProjection.Apply(printed, pickSpec);
        if (picked.ExitCode == 0) return picked;
        pickRefused = true;
        error.Write(picked.Output);
        return picked with { Output = "" };
    }
    if (pickSpec is not null && !asJson)
    {
        error.Write(PickProjection.NeedsJson().Output);
        return FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument);
    }

    CommandOutcome<ApplyPendingResult> WriteApplyEffectsArtifact(
        CommandOutcome<ApplyPendingResult> outcome, string projectPath)
    {
        if (!outcome.Succeeded || outcome.Value?.Receipt is not { } receipt) return outcome;
        try
        {
            var path = EffectsArtifactWriter.WriteApply(ResolveRoot(), projectPath, receipt.ProposalId,
                receipt.EffectDigest, receipt.Effects);
            return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(
                receipt with { EffectsFile = path }, outcome.Value.Summary));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(
                receipt with { Warnings = receipt.Warnings.Append("The full effects file could not be written.").ToArray() },
                outcome.Value.Summary));
        }
    }

    CommandOutcome<DryRunProjection> WriteDryRunEffectsArtifact(
        CommandOutcome<DryRunProjection> outcome, string projectPath, string jobId)
    {
        if (!outcome.Succeeded || outcome.Value is null) return outcome;
        try
        {
            var path = EffectsArtifactWriter.WriteDryRun(ResolveRoot(), projectPath, jobId, outcome.Value);
            return CommandOutcome<DryRunProjection>.Success(outcome.Value with { EffectsFile = path });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandOutcome<DryRunProjection>.Success(outcome.Value with
            {
                Warnings = outcome.Value.Warnings.Append("The full effects file could not be written.").ToArray(),
            });
        }
    }

    // --apply stages the change, then applies every pending change like apply --all-pending.
    CommandResult StageSource<TStaged>(string projectPath, Func<CommandOutcome<TStaged>> stage) where TStaged : class
    {
        if (!flags.ContainsKey("apply")) return RenderCommand(stage());
        var attribution = ApplyAttributionFromFlags(flags, environment);
        if (ApplyAttributionCheck.Refusal(attribution) is { } refusal)
            return RenderProposal(CommandOutcome<ApplyPendingResult>.Refused(refusal));
        var staged = stage();
        if (!staged.Succeeded) return RenderCommand(staged);
        var request = new ApplyPendingRequest(projectPath, DraftId: null, Revision: null, Attribution: attribution);
        var applied = RunWithConsoleCancellation(cancellationToken => RenderProposal(
            WriteApplyEffectsArtifact(PendingChangesWorkflow.Apply(request, cancellationToken), projectPath),
            includeEffects: flags.ContainsKey("effects")));
        if (applied.ExitCode == 0) startRunner(projectPath, error.WriteLine);
        return applied;
    }

    var hasCitationFlag = flags.ContainsKey("cite-source") || flags.ContainsKey("cite-locator");
    SIL.Motif.Contract.Model.OperationCitation? commandCitation = null;
    if (hasCitationFlag)
    {
        var citationSupported = verb is "add-set-gloss" or "add-delete-lexeme-form" or "promote-gloss" ||
                                ProposalCommands.DraftComposers.ContainsKey(verb);
        if (!citationSupported)
            return Usage("--cite-source and --cite-locator are available on Draft-authoring commands.", asJson);
        if (!TryReadCitation(flags, out commandCitation, out var citationError))
            return Usage(citationError!, asJson);
    }

    CommandOutcome<ComposedOperationsResponse> ComposeWithCitation(string project, string draft, string intent,
        Func<CommandOutcome<ComposedOperationsResponse>> compose) => commandCitation is null
        ? compose()
        : ProposalCommands.ComposeIntent(new(project, CliProductVersion(), draft, verb, intent, commandCitation));

    // A flag that was renamed names its replacement instead of failing as an unknown option.
    if (verb != "phonology-examples" && flags.ContainsKey("step-cap"))
        return Usage("--step-cap was renamed --parser-work-limit <work|unbounded>.", asJson);

    switch (verb)
    {
        case var typedVerb when ProposalCommands.ComposerDescriptors.Any(composer => composer.Verb == typedVerb):
            if (typedVerb == "compose-edit-text-notes" && flags.TryGetValue("notes-tsv", out var notesTsv))
            {
                if (flags.ContainsKey("intent"))
                    return Usage("Give --notes-tsv or --intent, not both.", asJson);
                if (!File.Exists(notesTsv)) return Usage($"The --notes-tsv file '{notesTsv}' does not exist.", asJson);
                try
                {
                    flags["intent"] = SIL.Motif.Runner.Composers.EditTextNotesIntentParser
                        .FromTsv(File.ReadAllText(notesTsv), flags.GetValueOrDefault("analysis-ws")).GetRawText();
                }
                catch (SIL.Motif.Contract.Parsing.ContractParseException exception)
                {
                    return Usage("The --notes-tsv file is not usable: " + exception.Message, asJson);
                }
            }
            if (typedVerb == "compose-set-entry-attribution" && flags.TryGetValue("entries-file", out var entriesFile))
            {
                if (flags.ContainsKey("intent"))
                    return Usage("Give --entries-file or --intent, not both.", asJson);
                if (!flags.TryGetValue("tag", out var attributionTag))
                    return Usage("--entries-file needs --tag '<source>@<revision> <licence>'.", asJson);
                if (!File.Exists(entriesFile)) return Usage($"The --entries-file '{entriesFile}' does not exist.", asJson);
                try
                {
                    flags["intent"] = SIL.Motif.Runner.Composers.SetEntryAttributionIntentParser
                        .FromList(File.ReadAllText(entriesFile), attributionTag, flags.GetValueOrDefault("analysis-ws")).GetRawText();
                }
                catch (SIL.Motif.Contract.Parsing.ContractParseException exception)
                {
                    return Usage("The --entries-file is not usable: " + exception.Message, asJson);
                }
            }
            if (!flags.TryGetValue("draft", out var typedDraft) ||
                !flags.TryGetValue("project", out var typedProject) ||
                !flags.TryGetValue("intent", out var typedIntent))
                return Usage("Usage: motif " + CliVerbCatalog.ComposerUsage(typedVerb), asJson);
            result = RenderProposal(
                ProposalCommands.ComposeIntent(new(typedProject, CliProductVersion(), typedDraft, typedVerb,
                    typedIntent, commandCitation)));
            break;

        case "open":
            if (positionals.Count != 1)
                return Usage("Usage: motif open <path-to-.fwdata> [--json]", asJson);
            result = RenderProposal(ProposalCommands.Open(new OpenRequest(positionals[0])));
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
                            currentGrammarSha256!)))
                : RenderProposal(ProposalCommands.Analyses(new ManualAnalysesRequest(analysesProject)));
            break;

        case "new":
            if (!flags.TryGetValue("project", out var newProject) || !flags.TryGetValue("draft", out var newDraftName))
                return Usage("Usage: motif new --project <fwdata> --draft <name> [--label <text>] [--request <requestId>] [--json]", asJson);
            result = RenderProposal(
                ProposalCommands.New(new NewDraftRequest(
                    newProject, CliProductVersion(), newDraftName, flags.GetValueOrDefault("label"),
                    flags.GetValueOrDefault("request"))));
            break;

        case "import":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var importProject) ||
                !flags.TryGetValue("file", out var importFile) ||
                flags.Keys.Any(key => key is not ("project" or "file" or "draft" or "page" or "strict" or "json")) ||
                flags.TryGetValue("draft", out var importDraft) && importDraft == "true" ||
                flags.ContainsKey("strict") && !flags.ContainsKey("draft"))
                return Usage(UsageLineFor("import"), asJson);
            var importPage = 1;
            if (flags.TryGetValue("page", out var importPageText) &&
                (!int.TryParse(importPageText, NumberStyles.None, CultureInfo.InvariantCulture, out importPage) || importPage < 1))
                return Usage(UsageLineFor("import"), asJson);
            result = RenderCommand(RecordImportCommand.Run(new RecordImportRequest(
                importProject, importFile, flags.GetValueOrDefault("draft"), importPage, flags.ContainsKey("strict"))));
            break;

        case "record":
            if (positionals is not ["export"] || !flags.TryGetValue("project", out var recordExportProject) ||
                !flags.TryGetValue("out", out var recordExportOut) || recordExportOut is "true" or "" ||
                flags.Keys.Any(key => key is not ("project" or "out" or "record" or "all" or "kind" or "json")) ||
                flags.TryGetValue("all", out var recordExportAll) && recordExportAll != "true" ||
                flags.TryGetValue("record", out var recordExportId) && recordExportId == "true" ||
                flags.TryGetValue("kind", out var recordExportKind) && recordExportKind == "true")
                return Usage(UsageLineFor("record export"), asJson);
            result = RenderCommand(RecordExportCommand.Run(new RecordExportRequest(recordExportProject, recordExportOut,
                flags.GetValueOrDefault("record"), flags.GetValueOrDefault("kind"), flags.ContainsKey("all"))));
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
                    "--ws <wsTag> --text <text> [--depends-on <opId>[,<opId>...]] " +
                    "[--cite-source <text> [--cite-locator <text>]]", asJson);
            }
            var addDependsOn = flags.TryGetValue("depends-on", out var addDependsOnRaw)
                ? addDependsOnRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            result = RenderProposal(
                ProposalCommands.AddSetGloss(new AddSetGlossRequest(
                    addProject, CliProductVersion(), addDraftName, addTarget, addWs, addText, addDependsOn),
                    commandCitation),
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
                    new AddDeleteLexemeFormRequest(addDelProject, CliProductVersion(), addDelDraftName, addDelTarget),
                    commandCitation),
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
                ComposeWithCitation(composeProject, composeDraftName, composeIntent,
                    () => ProposalCommands.ComposeAuthorLexemeForm(new ComposeAuthorLexemeFormRequest(
                        composeProject, CliProductVersion(), composeDraftName, composeIntent))),
                successAsJson: false);
            break;

        case "compose-author-phoneme":
        case "compose-author-natural-class":
        case "compose-author-environment":
        case "compose-author-phonological-rule":
        case "compose-author-metathesis-rule":
        case "compose-author-affix-slot":
        case "compose-author-part-of-speech":
        case "compose-author-inflection-feature":
        case "compose-author-inflection-class":
        case "compose-author-stem-name":
        case "compose-author-lexical-entry":
        case "compose-author-alternate-form":
        case "compose-author-affix-template":
        case "compose-edit-natural-class":
        case "compose-relink-natural-class":
            if (!flags.TryGetValue("draft", out var soundDraft) ||
                !flags.TryGetValue("project", out var soundProject) ||
                !flags.TryGetValue("intent", out var soundIntent))
                return Usage("Usage: motif " + verb + " --draft <name> --project <fwdata> --intent '<closed intent JSON>'", asJson);
            var soundOutcome = ComposeWithCitation(soundProject, soundDraft, soundIntent, () => verb switch
            {
                "compose-author-phoneme" => ProposalCommands.ComposeAuthorPhoneme(new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-natural-class" => ProposalCommands.ComposeAuthorNaturalClass(new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-phonological-rule" => ProposalCommands.ComposeAuthorPhonologicalRule(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-metathesis-rule" => ProposalCommands.ComposeAuthorMetathesisRule(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-inflection-feature" => ProposalCommands.ComposeAuthorInflectionFeature(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-inflection-class" => ProposalCommands.ComposeAuthorInflectionClass(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-stem-name" => ProposalCommands.ComposeAuthorStemName(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-lexical-entry" => ProposalCommands.ComposeAuthorLexicalEntry(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-alternate-form" => ProposalCommands.ComposeAuthorAlternateForm(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-part-of-speech" => ProposalCommands.ComposeAuthorPartOfSpeech(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-affix-slot" => ProposalCommands.ComposeAuthorAffixSlot(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-affix-template" => ProposalCommands.ComposeAuthorAffixTemplate(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-edit-natural-class" => ProposalCommands.ComposeEditNaturalClass(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-relink-natural-class" => ProposalCommands.ComposeRelinkNaturalClass(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                "compose-author-environment" => ProposalCommands.ComposeAuthorEnvironment(
                    new(soundProject, CliProductVersion(), soundDraft, soundIntent)),
                _ => throw new InvalidOperationException($"'{verb}' has a compose case label but no composer arm."),
            });
            result = RenderProposal(soundOutcome, successAsJson: false);
            break;

        case "compose-batch":
            if (!flags.TryGetValue("draft", out var batchDraft) ||
                !flags.TryGetValue("project", out var batchProject) ||
                !flags.TryGetValue("intents", out var batchFile))
                return Usage("Usage: motif compose-batch --draft <name> --project <fwdata> --intents <file.json> [--json]",
                    asJson);
            if (!File.Exists(batchFile))
                return Usage($"The --intents file '{batchFile}' does not exist.", asJson);
            result = RenderProposal(ProposalCommands.ComposeBatch(new ComposeBatchRequest(
                batchProject, CliProductVersion(), batchDraft, File.ReadAllText(batchFile),
                Path.GetDirectoryName(Path.GetFullPath(batchFile)))));
            break;

        case "compose":
            if (!flags.TryGetValue("proposal", out var callComposeProposal) ||
                !flags.TryGetValue("project", out var callComposeProject) ||
                !flags.TryGetValue("steps", out var callComposeStepsJson) || positionals.Count != 0)
                return Usage("Usage: motif compose --proposal <name> --project <fwdata> --steps '<json array>' " +
                    "[--remove <id>[,<id>...]] [--json]  (--proposal names a Draft or a Proposal)", asJson);
            IReadOnlyList<ComposeStepRequest> callComposeSteps;
            try
            {
                callComposeSteps = ComposeStepRequest.ParseList(callComposeStepsJson);
            }
            catch (InvalidOperationException exception)
            {
                return Usage("The --steps value is not usable: " + exception.Message, asJson);
            }
            var callComposeRemove = flags.TryGetValue("remove", out var callComposeRemoveText)
                ? callComposeRemoveText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            result = RenderProposal(ProposalCommands.Compose(new ComposeRequest(
                callComposeProject, CliProductVersion(), callComposeProposal, callComposeSteps, callComposeRemove)));
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
            result = RenderProposal(ComposeWithCitation(composeFsProject, composeFsDraftName, composeFsIntent,
                () => ProposalCommands.ComposeAuthorFeatureStructure(new ComposeAuthorFeatureStructureRequest(
                    composeFsProject, CliProductVersion(), composeFsDraftName, composeFsIntent))), successAsJson: false);
            break;

        case "compose-edit-adhoc-prohibition":
            if (!flags.TryGetValue("draft", out var adhocDraft) ||
                !flags.TryGetValue("project", out var adhocProject) ||
                !flags.TryGetValue("intent", out var adhocIntent))
                return Usage("Usage: motif compose-edit-adhoc-prohibition --draft <name> " +
                    "--project <fwdata> --intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(adhocProject, adhocDraft, adhocIntent,
                () => ProposalCommands.ComposeEditAdhocProhibition(new ComposeEditAdhocProhibitionRequest(
                    adhocProject, CliProductVersion(), adhocDraft, adhocIntent))), successAsJson: false);
            break;

        case "compose-edit-affix-slot":
            if (!flags.TryGetValue("draft", out var affixSlotDraft) ||
                !flags.TryGetValue("project", out var affixSlotProject) ||
                !flags.TryGetValue("intent", out var affixSlotIntent))
                return Usage("Usage: motif compose-edit-affix-slot --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(affixSlotProject, affixSlotDraft, affixSlotIntent,
                () => ProposalCommands.ComposeEditAffixSlot(new ComposeEditAffixSlotRequest(
                    affixSlotProject, CliProductVersion(), affixSlotDraft, affixSlotIntent))), successAsJson: false);
            break;

        case "compose-edit-affix-template":
            if (!flags.TryGetValue("draft", out var affixTemplateDraft) ||
                !flags.TryGetValue("project", out var affixTemplateProject) ||
                !flags.TryGetValue("intent", out var affixTemplateIntent))
                return Usage("Usage: motif compose-edit-affix-template --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(affixTemplateProject, affixTemplateDraft, affixTemplateIntent,
                () => ProposalCommands.ComposeEditAffixTemplate(new ComposeEditAffixTemplateRequest(
                    affixTemplateProject, CliProductVersion(), affixTemplateDraft, affixTemplateIntent))), successAsJson: false);
            break;

        case "compose-edit-inflectional-affix":
            if (!flags.TryGetValue("draft", out var inflectionalAffixDraft) ||
                !flags.TryGetValue("project", out var inflectionalAffixProject) ||
                !flags.TryGetValue("intent", out var inflectionalAffixIntent))
                return Usage("Usage: motif compose-edit-inflectional-affix --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(inflectionalAffixProject, inflectionalAffixDraft,
                inflectionalAffixIntent, () => ProposalCommands.ComposeEditInflectionalAffix(
                    new ComposeEditInflectionalAffixRequest(inflectionalAffixProject, CliProductVersion(),
                        inflectionalAffixDraft, inflectionalAffixIntent))), successAsJson: false);
            break;

        case "compose-edit-allomorph-condition":
            if (!flags.TryGetValue("draft", out var allomorphConditionDraft) ||
                !flags.TryGetValue("project", out var allomorphConditionProject) ||
                !flags.TryGetValue("intent", out var allomorphConditionIntent))
                return Usage("Usage: motif compose-edit-allomorph-condition --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(allomorphConditionProject, allomorphConditionDraft,
                allomorphConditionIntent, () => ProposalCommands.ComposeEditAllomorphCondition(
                    new ComposeEditAllomorphConditionRequest(allomorphConditionProject, CliProductVersion(),
                        allomorphConditionDraft, allomorphConditionIntent))), successAsJson: false);
            break;

        case "compose-author-text":
            if (!flags.TryGetValue("draft", out var authorTextDraft) ||
                !flags.TryGetValue("project", out var authorTextProject) ||
                !flags.TryGetValue("intent", out var authorTextIntent))
                return Usage("Usage: motif compose-author-text --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(authorTextProject, authorTextDraft, authorTextIntent,
                () => ProposalCommands.ComposeAuthorText(new ComposeAuthorTextRequest(
                    authorTextProject, CliProductVersion(), authorTextDraft, authorTextIntent))), successAsJson: false);
            break;

        case "compose-edit-analysis-decision":
            if (!flags.TryGetValue("draft", out var decisionDraft) ||
                !flags.TryGetValue("project", out var decisionProject) ||
                !flags.TryGetValue("intent", out var decisionIntent))
                return Usage("Usage: motif compose-edit-analysis-decision --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(decisionProject, decisionDraft, decisionIntent,
                () => ProposalCommands.ComposeEditAnalysisDecision(new ComposeEditAnalysisDecisionRequest(
                    decisionProject, CliProductVersion(), decisionDraft, decisionIntent))), successAsJson: false);
            break;

        case "order-allomorphs":
            if (!flags.TryGetValue("draft", out var orderAllomorphsDraft) ||
                !flags.TryGetValue("project", out var orderAllomorphsProject) ||
                !flags.TryGetValue("intent", out var orderAllomorphsIntent))
                return Usage("Usage: motif order-allomorphs --draft <name> --project <fwdata> " +
                    "--intent '<closed intent JSON>'", asJson);
            result = RenderProposal(ComposeWithCitation(orderAllomorphsProject, orderAllomorphsDraft,
                orderAllomorphsIntent, () => ProposalCommands.ComposeOrderAllomorphs(
                    new ComposeOrderAllomorphsRequest(orderAllomorphsProject, CliProductVersion(),
                        orderAllomorphsDraft, orderAllomorphsIntent))), successAsJson: false);
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
                    promoteCorpus, flags.GetValueOrDefault("document")), commandCitation),
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
                return Usage("Usage: motif discard-draft --project <fwdata> --draft <name> [--revision <r>]", asJson);
            result = RenderProposal(
                ProposalCommands.DiscardNamedDraft(
                    new DiscardDraftRequest(discardProject, CliProductVersion(), discardDraftName,
                        flags.GetValueOrDefault("revision"))),
                successAsJson: false);
            break;

        case "discard-empty-drafts":
            if (!flags.TryGetValue("project", out var emptyDraftsProject))
                return Usage("Usage: motif discard-empty-drafts --project <fwdata> [--prefix <text>]", asJson);
            result = RenderProposal(
                ProposalCommands.DiscardEmptyDrafts(
                    new DiscardEmptyDraftsRequest(emptyDraftsProject, CliProductVersion(), flags.GetValueOrDefault("prefix"))),
                successAsJson: false);
            break;

        case "reopen" when flags.ContainsKey("proposal"):
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var reopenProposalProject) ||
                string.IsNullOrWhiteSpace(flags["proposal"]))
                return Usage(UsageLineFor("reopen --proposal"), asJson);
            result = RenderCommand(ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(reopenProposalProject,
                flags["proposal"])));
            break;

        case "reject" when flags.ContainsKey("proposal"):
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var declineProject) ||
                string.IsNullOrWhiteSpace(flags["proposal"]))
                return Usage(UsageLineFor("reject --proposal"), asJson);
            result = RenderCommand(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(declineProject,
                flags["proposal"])));
            break;

        case "withdraw":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var withdrawProject) ||
                !flags.TryGetValue("proposal", out var withdrawProposal) || string.IsNullOrWhiteSpace(withdrawProposal))
                return Usage(UsageLineFor("withdraw"), asJson);
            result = RenderCommand(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(withdrawProject,
                withdrawProposal)));
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

        case "undefer":
            if (!flags.TryGetValue("project", out var undeferProject) || positionals.Count != 1)
                return Usage("Usage: motif " + UsageLineFor("undefer"), asJson);
            result = RenderProposal(
                ProposalCommands.Undefer(new UndeferRequest(undeferProject, CliProductVersion(), positionals[0])),
                successAsJson: false);
            break;

        case "reject":
            if (!flags.TryGetValue("project", out var rejectProject) || positionals.Count != 1)
                return Usage("Usage: motif reject --project <fwdata> <proposalId>", asJson);
            result = RenderProposal(
                ProposalLifecycleCommand.Decline(new DeclineProposalRequest(rejectProject, positionals[0])),
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
            if (positionals is ["hypotheses"])
            {
                if (!flags.TryGetValue("project", out var listHypothesesProject) ||
                    !int.TryParse(flags.GetValueOrDefault("offset") ?? "0", out var hypothesesOffset) ||
                    !int.TryParse(flags.GetValueOrDefault("limit") ?? "50", out var hypothesesLimit))
                    return Usage(UsageLineFor("list hypotheses"), asJson);
                result = RenderCommand(HypothesisCommands.List(new ListHypothesesRequest(listHypothesesProject,
                    CliProductVersion(), flags.GetValueOrDefault("status"), flags.GetValueOrDefault("id"),
                    hypothesesOffset, hypothesesLimit)));
                break;
            }
            if (positionals is ["requests"])
            {
                if (!flags.TryGetValue("project", out var listRequestsProject) ||
                    !int.TryParse(flags.GetValueOrDefault("offset") ?? "0", out var requestsOffset) ||
                    !int.TryParse(flags.GetValueOrDefault("limit") ?? "25", out var requestsLimit))
                    return Usage(UsageLineFor("list requests"), asJson);
                result = RenderCommand(RequestCommands.List(new ListRequestsRequest(listRequestsProject,
                    CliProductVersion(), flags.GetValueOrDefault("status"), requestsOffset, requestsLimit)));
                break;
            }
            if (!flags.TryGetValue("project", out var listProject) ||
                flags.TryGetValue("status", out var listStatus) && string.IsNullOrWhiteSpace(listStatus))
                return Usage("Usage: motif " + UsageLineFor("list"), asJson);
            var (listWindow, listPagingRefusal) = PagingFlags.Read(flags, ResultPaging.DefaultLimit, allowAll: false);
            if (listPagingRefusal is not null)
            {
                result = RenderProposal(CommandOutcome<ProposalListProjection>.Refused(listPagingRefusal));
                break;
            }
            result = RenderProposal(
                ProposalCommands.List(new ListProposalsRequest(listProject, CliProductVersion(),
                    flags.ContainsKey("all"), flags.GetValueOrDefault("status"))).Map(
                    list => list.Window(listWindow.Offset, listWindow.Limit).WithNextStep(
                        next => PagingFlags.NextCommand("list", [], flags, next, listWindow.Limit, keepAll: true))));
            break;

        case "show":
            if (!flags.TryGetValue("project", out var showProject) || positionals.Count != 1)
                return Usage("Usage: motif " + UsageLineFor("show"), asJson);
            var (showWindow, showPagingRefusal) = PagingFlags.Read(flags, ResultPaging.HeavyRowLimit);
            if (showPagingRefusal is not null)
            {
                result = RenderProposal(CommandOutcome<ProposalDetailProjection>.Refused(showPagingRefusal));
                break;
            }
            result = RenderProposal(
                ProposalCommands.Show(new ShowProposalRequest(showProject, CliProductVersion(), positionals[0],
                    flags.ContainsKey("current"))).Map(detail => showWindow.All
                    ? (asJson ? detail.WithTotal() : detail)
                    : asJson && !flags.ContainsKey("offset") && !flags.ContainsKey("limit") && !flags.ContainsKey("page")
                    ? detail.WithTotal() : detail.Window(showWindow.Offset, showWindow.Limit).WithNextStep(
                        next => PagingFlags.NextCommand("show", positionals, flags, next, showWindow.Limit))));
            break;

        case "pending-changes":
            if (!flags.TryGetValue("project", out var pendingProject))
                return Usage("Usage: motif pending-changes --project <fwdata> [--json]", asJson);
            result = RenderProposal(PendingChanges.Load(new PendingChangesRequest(
                pendingProject, CliProductVersion())));
            break;

        case "put-pending-change":
            if (!flags.TryGetValue("project", out var putProject) ||
                !flags.TryGetValue("expected-revision", out var putRevision) ||
                !flags.TryGetValue("change-id", out var putId) ||
                !flags.TryGetValue("kind", out var putKind) ||
                !flags.TryGetValue("word", out var putWord))
                return Usage("Usage: motif put-pending-change --project <fwdata> " +
                    "--expected-revision <revision> --change-id <id> --kind <kind> --word <word> " +
                    "[--wordform-id <id>] [--assessment <id> --reading-index <zero-based> " +
                    "--reading-json <json>] [--stored-analysis-id <id>] " +
                    "[--occurrence-text-id <guid> --occurrence-paragraph-id <guid> " +
                    "--occurrence-segment-id <guid> --occurrence-index <zero-based>] " +
                    "[--expected-context <json>] [--json]", asJson);
            if (!TryParseExpectedContext(flags.GetValueOrDefault("expected-context"), out var putExpectedContext))
                return Usage("--expected-context must contain one ExpectedContext object.", asJson);
            ParseAnalysis? chosenReading = null;
            if (flags.TryGetValue("reading-json", out var readingJson))
            {
                try { chosenReading = System.Text.Json.JsonSerializer.Deserialize<ParseAnalysis>(readingJson); }
                catch (System.Text.Json.JsonException)
                {
                    return Usage("--reading-json must contain one ParseAnalysis object.", asJson);
                }
            }
            int? readingIndex = null;
            if (flags.TryGetValue("reading-index", out var indexText))
            {
                if (!int.TryParse(indexText, out var parsedIndex) || parsedIndex < 0)
                    return Usage("--reading-index must be a zero-based nonnegative integer.", asJson);
                readingIndex = parsedIndex;
            }
            var occurrenceFlags = new[]
            {
                "occurrence-text-id", "occurrence-paragraph-id", "occurrence-segment-id", "occurrence-index",
            };
            var hasOccurrence = occurrenceFlags.Any(flags.ContainsKey);
            OccurrenceAnchor? occurrence = null;
            if (hasOccurrence)
            {
                if (occurrenceFlags.Any(name => !flags.ContainsKey(name)) ||
                    !Guid.TryParse(flags.GetValueOrDefault("occurrence-text-id"), out var textId) ||
                    !Guid.TryParse(flags.GetValueOrDefault("occurrence-paragraph-id"), out var paragraphId) ||
                    !Guid.TryParse(flags.GetValueOrDefault("occurrence-segment-id"), out var segmentId) ||
                    !int.TryParse(flags.GetValueOrDefault("occurrence-index"), out var occurrenceIndex) ||
                    occurrenceIndex < 0)
                    return Usage("Occurrence requires --occurrence-text-id, --occurrence-paragraph-id, " +
                        "--occurrence-segment-id, and a nonnegative --occurrence-index.", asJson);
                occurrence = new OccurrenceAnchor(textId, paragraphId, segmentId, occurrenceIndex);
            }
            result = RenderProposal(PendingChanges.Put(new PutPendingChangeRequest(
                putProject, CliProductVersion(), putRevision,
                new ChangeIntent(putId, putKind, flags.GetValueOrDefault("wordform-id") ?? "",
                    putWord, flags.GetValueOrDefault("assessment"), chosenReading,
                    flags.GetValueOrDefault("stored-analysis-id"), flags.GetValueOrDefault("display-reading"),
                    readingIndex, Occurrence: occurrence)) { ExpectedContext = putExpectedContext }));
            break;

        case "remove-analysis":
            var hasSingleAnalysis = flags.TryGetValue("analysis-id", out var removeAnalysisId);
            var hasAnalysisSelection = flags.TryGetValue("analysis-ids", out var removeAnalysisIdsText);
            var hasAnalysisText = flags.TryGetValue("text-id", out var removeAnalysisTextId);
            var removeAnalysisIds = hasAnalysisSelection
                ? removeAnalysisIdsText!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            if (!flags.TryGetValue("project", out var removeAnalysisProject) ||
                !flags.TryGetValue("expected-revision", out var removeAnalysisRevision) ||
                (hasSingleAnalysis ? 1 : 0) + (hasAnalysisSelection ? 1 : 0) + (hasAnalysisText ? 1 : 0) != 1 ||
                hasSingleAnalysis && (!flags.ContainsKey("change-id") || !flags.ContainsKey("wordform-id") ||
                    !flags.ContainsKey("word")) ||
                hasAnalysisSelection && removeAnalysisIds!.Length == 0 ||
                hasAnalysisText && !Guid.TryParse(removeAnalysisTextId, out _))
                return Usage("Usage: motif remove-analysis --project <fwdata> " +
                    "--expected-revision <revision> (--analysis-id <id> --change-id <id> " +
                    "--wordform-id <id> --word <word> | --analysis-ids <id,id,...> | --text-id <guid>) [--json]",
                    asJson);
            result = RenderProposal(PendingChanges.RemoveAnalysis(new RemoveAnalysisRequest(
                removeAnalysisProject, CliProductVersion(), removeAnalysisRevision,
                flags.GetValueOrDefault("change-id"), flags.GetValueOrDefault("wordform-id"),
                flags.GetValueOrDefault("word"), removeAnalysisId, removeAnalysisIds,
                hasAnalysisText ? Guid.Parse(removeAnalysisTextId!) : null)));
            break;

        case "accept-new-set":
            var hasWordformScope = flags.TryGetValue("wordform-id", out var acceptWordformId);
            var hasTextScope = flags.TryGetValue("text-id", out var acceptTextId);
            var hasSelectionScope = flags.ContainsKey("selection");
            if (!flags.TryGetValue("project", out var acceptProject) ||
                !flags.TryGetValue("expected-revision", out var acceptRevision) ||
                !flags.TryGetValue("assessment", out var acceptAssessment) ||
                (hasWordformScope ? 1 : 0) + (hasTextScope ? 1 : 0) + (hasSelectionScope ? 1 : 0) != 1 ||
                hasTextScope && !Guid.TryParse(acceptTextId, out _))
                return Usage("Usage: motif accept-new-set --project <fwdata> " +
                    "--expected-revision <revision> --assessment <id> " +
                    "(--wordform-id <id> | --text-id <guid> | --selection) [--json]", asJson);
            result = RenderProposal(PendingChanges.AcceptNewSet(new AcceptNewSetRequest(
                acceptProject, CliProductVersion(), acceptRevision, acceptAssessment,
                acceptWordformId, hasTextScope ? Guid.Parse(acceptTextId!) : null, hasSelectionScope)));
            break;

        case "remove-pending-change":
            if (!flags.TryGetValue("project", out var removePendingProject) ||
                !flags.TryGetValue("expected-revision", out var removePendingRevision) ||
                !flags.TryGetValue("change-id", out var removePendingId))
                return Usage("Usage: motif remove-pending-change --project <fwdata> " +
                    "--expected-revision <revision> --change-id <id> [--json]", asJson);
            result = RenderProposal(PendingChanges.Remove(new RemovePendingChangeRequest(
                removePendingProject, CliProductVersion(), removePendingRevision, removePendingId)));
            break;

        case "recheck-pending-changes":
            if (!flags.TryGetValue("project", out var recheckProject) ||
                !flags.TryGetValue("expected-revision", out var recheckRevision))
                return Usage("Usage: motif recheck-pending-changes --project <fwdata> " +
                    "--expected-revision <revision> [--json]", asJson);
            result = RenderProposal(PendingChanges.Recheck(new RecheckPendingChangesRequest(
                recheckProject, CliProductVersion(), recheckRevision)));
            break;

        case "reconfirm-pending-change":
            if (!flags.TryGetValue("project", out var reconfirmProject) ||
                !flags.TryGetValue("expected-revision", out var reconfirmRevision) ||
                !flags.TryGetValue("change-id", out var reconfirmId))
                return Usage("Usage: motif reconfirm-pending-change --project <fwdata> " +
                    "--expected-revision <revision> --change-id <id> [--json]", asJson);
            result = RenderProposal(PendingChanges.Reconfirm(new ReconfirmPendingChangeRequest(
                reconfirmProject, CliProductVersion(), reconfirmRevision, reconfirmId)));
            break;

        case "find-composer":
            if (positionals.Count != 0 || flags.ContainsKey("goal") == flags.ContainsKey("subject") ||
                flags.GetValueOrDefault("goal") == "true" || flags.GetValueOrDefault("subject") == "true")
                return Usage("Usage: motif find-composer (--goal <text> | --subject <id>) " +
                    "[--detail concise|detailed] [--json]", asJson);
            result = RenderCommand(FindComposerCommand.Find(new FindComposerRequest(
                flags.GetValueOrDefault("goal"), flags.GetValueOrDefault("subject"),
                flags.GetValueOrDefault("detail", "concise"))));
            break;

        case "review-numbers":
            if (!flags.TryGetValue("project", out var reviewProject) ||
                !flags.TryGetValue("to", out var reviewTrial) ||
                !flags.TryGetValue("touched-words", out var touchedWordsText) ||
                !int.TryParse(touchedWordsText, out var touchedWords) || touchedWords < 0)
                return Usage("Usage: motif review-numbers --project <fwdata> " +
                    "[--from <assessmentId>] --to <assessmentId> --touched-words <count> [--json]", asJson);
            result = RenderCommand(ReviewNumbersCommand.Read(new ReviewNumbersCommand.Request(
                reviewProject, flags.GetValueOrDefault("from"), reviewTrial, touchedWords)));
            break;

        case "preflight":
            if (!flags.TryGetValue("project", out var preflightProject) ||
                positionals.Count != 1 || flags.ContainsKey("draft"))
                return Usage("Usage: motif preflight --project <fwdata> <proposalId> [--json]", asJson);
            result = RenderProposal(ProposalCommands.Preflight(new PreflightRequest(
                preflightProject, CliProductVersion(), positionals[0])));
            break;

        case "dry-run":
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var dryRunProject))
            {
                return Usage(
                    "Usage: motif dry-run --project <fwdata> <proposalId> [--wait] [--no-background] [--json]", asJson);
            }
            if (flags.ContainsKey("effects") && !flags.ContainsKey("wait"))
                return Usage("Usage: motif dry-run --project <fwdata> <proposalId> --wait [--effects] [--json]", asJson);
            if (flags.ContainsKey("no-background") && !flags.ContainsKey("wait"))
                return Usage("--no-background needs --wait: the Dry Run is the foreground work, and the command " +
                    "returns only once it has ended. Usage: motif dry-run --project <fwdata> <proposalId> --wait " +
                    "[--no-background] [--json]", asJson);
            var dryRunWaitTimeout = JobCommands.DefaultWaitTimeout;
            if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                "Usage: motif dry-run --project <fwdata> <proposalId> --wait " +
                "[--wait-terminal] [--wait-timeout-ms <ms>] [--no-background] [--effects] [--json]", asJson, out dryRunWaitTimeout) is { } dryRunUsage)
                return dryRunUsage;
            var shouldWaitForDryRun = flags.ContainsKey("wait");
            result = WaitWithConsoleCancellation(shouldWaitForDryRun, token =>
            {
                var enqueued = JobCommands.EnqueueDryRun(
                    new EnqueueDryRunRequest(dryRunProject, CliProductVersion(), positionals[0],
                        flags.ContainsKey("no-background")));
                if (!enqueued.Succeeded) return RenderCommand(enqueued, successAsJson: false);
                if (!shouldWaitForDryRun)
                {
                    startRunner(dryRunProject, error.WriteLine);
                    return RenderCommand(enqueued, successAsJson: false);
                }
                var dryRunJobId = enqueued.Value!.JobId;
                var warnings = RunnerWarnings.Once(error.WriteLine);
                var waited = JobCommands.WaitForDryRun(
                    new WaitForDryRunRequest(dryRunProject, CliProductVersion(), positionals[0], dryRunJobId,
                        dryRunWaitTimeout, waitForTerminal), token,
                    wakeRunner: () => startRunner(dryRunProject, warnings), reportStall: error.WriteLine,
                    wakeImmediately: true);
                return RenderCommand(WriteDryRunEffectsArtifact(waited, dryRunProject, dryRunJobId),
                    includeEffects: flags.ContainsKey("effects"));
            });
            break;

        case "trial":
            if (flags.ContainsKey("pending"))
            {
                if (positionals.Count != 0 || !flags.ContainsKey("wait") ||
                    !flags.TryGetValue("project", out var pendingTrialProject) ||
                    !flags.TryGetValue("words", out var pendingTrialWords))
                {
                    return Usage(
                        "Usage: motif trial --pending --project <fwdata> [--draft <id>] [--revision <r>] " +
                        "--words <w,…> --wait [--wait-terminal] [--wait-timeout-ms <ms>] " +
                        "[--before-correctness <assessmentId>] [--json]", asJson);
                }
                var words = pendingTrialWords.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (words.Length == 0)
                    return Usage("Usage: motif trial --pending requires at least one --words value.", asJson);
                if (ParseWaitTimeout(flags,
                    "Usage: motif trial --pending --project <fwdata> [--draft <id>] [--revision <r>] " +
                    "--words <w,…> --wait [--wait-terminal] [--wait-timeout-ms <ms>] " +
                    "[--before-correctness <assessmentId>] [--json]", asJson, out var pendingWaitTimeout)
                    is { } pendingTrialUsage)
                    return pendingTrialUsage;
                result = RunWithConsoleCancellation(cancellationToken => RenderCommand(
                    PendingChangesWorkflow.Measure(new MeasurePendingRequest(
                        pendingTrialProject, flags.GetValueOrDefault("draft"),
                        flags.GetValueOrDefault("revision"), words,
                        flags.GetValueOrDefault("before-correctness")), new Progress<MeasureProgress>(),
                        cancellationToken, pendingWaitTimeout, waitForTerminal: waitForTerminal).GetAwaiter().GetResult()));
                break;
            }
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var trialProject))
            {
                return Usage(
                    "Usage: motif trial --project <fwdata> <proposalId> [--scope <name>] " +
                    "[--words <file>|--all-words] [--touched] [--siblings --corpus-types <file> [--siblings-max <n>] [--siblings-pattern <regex>]|--no-siblings] [--no-base-sample] [--wait] [--no-background] [--expect-loss <file>] [--json]", asJson);
            }
            IReadOnlyList<string>? trialWords = null;
            IReadOnlyList<string>? reviewSets = null;
            if (flags.TryGetValue("review-set", out var reviewSetText))
            {
                reviewSets = reviewSetText == "none" ? [] : reviewSetText.Split(',', StringSplitOptions.TrimEntries);
                if (reviewSets.Any(string.IsNullOrWhiteSpace)) return Usage("Invalid review set selection.", asJson);
            }
            if (flags.TryGetValue("words", out var trialWordsPath))
            {
                if (flags.ContainsKey("all-words"))
                    return Usage("Use either --words <file> or --all-words, not both.", asJson);
                var trialWordList = WordListFile.Resolve(null, trialWordsPath);
                if (!trialWordList.Succeeded)
                {
                    result = RenderCommand(trialWordList);
                    break;
                }
                trialWords = trialWordList.Value!;
            }
            IReadOnlyList<string>? expectedLosses = null;
            if (flags.TryGetValue("expect-loss", out var expectLossPath))
            {
                if (!flags.ContainsKey("wait"))
                    return Usage("--expect-loss needs --wait: the gate is read when the Trial completes.", asJson);
                var expectedList = WordListFile.Resolve(null, expectLossPath);
                if (!expectedList.Succeeded)
                {
                    result = RenderCommand(expectedList);
                    break;
                }
                expectedLosses = expectedList.Value!;
            }
            if (flags.ContainsKey("no-background") && !flags.ContainsKey("wait"))
                return Usage("--no-background needs --wait: the Trial is the foreground work, and the command " +
                    "returns only once it has ended.", asJson);
            var trialWaitTimeout = JobCommands.DefaultWaitTimeout;
            if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                "Usage: motif trial --project <fwdata> <proposalId> [--scope <name>] " +
                "[--words <file>|--all-words] [--touched] [--siblings --corpus-types <file> [--siblings-max <n>] [--siblings-pattern <regex>]|--no-siblings] [--no-base-sample] " +
                "--wait [--wait-terminal] [--wait-timeout-ms <ms>] [--no-background] [--expect-loss <file>] [--verbose] [--json]", asJson, out trialWaitTimeout) is { } trialUsage)
                return trialUsage;
            var siblingsRequest = TrialSiblingsCli.Read(flags, out var siblingsRefusal);
            if (siblingsRefusal is not null)
            {
                result = RenderCommand(CommandOutcome<JobEnqueuedResponse>.Refused(siblingsRefusal), successAsJson: false);
                break;
            }
            var trialResultExit = 0;
            result = RunWithConsoleCancellation(cancellationToken =>
            {
                var queued = TrialWorkflow.Enqueue(
                    new EnqueueTrialRequest(
                        trialProject, CliProductVersion(), positionals[0], flags.GetValueOrDefault("scope"),
                        Words: trialWords, AllWords: flags.ContainsKey("all-words"),
                        NoBackground: flags.ContainsKey("no-background"),
                        NoBaseSample: flags.ContainsKey("no-base-sample"))
                    { ReviewSetIds = reviewSets, ExpectedLosses = expectedLosses, Touched = flags.ContainsKey("touched"), Siblings = siblingsRequest },
                    ResolveRoot(), null, BaselineMode.BestEffort, cancellationToken);
                if (!queued.Succeeded)
                    return RenderCommand(CommandOutcome<JobEnqueuedResponse>.Refused(queued.Refusal!), successAsJson: false);
                if (queued.Value!.Touched is { } touched)
                    error.WriteLine(touched.Describe());
                if (queued.Value!.Siblings is { } siblingsNote)
                    error.WriteLine(siblingsNote);
                if (queued.Value!.BaselineWarning is { } reason)
                    error.WriteLine(
                        "warning: the Trial has no Baseline Assessment, so `difference produce` will refuse it: " + reason);
                if (!flags.ContainsKey("wait"))
                {
                    startRunner(trialProject, error.WriteLine);
                    return RenderCommand(CommandOutcome<JobEnqueuedResponse>.Success(queued.Value.Job), successAsJson: false);
                }
                var trialJobId = queued.Value.Job.JobId;
                var warnings = RunnerWarnings.Once(error.WriteLine);
                var waited = JobCommands.WaitForJob(
                    new WaitForJobRequest(trialProject, trialJobId, CliProductVersion(), trialWaitTimeout, waitForTerminal),
                    () => startRunner(trialProject, warnings), error.WriteLine, cancellationToken,
                    trialProposal: positionals[0], wakeImmediately: true);
                if (waited.Value is { Status: SIL.Motif.Contract.Jobs.JobStatus.Completed })
                {
                    var reportStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    var trialReview = TrialReviewReads.ReadJob(trialProject, CliProductVersion(), positionals[0], trialJobId);
                    var rendered = RenderCommand(trialReview);
                    if (flags.ContainsKey("verbose"))
                        WriteTrialPhases(trialProject, positionals[0], trialJobId, System.Diagnostics.Stopwatch.GetElapsedTime(reportStarted), error);
                    if (rendered.ExitCode == 0 && trialReview.Value is AvailableTrialResult { Gate: { } gate } &&
                        gate.State != TrialGateState.Green)
                    {
                        error.WriteLine("error: " + gate.Reason);
                        trialResultExit = TrialGateExitCode(gate);
                    }
                    return rendered;
                }
                if (waited.Value is { } trialEnded && TrialReviewReads.UnfinishedReason(trialEnded) is { } unfinished)
                {
                    error.WriteLine("error: " + unfinished);
                    trialResultExit = FailureEnvelope.ExitCodeFor(FailureReason.Refused);
                }
                return RenderCommand(waited);
            });
            if (trialResultExit != 0)
            {
                result = StoreUpgradeReport.Apply(storeUpgrades.Notices, result, asJson, error);
                var pickedTrial = Picked(result);
                output.Write(pickedTrial.Output);
                return pickRefused ? pickedTrial.ExitCode : trialResultExit;
            }
            break;

        case "apply":
            var applyAttribution = ApplyAttributionFromFlags(flags, environment);
            var applyNoBackground = flags.ContainsKey("no-background");
            if (ApplyAttributionCheck.Refusal(applyAttribution, flags.ContainsKey("force")) is { } applyAttributionRefusal)
            {
                result = RenderProposal(CommandOutcome<ApplyPendingResult>.Refused(applyAttributionRefusal));
                break;
            }
            if (flags.ContainsKey("receipt") && (flags["receipt"] is "true" or "" || flags.ContainsKey("all-pending")))
                return Usage("--receipt <file> names a Trial receipt and applies to 'apply <proposalId>' and 'apply --proposal' only.", asJson);
            if (flags.TryGetValue("receipt", out var receiptFlag)) applyAttribution = applyAttribution with { ReceiptPath = receiptFlag };
            if (flags.ContainsKey("all-pending"))
            {
                if (positionals.Count != 0 || flags.ContainsKey("user") ||
                    !flags.TryGetValue("project", out var pendingApplyProject))
                    return Usage(
                        "Usage: motif apply --all-pending --project <fwdata> [--revision <r>] " +
                        "[--autonomous] [--message <text>] [--note] [--no-background] [--effects] [--json]", asJson);
                var pendingRequest = new ApplyPendingRequest(pendingApplyProject,
                    DraftId: null, Revision: flags.GetValueOrDefault("revision"),
                    Attribution: applyAttribution, NoBackground: applyNoBackground);
                result = RunWithConsoleCancellation(cancellationToken => RenderProposal(
                    WriteApplyEffectsArtifact(PendingChangesWorkflow.Apply(pendingRequest, cancellationToken),
                        pendingApplyProject), includeEffects: flags.ContainsKey("effects")));
                if (result.ExitCode == 0 && !applyNoBackground) startRunner(pendingApplyProject, error.WriteLine);
                break;
            }
            if (flags.ContainsKey("proposal"))
            {
                if (positionals.Count != 0 || flags.ContainsKey("user") ||
                    !flags.TryGetValue("project", out var proposalApplyProject) ||
                    string.IsNullOrWhiteSpace(flags["proposal"]))
                    return Usage(UsageLineFor("apply --proposal"), asJson);
                var proposalRequest = new ApplyProposalRequest(proposalApplyProject, flags["proposal"],
                    flags.GetValueOrDefault("revision"), applyAttribution, applyNoBackground);
                result = RunWithConsoleCancellation(cancellationToken => RenderProposal(
                    WriteApplyEffectsArtifact(PendingChangesWorkflow.Apply(proposalRequest, cancellationToken),
                        proposalApplyProject), includeEffects: flags.ContainsKey("effects")));
                if (result.ExitCode == 0 && !applyNoBackground) startRunner(proposalApplyProject, error.WriteLine);
                break;
            }
            if (positionals.Count != 1 ||
                flags.ContainsKey("user") ||
                !flags.TryGetValue("project", out var applyProject))
            {
                return Usage(
                    "Usage: motif apply <proposalId> --project <fwdata> " +
                    "[--force] [--autonomous] [--message <text>] [--note] [--no-background] [--effects] [--json]", asJson);
            }
            var applyForce = flags.ContainsKey("force");
            result = RenderProposal(WriteApplyEffectsArtifact(
                    PendingChangesWorkflow.Apply(
                        new ApplyRequest(applyProject, CliProductVersion(), positionals[0], applyForce,
                            applyAttribution, applyNoBackground)), applyProject),
                includeEffects: flags.ContainsKey("effects"));
            if (result.ExitCode == 0 && !applyNoBackground) startRunner(applyProject, error.WriteLine);
            break;

        case "log":
            if (!flags.TryGetValue("project", out var logProject))
                return Usage("Usage: motif log --project <fwdata> [--json]", asJson);
            result = RenderProposal(ProposalCommands.Log(new LogRequest(logProject)));
            break;

        case "citations":
            if (!flags.TryGetValue("project", out var citationsProject))
                return Usage("Usage: motif citations --project <fwdata> [--uncited] [--json]", asJson);
            result = RenderProposal(ProposalCommands.Citations(new CitationsRequest(citationsProject, CliProductVersion(),
                flags.ContainsKey("uncited"))));
            break;

        case "evidence":
            if (positionals.Count == 2 && positionals[0] == "show")
            {
                if (!flags.TryGetValue("project", out var evidenceShowProject))
                    return Usage(UsageLineFor("evidence show"), asJson);
                result = RenderProposal(ProposalCommands.EvidenceShow(
                    new EvidenceShowRequest(evidenceShowProject, CliProductVersion(), positionals[1])));
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "list")
            {
                var evidenceOffset = 0;
                var evidenceLimit = 50;
                if (!flags.TryGetValue("project", out var evidenceListProject) ||
                    flags.TryGetValue("offset", out var evidenceOffsetText) &&
                    !int.TryParse(evidenceOffsetText, NumberStyles.None, CultureInfo.InvariantCulture, out evidenceOffset) ||
                    flags.TryGetValue("limit", out var evidenceLimitText) &&
                    !int.TryParse(evidenceLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out evidenceLimit))
                    return Usage(UsageLineFor("evidence list"), asJson);
                result = RenderProposal(ProposalCommands.EvidenceList(new EvidenceListRequest(
                    evidenceListProject, CliProductVersion(), flags.GetValueOrDefault("tier"), evidenceOffset,
                    evidenceLimit)));
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "export")
            {
                var exportPageSize = EvidenceExportCli.DefaultPageSize;
                if (!flags.TryGetValue("project", out var evidenceExportProject) ||
                    flags.TryGetValue("out", out var exportOutText) && (exportOutText == "true" || exportOutText.Length == 0) ||
                    flags.TryGetValue("page-size", out var exportPageSizeText) &&
                    (!int.TryParse(exportPageSizeText, NumberStyles.None, CultureInfo.InvariantCulture, out exportPageSize) ||
                     exportPageSize is < 1 or > 500))
                    return Usage(UsageLineFor("evidence export"), asJson);
                alreadyRendered = true;
                result = EvidenceExportCli.Run(evidenceExportProject, CliProductVersion(), flags.GetValueOrDefault("tier"),
                    exportPageSize, flags.GetValueOrDefault("out"), output, asJson);
                break;
            }
            return Usage(UsageLineFor("evidence show"), asJson);

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
                flags.GetValueOrDefault("tokeniser-notes"))));
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
                documentCapabilities)));
            break;

        case "add-corpus-bundle":
            if (!flags.TryGetValue("project", out var addBundleProject) ||
                !flags.TryGetValue("bundle", out var bundlePath))
                return Usage("Usage: motif add-corpus-bundle --project <fwdata> --bundle <path-to-bundle.json>", asJson);
            result = RenderCommand(
                CorpusCommands.AddBundle(new AddCorpusBundleRequest(addBundleProject, CliProductVersion(), bundlePath)));
            break;

        case "corpus-paste":
            if (!flags.TryGetValue("project", out var pasteProject) || !flags.TryGetValue("corpus", out var pasteCorpus) ||
                !flags.TryGetValue("doc", out var pasteDocument) || !flags.TryGetValue("title", out var pasteTitle) ||
                !flags.TryGetValue("text", out var pasteText))
                return Usage(UsageLineFor("corpus-paste"), asJson);
            result = RenderCommand(CorpusCommands.PasteDocument(new PasteCorpusDocumentRequest(pasteProject,
                CliProductVersion(), pasteCorpus, pasteDocument, pasteTitle, pasteText, flags.GetValueOrDefault("licence"),
                HasAnyLicenceFlag(flags) ? CorpusCommands.CapabilitiesFromFlags(flags) : null)));
            break;

        case "corpus-reindex":
            if (!flags.TryGetValue("project", out var reindexProject))
                return Usage(UsageLineFor("corpus-reindex"), asJson);
            result = RenderCommand(CorpusCommands.RebuildIndex(new RebuildCorpusIndexRequest(reindexProject, CliProductVersion())));
            break;

        case "corpus-open":
            if (!flags.TryGetValue("project", out var corpusOpenProject) ||
                !flags.TryGetValue("corpus", out var corpusOpenId) || !flags.TryGetValue("doc", out var corpusOpenDocument))
                return Usage(UsageLineFor("corpus-open"), asJson);
            result = RenderCommand(CorpusCommands.OpenDocument(new OpenCorpusDocumentRequest(
                corpusOpenProject, CliProductVersion(), corpusOpenId, corpusOpenDocument)));
            break;

        case "corpus-range":
            if (!flags.TryGetValue("project", out var rangeProject) || !flags.TryGetValue("corpus", out var rangeCorpus) ||
                !flags.TryGetValue("doc", out var rangeDocument) || !flags.TryGetValue("sha256", out var rangeHash) ||
                !flags.TryGetValue("offset", out var rangeOffsetText) || !long.TryParse(rangeOffsetText, out var rangeOffset) ||
                !flags.TryGetValue("length", out var rangeLengthText) || !int.TryParse(rangeLengthText, out var rangeLength))
                return Usage(UsageLineFor("corpus-range"), asJson);
            result = RenderCommand(CorpusCommands.ReadRange(new CorpusRangeRequest(rangeProject, CliProductVersion(),
                rangeCorpus, rangeDocument, rangeHash, rangeOffset, rangeLength)));
            break;

        case "corpora":
            if (!flags.TryGetValue("project", out var corporaProject))
                return Usage("Usage: motif corpora --project <fwdata> [--json]", asJson);
            result = RenderCommand(
                CorpusCommands.ListCorpora(new ListCorporaRequest(corporaProject, CliProductVersion())));
            break;

        case "show-corpus":
            if (!flags.TryGetValue("project", out var showCorpusProject) || positionals.Count != 1)
                return Usage("Usage: motif show-corpus --project <fwdata> <corpusId> [--json]", asJson);
            result = RenderCommand(CorpusCommands.ShowCorpus(
                new ShowCorpusRequest(showCorpusProject, CliProductVersion(), positionals[0])));
            break;

        case "baseline-refresh":
            if (!flags.TryGetValue("project", out var refreshProject))
                return Usage("Usage: motif baseline-refresh --project <fwdata>", asJson);
            result = RenderCommand(
                JobCommands.EnqueueBaselineRefresh(
                    new EnqueueBaselineRefreshRequest(refreshProject, CliProductVersion())),
                successAsJson: false);
            if (result.ExitCode == 0) startRunner(refreshProject, error.WriteLine);
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

        case "parsimony":
            if (flags.ContainsKey("rule-attempts"))
            {
                if (positionals.Count != 0 || !flags.TryGetValue("project", out var attemptProject) ||
                    !flags.TryGetValue("plan", out var attemptPlan))
                    return Usage(UsageLineFor("parsimony --rule-attempts"), asJson);
                if (!File.Exists(attemptPlan))
                    return Usage($"The --plan file '{attemptPlan}' does not exist.", asJson);
                result = RenderCommand(RuleAttemptCommands.Check(
                    new CheckRuleAttemptsRequest(attemptProject, CliProductVersion(), File.ReadAllText(attemptPlan))));
                break;
            }
            if (positionals.Count == 2 && positionals[0] == "revise")
            {
                const string reviseCommandName = "parsimony revise";
                if (!flags.TryGetValue("project", out var reviseProject) ||
                    !flags.TryGetValue("draft", out var reviseDraft) ||
                    !flags.TryGetValue("expected-heads", out var reviseExpectedHeads) ||
                    !flags.TryGetValue("disposition", out var reviseDisposition))
                    return Usage(UsageLineFor(reviseCommandName), asJson);
                JsonNode? heads;
                try { heads = JsonNode.Parse(reviseExpectedHeads); }
                catch (JsonException) { return Usage(UsageLineFor(reviseCommandName), asJson); }
                if (heads is not JsonArray) return Usage(UsageLineFor(reviseCommandName), asJson);
                var intent = new JsonObject
                {
                    ["recordId"] = positionals[1],
                    ["expectedHeads"] = heads,
                    ["disposition"] = reviseDisposition,
                };
                if (flags.TryGetValue("reason", out var reviseReason)) intent["reason"] = reviseReason;
                if (flags.ContainsKey("clear-reason")) intent["clearReason"] = true;
                if (flags.TryGetValue("question", out var reviseQuestion)) intent["question"] = reviseQuestion;
                result = RenderProposal(ParsimonyCommands.ReviseDisposition(
                    new ReviseParsimonyDispositionRequest(reviseProject, CliProductVersion(), reviseDraft,
                        intent.ToJsonString())));
                break;
            }
            if (positionals.Count == 2 && positionals[0] == "retract")
            {
                const string retractCommandName = "parsimony retract";
                if (!flags.TryGetValue("project", out var retractProject) ||
                    !flags.TryGetValue("draft", out var retractDraft) ||
                    !flags.TryGetValue("expected-heads", out var retractExpectedHeads))
                    return Usage(UsageLineFor(retractCommandName), asJson);
                JsonNode? heads;
                try { heads = JsonNode.Parse(retractExpectedHeads); }
                catch (JsonException) { return Usage(UsageLineFor(retractCommandName), asJson); }
                if (heads is not JsonArray) return Usage(UsageLineFor(retractCommandName), asJson);
                var intent = new JsonObject
                {
                    ["recordId"] = positionals[1],
                    ["expectedHeads"] = heads,
                };
                result = RenderProposal(ParsimonyCommands.RetractDisposition(
                    new RetractParsimonyDispositionRequest(retractProject, CliProductVersion(), retractDraft,
                        intent.ToJsonString())));
                break;
            }
            if (positionals.SequenceEqual(new[] { "expectations" }))
            {
                if (!flags.TryGetValue("project", out var expectationProject))
                    return Usage(UsageLineFor("parsimony expectations"), asJson);
                result = RenderCommand(ParsimonyCommands.ReadExpectations(
                    new ReadParsimonyExpectationsRequest(expectationProject, CliProductVersion())));
                break;
            }
            if (positionals.Count == 2 && positionals[0] == "retirement-review")
            {
                if (!flags.TryGetValue("project", out var retirementProject))
                    return Usage(UsageLineFor("parsimony retirement-review"), asJson);
                result = RenderCommand(ParsimonyCommands.ReadRetirementReview(
                    new ReadRetirementReviewRequest(retirementProject, CliProductVersion(), positionals[1])));
                break;
            }
            if (positionals.Count == 2 && positionals[0] == "negative" &&
                positionals[1] is "confirm" or "retract")
            {
                var retract = positionals[1] == "retract";
                var negativeCommandName = retract ? "parsimony negative retract" : "parsimony negative confirm";
                if (!flags.TryGetValue("project", out var negativeProject) ||
                    !flags.TryGetValue("draft", out var negativeDraft) ||
                    !flags.TryGetValue("intent", out var negativeIntent) ||
                    !flags.TryGetValue("confirm", out var negativeConfirmation))
                    return Usage(UsageLineFor(negativeCommandName), asJson);
                result = RenderProposal(retract
                    ? ParsimonyCommands.RetractReviewedNegative(new RetractReviewedNegativeRequest(
                        negativeProject, CliProductVersion(), negativeDraft, negativeIntent, negativeConfirmation))
                    : ParsimonyCommands.ConfirmReviewedNegative(new ConfirmReviewedNegativeRequest(
                        negativeProject, CliProductVersion(), negativeDraft, negativeIntent, negativeConfirmation)));
                break;
            }
            if (positionals.FirstOrDefault() == "record-types")
            {
                if (positionals.Count != 1 || !flags.TryGetValue("project", out var recordTypesProject))
                    return Usage(UsageLineFor("parsimony record-types"), asJson);
                result = RenderCommand(ParsimonyCommands.ListNotebookRecordTypes(
                    new ListNotebookRecordTypesRequest(recordTypesProject, CliProductVersion())));
                break;
            }
            if (positionals.FirstOrDefault() == "dispose")
            {
                if (positionals.Count != 1 || !flags.TryGetValue("project", out var findingProject) ||
                    !flags.TryGetValue("report", out var dispositionReport) ||
                    !flags.TryGetValue("finding", out var dispositionFinding) ||
                    !flags.TryGetValue("disposition", out var dispositionKind) ||
                    !flags.TryGetValue("record-type", out var dispositionRecordType) ||
                    !flags.TryGetValue("draft", out var findingDraft))
                    return Usage(UsageLineFor("parsimony dispose"), asJson);
                result = RenderProposal(ParsimonyCommands.RecordDisposition(
                    new RecordParsimonyDispositionFromFindingRequest(
                        findingProject, CliProductVersion(), dispositionReport, dispositionFinding,
                        dispositionKind, dispositionRecordType, findingDraft,
                        flags.GetValueOrDefault("reason"), flags.GetValueOrDefault("question"))));
                break;
            }
            if (flags.TryGetValue("dry-run", out var candidateDryRunJobId))
            {
                if (positionals.Count != 0 || !flags.TryGetValue("project", out var candidateProject))
                    return Usage("Usage: motif parsimony --project <fwdata> --dry-run <job-id> " +
                        "[--measure <measure-id>] [--evidence-scope project-approved|default-selection] " +
                        "[--assessment <ParseTime-id> ...] [--wait] [--wait-timeout-ms <ms>] [--json]", asJson);
                var candidateScope = flags.GetValueOrDefault("evidence-scope") switch
                {
                    null or "project-approved" => ParsimonyEvidenceScopeKind.ProjectApproved,
                    "default-selection" => ParsimonyEvidenceScopeKind.DefaultSelection,
                    _ => (ParsimonyEvidenceScopeKind?)null,
                };
                if (candidateScope is null)
                    return Usage("--evidence-scope must be 'default-selection' or 'project-approved'.", asJson);
                var candidateTimeout = JobCommands.DefaultWaitTimeout;
                if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                    "Usage: motif parsimony --project <fwdata> --dry-run <job-id> --wait " +
                    "[--measure <measure-id>] [--evidence-scope project-approved|default-selection] " +
                    "[--wait-timeout-ms <ms>] [--json]", asJson, out candidateTimeout) is { } candidateUsage)
                    return candidateUsage;
                var queuedCandidate = ParsimonyCommands.EnqueueCandidate(new EnqueueParsimonyCandidateRequest(
                    candidateProject, CliProductVersion(), candidateDryRunJobId,
                    flags.GetValueOrDefault("measure") ?? "P-adhoc-duplicate", candidateScope.Value,
                    ReadRepeatedFlagValues(flags, "assessment")), SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());
                result = RenderCommand(queuedCandidate);
                if (queuedCandidate.Succeeded)
                {
                    startRunner(candidateProject, error.WriteLine);
                    if (flags.ContainsKey("wait"))
                    {
                        var candidateWakeWarnings = RunnerWarnings.Once(error.WriteLine);
                        result = RunWithConsoleCancellation(cancellationToken => RenderCommand(
                            ParsimonyCommands.WaitCandidate(new WaitForParsimonyCandidateRequest(candidateProject,
                                CliProductVersion(), queuedCandidate.Value!.JobId, candidateTimeout),
                                cancellationToken, () => startRunner(candidateProject, candidateWakeWarnings))));
                    }
                }
                break;
            }
            if (positionals.FirstOrDefault() == "status")
            {
                if (!flags.TryGetValue("project", out var statusProject) || positionals.Count != 1 ||
                    !flags.TryGetValue("proposal", out var statusProposal))
                    return Usage("Usage: motif parsimony status --project <fwdata> --proposal <id> [--json]", asJson);
                result = RenderCommand(ProposalParsimonyStatusCommand.Read(new ReadProposalParsimonyStatusRequest(
                    statusProject, CliProductVersion(), statusProposal),
                    SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate()));
                break;
            }
            if (positionals.FirstOrDefault() == "measures")
            {
                if (!flags.TryGetValue("project", out var measureProject) || positionals.Count != 1)
                    return Usage("Usage: motif parsimony measures --project <fwdata> [--json]", asJson);
                result = RenderCommand(ParsimonyViewsCommand.Measures(new ListParsimonyMeasuresRequest(
                    measureProject, CliProductVersion())));
                break;
            }
            if (positionals.FirstOrDefault() == "view")
            {
                if (positionals.Count != 2 || !flags.TryGetValue("project", out var viewProject) ||
                    !flags.TryGetValue("bundle", out var viewBundle))
                    return Usage("Usage: motif parsimony view <view-code> --project <fwdata> --bundle <id> " +
                        "[--report <id>] " +
                        "[--object-id <guid> | --category-id <guid> | --statement-kind environment|natural-class] " +
                        "[--scope project-approved|default-selection] [--case-key <key>] [--cursor <cursor>] " +
                        "[--limit <n>] [--json]", asJson);
                var viewLimit = 50;
                if (flags.TryGetValue("limit", out var limitText) &&
                    (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out viewLimit) ||
                     viewLimit is < 1 or > 200))
                    return Usage("--limit must be an integer from 1 to 200.", asJson);
                var viewFilters = new ParsimonyViewFilters(
                    ObjectGuid: flags.GetValueOrDefault("object-id"),
                    CategoryGuid: flags.GetValueOrDefault("category-id"),
                    StatementKind: flags.GetValueOrDefault("statement-kind"),
                    Scope: flags.GetValueOrDefault("scope"),
                    CaseKey: flags.GetValueOrDefault("case-key"),
                    ReportId: flags.GetValueOrDefault("report"),
                    MeasureId: flags.GetValueOrDefault("measure-id"),
                    Disposition: flags.GetValueOrDefault("disposition"),
                    State: flags.GetValueOrDefault("state"),
                    Search: flags.GetValueOrDefault("search"),
                    SubjectKey: flags.GetValueOrDefault("subject-key"));
                result = RenderCommand(ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(
                    viewProject, CliProductVersion(), new ParsimonyNamedViewRequest(viewBundle, positionals[1],
                        viewFilters, viewLimit, flags.GetValueOrDefault("cursor")))));
                break;
            }
            if (positionals.FirstOrDefault() == "latest")
            {
                if (!flags.TryGetValue("project", out var latestProject) || positionals.Count != 1)
                    return Usage("Usage: motif parsimony latest --project <fwdata> [--json]", asJson);
                result = RenderCommand(ParsimonyCommands.ReadLatest(new ReadLatestParsimonyReportRequest(
                    latestProject, CliProductVersion())));
                break;
            }
            if (positionals.FirstOrDefault() == "show")
            {
                if (!flags.TryGetValue("project", out var showParsimonyProject) || positionals.Count != 2)
                    return Usage("Usage: motif parsimony show --project <fwdata> <reportId> [--json]", asJson);
                result = RenderCommand(ParsimonyCommands.Show(new ShowParsimonyReportRequest(
                    showParsimonyProject, CliProductVersion(), positionals[1])));
                break;
            }
            if (flags.TryGetValue("group", out var parsimonyGroup) && !flags.ContainsKey("dry-run"))
            {
                const string groupUsage = "Usage: motif parsimony --project <fwdata> --group grammar|text|all " +
                    "[--measure <measure-id>] [--assessment <ParseTime-id> ...] [--wait-timeout-ms <ms>] [--json]";
                if (positionals.Count != 0 || !flags.TryGetValue("project", out var groupProject))
                    return Usage(groupUsage, asJson);
                if (flags.ContainsKey("evidence-scope"))
                    return Usage("--evidence-scope is not used with --group; the group names its own evidence. " +
                        groupUsage, asJson);
                var groupTimeout = JobCommands.DefaultWaitTimeout;
                if (flags.ContainsKey("wait-timeout-ms") &&
                    ParseWaitTimeout(flags, groupUsage, asJson, out groupTimeout) is { } groupWaitUsage)
                    return groupWaitUsage;
                var enqueuedGroup = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(groupProject,
                    CliProductVersion(), parsimonyGroup, flags.GetValueOrDefault("measure"),
                    ReadRepeatedFlagValues(flags, "assessment")), SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());
                result = RenderCommand(enqueuedGroup);
                if (enqueuedGroup.Succeeded)
                {
                    startRunner(groupProject, error.WriteLine);
                    var groupJobId = enqueuedGroup.Value!.JobId;
                    var groupWarnings = RunnerWarnings.Once(error.WriteLine);
                    result = RunWithConsoleCancellation(cancellationToken => RenderCommand(ParsimonyCommands.WaitAll(
                        new WaitForParsimonyReportSetRequest(groupProject, CliProductVersion(), groupJobId,
                            groupTimeout), cancellationToken, () => startRunner(groupProject, groupWarnings))));
                }
                break;
            }
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var parsimonyProject))
                return Usage("Usage: motif parsimony --project <fwdata> [--measure <measure-id>|all] [--group grammar|text|all] " +
                    "[--evidence-scope default-selection|project-approved] [--assessment <ParseTime-id> ...] [--wait] " +
                    "[--wait-timeout-ms <ms>] [--json]", asJson);
            var evidenceScope = flags.GetValueOrDefault("evidence-scope") switch
            {
                null or "default-selection" => ParsimonyEvidenceScopeKind.DefaultSelection,
                "project-approved" => ParsimonyEvidenceScopeKind.ProjectApproved,
                _ => (ParsimonyEvidenceScopeKind?)null,
            };
            if (evidenceScope is null)
                return Usage("--evidence-scope must be 'default-selection' or 'project-approved'.", asJson);
            var parsimonyWaitTimeout = JobCommands.DefaultWaitTimeout;
            if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                "Usage: motif parsimony --project <fwdata> [--measure <measure-id>] --wait " +
                "[--evidence-scope default-selection|project-approved] [--wait-timeout-ms <ms>] [--json]",
                asJson, out parsimonyWaitTimeout) is { } parsimonyUsage)
                return parsimonyUsage;
            var enqueuedParsimony = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(
                parsimonyProject, CliProductVersion(), flags.GetValueOrDefault("measure") ?? "P-adhoc-duplicate",
                evidenceScope.Value, ReadRepeatedFlagValues(flags, "assessment")), SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());
            result = RenderCommand(enqueuedParsimony);
            if (enqueuedParsimony.Succeeded)
            {
                startRunner(parsimonyProject, error.WriteLine);
                if (flags.ContainsKey("wait"))
                {
                    var parsimonyJobId = enqueuedParsimony.Value!.JobId;
                    var parsimonyWakeWarnings = RunnerWarnings.Once(error.WriteLine);
                    result = flags.GetValueOrDefault("measure") == ParsimonyAdmission.AllMeasures
                        ? RunWithConsoleCancellation(cancellationToken =>
                        {
                            var waitedAll = ParsimonyCommands.WaitAll(
                                new WaitForParsimonyReportSetRequest(parsimonyProject, CliProductVersion(),
                                    parsimonyJobId, parsimonyWaitTimeout), cancellationToken,
                                () => startRunner(parsimonyProject, parsimonyWakeWarnings));
                            if (waitedAll.Succeeded && flags.TryGetValue("trend", out var trendPath))
                                SweepTrend.Append(trendPath, SweepTrend.Rows(DateOnly.FromDateTime(DateTime.UtcNow),
                                    waitedAll.Value!.Reports.SelectMany(report => report.MeasureRuns)));
                            return RenderCommand(waitedAll);
                        })
                        : RunWithConsoleCancellation(cancellationToken => RenderCommand(ParsimonyCommands.Wait(
                            new WaitForParsimonyReportRequest(parsimonyProject, CliProductVersion(),
                                parsimonyJobId, parsimonyWaitTimeout), cancellationToken,
                            () => startRunner(parsimonyProject, parsimonyWakeWarnings))));
                }
            }
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
                    if (positionals.Count != 1 || !flags.TryGetValue("project", out var captureProject) ||
                        string.IsNullOrWhiteSpace(captureProject))
                        return Usage(BaselineUsage(), asJson);
                    var captureNoBackground = flags.ContainsKey("no-background");
                    result = RenderCommand(CaptureBaseline(captureProject, ResolveRoot(), captureNoBackground));
                    if (result.ExitCode == 0 && !captureNoBackground)
                    {
                        _ = BaselineBundlePrebuild.QueueAfterCapture(
                            captureProject, JobRunnerLaunchOptions.FromEnvironment().ParserPath);
                        startRunner(captureProject, error.WriteLine);
                    }
                    break;

                default:
                    return Usage(BaselineUsage(), asJson);
            }
            break;

        case "overview":
            if (!flags.TryGetValue("project", out var overviewProject))
                return Usage(UsageLineFor("overview"), asJson);
            result = RenderCommand(OverviewCommand.Overview(new OverviewRequest(
                overviewProject, flags.GetValueOrDefault("proposal"))));
            break;

        case "group":
        {
            if (positionals.Count != 1 || positionals[0] is not ("suffix" or "prefix") ||
                !flags.TryGetValue("project", out var groupProject) ||
                flags.Keys.Any(flag => flag is not ("project" or "json" or "format" or "ending" or "start" or "pattern" or
                    "parse-state" or "limit" or "page" or "fold" or "as-of" or "min-stem" or "autonomous")))
                return Usage(UsageLineFor("group"), asJson);
            if (flags.TryGetValue("format", out var groupFormat))
            {
                if (groupFormat is not ("table" or "json")) return Usage("--format must be table or json.", asJson);
                if (groupFormat == "json") asJson = true;
            }
            int? groupLimit = null;
            int? groupPage = null;
            if (flags.TryGetValue("limit", out var groupLimitText))
            {
                if (!int.TryParse(groupLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limitNumber) || limitNumber < 1)
                    return Usage("--limit must be a positive integer.", asJson);
                groupLimit = limitNumber;
            }
            if (flags.TryGetValue("page", out var groupPageText))
            {
                if (!int.TryParse(groupPageText, NumberStyles.None, CultureInfo.InvariantCulture, out var pageNumber) || pageNumber < 1)
                    return Usage("--page must be a positive integer.", asJson);
                groupPage = pageNumber;
            }
            int? groupMinStem = null;
            if (flags.TryGetValue("min-stem", out var groupMinStemText))
            {
                if (!int.TryParse(groupMinStemText, NumberStyles.None, CultureInfo.InvariantCulture, out var minStemNumber) || minStemNumber < 1)
                    return Usage("--min-stem must be a positive integer.", asJson);
                groupMinStem = minStemNumber;
            }
            result = RenderCommand(GroupQueryCommand.Run(new GroupRequest(groupProject, positionals[0],
                flags.GetValueOrDefault("ending"), flags.GetValueOrDefault("start"), flags.GetValueOrDefault("pattern"),
                flags.GetValueOrDefault("parse-state"), groupLimit, groupPage, flags.GetValueOrDefault("as-of"),
                flags.GetValueOrDefault("fold"), groupMinStem)));
            break;
        }

        case "proposal":
            if (positionals.Count >= 1 && positionals[0] == "why-lost")
            {
                if (!flags.TryGetValue("project", out var whyLostProject) || positionals.Count != 2 ||
                    !flags.TryGetValue("proposal", out var whyLostProposal) || whyLostProposal == "true" ||
                    flags.Keys.Any(flag => flag is not ("project" or "proposal" or "max-paths" or "json" or "autonomous")))
                    return Usage("Usage: motif " + UsageLineFor("proposal why-lost"), asJson);
                int? whyLostMax = null;
                if (flags.TryGetValue("max-paths", out var whyLostMaxText))
                {
                    if (!int.TryParse(whyLostMaxText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedWhyLostMax))
                    {
                        result = RenderCommand(RejectionTraceQuery.InvalidCap(whyLostMaxText));
                        break;
                    }
                    whyLostMax = parsedWhyLostMax;
                }
                result = RunWithConsoleCancellation(token => RenderCommand(WhyLostQuery.Compare(
                    new WhyLostRequest(whyLostProject, positionals[1], whyLostProposal, whyLostMax), token)));
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "review" &&
                flags.TryGetValue("project", out var proposalReviewProject))
            {
                result = RenderCommand(ProposalReview.Read(new ProposalReviewRequest(
                    proposalReviewProject, flags.GetValueOrDefault("proposal"))));
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "review")
                return Usage(UsageLineFor("proposal review"), asJson);
            if (positionals.Count >= 1 && positionals[0] == "comment")
            {
                var commentUsage = positionals.Count >= 2 && positionals[1] is "add" or "edit" or "delete"
                    ? UsageLineFor("proposal comment " + positionals[1])
                    : "proposal comment (add | edit | delete) <proposalId> ... --project <fwdata>";
                if (positionals.Count < 3 || positionals[1] is not ("add" or "edit" or "delete") ||
                    !flags.TryGetValue("project", out var commentProject))
                    return Usage(commentUsage, asJson);
                if (positionals[1] != "add" && (flags.ContainsKey("author") || flags.ContainsKey("person")))
                    return Usage("Only 'proposal comment add' takes --author or --person; an edit or delete is " +
                        "the person's own. " + commentUsage, asJson);
                var wantsText = positionals[1] != "delete";
                var textSources = new[] { "text", "file" }.Count(flags.ContainsKey);
                if (wantsText ? textSources != 1 : textSources != 0) return Usage(commentUsage, asJson);
                string commentBody = "";
                if (flags.TryGetValue("file", out var commentFile))
                {
                    if (!File.Exists(commentFile))
                        return Usage("The file '" + commentFile + "' does not exist. " + commentUsage, asJson);
                    commentBody = File.ReadAllText(commentFile);
                }
                else if (flags.TryGetValue("text", out var commentText)) commentBody = commentText;
                if (positionals[1] == "add")
                {
                    if (positionals.Count != 3) return Usage(commentUsage, asJson);
                    result = RenderCommand(ProposalComments.Add(new AddCommentRequest(commentProject, CliProductVersion(),
                        positionals[2], commentBody, flags.GetValueOrDefault("person") ?? Environment.UserName,
                        flags.GetValueOrDefault("author"))));
                    break;
                }
                if (positionals.Count != 4 || !long.TryParse(positionals[3], out var commentId))
                    return Usage(commentUsage, asJson);
                result = RenderCommand(positionals[1] == "edit"
                    ? ProposalComments.Edit(new EditCommentRequest(
                        commentProject, CliProductVersion(), positionals[2], commentId, commentBody))
                    : ProposalComments.Delete(new DeleteCommentRequest(
                        commentProject, CliProductVersion(), positionals[2], commentId)));
                break;
            }
            if (positionals.Count >= 2 && positionals[0] == "picture")
            {
                var pictureUsage = positionals[1] is "add" or "remove" or "show"
                    ? UsageLineFor("proposal picture " + positionals[1])
                    : "proposal picture (add | remove | show) <proposalId> ... --project <fwdata>";
                if (positionals.Count < 3 || positionals[1] is not ("add" or "remove" or "show") ||
                    !flags.TryGetValue("project", out var pictureProject))
                    return Usage(pictureUsage, asJson);
                if (positionals[1] == "add")
                {
                    if (positionals.Count != 3 || !flags.TryGetValue("file", out var pictureFile))
                        return Usage(pictureUsage, asJson);
                    if (!File.Exists(pictureFile))
                        return Usage("The file '" + pictureFile + "' does not exist. " + pictureUsage, asJson);
                    long? pictureComment = null;
                    if (flags.TryGetValue("comment", out var pictureCommentText))
                    {
                        if (!long.TryParse(pictureCommentText, out var parsedComment)) return Usage(pictureUsage, asJson);
                        pictureComment = parsedComment;
                    }
                    if (new FileInfo(pictureFile).Length > PictureScaler.MaxSourceBytes)
                        return Usage("This file is " + new FileInfo(pictureFile).Length + " bytes; Motif reads picture files up to " +
                            PictureScaler.MaxSourceBytes + " bytes. Reduce it and try again. " + pictureUsage, asJson);
                    result = RenderCommand(ProposalPictures.Add(new AddPictureRequest(pictureProject, CliProductVersion(),
                        positionals[2], File.ReadAllBytes(pictureFile), pictureComment,
                        flags.GetValueOrDefault("caption"))));
                    break;
                }
                if (positionals[1] == "remove")
                {
                    if (positionals.Count != 4 || !long.TryParse(positionals[3], out var removePictureId))
                        return Usage(pictureUsage, asJson);
                    result = RenderCommand(ProposalPictures.Remove(new RemovePictureRequest(
                        pictureProject, CliProductVersion(), positionals[2], removePictureId)));
                    break;
                }
                long? showPictureId = null;
                if (positionals.Count == 4)
                {
                    if (!long.TryParse(positionals[3], out var parsedShow) || !flags.ContainsKey("out"))
                        return Usage(pictureUsage, asJson);
                    showPictureId = parsedShow;
                }
                else if (positionals.Count != 3 || flags.ContainsKey("out")) return Usage(pictureUsage, asJson);
                var shown = ProposalPictures.Show(new ShowPicturesRequest(
                    pictureProject, CliProductVersion(), positionals[2], showPictureId));
                if (shown is { Succeeded: true, Value.DataBase64: { } shownData })
                    File.WriteAllBytes(flags["out"], Convert.FromBase64String(shownData));
                result = RenderCommand(shown.Succeeded
                    ? CommandOutcome<ProposalPicturesResponse>.Success(shown.Value! with { DataBase64 = null })
                    : shown);
                break;
            }
            if (positionals.Count == 2 && positionals[0] == "rationale")
            {
                var textUsage = UsageLineFor("proposal rationale");
                if (!flags.TryGetValue("project", out var textProject)) return Usage(textUsage, asJson);
                if (flags.ContainsKey("clear"))
                    return Usage("A Rationale cannot be cleared, only rewritten: give --text or --file. " + textUsage, asJson);
                if (new[] { "text", "file" }.Count(flags.ContainsKey) != 1) return Usage(textUsage, asJson);
                string textBody;
                if (flags.TryGetValue("file", out var textFile))
                {
                    if (!File.Exists(textFile))
                        return Usage("The file '" + textFile + "' does not exist. " + textUsage, asJson);
                    textBody = File.ReadAllText(textFile);
                }
                else textBody = flags.GetValueOrDefault("text") ?? "";
                var rationaleAuthor = flags.GetValueOrDefault("author");
                if (rationaleAuthor is null && flags.ContainsKey("person"))
                    return Usage("--person names who is speaking for the --author; give --author as well. " + textUsage,
                        asJson);
                result = RenderCommand(ProposalTexts.SetRationale(new SetRationaleRequest(
                    textProject, CliProductVersion(), positionals[1], textBody, rationaleAuthor,
                    rationaleAuthor is null ? null : flags.GetValueOrDefault("person") ?? Environment.UserName)));
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "looked-at")
            {
                if (!flags.TryGetValue("project", out var markProject) ||
                    !flags.TryGetValue("proposal", out var markProposal) ||
                    !flags.TryGetValue("entry", out var markEntry))
                    return Usage(UsageLineFor("proposal looked-at"), asJson);
                var rationaleEntry = markEntry == "proposal.rationale";
                if (!rationaleEntry && (!flags.ContainsKey("content-digest") || !flags.ContainsKey("evidence-digest")))
                    return Usage(UsageLineFor("proposal looked-at"), asJson);
                var markContent = flags.GetValueOrDefault("content-digest") ?? "";
                var markEvidence = flags.GetValueOrDefault("evidence-digest") ?? "";
                result = RenderCommand(ReviewMarks.Set(new ReviewMarkRequest(markProject, markProposal, markEntry,
                    markContent, markEvidence, !flags.ContainsKey("clear"))));
                break;
            }
            if (positionals is ["compare", ..])
            {
                if (positionals.Count < 3 || !flags.TryGetValue("project", out var compareReadProject) ||
                    flags.Keys.Any(flag => flag is not ("project" or "json" or "autonomous" or "page" or "at" or "snapshot" or "job")))
                    return Usage(UsageLineFor("proposal compare"), asJson);
                if (flags.ContainsKey("snapshot") && !flags.ContainsKey("page"))
                    return Usage("--snapshot checks the Trials a page was read from, so it needs --page. " +
                        UsageLineFor("proposal compare"), asJson);
                int? proposalComparePage = null;
                if (flags.TryGetValue("page", out var proposalCompareText))
                {
                    if (!int.TryParse(proposalCompareText, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                        return Usage("--page must be a positive integer.", asJson);
                    proposalComparePage = number;
                }
                result = RenderCommand(ProposalCompareReads.Read(new CompareProposalsRequest(compareReadProject,
                    positionals.Skip(1).ToArray(), flags.GetValueOrDefault("at"), proposalComparePage,
                    flags.GetValueOrDefault("snapshot"), ReadRepeatedFlagValues(flags, "job"))));
                break;
            }
            if (positionals is ["trial", "delta"])
            {
                var delta = TrialDeltaCli.Run(flags, asJson, out var deltaUsage);
                if (delta is null) return Usage(UsageLineFor("proposal trial delta"), asJson);
                result = delta;
                alreadyRendered = true;
                // A delta that is not green still prints its result on stdout; only its exit code fails the command.
                if (delta.ExitCode != 0 && delta.Reason is null)
                {
                    var projected = Picked(delta with { ExitCode = 0 });
                    if (pickRefused) return projected.ExitCode;
                    output.Write(projected.Output);
                    return delta.ExitCode;
                }
                break;
            }
            if (positionals is ["trial", "export", ..])
            {
                if (positionals.Count != 3 || !flags.TryGetValue("project", out var exportProject) ||
                    flags.Keys.Any(flag => flag is not ("project" or "json" or "autonomous" or "page" or "at")))
                    return Usage(UsageLineFor("proposal trial export"), asJson);
                int? page = null;
                if (flags.TryGetValue("page", out var trialExportPage))
                {
                    if (!int.TryParse(trialExportPage, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                        return Usage("--page must be a positive integer.", asJson);
                    page = number;
                }
                result = RenderCommand(TrialExportReads.Read(new ReadTrialExportRequest(exportProject, positionals[2],
                    flags.GetValueOrDefault("at"), page)));
                break;
            }
            if (positionals.Count != 2 || positionals[0] != "trial" ||
                !flags.TryGetValue("project", out var trialReadProject))
                return Usage("Usage: motif proposal trial --project <fwdata> <proposalId> [--json] [--word-results]", asJson);
            DetailLevel? reviewDetail = flags.GetValueOrDefault("detail") switch
            {
                null => flags.ContainsKey("word-results") ? DetailLevel.Full : null,
                "summary" => DetailLevel.Summary, "failures" => DetailLevel.Failures, "full" => DetailLevel.Full,
                _ => (DetailLevel)(-1)
            };
            if (flags.ContainsKey("word-results") && reviewDetail != DetailLevel.Full)
                return Usage("--word-results requires full detail.", asJson);
            ReviewFlagId? reviewFlag = null;
            if (flags.TryGetValue("flag", out var reviewFlagText))
            {
                try { reviewFlag = JsonSerializer.Deserialize<ReviewFlagId>(JsonSerializer.Serialize(reviewFlagText), ReviewJson.Options); }
                catch (JsonException) { return Usage("Unknown Trial review flag.", asJson); }
            }
            Side? reviewSide = flags.GetValueOrDefault("side") switch
            { null => null, "before" => Side.Before, "after" => Side.After, _ => (Side)(-1) };
            int? reviewPage = null;
            if (flags.TryGetValue("page", out var reviewPageText))
            {
                if (!int.TryParse(reviewPageText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPage) || parsedPage < 1)
                    return Usage("--page must be a positive integer.", asJson);
                reviewPage = parsedPage;
            }
            int? reviewLimit = null;
            if (flags.TryGetValue("limit", out var reviewLimitText))
            {
                if (!int.TryParse(reviewLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLimit))
                    return Usage("Invalid Trial review limit.", asJson);
                reviewLimit = parsedLimit;
            }
            var reviewRequest = new ReviewReadRequest("project", positionals[1], flags.GetValueOrDefault("job"),
                flags.GetValueOrDefault("report"), reviewDetail, flags.GetValueOrDefault("section"), flags.GetValueOrDefault("set"),
                flags.GetValueOrDefault("outcome"), reviewFlag, flags.GetValueOrDefault("filter"), flags.GetValueOrDefault("word"),
                flags.GetValueOrDefault("case"), flags.ContainsKey("trace"), reviewSide, flags.GetValueOrDefault("cursor"), reviewLimit, reviewPage,
                Latest: flags.ContainsKey("latest"));
            if (reviewRequest.Validate() is { } reviewProblem) return Usage(reviewProblem, asJson);
            var refresh = flags.ContainsKey("refresh-report");
            if (refresh && (reviewRequest.Latest || reviewRequest.Section is not null || reviewRequest.Set is not null || reviewRequest.Outcome is not null ||
                reviewRequest.Flag is not null || reviewRequest.Filter is not null || reviewRequest.Word is not null || reviewRequest.Case is not null ||
                reviewRequest.Trace || reviewRequest.Side is not null || reviewRequest.Cursor is not null || reviewRequest.Limit is not null ||
                reviewRequest.Detail is not null)) return Usage("Refresh cannot be combined with detail selectors.", asJson);
            var reviewRead = !refresh && !reviewRequest.Trace
                ? TrialReviewReads.ReadStored(trialReadProject, reviewRequest with { ProjectKey = "project" },
                    JobRunnerLaunchOptions.ResolveParserPath())
                : ProjectStoreCommand.Run<TrialReviewResult>(trialReadProject, CliProductVersion(), (database, project) =>
            {
                var commands = new TrialReviewCommands(database, JobRunnerLaunchOptions.ResolveParserPath());
                var key = ProjectWorkspaceKey.Compute(project);
                return refresh ? commands.ProduceAsync(new ReviewProduceRequest(key, positionals[1], reviewRequest.JobId,
                        reviewRequest.ReportId)).GetAwaiter().GetResult() : commands.Read(reviewRequest with { ProjectKey = key });
            });
            result = RenderCommand(reviewRead);
            break;

        case "hand-analysis":
            if (!positionals.SequenceEqual(new[] { "check" }) || !flags.TryGetValue("project", out var handProject) ||
                !flags.TryGetValue("input", out var handInput) || handInput == "true" ||
                flags.Keys.Any(key => key is not ("project" or "input" or "json")))
                return Usage(UsageLineFor("hand-analysis check"), asJson);
            string handJson;
            try { handJson = File.ReadAllText(handInput); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                result = RenderCommand(CommandOutcome<HandAnalysisCheckResponse>.Refused(new(
                    RefusalCodes.HandAnalysisInvalidLink, FailureReason.InvalidArgument,
                    "The hand-analysis input file cannot be read.", new Dictionary<string, string> { ["input"] = handInput })));
                break;
            }
            result = RenderCommand(HandAnalysisCommands.Check(new HandAnalysisCheckRequest(handProject, handJson)));
            break;

        case "writing-systems":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var writingSystemsProject))
                return Usage(UsageLineFor("writing-systems"), asJson);
            result = RenderCommand(WritingSystemsQuery.Query(new WritingSystemsRequest(writingSystemsProject)));
            break;

        case "guide":
            if (flags.TryGetValue("project", out var gdrAddProject) && positionals is ["add"])
            {
                bool GdrAddMissingValue(string name)
                {
                    var at = Array.IndexOf(args, "--" + name);
                    return at >= 0 && (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal));
                }
                if (!flags.TryGetValue("title", out var gdrAddTitle) || GdrAddMissingValue("title") ||
                    flags.Keys.Any(key => key is not ("project" or "title" or "parent" or "after" or "from-file" or "ws" or "draft" or "json" or "autonomous")) ||
                    new[] { "parent", "after", "from-file", "ws", "draft" }.Any(name => flags.ContainsKey(name) && GdrAddMissingValue(name)))
                    return Usage(UsageLineFor("guide add"), asJson);
                string? gdrAddBody = null;
                if (flags.TryGetValue("from-file", out var gdrAddFile))
                {
                    try
                    {
                        gdrAddBody = gdrAddFile == "-"
                            ? new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)).ReadToEnd()
                            : File.ReadAllText(gdrAddFile);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        return Usage($"The --from-file '{gdrAddFile}' cannot be read: {exception.Message}", asJson);
                    }
                }
                result = RenderCommand(GdrAddCommands.Add(new GdrAddRequest(gdrAddProject, gdrAddTitle, flags.GetValueOrDefault("parent"),
                    flags.GetValueOrDefault("after"), gdrAddBody, flags.GetValueOrDefault("ws"), flags.GetValueOrDefault("draft"))));
                break;
            }
            if (flags.TryGetValue("project", out var gdrEditProject) && positionals is ["edit" or "decide", var gdrEditId])
            {
                var gdrDraft = flags.GetValueOrDefault("draft");
                // A value flag left without its value parses as "true"; refuse that instead of recording it as text.
                bool GdrFlagMissingValue(string name)
                {
                    var at = Array.IndexOf(args, "--" + name);
                    return at >= 0 && (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal));
                }
                if (positionals[0] == "edit")
                {
                    if (!flags.TryGetValue("from-file", out var gdrFile) || GdrFlagMissingValue("from-file") ||
                        flags.Keys.Any(key => key is not ("project" or "from-file" or "draft" or "json" or "autonomous")) ||
                        gdrDraft == "true")
                        return Usage(UsageLineFor("guide edit"), asJson);
                    string gdrMarkdown;
                    try
                    {
                        gdrMarkdown = gdrFile == "-"
                            ? new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)).ReadToEnd()
                            : File.ReadAllText(gdrFile);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        return Usage($"The --from-file '{gdrFile}' cannot be read: {exception.Message}", asJson);
                    }
                    result = RenderCommand(GdrEditCommands.Edit(new GdrEditRequest(gdrEditProject, gdrEditId, gdrMarkdown, gdrDraft)));
                    break;
                }
                if (!flags.TryGetValue("choosing", out var gdrChoosing) || !flags.TryGetValue("over", out var gdrOver) ||
                    !flags.TryGetValue("because", out var gdrBecause) ||
                    GdrFlagMissingValue("choosing") || GdrFlagMissingValue("over") || GdrFlagMissingValue("because") ||
                    flags.Keys.Any(key => key is not ("project" or "choosing" or "over" or "because" or "draft" or "json" or "autonomous")) ||
                    gdrDraft == "true")
                    return Usage(UsageLineFor("guide decide"), asJson);
                result = RenderCommand(GdrEditCommands.Decide(new GdrDecideRequest(gdrEditProject, gdrEditId, gdrChoosing, gdrOver, gdrBecause, gdrDraft)));
                break;
            }
            if (!flags.TryGetValue("project", out var gdrProject) || positionals.Count is < 1 or > 2 ||
                (positionals[0] is "list" or "check" ? positionals.Count != 1 : positionals[0] != "show" || positionals.Count != 2) ||
                flags.ContainsKey("as-of") && positionals[0] != "show")
                return Usage(UsageLineFor("guide list") + " OR " + UsageLineFor("guide show") + " OR " + UsageLineFor("guide check") +
                    " OR " + UsageLineFor("guide edit") + " OR " + UsageLineFor("guide add") + " OR " + UsageLineFor("guide decide"), asJson);
            if (positionals[0] == "check" && (flags.Keys.Any(key => key is not ("project" or "json" or "autonomous" or "no-run" or "timeout" or "format" or "build")) ||
                    flags.GetValueOrDefault("no-run", "true") != "true" || flags.ContainsKey("no-run") && flags.ContainsKey("build") || flags.TryGetValue("timeout", out var gdrTimeoutText) &&
                    (!int.TryParse(gdrTimeoutText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gdrTimeout) || gdrTimeout < 1)))
                return Usage(UsageLineFor("guide check"), asJson);
            var gdrAsOf = flags.GetValueOrDefault("as-of");
            if (gdrAsOf == "true") return Usage(UsageLineFor("guide show"), asJson);
            if (flags.TryGetValue("format", out var gdrFormat))
            {
                if (positionals[0] != "show" || gdrFormat != "fw-md" || asJson)
                    return Usage("Use --format fw-md with guide show, without --json.", asJson);
                var shown = GdrQuery.Show(new GdrShowRequest(gdrProject, positionals[1], AsOf: gdrAsOf));
                result = shown.Succeeded
                    ? new CommandResult(0, new GdrMarkdownView(shown.Value!, Path.GetFileNameWithoutExtension(gdrProject)).Text)
                    : RenderCommand(shown);
                break;
            }
            result = positionals[0] switch
            {
                "list" => RenderCommand(GdrQuery.List(new GdrListRequest(gdrProject))),
                "check" => RenderCommand(GdrQuery.Check(new GdrCheckRequest(gdrProject, RunBlocks: !flags.ContainsKey("no-run"),
                    BlockTimeoutSeconds: flags.TryGetValue("timeout", out var gdrBlockTimeout) ? int.Parse(gdrBlockTimeout, CultureInfo.InvariantCulture) : 60,
                    BuildIndex: flags.ContainsKey("build")),
                    JobRunnerLaunchOptions.ResolveParserPath())),
                _ => RenderCommand(GdrQuery.Show(new GdrShowRequest(gdrProject, positionals[1], AsOf: gdrAsOf))),
            };
            break;

        case "source":
            if (!flags.TryGetValue("project", out var sourceProject) || positionals.Count < 1)
                return Usage(UsageLineFor("source list") + " OR " + UsageLineFor("source show") + " OR " + UsageLineFor("source where"), asJson);
            switch (positionals[0])
            {
                case "list" when positionals.Count == 1:
                    result = RenderCommand(SourceQuery.List(new SourceListRequest(sourceProject, flags.ContainsKey("all"))));
                    break;
                case "show" when positionals.Count == 2:
                    result = RenderCommand(SourceQuery.Show(new SourceShowRequest(sourceProject, positionals[1])));
                    break;
                case "passage" when positionals.Count == 1 && flags.TryGetValue("source", out var passageSource) &&
                    flags.TryGetValue("ref", out var passageRef):
                    if (flags.Keys.Any(key => key is not ("project" or "source" or "ref" or "limit" or "cursor" or "json")) ||
                        flags.TryGetValue("limit", out var passageLimitText) && !int.TryParse(passageLimitText, out _))
                        return Usage(UsageLineFor("source passage"), asJson);
                    result = RenderCommand(SourcePassageQuery.Query(new SourcePassageRequest(sourceProject, passageSource,
                        passageRef, flags.TryGetValue("limit", out var passageLimit) ? int.Parse(passageLimit) : 100,
                        flags.GetValueOrDefault("cursor"))));
                    break;
                case "where" when positionals.Count == 2:
                    result = RenderCommand(SourceQuery.Where(new SourceWhereRequest(
                        sourceProject, positionals[1], flags.GetValueOrDefault("source"))));
                    break;
                case "set-folder" when positionals.Count == 3:
                    result = RenderCommand(SourceLocalCommand.SetFolder(new SourceSetFolderRequest(sourceProject, positionals[1], positionals[2])));
                    if (result.ExitCode == 0) startRunner(sourceProject, error.WriteLine);
                    break;
                case "clear-folder" when positionals.Count == 2:
                    result = RenderCommand(SourceLocalCommand.ClearFolder(new SourceClearFolderRequest(sourceProject, positionals[1])));
                    if (result.ExitCode == 0) startRunner(sourceProject, error.WriteLine);
                    break;
                case "add" when positionals.Count == 2 && flags.ContainsKey("name") && flags.ContainsKey("versification") && flags.ContainsKey("context"):
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceAdd(new SourceAddRequest(
                        sourceProject, positionals[1], flags["name"], flags["versification"], flags["context"],
                        flags.GetValueOrDefault("kind", "Paratext"), flags.GetValueOrDefault("folder"))));
                    break;
                case "set-name" when positionals.Count == 3:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceSetName(
                        new SourceSetNameRequest(sourceProject, positionals[1], positionals[2])));
                    break;
                case "set-context" when positionals.Count == 3:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceSetContext(
                        new SourceSetContextRequest(sourceProject, positionals[1], positionals[2])));
                    break;
                case "set-versification" when positionals.Count == 3:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceSetVersification(
                        new SourceSetVersificationRequest(sourceProject, positionals[1], positionals[2])));
                    break;
                case "remove" when positionals.Count == 2:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceRemove(
                        new SourceRemoveRequest(sourceProject, positionals[1])));
                    break;
                case "restore" when positionals.Count == 2:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceRestore(
                        new SourceRestoreRequest(sourceProject, positionals[1])));
                    break;
                case "approve" when positionals.Count == 3:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceApprove(new SourceApproveRequest(
                        sourceProject, positionals[1], positionals[2], flags.GetValueOrDefault("analysis"),
                        flags.GetValueOrDefault("wordform"), flags.GetValueOrDefault("reading"), flags.ContainsKey("bulk"))));
                    break;
                case "clear" when positionals.Count == 3:
                    result = StageSource(sourceProject, () => ProposalCommands.StageSourceClearApproval(
                        new SourceClearApprovalRequest(sourceProject, positionals[1], positionals[2])));
                    break;
                default:
                    return Usage(string.Join(" OR ", new[]
                    {
                        "source list", "source show", "source passage", "source where", "source set-folder", "source clear-folder", "source add",
                        "source set-name", "source set-context", "source set-versification", "source remove", "source restore",
                        "source approve", "source clear",
                    }.Select(UsageLineFor)), asJson);
            }
            break;

        case "current":
            if (!flags.TryGetValue("project", out var currentProject) || positionals.Count < 1)
                return Usage(UsageLineFor("current get") + " OR " + UsageLineFor("current set") + " OR " + UsageLineFor("current set --clear"), asJson);
            switch (positionals[0])
            {
                case "get" when positionals.Count == 1:
                    result = RenderCommand(SourceQuery.CurrentGet(new CurrentGetRequest(currentProject, flags.GetValueOrDefault("source"))));
                    break;
                case "set" when positionals.Count == 3 && !flags.ContainsKey("clear"):
                    result = RenderCommand(SourceLocalCommand.SetCurrent(new CurrentSetRequest(currentProject, positionals[1], positionals[2])));
                    break;
                case "set" when positionals.Count == 2 && flags.ContainsKey("clear"):
                    result = RenderCommand(SourceLocalCommand.ClearCurrent(new CurrentClearRequest(currentProject, positionals[1])));
                    break;
                default:
                    return Usage(UsageLineFor("current get") + " OR " + UsageLineFor("current set") + " OR " + UsageLineFor("current set --clear"), asJson);
            }
            break;

        case "query":
        {
            if (flags.TryGetValue("values", out var valuesKind))
            {
                if (valuesKind != "classes" || positionals.Count != 0 || flags.Keys.Any(key => key is not ("values" or "project" or "grammar" or "json" or "verbose")))
                    return Usage(UsageLineFor("query values"), asJson);
                result = RenderCommand(PhonValues.Read(new QueryValuesRequest(flags.GetValueOrDefault("project"), flags.GetValueOrDefault("grammar")),
                    flags.ContainsKey("verbose") ? error.WriteLine : null));
                break;
            }
            if (CorpusQueryCli.Handles(flags) && CorpusQueryCli.IsForms(positionals))
            {
                if (!CorpusQueryCli.TryBuildFormsRequest(flags, positionals, source =>
                    {
                        try
                        {
                            return source == "-" ? new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)).ReadToEnd() : File.ReadAllText(source);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
                    }, out var corpusFormsRequest))
                    return Usage(UsageLineFor("query corpus forms"), asJson);
                result = RenderCommand(CorpusQueryCommand.Forms(corpusFormsRequest, JobRunnerLaunchOptions.ResolveParserPath(), notice: error.WriteLine));
                break;
            }
            if (CorpusQueryCli.Handles(flags))
            {
                if (!CorpusQueryCli.TryBuildRequest(flags, positionals, out var corpusRequest)) return Usage(UsageLineFor("query pairs"), asJson);
                result = RenderCommand(CorpusQueryCommand.Pairs(corpusRequest));
                break;
            }
            var queryUsage = UsageLineFor("query forms");
            if (flags.ContainsKey("schema"))
            {
                if (positionals.Count != 0 || flags.Keys.Any(key => key is not ("schema" or "json"))) return Usage(queryUsage, asJson);
                result = new CommandResult(0, QuerySchema.Generate());
                break;
            }
            QueryArguments? queryArguments;
            if (!flags.TryGetValue("project", out var queryProject)) return Usage(queryUsage, asJson);
            if (flags.TryGetValue("query-json", out var queryJsonSource))
            {
                if (positionals.Count != 0 || flags.Keys.Any(key => key is not ("project" or "query-json" or "json")))
                    return Usage(queryUsage, asJson);
                string queryJson;
                try
                {
                    queryJson = queryJsonSource == "-"
                        ? new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)).ReadToEnd()
                        : File.ReadAllText(queryJsonSource);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return Usage($"The --query-json '{queryJsonSource}' cannot be read: {exception.Message}", asJson);
                }
                QueryRecord? documentRecord = null;
                Refusal? documentRefusal;
                try
                {
                    if (QueryDocument.TryParse(queryJson, out var queryDocument, out documentRefusal))
                        documentRecord = queryDocument.ToRecord();
                }
                catch (QueryRefusalException refused)
                {
                    documentRefusal = refused.Refusal;
                }
                if (documentRecord is null)
                {
                    result = RenderCommand(CommandOutcome<FormsQueryResponse>.Refused(documentRefusal!));
                    break;
                }
                queryArguments = new QueryArguments(documentRecord, queryProject, documentRecord.AsOf, false, false, null);
            }
            else if (!QueryRunLine.TryBuildQuery(flags, positionals, rest, true, out queryArguments, out var buildRefusal))
            {
                if (buildRefusal.Code != RefusalCodes.QueryNotYetAvailable) return Usage(queryUsage, asJson);
                result = RenderCommand(CommandOutcome<FormsQueryResponse>.Refused(buildRefusal));
                break;
            }
            var record = queryArguments.Record;
            if (record.AsOf is not null || record.Sides is not null) asJson = true;
            if (queryArguments.MembersFile is { } membersFile)
            {
                if (!File.Exists(membersFile)) return Usage(queryUsage, asJson);
                record = record with { Members = [.. File.ReadLines(membersFile).Select(line => line.Trim()).Where(line => line.Length > 0)] };
            }
            var groups = record.Group;
            var queryOutcome = FormsQueryCommand.Run(new FormsQueryRequest(queryProject, CliProductVersion(), record));
            if (flags.ContainsKey("test-result") && (record.Score?.Contains("control") != true || record.Ids || record.Group.Count > 0))
                return Usage(queryUsage, asJson);
            if (flags.ContainsKey("test-result") && queryOutcome.Succeeded)
            {
                var control = queryOutcome.Value!.Result.Scores!.First(score => score.Scorer == "control");
                var testResult = new HypothesisTestResult(control.Verdict!, (int)control.Counts["predicted"], (int)control.Counts["rule_hits"],
                    control.Controls!.ToDictionary(pair => pair.Key, pair => (int)pair.Value), (int)control.Counts["margin"]);
                result = new CommandResult(0, JsonSerializer.Serialize(testResult, HypothesisFileOptions) + Environment.NewLine);
            }
            else if (asJson || !queryOutcome.Succeeded) result = RenderCommand(queryOutcome);
            else
            {
                alreadyRendered = true;
                var response = queryOutcome.Value!;
                var lines = new List<string>();
                if (response.IsStale && record.Ids) Console.Error.WriteLine("This evidence is stale: a newer Trial or project save exists.");
                if (!record.Ids) lines.Add($"Trial {response.TrialId} | grammar {response.GrammarHash}");
                if (response.IsStale && !record.Ids) lines.Add("This evidence is stale: a newer Trial or project save exists.");
                foreach (var note in response.NotMeasured) (record.Ids ? (Action<string>)Console.Error.WriteLine : lines.Add)(note);
                lines.AddRange(QueryEvidenceText.Scores(response.Result.Scores ?? []));
                if (response.Result.Evidence is { } evidenceScreen) lines.AddRange(QueryEvidenceText.Evidence(evidenceScreen));
                var columns = QuerySources.StoredColumns(record.From == "forms" ? "stems" : record.From);
                if (record.Change is not null) columns = [.. columns, "predicted", "evidence", "collides"];
                string Item(QueryItem item) => item.Id + "\t" + string.Join("\t", columns.Select(c => string.Join(",", item.Values[c])));
                if (response.Result.Phon is { } phonReport)
                {
                    var phonHeader = new StringBuilder();
                    PhonText.Append(phonHeader, phonReport);
                    var phonLines = phonHeader.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'));
                    foreach (var line in phonLines) (record.Ids ? (Action<string>)Console.Error.WriteLine : lines.Add)(line);
                }
                if (record.Evidence) { }
                else if (record.Ids)
                    lines.AddRange(response.Result.Ids);
                else if (groups.Count == 0 && record.From != "forms")
                    lines.AddRange((response.Result.Items ?? []).Select(Item));
                else if (groups.Count == 0)
                    lines.AddRange(response.Result.Rows.Select(row =>
                        $"{row.Form}\t{row.Base}\t{row.Kind}\t{row.Id}\t{row.Entry}\t{row.MorphType}\t" +
                        $"{row.VariantType}\t{string.Join(",", row.Environments)}\t{string.Join(",", row.Category)}" +
                        (row.Matches is { Count: > 0 } ? "\t" + PhonText.Spans(row.Matches) : "")));
                else
                    foreach (var group in response.Result.Groups)
                    {
                        lines.Add($"{string.Join(", ", group.Key)}\t{group.Count}\t{new string('█', Math.Min(group.Count, 40))}" +
                            (group.Tokens is { } tokens ? $"\ttokens={tokens}" : ""));
                        foreach (var example in group.Examples) lines.Add($"  {example.Form}" +
                            (example.Tokens is { } exampleTokens ? $"\t{exampleTokens}" : ""));
                        foreach (var sample in group.Samples ?? []) lines.Add("  " + Item(sample));
                    }
                result = new CommandResult(0, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            break;
        }

        case "word-context":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var contextProject) ||
                !flags.TryGetValue("word", out var contextWord) || string.IsNullOrWhiteSpace(contextWord))
                return Usage(UsageLineFor("word-context"), asJson);
            result = RenderCommand(WordContextQuery.Query(new WordContextRequest(contextProject, contextWord)));
            break;

        case "lexical-entry":
            if (positionals.Count == 0 || !flags.TryGetValue("project", out var entryProject))
                return Usage(UsageLineFor("lexical-entry"), asJson);
            result = RenderCommand(LexicalEntryQuery.Query(new LexicalEntryRequest(entryProject, positionals)));
            break;

        case "phonology-examples":
            if (positionals.Count < 1 || !flags.TryGetValue("project", out var phonologyProject))
                return Usage(UsageLineFor("phonology-examples save") + " OR " +
                    UsageLineFor("phonology-examples list") + " OR " +
                    UsageLineFor("phonology-examples show") + " OR " +
                    UsageLineFor("phonology-examples check") + " OR " +
                    UsageLineFor("phonology-examples results"), asJson);
            var phonologyPageSize = 20;
            if (flags.TryGetValue("page-size", out var phonologyPageSizeText) &&
                (!int.TryParse(phonologyPageSizeText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out phonologyPageSize) || phonologyPageSize is < 1 or > PhonologyExampleValidation.MaxPageSize))
                return Usage("--page-size must be between 1 and " +
                    PhonologyExampleValidation.MaxPageSize.ToString(CultureInfo.InvariantCulture) + ".", asJson);
            switch (positionals[0])
            {
                case "save":
                    if (positionals.Count != 1 || !flags.TryGetValue("file", out var phonologyFile) ||
                        !File.Exists(phonologyFile))
                        return Usage(UsageLineFor("phonology-examples save"), asJson);
                    try
                    {
                        var payload = ReadBoundedUtf8File(phonologyFile,
                            PhonologyExampleValidation.MaxPayloadUtf8Bytes);
                        var request = PhonologyExampleValidation.ParseSaveFile(payload, phonologyProject);
                        result = RenderCommand(PhonologyExampleCommands.Save(request));
                    }
                    catch (Exception exception) when (exception is ArgumentException or IOException or
                        DecoderFallbackException)
                    {
                        return Usage("The Example Set file is invalid: " + exception.Message, asJson);
                    }
                    break;
                case "list":
                    if (positionals.Count != 1)
                        return Usage(UsageLineFor("phonology-examples list"), asJson);
                    PhonologySetListCursor? listCursor;
                    try
                    {
                        listCursor = flags.TryGetValue("continuation", out var listCursorJson)
                            ? DeserializePhonologyCursor<PhonologySetListCursor>(listCursorJson)
                            : null;
                        if (flags.ContainsKey("continuation") && listCursor is null)
                            return Usage("--continuation must contain a list cursor JSON object.", asJson);
                    }
                    catch (JsonException exception)
                    {
                        return Usage("--continuation is not a valid list cursor: " + exception.Message, asJson);
                    }
                    result = RenderCommand(PhonologyExampleCommands.List(new ListPhonologyExamplesRequest(
                        phonologyProject, phonologyPageSize, listCursor)));
                    break;
                case "show":
                    if (positionals.Count != 2)
                        return Usage(UsageLineFor("phonology-examples show"), asJson);
                    PhonologyPageCursor? pageCursor;
                    try
                    {
                        pageCursor = flags.TryGetValue("continuation", out var pageCursorJson)
                            ? DeserializePhonologyCursor<PhonologyPageCursor>(pageCursorJson)
                            : null;
                        if (flags.ContainsKey("continuation") && pageCursor is null)
                            return Usage("--continuation must contain a revision page cursor JSON object.", asJson);
                    }
                    catch (JsonException exception)
                    {
                        return Usage("--continuation is not a valid revision page cursor: " + exception.Message, asJson);
                    }
                    result = RenderCommand(PhonologyExampleCommands.Show(new ShowPhonologyExamplesRequest(
                        phonologyProject, positionals[1], phonologyPageSize, pageCursor)));
                    break;
                case "check":
                    if (positionals.Count != 2)
                        return Usage(UsageLineFor("phonology-examples check"), asJson);
                    if (flags.ContainsKey("step-cap"))
                        return Usage("--step-cap was removed with PanGloss 0.14.0; use --work-cap and --timeout-ms.", asJson);
                    var checkLimits = PhonologyExampleValidation.DefaultLimits;
                    if (!TryReadCheckCeiling(flags, "work-cap", checkLimits.WorkCap, out var checkWorkCap) ||
                        !TryReadCheckCeiling(flags, "timeout-ms", checkLimits.TimeoutMs, out var checkTimeout) ||
                        checkTimeout > uint.MaxValue)
                        return Usage("--work-cap and --timeout-ms must be whole numbers.", asJson);
                    var checkWaitTimeout = JobCommands.DefaultWaitTimeout;
                    if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                        UsageLineFor("phonology-examples check"), asJson, out checkWaitTimeout) is { } checkUsage)
                        return checkUsage;
                    var queuedCheck = PhonologyExampleCommands.Check(new PhonologyCheckRequest(phonologyProject,
                        positionals[1], null, new PhonologyLimits(checkWorkCap, (uint)checkTimeout)),
                        SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());
                    result = RenderCommand(queuedCheck);
                    if (queuedCheck.Succeeded)
                    {
                        startRunner(phonologyProject, error.WriteLine);
                        if (flags.ContainsKey("wait"))
                            result = RenderCommand(JobCommands.WaitForJob(new WaitForJobRequest(phonologyProject,
                                queuedCheck.Value!.JobId, CliProductVersion(), checkWaitTimeout),
                                reportStall: error.WriteLine));
                    }
                    break;
                case "results":
                    if (positionals.Count != 2)
                        return Usage(UsageLineFor("phonology-examples results"), asJson);
                    PhonologyResultsRead resultsRead;
                    if (flags.TryGetValue("example", out var resultsExample))
                    {
                        if (flags.ContainsKey("page-size") || flags.ContainsKey("continuation") ||
                            string.IsNullOrWhiteSpace(resultsExample))
                            return Usage("--example reads one complete example and cannot be combined with " +
                                "--page-size or --continuation.", asJson);
                        resultsRead = new PhonologyResultsRead.Example(resultsExample);
                    }
                    else
                    {
                        PhonologyPageCursor? resultsCursor;
                        try
                        {
                            resultsCursor = flags.TryGetValue("continuation", out var resultsCursorJson)
                                ? DeserializePhonologyCursor<PhonologyPageCursor>(resultsCursorJson)
                                : null;
                            if (flags.ContainsKey("continuation") && resultsCursor is null)
                                return Usage("--continuation must contain a results page cursor JSON object.", asJson);
                        }
                        catch (JsonException exception)
                        {
                            return Usage("--continuation is not a valid results page cursor: " + exception.Message, asJson);
                        }
                        resultsRead = new PhonologyResultsRead.Page(phonologyPageSize, resultsCursor);
                    }
                    var resultsOutcome = PhonologyExampleCommands.Results(new PhonologyResultsRequest(
                        phonologyProject, positionals[1], resultsRead));
                    if (flags.TryGetValue("out", out var resultsOut) && resultsOutcome.Succeeded)
                    {
                        try
                        {
                            File.WriteAllText(resultsOut, ProjectionJson.Serialize(resultsOutcome.Value!) +
                                Environment.NewLine, new UTF8Encoding(false));
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            return Usage("--out could not be written: " + exception.Message, asJson);
                        }
                    }
                    result = RenderCommand(resultsOutcome);
                    break;
                default:
                    return Usage(UsageLineFor("phonology-examples save") + " OR " +
                        UsageLineFor("phonology-examples list") + " OR " +
                        UsageLineFor("phonology-examples show") + " OR " +
                        UsageLineFor("phonology-examples check") + " OR " +
                        UsageLineFor("phonology-examples results"), asJson);
            }
            break;

        case "delete":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var deleteProject) ||
                !flags.TryGetValue("proposal", out var deleteProposal) || string.IsNullOrWhiteSpace(deleteProposal))
                return Usage(UsageLineFor("delete --proposal"), asJson);
            result = RenderCommand(ProposalLifecycleCommand.Delete(new DeleteProposalRequest(deleteProject,
                deleteProposal)));
            break;

        case "proposals":
            if (!flags.TryGetValue("project", out var proposalsProject))
                return Usage(UsageLineFor("proposals"), asJson);
            result = RenderCommand(ProposalStandingsCommand.Read(new ProposalStandingsRequest(
                proposalsProject, flags.ContainsKey("include-set-aside"))));
            break;

        case "request":
            if (positionals.Count >= 1 && positionals[0] == "create")
            {
                if (positionals.Count != 1 || !flags.TryGetValue("project", out var createRequestProject) ||
                    !flags.TryGetValue("kind", out var requestKind) || !flags.TryGetValue("target", out var requestTarget) ||
                    !flags.TryGetValue("label", out var requestLabel))
                    return Usage(UsageLineFor("request create"), asJson);
                result = RenderCommand(RequestCommands.Create(new CreateRequestRequest(createRequestProject,
                    CliProductVersion(), requestKind, requestTarget, requestLabel, flags.GetValueOrDefault("note"))));
                break;
            }
            if (positionals.Count == 2 && positionals[0] is "withdraw" or "ask-again" &&
                flags.TryGetValue("project", out var requestProject))
            {
                result = positionals[0] == "withdraw"
                    ? RenderCommand(RequestCommands.Withdraw(
                        new WithdrawRequestRequest(requestProject, CliProductVersion(), positionals[1])))
                    : RenderCommand(RequestCommands.AskAgain(
                        new AskAgainRequest(requestProject, CliProductVersion(), positionals[1])));
                break;
            }
            return Usage("Usage: motif request (create | withdraw | ask-again) ... --project <fwdata>", asJson);

        case "hypothesis":
        {
            var sub = positionals.Count > 0 ? positionals[0] : "";
            var hypothesisId = positionals.Count > 1 ? positionals[1] : null;
            if (!flags.TryGetValue("project", out var hypothesisProject) || sub == "" ||
                (sub is "add" or "neighbours" or "report" ? positionals.Count != 1 : positionals.Count != 2))
                return Usage("Usage: motif hypothesis (add | test | decide | link | grade | answer | neighbours | report) ... --project <fwdata>", asJson);
            string[]? Split(string? list) => list?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            switch (sub)
            {
                case "add":
                case "test":
                    if (!flags.TryGetValue("file", out var hypothesisFile) || !File.Exists(hypothesisFile))
                        return Usage(UsageLineFor("hypothesis " + sub), asJson);
                    var fileJson = File.ReadAllText(hypothesisFile);
                    try
                    {
                        result = sub == "add"
                            ? RenderCommand(HypothesisCommands.Add(new AddHypothesisRequest(hypothesisProject, CliProductVersion(),
                                JsonSerializer.Deserialize<HypothesisDraft>(fileJson, HypothesisFileOptions)!)))
                            : RenderCommand(HypothesisCommands.RecordTest(new RecordHypothesisTestRequest(hypothesisProject,
                                CliProductVersion(), hypothesisId!,
                                JsonSerializer.Deserialize<HypothesisTestResult>(fileJson, HypothesisFileOptions)! with
                                {
                                    Command = flags.GetValueOrDefault("command") ??
                                        JsonSerializer.Deserialize<HypothesisTestResult>(fileJson, HypothesisFileOptions)!.Command,
                                })));
                    }
                    catch (Exception exception) when (exception is JsonException or NullReferenceException)
                    {
                        return Usage($"The file '{hypothesisFile}' is not a valid {(sub == "add" ? "hypothesis" : "test result")} " +
                            "record. " + UsageLineFor("hypothesis " + sub), asJson);
                    }
                    break;
                case "decide":
                    if (!flags.TryGetValue("status", out var decideStatus) || !flags.TryGetValue("decision", out var decideText))
                        return Usage(UsageLineFor("hypothesis decide"), asJson);
                    result = RenderCommand(HypothesisCommands.Decide(new DecideHypothesisRequest(hypothesisProject,
                        CliProductVersion(), hypothesisId!, decideStatus, decideText, flags.GetValueOrDefault("superseded-by"),
                        Split(flags.GetValueOrDefault("supersedes")))));
                    break;
                case "link":
                    result = RenderCommand(HypothesisCommands.Link(new LinkHypothesisRequest(hypothesisProject,
                        CliProductVersion(), hypothesisId!, Split(flags.GetValueOrDefault("proposal")),
                        flags.GetValueOrDefault("guide"), flags.GetValueOrDefault("guide-section"), flags.GetValueOrDefault("guide-none"))));
                    break;
                case "grade":
                    int? Count(string name) => int.TryParse(flags.GetValueOrDefault(name), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
                    double? Hours(string name) => double.TryParse(flags.GetValueOrDefault(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : null;
                    foreach (var name in new[] { "word-questions", "objects-before", "types-before", "objects-after", "types-after" })
                        if (flags.ContainsKey(name) && Count(name) is null) return Usage(UsageLineFor("hypothesis grade"), asJson);
                    foreach (var name in new[] { "agent-hours", "alternative-hours" })
                        if (flags.ContainsKey(name) && Hours(name) is null) return Usage(UsageLineFor("hypothesis grade"), asJson);
                    result = RenderCommand(HypothesisCommands.Grade(new GradeHypothesisRequest(hypothesisProject,
                        CliProductVersion(), hypothesisId!, flags.GetValueOrDefault("sealed-verdict"),
                        flags.GetValueOrDefault("verdict-source"), Count("word-questions"), Count("objects-before"),
                        Count("types-before"), Count("objects-after"), Count("types-after"), Hours("agent-hours"),
                        Hours("alternative-hours"))));
                    break;
                case "answer":
                    if (!int.TryParse(flags.GetValueOrDefault("question"), out var questionNumber) ||
                        !flags.TryGetValue("answer", out var questionAnswer))
                        return Usage(UsageLineFor("hypothesis answer"), asJson);
                    result = RenderCommand(HypothesisCommands.AnswerQuestion(new AnswerHypothesisQuestionRequest(
                        hypothesisProject, CliProductVersion(), hypothesisId!, questionNumber, questionAnswer)));
                    break;
                case "neighbours":
                    if (!flags.TryGetValue("change", out var neighbourChange))
                        return Usage(UsageLineFor("hypothesis neighbours"), asJson);
                    result = RenderCommand(HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(hypothesisProject,
                        CliProductVersion(), neighbourChange, flags.GetValueOrDefault("environment"),
                        flags.GetValueOrDefault("host-class"), flags.GetValueOrDefault("slot"))));
                    break;
                case "report":
                    if (!flags.TryGetValue("kind", out var hypKind))
                        return Usage(UsageLineFor("hypothesis report"), asJson);
                    result = RenderCommand(HypothesisCommands.Report(new HypothesisReportRequest(
                        hypothesisProject, CliProductVersion(), hypKind)));
                    break;
                default:
                    return Usage("Usage: motif hypothesis (add | test | decide | link | grade | answer | neighbours | report) ... --project <fwdata>", asJson);
            }
            break;
        }

        case "wordform":
        {
            var wordformCommand = positionals.Count > 0 ? positionals[0] : "";
            if (wordformCommand == "dispose" && flags.ContainsKey("from-file"))
            {
                if (!flags.TryGetValue("project", out var bulkProject) || positionals.Count != 1 ||
                    flags.Keys.Any(flag => flag is not ("project" or "json" or "autonomous" or "from-file" or "format" or "input-format" or
                        "replace" or "dry-run" or "skip-refused" or "by")) ||
                    flags["from-file"] == "true" || flags.TryGetValue("dry-run", out var dryRunValue) && dryRunValue != "true")
                    return Usage(UsageLineFor("wordform dispose --from-file"), asJson);
                var inputFormat = flags.GetValueOrDefault("input-format");
                var outputLines = false;
                if (flags.TryGetValue("format", out var bulkFormat))
                {
                    if (bulkFormat == "json") outputLines = true;
                    else if (bulkFormat is "tsv" or "jsonl" or "ledger" && inputFormat is null) inputFormat = bulkFormat;
                    else return Usage("--format must be tsv, jsonl or ledger (the file's format) or json (one JSON object per row).", asJson);
                }
                var bulk = WordTypeDispositionCommands.RecordFromFile(new DisposeFromFileRequest(bulkProject, CliProductVersion(),
                    flags["from-file"], inputFormat, flags.ContainsKey("replace"), flags.ContainsKey("dry-run"),
                    flags.ContainsKey("skip-refused"), outputLines, flags.GetValueOrDefault("by")));
                result = RenderCommand(bulk, successAsJson: !outputLines);
                if (outputLines && bulk.Succeeded)
                    result = result with { Output = DispositionRenderer.RenderFromFileLines(bulk.Value!) };
                if (bulk.Value is { Refused: > 0, Written: false } && !flags.ContainsKey("skip-refused"))
                    result = result with { ExitCode = FailureEnvelope.ExitCodeFor(FailureReason.Refused), Reason = FailureReason.Refused };
                break;
            }
            if (wordformCommand is not ("dispose" or "undispose") || positionals.Count < 2 ||
                !flags.TryGetValue("project", out var wordformProject))
                return Usage("Usage: motif wordform (dispose | undispose) <form>... --project <fwdata>", asJson);
            var wordformFlags = wordformCommand == "dispose"
                ? new[] { "project", "json", "autonomous", "as", "evidence", "intended", "replace", "by" }
                : new[] { "project", "json", "autonomous", "reason", "by" };
            if (flags.Keys.Any(flag => !wordformFlags.Contains(flag)))
                return Usage(UsageLineFor("wordform " + wordformCommand), asJson);
            var wordForms = positionals.Skip(1).ToArray();
            if (wordformCommand == "undispose")
            {
                result = RenderCommand(WordTypeDispositionCommands.Clear(new UndisposeWordFormsRequest(wordformProject,
                    CliProductVersion(), wordForms, flags.GetValueOrDefault("reason"), flags.GetValueOrDefault("by"))));
                break;
            }
            if (!flags.TryGetValue("as", out var wordformCategory) || !flags.TryGetValue("evidence", out var wordformEvidence))
                return Usage(UsageLineFor("wordform dispose"), asJson);
            result = RenderCommand(WordTypeDispositionCommands.Record(new DisposeWordFormsRequest(wordformProject,
                CliProductVersion(), wordForms, wordformCategory, wordformEvidence, flags.GetValueOrDefault("intended"),
                flags.ContainsKey("replace"), flags.GetValueOrDefault("by"))));
            break;
        }

        case "coverage":
        {
            var coverageUsage = UsageLineFor("coverage");
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var coverageProject) ||
                !flags.TryGetValue("ledger", out var coverageLedger) || !flags.TryGetValue("sidecar", out var coverageSidecar) ||
                !flags.TryGetValue("frequency", out var coverageFrequency) ||
                flags.Keys.Any(flag => flag is not ("project" or "json" or "autonomous" or "ledger" or "sidecar" or "frequency" or
                    "frequency-format" or "audit-dir" or "protected" or "limit" or "tokens")))
                return Usage(coverageUsage, asJson);
            var coverageLimit = CoverageCommands.DefaultLimit;
            if (flags.TryGetValue("limit", out var coverageLimitText) &&
                !int.TryParse(coverageLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out coverageLimit))
                return Usage("--limit must be a positive integer.", asJson);
            result = RenderCommand(CoverageCommands.Coverage(new CoverageRequest(coverageProject, CliProductVersion(), coverageLedger,
                coverageSidecar, coverageFrequency, flags.GetValueOrDefault("frequency-format"), flags.GetValueOrDefault("audit-dir"),
                flags.GetValueOrDefault("protected"), coverageLimit, flags.ContainsKey("tokens"))));
            break;
        }

        case "disposition":
        {
            var dispositionCommand = positionals.Count > 0 ? positionals[0] : "";
            if (dispositionCommand is not ("show" or "list") || !flags.TryGetValue("project", out var dispositionProject) ||
                positionals.Count != (dispositionCommand == "show" ? 2 : 1) ||
                flags.Keys.Any(flag => flag is not ("project" or "json" or "autonomous" or "as" or "limit" or "page" or "format")) ||
                dispositionCommand == "show" && flags.Keys.Any(flag => flag is "as" or "limit" or "page" or "format"))
                return Usage("Usage: motif disposition (show <form> | list) --project <fwdata>", asJson);
            if (dispositionCommand == "show")
            {
                result = RenderCommand(WordTypeDispositionCommands.Show(
                    new ShowDispositionRequest(dispositionProject, CliProductVersion(), positionals[1])));
                break;
            }
            if (flags.TryGetValue("format", out var dispositionFormat))
            {
                if (dispositionFormat is not ("table" or "json")) return Usage("--format must be table or json.", asJson);
                if (dispositionFormat == "json") asJson = true;
            }
            var dispositionLimit = WordTypeDispositionCommands.DefaultLimit;
            if (flags.TryGetValue("limit", out var dispositionLimitText) &&
                !int.TryParse(dispositionLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out dispositionLimit))
                return Usage("--limit must be a positive integer.", asJson);
            var dispositionPage = 1;
            if (flags.TryGetValue("page", out var dispositionPageText) &&
                !int.TryParse(dispositionPageText, NumberStyles.None, CultureInfo.InvariantCulture, out dispositionPage))
                return Usage("--page must be a positive integer.", asJson);
            result = RenderCommand(WordTypeDispositionCommands.List(new ListDispositionsRequest(dispositionProject,
                CliProductVersion(), flags.GetValueOrDefault("as"), dispositionLimit, dispositionPage)));
            break;
        }

        case "warnings":
            if (!flags.TryGetValue("project", out var warningsProject) ||
                (flags.TryGetValue("kind", out var warningKind) && string.IsNullOrWhiteSpace(warningKind)))
                return Usage(UsageLineFor("warnings"), asJson);
            var (warningsWindow, warningsPagingRefusal) = PagingFlags.Read(flags, ResultPaging.HeavyRowLimit);
            if (warningsPagingRefusal is not null)
            {
                result = RenderCommand(CommandOutcome<WarningsResponse>.Refused(warningsPagingRefusal));
                break;
            }
            result = RenderCommand(WarningsCommand.Warnings(new WarningsRequest(
                warningsProject, flags.GetValueOrDefault("kind"), flags.ContainsKey("left-out"),
                flags.GetValueOrDefault("proposal"))).Map(warnings => warningsWindow.All
                ? warnings : warnings.Window(warningsWindow.Offset, warningsWindow.Limit).WithNextStep(
                    next => PagingFlags.NextCommand("warnings", [], flags, next, warningsWindow.Limit))));
            break;

        case "difference":
            if (positionals.Count == 1 && positionals[0] == "produce")
            {
                if (!flags.TryGetValue("project", out var differenceProject) ||
                    !flags.TryGetValue("proposal", out var differenceProposal) ||
                    !flags.TryGetValue("text", out var differenceText))
                    return Usage(UsageLineFor("difference produce"), asJson);
                result = RenderCommand(ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(
                    differenceProject, differenceProposal, differenceText, flags.GetValueOrDefault("job")),
                    flags.GetValueOrDefault("reference") ?? ProposalDifferenceReferences.Human), asJson);
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "show")
            {
                var differenceOffset = 0;
                var differenceLimit = 25;
                int? occurrenceOffset = null;
                int? occurrenceLimit = null;
                if (!flags.TryGetValue("project", out var deltaShowProject) ||
                    !(flags.ContainsKey("proposal") || flags.ContainsKey("difference")) ||
                    flags.TryGetValue("offset", out var differenceOffsetText) &&
                    !int.TryParse(differenceOffsetText, NumberStyles.None, CultureInfo.InvariantCulture, out differenceOffset) ||
                    flags.TryGetValue("limit", out var differenceLimitText) &&
                    !int.TryParse(differenceLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out differenceLimit) ||
                    flags.TryGetValue("occurrence-offset", out var occurrenceOffsetText) &&
                    !TryParseCount(occurrenceOffsetText, out occurrenceOffset) ||
                    flags.TryGetValue("occurrence-limit", out var occurrenceLimitText) &&
                    !TryParseCount(occurrenceLimitText, out occurrenceLimit))
                    return Usage(UsageLineFor("difference show"), asJson);
                result = RenderCommand(ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(deltaShowProject,
                    flags.GetValueOrDefault("proposal"), flags.GetValueOrDefault("difference"),
                    flags.ContainsKey("could-be-bad"), flags.GetValueOrDefault("bucket"),
                    flags.GetValueOrDefault("before"), flags.GetValueOrDefault("after"), differenceOffset,
                    differenceLimit, flags.GetValueOrDefault("limitation"), flags.GetValueOrDefault("word"),
                    occurrenceOffset, occurrenceLimit, flags.GetValueOrDefault("text"),
                    flags.GetValueOrDefault("reference"))), asJson);
                break;
            }
            return Usage(UsageLineFor("difference"), asJson);

        case "lexicon":
            var lexiconFound = LexiconFindCli.Run(positionals, flags, asJson);
            if (lexiconFound is null) return Usage(UsageLineFor("lexicon find"), asJson);
            result = lexiconFound;
            alreadyRendered = true;
            break;

        case "grammar":
            if (positionals.Count == 1 && positionals[0] == "read")
            {
                if (flags.Keys.Any(name => name is not ("project" or "kind" or "id" or "query" or "offset" or "limit" or "json" or "exact" or
                    "morph-type" or "page" or "at")) ||
                    !flags.TryGetValue("project", out var grammarReadProject) ||
                    !flags.TryGetValue("kind", out var grammarReadKindText) ||
                    !Enum.TryParse<GrammarReadKind>(grammarReadKindText == "phonological_rules" ? "phonological_rule" : grammarReadKindText,
                        ignoreCase: false, out var grammarReadKind) ||
                    !Enum.IsDefined(grammarReadKind))
                    return Usage(UsageLineFor("grammar read"), asJson);
                var grammarReadOffset = 0;
                var grammarReadLimit = 25;
                if (flags.TryGetValue("offset", out var grammarReadOffsetText) &&
                    !int.TryParse(grammarReadOffsetText, NumberStyles.None, CultureInfo.InvariantCulture, out grammarReadOffset) ||
                    flags.TryGetValue("limit", out var grammarReadLimitText) &&
                    !int.TryParse(grammarReadLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out grammarReadLimit))
                    return Usage(UsageLineFor("grammar read"), asJson);
                if (flags.TryGetValue("page", out var grammarReadPageText))
                {
                    if (flags.ContainsKey("offset"))
                        return Usage("Give --page or --offset, not both.", asJson);
                    if (!int.TryParse(grammarReadPageText, NumberStyles.None, CultureInfo.InvariantCulture, out var grammarReadPage) ||
                        grammarReadPage < 1 || (long)(grammarReadPage - 1) * Math.Max(grammarReadLimit, 1) > int.MaxValue)
                        return Usage("--page must be a positive integer.", asJson);
                    grammarReadOffset = (grammarReadPage - 1) * grammarReadLimit;
                }
                var grammarRead = GrammarReadQuery.Query(new GrammarReadRequest(
                    grammarReadProject,
                    grammarReadKind,
                    flags.GetValueOrDefault("id"),
                    flags.GetValueOrDefault("query"),
                    grammarReadOffset,
                    grammarReadLimit,
                    flags.GetValueOrDefault("exact"),
                    flags.GetValueOrDefault("morph-type"),
                    flags.GetValueOrDefault("at")));
                if (asJson || !grammarRead.Succeeded)
                    result = RenderCommand(grammarRead, asJson);
                else
                {
                    alreadyRendered = true;
                    result = new CommandResult(0, GrammarReadTextRenderer.Render(grammarRead.Value!));
                }
                break;
            }
            if (positionals.Count != 1 || positionals[0] != "check" ||
                !flags.TryGetValue("project", out var grammarProject))
                return Usage(UsageLineFor("grammar check"), asJson);
            StoredAnalysisComparison? storedAnalyses = null;
            if (flags.TryGetValue("stored-analyses", out var storedAnalysesWord))
            {
                storedAnalyses = storedAnalysesWord switch
                {
                    "budget" => StoredAnalysisComparison.Budget,
                    "all" => StoredAnalysisComparison.All,
                    "off" => StoredAnalysisComparison.Off,
                    _ => null,
                };
                if (storedAnalyses is null) return Usage(UsageLineFor("grammar check"), asJson);
            }
            var (checkWindow, checkPagingRefusal) = PagingFlags.Read(flags, ResultPaging.HeavyRowLimit);
            if (checkPagingRefusal is not null)
            {
                result = RenderCommand(CommandOutcome<GrammarCheckResponse>.Refused(checkPagingRefusal));
                break;
            }
            result = RunWithConsoleCancellation(token => RenderCommand(
                GrammarCheckQuery.Query(new GrammarCheckRequest(grammarProject, StoredAnalyses: storedAnalyses), token)
                    .Map(check => checkWindow.All ? check : check.Window(checkWindow.Offset, checkWindow.Limit).WithNextStep(
                        next => PagingFlags.NextCommand("grammar check", [], flags, next, checkWindow.Limit)))));
            break;

        case "timing":
            if (!flags.TryGetValue("project", out var timingProject))
                return Usage(UsageLineFor("timing"), asJson);
            var timingTop = 10;
            if (flags.TryGetValue("top", out var timingTopRaw) &&
                (!int.TryParse(timingTopRaw, out timingTop) || timingTop <= 0))
                return Usage(UsageLineFor("timing"), asJson);
            var explicitTimingWords = flags.TryGetValue("word", out var timingWordsRaw)
                ? timingWordsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            var timingBy = flags.GetValueOrDefault("by", "kind");
            if (timingBy is not ("kind" or "rule") ||
                (flags.ContainsKey("rule") && (timingBy != "rule" || TraceTimingKey.Parse(flags["rule"]) is null)))
                return Usage(UsageLineFor("timing"), asJson);
            result = RenderCommand(TimingCommand.Timing(new TimingRequest(
                timingProject, flags.GetValueOrDefault("assessment"), flags.GetValueOrDefault("words", "all"),
                timingBy, TraceTimingKey.Parse(flags.GetValueOrDefault("rule")) is { } timingKey
                    ? timingKey with { IdentityQuality = flags.ContainsKey("local") ? "grammar-local"
                        : flags.ContainsKey("structural") ? "structural" : "authored",
                        Scope = flags.GetValueOrDefault("scope") } : null, timingTop, explicitTimingWords,
                ProposalId: flags.GetValueOrDefault("proposal"))));
            break;

        case "activity":
            if (!flags.TryGetValue("log", out var activityLog))
                return Usage(UsageLineFor("activity"), asJson);
            result = RenderCommand(ActivityQuery.Read(new ActivityRequest(activityLog)));
            break;

        case "uses":
            var usesFlags = new[] { "allomorph", "grammatical-info", "timing", "words" };
            if (!flags.TryGetValue("project", out var usesProject) || positionals.Count != 0 ||
                usesFlags.Any(name => flags.TryGetValue(name, out var value) && (value == "true" || string.IsNullOrWhiteSpace(value))) ||
                flags.TryGetValue("timing", out var usesTiming) && usesTiming.IndexOf(':') is <= 0)
                return Usage("Usage: motif " + UsageLineFor("uses"), asJson);
            var usesTimingParts = flags.GetValueOrDefault("timing")?.Split(':', 2);
            var usesRef = flags.ContainsKey("allomorph") || flags.ContainsKey("grammatical-info") || usesTimingParts is not null
                ? new ObjectUseRef
                {
                    AllomorphId = flags.GetValueOrDefault("allomorph"),
                    GrammaticalInfoId = flags.GetValueOrDefault("grammatical-info"),
                    TimingKind = usesTimingParts?[0],
                    TimingKey = usesTimingParts?[1],
                    TimingIdentityQuality = flags.ContainsKey("local") ? "grammar-local"
                        : flags.ContainsKey("structural") ? "structural" : "authored",
                    TimingScope = flags.GetValueOrDefault("scope"),
                }
                : null;
            var usesWords = flags.GetValueOrDefault("words")
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result = RenderCommand(ObjectUsesQuery.Query(new ObjectUsesRequest(usesProject, usesRef, usesWords)));
            break;

        case "inspect":
            if (!flags.TryGetValue("project", out var inspectProject) || positionals.Count != 0 ||
                InspectSubjectFrom(flags) is not { } inspectSubject)
                return Usage("Usage: motif " + UsageLineFor("inspect"), asJson);
            result = RenderCommand(InspectQuery.Query(new InspectRequest(inspectProject, inspectSubject,
                flags.GetValueOrDefault("proposal"))));
            break;

        case "trace":
            if (flags.TryGetValue("load", out var tracePath))
            {
                if (flags.ContainsKey("project") || flags.ContainsKey("word") || positionals.Count != 0 ||
                    tracePath == "true")
                    return Usage("Usage: motif " + UsageLineFor("trace --load"), asJson);
                result = RenderCommand(WordTraceQuery.Load(new WordTraceLoadRequest(tracePath)));
                break;
            }
            if (!flags.TryGetValue("project", out var traceProject) || !flags.TryGetValue("word", out var traceWord) ||
                string.IsNullOrWhiteSpace(traceWord) || traceWord == "true" || positionals.Count != 0)
                return Usage("Usage: motif " + UsageLineFor("trace") + " OR motif " + UsageLineFor("trace --load"), asJson);
            result = RunWithConsoleCancellation(token => RenderCommand(
                WordTraceQuery.Query(new WordTraceRequest(traceProject, traceWord, flags.GetValueOrDefault("proposal")), token)));
            break;

        case "parse" or "generate":
        {
            var traceVerb = verb;
            if (!flags.TryGetValue("project", out var rejectionProject) || positionals.Count != 1 ||
                !flags.ContainsKey("trace") || flags["trace"] != "true" ||
                traceVerb == "generate" && flags.ContainsKey("max-paths"))
                return Usage("Usage: motif " + UsageLineFor(traceVerb), asJson);
            int? rejectionMax = null;
            if (flags.TryGetValue("max-paths", out var maxPathsText))
            {
                if (!int.TryParse(maxPathsText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedMax))
                {
                    result = RenderCommand(RejectionTraceQuery.InvalidCap(maxPathsText));
                    break;
                }
                rejectionMax = parsedMax;
            }
            var rejectionAt = flags.GetValueOrDefault("at");
            result = traceVerb == "parse"
                ? RunWithConsoleCancellation(token => RenderCommand(RejectionTraceQuery.Parse(
                    new ParseTraceRequest(rejectionProject, positionals[0], rejectionAt, rejectionMax), token)))
                : RunWithConsoleCancellation(token => RenderCommand(FormsExplainQuery.Generate(
                    new GenerateTraceRequest(rejectionProject, positionals[0], rejectionAt), token)));
            break;
        }

        case "forms":
        {
            var wordFlag = flags.GetValueOrDefault("word");
            var allowed = new[] { "project", "with", "word", "at", "json", "md", "timeout-ms", "max-work" };
            if (!flags.TryGetValue("project", out var formsProject) || positionals.FirstOrDefault() != "explain" ||
                flags.Keys.Any(flag => !allowed.Contains(flag)) || flags.ContainsKey("json") && flags.ContainsKey("md") ||
                wordFlag == "true" || flags.GetValueOrDefault("with") == "true" ||
                positionals.Count != (wordFlag is null ? 2 : 1))
                return Usage("Usage: motif " + UsageLineFor("forms explain"), asJson);
            var timeoutText = flags.GetValueOrDefault("timeout-ms");
            var workText = flags.GetValueOrDefault("max-work");
            if (timeoutText is not null && !int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                workText is not null && !long.TryParse(workText, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return Usage("Usage: motif " + UsageLineFor("forms explain"), asJson);
            int? formsTimeout = timeoutText is null ? null : int.Parse(timeoutText, CultureInfo.InvariantCulture);
            long? formsMaxWork = workText is null ? null : long.Parse(workText, CultureInfo.InvariantCulture);
            var formsRequest = new FormsExplainRequest(formsProject, wordFlag is null ? positionals[1] : null,
                ReadRepeatedFlagValues(flags, "with"), wordFlag, flags.GetValueOrDefault("at"), formsTimeout, formsMaxWork);
            result = RunWithConsoleCancellation(token => RenderCommand(FormsExplainQuery.Explain(formsRequest, token)));
            break;
        }

        case "why-not":
        {
            if (!flags.TryGetValue("project", out var whyNotProject) || positionals.Count != 1 ||
                !flags.TryGetValue("expect", out var whyNotExpect) || whyNotExpect == "true" ||
                flags.Keys.Any(flag => flag is not ("project" or "expect" or "at" or "json" or "autonomous")))
                return Usage("Usage: motif " + UsageLineFor("why-not"), asJson);
            var whyNotAt = flags.GetValueOrDefault("at");
            result = RunWithConsoleCancellation(token => RenderCommand(WhyNotQuery.Explain(
                new WhyNotRequest(whyNotProject, positionals[0], ReadRepeatedFlagValues(flags, "expect"), whyNotAt), token)));
            break;
        }

        case "selection":
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var selectionProject))
                return Usage(UsageLineFor("selection show") + " OR " + UsageLineFor("selection set-default") +
                    " OR " + UsageLineFor("selection set-parsimony") + " OR " + UsageLineFor("selection set-limits"), asJson);
            switch (positionals[0])
            {
                case "show":
                    result = RenderCommand(SelectionCommands.ReadDefault(
                        new ReadDefaultSelectionRequest(selectionProject)));
                    break;
                case "set-default":
                    if (!flags.TryGetValue("name", out var selectionName) ||
                        !TryParseGuidList(flags.GetValueOrDefault("texts"), out var selectionTexts))
                        return Usage(UsageLineFor("selection set-default"), asJson);
                    IReadOnlyList<string> addedWords = flags.TryGetValue("add-words", out var addedWordsRaw)
                        ? addedWordsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : Array.Empty<string>();
                    var wordsFilePath = flags.GetValueOrDefault("words-file");
                    if (wordsFilePath == "true") return Usage(UsageLineFor("selection set-default"), asJson);
                    var wordFile = WordListFile.ResolveWithCounts(addedWords, wordsFilePath);
                    if (!wordFile.Succeeded)
                    {
                        result = RenderCommand(wordFile);
                        break;
                    }
                    addedWords = wordFile.Value!.Words;
                    if (!TryReadSelectionLimits(flags, out var limits))
                        return Usage(UsageLineFor("selection set-default"), asJson);
                    var existingSelection = SelectionCommands.ReadDefault(new ReadDefaultSelectionRequest(selectionProject));
                    if (existingSelection.Value?.Selection is { } savedSelection)
                        limits = limits with { AssessBudgetSeconds = savedSelection.Limits.AssessBudgetSeconds };
                    result = RenderCommand(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
                        selectionProject, selectionName, selectionTexts, addedWords, limits,
                        flags.GetValueOrDefault("expected-revision")) { WordList = wordFile.Value.List }));
                    break;
                case "set-parsimony":
                    if (!flags.TryGetValue("name", out var parsimonySelectionName) ||
                        !TryParseGuidList(flags.GetValueOrDefault("texts"), out var parsimonyTexts) ||
                        flags.ContainsKey("texts") && parsimonyTexts.Count == 0)
                        return Usage(UsageLineFor("selection set-parsimony"), asJson);
                    result = RenderCommand(SelectionCommands.SetParsimony(new SetParsimonySelectionRequest(
                        selectionProject, parsimonySelectionName, flags.ContainsKey("texts") ? parsimonyTexts : null)));
                    break;
                case "set-limits":
                    if (!flags.TryGetValue("name", out var limitSelectionName) ||
                        !flags.TryGetValue("expected-revision", out var expectedRevision) ||
                        !TryReadSelectionLimits(flags, out var limitsForSelection))
                        return Usage(UsageLineFor("selection set-limits"), asJson);
                    var currentSelection = SelectionCommands.ReadDefault(new ReadDefaultSelectionRequest(selectionProject));
                    if (currentSelection.Value?.Selection is { } savedLimits)
                        limitsForSelection = limitsForSelection with { AssessBudgetSeconds = savedLimits.Limits.AssessBudgetSeconds };
                    result = RenderCommand(SelectionCommands.SetLimits(new SetSelectionLimitsRequest(
                        selectionProject, limitSelectionName, expectedRevision, limitsForSelection)));
                    break;
                default:
                    return Usage(UsageLineFor("selection show") + " OR " + UsageLineFor("selection set-default") +
                        " OR " + UsageLineFor("selection set-parsimony") + " OR " + UsageLineFor("selection set-limits"), asJson);
            }
            break;

        case "project":
            if (positionals is ["create"])
            {
                if (!flags.TryGetValue("into", out var createInto) || !flags.TryGetValue("name", out var createName) ||
                    !flags.TryGetValue("vernacular", out var createVernacular))
                    return Usage(UsageLineFor("project create"), asJson);
                var created = ProjectCreateCommand.Create(new ProjectCreateRequest(createInto, createName, createVernacular,
                    flags.GetValueOrDefault("vernacular-name"), flags.GetValueOrDefault("word-forming"),
                    flags.GetValueOrDefault("analysis")));
                if (created.Succeeded) RecordKnownProject(created.Value!.FwDataPath, ResolveRoot(), error);
                result = RenderCommand(created);
                break;
            }
            if (positionals is ["word-forming"])
            {
                if (!flags.TryGetValue("project", out var wordFormingProject) ||
                    !flags.TryGetValue("add", out var wordFormingAdd))
                    return Usage(UsageLineFor("project word-forming"), asJson);
                result = RenderCommand(ProjectWordFormingCommand.Add(
                    new ProjectWordFormingRequest(wordFormingProject, wordFormingAdd)));
                break;
            }
            if (positionals is ["tag"] && flags.ContainsKey("set") && !HasProjectTagValue(rest))
                return Usage(UsageLineFor("project tag --set"), asJson);
            if (positionals is ["tag"] && flags.TryGetValue("project", out var tagProject))
            {
                result = RenderCommand(flags.TryGetValue("set", out var newTag)
                    ? ProjectTagCommands.Set(new SetProjectTagRequest(tagProject, newTag))
                    : ProjectTagCommands.Read(new ReadProjectTagRequest(tagProject)));
                break;
            }
            if (positionals is ["tag"])
                return Usage(UsageLineFor("project tag") + " OR " + UsageLineFor("project tag --set"), asJson);
            if (positionals.Count != 1 || positionals[0] != "initialize" ||
                !flags.TryGetValue("project", out var initializationProject))
                return Usage(UsageLineFor("project initialize"), asJson);
            result = RunWithConsoleCancellation(cancellationToken => RenderCommand(
                ProjectInitializationCommand.Initialize(
                    new ProjectInitializationRequest(
                        initializationProject, flags.GetValueOrDefault("confirm")),
                    ResolveRoot(), cancellationToken: cancellationToken)));
            break;

        case "progress":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var progressProject) ||
                flags.Keys.Any(key => key is not ("project" or "texts" or "source" or "detail" or "limit" or "cursor" or "since-revision" or "json")))
                return Usage(UsageLineFor("progress"), asJson);
            var progressIds = new List<Guid>();
            if (flags.TryGetValue("texts", out var progressTexts))
                foreach (var value in progressTexts.Split(','))
                {
                    if (!Guid.TryParse(value, out var parsedText)) return Usage(UsageLineFor("progress"), asJson);
                    progressIds.Add(parsedText);
                }
            var progressLimit = 50;
            if (flags.TryGetValue("limit", out var progressLimitText) && !int.TryParse(progressLimitText, out progressLimit))
                return Usage(UsageLineFor("progress"), asJson);
            if (flags.TryGetValue("source", out var progressSource))
            {
                if (flags.ContainsKey("texts") || flags.ContainsKey("since-revision")) return Usage(UsageLineFor("progress"), asJson);
                result = RenderCommand(SourceProgressQuery.Query(progressProject, progressSource,
                    flags.GetValueOrDefault("detail") ?? "summary", progressLimit, flags.GetValueOrDefault("cursor")));
                break;
            }
            result = RenderCommand(ProgressQuery.Query(new ProgressRequest(progressProject,
                new ProgressScope("texts", progressIds), flags.GetValueOrDefault("detail") ?? "summary", progressLimit,
                flags.GetValueOrDefault("cursor"), flags.GetValueOrDefault("since-revision"))));
            break;

        case "texts":
            if (positionals.Count != 1 || positionals[0] != "list" ||
                !flags.TryGetValue("project", out var textsProject))
                return Usage(UsageLineFor("texts list"), asJson);
            result = RenderCommand(TextInventoryQuery.Query(new TextInventoryRequest(textsProject)));
            break;

        case "word":
            if (positionals.Count != 1 || positionals[0] != "read-state" ||
                !flags.TryGetValue("project", out var readStateProject) ||
                !flags.TryGetValue("text", out var readStateText))
                return Usage(UsageLineFor("word read-state"), asJson);
            if (!Guid.TryParse(readStateText, out var readStateTextId) ||
                flags.ContainsKey("read") && flags.ContainsKey("unread") ||
                !TryParseReadOccurrences(flags.GetValueOrDefault("occurrences"), readStateTextId,
                    out var readStateOccurrences))
                return Usage(UsageLineFor("word read-state"), asJson);
            var wantsRead = flags.ContainsKey("read");
            var wantsUnread = flags.ContainsKey("unread");
            if (readStateOccurrences is not null && !wantsRead && !wantsUnread)
                return Usage(UsageLineFor("word read-state"), asJson);
            if (!TryParseExpectedContext(flags.GetValueOrDefault("expected-context"), out var readExpectedContext))
                return Usage("--expected-context must contain one ExpectedContext object.", asJson);
            bool? isRead = wantsRead ? true : wantsUnread ? false : null;
            result = RenderCommand(ReadStateCommands.Execute(new WordReadStateRequest(
                readStateProject, readStateTextId, readStateOccurrences, isRead)
                { ExpectedContext = readExpectedContext }));
            break;

        case "setup":
            if (positionals.Count != 1 || positionals[0] != "skip" ||
                !flags.TryGetValue("project", out var setupProject))
                return Usage(UsageLineFor("setup skip"), asJson);
            result = RenderCommand(ProjectSetupCommands.Skip(new SkipSetupRequest(setupProject)));
            break;

        case "worker":
            if (positionals is not ["stop"] || flags.Keys.Any(flag => flag is not ("project" or "json")))
                return Usage(UsageLineFor("worker stop"), asJson);
            result = RenderCommand(WorkerStopCommand.Run(new WorkerStopRequest(flags.GetValueOrDefault("project"))));
            break;

        case "store":
            if (positionals.Count == 1 && positionals[0] == "upgrade")
            {
                if (flags.TryGetValue("source", out var upgradeSource) && flags.TryGetValue("out", out var upgradeOutput) &&
                    !flags.ContainsKey("project") && flags.Keys.All(key => key is "source" or "out" or "json"))
                {
                    var upgradeReport = ProjectStoreUpgrade.Copy(upgradeSource, upgradeOutput);
                    output.WriteLine(JsonSerializer.Serialize(upgradeReport, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                if (flags.TryGetValue("project", out var upgradeProject) &&
                    flags.Keys.All(key => key is "project" or "json"))
                {
                    var upgradeOutcome = ProjectStoreCommand.Run(upgradeProject, CliProductVersion(), (database, _) =>
                        CommandOutcome<string>.Success(File.Exists(database.FullPath + ".upgrade.json")
                            ? File.ReadAllText(database.FullPath + ".upgrade.json") : "{}"));
                    if (!upgradeOutcome.Succeeded)
                    {
                        result = RenderCommand(upgradeOutcome);
                        break;
                    }
                    output.WriteLine(upgradeOutcome.Value);
                    return 0;
                }
                return Usage(UsageLineFor("store upgrade"), asJson);
            }
            if (positionals is ["backup"])
            {
                if (!flags.TryGetValue("project", out var backupProject) ||
                    flags.Keys.Any(key => key is not ("project" or "to" or "json")))
                    return Usage(UsageLineFor("store backup"), asJson);
                result = RenderCommand(StoreBackupCommand.Backup(
                    new StoreBackupRequest(backupProject, flags.GetValueOrDefault("to"))));
                break;
            }
            if (positionals is ["restore", ..])
            {
                if (positionals.Count != 2 || !flags.TryGetValue("project", out var restoreProject) ||
                    flags.Keys.Any(key => key is not ("project" or "force" or "json")))
                    return Usage(UsageLineFor("store restore"), asJson);
                result = RenderCommand(StoreBackupCommand.Restore(new StoreRestoreRequest(restoreProject, positionals[1],
                    flags.TryGetValue("force", out var restoreForce) && IsTruthyFlag(restoreForce))));
                break;
            }
            if (positionals is ["relocate"] && flags.TryGetValue("project", out var relocateProject) &&
                flags.Keys.All(key => key is "project" or "confirm" or "match-by-name" or "json"))
            {
                result = RenderCommand(StoreRelocateCommand.Run(
                    new StoreRelocateRequest(relocateProject, flags.GetValueOrDefault("confirm"),
                        flags.ContainsKey("match-by-name"))));
                break;
            }
            if (positionals is ["relocate"]) return Usage(UsageLineFor("store relocate"), asJson);
            if (positionals.Count != 1 || positionals[0] is not ("delete-refused" or "prune") ||
                !flags.TryGetValue("project", out var storeProject))
                return Usage(UsageLineFor(positionals.Count == 1 && positionals[0] == "prune" ? "store prune" : "store delete-refused"), asJson);
            result = positionals[0] == "prune"
                ? RenderCommand(StorePruneCommand.Run(new StorePruneRequest(storeProject)))
                : RenderCommand(ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(storeProject)));
            break;

        case "assess":
            if (positionals.Count != 1) return Usage(AssessUsage(), asJson);
            if (flags.Keys.Any(flag => flag is not ("texts" or "all-wordforms" or "words" or "retry-failed" or
                    "retry-work-over" or "retry-source-assessment" or "replaces" or "time-limit-ms" or "parser-work-limit" or
                    "summary" or "offset" or "limit" or "full-object-timing" or "max-seconds" or "collect" or "no-background" or "json"))) return Usage(AssessUsage(), asJson);
            flags.TryGetValue("replaces", out var assessReplaces);
            if (assessReplaces is not null && !CanonicalId.TryParse(assessReplaces, out _))
                return Usage(AssessUsage(), asJson);
            var assessRetryFailed = flags.ContainsKey("retry-failed");
            var hasAssessRetryWorkOver = flags.ContainsKey("retry-work-over");
            var hasAssessRetrySource = flags.TryGetValue("retry-source-assessment", out var assessRetrySource);
            if (hasAssessRetrySource != (assessRetryFailed || hasAssessRetryWorkOver))
                return Usage(AssessUsage(), asJson);
            if (hasAssessRetrySource && !CanonicalId.TryParse(assessRetrySource, out _))
                return Usage(AssessUsage(), asJson);
            if (!TryParseGuidList(flags.GetValueOrDefault("texts"), out var assessTextIds))
                return Usage(AssessUsage(), asJson);
            flags.TryGetValue("words", out var assessWordsFile);
            var assessWordList = WordListFile.Resolve(null, assessWordsFile);
            if (!assessWordList.Succeeded)
            {
                result = RenderCommand(assessWordList);
                break;
            }
            var assessWords = assessWordList.Value!;
            var assessSummary = flags.ContainsKey("summary");
            var assessPageOffset = 0;
            var assessPageLimit = 25;
            if (flags.TryGetValue("offset", out var assessOffsetRaw) &&
                    (!int.TryParse(assessOffsetRaw, out assessPageOffset) || assessPageOffset < 0) ||
                flags.TryGetValue("limit", out var assessLimitRaw) &&
                    (!int.TryParse(assessLimitRaw, out assessPageLimit) || assessPageLimit < 1) ||
                (flags.ContainsKey("offset") || flags.ContainsKey("limit")) && !assessSummary)
                return Usage(AssessUsage(), asJson);
            long? assessRetryWorkOver = null;
            if (hasAssessRetryWorkOver)
            {
                var assessRetryWorkOverRaw = flags["retry-work-over"];
                if (!long.TryParse(assessRetryWorkOverRaw, out var assessRetryWorkOverLabel) ||
                    assessRetryWorkOverLabel < 0)
                {
                    return Usage(AssessUsage(), asJson);
                }
                assessRetryWorkOver = assessRetryWorkOverLabel;
            }
            int? assessTimeLimitMs = null;
            if (flags.TryGetValue("time-limit-ms", out var assessTimeLimitRaw))
            {
                if (!int.TryParse(assessTimeLimitRaw, out var parsedTimeLimitMs) || parsedTimeLimitMs <= 0)
                    return Usage(AssessUsage(), asJson);
                assessTimeLimitMs = parsedTimeLimitMs;
            }
            int? assessBudgetSeconds = null;
            if (flags.TryGetValue("max-seconds", out var assessBudgetRaw))
            {
                if (!int.TryParse(assessBudgetRaw, out var parsedBudget) || parsedBudget is < 60 or > 4294967)
                    return Usage(AssessUsage(), asJson);
                assessBudgetSeconds = parsedBudget;
            }
            ParserWorkLimit? assessParserWorkLimit = null;
            if (flags.TryGetValue("parser-work-limit", out var assessParserWorkLimitRaw))
            {
                try { assessParserWorkLimit = ParserWorkLimit.Parse(assessParserWorkLimitRaw); }
                catch (FormatException) { return Usage(AssessUsage(), asJson); }
            }
            string[]? assessCollect = null;
            if (flags.TryGetValue("collect", out var assessCollectRaw))
            {
                var names = assessCollectRaw.Split(',', StringSplitOptions.TrimEntries);
                if (names.Any(string.IsNullOrEmpty))
                {
                    result = RenderCommand(CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.collect-empty", FailureReason.InvalidArgument,
                        "Name at least one collector, or leave collect unset for the default.")));
                    break;
                }
                var uniqueNames = new List<string>(names.Length);
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                {
                    var canonicalName = Enum.TryParse<AssessmentKind>(name, ignoreCase: true, out var kind) &&
                        Enum.IsDefined(kind) ? kind.ToString() : name;
                    if (seenNames.Add(canonicalName)) uniqueNames.Add(canonicalName);
                }
                assessCollect = uniqueNames.ToArray();
            }
            var selectionWasRequested = flags.ContainsKey("all-wordforms") || flags.ContainsKey("texts") ||
                assessWordsFile is not null || assessRetryFailed || assessRetryWorkOver is not null;
            var assessSelection = selectionWasRequested
                ? new SelectionRequest(flags.ContainsKey("all-wordforms"), assessTextIds,
                    assessWords, assessRetryFailed, assessRetryWorkOver, assessRetrySource)
                : null;
            using (var activityLease = MotifUpdateGate.TryAcquire())
            {
                if (activityLease is null)
                    return RefuseActivityDuringUpdate();
                result = RunWithConsoleCancellation(cancellationToken =>
                {
                    var request = new AssessRequest(positionals[0], assessSelection, assessTimeLimitMs,
                        assessParserWorkLimit, assessReplaces, flags.ContainsKey("full-object-timing"),
                        assessBudgetSeconds, assessCollect, flags.ContainsKey("no-background"));
                    var assessed = assess is null
                        ? AssessCommand.Assess(request, asJson ? null : progress => error.WriteLine(progress.Message),
                            cancellationToken)
                        : assess(request);
                    if (!assessSummary || !assessed.Succeeded) return RenderCommand(assessed);
                    alreadyRendered = true;
                    return CommandTextRenderer.RenderAssessSummary(
                        AssessSummary.Page(assessed.Value!, assessPageOffset, assessPageLimit, detailed: false), asJson);
                });
            }
            break;

        case "stats":
            if (positionals.Count != 1) return Usage(StatsUsage(), asJson);
            var statsOutput = asJson ? StatsOutputKind.JsonRows : StatsOutputKind.Text;
            result = RunWithConsoleCancellation(cancellationToken => RenderCommand(StatsCommand.Stats(
                new StatsRequest(positionals[0], flags.GetValueOrDefault("assessment"), statsOutput, forwardedArguments,
                    flags.GetValueOrDefault("proposal")),
                cancellationToken)));
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
            if (handoffNoAssess)
            {
                result = RunWithConsoleCancellation(cancellationToken => RenderCommand(HandoffCommand.Handoff(
                    new HandoffRequest(positionals[0], handoffOut, handoffSelection, false, handoffInvocation),
                    asJson ? null : progress => error.WriteLine(progress.Message), cancellationToken)));
            }
            else
            {
                using var activityLease = MotifUpdateGate.TryAcquire();
                if (activityLease is null)
                    return RefuseActivityDuringUpdate();
                result = RunWithConsoleCancellation(cancellationToken => RenderCommand(HandoffCommand.Handoff(
                    new HandoffRequest(positionals[0], handoffOut, handoffSelection, true, handoffInvocation),
                    asJson ? null : progress => error.WriteLine(progress.Message), cancellationToken)));
            }
            break;

        case "jobs":
            if (positionals.Count == 0)
                return Usage(JobsUsage(), asJson);
            switch (positionals[0])
            {
                case "wait":
                    const string jobsWaitUsage = "Usage: motif jobs wait <jobId> --project <fwdata> [--wait-terminal] [--wait-timeout-ms <ms>] [--json]";
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsWaitProject))
                        return Usage(jobsWaitUsage, asJson);
                    if (ParseWaitTimeout(flags, jobsWaitUsage, asJson, out var jobsWaitTimeout) is { } jobsWaitError)
                        return jobsWaitError;
                    var jobWaitWarnings = RunnerWarnings.Once(error.WriteLine);
                    result = RenderCommand(WaitWithConsoleCancellation(waitForTerminal, token => JobCommands.Wait(
                        new JobsWaitRequest(jobsWaitProject, positionals[1], CliProductVersion(), jobsWaitTimeout, waitForTerminal),
                        token, waitForTerminal ? () => startRunner(jobsWaitProject, jobWaitWarnings) : null)));
                    break;

                case "show":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsShowProject))
                        return Usage("Usage: motif jobs show <jobId> --project <fwdata> [--json]", asJson);
                    var shownJob = JobCommands.Show(
                        new ShowJobRequest(jobsShowProject, positionals[1], CliProductVersion()));
                    result = RenderCommand(shownJob);
                    // A job nobody is running would otherwise stay put until some other command queued work.
                    if (shownJob.Value is { } job && (job.ClaimLost || job.Status == SIL.Motif.Contract.Jobs.JobStatus.Queued))
                        startRunner(jobsShowProject, error.WriteLine);
                    break;

                case "assessments":
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsAssessmentsProject))
                        return Usage("Usage: motif jobs assessments <jobId> --project <fwdata> [--json]", asJson);
                    result = RenderCommand(JobCommands.Assessments(
                        new JobAssessmentsRequest(jobsAssessmentsProject, positionals[1], CliProductVersion())));
                    break;

                case "list":
                    if (positionals.Count != 1)
                        return Usage("Usage: motif jobs list [--all] [--json]", asJson);
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
                    if (result.ExitCode == 0) startRunner(jobsRequeueProject, error.WriteLine);
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

    result = StoreUpgradeReport.Apply(storeUpgrades.Notices, result, asJson, error);
    result = Picked(result);
    if (result.ExitCode == 0)
    {
        output.Write(result.Output);
        return result.ExitCode;
    }
    if (pickRefused) return result.ExitCode;
    // A caller that asked for JSON gets JSON when it goes wrong too; a Proposal verb already did this itself.
    error.Write(!alreadyRendered && asJson && result.Reason is { } reason
        ? ProjectionJson.Serialize(new FailureEnvelope(reason, result.Output.Trim())) + Environment.NewLine
        : result.Output);
    return result.ExitCode;
}
catch (ProjectWritingSystemsUnavailableException ex)
{
    NoteStoreUpgrades();
    // A setup problem the message explains, never an unexpected failure, even outside a project-store command.
    var refusal = ProjectStoreCommand.WritingSystemRefusal(ex, namedProject);
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(refusal.Reason, refusal.Message, refusal.Facts, refusal.Code)));
    else
        error.WriteLine($"{refusal.Code}: {refusal.Message}");
    return FailureEnvelope.ExitCodeFor(refusal.Reason);
}
catch (OperationCanceledException ex)
{
    NoteStoreUpgrades();
    var message = string.IsNullOrWhiteSpace(ex.Message) ? "The CLI operation was cancelled." : ex.Message;
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.Cancelled, message, code: "cli.cancelled")));
    else
        error.WriteLine($"cli.cancelled: {message}");
    return FailureEnvelope.ExitCodeFor(FailureReason.Cancelled);
}
catch (OutOfMemoryException ex)
{
    NoteStoreUpgrades();
    // A memory shortage says nothing about the store, so it is a refusal, not the "something broke" bucket.
    var message = string.IsNullOrWhiteSpace(ex.Message) ? "The CLI ran out of memory." : ex.Message;
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.Refused, message, code: HostRefusalCodes.CliOutOfMemory)));
    else
        error.WriteLine($"{HostRefusalCodes.CliOutOfMemory}: {message}");
    return FailureEnvelope.ExitCodeFor(FailureReason.Refused);
}
catch (Exception ex) when (SqliteConnections.IsBusyInChain(ex))
{
    NoteStoreUpgrades();
    // Another process held a store lock past the busy timeout. Nothing is damaged; the same call can be repeated.
    const string message = "Motif's store is locked by another process. Wait a few seconds and repeat the same call.";
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.Busy, message, code: HostRefusalCodes.CliStoreBusy)));
    else
        error.WriteLine($"{HostRefusalCodes.CliStoreBusy}: {message}");
    return FailureEnvelope.ExitCodeFor(FailureReason.Busy);
}
catch (Exception ex)
{
    NoteStoreUpgrades();
    // Nothing decided this; it escaped. That is a bug, not a refusal, and it gets its own code.
    var message = string.IsNullOrWhiteSpace(ex.Message) ? "An unexpected CLI failure occurred." : ex.Message;
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.StoreInconsistent, message, code: "cli.unexpected")));
    else
        error.WriteLine($"error: {message}");
    return FailureEnvelope.ExitCodeFor(FailureReason.StoreInconsistent);
}

static bool TryReadCitation(IReadOnlyDictionary<string, string> flags, out SIL.Motif.Contract.Model.OperationCitation? citation,
    out string? error)
{
    citation = null;
    error = null;
    var hasSource = flags.TryGetValue("cite-source", out var source);
    var hasLocator = flags.TryGetValue("cite-locator", out var locator);
    if (!hasSource && !hasLocator) return true;
    // ParseArgs records a flag given no value as "true", which is never a meant source or locator.
    if (source == "true" || locator == "true")
    {
        error = "--cite-source and --cite-locator each need a value.";
        return false;
    }
    if (!hasSource)
    {
        error = "--cite-locator needs --cite-source: a citation names the work it cites.";
        return false;
    }
    try
    {
        citation = SIL.Motif.Contract.Model.OperationCitation.Create(source, locator);
        return true;
    }
    catch (SIL.Motif.Contract.Parsing.ContractParseException exception)
    {
        error = exception.Message;
        return false;
    }
}

// The leading non-flag words name the verb; a help flag after `--` belongs to the forwarded command.
static string[]? HelpFlagRewrite(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] == "help") return null;
    var separator = Array.IndexOf(arguments, "--");
    var own = separator < 0 ? arguments : arguments[..separator];
    if (!own.Any(argument => argument is "--help" or "-h")) return null;
    var words = own.TakeWhile(argument => !argument.StartsWith('-')).ToArray();
    var autonomous = own.Where(argument => argument == CommandSurfacePolicy.AutonomousFlag);
    return ["help", .. words, .. autonomous];
}

int Usage(string message, bool asJson = false, bool withUsageBanner = false)
{
    if (asJson)
    {
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.InvalidArgument, message)));
        return FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument);
    }
    error.WriteLine(message);
    if (withUsageBanner) PrintUsage(error, commandPolicy);
    return FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument);
}

int RefuseActivityDuringUpdate()
{
    error.WriteLine("Motif cannot update while a Worker job, Assessment, Handoff, or pending apply is running.");
    return 3;
}

int? ParseWaitTimeout(Dictionary<string, string> flags, string usageLine, bool asJson, out TimeSpan timeout)
{
    timeout = JobCommands.DefaultWaitTimeout;
    if (!flags.TryGetValue("wait-timeout-ms", out var raw)) return null;
    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        return Usage(usageLine, asJson);
    timeout = TimeSpan.FromMilliseconds(milliseconds);
    return null;
}

static string ConfigUsage() => UsageLineFor("config show");

static string ReportUsage() => UsageLineFor("report");

static string CompareUsage() => UsageLineFor("compare");

static string BaselineUsage() => "Usage: motif " + UsageLineFor("baseline capture");

static CommandOutcome<BaselineCaptureResponse> CaptureBaseline(string selector, string managedRoot, bool noBackground)
{
    var selectorIsPath = Path.IsPathRooted(selector) || selector.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase) ||
                         selector.Contains(Path.DirectorySeparatorChar) || selector.Contains(Path.AltDirectorySeparatorChar);
    if (selectorIsPath)
        return BaselineCaptureCommand.Capture(new BaselineCaptureRequest(selector, noBackground));

    try
    {
        var projectPath = ProjectResolver.Resolve(selector, managedRoot);
        return BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath, noBackground));
    }
    catch (ProfileException exception)
    {
        return CommandOutcome<BaselineCaptureResponse>.Refused(new Refusal(
            "project.unknown", FailureReason.InvalidArgument, exception.Message));
    }
}

static string AssessUsage() => "Usage: motif " + UsageLineFor("assess");

static string StatsUsage() => "Usage: motif " + UsageLineFor("stats");

static string HandoffUsage() => "Usage: motif " + UsageLineFor("handoff");

/// <summary>Parses a comma-separated GUID list; an absent flag is an empty list, not a failure.</summary>
static bool TryParseCount(string? text, out int? count)
{
    count = null;
    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return false;
    count = parsed;
    return true;
}

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

static bool TryReadSelectionLimits(IReadOnlyDictionary<string, string> flags, out SelectionParsingLimits limits)
{
    limits = new SelectionParsingLimits(ParserWorkLimit.Default, SelectionTimeLimitMode.Estimated, null);
    var timeMode = SelectionTimeLimitMode.Estimated;
    if (flags.TryGetValue("time-mode", out var mode))
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(mode, "estimated")) timeMode = SelectionTimeLimitMode.Estimated;
        else if (StringComparer.OrdinalIgnoreCase.Equals(mode, "explicit")) timeMode = SelectionTimeLimitMode.Explicit;
        else return false;
    }

    int? explicitTime = null;
    if (flags.TryGetValue("time-limit-ms", out var timeText))
    {
        if (!int.TryParse(timeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            return false;
        explicitTime = parsed;
    }
    if ((timeMode == SelectionTimeLimitMode.Explicit) != (explicitTime is not null)) return false;

    var parserWorkLimit = ParserWorkLimit.Default;
    if (flags.TryGetValue("parser-work-limit", out var workText))
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(workText, "none")) parserWorkLimit = ParserWorkLimit.Unbounded;
        else
        {
            try { parserWorkLimit = ParserWorkLimit.Parse(workText); }
            catch (FormatException) { return false; }
        }
    }

    if (parserWorkLimit.IsUnbounded && timeMode != SelectionTimeLimitMode.Estimated) return false;
    limits = new SelectionParsingLimits(parserWorkLimit, timeMode, explicitTime);
    return limits.ValidationError() is null;
}

// Cleanup is recorded after the commit, so ask again briefly.
static void WriteTrialPhases(string project, string proposalId, string jobId, TimeSpan cliReport, TextWriter error)
{
    IReadOnlyList<TrialPhase>? phases = null;
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (true)
    {
        var read = Trials.Read(new ReadTrialRequest(project, proposalId));
        phases = read.Succeeded && read.Value is { } record && record.JobId == jobId ? record.Phases : null;
        if (phases is null || phases.Any(phase => phase.Name == TrialPhaseNames.Cleanup && phase.Note != TrialPhaseNames.NotRun) ||
            DateTime.UtcNow >= deadline) break;
        Thread.Sleep(250);
    }
    if (phases is null) { error.WriteLine("Trial phases: this Trial recorded none."); return; }
    error.Write(TrialPhaseTable.Render(phases, new TrialPhase("cli-report", Math.Round(cliReport.TotalSeconds, 3))));
}

static string JobsUsage() =>
    "Usage: motif jobs wait <jobId> --project <fwdata> [--json] OR motif jobs show <jobId> --project <fwdata> [--json] OR " +
    "motif jobs assessments <jobId> --project <fwdata> [--json] OR motif jobs list [--all] [--json] OR " +
    "motif jobs cancel <jobId> --project <fwdata> [--json] OR motif jobs requeue <jobId> --project <fwdata> " +
    "[--json] OR " + JobsMoveUsage();

static T WaitWithConsoleCancellation<T>(bool waitForTerminal, Func<CancellationToken, T> wait) =>
    waitForTerminal ? RunWithConsoleCancellation(wait) : wait(CancellationToken.None);

static T RunWithConsoleCancellation<T>(Func<CancellationToken, T> run)
{
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler handler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += handler;
    try
    {
        return run(cancellation.Token);
    }
    finally
    {
        Console.CancelKeyPress -= handler;
    }
}

static string JobsMoveUsage() => UsageLineFor("jobs move");

static bool TryReadCheckCeiling(IReadOnlyDictionary<string, string> flags, string name, ulong fallback, out ulong value)
{
    value = fallback;
    return !flags.TryGetValue(name, out var text) ||
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}

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

// Exactly one subject, each kind by its own flags; a flag left without a value names nothing.
static InspectorSubject? InspectSubjectFrom(IReadOnlyDictionary<string, string> flags)
{
    string? Value(string name) => flags.TryGetValue(name, out var value) ? value : null;
    var kinds = new[] { "rule", "slot", "environment", "feature", "warning" };
    if (kinds.Append("allomorph").Append("grammatical-info")
        .Any(name => Value(name) is "true" || Value(name) is { } value && string.IsNullOrWhiteSpace(value)))
        return null;
    var morpheme = Value("allomorph") is not null || Value("grammatical-info") is not null;
    var named = kinds.Where(name => Value(name) is not null).ToArray();
    if ((morpheme ? 1 : 0) + named.Length != 1) return null;
    if (morpheme) return InspectorSubject.Morpheme(Value("allomorph"), Value("grammatical-info"), identityQuality: "authored");
    var given = Value(named[0])!;
    return named[0] switch
    {
        "rule" => given.Split(':', 2) is [{ Length: > 0 } kind, { Length: > 0 } key]
            ? InspectorSubject.Rule(new TraceTimingKey(kind, key),
                identityQuality: flags.ContainsKey("structural") ? "structural" : Guid.TryParse(key, out _) ? "authored" : "unknown")
            : null,
        "slot" => new InspectorSubject(InspectorSubjectKind.Slot) { ObjectId = given },
        "environment" => new InspectorSubject(InspectorSubjectKind.Environment) { ObjectId = given },
        "feature" => new InspectorSubject(InspectorSubjectKind.Feature) { ObjectId = given },
        _ => given.Split(':', 2) is [{ Length: > 0 } code, .. var rest]
            ? new InspectorSubject(InspectorSubjectKind.Warning) { WarningCode = code, ObjectId = rest.FirstOrDefault() }
            : null,
    };
}

static IReadOnlyList<string> CliUsageArgumentShapes(
    string commandName,
    string verb,
    IReadOnlyDictionary<string, string> flags,
    IReadOnlyList<string> positionals,
    IReadOnlyList<string> forwardedArguments)
{
    var shapes = new List<string>();
    var catalogEntry = CliVerbCatalog.All.FirstOrDefault(entry => entry.CommandName == commandName);
    if (commandName is not "help" && catalogEntry is null)
    {
        if (flags.Count > 0) shapes.Add(UsageArgumentShape.List("flags", flags.Count));
        if (positionals.Count > 0) shapes.Add(UsageArgumentShape.List("arguments", positionals.Count));
        if (forwardedArguments.Count > 0)
            shapes.Add(UsageArgumentShape.List("forwardedArguments", forwardedArguments.Count));
        return shapes;
    }

    if (commandName == "help")
    {
        if (flags.Count > 0) shapes.Add(UsageArgumentShape.List("flags", flags.Count));
        if (positionals.Count > 0) shapes.Add(UsageArgumentShape.List("arguments", positionals.Count));
        if (forwardedArguments.Count > 0)
            shapes.Add(UsageArgumentShape.List("forwardedArguments", forwardedArguments.Count));
        return shapes;
    }

    var allowedFlags = Regex.Matches(
            string.Join(" ", catalogEntry!.UsageLines), "--([a-z0-9][a-z0-9-]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .Select(match => match.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);
    var unrecognizedFlagCount = 0;
    foreach (var (flag, value) in flags)
    {
        if (flag == "json") continue;
        if (!allowedFlags.Contains(flag))
        {
            unrecognizedFlagCount++;
            continue;
        }
        var name = UsageParameterName(flag);
        if (value == "true")
            shapes.Add(UsageArgumentShape.Flag(name));
        else if (flag is "depends-on" or "words" or "texts" or "text-ids" or "occurrences")
            shapes.Add(UsageArgumentShape.List(name,
                value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length));
        else
            shapes.Add(UsageArgumentShape.Text(name));
    }
    if (unrecognizedFlagCount > 0)
        shapes.Add(UsageArgumentShape.List("unrecognizedFlags", unrecognizedFlagCount));

    var positionalOffset = positionals.Count > 0 &&
        commandName == $"{verb} {positionals[0]}" &&
        CliVerbCatalog.All.Any(entry => entry.CommandName == commandName) ? 1 : 0;
    var usageLine = CliVerbCatalog.All.FirstOrDefault(entry => entry.CommandName == commandName)
        ?.UsageLines.FirstOrDefault();
    var positionalNames = usageLine is null ? Array.Empty<string>() : PositionalParameterNames(usageLine);
    var declaredPositionals = Math.Min(positionalNames.Count, Math.Max(0, positionals.Count - positionalOffset));
    for (var index = positionalOffset; index < positionalOffset + declaredPositionals; index++)
    {
        shapes.Add(UsageArgumentShape.Text(positionalNames[index - positionalOffset]));
    }
    var extraPositionals = positionals.Count - positionalOffset - declaredPositionals;
    if (extraPositionals > 0)
        shapes.Add(UsageArgumentShape.List("extraArguments", extraPositionals));

    if (forwardedArguments.Count > 0)
        shapes.Add(UsageArgumentShape.List("forwardedArguments", forwardedArguments.Count));
    return shapes;
}

static IReadOnlyList<string> PositionalParameterNames(string usageLine)
{
    var names = new List<string>();
    foreach (Match match in Regex.Matches(usageLine, "<([^<>]+)>"))
    {
        var prefix = usageLine[..match.Index].TrimEnd();
        var previousToken = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (previousToken?.StartsWith("--", StringComparison.Ordinal) == true) continue;
        names.Add(UsageParameterName(match.Groups[1].Value));
    }
    return names;
}

static string UsageParameterName(string source)
{
    if (source == "project") return "fwDataPath";
    if (source == "assessment") return "assessmentId";
    if (source == "current-grammar-sha256") return "currentGrammarSourceSha256";
    if (source.Contains("fwdata", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("path-to", StringComparison.OrdinalIgnoreCase))
        return "fwDataPath";

    var segments = Regex.Split(source, "[-_]+")
        .Where(segment => segment.Length > 0)
        .ToArray();
    if (segments.Length == 0) return "argument";
    return char.ToLowerInvariant(segments[0][0]) + segments[0][1..] + string.Concat(
        segments.Skip(1).Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
}

static int RefuseUnavailableCommand(
    string commandName, bool asJson, TextWriter error, CommandDescriptor command)
{
    var facts = CommandAvailabilityFacts.For(command);
    var message = CommandAvailabilityText.Render(facts);
    if (asJson)
    {
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.Refused, message, facts.RefusalFacts(), facts.RefusalCode)));
    }
    else
    {
        error.WriteLine("error: " + message);
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
    PrintSection(writer, "Project", "Project (initialization, stored Selection and read-only Assessment summaries):", policy);
    PrintSection(
        writer, "Baseline",
        "Baseline (a saved-file capture of a project FieldWorks may hold open, synchronous, no queue):", policy);
    PrintSection(
        writer, "Assess",
        "Assess (a synchronous PanGloss run over a Selection, stored as Assessments; no queue):", policy);
    PrintSection(
        writer, "Trace",
        "Trace (one word through the Baseline grammar, attempt by attempt, as Why This Analysis? reads it):", policy);
    PrintSection(
        writer, "Handoff",
        "AI Handoff (the self-explaining folder a chat model reads, written atomically):", policy);
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

static bool TryParseReadOccurrences(string? value, Guid textId,
    out IReadOnlyList<OccurrenceAnchor>? occurrences)
{
    occurrences = null;
    if (value is null) return true;

    var parsed = new List<OccurrenceAnchor>();
    foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var parts = item.Split('/');
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var paragraphId) ||
            !Guid.TryParse(parts[1], out var segmentId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var wordIndex) ||
            wordIndex < 0)
            return false;
        parsed.Add(new OccurrenceAnchor(textId, paragraphId, segmentId, wordIndex));
    }

    if (parsed.Count == 0) return false;
    occurrences = parsed;
    return true;
}

static bool TryParseExpectedContext(string? value, out ExpectedContext? expectedContext)
{
    expectedContext = null;
    if (value is null) return true;
    try
    {
        expectedContext = System.Text.Json.JsonSerializer.Deserialize<ExpectedContext>(value,
            MotifJson.CreateOptions());
        return expectedContext?.Baseline is not null;
    }
    catch (System.Text.Json.JsonException)
    {
        return false;
    }
}

static (Dictionary<string, string> Flags, List<string> Positionals, IReadOnlyList<string> Forwarded) ParseArgs(
    string[] tokens, bool joinRepeatedCorpus = false)
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
            if (name is "latest" or "wait-terminal" or "word-results" or "refresh-report" or "trace" or "md" or "apply" or "bulk" or "effects" or "no-background" or "no-base-sample" or "touched" or "siblings" or "no-siblings" or "verbose" or "replace" or "skip-refused" or "current" or "tokens" or "strict" or "fold-case" or "split-hyphens" or "thorough" or "with-counts" or "no-run" or "build")
            {
                flags[name] = "true";
                continue;
            }
            // --autonomous and --force are switches: the next token stays a positional unless it is true or false.
            if (name == CommandSurfacePolicy.AutonomousFlag[2..] || name == "force")
            {
                var explicitValue = i + 1 < tokens.Length && tokens[i + 1] is "true" or "false";
                flags[name] = explicitValue ? tokens[++i] : "true";
                continue;
            }
            // Canonical ids can start with "--", so recognize those before treating a token as another switch.
            if (i + 1 >= tokens.Length ||
                (tokens[i + 1].StartsWith("--", StringComparison.Ordinal) &&
                    !CanonicalId.TryParse(tokens[i + 1], out _)))
                flags[name] = "true";
            else
            {
                var value = tokens[++i];
                var joins = name is "assessment" or "expect" or "job" or "with" or "twin" || (name == "corpus" && joinRepeatedCorpus);
                flags[name] = joins && flags.TryGetValue(name, out var previous)
                    ? previous + "\n" + value
                    : value;
            }
        }
        else
        {
            positionals.Add(token);
        }
    }

    return (flags, positionals, Array.Empty<string>());
}

static bool HasProjectTagValue(string[] tokens)
{
    var forwardingBoundary = Array.IndexOf(tokens, "--");
    var parsedLength = forwardingBoundary < 0 ? tokens.Length : forwardingBoundary;
    for (var index = parsedLength - 1; index >= 0; index--)
    {
        if (tokens[index] != "--set") continue;
        if (index + 1 >= parsedLength) return false;
        var value = tokens[index + 1];
        return !value.StartsWith("--", StringComparison.Ordinal) || CanonicalId.TryParse(value, out _);
    }
    return false;
}

// The version this CLI negotiates with; the worker decides compatibility from the protocol range, not this.
static string CliProductVersion() => MotifProductVersion.CurrentText;

static string ReadBoundedUtf8File(string path, int maximumBytes)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (stream.Length > maximumBytes)
        throw new InvalidDataException($"The file exceeds the {maximumBytes} byte limit.");
    var buffer = new byte[maximumBytes + 1];
    var total = 0;
    while (total < buffer.Length)
    {
        var read = stream.Read(buffer, total, buffer.Length - total);
        if (read == 0) break;
        total += read;
    }
    if (total > maximumBytes)
        throw new InvalidDataException($"The file exceeds the {maximumBytes} byte limit.");
    return new UTF8Encoding(false, true).GetString(buffer, 0, total);
}

static T? DeserializePhonologyCursor<T>(string json) where T : class
{
    using var document = JsonDocument.Parse(json);
    EnsureUniqueJsonProperties(document.RootElement);
    return JsonSerializer.Deserialize<T>(json, PhonologyJson.CreateOptions());
}

static void EnsureUniqueJsonProperties(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new JsonException($"The cursor JSON repeats '{property.Name}'.");
            EnsureUniqueJsonProperties(property.Value);
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray()) EnsureUniqueJsonProperties(item);
    }
}

// Autonomous is declared by --autonomous or MOTIF_AUTONOMOUS=1 (or true); with neither, the mode is person.
static ApplyAttribution ApplyAttributionFromFlags(
    Dictionary<string, string> flags, IReadOnlyDictionary<string, string?> environment)
{
    var autonomous = (flags.TryGetValue("autonomous", out var raw) && IsTruthyFlag(raw)) ||
                     CommandSurfacePolicy.IsAutonomousValue(environment.GetValueOrDefault(ApplyModes.EnvironmentVariable));
    return new ApplyAttribution(autonomous ? ApplyModes.Autonomous : ApplyModes.Person,
        flags.GetValueOrDefault("message"), flags.TryGetValue("note", out var note) && IsTruthyFlag(note));
}

static bool IsTruthyFlag(string value) => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

static IReadOnlyList<string> ReadRepeatedFlagValues(Dictionary<string, string> flags, string name) =>
    flags.TryGetValue(name, out var value)
        ? value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [];

/// <summary>Upserts a named project into the machine store's <c>KnownProjects</c>.</summary>
static Exception? RecordKnownProject(string fwDataPath, string root, TextWriter error)
{
    try
    {
        var fullPath = Path.GetFullPath(fwDataPath);
        if (!File.Exists(fullPath)) return null;

        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var failure = KnownProjectRecorder.TryRecord(root, project);
        if (failure is not null)
            error.WriteLine("warning: this project could not be recorded for background work (" +
                failure.Message + "). Queued jobs will not run until it is.");
        return failure;
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
    {
        // Reported, not thrown: an unregistered project is never swept, so silence would hide lost work.
        error.WriteLine("warning: this project could not be recorded for background work (" +
            exception.Message + "). Queued jobs will not run until it is.");
        return exception;
    }
}

static bool QueuesJobs(string verb, IReadOnlyList<string> positionals) => verb switch
{
    "dry-run" or "trial" or "baseline-refresh" or "parsimony" => true,
    "phonology-examples" => positionals.Count > 0 && positionals[0] == "check",
    "jobs" => positionals.Count > 0 && positionals[0] == "requeue",
    _ => false,
};

static int RefuseProjectTag(ProjectTagException failure, bool asJson, TextWriter error)
{
    var code = failure.Problem == ProjectTagProblem.Taken ? RefusalCodes.ProjectTagTaken
        : failure.Problem == ProjectTagProblem.Invalid ? RefusalCodes.ProjectTagInvalid
        : RefusalCodes.ProjectTagUnavailable;
    var facts = new Dictionary<string, string>(StringComparer.Ordinal);
    if (failure.Tag.Length > 0) facts["tag"] = failure.Tag;
    if (failure.CollidingProjectPath is not null) facts["collidingProject"] = failure.CollidingProjectPath;
    var message = failure.Message + " Nothing was queued.";
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(new FailureEnvelope(FailureReason.Refused, message, facts, code)));
    else
        error.WriteLine("error: " + message);
    return FailureEnvelope.ExitCodeFor(FailureReason.Refused);
}
}
}
