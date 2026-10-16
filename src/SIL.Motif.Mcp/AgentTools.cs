using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Mcp;

/// <summary>
/// Every tool the server can offer, each built over the catalogued commands named in
/// the command catalog. The built-in profile exposes the ones marked on by default; a
/// profile may expose more, rename, or hide. Nothing here reaches a <see cref="AgentClass.HumanOnly"/> command.
/// </summary>
internal static class AgentTools
{
    private static readonly (string, JsonObject, bool)[] NoArguments = [];
    private static readonly HashSet<string> DefaultAuthoringTools = new(StringComparer.Ordinal)
    {
        "motif_add_lexeme_form",
        "motif_add_feature_structure",
        "motif_add_phoneme",
        "motif_add_natural_class",
        "motif_add_environment",
        "motif_add_phonological_rule",
        "motif_add_part_of_speech",
        "motif_add_lexical_entry",
        "motif_add_alternate_form",
        "motif_add_inflection_feature",
        "motif_add_inflection_class",
        "motif_add_stem_name",
        "motif_add_affix_slot",
        "motif_add_affix_template",
        "motif_compose_batch",
    };

    private static readonly string[] BatchComposerVerbs =
    [
        "compose-author-lexeme-form",
        "compose-author-feature-structure",
        "compose-author-phoneme",
        "compose-author-natural-class",
        "compose-edit-natural-class",
        "compose-relink-natural-class",
        "compose-author-environment",
        "compose-author-phonological-rule",
        "compose-author-part-of-speech",
        "compose-author-inflection-feature",
        "compose-author-inflection-class",
        "compose-author-stem-name",
        "compose-author-lexical-entry",
        "compose-author-alternate-form",
        "compose-author-affix-slot",
        "compose-author-affix-template",
        "compose-edit-adhoc-prohibition",
        "compose-edit-affix-slot",
        "compose-edit-affix-template",
        "compose-edit-inflectional-affix",
        "compose-edit-allomorph-condition",
        "order-allomorphs",
        "compose-author-text",
        "compose-edit-analysis-decision",
    ];

    public static IReadOnlyList<AgentTool> All { get; } = BuildAll();

    private static IReadOnlyList<AgentTool> BuildAll()
    {
        var tools = CatalogTools.Build(BuildAdapters()).Concat(ComposerTools.Build()).ToArray();
        var batchSchema = ComposeBatchSchema(tools);
        return tools.Select(tool => tool.Name == "motif_compose_batch"
                ? tool with { InputSchema = batchSchema }
                : tool)
            .Select(tool => DefaultAuthoringTools.Contains(tool.Name) ? tool with { OnByDefault = true } : tool)
            .Select(tool => tool.Name == FindComposerTool ? FindComposerOptIn(tool) : tool)
            .ToArray();
    }

    private const string FindComposerTool = "motif_find_composer";

