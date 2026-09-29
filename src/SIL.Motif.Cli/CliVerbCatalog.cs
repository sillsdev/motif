using System;
using System.Collections.Generic;

namespace SIL.Motif.Cli;

/// <summary>
/// One CLI invocation path: the section <c>PrintUsage</c> groups it under, the verb it dispatches on,
/// the single command it reaches (matched by name against
/// <see cref="SIL.Motif.Commands.Catalog.CommandCatalog.All"/>), and the usage line(s) printed for it.
/// A verb whose flag selects between two different handlers — <c>report</c>, <c>dry-run</c>,
/// <c>trial</c> — contributes descriptors that share a <see cref="Section"/> and <see cref="Verb"/>
/// while naming distinct <see cref="CommandName"/> values and their invocation paths.
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
        new CliVerbDescriptor("Commands", "pending-changes", "pending-changes",
            new[] { "pending-changes --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "put-pending-change", "put-pending-change",
            new[] { "put-pending-change --project <fwdata> --expected-revision <revision> " +
                "--change-id <id> --kind <kind> --word <word> [--wordform-id <id>] " +
                "[--assessment <id> --reading-index <zero-based> --reading-json <json>] " +
                "[--stored-analysis-id <id>] [--occurrence-text-id <guid> " +
                "--occurrence-paragraph-id <guid> --occurrence-segment-id <guid> " +
                "--occurrence-index <zero-based>] [--json]" }),
        new CliVerbDescriptor("Commands", "remove-analysis", "remove-analysis",
            new[] { "remove-analysis --project <fwdata> --expected-revision <revision> " +
                "(--analysis-id <id> --change-id <id> --wordform-id <id> --word <word> " +
                "| --analysis-ids <id,id,...> | --text-id <guid>) [--json]" }),
        new CliVerbDescriptor("Commands", "accept-new-set", "accept-new-set",
            new[] { "accept-new-set --project <fwdata> --expected-revision <revision> " +
                "--assessment <id> (--wordform-id <id> | --text-id <guid> | --selection) [--json]" }),
        new CliVerbDescriptor("Commands", "remove-pending-change", "remove-pending-change",
            new[] { "remove-pending-change --project <fwdata> --expected-revision <revision> " +
                "--change-id <id> [--json]" }),
        new CliVerbDescriptor("Commands", "recheck-pending-changes", "recheck-pending-changes",
            new[] { "recheck-pending-changes --project <fwdata> --expected-revision <revision> [--json]" }),
        new CliVerbDescriptor("Commands", "reconfirm-pending-change", "reconfirm-pending-change",
            new[] { "reconfirm-pending-change --project <fwdata> --expected-revision <revision> " +
                "--change-id <id> [--json]" }),
        new CliVerbDescriptor("Commands", "review-numbers", "review-numbers",
            new[] { "review-numbers --project <fwdata> [--from <assessmentId>] " +
                "--to <assessmentId> --touched-words <count> [--json]" }),
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
            "Commands", "preflight", "preflight",
            new[] { "preflight --project <fwdata> <proposalId> [--json]" }),
        new CliVerbDescriptor(
            "Commands", "dry-run", "dry-run",
            new[] { "dry-run --project <fwdata> <proposalId> [--wait] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "dry-run", "dry-run --wait",
            new[] { "dry-run --project <fwdata> <proposalId> --wait [--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial",
            new[] { "trial --project <fwdata> <proposalId> [--scope <name>] [--all-words] [--wait] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial --wait",
            new[]
            {
                "trial --project <fwdata> <proposalId> [--scope <name>] [--all-words] --wait " +
                "[--wait-timeout-ms <ms>] [--json]",
            }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial --pending",
            new[]
            {
                "trial --pending --project <fwdata> [--draft <id>] [--revision <r>] --words <w,…> " +
                "--wait [--wait-timeout-ms <ms>] [--before-correctness <assessmentId>] [--json]",
            }),
        new CliVerbDescriptor(
            "Commands", "apply", "apply",
            new[] { "apply <proposalId> --project <fwdata> --user <name> [--force] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "apply", "apply --all-pending",
            new[] { "apply --all-pending --project <fwdata> [--revision <r>] [--user <name>] [--json]" }),
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
                "[--retry-failed] [--retry-slower-than <ms>] [--retry-source-assessment <id>] " +
                "[--time-limit-ms <ms>] [--step-cap <steps|unbounded>] [--json]",
                "The default per-word step cap is 1,000,000 steps.",
            }),

        new CliVerbDescriptor(
            "Assess", "stats", "stats",
            new[] { "stats <project> [--assessment <id>] [--json] [-- <pangloss stats options>]" }),

        new CliVerbDescriptor(
            "Project", "selection", "selection show",
            new[] { "selection show --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "selection", "selection set-default",
            new[]
            {
                "selection set-default --project <fwdata> --name <name> [--texts <guid,guid>] " +
                "[--add-words <word,word>] [--time-limit-ms <ms>] [--step-cap <steps|unbounded>] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "texts", "texts list",
            new[] { "texts list --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "setup", "setup skip",
            new[] { "setup skip --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "store", "store delete-refused",
            new[] { "store delete-refused --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "word", "word read-state",
            new[]
            {
                "word read-state --project <fwdata> --text <textId> [--read | --unread] " +
                "[--occurrences <paragraphId>/<segmentId>/<wordIndex>[,...]] [--json]",
                "With no action flag, it reads state; without occurrence anchors, an action applies to every word in the Text.",
            }),
        new CliVerbDescriptor(
            "Project", "overview", "overview",
            new[] { "overview --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "warnings", "warnings",
            new[] { "warnings --project <fwdata> [--kind <code>] [--left-out] [--json]" }),
        new CliVerbDescriptor(
            "Project", "grammar", "grammar check",
            new[] { "grammar check --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "timing", "timing",
            new[]
            {
                "timing --project <fwdata> [--assessment <id>] [--words <set>] [--word <word,word>] " +
                "[--by kind|rule] [--rule <name>] [--top N] [--json]",
            }),

        new CliVerbDescriptor(
            "Handoff", "handoff", "handoff",
            new[]
            {
                "handoff <project> --out <folder> --invocation <id> [--no-assess] [--json] OR " +
                "motif handoff <project> --out <folder> --no-assess [--texts <id,…>] [--json]",
            }),

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
