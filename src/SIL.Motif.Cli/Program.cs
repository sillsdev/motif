using SIL.Motif.Projection;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
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
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Projects;
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

internal static int Run(
    string[] args,
    TextWriter output,
    TextWriter error,
    IReadOnlyDictionary<string, string?> environment,
    Action<string, Action<string>?> startRunner)
{
    using var ambient = AmbientEnvironment.Push(environment);
if (ArtifactInventoryCli.IsInvocation(args))
    return ArtifactInventoryCli.Run(args, output, error,
        environment.GetValueOrDefault(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable));

string ResolveRoot() => environment.TryGetValue(RunnerOptions.RootVariable, out var root)
    && !string.IsNullOrWhiteSpace(root) ? root : RunnerOptions.DefaultRoot;

var usageVerb = args.Length > 0 ? args[0] : "cli";
var usageArguments = args.Length > 0
    ? ParseArgs(args[1..])
    : (new Dictionary<string, string>(StringComparer.Ordinal), new List<string>(),
        (IReadOnlyList<string>)Array.Empty<string>());
var resolvedUsageCommand = args.Length > 0
    ? ResolveCommandName(usageVerb, usageArguments.Item1, usageArguments.Item2)
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

var advancedAiModeEnabled = environment.TryGetValue(
        FileAdvancedAiModePreferenceStore.PathEnvironmentVariable, out var preferencePath) &&
    new FileAdvancedAiModePreferenceStore(preferencePath!).IsEnabled;
var commandPolicy = CommandSurfacePolicy.FromEnvironment(
    environment.GetValueOrDefault(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable), advancedAiModeEnabled);

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
string? namedProject = null;

try
{
    var (flags, positionals, forwardedArguments) = ParseArgs(rest);
    namedProject = flags.TryGetValue("project", out var flaggedProject) ? flaggedProject
        : verb == "open" && positionals.Count == 1 ? positionals[0] : null;
    var commandName = ResolveCommandName(verb, flags, positionals);
    var command = CommandCatalog.All.FirstOrDefault(item => item.Name == commandName);
    if (command is not null && !commandPolicy.IsAvailable(command))
        return RefuseUnavailableCommand(commandName, flags.ContainsKey("json"), error, command);

    // Every invocation naming a project upserts it into the machine store (ADR 0041 decision 4).
    if (flags.TryGetValue("project", out var projectForRegistry) &&
        commandName is not ("project initialize" or "project tag" or "project tag --set"))
        RecordKnownProject(projectForRegistry, ResolveRoot(), error);

    // Every migrated read surface renders both ways from one projection (ADR 0021 decision 2).
    asJson = flags.ContainsKey("json");
    CommandResult result;
    // RenderProposal below already fully renders text or JSON; the bottom printer must not wrap it again.
    var alreadyRendered = false;

    CommandResult RenderProposal<T>(CommandOutcome<T> outcome, bool successAsJson = true) where T : class
    {
        alreadyRendered = true;
        return ProposalCommandRenderer.Render(outcome, asJson, successAsJson);
    }

    CommandResult RenderCommand<T>(CommandOutcome<T> outcome, bool successAsJson = true) where T : class?
    {
        alreadyRendered = true;
        return CommandTextRenderer.Render(outcome, asJson, successAsJson);
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

    switch (verb)
    {
        case var typedVerb when ProposalCommands.ComposerDescriptors.Any(composer => composer.Verb == typedVerb):
            if (!flags.TryGetValue("draft", out var typedDraft) ||
                !flags.TryGetValue("project", out var typedProject) ||
                !flags.TryGetValue("intent", out var typedIntent))
                return Usage("Usage: motif " + CliVerbCatalog.ComposerUsage(typedVerb), asJson);
            result = RenderProposal(
                ProposalCommands.ComposeIntent(new(typedProject, CliProductVersion(), typedDraft, typedVerb,
                    typedIntent, commandCitation)),
                successAsJson: false);
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
                batchProject, CliProductVersion(), batchDraft, File.ReadAllText(batchFile))), successAsJson: false);
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
                ProposalCommands.DiscardDraft(
                    new DiscardDraftRequest(discardProject, CliProductVersion(), discardDraftName,
                        flags.GetValueOrDefault("revision"))),
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
            if (!flags.TryGetValue("project", out var listProject) ||
                flags.TryGetValue("status", out var listStatus) && string.IsNullOrWhiteSpace(listStatus))
                return Usage("Usage: motif list --project <fwdata> [--all | --status <status>] [--json]", asJson);
            result = RenderProposal(
                ProposalCommands.List(new ListProposalsRequest(listProject, CliProductVersion(),
                    flags.ContainsKey("all"), flags.GetValueOrDefault("status"))));
            break;

        case "show":
            if (!flags.TryGetValue("project", out var showProject) || positionals.Count != 1)
                return Usage("Usage: motif show --project <fwdata> <proposalId> [--json]", asJson);
            result = RenderProposal(
                ProposalCommands.Show(new ShowProposalRequest(showProject, CliProductVersion(), positionals[0])));
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
                    "Usage: motif dry-run --project <fwdata> <proposalId> [--wait] [--json]", asJson);
            }
            var dryRunWaitTimeout = JobCommands.DefaultWaitTimeout;
            if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                "Usage: motif dry-run --project <fwdata> <proposalId> [--wait] " +
                "[--wait-timeout-ms <ms>] [--json]", asJson, out dryRunWaitTimeout) is { } dryRunUsage)
                return dryRunUsage;
            result = RenderCommand(
                JobCommands.EnqueueDryRun(
                    new EnqueueDryRunRequest(dryRunProject, CliProductVersion(), positionals[0])),
                successAsJson: false);
            // A job just entered the queue: wake the runner before anything below waits on it.
            if (result.ExitCode == 0) startRunner(dryRunProject, error.WriteLine);
            if (result.ExitCode == 0 && flags.ContainsKey("wait"))
            {
                var dryRunJobId = result.Output.Trim();
                var dryRunWakeWarnings = RunnerWarnings.Once(error.WriteLine);
                result = RenderCommand(JobCommands.WaitForDryRun(new WaitForDryRunRequest(
                    dryRunProject, CliProductVersion(), positionals[0], dryRunJobId, dryRunWaitTimeout),
                    wakeRunner: () => startRunner(dryRunProject, dryRunWakeWarnings)));
            }
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
                        "--words <w,…> --wait [--wait-timeout-ms <ms>] " +
                        "[--before-correctness <assessmentId>] [--json]", asJson);
                }
                var words = pendingTrialWords.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (words.Length == 0)
                    return Usage("Usage: motif trial --pending requires at least one --words value.", asJson);
                if (ParseWaitTimeout(flags,
                    "Usage: motif trial --pending --project <fwdata> [--draft <id>] [--revision <r>] " +
                    "--words <w,…> --wait [--wait-timeout-ms <ms>] " +
                    "[--before-correctness <assessmentId>] [--json]", asJson, out var pendingWaitTimeout)
                    is { } pendingTrialUsage)
                    return pendingTrialUsage;
                result = RunWithConsoleCancellation(cancellationToken => RenderCommand(
                    PendingChangesWorkflow.Measure(new MeasurePendingRequest(
                        pendingTrialProject, flags.GetValueOrDefault("draft"),
                        flags.GetValueOrDefault("revision"), words,
                        flags.GetValueOrDefault("before-correctness")), new Progress<MeasureProgress>(),
                        cancellationToken, pendingWaitTimeout).GetAwaiter().GetResult()));
                break;
            }
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var trialProject))
            {
                return Usage(
                    "Usage: motif trial --project <fwdata> <proposalId> [--scope <name>] " +
                    "[--words <file>|--all-words] [--wait] [--json]", asJson);
            }
            IReadOnlyList<string>? trialWords = null;
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
            var trialWaitTimeout = JobCommands.DefaultWaitTimeout;
            if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                "Usage: motif trial --project <fwdata> <proposalId> [--scope <name>] " +
                "[--words <file>|--all-words] " +
                "[--wait] [--wait-timeout-ms <ms>] [--json]", asJson, out trialWaitTimeout) is { } trialUsage)
                return trialUsage;
            // The workflow assesses the Baseline and takes the update lease, as the MCP tool and the window do.
            result = RunWithConsoleCancellation(cancellationToken =>
            {
                var queued = TrialWorkflow.Enqueue(
                    new EnqueueTrialRequest(
                        trialProject, CliProductVersion(), positionals[0], flags.GetValueOrDefault("scope"),
                        Words: trialWords, AllWords: flags.ContainsKey("all-words")),
                    ResolveRoot(), null, BaselineMode.BestEffort, cancellationToken);
                if (queued.Value?.BaselineWarning is { } reason)
                    error.WriteLine(
                        "warning: the Trial has no Baseline Assessment, so `difference produce` will refuse it: " + reason);
                return RenderCommand(queued.Succeeded
                    ? CommandOutcome<JobEnqueuedResponse>.Success(queued.Value!.Job)
                    : CommandOutcome<JobEnqueuedResponse>.Refused(queued.Refusal!), successAsJson: false);
            });
            // A job just entered the queue: wake the runner before anything below waits on it.
            if (result.ExitCode == 0) startRunner(trialProject, error.WriteLine);
            if (result.ExitCode == 0 && flags.ContainsKey("wait"))
            {
                var trialJobId = result.Output.Trim();
                var trialWakeWarnings = RunnerWarnings.Once(error.WriteLine);
                result = RenderCommand(JobCommands.WaitForJob(
                    new WaitForJobRequest(trialProject, trialJobId, CliProductVersion(), trialWaitTimeout),
                    () => startRunner(trialProject, trialWakeWarnings)));
            }
            break;

        case "apply":
            var applyAttribution = ApplyAttributionFromFlags(flags, environment);
            if (ApplyAttributionCheck.Refusal(applyAttribution, flags.ContainsKey("force")) is { } applyAttributionRefusal)
            {
                result = RenderProposal(CommandOutcome<ApplyPendingResult>.Refused(applyAttributionRefusal));
                break;
            }
            if (flags.ContainsKey("all-pending"))
            {
                if (positionals.Count != 0 || flags.ContainsKey("user") ||
                    !flags.TryGetValue("project", out var pendingApplyProject))
                    return Usage(
                        "Usage: motif apply --all-pending --project <fwdata> [--revision <r>] " +
                        "[--autonomous] [--message <text>] [--note] [--json]", asJson);
                var pendingRequest = new ApplyPendingRequest(pendingApplyProject,
                    DraftId: null, Revision: flags.GetValueOrDefault("revision"),
                    Attribution: applyAttribution);
                result = RunWithConsoleCancellation(cancellationToken => RenderProposal(
                    PendingChangesWorkflow.Apply(pendingRequest, cancellationToken)));
                break;
            }
            if (flags.ContainsKey("proposal"))
            {
                if (positionals.Count != 0 || flags.ContainsKey("user") ||
                    !flags.TryGetValue("project", out var proposalApplyProject) ||
                    string.IsNullOrWhiteSpace(flags["proposal"]))
                    return Usage(UsageLineFor("apply --proposal"), asJson);
                var proposalRequest = new ApplyProposalRequest(proposalApplyProject, flags["proposal"],
                    flags.GetValueOrDefault("revision"), applyAttribution);
                result = RunWithConsoleCancellation(cancellationToken => RenderProposal(
                    PendingChangesWorkflow.Apply(proposalRequest, cancellationToken)));
                break;
            }
            if (positionals.Count != 1 ||
                flags.ContainsKey("user") ||
                !flags.TryGetValue("project", out var applyProject))
            {
                return Usage(
                    "Usage: motif apply <proposalId> --project <fwdata> " +
                    "[--force] [--autonomous] [--message <text>] [--note] [--json]", asJson);
            }
            var applyForce = flags.ContainsKey("force");
            result = RenderProposal(
                PendingChangesWorkflow.Apply(
                    new ApplyRequest(applyProject, CliProductVersion(), positionals[0], applyForce,
                        applyAttribution)));
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
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var parsimonyProject))
                return Usage("Usage: motif parsimony --project <fwdata> [--measure <measure-id>] " +
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
                        ? RunWithConsoleCancellation(cancellationToken => RenderCommand(ParsimonyCommands.WaitAll(
                            new WaitForParsimonyReportSetRequest(parsimonyProject, CliProductVersion(),
                                parsimonyJobId, parsimonyWaitTimeout), cancellationToken,
                            () => startRunner(parsimonyProject, parsimonyWakeWarnings))))
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
                    result = RenderCommand(CaptureBaseline(captureProject, ResolveRoot()));
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

        case "proposal":
            if (positionals.Count == 1 && positionals[0] == "review" &&
                flags.TryGetValue("project", out var proposalReviewProject))
            {
                result = RenderCommand(ProposalReview.Read(new ProposalReviewRequest(
                    proposalReviewProject, flags.GetValueOrDefault("proposal"))));
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "review")
                return Usage(UsageLineFor("proposal review"), asJson);
            if (positionals.Count == 2 && positionals[0] is "rationale" or "note")
            {
                var isNote = positionals[0] == "note";
                var textUsage = UsageLineFor(isNote ? "proposal note" : "proposal rationale");
                if (!flags.TryGetValue("project", out var textProject)) return Usage(textUsage, asJson);
                if (!isNote && flags.ContainsKey("clear"))
                    return Usage("A Rationale cannot be cleared, only rewritten: give --text or --file. " + textUsage, asJson);
                var sources = new[] { "text", "file" }.Count(flags.ContainsKey) + (isNote && flags.ContainsKey("clear") ? 1 : 0);
                if (sources != 1) return Usage(textUsage, asJson);
                string? textBody;
                if (flags.TryGetValue("file", out var textFile))
                {
                    if (!File.Exists(textFile))
                        return Usage("The file '" + textFile + "' does not exist. " + textUsage, asJson);
                    textBody = File.ReadAllText(textFile);
                }
                else textBody = flags.TryGetValue("text", out var textValue) ? textValue : null;
                result = RenderCommand(isNote
                    ? ProposalTexts.SetNote(new SetNoteRequest(textProject, CliProductVersion(), positionals[1], textBody))
                    : ProposalTexts.SetRationale(new SetRationaleRequest(
                        textProject, CliProductVersion(), positionals[1], textBody ?? "")));
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
            if (positionals.Count != 2 || positionals[0] != "trial" ||
                !flags.TryGetValue("project", out var trialReadProject))
                return Usage("Usage: motif proposal trial --project <fwdata> <proposalId> [--json]", asJson);
            result = RenderCommand(Trials.Read(new ReadTrialRequest(trialReadProject, positionals[1])));
            break;

        case "writing-systems":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var writingSystemsProject))
                return Usage(UsageLineFor("writing-systems"), asJson);
            result = RenderCommand(WritingSystemsQuery.Query(new WritingSystemsRequest(writingSystemsProject)));
            break;

        case "word-context":
            if (positionals.Count != 0 || !flags.TryGetValue("project", out var contextProject) ||
                !flags.TryGetValue("word", out var contextWord) || string.IsNullOrWhiteSpace(contextWord))
                return Usage(UsageLineFor("word-context"), asJson);
            result = RenderCommand(WordContextQuery.Query(new WordContextRequest(contextProject, contextWord)));
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
                    var checkLimits = PhonologyExampleValidation.DefaultLimits;
                    if (!TryReadCheckCeiling(flags, "step-cap", checkLimits.StepCap, out var checkStepCap) ||
                        !TryReadCheckCeiling(flags, "work-cap", checkLimits.WorkCap, out var checkWorkCap) ||
                        !TryReadCheckCeiling(flags, "timeout-ms", checkLimits.TimeoutMs, out var checkTimeout) ||
                        checkTimeout > uint.MaxValue)
                        return Usage("--step-cap, --work-cap and --timeout-ms must be whole numbers.", asJson);
                    var checkWaitTimeout = JobCommands.DefaultWaitTimeout;
                    if (flags.ContainsKey("wait") && ParseWaitTimeout(flags,
                        UsageLineFor("phonology-examples check"), asJson, out checkWaitTimeout) is { } checkUsage)
                        return checkUsage;
                    var queuedCheck = PhonologyExampleCommands.Check(new PhonologyCheckRequest(phonologyProject,
                        positionals[1], null, new PhonologyLimits(checkStepCap, checkWorkCap, (uint)checkTimeout)),
                        SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());
                    result = RenderCommand(queuedCheck);
                    if (queuedCheck.Succeeded)
                    {
                        startRunner(phonologyProject, error.WriteLine);
                        if (flags.ContainsKey("wait"))
                            result = RenderCommand(JobCommands.WaitForJob(new WaitForJobRequest(phonologyProject,
                                queuedCheck.Value!.JobId, CliProductVersion(), checkWaitTimeout)));
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

        case "warnings":
            if (!flags.TryGetValue("project", out var warningsProject) ||
                (flags.TryGetValue("kind", out var warningKind) && string.IsNullOrWhiteSpace(warningKind)))
                return Usage(UsageLineFor("warnings"), asJson);
            result = RenderCommand(WarningsCommand.Warnings(new WarningsRequest(
                warningsProject, flags.GetValueOrDefault("kind"), flags.ContainsKey("left-out"),
                flags.GetValueOrDefault("proposal"))));
            break;

        case "difference":
            if (positionals.Count == 1 && positionals[0] == "produce")
            {
                if (!flags.TryGetValue("project", out var differenceProject) ||
                    !flags.TryGetValue("proposal", out var differenceProposal) ||
                    !flags.TryGetValue("text", out var differenceText))
                    return Usage(UsageLineFor("difference produce"), asJson);
                result = RenderCommand(ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(
                    differenceProject, differenceProposal, differenceText, flags.GetValueOrDefault("job"))), asJson);
                break;
            }
            if (positionals.Count == 1 && positionals[0] == "show")
            {
                var differenceOffset = 0;
                var differenceLimit = 25;
                if (!flags.TryGetValue("project", out var deltaShowProject) ||
                    !(flags.ContainsKey("proposal") || flags.ContainsKey("difference")) ||
                    flags.TryGetValue("offset", out var differenceOffsetText) &&
                    !int.TryParse(differenceOffsetText, NumberStyles.None, CultureInfo.InvariantCulture, out differenceOffset) ||
                    flags.TryGetValue("limit", out var differenceLimitText) &&
                    !int.TryParse(differenceLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out differenceLimit))
                    return Usage(UsageLineFor("difference show"), asJson);
                result = RenderCommand(ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(deltaShowProject,
                    flags.GetValueOrDefault("proposal"), flags.GetValueOrDefault("difference"),
                    flags.ContainsKey("could-be-bad"), flags.GetValueOrDefault("bucket"),
                    flags.GetValueOrDefault("before"), flags.GetValueOrDefault("after"), differenceOffset,
                    differenceLimit)), asJson);
                break;
            }
            return Usage(UsageLineFor("difference"), asJson);

        case "grammar":
            if (positionals.Count == 1 && positionals[0] == "read")
            {
                if (flags.Keys.Any(name => name is not ("project" or "kind" or "id" or "query" or "offset" or "limit" or "json")) ||
                    !flags.TryGetValue("project", out var grammarReadProject) ||
                    !flags.TryGetValue("kind", out var grammarReadKindText) ||
                    !Enum.TryParse<GrammarReadKind>(grammarReadKindText, ignoreCase: false, out var grammarReadKind) ||
                    !Enum.IsDefined(grammarReadKind))
                    return Usage(UsageLineFor("grammar read"), asJson);
                var grammarReadOffset = 0;
                var grammarReadLimit = 25;
                if (flags.TryGetValue("offset", out var grammarReadOffsetText) &&
                    !int.TryParse(grammarReadOffsetText, NumberStyles.None, CultureInfo.InvariantCulture, out grammarReadOffset) ||
                    flags.TryGetValue("limit", out var grammarReadLimitText) &&
                    !int.TryParse(grammarReadLimitText, NumberStyles.None, CultureInfo.InvariantCulture, out grammarReadLimit))
                    return Usage(UsageLineFor("grammar read"), asJson);
                var grammarRead = GrammarReadQuery.Query(new GrammarReadRequest(
                    grammarReadProject,
                    grammarReadKind,
                    flags.GetValueOrDefault("id"),
                    flags.GetValueOrDefault("query"),
                    grammarReadOffset,
                    grammarReadLimit));
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
            result = RunWithConsoleCancellation(token => RenderCommand(
                GrammarCheckQuery.Query(new GrammarCheckRequest(grammarProject, StoredAnalyses: storedAnalyses), token)));
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

        case "selection":
            if (positionals.Count != 1 || !flags.TryGetValue("project", out var selectionProject))
                return Usage(UsageLineFor("selection show") + " OR " + UsageLineFor("selection set-default") +
                    " OR " + UsageLineFor("selection set-limits"), asJson);
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
                    var addedWords = flags.TryGetValue("add-words", out var addedWordsRaw)
                        ? addedWordsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : Array.Empty<string>();
                    if (!TryReadSelectionLimits(flags, out var limits))
                        return Usage(UsageLineFor("selection set-default"), asJson);
                    var existingSelection = SelectionCommands.ReadDefault(new ReadDefaultSelectionRequest(selectionProject));
                    if (existingSelection.Value?.Selection is { } savedSelection)
                        limits = limits with { AssessBudgetSeconds = savedSelection.Limits.AssessBudgetSeconds };
                    result = RenderCommand(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
                        selectionProject, selectionName, selectionTexts, addedWords, limits,
                        flags.GetValueOrDefault("expected-revision"))));
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
                        " OR " + UsageLineFor("selection set-limits"), asJson);
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

        case "store":
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
                    "retry-work-over" or "retry-source-assessment" or "replaces" or "time-limit-ms" or "step-cap" or
                    "summary" or "offset" or "limit" or "full-object-timing" or "max-seconds" or "json"))) return Usage(AssessUsage(), asJson);
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
            StepCap? assessStepCap = null;
            if (flags.TryGetValue("step-cap", out var assessStepCapRaw))
            {
                try { assessStepCap = StepCap.Parse(assessStepCapRaw); }
                catch (FormatException) { return Usage(AssessUsage(), asJson); }
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
                    var assessed = AssessCommand.Assess(
                        new AssessRequest(positionals[0], assessSelection, assessTimeLimitMs, assessStepCap, assessReplaces,
                            flags.ContainsKey("full-object-timing"), assessBudgetSeconds),
                        asJson ? null : progress => error.WriteLine(progress.Message), cancellationToken);
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
                    const string jobsWaitUsage = "Usage: motif jobs wait <jobId> --project <fwdata> [--wait-timeout-ms <ms>] [--json]";
                    if (positionals.Count != 2 || !flags.TryGetValue("project", out var jobsWaitProject))
                        return Usage(jobsWaitUsage, asJson);
                    if (ParseWaitTimeout(flags, jobsWaitUsage, asJson, out var jobsWaitTimeout) is { } jobsWaitError)
                        return jobsWaitError;
                    result = RenderCommand(JobCommands.WaitForJob(
                        new WaitForJobRequest(jobsWaitProject, positionals[1], CliProductVersion(), jobsWaitTimeout)));
                    break;

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

    if (result.ExitCode == 0)
    {
        output.Write(result.Output);
        return result.ExitCode;
    }
    // A caller that asked for JSON gets JSON when it goes wrong too; a Proposal verb already did this itself.
    error.Write(!alreadyRendered && asJson && result.Reason is { } reason
        ? ProjectionJson.Serialize(new FailureEnvelope(reason, result.Output.Trim())) + Environment.NewLine
        : result.Output);
    return result.ExitCode;
}
catch (ProjectWritingSystemsUnavailableException ex)
{
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
    var message = string.IsNullOrWhiteSpace(ex.Message) ? "The CLI operation was cancelled." : ex.Message;
    if (asJson)
        error.WriteLine(ProjectionJson.Serialize(
            new FailureEnvelope(FailureReason.Cancelled, message, code: "cli.cancelled")));
    else
        error.WriteLine($"cli.cancelled: {message}");
    return FailureEnvelope.ExitCodeFor(FailureReason.Cancelled);
}
catch (Exception ex)
{
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

static CommandOutcome<BaselineCaptureResponse> CaptureBaseline(string selector, string managedRoot)
{
    var selectorIsPath = Path.IsPathRooted(selector) || selector.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase) ||
                         selector.Contains(Path.DirectorySeparatorChar) || selector.Contains(Path.AltDirectorySeparatorChar);
    if (selectorIsPath)
        return BaselineCaptureCommand.Capture(new BaselineCaptureRequest(selector));

    try
    {
        var projectPath = ProjectResolver.Resolve(selector, managedRoot);
        return BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath));
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
    limits = new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null);
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

    var stepLimit = StepCap.Default;
    if (flags.TryGetValue("step-cap", out var stepText))
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(stepText, "none")) stepLimit = StepCap.Unbounded;
        else
        {
            try { stepLimit = StepCap.Parse(stepText); }
            catch (FormatException) { return false; }
        }
    }

    if (stepLimit.IsUnbounded && timeMode != SelectionTimeLimitMode.Estimated) return false;
    limits = new SelectionParsingLimits(stepLimit, timeMode, explicitTime);
    return limits.ValidationError() is null;
}