    // Composer search is additive: a profile selects it by name until the older tools retire.
    private static AgentTool FindComposerOptIn(AgentTool tool)
    {
        var schema = tool.InputSchema.DeepClone().AsObject();
        schema["properties"]!.AsObject()["detail"] = Schema.String(
            "concise (default) or detailed, which adds a worked example and the Subject page.", "concise", "detailed");
        return tool with { OnByDefault = false, InputSchema = schema };
    }

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
                ("msa", Schema.String("Id of a stem or inflectional affix MSA with no existing feature structure."), true)]),
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
                r => "Use a createdObjects[].id from this response in motif_add_natural_class or motif_add_environment."))) { Requests = [typeof(ComposeAuthorPhonemeRequest)] },

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
                r => "Use a createdObjects[].id from this response in motif_add_environment."))) { Requests = [typeof(ComposeAuthorNaturalClassRequest)] },

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

        new("motif_add_inflection_feature",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The Draft name from motif_start_proposal."), true),
                ("name", Schema.String("Name of the closed morphosyntactic feature."), true),
                ("ws", Schema.String("Writing system tag for the feature and value names."), true),
                ("abbreviation", Schema.String("Optional short feature label."), false),
                ("values", Schema.Array("Nonempty named choices for this feature.", Schema.Object(
                    ("name", Schema.String("Value name; distinct within this feature."), true),
                    ("abbreviation", Schema.String("Optional short value label."), false))), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorInflectionFeature(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "ws", "abbreviation", "values"))),
                r => "Use the feature and value createdObjects[].id values in later composer calls; Dry Run and Trial before Finalize.")))
            { Requests = [typeof(ComposeAuthorInflectionFeatureRequest)] },

        new("motif_add_inflection_class",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The Draft name from motif_start_proposal."), true),
                ("category", Schema.String("Canonical id of the category owning this class tree."), true),
                ("name", Schema.String("Inflection class name, such as a noun class."), true),
                ("ws", Schema.String("Writing system tag for the name and abbreviation."), true),
                ("abbreviation", Schema.String("Optional short class label."), false),
                ("parent", Schema.String("Optional existing parent class in the declared category."), false)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorInflectionClass(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("category", "name", "ws", "abbreviation", "parent"))),
                r => "Use a createdObjects[].id from this response when authoring a stem; Dry Run and Trial before Finalize.")))
            { Requests = [typeof(ComposeAuthorInflectionClassRequest)] },

        new("motif_add_stem_name",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The Draft name from motif_start_proposal."), true),
                ("category", Schema.String("Canonical id of the category owning this stem name."), true),
                ("name", Schema.String("Stem name label."), true),
                ("ws", Schema.String("Writing system tag for the name and abbreviation."), true),
                ("abbreviation", Schema.String("Optional short stem name label."), false),
                ("regions", Schema.Array("Nonempty feature regions permitting the named stem.",
                    Schema.Array("Nonempty choices; one value for each feature.", Schema.Object(
                        ("feature", Schema.String("Closed feature id in the morphosyntactic system."), true),
                        ("value", Schema.String("Value id belonging to that feature."), true)))), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorStemName(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("category", "name", "ws", "abbreviation", "regions"))),
                r => "Attaching the created stem name to an allomorph is separate; Dry Run and Trial before Finalize.")))
            { Requests = [typeof(ComposeAuthorStemNameRequest)] },

        new("motif_add_lexical_entry",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("form", Schema.String("The lexeme form, without affix hyphens."), true),
                ("formWs", Schema.String("Vernacular writing system tag of the form."), true),
                ("morphType", Schema.String("Canonical id of the morph type, such as stem, root, prefix or suffix."), true),
                ("gloss", Schema.String("The sense's gloss."), true),
                ("analysisWs", Schema.String("Analysis writing system tag of the gloss and definition."), true),
                ("definition", Schema.String("Optional definition of the sense."), false),
                ("inflectionClasses", Schema.StringArray("Affix only: canonical ids of the inflection classes of the stems this form attaches to; omit for no restriction."), false),
                ("environments", Schema.StringArray("Canonical ids of the phonological environments this form occurs in; omit for any."), false),
                ("msa", Schema.Object(
                    ("kind", Schema.String("Grammatical info (MSA) kind; must match the morph type.",
                        "stem", "inflectional", "derivational", "unclassified"), true),
                    ("category", Schema.String("Canonical id of the part of speech; for a derivational affix, the one it attaches to."), true),
                    ("toCategory", Schema.String("Derivational only: canonical id of the part of speech it makes."), false),
                    ("slots", Schema.StringArray("Inflectional only: canonical ids of existing slots the affix fills."), false),
                    ("inflectionClass", Schema.String("Stem only: canonical id of its inflection class, such as a noun class."), false)), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorLexicalEntry(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("form", "formWs", "morphType", "gloss", "analysisWs", "definition", "msa", "inflectionClasses",
                    "environments"))),
                r => $"Draft now has {r.OperationCount} changes. Dry Run, try the new word with motif_try_word, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeAuthorLexicalEntryRequest)] },

        new("motif_add_alternate_form",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("entry", Schema.String("Canonical id of an existing entry, or one added earlier in this Draft."), true),
                ("form", Schema.String("The alternate form, without affix hyphens."), true),
                ("formWs", Schema.String("Vernacular writing system tag of the form."), true),
                ("morphType", Schema.String("Canonical id of the morph type; stem-like for a stem entry, affix-like for an affix entry."), true),
                ("environments", Schema.StringArray("Canonical ids of the phonological environments this form occurs in; omit for any."), false),
                ("inflectionClasses", Schema.StringArray("Affix entries only: canonical ids of the inflection classes of the stems this form attaches to; omit for no restriction."), false),
                ("placement", Schema.Object(
                    ("after", Schema.String("Existing alternate form before the insertion gap."), false),
                    ("before", Schema.String("Existing alternate form after the insertion gap."), false)), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorAlternateForm(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("entry", "form", "formWs", "morphType", "environments", "inflectionClasses", "placement"))),
                r => $"Draft now has {r.OperationCount} changes. Dry Run, try the affected words with motif_try_word, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeAuthorAlternateFormRequest)] },

        new("motif_add_part_of_speech",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("name", Schema.String("Category name, such as noun; unique among the project's categories."), true),
                ("ws", Schema.String("Writing system tag for the name and abbreviation."), true),
                ("abbreviation", Schema.String("Optional short label, such as n."), false),
                ("parent", Schema.String("Optional canonical id of an existing category to place this one under."), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorPartOfSpeech(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("name", "ws", "abbreviation", "parent"))),
                r => $"Draft now has {r.OperationCount} changes. Use a createdObjects[].id as the category of new entries, slots and affixes; Dry Run, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeAuthorPartOfSpeechRequest)] },

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

        new("motif_compose_batch",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(),
            false,
            false,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ComposeBatch(c, a)))
            { Requests = [typeof(ComposeBatchRequest)] },

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
                ("expectedEnvironments", Schema.StringArray("Current environment ids; order matters only for position."), true),
                ("environments", Schema.StringArray("Requested environment ids; order matters only for position."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAllomorphCondition(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "field", "expectedEnvironments", "environments"))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run and a bounded motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditAllomorphConditionRequest)] },
        new("motif_order_allomorphs",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the owning lexical entry."), true),
                ("expectedAlternates", Schema.StringArray("Current alternate-form ids in project order."), true),
                ("alternates", Schema.StringArray("Requested order of those same alternate forms."), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeOrderAllomorphs(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "expectedAlternates", "alternates"))),
                r => $"Draft now has {r.OperationCount} moves. Run motif_dry_run and motif_trial, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeOrderAllomorphsRequest)] },
        new("motif_add_text",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("title", Schema.String("The Text's title, in NFD."), true),
                ("ws", Schema.String("Writing system tag of the title, the paragraphs and every wordform form."), true),
                ("paragraphs", Schema.Array("Paragraphs in order, each a list of segments of word tokens.",
                    Schema.Object(("segments", Schema.Array("Segments in order; each is the NFD word tokens of one segment.",
                        Schema.StringArray("Word tokens, each one exact NFD form without spaces.")), true))), true)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorText(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("title", "ws", "paragraphs"))),
                r => $"Draft now has {r.OperationCount} changes. Created ids are listed in createdObjects. Run motif_dry_run, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeAuthorTextRequest)] },
        new("motif_decide_analysis",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("decision", Schema.String("The decision to record.", "approve", "disapprove", "unknown", "misspelled", "add-reading"), true),
                ("analysis", Schema.String("Id of a stored analysis; required for approve, disapprove and unknown."), false),
                ("wordform", Schema.String("Id of a wordform; required for misspelled and add-reading."), false),
                ("morphs", Schema.Array("Ordered morphs of the parser reading to store as unknown; required for add-reading.",
                    Schema.Object(
                        ("form", Schema.String("Id of the allomorph."), false),
                        ("msa", Schema.String("Id of the morphosyntactic analysis."), false),
                        ("inflType", Schema.String("Id of the inflectional type."), false),
                        ("guessedString", Schema.String("Surface text of a guessed morph with no allomorph."), false))), false)),
            false,
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAnalysisDecision(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("decision", "analysis", "wordform", "morphs"))),
                r => $"Draft now has {r.OperationCount} changes. Run motif_dry_run, revise, then Finalize; only a person can Apply."))) { Requests = [typeof(ComposeEditAnalysisDecisionRequest)] },
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
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.RequiredList("operation_ids"), Force: false))))) { Requests = [typeof(RemoveOperationsRequest)] },

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

        new("motif_withdraw_proposal",
            string.Empty,
            AgentClass.Draft,
            Schema.Object([("proposal", Schema.String("The id of the Proposal you started and now take back."), true)]),
            false,
            false,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalLifecycleCommand.Withdraw(
                new WithdrawProposalRequest(c.ProjectPath, a.Required("proposal"))),
                r => $"Proposal {r.ProposalId} is withdrawn. The linguist sees it as withdrawn and may reopen it."))) { Requests = [typeof(WithdrawProposalRequest)] },

        new("motif_proposals",
            string.Empty,
            AgentClass.Read,
            Schema.Object([("proposal", Schema.String("A Draft name or Proposal id to show in full. Omit to list."), false),
                ("include_set_aside", Schema.Boolean("Also list applied, rejected, superseded and deferred."), false),
                ..Schema.Verbosity()]),
            true,
            true,
            true,
            true,
            true,
            (c, a, ct) => Task.FromResult(a.Optional("proposal") is { } id
                ? ToolOutcome.From(ProposalCommands.Show(new ShowProposalRequest(c.ProjectPath, c.ProductVersion, id)))
                : ToolOutcome.From(ProposalStandingsCommand.Read(
                    new ProposalStandingsRequest(c.ProjectPath, a.Flag("include_set_aside")))))) { Requests = [typeof(ProposalStandingsRequest), typeof(ShowProposalRequest)] },

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
        new("motif_open", string.Empty, AgentClass.Read,
            Schema.Object(("path", Schema.String("Path to an existing FieldWorks .fwdata project."), true)),
            false, true, true, false, true,
            (c, a, ct) => Task.Run(() => OpenProject(a), ct))
            { Requests = [typeof(OpenRequest)], NeedsProject = false },
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
        new("motif_proposal_difference", string.Empty, AgentClass.Read,
            Schema.Object(("proposal", Schema.String("Proposal whose newest saved Difference to read."), false),
                ("difference", Schema.String("Saved Difference id; takes precedence over proposal."), false),
                ("could_be_bad", Schema.Boolean("List only words filed under a could-be-bad category."), false),
                ("bucket", Schema.String("Category code that listed words must have.",
                    Enum.GetValues<SIL.Motif.Contract.Responses.ProposalDifferenceBucket>()
                        .Select(SIL.Motif.Worker.Store.ProposalDifferenceRepository.BucketName).ToArray()), false),
                ("before_state", Schema.String("Approval state a listed word had before the Proposal.", ApprovalStates), false),
                ("after_state", Schema.String("Approval state a listed word has after the Proposal.", ApprovalStates), false),
                ("offset", Schema.Integer("Words to skip.", 0, 1000000), false),
                ("limit", Schema.Integer("Page size, maximum 200.", 1, 200), false)), true, true, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
                c.ProjectPath, a.Optional("proposal"), a.Optional("difference"), a.Flag("could_be_bad"), a.Optional("bucket"),
                a.Optional("before_state"), a.Optional("after_state"), a.Int("offset", 0), a.Int("limit", 25))))))
            { Requests = [typeof(ProposalDifferenceShowRequest)] },
        new("motif_difference_produce", string.Empty, AgentClass.Evaluate,
            Schema.Object(("proposal", Schema.String("Proposal whose completed Trial is compared."), true),
                ("text", Schema.String("GUID of the Text whose recorded human decisions are compared."), true),
                ("job", Schema.String("Completed Trial job id; omit for the newest completed Trial."), false)),
            false, false, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalDifferenceCommands.Produce(
                new ProposalDifferenceProduceRequest(c.ProjectPath, a.Required("proposal"), a.Required("text"),
                    a.Optional("job"))))))
            { Requests = [typeof(ProposalDifferenceProduceRequest)] },
        ..ParsimonyTools.All,
    ];

    private static string[] ApprovalStates =>
    [
        .. Enum.GetValues<SIL.Motif.Contract.Responses.HumanAgreementApprovalState>()
            .Select(SIL.Motif.Worker.Store.ProposalDifferenceRepository.ApprovalStateName)
            .Append("unavailable").Distinct(StringComparer.Ordinal),
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

    private static JsonObject ComposeBatchSchema(IReadOnlyList<AgentTool> tools)
    {
        var typedIntents = ProposalCommands.ComposerDescriptors.Where(composer => composer.InBatch)
            .Select(ComposerTools.BatchIntentSchema);
        var supportedIntents = BatchComposerVerbs
            .Select(verb =>
            {
                var command = CommandCatalog.All.SingleOrDefault(candidate => candidate.Name == verb)
                    ?? throw new InvalidOperationException($"Batch composer '{verb}' is missing from the command catalog.");
                if (command.ResponseType != typeof(ComposedOperationsResponse))
                    throw new InvalidOperationException($"Batch composer '{verb}' does not return composed operations.");
                var tool = tools.SingleOrDefault(candidate => candidate.Requests.Contains(command.RequestType))
                    ?? throw new InvalidOperationException($"Batch composer '{verb}' has no MCP adapter.");
                return BatchIntentSchema(verb, tool.InputSchema)
                    ?? throw new InvalidOperationException($"Batch composer '{verb}' has no structured MCP intent schema.");
            })
            .Concat(typedIntents)
            .ToArray();
        var citation = Schema.Object(
            ("source", Schema.String("Where this change came from."), true),
            ("locator", Schema.String("Optional page, section or other source location."), false));
        var itemBranches = new JsonArray(supportedIntents.Select(pair => (JsonNode)Schema.Object(
            ("verb", Schema.String("Composing verb for this intent.", pair.Verb), true),
            ("intent", pair.Schema.DeepClone().AsObject(), true),
            ("citation", citation.DeepClone().AsObject(), false))).ToArray());
        var item = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "One structured semantic intent. Its verb selects the matching intent fields.",
            ["oneOf"] = itemBranches,
        };
        return Schema.Object(
            ("draft", Schema.String("Draft name from motif_start_proposal."), true),
            ("intents", Schema.Array("Semantic composer intents for one Draft. Referenced entity ids must come from earlier completed calls or batches; declare operation ordering and dependencies in the intents.",
                item), true),
            ("project", Schema.String("Known project name or recorded .fwdata path."), true));
    }

    private static ToolOutcome ComposeBatch(ServerContext context, ToolArgs arguments)
    {
        var input = arguments.Copy();
        if (!input.TryGetPropertyValue("intents", out var intentsNode))
            throw new ToolArgumentException("tool.missing-argument", "Argument 'intents' is required.");
        if (intentsNode is not JsonArray intents)
            throw new ToolArgumentException("tool.invalid-argument", "Argument 'intents' must be an array.");

        var refusals = new List<string>();
        for (var index = 0; index < intents.Count; index++)
        {
            if (intents[index] is not JsonObject intent ||
                !intent.TryGetPropertyValue("verb", out var verbNode) ||
                verbNode is not JsonValue verbValue ||
                !verbValue.TryGetValue<string>(out var verb) ||
                string.IsNullOrWhiteSpace(verb))
            {
                refusals.Add($"Intent {index} must have a string 'verb'.");
                continue;
            }

            if (!BatchComposerVerbs.Contains(verb, StringComparer.Ordinal) && !ComposerTools.BatchVerbs.Contains(verb))
                refusals.Add($"Intent {index} uses unsupported batch verb '{verb}'.");
        }

        if (refusals.Count > 0)
            throw new ToolArgumentException("tool.invalid-argument",
                "Every batch entry must use one of the ordinary grammar composer verbs. " + string.Join(" ", refusals));

        return ToolOutcome.From(ProposalCommands.ComposeBatch(new(context.ProjectPath, context.ProductVersion,
                arguments.Required("draft"), arguments.IntentJson("intents"))),
            result => $"Draft now has {result.OperationCount} changes from {result.Intents.Count} intents. Dry Run the Draft, then Finalize for a person to Apply.");
    }

    private static (string Verb, JsonObject Schema)? BatchIntentSchema(string verb, JsonObject toolSchema)
    {
        var sourceProperties = toolSchema["properties"]?.AsObject();
        if (sourceProperties is null || sourceProperties.ContainsKey("intent_json") || sourceProperties.ContainsKey("intentJson"))
            return null;
        var canonicalToolSchema = CanonicalizeSchema(toolSchema);
        sourceProperties = canonicalToolSchema["properties"]!.AsObject();
        var properties = new JsonObject();
        foreach (var (name, schema) in sourceProperties)
            if (name is not ("draft" or "project"))
                properties[name] = schema?.DeepClone();
        if (properties.Count == 0) return null;

        var required = new JsonArray();
        if (canonicalToolSchema["required"] is JsonArray requiredProperties)
            foreach (var name in requiredProperties.Select(value => value?.GetValue<string>())
                         .Where(name => name is not null and not ("draft" or "project")))
                required.Add(name);
        var intent = new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Structured intent for {verb}.",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (required.Count > 0) intent["required"] = required;
        return (verb, intent);
    }

    private static JsonObject CanonicalizeSchema(JsonObject schema)
    {
        var canonical = schema.DeepClone().AsObject();
        if (canonical["properties"] is JsonObject sourceProperties)
        {
            var properties = new JsonObject();
            foreach (var (name, value) in sourceProperties)
                properties[CanonicalIntentName(name)] = value is JsonObject propertySchema
                    ? CanonicalizeSchema(propertySchema)
                    : value?.DeepClone();
            canonical["properties"] = properties;
        }
        if (canonical["required"] is JsonArray required)
            canonical["required"] = new JsonArray(required.Select(value =>
                (JsonNode?)JsonValue.Create(CanonicalIntentName(value!.GetValue<string>()))).ToArray());
        if (canonical["items"] is JsonObject items) canonical["items"] = CanonicalizeSchema(items);
        if (canonical["oneOf"] is JsonArray oneOf)
            canonical["oneOf"] = new JsonArray(oneOf.Select(value =>
                (JsonNode?)CanonicalizeSchema(value!.AsObject())).ToArray());
        return canonical;
    }

    private static string CanonicalIntentName(string name)
    {
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2
            ? name
            : parts[0] + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
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

    private static ToolOutcome OpenProject(ToolArgs args)
    {
        var fwDataPath = args.Required("path");
        var opened = ProposalCommands.Open(new OpenRequest(fwDataPath));
        if (!opened.Succeeded) return ToolOutcome.From(opened);

        var fullPath = Path.GetFullPath(fwDataPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var managedRoot = RunnerOptions.ResolveRoot();
        var workerRoot = managedRoot;
        var machineStorePath = Path.Combine(managedRoot, "motif.db");
        Exception? registrationFailure;
        try
        {
            workerRoot = Path.GetFullPath(managedRoot);
            machineStorePath = Path.Combine(workerRoot, "motif.db");
            registrationFailure = KnownProjectRecorder.TryRecord(managedRoot, project);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
            UnauthorizedAccessException or InvalidDataException or NotSupportedException or
            Microsoft.Data.Sqlite.SqliteException)
        {
            registrationFailure = exception;
        }

        if (registrationFailure is not null)
            return ToolOutcome.Refused("project.registration-failed", FailureReason.Refused,
                "Motif opened a disposable copy of the project but could not register it as Known: " +
                registrationFailure.Message,
                new Dictionary<string, string>
                {
                    ["workerRoot"] = workerRoot,
                    ["machineStorePath"] = machineStorePath,
                    ["cause"] = registrationFailure.Message,
                });

        return ToolOutcome.From(opened);
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
        var outcome = BaselineReads.ReadLexicon(context.ProjectPath, args.Optional("query"),
            args.Optional("morph_type"), args.Int("limit", 25), args.Int("offset", 0), detailed);
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
        ["expectedPrefixSlots"] = new JsonArray(args.RequiredList("expected_prefix_slots").Select(value => (JsonNode)value).ToArray()),
        ["prefixSlots"] = new JsonArray(args.RequiredList("prefix_slots").Select(value => (JsonNode)value).ToArray()),
        ["expectedSuffixSlots"] = new JsonArray(args.RequiredList("expected_suffix_slots").Select(value => (JsonNode)value).ToArray()),
        ["suffixSlots"] = new JsonArray(args.RequiredList("suffix_slots").Select(value => (JsonNode)value).ToArray()),
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
        ["expectedSlots"] = new JsonArray(args.RequiredList("expected_slots").Select(value => (JsonNode)value).ToArray()),
        ["slots"] = new JsonArray(args.RequiredList("slots").Select(value => (JsonNode)value).ToArray()),
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
        }
        // Wake on a resumed wait too: a queued job whose runner idled out or never started runs again.
        context.StartRunner();
        var waited = await Task.Run(() => JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            context.ProjectPath, context.ProductVersion, proposal, job, wait), cancellation,
            wakeRunner: context.StartRunner), cancellation);
        if (!waited.Succeeded) return StillRunning(context, waited.Refusal!, job) ?? ToolOutcome.Refused(waited.Refusal!);
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
        }
        // Wake on a resumed wait too: a queued job whose runner idled out or never started runs again.
        context.StartRunner();
        var waited = await Task.Run(() => JobCommands.WaitForJob(
            new WaitForJobRequest(context.ProjectPath, job, context.ProductVersion, wait), context.StartRunner),
            cancellation);
        if (!waited.Succeeded) return StillRunning(context, waited.Refusal!, job) ?? ToolOutcome.Refused(waited.Refusal!);
        return TrialResults.Complete(context, job, proposal) with { JobId = job };
    }

    // A wait that ran out is progress, not a failure: say where the job is and how to keep waiting.
    private static ToolOutcome? StillRunning(ServerContext context, Refusal refusal, string job)
    {
        if (refusal.Code is not ("job.wait-timeout" or "job.wait-cancelled")) return null;
        var running = new JsonObject
        {
            ["status"] = "running",
            ["job"] = job,
            ["detail"] = refusal.Message,
        };
        if (context.RunnerWarning is { } warning) running["runnerWarning"] = warning;
        return ToolOutcome.Ok(running, context.RunnerWarning is null
            ? "Call this tool again with job set to " + job + " to keep waiting."
            : "No runner could start, so this job will not progress: fix the runnerWarning, then call this tool again with job set to " + job + ".", job);
    }

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
