using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>
/// Every tool the server can offer, each built over the catalogued commands named in
/// the command catalog. The built-in profile exposes the ones marked on by default; a
/// profile may expose more, rename, or hide. Nothing here reaches a <see cref="AgentClass.HumanOnly"/> command.
/// </summary>
internal static class AgentTools
{
    private static readonly (string, JsonObject, bool)[] NoArguments = [];

    public static IReadOnlyList<AgentTool> All { get; } = CatalogTools.Build(BuildAdapters());

    /// <summary>Every tool name, which a profile may select.</summary>
    public static IReadOnlySet<string> Names { get; } = All.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<AgentTool> BuildAdapters() =>
    [
        // Orient
        new("motif_overview",
            string.Empty,
            AgentClass.Read,
            Schema.Object(Schema.Verbosity()),
            true,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(OverviewCommand.Overview(new OverviewRequest(c.ProjectPath)),
                _ => "Read the grammar behind a failing word with motif_grammar, or look at one word with motif_word."))) { Requests = [typeof(OverviewRequest)] },

        new("motif_capture_baseline",
            string.Empty,
            AgentClass.Evaluate,
            Schema.Object(),
            false,
            true,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                BaselineCaptureCommand.Capture(new BaselineCaptureRequest(c.ProjectPath)),
                _ => "Now read with motif_grammar, motif_lexicon or motif_overview."))) { Requests = [typeof(BaselineCaptureRequest)] },

        new("motif_findings",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("kind", Schema.String("Only findings with this code."), false),
                ("left_out", Schema.Boolean("Only findings that explain words left out of the analysis."), false),
                ..Schema.Verbosity()]),
            true,
            true,
            false,
            true,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WarningsCommand.Warnings(
                new WarningsRequest(c.ProjectPath, a.Optional("kind"), a.Flag("left_out")))))) { Requests = [typeof(WarningsRequest)] },

        new("motif_check_grammar",
            string.Empty,
            AgentClass.Evaluate,
            Schema.Object(Schema.Verbosity()),
            false,
            true,
            false,
            true,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(GrammarCheckQuery.Query(new GrammarCheckRequest(c.ProjectPath), ct)))) { Requests = [typeof(GrammarCheckRequest)] },

        // Read
        new("motif_grammar",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("kind", Schema.String("What to list. Omit for counts of every kind.",
                    GrammarReader.Kinds.ToArray()), false),
                ("query", Schema.String("Only objects whose name or abbreviation contains this text."), false),
                ("offset", Schema.Integer("Items to skip, for the next page.", 0, 100000), false),
                ..Schema.Verbosity()]),
            true,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(ReadGrammar(c, a))),

        new("motif_lexicon",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("query", Schema.String("Text the headword, a gloss or a form contains."), false),
                ("morph_type", Schema.String("Only entries of this morph type, such as 'stem' or 'suffix'."), false),
                ("offset", Schema.Integer("Entries to skip, for the next page.", 0, 100000), false),
                ..Schema.Verbosity()]),
            true,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(ReadLexicon(c, a))),

        new("motif_word",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("word", Schema.String("The word, exactly as written in the project."), true),
                ..Schema.Verbosity()]),
            true,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WordContextQuery.Query(
                new WordContextRequest(c.ProjectPath, a.Required("word"))),
                _ => "To see why the parser does or does not analyze it, call motif_try_word."))) { Requests = [typeof(WordContextRequest)] },

        new("motif_texts",
            string.Empty,
            AgentClass.Read,
            Schema.Object(Schema.Verbosity()),
            true,
            true,
            false,
            true,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(TextInventoryQuery.Query(new TextInventoryRequest(c.ProjectPath))))) { Requests = [typeof(TextInventoryRequest)] },

        new("motif_writing_systems",
            string.Empty,
            AgentClass.Read,
            Schema.Object(),
            true,
            true,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WritingSystemsQuery.Query(new WritingSystemsRequest(c.ProjectPath))))) { Requests = [typeof(WritingSystemsRequest)] },

        // Draft
        new("motif_start_proposal",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("draft", Schema.String("Short draft name, for example 'plural-s'."), true),
                ("label", Schema.String("A one-line description a reviewer reads in Motif's window."), true),
                ("comment", Schema.String("Why this change is right: the evidence, in a few sentences."), true)]),
            false,
            false,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(StartProposal(c, a))) { Requests = [typeof(NewDraftRequest)] },

        new("motif_guide",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("topic", Schema.String("The guide topic to read.", EncodingGuides.Topics.ToArray()), true)]),
            true,
            true,
            true,
            false,
            false,
            (c, a, ct) => Task.FromResult(ReadGuide(a))),

        new("motif_add_lexeme_form",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("entry", Schema.String("Id of the existing entry."), true),
                ("morph_type", Schema.String("Id of the morph type of the new form."), true),
                ("ws", Schema.String("Writing system tag the form is written in."), true),
                ("text", Schema.String("The form's written text."), true),
                ("environments", Schema.StringArray("Optional environment ids restricting the new form."), false),
                ("is_abstract", Schema.Boolean("True for a root-and-pattern form with no fixed segments."), false),
                ("sense", Schema.String("Id of an existing sense of the entry to gloss. Needs gloss_ws and gloss_text."), false),
                ("gloss_ws", Schema.String("Writing system tag of the gloss."), false),
                ("gloss_text", Schema.String("The gloss text."), false)]),
            false,
            false,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorLexemeForm(
                new ComposeAuthorLexemeFormRequest(c.ProjectPath, c.ProductVersion, a.Required("draft"),
                    LexemeFormIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Check them with motif_dry_run before Finalize."))) { Requests = [typeof(ComposeAuthorLexemeFormRequest)] },

        new("motif_add_feature_structure",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("msa", Schema.String("Id of the stem's grammatical info."), true)]),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeAuthorFeatureStructure(new ComposeAuthorFeatureStructureRequest(
                    c.ProjectPath, c.ProductVersion, a.Required("draft"),
                    new JsonObject { ["msa"] = a.Required("msa") }.ToJsonString())),
                r => $"Draft now has {r.OperationCount} changes. Read motif_guide(topic=feature-conditioned-slots) before adding any feature values."))) { Requests = [typeof(ComposeAuthorFeatureStructureRequest)] },

        new("motif_add_feature_value",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(("draft", Schema.String("Draft name."), true),
                ("featStruc", Schema.String("Feature structure id."), true),
                ("feature", Schema.String("Feature id."), true), ("value", Schema.String("Optional symbolic value id."), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorFeatureValue(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("featStruc", "feature", "value"))),
                r => "Use created entityId values in later composer calls; Dry Run and Trial the Draft, then Finalize."))) { Requests = [typeof(ComposeAuthorFeatureValueRequest)] },

        new("motif_add_phoneme",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(("draft", Schema.String("Draft name."), true),
                ("name", Schema.String("Phoneme name."), true),
                ("representations", Schema.StringArray("Explicit grapheme codes, such as ['a'] or ['sh', 'š']."), true),
                ("features", Schema.FeatureValues(), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorPhoneme(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "representations", "features"))),
                r => "Use the phoneme create's entityId in motif_add_natural_class or motif_add_environment."))) { Requests = [typeof(ComposeAuthorPhonemeRequest)] },

        new("motif_add_natural_class",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(("draft", Schema.String("Draft name."), true),
                ("name", Schema.String("Natural class name."), true), ("abbreviation", Schema.String("Unique environment abbreviation, such as V."), true),
                ("members", Schema.StringArray("Phoneme ids in the first set."), false), ("features", Schema.FeatureValues(), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorNaturalClass(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "abbreviation", "members", "features"))),
                r => "Use the natural class create's entityId in motif_add_environment."))) { Requests = [typeof(ComposeAuthorNaturalClassRequest)] },

        new("motif_edit_natural_class",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing segment natural class."), true),
                ("expectedMembers", Schema.StringArray("Exact current phoneme ids in the class."), true),
                ("members", Schema.StringArray("Requested distinct phoneme ids; the class must remain nonempty."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditNaturalClass(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "expectedMembers", "members"))),
                r => $"Draft now has {r.OperationCount} member changes. Review RelatedObjects before Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditNaturalClassRequest)] },

        new("motif_relink_natural_class",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("source", Schema.String("Canonical id of the class users currently reference."), true),
                ("replacement", Schema.String("Canonical id of the replacement class."), true),
                ("replacementCreationOperation", Schema.String("Operation id returned by the class create earlier in this Draft."), true),
                ("environments", Schema.Array("Explicit environment users and their exact current typed contexts.",
                    Schema.Object(("target", Schema.String("Environment id."), true),
                        ("expectedLeft", NaturalClassRelinkContexts(), true),
                        ("expectedRight", NaturalClassRelinkContexts(), true))), true),
                ("ruleContexts", Schema.StringArray("Explicit rewrite-rule context object ids using the source class."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeRelinkNaturalClass(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("source", "replacement", "replacementCreationOperation", "environments", "ruleContexts"))),
                r => $"Draft now has {r.OperationCount} relinks. Review RelatedObjects against the declared scope; only a person can Apply."))) { Requests = [typeof(ComposeRelinkNaturalClassRequest)] },

        new("motif_add_environment",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(("draft", Schema.String("Draft name."), true),
                ("name", Schema.String("Environment name."), true), ("left", Schema.Contexts(), true), ("right", Schema.Contexts(), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorEnvironment(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "left", "right"))),
                r => "Run motif_dry_run and motif_trial on positive and negative words, revise, then Finalize."))) { Requests = [typeof(ComposeAuthorEnvironmentRequest)] },

        new("motif_add_phonological_rule",
            string.Empty,
            AgentClass.Draft,
            RuleComposerSchema(),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorPhonologicalRule(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "direction", "input",
                    "output", "left", "right", "placement"))),
                r => $"Draft now has {r.OperationCount} operations. Run motif_dry_run and motif_trial, revise, then Finalize."))) { Requests = [typeof(ComposeAuthorPhonologicalRuleRequest)] },

        new("motif_add_affix_slot",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("category", Schema.String("Canonical id of the existing category that owns the new slot."), true),
                ("name", Schema.String("Slot name in the project's writing system."), true),
                ("ws", Schema.String("Writing system tag for the slot name."), true),
                ("optional", Schema.Boolean("Whether forms may omit this slot."), true),
                ("assignments", Schema.StringArray("Optional canonical ids of existing inflectional affix MSAs to assign."), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorAffixSlot(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("category", "name", "ws", "optional", "assignments"))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeAuthorAffixSlotRequest)] },

        new("motif_add_affix_template",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("category", Schema.String("Canonical id of the existing category that owns the template."), true),
                ("name", Schema.String("Template name in the project's writing system."), true),
                ("ws", Schema.String("Writing system tag for the template name."), true),
                ("prefix_slots", Schema.StringArray("Ordered canonical ids of prefix slots."), true),
                ("suffix_slots", Schema.StringArray("Ordered canonical ids of suffix slots."), true),
                ("final", Schema.Boolean("Whether the template requires further derivation."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorAffixTemplate(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), AuthorAffixTemplateIntent(a))),
                r => $"Draft now has {r.OperationCount} operations. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeAuthorAffixTemplateRequest)] },

        new("motif_retire_allomorph",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("intent_json", Schema.String("Closed motif-retire-allomorph/v1 JSON with exact source, destination and reference identities."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeRetireAllomorph(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Required("intent_json"))),
                r => $"Draft now has {r.OperationCount} operations. Run motif_dry_run and motif_trial, revise, then Finalize."))) { Requests = [typeof(ComposeRetireAllomorphRequest)] },
        new("motif_retire_redundant_zero_affix",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("intent_json", Schema.String("Closed motif-retire-redundant-zero-affix/v1 JSON naming one canonical entry id."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeRetireRedundantZeroAffix(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Required("intent_json"))),
                r => $"Draft now has {r.OperationCount} operations. Run motif_dry_run and motif_trial, revise, then Finalize for a person to Apply."))) { Requests = [typeof(ComposeRetireRedundantZeroAffixRequest)] },
        new("motif_edit_adhoc_prohibition",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of one flat allomorph or morpheme prohibition."), true),
                ("expected_disabled", Schema.Boolean("The value the target must currently have."), true),
                ("disabled", Schema.Boolean("The explicit Disabled value to write."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeEditAdhocProhibition(new(c.ProjectPath, c.ProductVersion,
                    a.Required("draft"), EditAdhocProhibitionIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditAdhocProhibitionRequest)] },

        new("motif_edit_affix_slot",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing affix slot."), true),
                ("expected_optional", Schema.Boolean("The optionality the project must currently have."), true),
                ("optional", Schema.Boolean("The requested optionality."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAffixSlot(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), EditAffixSlotIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditAffixSlotRequest)] },
        new("motif_edit_affix_template",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing affix template."), true),
                ("expected_prefix_slots", Schema.StringArray("Current prefix slot ids in project order."), true),
                ("prefix_slots", Schema.StringArray("Requested prefix slot ids in explicit order."), true),
                ("expected_suffix_slots", Schema.StringArray("Current suffix slot ids in project order."), true),
                ("suffix_slots", Schema.StringArray("Requested suffix slot ids in explicit order."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAffixTemplate(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), EditAffixTemplateIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditAffixTemplateRequest)] },
        new("motif_edit_inflectional_affix",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing inflectional affix MSA."), true),
                ("expected_slots", Schema.StringArray("Current slot ids assigned to the MSA."), true),
                ("slots", Schema.StringArray("Requested slot ids; order has no meaning."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditInflectionalAffix(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), EditInflectionalAffixIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditInflectionalAffixRequest)] },
        new("motif_edit_allomorph_condition",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing stem or affix allomorph."), true),
                ("field", Schema.String("LibLCM condition field.", "phoneEnv", "position"), true),
                ("expected_environments", Schema.StringArray("Current environment ids; order matters only for position."), true),
                ("environments", Schema.StringArray("Requested environment ids; order matters only for position."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAllomorphCondition(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "field", "expected_environments", "environments"))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditAllomorphConditionRequest)] },
        new("motif_order_allomorphs",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the owning lexical entry."), true),
                ("expected_alternates", Schema.StringArray("Current alternate-form ids in project order."), true),
                ("alternates", Schema.StringArray("Requested order of those same alternate forms."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeOrderAllomorphs(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "expected_alternates", "alternates"))),
                r => $"Draft now has {r.OperationCount} moves. Run motif_dry_run and motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeOrderAllomorphsRequest)] },
        new("motif_record_parsimony_disposition",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("recordTypeId", Schema.String("Id of an existing possibility in the Notebook record type list."), true),
                ("measureId", Schema.String("The registered Parsimony measure id."), true),
                ("subject", JudgmentSubject(), true),
                ("disposition", Schema.String("The proposed decision.", "fix", "keep", "ask", "defer"), true),
                ("evidenceDigest", Schema.String("Canonical sha256 digest from the exact finding."), true),
                ("evidenceContract", Schema.String("Versioned evidence contract for the finding."), true),
                ("subjectCaption", Schema.String("Readable name for the exact finding subject."), true),
                ("measureCaption", Schema.String("Readable name for the Parsimony measure."), true),
                ("reason", Schema.String("Optional explanation, stored with the readable record."), false),
                ("question", Schema.String("Required only when disposition is ask."), false),
                ("reportId", Schema.String("Optional source Parsimony Report id."), false),
            ]),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeRecordParsimonyDisposition(new(c.ProjectPath, c.ProductVersion,
                    a.Required("draft"), a.IntentJson("recordTypeId", "measureId", "subject", "disposition",
                        "evidenceDigest", "evidenceContract", "subjectCaption", "measureCaption", "reason",
                        "question", "reportId"))),
                r => $"Draft now has {r.OperationCount} changes. A pending keep does not suppress findings; only a person can Apply."))) { Requests = [typeof(ComposeRecordParsimonyDispositionRequest)] },

        new("motif_parsimony_record_types",
            string.Empty,
            AgentClass.Read,
            Schema.Object(),
            true,
            true,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.ListNotebookRecordTypes(
                new(c.ProjectPath, c.ProductVersion))))) { Requests = [typeof(ListNotebookRecordTypesRequest)] },

        new("motif_dispose_parsimony_finding",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([
                ("report_id", Schema.String("Id of a stored Parsimony Report from this project."), true),
                ("finding_id", Schema.String("Exact finding id returned by that Report."), true),
                ("disposition", Schema.String("The proposed decision.", "fix", "keep", "ask", "defer"), true),
                ("record_type_id", Schema.String("Portable id from motif_parsimony_record_types."), true),
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("reason", Schema.String("Optional explanation, stored with the readable record."), false),
                ("question", Schema.String("Required only when disposition is ask."), false),
            ]),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.RecordDisposition(
                new(c.ProjectPath, c.ProductVersion, a.Required("report_id"), a.Required("finding_id"),
                    a.Required("disposition"), a.Required("record_type_id"), a.Required("draft"),
                    a.Optional("reason"), a.Optional("question"))),
                r => $"Draft now has {r.OperationCount} operations. Run motif_dry_run on the Draft, revise, then Finalize."))) { Requests = [typeof(RecordParsimonyDispositionFromFindingRequest)] },

        new("motif_set_gloss",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("sense", Schema.String("Id of the sense."), true),
                ("ws", Schema.String("Writing system tag of the gloss."), true),
                ("text", Schema.String("The new gloss."), true)]),
            false,
            true,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Required("sense"), a.Required("ws"),
                a.Required("text")))))) { Requests = [typeof(AddSetGlossRequest)] },

        new("motif_remove_operations",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("draft", Schema.String("The draft name."), true),
                ("operation_ids", Schema.StringArray("Operation ids to remove."), true)]),
            false,
            true,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.List("operation_ids"), Force: false))))) { Requests = [typeof(RemoveOperationsRequest)] },

        // Evaluate
        new("motif_dry_run",
            string.Empty,
            AgentClass.Evaluate,
            Schema.Object([("proposal", Schema.String("The proposal id."), true),
                ("job", Schema.String("A job id from an earlier 'running' result, to keep waiting for it."), false),
                ("wait_seconds", Schema.Integer("How long to wait for the result. Default 40.", 1, 300), false),
                ..Schema.Verbosity()]),
            false,
            false,
            true,
            true,
            false,
            DryRun) { Requests = [typeof(EnqueueDryRunRequest), typeof(WaitForDryRunRequest)] },

        new("motif_trial",
            string.Empty,
            AgentClass.Evaluate,
            Schema.Object([("proposal", Schema.String("The proposal id."), true),
                ("words", Schema.StringArray("Words to parse. Omit to use the saved Default Selection."), false),
                ("negative_words", Schema.StringArray("Attested counterexamples that should not parse."), false),
                ("job", Schema.String("A job id from an earlier 'running' result, to keep waiting for it."), false),
                ("wait_seconds", Schema.Integer("How long to wait for the result. Default 40.", 1, 300), false),
                ..Schema.Verbosity()]),
            false,
            false,
            true,
            true,
            false,
            Trial) { Requests = [typeof(EnqueueTrialRequest), typeof(WaitForJobRequest)] },

        new("motif_try_word",
            string.Empty,
            AgentClass.Evaluate,
            Schema.Object([("word", Schema.String("The word to parse."), true), ..Schema.Verbosity()]),
            false,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WordTraceQuery.Query(
                new WordTraceRequest(c.ProjectPath, a.Required("word")), ct)))) { Requests = [typeof(WordTraceRequest)] },

        new("motif_job",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("job", Schema.String("The job id."), true)]),
            true,
            true,
            false,
            false,
            false,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(JobCommands.Show(
                new ShowJobRequest(c.ProjectPath, a.Required("job"), c.ProductVersion))))) { Requests = [typeof(ShowJobRequest)] },

        // Hand off
        new("motif_finalize_proposal",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("draft", Schema.String("The draft name."), true),
                ("expected_revision", Schema.String("The revision you last read, to refuse if the draft changed."), false)]),
            false,
            false,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.Finalize(new FinalizeRequest(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Optional("expected_revision"))),
                r => $"Proposal {r.ProposalId} is ready for the linguist to review in Motif. Tell them its label."))) { Requests = [typeof(FinalizeRequest)] },

        new("motif_proposals",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("proposal", Schema.String("A proposal id to show in full. Omit to list."), false),
                ..Schema.Verbosity()]),
            true,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(a.Optional("proposal") is { } id
                ? ToolOutcome.From(ProposalCommands.Show(new ShowProposalRequest(c.ProjectPath, c.ProductVersion, id)))
                : ToolOutcome.From(ProposalCommands.List(new ListProposalsRequest(c.ProjectPath, c.ProductVersion))))) { Requests = [typeof(ListProposalsRequest), typeof(ShowProposalRequest)] },

        new("motif_activity",
            string.Empty,
            AgentClass.Read,
            Schema.Object(),
            true,
            true,
            true,
            false,
            false,
            (c, a, ct) => Task.FromResult(Activity(c))),
        new("motif_list_projects", string.Empty, AgentClass.Read, Schema.Object(), true, true, true, false, false,
            (c, a, ct) => Task.FromResult(ToolOutcome.Ok(System.Text.Json.JsonSerializer.SerializeToNode(
                KnownProjectsQuery.List(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!)))
            { NeedsProject = false },
        new("motif_assess", string.Empty, AgentClass.Evaluate,
            Schema.Object([("words", Schema.StringArray("Words to assess; omit for the Default Selection."), false),
                ..Schema.Verbosity()]), false, false, true, true, true,
            (c, a, ct) => Task.Run(() => ToolOutcome.From(TrialResults.Assess(c, a.List("words"), ct)), ct))
            { Requests = [typeof(AssessRequest)] },
        new("motif_difference", string.Empty, AgentClass.Read,
            Schema.Object(("difference", Schema.String("Stored Difference Assessment id."), true),
                ("category", Schema.String("Category to page through.", TrialResults.Categories), true),
                ("offset", Schema.Integer("Rows to skip.", 0, 1000000), false),
                ("limit", Schema.Integer("Page size, maximum 200.", 1, 200), false)), true, true, true, false, true,
            (c, a, ct) => Task.FromResult(TrialResults.Read(c, a))),
        ..ParsimonyTools.All,
    ];

    private static JsonObject RuleComposerSchema()
    {
        var item = Schema.Object(("phoneme", Schema.String("Phoneme id in the first phoneme set."), false),
            ("naturalClass", Schema.String("Natural class id owned by this project's phonological data."), false));
        var changedItems = Schema.Array("Zero or one explicit phoneme or natural-class items.", item);
        changedItems["maxItems"] = 1;
        var contexts = Schema.Contexts();
        contexts["maxItems"] = 8;
        return Schema.Object(("draft", Schema.String("Draft name."), true),
            ("name", Schema.String("Rule name."), true),
            ("direction", Schema.String("Rule scan direction or application mode.",
                "left-to-right", "right-to-left", "simultaneous"), true),
            ("input", changedItems.DeepClone().AsObject(), true),
            ("output", changedItems.DeepClone().AsObject(), true),
            ("left", contexts.DeepClone().AsObject(), true),
            ("right", contexts.DeepClone().AsObject(), true),
            ("placement", Schema.Object(("after", Schema.String("Adjacent existing rule before the insertion gap."), false),
                ("before", Schema.String("Adjacent existing rule after the insertion gap."), false)), false));
    }

    private static JsonObject NaturalClassRelinkContexts()
    {
        var contexts = Schema.Contexts();
        var properties = contexts["items"]!.AsObject()["properties"]!.AsObject();
        properties["boundaryMarker"] = Schema.String("Declared morpheme-boundary marker id.");
        return contexts;
    }

    private static ToolOutcome StartProposal(ServerContext context, ToolArgs args)
    {
        var draft = args.Required("draft");
        var created = ProposalCommands.New(new NewDraftRequest(
            context.ProjectPath, context.ProductVersion, draft, args.Required("label")));
        if (!created.Succeeded) return ToolOutcome.Refused(created.Refusal!);
        var commented = ProposalCommands.Comment(new CommentRequest(
            context.ProjectPath, context.ProductVersion, draft, args.Required("comment")));
        if (!commented.Succeeded) return ToolOutcome.Refused(commented.Refusal!);
        return ToolOutcome.From(created, r =>
            $"Add one related change to draft '{r.DraftName}'. Read motif_guide(topic=workflow) for the inspect-to-Trial steps.");
    }

    private static ToolOutcome ReadGuide(ToolArgs args)
    {
        var topic = args.Required("topic");
        if (!EncodingGuides.Topics.Contains(topic, StringComparer.Ordinal))
            return ToolOutcome.Refused("guide.unknown-topic", FailureReason.InvalidArgument,
                $"There is no guide topic '{topic}'. Topics are: {string.Join(", ", EncodingGuides.Topics)}.");
        return ToolOutcome.Ok(new JsonObject
        {
            ["topic"] = topic,
            ["uri"] = EncodingGuides.UriPrefix + topic,
            ["guide"] = EncodingGuides.ReadTopic(topic),
        }, "Follow the guide's Trial check and ask the linguist if its evidence leaves a choice open.");
    }

    private static ToolOutcome ReadGrammar(ServerContext context, ToolArgs args)
    {
        var kind = args.Optional("kind");
        if (kind is not null && !GrammarReader.Kinds.Contains(kind))
            return ToolOutcome.Refused("grammar.unknown-kind", FailureReason.InvalidArgument,
                $"'{kind}' is not a grammar kind. Kinds are: {string.Join(", ", GrammarReader.Kinds)}.");
        var detailed = args.Optional("detail") == "detailed";
        var offset = args.Int("offset", 0);
        var limit = args.Int("limit", 25);
        var outcome = BaselineReads.Read(context.ProjectPath, cache => kind is null
            ? GrammarReader.Summary(cache) : Page(GrammarReader.Read(cache, kind, args.Optional("query"), detailed), offset, limit));
        return ToolOutcome.From(outcome, kind is null
            ? "Pick a kind and call motif_grammar again to list it, or motif_lexicon for stems and affixes."
            : "Name these ids in changes. Page on with offset if total exceeds what was returned.");
    }

    private static JsonObject Page(JsonObject result, int offset, int limit)
    {
        var items = (JsonArray)result["items"]!;
        var page = items.Skip(offset).Take(limit).Select(item => item!.DeepClone()).ToArray();
        result["offset"] = offset;
        result["returned"] = page.Length;
        result["items"] = new JsonArray(page);
        return result;
    }

    private static ToolOutcome ReadLexicon(ServerContext context, ToolArgs args)
    {
        var detailed = args.Optional("detail") == "detailed";
        var outcome = BaselineReads.Read(context.ProjectPath, cache => LexiconReader.Read(cache, args.Optional("query"),
            args.Optional("morph_type"), args.Int("limit", 25), args.Int("offset", 0), detailed));
        return ToolOutcome.From(outcome, "Use the entry, sense and grammatical info ids in changes. Page on with offset.");
    }

    private static string LexemeFormIntent(ToolArgs args)
    {
        var intent = new JsonObject
        {
            ["entry"] = args.Required("entry"),
            ["morphType"] = args.Required("morph_type"),
            ["ws"] = args.Required("ws"),
            ["text"] = args.Required("text"),
        };
        if (args.List("environments").Count > 0) intent["environments"] = new JsonArray(args.List("environments").Select(e => (JsonNode)e).ToArray());
        if (args.Flag("is_abstract")) intent["isAbstract"] = true;
        if (args.Optional("sense") is { } sense)
        {
            intent["sense"] = sense;
            intent["glossWs"] = args.Optional("gloss_ws");
            intent["glossText"] = args.Optional("gloss_text");
        }
        return intent.ToJsonString();
    }

    private static string EditAdhocProhibitionIntent(ToolArgs args) => new JsonObject
    {
        ["target"] = args.Required("target"),
        ["expectedDisabled"] = args.RequiredBoolean("expected_disabled"),
        ["disabled"] = args.RequiredBoolean("disabled"),
    }.ToJsonString();

    private static string EditAffixSlotIntent(ToolArgs args) => new JsonObject
    {
        ["target"] = args.Required("target"),
        ["expectedOptional"] = args.RequiredBoolean("expected_optional"),
        ["optional"] = args.RequiredBoolean("optional"),
    }.ToJsonString();

    private static string EditAffixTemplateIntent(ToolArgs args) => new JsonObject
    {
        ["target"] = args.Required("target"),
        ["expectedPrefixSlots"] = new JsonArray(args.List("expected_prefix_slots").Select(value => (JsonNode)value).ToArray()),
        ["prefixSlots"] = new JsonArray(args.List("prefix_slots").Select(value => (JsonNode)value).ToArray()),
        ["expectedSuffixSlots"] = new JsonArray(args.List("expected_suffix_slots").Select(value => (JsonNode)value).ToArray()),
        ["suffixSlots"] = new JsonArray(args.List("suffix_slots").Select(value => (JsonNode)value).ToArray()),
    }.ToJsonString();

    private static string AuthorAffixTemplateIntent(ToolArgs args) => new JsonObject
    {
        ["category"] = args.Required("category"),
        ["name"] = args.Required("name"),
        ["ws"] = args.Required("ws"),
        ["prefixSlots"] = new JsonArray(args.RequiredList("prefix_slots").Select(value => (JsonNode)value).ToArray()),
        ["suffixSlots"] = new JsonArray(args.RequiredList("suffix_slots").Select(value => (JsonNode)value).ToArray()),
        ["final"] = args.RequiredBoolean("final"),
    }.ToJsonString();

    private static string EditInflectionalAffixIntent(ToolArgs args) => new JsonObject
    {
        ["target"] = args.Required("target"),
        ["expectedSlots"] = new JsonArray(args.List("expected_slots").Select(value => (JsonNode)value).ToArray()),
        ["slots"] = new JsonArray(args.List("slots").Select(value => (JsonNode)value).ToArray()),
    }.ToJsonString();

    private static async Task<ToolOutcome> DryRun(ServerContext context, ToolArgs args, CancellationToken cancellation)
    {
        var proposal = args.Required("proposal");
        var wait = TimeSpan.FromSeconds(Math.Clamp(args.Int("wait_seconds", 40), 1, 300));
        var job = args.Optional("job");
        if (job is null)
        {
            var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(context.ProjectPath, context.ProductVersion, proposal));
            if (!queued.Succeeded) return ToolOutcome.Refused(queued.Refusal!);
            job = queued.Value!.JobId;
            context.StartRunner();
        }
        var waited = await Task.Run(() => JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            context.ProjectPath, context.ProductVersion, proposal, job, wait), cancellation), cancellation);
        if (!waited.Succeeded) return StillRunning(waited.Refusal!, job) ?? ToolOutcome.Refused(waited.Refusal!);
        var result = JsonNode.Parse(ProjectionJson.Serialize(waited.Value!))!.AsObject();
        result["job"] = job;
        return ToolOutcome.Ok(result, "If the changes fit, run motif_trial on sample words; if not, fix the draft first.", job);
    }

    private static async Task<ToolOutcome> Trial(ServerContext context, ToolArgs args, CancellationToken cancellation)
    {
        var proposal = args.Required("proposal");
        var wait = TimeSpan.FromSeconds(Math.Clamp(args.Int("wait_seconds", 40), 1, 300));
        var job = args.Optional("job");
        if (job is null)
        {
            var negatives = args.List("negative_words");
            var words = args.List("words").Concat(negatives).Distinct(StringComparer.Ordinal).ToArray();
            var before = await Task.Run(() => TrialResults.Assess(context, words, cancellation), cancellation);
            if (!before.Succeeded) return ToolOutcome.Refused(before.Refusal!);
            var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(context.ProjectPath, context.ProductVersion,
                proposal, Words: before.Value!.Selection.Words,
                BaselineAssessmentIds: before.Value!.AssessmentIds, NegativeWords: negatives));
            if (!queued.Succeeded) return ToolOutcome.Refused(queued.Refusal!);
            job = queued.Value!.JobId;
            context.StartRunner();
        }
        var waited = await Task.Run(() => JobCommands.WaitForJob(
            new WaitForJobRequest(context.ProjectPath, job, context.ProductVersion, wait)), cancellation);
        if (!waited.Succeeded) return StillRunning(waited.Refusal!, job) ?? ToolOutcome.Refused(waited.Refusal!);
        return TrialResults.Complete(context, job, proposal) with { JobId = job };
    }

    // A wait that ran out is progress, not a failure: say where the job is and how to keep waiting.
    private static ToolOutcome? StillRunning(Refusal refusal, string job) =>
        refusal.Code is "job.wait-timeout" or "job.wait-cancelled"
            ? ToolOutcome.Ok(new JsonObject
            {
                ["status"] = "running",
                ["job"] = job,
                ["detail"] = refusal.Message,
            }, "Call this tool again with job set to " + job + " to keep waiting.", job)
            : null;

    private static ToolOutcome Activity(ServerContext context)
    {
        var entries = context.Activity.Entries;
        var proposals = ProposalCommands.List(new ListProposalsRequest(context.ProjectPath, context.ProductVersion));
        var jobs = new JsonArray();
        foreach (var jobId in entries.Select(entry => entry.JobId).Where(id => id is not null).Distinct().TakeLast(5))
        {
            var shown = JobCommands.Show(new ShowJobRequest(context.ProjectPath, jobId!, context.ProductVersion));
            jobs.Add(shown.Succeeded
                ? new JsonObject { ["job"] = jobId, ["kind"] = shown.Value!.Kind, ["status"] = shown.Value.Status?.ToString() }
                : new JsonObject { ["job"] = jobId, ["status"] = "unreadable" });
        }
        return ToolOutcome.Ok(new JsonObject
        {
            ["profile"] = context.Profile.Name,
            ["calls"] = new JsonObject
            {
                ["total"] = entries.Count,
                ["errors"] = entries.Count(entry => entry.IsError),
                ["byTool"] = new JsonObject(entries.GroupBy(entry => entry.Tool).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new KeyValuePair<string, JsonNode?>(group.Key, group.Count()))),
            },
            ["recent"] = new JsonArray(entries.TakeLast(10).Select(entry => (JsonNode)new JsonObject
            {
                ["tool"] = entry.Tool,
                ["isError"] = entry.IsError,
                ["code"] = entry.Code,
            }).ToArray()),
            ["proposals"] = proposals.Succeeded ? JsonNode.Parse(ProjectionJson.Serialize(proposals.Value!)) : null,
            ["jobs"] = jobs,
        }, "Continue from the Proposals above, or start a new one with motif_start_proposal.");
    }

    private static JsonObject JudgmentSubject() => new()
    {
        ["description"] = "One closed project, object, relationship or computed-group identity.",
        ["oneOf"] = new JsonArray
        {
            Schema.Object(("kind", Schema.String("Subject discriminator.", "object"), true),
                ("object", JudgmentObject(), true)),
            Schema.Object(("kind", Schema.String("Subject discriminator.", "project"), true),
                ("id", Schema.String("Project identity."), true)),
            Schema.Object(("kind", Schema.String("Subject discriminator.", "edge"), true),
                ("role", Schema.String("Directed relationship role.", "membership", "precedence", "rhs", "left-context", "right-context", "constraint-use"), true),
                ("owner", JudgmentObject(), true), ("from", JudgmentObject(), true), ("to", JudgmentObject(), true)),
            Schema.Object(("kind", Schema.String("Subject discriminator.", "computed-group"), true),
                ("measureId", Schema.String("Measure defining the group."), true),
                ("role", Schema.String("Computed group role.", "alternation-family", "duplicate-pair", "adhoc-cluster", "adhoc-slot-order"), true),
                ("members", Schema.Array("Exact typed group members.", JudgmentObject()), true)),
        },
    };

    private static JsonObject JudgmentObject() => Schema.Object(
        ("class", Schema.String("Exact FieldWorks model class."), true),
        ("id", Schema.String("Portable identity of the model object."), true),
        ("owningEntryId", Schema.String("Owning entry identity, required for an MSA."), false));
}
