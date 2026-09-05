using System;
using System.Collections.Generic;

namespace SIL.Motif.Cli;

/// <summary>
/// One CLI invocation path: the section <c>PrintUsage</c> groups it under, the verb it dispatches on,
/// the single command it reaches (matched by name against
/// <see cref="SIL.Motif.Commands.Catalog.CommandCatalog.All"/>), and the usage line(s) printed for it.
/// A verb whose flag selects between two different handlers — <c>report</c>, <c>dry-run</c>,
/// <c>trial</c> — contributes two descriptors sharing a <see cref="Section"/> and <see cref="Verb"/>
/// but each naming a distinct <see cref="CommandName"/>; only the first of the pair carries a usage
/// line, so the banner does not print the shared line twice.
/// </summary>
public sealed record CliVerbDescriptor(
    string Section, string Verb, string CommandName, IReadOnlyList<string> UsageLines);

/// <summary>
/// The sole enumeration of Motif's CLI invocation paths (ADR 0043 decision 3): one entry per
/// <c>CommandCatalog</c> command, naming the CLI verb that reaches it. <c>PrintUsage</c> iterates this
/// rather than repeating its own list of verbs and usage text.
/// </summary>
public static class CliVerbCatalog
{
    private static readonly string[] NoUsage = Array.Empty<string>();