static string JobsUsage() =>
    "Usage: motif jobs wait <jobId> --project <fwdata> [--json] OR motif jobs show <jobId> --project <fwdata> [--json] OR " +
    "motif jobs assessments <jobId> --project <fwdata> [--json] OR motif jobs list --all [--json] OR " +
    "motif jobs cancel <jobId> --project <fwdata> [--json] OR motif jobs requeue <jobId> --project <fwdata> " +
    "[--json] OR " + JobsMoveUsage();

static CommandResult RunWithConsoleCancellation(Func<CancellationToken, CommandResult> run)
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

static string ResolveCommandName(string verb, IReadOnlyDictionary<string, string> flags,
    IReadOnlyList<string> positionals)
{
    if (verb == "proposal" && positionals.FirstOrDefault() is "trial" or "review" or "looked-at" or "rationale" or "note")
        return "proposal " + positionals[0];
    if (verb == "parsimony")
    {
        if (positionals.SequenceEqual(new[] { "expectations" })) return "parsimony expectations";
        if (positionals.Count == 2 && positionals[0] == "negative" && positionals[1] == "confirm")
            return "parsimony negative confirm";
        if (positionals.Count == 2 && positionals[0] == "negative" && positionals[1] == "retract")
            return "parsimony negative retract";
        if (positionals.Count == 2 && positionals[0] == "revise") return "parsimony revise";
        if (positionals.Count == 2 && positionals[0] == "retract") return "parsimony retract";
        if (positionals.FirstOrDefault() == "show") return "parsimony show";
        if (positionals.FirstOrDefault() == "latest") return "parsimony latest";
        if (positionals.FirstOrDefault() == "retirement-review") return "parsimony retirement-review";
        if (positionals.FirstOrDefault() == "measures") return "parsimony measures";
        if (positionals.FirstOrDefault() == "view") return "parsimony view";
        if (positionals.FirstOrDefault() == "record-types") return "parsimony record-types";
        if (positionals.FirstOrDefault() == "dispose") return "parsimony dispose";
        if (flags.ContainsKey("wait"))
            return flags.GetValueOrDefault("measure") == ParsimonyAdmission.AllMeasures && !flags.ContainsKey("dry-run")
                ? "parsimony --measure all --wait" : "parsimony --wait";
        return "parsimony";
    }
    if (verb == "apply" && flags.ContainsKey("all-pending"))
        return "apply --all-pending";
    if (verb == "delete") return "delete --proposal";
    if (verb is "reject" or "reopen" && flags.ContainsKey("proposal")) return verb + " --proposal";
    if (verb == "apply" && flags.ContainsKey("proposal"))
        return "apply --proposal";
    if (verb == "trial" && flags.ContainsKey("pending"))
        return "trial --pending";
    if (verb == "phonology-examples" && positionals.FirstOrDefault() is "save" or "list" or "show" or "check" or "results")
        return verb + " " + positionals[0];
    if (verb == "project" && positionals.SequenceEqual(new[] { "tag" }) && flags.ContainsKey("set"))
        return "project tag --set";
    if (verb is "config" or "baseline" or "grammar" or "jobs" or "project" or "selection" or "texts" or "setup" or "store" or "word" or "difference" or "evidence")
    {
        var candidate = positionals.Count > 0 ? verb + " " + positionals[0] : verb;
        if (CommandCatalog.All.Any(command => command.Name == candidate)) return candidate;
        return verb;
    }

    if (verb == "report" && flags.ContainsKey("list-kinds"))
        return "report --list-kinds";
    if (verb == "trace" && flags.ContainsKey("load"))
        return "trace --load";
    if (verb is "dry-run" or "trial" && flags.ContainsKey("wait"))
        return verb + " --wait";
    return verb;
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
        "Trace (one word through the Baseline grammar, attempt by attempt, as Try a Word reads it):", policy);
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
            // Canonical ids can start with "--", so recognize those before treating a token as another switch.
            if (i + 1 >= tokens.Length ||
                (tokens[i + 1].StartsWith("--", StringComparison.Ordinal) &&
                    !CanonicalId.TryParse(tokens[i + 1], out _)))
                flags[name] = "true";
            else
            {
                var value = tokens[++i];
                flags[name] = name == "assessment" && flags.TryGetValue(name, out var previous)
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
    var env = environment.GetValueOrDefault(ApplyModes.EnvironmentVariable);
    var autonomous = (flags.TryGetValue("autonomous", out var raw) && IsTruthyFlag(raw)) ||
                     string.Equals(env?.Trim(), "1", StringComparison.Ordinal) ||
                     string.Equals(env?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    return new ApplyAttribution(autonomous ? ApplyModes.Autonomous : ApplyModes.Person,
        flags.GetValueOrDefault("message"), flags.TryGetValue("note", out var note) && IsTruthyFlag(note));
}

static bool IsTruthyFlag(string value) => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

static IReadOnlyList<string> ReadRepeatedFlagValues(Dictionary<string, string> flags, string name) =>
    flags.TryGetValue(name, out var value)
        ? value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [];

/// <summary>Upserts a named project into the machine store's <c>KnownProjects</c>.</summary>
static void RecordKnownProject(string fwDataPath, string root, TextWriter error)
{
    try
    {
        var fullPath = Path.GetFullPath(fwDataPath);
        if (!File.Exists(fullPath)) return;

        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var failure = KnownProjectRecorder.TryRecord(root, project);
        if (failure is not null)
            error.WriteLine("warning: this project could not be recorded for background work (" +
                failure.Message + "). Queued jobs will not run until it is.");
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
    {
        // Reported, not thrown: an unregistered project is never swept, so silence would hide lost work.
        error.WriteLine("warning: this project could not be recorded for background work (" +
            exception.Message + "). Queued jobs will not run until it is.");
    }
}
}
}
