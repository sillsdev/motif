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
        new CliVerbDescriptor("Project", "writing-systems", "writing-systems",
            new[] { "writing-systems --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "project initialize", "project initialize",
            new[]
            {
                "project initialize --project <fwdata> --confirm \"Initialize this project to work with Motif Proposals?\" [--json]",
            }),
        new CliVerbDescriptor("Project", "word-context", "word-context",
            new[] { "word-context --project <fwdata> --word <word> [--json]" }),
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
                "--occurrence-index <zero-based>] [--expected-context <json>] [--json]" }),
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
        new CliVerbDescriptor("Commands", "compose-author-feature-value", "compose-author-feature-value",
            new[] { "compose-author-feature-value --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-phoneme", "compose-author-phoneme",
            new[] { "compose-author-phoneme --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-natural-class", "compose-author-natural-class",
            new[] { "compose-author-natural-class --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-environment", "compose-author-environment",
            new[] { "compose-author-environment --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-phonological-rule", "compose-author-phonological-rule",
            new[] { "compose-author-phonological-rule --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-affix-slot", "compose-author-affix-slot",
            new[] { "compose-author-affix-slot --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-affix-template", "compose-author-affix-template",
            new[] { "compose-author-affix-template --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-adhoc-prohibition", "compose-edit-adhoc-prohibition",
            new[] { "compose-edit-adhoc-prohibition --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-affix-slot", "compose-edit-affix-slot",
            new[] { "compose-edit-affix-slot --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-affix-template", "compose-edit-affix-template",
            new[] { "compose-edit-affix-template --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-inflectional-affix", "compose-edit-inflectional-affix",
            new[] { "compose-edit-inflectional-affix --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-allomorph-condition", "compose-edit-allomorph-condition",
            new[] { "compose-edit-allomorph-condition --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-natural-class", "compose-edit-natural-class",
            new[] { "compose-edit-natural-class --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-relink-natural-class", "compose-relink-natural-class",
            new[] { "compose-relink-natural-class --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "order-allomorphs", "order-allomorphs",
            new[] { "order-allomorphs --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-record-parsimony-disposition", "compose-record-parsimony-disposition",
            new[] { "compose-record-parsimony-disposition --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "retire-allomorph", "retire-allomorph",
            new[] { "retire-allomorph --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "retire-redundant-zero-affix", "retire-redundant-zero-affix",
            new[] { "retire-redundant-zero-affix --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
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
            new[] { "trial --project <fwdata> <proposalId> [--scope <name>] [--words <file>|--all-words] [--wait] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial --wait",
            new[]
            {
                "trial --project <fwdata> <proposalId> [--scope <name>] [--words <file>|--all-words] --wait " +
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
        new CliVerbDescriptor("Reports", "parsimony", "parsimony",
            new[] { "parsimony --project <fwdata> [--measure <measure-id>] " +
                "[--evidence-scope default-selection|project-approved] [--assessment <ParseTime-id> ...] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --wait",
            new[] { "parsimony --project <fwdata> [--measure <measure-id>] --wait " +
                "[--evidence-scope default-selection|project-approved] [--assessment <ParseTime-id> ...] " +
                "[--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --dry-run",
            new[] { "parsimony --project <fwdata> --dry-run <job-id> [--measure <measure-id>] " +
                "[--evidence-scope project-approved|default-selection] [--assessment <ParseTime-id> ...] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --dry-run --wait",
            new[] { "parsimony --project <fwdata> --dry-run <job-id> --wait [--measure <measure-id>] " +
                "[--evidence-scope project-approved|default-selection] [--assessment <ParseTime-id> ...] " +
                "[--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony show",
            new[] { "parsimony show --project <fwdata> <reportId> [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony latest",
            new[] { "parsimony latest --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony measures",
            new[] { "parsimony measures --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony expectations",
            new[] { "parsimony expectations --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony record-types",
            new[] { "parsimony record-types --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony dispose",
            new[] { "parsimony dispose --project <fwdata> --report <reportId> --finding <findingId> " +
                "--disposition keep|fix|ask|defer --record-type <portableId> --draft <name> " +
                "[--reason <text>] [--question <text>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony revise",
            new[] { "parsimony revise <recordId> --project <fwdata> --draft <name> " +
                "--expected-heads '<revisionId and contentDigest JSON array>' --disposition keep|fix|ask|defer " +
                "[--reason <text> | --clear-reason] [--question <text>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony retract",
            new[] { "parsimony retract <recordId> --project <fwdata> --draft <name> " +
                "--expected-heads '<revisionId and contentDigest JSON array>' [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony negative confirm",
            new[] { "parsimony negative confirm --project <fwdata> --draft <name> --intent '<closed intent JSON>' " +
                "--confirm \"I confirm this form or reading is forbidden in the stated context.\" [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony negative retract",
            new[] { "parsimony negative retract --project <fwdata> --draft <name> --intent '<closed retraction JSON>' " +
                "--confirm \"I confirm this reviewed negative should be withdrawn.\" [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony view",
            new[] { "parsimony view <view-code> --project <fwdata> --bundle <id> " +
                "[--object-id <guid> | --category-id <guid> | --statement-kind environment|natural-class] " +
                "[--scope project-approved|default-selection] [--case-key <key>] [--report <id>] " +
                "[--measure-id <id>] [--disposition keep|defer|fix|ask] [--state <state>] [--search <text>] " +
                "[--subject-key <key>] [--cursor <cursor>] [--limit <n>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony retirement-review",
            new[] { "parsimony retirement-review <draft-id> --project <fwdata> [--json]" }),

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
                "[--replaces <assessment-id>] " +
                "[--time-limit-ms <ms>] [--step-cap <attempts|unbounded>] [--json]",
                "The default per-word limit is 200,000 analysis attempts. PanGloss derives an inner search-work limit at 100 times that value.",
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
                "[--add-words <word,word>] [--time-mode estimated|explicit] [--time-limit-ms <ms>] " +
                "[--step-cap <steps|none>] [--expected-revision <revision>] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "selection", "selection set-limits",
            new[]
            {
                "selection set-limits --project <fwdata> --name <name> --expected-revision <revision> " +
                "[--time-mode estimated|explicit] [--time-limit-ms <ms>] [--step-cap <steps|none>] [--json]",
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
                "[--occurrences <paragraphId>/<segmentId>/<wordIndex>[,...]] " +
                "[--expected-context <json>] [--json]",
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
                "[--by kind|rule] [--rule <kind>:<key>] [--structural | --local --scope <scope>] [--top N] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "uses", "uses",
            new[]
            {
                "uses --project <fwdata> [--allomorph <guid>] [--grammatical-info <guid>] [--timing <kind>:<key>] " +
                "[--structural | --local --scope <scope>] " +
                "[--words <word,word>] [--json]",
                "Name an object, some words, or both; objects and morphemes are matched by identity, never by spelling.",
                "An object also gets what the Baseline's FieldWorks project says about it, and its timing key when none is given.",
            }),
        new CliVerbDescriptor(
            "Project", "inspect", "inspect",
            new[]
            {
                "inspect --project <fwdata> (--allomorph <guid> [--grammatical-info <guid>] | --grammatical-info <guid> | " +
                "--rule <kind>:<key> [--structural] | --slot <guid> | --environment <guid> | --feature <guid> | " +
                "--warning <code>[:<guid>]) [--json]",
                "One subject, by identity: FieldWorks' facts from the Baseline, its words and times from the stored Parse all words,",
                "and the stored grammar check's findings that name it. Each section says whether it was read, and why not.",
            }),

        new CliVerbDescriptor(
            "Trace", "trace", "trace",
            new[] { "trace --project <fwdata> --word <word> [--json]" }),
        new CliVerbDescriptor(
            "Trace", "trace", "trace --load",
            new[] { "trace --load <file> [--json]" }),

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