    public static IReadOnlyList<CliVerbDescriptor> All { get; } = new[]
    {
        new CliVerbDescriptor("Commands", "open", "open", new[] { "open <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Commands", "analyses", "analyses",
            new[]
            {
                "analyses --project <fwdata> [--json]",
                "analyses --project <fwdata> --assessment <assessmentId> --current-selection-sha256 " +
                "<sha256> --current-grammar-sha256 <sha256> [--json]",
            }),
        new CliVerbDescriptor(
            "Commands", "new", "new",
            new[] { "new --project <fwdata> --draft <name> [--label <text>]" }),
        new CliVerbDescriptor(
            "Commands", "add-set-gloss", "add-set-gloss",
            new[]
            {
                "add-set-gloss --project <fwdata> --draft <name> --target <canonicalId> --ws <wsTag> " +
                "--text <text> [--depends-on <opId>[,<opId>...]]",
            }),
        new CliVerbDescriptor(
            "Commands", "add-delete-lexeme-form", "add-delete-lexeme-form",
            new[] { "add-delete-lexeme-form --project <fwdata> --draft <name> --target <canonicalId>" }),
        new CliVerbDescriptor(
            "Commands", "compose-author-lexeme-form", "compose-author-lexeme-form",
            new[]
            {
                "compose-author-lexeme-form --draft <name> --project <fwdata> --intent " +
                "'{\"entry\":...,\"morphType\":...,\"ws\":...,\"text\":...}'",
            }),
        new CliVerbDescriptor(
            "Commands", "compose-author-feature-structure", "compose-author-feature-structure",
            new[]
            {
                "compose-author-feature-structure --draft <name> --project <fwdata> --intent '{\"msa\":...}'",
            }),
        new CliVerbDescriptor(
            "Commands", "promote-gloss", "promote-gloss",
            new[]
            {
                "promote-gloss --project <fwdata> --draft <name> --target <canonicalId> --ws <wsTag> " +
                "--text <text> --corpus <corpusId> [--document <docId>]",
            }),
        new CliVerbDescriptor(
            "Commands", "label", "label", new[] { "label --project <fwdata> --draft <name> <text>" }),
        new CliVerbDescriptor(
            "Commands", "comment", "comment", new[] { "comment --project <fwdata> --draft <name> <text>" }),
        new CliVerbDescriptor(
            "Commands", "finalize", "finalize", new[] { "finalize --project <fwdata> --draft <name>" }),
        new CliVerbDescriptor(
            "Commands", "discard-draft", "discard-draft",
            new[] { "discard-draft --project <fwdata> --draft <name>" }),
        new CliVerbDescriptor(
            "Commands", "reopen", "reopen", new[] { "reopen --project <fwdata> --draft <name> <proposalId>" }),
        new CliVerbDescriptor(
            "Commands", "duplicate", "duplicate",
            new[] { "duplicate --project <fwdata> --draft <newName> <proposalId>" }),
        new CliVerbDescriptor(
            "Commands", "remove-operations", "remove-operations",
            new[]
            {
                "remove-operations --project <fwdata> --draft <name> <operationId> [<operationId>...] " +
                "[--force]",
            }),
        new CliVerbDescriptor(
            "Commands", "split", "split",
            new[]
            {
                "split --project <fwdata> <proposalId> <draftName>=<opId>[,<opId>...] " +
                "[<draftName>=<opId>[,<opId>...] ...] [--force]",
            }),
        new CliVerbDescriptor(
            "Commands", "defer", "defer", new[] { "defer --project <fwdata> <proposalId>" }),
        new CliVerbDescriptor(
            "Commands", "reject", "reject", new[] { "reject --project <fwdata> <proposalId>" }),
        new CliVerbDescriptor(
            "Commands", "supersede", "supersede",
            new[] { "supersede --project <fwdata> <proposalId> <supersededByProposalId>" }),
        new CliVerbDescriptor(
            "Commands", "list", "list", new[] { "list --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Commands", "show", "show", new[] { "show --project <fwdata> <proposalId> [--json]" }),
        new CliVerbDescriptor(
            "Commands", "dry-run", "dry-run",
            new[] { "dry-run --project <fwdata> <proposalId> [--wait] [--json]" }),
        new CliVerbDescriptor("Commands", "dry-run", "dry-run --wait", NoUsage),
        new CliVerbDescriptor(
            "Commands", "trial", "trial",
            new[] { "trial --project <fwdata> <proposalId> [--scope <name>] [--wait] [--json]" }),
        new CliVerbDescriptor("Commands", "trial", "trial --wait", NoUsage),
        new CliVerbDescriptor(
            "Commands", "apply", "apply",
            new[] { "apply <proposalId> --project <fwdata> --user <name> [--force] [--json]" }),
        new CliVerbDescriptor("Commands", "log", "log", new[] { "log --project <fwdata> [--json]" }),

        new CliVerbDescriptor(
            "Configuration", "config", "config show",
            new[] { "Usage: motif config show --project <fwdata> [--json]" }),

        new CliVerbDescriptor(
            "Reports", "report", "report",
            new[]
            {
                "Usage: motif report --project <fwdata> --assessment <assessmentId> --kind <kind> " +
                "[--word <w>] [--text <t>] [--json] OR motif report --list-kinds [--json]",
            }),
        new CliVerbDescriptor("Reports", "report", "report --list-kinds", NoUsage),

        new CliVerbDescriptor(
            "Comparison", "compare", "compare",
            new[] { "Usage: motif compare --project <fwdata> --from <assessmentId> --to <assessmentId> [--json]" }),

        new CliVerbDescriptor(
            "Baseline", "baseline", "baseline capture",
            new[] { "baseline capture <project> [--json]" }),

        new CliVerbDescriptor(
            "Assess", "assess", "assess",
            new[]
            {
                "assess <project> [--texts <guid,guid>] [--all-wordforms] [--words <file>] " +
                "[--retry-failed] [--retry-slower-than <ms>] [--json]",
            }),

        new CliVerbDescriptor(
            "Assess", "stats", "stats",
            new[] { "stats <project> [--proposal <id>] [--json] [-- <pangloss stats options>]" }),

        new CliVerbDescriptor(
            "Corpus", "add-corpus", "add-corpus",
            new[]
            {
                "add-corpus --project <fwdata> --id <id> --description <text> --tokeniser <name> " +
                "--tokeniser-version <v> [--uri <url>] [--licence <text>] [--tokeniser-notes <text>] " +
                "[--may-derive true|false] [--may-redistribute true|false] " +
                "[--may-use-commercially true|false] [--requires-attribution true|false] " +
                "[--licence-basis <text>]",
            }),
        new CliVerbDescriptor(
            "Corpus", "add-document", "add-document",
            new[]
            {
                "add-document --project <fwdata> --corpus <id> --doc <id> --source <file-or-url> " +
                "[--title <text>] [--licence <text>] [--may-derive true|false] [--licence-basis <text>]",
            }),
        new CliVerbDescriptor(
            "Corpus", "add-corpus-bundle", "add-corpus-bundle",
            new[]
            {
                "add-corpus-bundle --project <fwdata> --bundle <path>   (the handoff a fetching tool writes)",
            }),
        new CliVerbDescriptor(
            "Corpus", "corpora", "corpora", new[] { "corpora --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Corpus", "show-corpus", "show-corpus",
            new[] { "show-corpus --project <fwdata> <corpusId> [--json]" }),

        new CliVerbDescriptor(
            "Jobs", "baseline-refresh", "baseline-refresh",
            new[] { "baseline-refresh --project <fwdata>" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs show", new[] { "jobs show <jobId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs assessments",
            new[] { "jobs assessments <jobId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Jobs", "jobs", "jobs list", new[] { "jobs list --all [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs cancel", new[] { "jobs cancel <jobId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs requeue", new[] { "jobs requeue <jobId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs move",
            new[]
            {
                "motif jobs move <jobId> --project <fwdata> (--before <jobId> | --to-top | --to-bottom) " +
                "[--json]",
            }),
    };
}
