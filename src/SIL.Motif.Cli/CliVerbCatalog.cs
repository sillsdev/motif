using System;
using System.Collections.Generic;
using System.Linq;

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
/// The sole enumeration of Motif's CLI invocation paths (ADR 0018 decision 2): one entry per
/// <c>CommandCatalog</c> command, naming the CLI verb that reaches it. <c>PrintUsage</c> iterates this
/// rather than repeating its own list of verbs and usage text.
/// </summary>
public static class CliVerbCatalog
{
    private static readonly string[] NoUsage = Array.Empty<string>();

    public static IReadOnlyList<CliVerbDescriptor> All { get; } = AddCitationOptions(new[]
    {
        new CliVerbDescriptor("Project", "query", "query pairs", new[] { "query pairs --corpus <words.tsv|folder> --differ suffix|prefix [--x <regex>] [--having <x1,x2>] [--segments <file>] [--fold-case] [--split-hyphens] [--max-x <1-12>] [--limit <n>] [--examples <n>] [--json]" }),
        new CliVerbDescriptor("Project", "query", "query values", new[] { "query values --values classes (--project <fwdata> | --grammar <path>) [--verbose] [--json]" }),
        new CliVerbDescriptor("Project", "query", "query corpus forms", new[] { "query corpus forms --corpus <words.tsv|folder> (forms where <expression> | forms --where <expression> | forms --query-json <file|->) [--grammar <path>] [--project <fwdata>] [--thorough] [--segments <file>] [--fold-case] [--split-hyphens] [--limit <n>] [--segment-test engine|identity] [--group-by cause|parse.state|readings|shape] [--sum tokens] [--sample <n>] [--seed <n>] [--write-words <file>] [--with-counts] [--force] [--what-if-retire <ids>] [--count-by morph-seq] [--adjacent] [--by-morph] [--twin <template>] [--baseline-grammar <path>] [--json]" }),
        new CliVerbDescriptor("Project", "query", "query forms", new[] { "query forms --project <fwdata> [--where <selector>] [--group <columns>] [--min-count <n>] [--distinct <column>] [--fold <set>] [--examples <n>] [--ids] [--raw-letters] [--segment-test engine|identity] [--trial <id>] [--limit <n>] [--as-of baseline|<proposalId> [--sides both] [--sort [baseline.|proposal.]<column>[:desc]]] [--json] OR motif query --project <fwdata> --query-json <file|-> [--json] OR motif query --schema OR motif query stems --project <fwdata> --change <from>><to> --affixes <list> [--at final|initial] [--wordset <ids>] [--discovery <ids>] [--heldout <ids>] [--score tolerance,reliability,control,split,size] [--exception <selector>] [--members <file>] [--context <selector>] [--unit msa|stem] [--controls <n>] [--control-letters <list>] [--test-result] [--where <selector>] [--group <columns>] [--examples <n>] [--json] OR motif query <stems|uses|objects> --project <fwdata> [--where <selector>] [--group <columns>] [--min-count <n>] [--distinct <column>] [--fold <set>] [--examples <n>] [--ids] [--raw-letters] [--trial <id>] [--json] OR motif query evidence <stems|forms> --project <fwdata> (--where <selector> | --members <file>) [--change <from>><to> --affixes <list> --wordset <ids>] [--examples <n>] [--json] OR motif query saved <measure-id> --project <fwdata> [--examples <n>] [--trial <id>] [--json]" }),
        new CliVerbDescriptor("Commands", "open", "open", new[] { "open <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "hand-analysis", "hand-analysis check",
            new[] { "hand-analysis check --project <project> --input <file.json> [--json]" }),
        new CliVerbDescriptor("Project", "progress", "progress",
            new[] { "progress --project <fwdata> [--texts <guid,guid> | --source <id>] [--detail summary|passages] [--limit <n>] [--cursor <cursor>] [--since-revision <revision>] [--json]" }),
        new CliVerbDescriptor("Project", "writing-systems", "writing-systems",
            new[] { "writing-systems --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "guide", "guide list",
            new[] { "guide list --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "guide", "guide show",
            new[] { "guide show --project <fwdata> <GDR-n> [--as-of <draft-or-proposal>] [--json | --format fw-md]" }),
        new CliVerbDescriptor("Project", "guide", "guide edit",
            new[] { "guide edit --project <fwdata> <GDR-n> --from-file <file.md | -> [--draft <name>] [--json]" }),
        new CliVerbDescriptor("Project", "guide", "guide add",
            new[] { "guide add --project <fwdata> --title <text> [--parent <GDR-n>] [--after <GDR-n>] [--from-file <file.md | ->] [--ws <tag>] [--draft <name>] [--json]" }),
        new CliVerbDescriptor("Project", "guide", "guide decide",
            new[] { "guide decide --project <fwdata> <GDR-n> --choosing <A> --over <B> --because <reason> [--draft <name>] [--json]" }),
        new CliVerbDescriptor("Project", "guide", "guide check",
            new[] { "guide check --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "source", "source list",
            new[] { "source list --project <fwdata> [--all] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source show",
            new[] { "source show --project <fwdata> <id> [--json]" }),
        new CliVerbDescriptor("Project", "source", "source passage",
            new[] { "source passage --project <fwdata> --source <id> --ref <chapter> [--limit 100] [--cursor <opaque>] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source where",
            new[] { "source where --project <fwdata> <ref> [--source <id>] [--json]" }),
        new CliVerbDescriptor("Project", "current", "current get",
            new[] { "current get --project <fwdata> [--source <id>] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source set-folder",
            new[] { "source set-folder --project <fwdata> <id> <path> [--json]" }),
        new CliVerbDescriptor("Project", "source", "source clear-folder",
            new[] { "source clear-folder --project <fwdata> <id> [--json]" }),
        new CliVerbDescriptor("Project", "source", "source add",
            new[] { "source add --project <fwdata> <id> --name <text> --versification <name> --context <ranges> [--kind Paratext] [--folder <path>] [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source set-name",
            new[] { "source set-name --project <fwdata> <id> <name> [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source set-context",
            new[] { "source set-context --project <fwdata> <id> <ranges> [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source set-versification",
            new[] { "source set-versification --project <fwdata> <id> <name> [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source remove",
            new[] { "source remove --project <fwdata> <id> [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source restore",
            new[] { "source restore --project <fwdata> <id> [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source approve",
            new[] { "source approve --project <fwdata> <id> <anchor> (--analysis <id> | --wordform <id> --reading <json>) [--bulk] [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "source", "source clear",
            new[] { "source clear --project <fwdata> <id> <anchor> [--apply] [--json]" }),
        new CliVerbDescriptor("Project", "current", "current set",
            new[] { "current set --project <fwdata> <id> <ref> [--json]" }),
        new CliVerbDescriptor("Project", "current", "current set --clear",
            new[] { "current set --project <fwdata> <id> --clear [--json]" }),
        new CliVerbDescriptor("Project", "project create", "project create",
            new[]
            {
                "project create --into <folder> --name <ProjectName> --vernacular <bcp47> [--vernacular-name <text>] " +
                "[--word-forming <characters>] [--analysis <bcp47>] [--json]",
            }),
        new CliVerbDescriptor("Project", "store", "store backup",
            new[] { "store backup --project <fwdata> [--to <dir>] [--json]" }),
        new CliVerbDescriptor("Project", "store", "store restore",
            new[] { "store restore <backup-dir> --project <fwdata> [--force] [--json]" }),
        new CliVerbDescriptor("Project", "project word-forming", "project word-forming",
            new[] { "project word-forming --project <fwdata> --add <characters> [--json]" }),
        new CliVerbDescriptor("Project", "project tag", "project tag",
            new[] { "project tag --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "project tag", "project tag --set",
            new[] { "project tag --set <new-tag> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Project", "project initialize", "project initialize",
            new[]
            {
                "project initialize --project <fwdata> --confirm \"Initialize this project to work with Motif Proposals?\" [--json]",
            }),
        new CliVerbDescriptor("Project", "word-context", "word-context",
            new[] { "word-context --project <fwdata> --word <word> [--json]" }),
        new CliVerbDescriptor("Project", "lexical-entry", "lexical-entry",
            new[] { "lexical-entry --project <fwdata> <entryOrSenseId>... [--json]" }),
        new CliVerbDescriptor("Project", "phonology-examples", "phonology-examples save",
            new[] { "phonology-examples save --project <fwdata> --file <examples.json> [--json]" }),
        new CliVerbDescriptor("Project", "phonology-examples", "phonology-examples list",
            new[] { "phonology-examples list --project <fwdata> [--page-size <n>] " +
                "[--continuation <cursor-json>] [--json]" }),
        new CliVerbDescriptor("Project", "phonology-examples", "phonology-examples show",
            new[] { "phonology-examples show --project <fwdata> <revisionId> [--page-size <n>] " +
                "[--continuation <cursor-json>] [--json]" }),
        new CliVerbDescriptor("Project", "phonology-examples", "phonology-examples check",
            new[] { "phonology-examples check --project <fwdata> <revisionId> [--work-cap <n>] " +
                "[--timeout-ms <ms>] [--wait] [--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor("Project", "phonology-examples", "phonology-examples results",
            new[] { "phonology-examples results --project <fwdata> <assessmentId> [--page-size <n>] " +
                "[--continuation <cursor-json>] [--json]",
                "phonology-examples results --project <fwdata> <assessmentId> --example <exampleId> " +
                "[--out <path.json>] [--json]" }),
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
            new[] { "new --project <fwdata> --draft <name> [--label <text>] [--request <requestId>] [--json]" }),
        new CliVerbDescriptor("Commands", "import", "import",
            new[] { "import --project <fwdata> --file <path> [--draft <name> [--strict]] [--page <n>] [--json]" }),
        new CliVerbDescriptor("Commands", "record export", "record export",
            new[] { "record export --project <fwdata> --out <path> (--record <guid> | --all) [--kind <kind>] [--json]" }),
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
        new CliVerbDescriptor("Commands", "find-composer", "find-composer",
            new[] { "find-composer (--goal <text> | --subject <id>) [--detail concise|detailed] [--json]" }),
        new CliVerbDescriptor("Commands", "review-numbers", "review-numbers",
            new[] { "review-numbers --project <fwdata> [--from <assessmentId>] " +
                "--to <assessmentId> --touched-words <count> [--json]" }),
        new CliVerbDescriptor(
            "Commands", "add-set-gloss", "add-set-gloss",
            new[]
            {
                "add-set-gloss --project <fwdata> --draft <name> --target <canonicalId> --ws <wsTag> " +
                "--text <text> [--depends-on <opId>[,<opId>...]] [--cite-source <text> [--cite-locator <text>]]",
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
        new CliVerbDescriptor("Commands", "compose-author-phoneme", "compose-author-phoneme",
            new[] { "compose-author-phoneme --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-natural-class", "compose-author-natural-class",
            new[] { "compose-author-natural-class --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-environment", "compose-author-environment",
            new[] { "compose-author-environment --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-phonological-rule", "compose-author-phonological-rule",
            new[] { "compose-author-phonological-rule --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-metathesis-rule", "compose-author-metathesis-rule",
            new[] { "compose-author-metathesis-rule --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-part-of-speech", "compose-author-part-of-speech",
            new[] { "compose-author-part-of-speech --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-inflection-feature", "compose-author-inflection-feature",
            new[] { "compose-author-inflection-feature --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-inflection-class", "compose-author-inflection-class",
            new[] { "compose-author-inflection-class --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-stem-name", "compose-author-stem-name",
            new[] { "compose-author-stem-name --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-lexical-entry", "compose-author-lexical-entry",
            new[] { "compose-author-lexical-entry --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-author-alternate-form", "compose-author-alternate-form",
            new[] { "compose-author-alternate-form --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
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
        new CliVerbDescriptor("Commands", "compose-author-text", "compose-author-text",
            new[] { "compose-author-text --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-edit-analysis-decision", "compose-edit-analysis-decision",
            new[] { "compose-edit-analysis-decision --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "order-allomorphs", "order-allomorphs",
            new[] { "order-allomorphs --draft <name> --project <fwdata> --intent '<closed intent JSON>'" }),
        new CliVerbDescriptor("Commands", "compose-batch", "compose-batch",
            new[] { "compose-batch --draft <name> --project <fwdata> --intents <file.json> [--json]" }),
        new CliVerbDescriptor("Commands", "compose", "compose",
            new[] { "compose --proposal <name> --project <fwdata> --steps '<json array>' [--remove <id>[,<id>...]] [--json]" }),
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
            "Commands", "finalize", "finalize", new[] { "finalize --project <fwdata> --draft <name>" }),
        new CliVerbDescriptor(
            "Commands", "discard-draft", "discard-draft",
            new[] { "discard-draft --project <fwdata> --draft <name> [--revision <r>]" }),
        new CliVerbDescriptor(
            "Commands", "discard-empty-drafts", "discard-empty-drafts",
            new[] { "discard-empty-drafts --project <fwdata> [--prefix <text>]" }),
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
            "Commands", "undefer", "undefer", new[] { "undefer --project <fwdata> <proposalId>" }),
        new CliVerbDescriptor(
            "Commands", "reject", "reject", new[] { "reject --project <fwdata> <proposalId>" }),
        new CliVerbDescriptor(
            "Commands", "supersede", "supersede",
            new[] { "supersede --project <fwdata> <proposalId> <supersededByProposalId>" }),
        new CliVerbDescriptor(
            "Commands", "list", "list", new[] { "list --project <fwdata> [--all | --status <status>] [--limit <1-200>] [--offset <n> | --page <N>] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "show", "show", new[] { "show --project <fwdata> <proposalId|draftName> [--current] [--limit <1-200>] [--offset <n> | --page <N> | --all] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "preflight", "preflight",
            new[] { "preflight --project <fwdata> <proposalId> [--json]" }),
        new CliVerbDescriptor(
            "Commands", "dry-run", "dry-run",
            new[] { "dry-run --project <fwdata> <proposalId> [--wait] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "dry-run", "dry-run --wait",
            new[] { "dry-run --project <fwdata> <proposalId> --wait [--wait-terminal] [--wait-timeout-ms <ms>] [--no-background] [--effects] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial",
            new[] { "trial --project <fwdata> <proposalId> [--scope <name>] [--words <file>|--all-words] [--touched] [--siblings --corpus-types <file> [--siblings-max <n>] [--siblings-pattern <regex>]|--no-siblings] [--no-base-sample] [--review-set <ids>|none] [--wait [--wait-terminal] [--wait-timeout-ms <ms>] [--no-background] [--expect-loss <file>] [--verbose]] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial --wait",
            new[]
            {
                "trial --project <fwdata> <proposalId> [--scope <name>] [--words <file>|--all-words] [--touched] [--siblings --corpus-types <file> [--siblings-max <n>] [--siblings-pattern <regex>]|--no-siblings] [--no-base-sample] --wait " +
                "[--wait-terminal] [--wait-timeout-ms <ms>] [--no-background] [--expect-loss <file>] [--verbose] [--json]",
            }),
        new CliVerbDescriptor(
            "Commands", "trial", "trial --pending",
            new[]
            {
                "trial --pending --project <fwdata> [--draft <id>] [--revision <r>] --words <w,…> " +
                "--wait [--wait-terminal] [--wait-timeout-ms <ms>] [--before-correctness <assessmentId>] [--json]",
            }),
        new CliVerbDescriptor(
            "Commands", "apply", "apply",
            new[] { "apply <proposalId> --project <fwdata> [--force] [--receipt <file>] [--autonomous] [--message <text>] [--note] [--no-background] [--effects] [--json]" }),
        new CliVerbDescriptor(
            "Commands", "apply", "apply --all-pending",
            new[] { "apply --all-pending --project <fwdata> [--revision <r>] [--autonomous] [--message <text>] [--note] [--no-background] [--effects] [--json]" }),
        new CliVerbDescriptor("Commands", "log", "log", new[] { "log --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "citations", "citations",
            new[] { "citations --project <fwdata> [--uncited] [--json]" }),
        new CliVerbDescriptor("Commands", "evidence show", "evidence show",
            new[] { "evidence show <subject> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "evidence list", "evidence list",
            new[] { "evidence list --project <fwdata> [--tier <tier>] [--offset <n>] [--limit <n>] [--json]" }),
        new CliVerbDescriptor("Commands", "evidence export", "evidence export",
            new[] { "evidence export --project <fwdata> [--out <path.json>] [--tier <tier>] [--page-size <n>] [--json]" }),

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
        new CliVerbDescriptor("Reports", "coverage", "coverage",
            new[] { "coverage --project <fwdata> --ledger <tsv> --sidecar <json> --frequency <file> [--frequency-format tsv|tsv-header|json] " +
                "[--audit-dir <dir>] [--protected <file>] [--limit <1-200>] [--tokens] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony",
            new[] { "parsimony --project <fwdata> [--measure <measure-id>|all] [--group grammar|text|all] " +
                "[--evidence-scope default-selection|project-approved] [--assessment <ParseTime-id> ...] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --wait",
            new[] { "parsimony --project <fwdata> [--measure <measure-id>] --wait " +
                "[--evidence-scope default-selection|project-approved] [--assessment <ParseTime-id> ...] " +
                "[--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --measure all --wait",
            new[] { "parsimony --project <fwdata> --measure all --wait " +
                "[--evidence-scope default-selection|project-approved] [--assessment <ParseTime-id> ...] " +
                "[--wait-timeout-ms <ms>] [--trend <tsv>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --rule-attempts",
            new[] { "parsimony --project <fwdata> --rule-attempts --plan <intents.json> [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --group",
            new[] { "parsimony --project <fwdata> --group grammar|text|all [--measure <measure-id>] " +
                "[--assessment <ParseTime-id> ...] [--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --dry-run",
            new[] { "parsimony --project <fwdata> --dry-run <job-id> [--measure <measure-id>] " +
                "[--evidence-scope project-approved|default-selection] [--assessment <ParseTime-id> ...] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony --dry-run --wait",
            new[] { "parsimony --project <fwdata> --dry-run <job-id> --wait [--measure <measure-id>] " +
                "[--evidence-scope project-approved|default-selection] [--assessment <ParseTime-id> ...] " +
                "[--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor("Reports", "parsimony", "parsimony status",
            new[] { "parsimony status --project <fwdata> --proposal <id> [--json]" }),
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
            new[] { "baseline capture --project <fwdata> [--no-background] [--json]" }),

        new CliVerbDescriptor(
            "Assess", "assess", "assess",
            new[]
            {
                "assess <project> [--texts <guid,guid>] [--all-wordforms] [--words <file>] " +
                "[--retry-failed] [--retry-work-over <parser-work>] [--retry-source-assessment <id>] " +
                "[--replaces <assessment-id>] [--full-object-timing] " +
                "[--collect <name>[,<name>...]] [--time-limit-ms <ms>] [--parser-work-limit <work|unbounded>] [--max-seconds <seconds>] [--summary [--offset <n>] [--limit <n>]] [--no-background] [--json]",
                "The default per-word limit is 20,000,000 parser work.",
                "--collect names the collectors for this run and wins over the project's .motif.toml collect, which wins over the built-in default; ParseTime is always collected.",
                "Reaching the parser work limit is a stop at the work limit, the same on every computer. --time-limit-ms is only a safety net, in milliseconds on this computer; --max-seconds is the whole run's time budget.",
            }),

        new CliVerbDescriptor(
            "Assess", "stats", "stats",
            new[] { "stats <project> [--assessment <id>] [--proposal <id>] [--json] [-- <pangloss stats options>]" }),

        new CliVerbDescriptor(
            "Project", "selection", "selection show",
            new[] { "selection show --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "selection", "selection set-default",
            new[]
            {
                "selection set-default --project <fwdata> --name <name> [--texts <guid,guid>] " +
                "[--add-words <word,word> | --words-file <path>] [--time-mode estimated|explicit] [--time-limit-ms <ms>] " +
                "[--parser-work-limit <work|none>] [--expected-revision <revision>] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "selection", "selection set-parsimony",
            new[]
            {
                "selection set-parsimony --project <fwdata> --name <name> [--texts <guid,guid>] [--json]",
                "Without --texts the name must already be a saved Selection. The Default Selection is not changed.",
            }),
        new CliVerbDescriptor(
            "Project", "selection", "selection set-limits",
            new[]
            {
                "selection set-limits --project <fwdata> --name <name> --expected-revision <revision> " +
                "[--time-mode estimated|explicit] [--time-limit-ms <ms>] [--parser-work-limit <work|none>] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "texts", "texts list",
            new[] { "texts list --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "setup", "setup skip",
            new[] { "setup skip --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "store", "store upgrade",
            new[] { "store upgrade --project <fwdata> [--json]", "store upgrade --source <old.db> --out <new.db> [--json]" }),
        new CliVerbDescriptor(
            "Project", "store", "store delete-refused",
            new[] { "store delete-refused --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "store", "store relocate",
            new[] { "store relocate --project <fwdata> --confirm \"" + SIL.Motif.Commands.StoreRelocateCommand.ConfirmationPhrase + "\" [--match-by-name] [--json]" }),
        new CliVerbDescriptor(
            "Project", "store", "store prune",
            new[] { "store prune --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Project", "store", "store artifacts",
            new[] { "store artifacts --root <worker-root> --stores <stores.json> --json" }),
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
            new[] { "overview --project <fwdata> [--proposal <id>] [--json]" }),
        new CliVerbDescriptor("Commands", "group", "group",
            new[] { "group <suffix|prefix> --project <fwdata> (--ending <text> | --start <text> | --pattern <regex>) [--parse-state parsed|unparsed|approved|all] [--limit <1-200>] [--page <N>] [--min-stem <N>] [--fold <set>] [--as-of baseline|<proposalId>] [--format table|json] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal compare", "proposal compare",
            new[] { "proposal compare <proposalId> <proposalId> [<proposalId>...] --project <fwdata> [--at <digest>] [--job <jobId>]... [--page <N> --snapshot <token>] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal trial delta", "proposal trial delta",
            new[] { "proposal trial delta --project <fwdata> (--job <jobId> | --proposal <id>) [--classes lost,gained,changed,doubled,stopped-negative] [--write-manifests <dir> [--alias-created true|false] [--keep-case]] [--expect-retired <file>] [--expect-added <file>] [--max-doubled <n>] [--max-doubled-frac <0-1>] [--write-receipt <file>] [--limit <1-200>] [--offset <n> | --page <N>] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal trial export", "proposal trial export",
            new[] { "proposal trial export <jobId> --project <fwdata> [--at <digest>] [--page <N>] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal trial", "proposal trial",
            new[] { "proposal trial --project <fwdata> <proposalId> [--job <id>|--report <id>] [--detail summary|failures|full] [--section <id>] [--page <N>] [--json]",
                "proposal trial --project <fwdata> <proposalId> --latest [--json]" }),
        new CliVerbDescriptor(
            "Project", "warnings", "warnings",
            new[] { "warnings --project <fwdata> [--proposal <id>] [--kind <code>] [--left-out] [--limit <1-200>] [--offset <n> | --page <N> | --all] [--json]" }),
        new CliVerbDescriptor(
            "Project", "difference", "difference produce",
            new[] { "difference produce --project <fwdata> --proposal <id> --text <guid> [--job <id>] [--reference human|source-validated] [--json]" }),
        new CliVerbDescriptor(
            "Project", "difference", "difference show",
            new[]
            {
                "difference show --project <fwdata> (--proposal <id> [--text <guid>] [--reference human|source-validated] | " +
                "--difference <id>) [--could-be-bad] " +
                "[--bucket <code>] [--limitation <code>] [--before <state>] [--after <state>] [--offset <n>] [--limit <n>] " +
                "[--word <word> [--occurrence-offset <n>] [--occurrence-limit <n>]] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "grammar", "grammar check",
            new[] { "grammar check --project <fwdata> [--stored-analyses budget|all|off] [--limit <1-200>] [--offset <n> | --page <N> | --all] [--json]" }),
        new CliVerbDescriptor(
            "Project", "grammar", "grammar read",
            new[]
            {
                "grammar read --project <fwdata> --kind <phoneme|natural_class|environment|part_of_speech|inflection_feature|lexical_entry|phonological_rule> " +
                "[--id <canonicalId> | --query <text> | --exact <form>] [--morph-type <name>[,<name>]] [--at baseline|<proposalId>] [--limit <1-100>] [--offset <n> | --page <N>] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "lexicon", "lexicon find",
            new[]
            {
                "lexicon find --project <fwdata> [--query <text> | --exact <form>] [--form-regex <regex>] [--suffix <text>] [--class <name|id>] " +
                "[--environment <text>] [--stem-name <name|id>] [--used-by <slot|template>] [--detail] " +
                "[--morph-type <name>[,<name>]] [--affix [true|false]] [--category <name|id>] [--headword <prefix>] [--headword-contains <text>] " +
                "[--has-gloss [true|false]] [--id-format canonical|guid] [--at baseline|<proposalId>] [--limit <1-200>] " +
                "[--offset <n> | --page <N> | --all] [--json]",
                "lexicon find --project <fwdata> --batch <file|-> [--at baseline|<proposalId>]",
                "A batch file holds one JSON object per line, keyed by the flag names above (\"form-regex\", \"suffix\", \"class\", \"limit\"...); the Baseline is opened once and each line answers as one JSON line.",
            }),
        new CliVerbDescriptor(
            "Project", "timing", "timing",
            new[]
            {
                "timing --project <fwdata> [--assessment <id>] [--proposal <id>] [--words <set>] [--word <word,word>] " +
                "[--by kind|rule] [--rule <kind>:<key>] [--structural | --local --scope <scope>] [--top N] [--json]",
            }),
        new CliVerbDescriptor(
            "Project", "activity", "activity",
            new[] { "activity --log <file.jsonl> [--json]" }),
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
                "inspect --project <fwdata> [--proposal <id>] (--allomorph <guid> [--grammatical-info <guid>] | --grammatical-info <guid> | " +
                "--rule <kind>:<key> [--structural] | --slot <guid> | --environment <guid> | --feature <guid> | " +
                "--warning <code>[:<guid>]) [--json]",
                "One subject, by identity: FieldWorks' facts from the Baseline, its words and times from the stored Parse all words,",
                "and the stored grammar check's findings that name it. Each section says whether it was read, and why not.",
            }),

        new CliVerbDescriptor(
            "Trace", "trace", "trace",
            new[] { "trace --project <fwdata> --word <word> [--proposal <id>] [--json]" }),
        new CliVerbDescriptor(
            "Trace", "trace", "trace --load",
            new[] { "trace --load <file> [--json]" }),

        new CliVerbDescriptor(
            "Trace", "parse", "parse",
            new[]
            {
                "parse <word> --project <fwdata> --trace [--at <baseline|proposal-id>] [--max-paths <1-64>] [--json]",
                "Where PanGloss's parse of the word stopped, path by path: each candidate's earliest recorded stop, with",
                "grammar objects named by canonical id, or by an explicit unknown where PanGloss could not bind them.",
            }),
        new CliVerbDescriptor(
            "Trace", "why-not", "why-not",
            new[]
            {
                "why-not <word> --project <fwdata> --expect <morphs> [--expect <morphs> ...] [--at <baseline|proposal-id>] [--json]",
                "Whether PanGloss parses the word as each expected morph sequence, and for a rejection the first failing",
                "constraint with the morphs and grammar object it is about. Write a candidate as kib,ak|ik,ek.",
            }),
        new CliVerbDescriptor(
            "Trace", "proposal why-lost", "proposal why-lost",
            new[]
            {
                "proposal why-lost <word> --project <fwdata> --proposal <id> [--max-paths <1-64>] [--json]",
                "Which Readings of the word the Proposal's latest Trial copy lost against the Baseline, and for each the stop",
                "PanGloss recorded and the Proposal operations that overlap the earlier form (not a cause), or that nothing was recorded.",
            }),
        new CliVerbDescriptor(
            "Trace", "generate", "generate",
            new[]
            {
                "generate <root> --project <fwdata> --trace [--at <baseline|proposal-id>] [--json]",
                "The forms PanGloss generates from one root alone, with the rules that made each; the same as forms explain <root>.",
            }),
        new CliVerbDescriptor(
            "Trace", "forms", "forms explain",
            new[]
            {
                "forms explain <root> --project <fwdata> [--with <morpheme> ...] [--at <baseline|proposal-id>] [--timeout-ms <n>] [--max-work <n>] [--json|--md] OR " +
                "motif forms explain --word <form> --project <fwdata> [--at <baseline|proposal-id>] [--timeout-ms <n>] [--max-work <n>] [--json|--md]",
                "Every form PanGloss generates for a root, alone or with the listed morphemes: each surface, the allomorphs",
                "chosen, and the rules that fired in order with the word before and after each. With --word, parses the word and",
                "explains each analysis from its own morphemes. A root is an entry guid or Motif id, entry:msa, or a form in the",
                "lexicon. Each request has a deadline of 30000 ms (--timeout-ms) and PanGloss's own work cap unless --max-work lowers it.",
                "Says when a cap stopped the generation, so a missing form is never read as an impossible one.",
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
        new CliVerbDescriptor("Corpus", "corpus-paste", "corpus-paste",
            new[] { "corpus-paste --project <fwdata> --corpus <id> --doc <id> --title <title> --text <text> [--licence <text>] [--json]" }),
        new CliVerbDescriptor("Corpus", "corpus-reindex", "corpus-reindex",
            new[] { "corpus-reindex --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Corpus", "corpus-open", "corpus-open",
            new[] { "corpus-open --project <fwdata> --corpus <id> --doc <id> [--json]" }),
        new CliVerbDescriptor("Corpus", "corpus-range", "corpus-range",
            new[] { "corpus-range --project <fwdata> --corpus <id> --doc <id> --sha256 <hash> --offset <bytes> --length <bytes> [--json]" }),
        new CliVerbDescriptor(
            "Corpus", "corpora", "corpora", new[] { "corpora --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Corpus", "show-corpus", "show-corpus",
            new[] { "show-corpus --project <fwdata> <corpusId> [--json]" }),

        new CliVerbDescriptor(
            "Jobs", "baseline-refresh", "baseline-refresh",
            new[] { "baseline-refresh --project <fwdata>" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs wait", new[] { "jobs wait <jobId> --project <fwdata> [--wait-terminal] [--wait-timeout-ms <ms>] [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs show", new[] { "jobs show <jobId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "jobs", "jobs assessments",
            new[] { "jobs assessments <jobId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Jobs", "jobs", "jobs list", new[] { "jobs list [--all] [--json]" }),
        new CliVerbDescriptor(
            "Jobs", "worker", "worker stop", new[] { "worker stop [--project <fwdata>] [--json]" }),
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
        new CliVerbDescriptor(
            "Commands", "proposals", "proposals",
            new[] { "proposals --project <fwdata> [--include-set-aside] [--json]" }),
        new CliVerbDescriptor("Commands", "delete", "delete --proposal",
            new[] { "delete --proposal <id> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "reject", "reject --proposal",
            new[] { "reject --proposal <id> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "reopen", "reopen --proposal",
            new[] { "reopen --proposal <id> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "withdraw", "withdraw",
            new[] { "withdraw --proposal <id> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal review",
            new[] { "proposal review --project <fwdata> [--proposal <id>] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal rationale",
            new[] { "proposal rationale <proposalId> --project <fwdata> (--text <markdown> | --file <path>) [--author <label>] [--person <name>] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal comment add",
            new[]
            {
                "proposal comment add <proposalId> --project <fwdata> (--text <markdown> | --file <path>) " +
                "[--author <label>] [--person <name>] [--json]",
            }),
        new CliVerbDescriptor("Commands", "proposal", "proposal comment edit",
            new[]
            {
                "proposal comment edit <proposalId> <commentId> --project <fwdata> (--text <markdown> | --file <path>) [--json]",
            }),
        new CliVerbDescriptor("Commands", "proposal", "proposal comment delete",
            new[] { "proposal comment delete <proposalId> <commentId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal picture add",
            new[] { "proposal picture add <proposalId> --project <fwdata> --file <webp-png-jpeg-gif-or-bmp> [--comment <commentId>] [--caption <text>] [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal picture remove",
            new[] { "proposal picture remove <proposalId> <pictureId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal picture show",
            new[] { "proposal picture show <proposalId> [<pictureId> --out <path>] --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "list", "list requests",
            new[] { "list requests --project <fwdata> [--status open|answered] [--offset <n>] [--limit <n>] [--json]" }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis add",
            new[] { "hypothesis add --project <fwdata> --file <hypothesis.json> [--json]" }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis test",
            new[] { "hypothesis test <H-n> --project <fwdata> --file <control-test.json> [--command <text>] [--json]" }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis decide",
            new[]
            {
                "hypothesis decide <H-n> --project <fwdata> --status passed|failed|superseded --decision <text> " +
                "[--superseded-by <H-n>] [--supersedes <H-n,...>] [--json]",
            }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis link",
            new[]
            {
                "hypothesis link <H-n> --project <fwdata> [--proposal <id,...>] [--guide <GDR-n> --guide-section <text> | --guide-none <reason>] [--json]",
            }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis grade",
            new[]
            {
                "hypothesis grade <H-n> --project <fwdata> [--sealed-verdict match|mismatch] [--verdict-source <text>] " +
                "[--word-questions <n>] [--objects-before <n>] [--types-before <n>] [--objects-after <n>] [--types-after <n>] " +
                "[--agent-hours <x>] [--alternative-hours <x>] [--json]",
            }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis answer",
            new[] { "hypothesis answer <H-n> --project <fwdata> --question <n> --answer <text> [--json]" }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis neighbours",
            new[] { "hypothesis neighbours --project <fwdata> --change <x>:<y> [--environment <text>] [--host-class <text>] [--slot <text>] [--json]" }),
        new CliVerbDescriptor("Commands", "hypothesis", "hypothesis report",
            new[] { "hypothesis report --project <fwdata> --kind decisions|questions [--json]" }),
        new CliVerbDescriptor("Commands", "list", "list hypotheses",
            new[] { "list hypotheses --project <fwdata> [--id <H-n> | --status proposed|passed|failed|superseded] [--offset <n>] [--limit <n>] [--json]" }),
        new CliVerbDescriptor("Commands", "wordform", "wordform dispose",
            new[] { "wordform dispose <form>... --project <fwdata> --as name|loan|other-language|abbreviation|numeral|non-word|misspelling " +
                "--evidence <text> [--intended <form>] [--replace] [--by <name>] [--json]" }),
        new CliVerbDescriptor("Commands", "wordform", "wordform dispose --from-file",
            new[] { "wordform dispose --from-file <path> --project <fwdata> [--format tsv|jsonl|ledger|json] [--replace] [--dry-run] " +
                "[--skip-refused] [--by <name>] [--json]" }),
        new CliVerbDescriptor("Commands", "wordform", "wordform undispose",
            new[] { "wordform undispose <form>... --project <fwdata> [--reason <text>] [--by <name>] [--json]" }),
        new CliVerbDescriptor("Commands", "disposition", "disposition show",
            new[] { "disposition show <form> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "disposition", "disposition list",
            new[] { "disposition list --project <fwdata> [--as <category>] [--limit <1-200>] [--page <N>] [--format table|json] [--json]" }),
        new CliVerbDescriptor("Commands", "request", "request create",
            new[]
            {
                "request create --project <fwdata> --kind word|warning|matrix-cell --target <id> --label <text> " +
                "[--note <text>] [--json]",
            }),
        new CliVerbDescriptor("Commands", "request", "request withdraw",
            new[] { "request withdraw <requestId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "request", "request ask-again",
            new[] { "request ask-again <proposalId> --project <fwdata> [--json]" }),
        new CliVerbDescriptor("Commands", "proposal", "proposal looked-at",
            new[]
            {
                "proposal looked-at --project <fwdata> --proposal <id> --entry <key> [--content-digest <digest> " +
                "--evidence-digest <digest>] [--clear] [--json]",
            }),
        new CliVerbDescriptor("Commands", "apply", "apply --proposal",
            new[] { "apply --proposal <id> --project <fwdata> [--revision <r>] [--receipt <file>] [--autonomous] [--message <text>] [--note] [--no-background] [--effects] [--json]" }),
    }.Concat(SIL.Motif.Commands.ProposalCommands.ComposerDescriptors.Select(composer =>
        new CliVerbDescriptor("Commands", composer.Verb, composer.Verb, new[] { ComposerUsage(composer.Verb) })))
        .ToArray());

    /// <summary>The usage text every typed composer verb shares, before the citation options.</summary>
    internal static string ComposerUsage(string verb) => verb == "compose-set-entry-attribution"
        ? verb + " --draft <name> --project <fwdata> --intent '<closed intent JSON>', or --entries-file <file> --tag '<source>@<revision> <licence>' [--analysis-ws <tag>] in place of --intent"
        : verb == "compose-edit-text-notes"
        ? verb + " --draft <name> --project <fwdata> --intent '<closed intent JSON>', or --notes-tsv <file> [--analysis-ws <tag>] in place of --intent"
        : verb + " --draft <name> --project <fwdata> --intent '<closed intent JSON>'";

    private static IReadOnlyList<CliVerbDescriptor> AddCitationOptions(
        IReadOnlyList<CliVerbDescriptor> descriptors) => descriptors.Select(descriptor =>
    {
        var supportsCitation = descriptor.Verb.StartsWith("compose-", StringComparison.Ordinal) &&
                               descriptor.Verb != "compose-batch" ||
                               descriptor.Verb.StartsWith("retire-", StringComparison.Ordinal) ||
                               descriptor.Verb is "add-set-gloss" or "add-delete-lexeme-form" or "promote-gloss" or
                                   "order-allomorphs";
        if (!supportsCitation || descriptor.UsageLines.Any(line => line.Contains("--cite-source", StringComparison.Ordinal)))
            return descriptor;
        return descriptor with
        {
            UsageLines = descriptor.UsageLines.Select(line =>
                line + " [--cite-source <text> [--cite-locator <text>]]").ToArray(),
        };
    }).ToArray();
}
