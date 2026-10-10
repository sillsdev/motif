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
/// <see cref="CommandDescriptor.AgentTool"/>. The built-in profile exposes the ones marked on by default; a
/// profile may expose more, rename, or hide. Nothing here reaches a <see cref="AgentClass.HumanOnly"/> command.
/// </summary>
internal static class AgentTools
{
    private static readonly (string, JsonObject, bool)[] NoArguments = [];

    public static IReadOnlyList<AgentTool> All { get; } = Build();

    /// <summary>Every tool name, which a profile may select.</summary>
    public static IReadOnlySet<string> Names { get; } = All.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<AgentTool> Build() =>
    [
        // Orient
        new("motif_overview",
            "Start here. Returns how well the project's grammar currently analyzes its texts: how many words get an " +
            "analysis, the accuracy against the words the linguist has approved, and which words to look at first. " +
            "Call it once at the beginning and again after the linguist applies a Proposal. Returns a refusal with " +
            "Next: when Motif has not yet measured the project.",
            AgentClass.Read, Schema.Object(Schema.Verbosity()), true, true, true, true, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(OverviewCommand.Overview(new OverviewRequest(c.ProjectPath)),
                _ => "Read the grammar behind a failing word with motif_grammar, or look at one word with motif_word."))),

        new("motif_capture_baseline",
            "Takes a fresh saved copy of the project that every read tool uses. Call it when a read tool reports that " +
            "Motif has no saved copy, or after the linguist says they saved changes in FieldWorks. It reads the saved " +
            "file only and never changes the project. It can take a minute on a large project.",
            AgentClass.Evaluate, Schema.Object(), false, true, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                BaselineCaptureCommand.Capture(new BaselineCaptureRequest(c.ProjectPath)),
                _ => "Now read with motif_grammar, motif_lexicon or motif_overview."))),

        new("motif_findings",
            "Lists the problems Motif's last grammar check found: for example an affix no template can place, or an " +
            "environment that names a class nothing belongs to. Each finding names the object it is about. Use it to " +
            "decide what to fix first. Filter by finding code with kind, or pass left_out to see only words the grammar " +
            "could not reach.",
            AgentClass.Read, Schema.Object([("kind", Schema.String("Only findings with this code."), false),
                ("left_out", Schema.Boolean("Only findings that explain words left out of the analysis."), false),
                ..Schema.Verbosity()]),
            true, true, false, true, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WarningsCommand.Warnings(
                new WarningsRequest(c.ProjectPath, a.Optional("kind"), a.Flag("left_out")))))),

        new("motif_check_grammar",
            "Runs the parser's own consistency check over the project's grammar and returns its findings. Slower than " +
            "motif_findings, which only reads the last result; call this after a change has been applied, not " +
            "while drafting. Needs the parser installed.",
            AgentClass.Evaluate, Schema.Object(Schema.Verbosity()), false, true, false, true, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(GrammarCheckQuery.Query(new GrammarCheckRequest(c.ProjectPath), ct)))),

        // Read
        new("motif_grammar",
            "Reads the project's grammar. With no kind it returns counts of everything the grammar holds. With a kind " +
            $"({string.Join(", ", GrammarReader.Kinds)}) it lists those objects with their ids, ready to name in a " +
            "change. Narrow with query (matches the name or abbreviation). Ids are 22 characters; copy them exactly. " +
            "To read affixes and stems, use motif_lexicon. For encoding help, use motif_guide with the matching topic.",
            AgentClass.Read, Schema.Object([("kind", Schema.String("What to list. Omit for counts of every kind.",
                    GrammarReader.Kinds.ToArray()), false),
                ("query", Schema.String("Only objects whose name or abbreviation contains this text."), false),
                ("offset", Schema.Integer("Items to skip, for the next page.", 0, 100000), false),
                ..Schema.Verbosity()]),
            true, true, true, true, true,
            (c, a, ct) => Task.FromResult(ReadGrammar(c, a))),

        new("motif_lexicon",
            "Searches the lexicon. Each entry has its id, headword, morph type (stem, suffix, prefix...), its lexeme " +
            "form, and its senses with the glosses and the id of each sense's grammatical info. Use it to find the " +
            "entry a change is about, and to see what already exists before you add anything. query matches the " +
            "headword, a gloss, or an allomorph form. detail=detailed adds every allomorph with its environments. " +
            "Use motif_guide for affix slots, conditioned allomorphs or feature-conditioned slots.",
            AgentClass.Read, Schema.Object([("query", Schema.String("Text the headword, a gloss or a form contains."), false),
                ("morph_type", Schema.String("Only entries of this morph type, such as 'stem' or 'suffix'."), false),
                ("offset", Schema.Integer("Entries to skip, for the next page.", 0, 100000), false),
                ..Schema.Verbosity()]),
            true, true, true, true, true,
            (c, a, ct) => Task.FromResult(ReadLexicon(c, a))),

        new("motif_word",
            "Shows what FieldWorks already knows about one exact word: each analysis it holds and the linguist's " +
            "opinion of it (approved, disapproved, or not yet judged). Use it to see what the linguist expects a " +
            "word to mean before you change the grammar to parse it.",
            AgentClass.Read, Schema.Object([("word", Schema.String("The word, exactly as written in the project."), true),
                ..Schema.Verbosity()]),
            true, true, true, true, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WordContextQuery.Query(
                new WordContextRequest(c.ProjectPath, a.Required("word"))),
                _ => "To see why the parser does or does not analyze it, call motif_try_word."))),

        new("motif_texts",
            "Lists the project's texts with their ids and word counts. Use it to pick example words from real text.",
            AgentClass.Read, Schema.Object(Schema.Verbosity()), true, true, false, true, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(TextInventoryQuery.Query(new TextInventoryRequest(c.ProjectPath))))),

        new("motif_writing_systems",
            "Lists the project's writing systems with their tags. Call it before writing a form or gloss so you use " +
            "the project's own tag, never one you guessed.",
            AgentClass.Read, Schema.Object(), true, true, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WritingSystemsQuery.Query(new WritingSystemsRequest(c.ProjectPath))))),

        // Draft
        new("motif_start_proposal",
            "Starts a new Draft Proposal: a named container for one small, related set of changes. Give it a short " +
            "draft name (letters, digits and hyphens), a one-line label a person reads in Motif's window, such as " +
            "'Add the plural suffix -s', and a comment explaining why the change is right, which the linguist " +
            "reads while deciding. Keep each Proposal to one concept. Returns the draft name and the proposal id; " +
            "use the draft name with the motif_add_* tools and the proposal id with motif_trial.",
            AgentClass.Draft, Schema.Object([("draft", Schema.String("Short draft name, for example 'plural-s'."), true),
                ("label", Schema.String("A one-line description a reviewer reads in Motif's window."), true),
                ("comment", Schema.String("Why this change is right: the evidence, in a few sentences."), true)]),
            false, false, true, false, true,
            (c, a, ct) => Task.FromResult(StartProposal(c, a))),

        new("motif_guide",
            "Reads Motif's FieldWorks encoding guidance. Choose a topic for affix slots, conditioned allomorphs, " +
            "feature-conditioned slots, nasal assimilation, no inflection, the workflow or asking the linguist. " +
            "Each guide names the FieldWorks objects and whether Motif can compose them today.",
            AgentClass.Read, Schema.Object([("topic", Schema.String("The guide topic to read.", EncodingGuides.Topics.ToArray()), true)]),
            true, true, true, false, false,
            (c, a, ct) => Task.FromResult(ReadGuide(a))),

        new("motif_add_lexeme_form",
            "Adds a lexeme form (the written form of an entry, such as the stem or affix text) to an entry that " +
            "already exists, and optionally a gloss on one of its senses, to a Draft. Find the entry, its morph " +
            "type id and its sense in motif_lexicon, and the writing system tag shown on existing forms in motif_lexicon. It does " +
            "not create entries, senses or allomorphs; Motif cannot author those yet, so tell the " +
            "linguist when a task needs one. See motif_guide for suffix-prefix-slots or conditioned-allomorphs.",
            AgentClass.Draft, Schema.Object([("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("entry", Schema.String("Id of the existing entry."), true),
                ("morph_type", Schema.String("Id of the morph type of the new form."), true),
                ("ws", Schema.String("Writing system tag the form is written in."), true),
                ("text", Schema.String("The form's written text."), true),
                ("environments", Schema.StringArray("Optional environment ids restricting the new form."), false),
                ("is_abstract", Schema.Boolean("True for a root-and-pattern form with no fixed segments."), false),
                ("sense", Schema.String("Id of an existing sense of the entry to gloss. Needs gloss_ws and gloss_text."), false),
                ("gloss_ws", Schema.String("Writing system tag of the gloss."), false),
                ("gloss_text", Schema.String("The gloss text."), false)]),
            false, false, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorLexemeForm(
                new ComposeAuthorLexemeFormRequest(c.ProjectPath, c.ProductVersion, a.Required("draft"),
                    LexemeFormIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Check them with motif_dry_run once finished."))),

        new("motif_add_feature_structure",
            "Gives an existing stem's grammatical info an empty feature structure to record features against. The " +
            "grammatical info id comes from a sense's grammaticalInfo in motif_lexicon. It refuses when the stem " +
            "already has one and cannot add feature values. See motif_guide(topic=feature-conditioned-slots) for " +
            "the full encoding.",
            AgentClass.Draft, Schema.Object([("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("msa", Schema.String("Id of the stem's grammatical info."), true)]),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeAuthorFeatureStructure(new ComposeAuthorFeatureStructureRequest(
                    c.ProjectPath, c.ProductVersion, a.Required("draft"),
                    new JsonObject { ["msa"] = a.Required("msa") }.ToJsonString())),
                r => $"Draft now has {r.OperationCount} changes. Read motif_guide(topic=feature-conditioned-slots) before adding any feature values."))),

        new("motif_add_feature_value",
            "Adds a feature specification to an existing feature structure in a Draft, optionally choosing a closed " +
            "symbolic value. The value must belong to the named feature; duplicate specifications refuse. Copy ids " +
            "from motif_grammar or an earlier composer result. Next: add remaining values, then finish the Proposal and run motif_trial.",
            AgentClass.Draft, Schema.Object(("draft", Schema.String("Draft name."), true),
                ("featStruc", Schema.String("Feature structure id."), true),
                ("feature", Schema.String("Feature id."), true), ("value", Schema.String("Optional symbolic value id."), false)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorFeatureValue(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("featStruc", "feature", "value"))),
                r => "Use created entityId values in later composer calls; finish the Proposal and run motif_trial."))),

        new("motif_add_phoneme",
            "Authors a phoneme and its grapheme codes in a Draft, using the first vernacular writing system. " +
            "Representations must be nonempty, unique and free of environment syntax. No placeholder code remains. " +
            "Optional feature/value pairs must belong to the phonological feature system. Returns created entityId values. " +
            "Next: use the phoneme id in motif_add_natural_class or motif_add_environment.",
            AgentClass.Draft, Schema.Object(("draft", Schema.String("Draft name."), true),
                ("name", Schema.String("Phoneme name."), true),
                ("representations", Schema.StringArray("Explicit grapheme codes, such as ['a'] or ['sh', 'š']."), true),
                ("features", Schema.FeatureValues(), false)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorPhoneme(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "representations", "features"))),
                r => "Use the phoneme create's entityId in motif_add_natural_class or motif_add_environment."))),

        new("motif_add_natural_class",
            "Authors a natural class in a Draft. Supply exactly one nonempty list: members (phoneme ids) or features " +
            "(closed phonological feature/value pairs). Its abbreviation must be unique and use letters, digits or hyphens. " +
            "References may name objects authored earlier in this Draft. Returns the class entityId. " +
            "Next: author its context with motif_add_environment using this class id.",
            AgentClass.Draft, Schema.Object(("draft", Schema.String("Draft name."), true),
                ("name", Schema.String("Natural class name."), true), ("abbreviation", Schema.String("Unique environment abbreviation, such as V."), true),
                ("members", Schema.StringArray("Phoneme ids in the first set."), false), ("features", Schema.FeatureValues(), false)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorNaturalClass(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "abbreviation", "members", "features"))),
                r => "Use the natural class create's entityId in motif_add_environment."))),

        new("motif_edit_natural_class",
            "Adds or removes members of one existing segment natural class after comparing its exact current " +
            "phoneme identities. The desired class must remain nonempty, and feature classes cannot change subtype " +
            "in place. The result lists environments, rewrite rules and insertions that still use the class. " +
            "This only stages a Draft; only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing segment natural class."), true),
                ("expectedMembers", Schema.StringArray("Exact current phoneme ids in the class."), true),
                ("members", Schema.StringArray("Requested distinct phoneme ids; the class must remain nonempty."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditNaturalClass(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "expectedMembers", "members"))),
                r => $"Draft now has {r.OperationCount} member changes. Review RelatedObjects before finishing; only a person can Apply."))),

        new("motif_relink_natural_class",
            "Moves only the named environment and rewrite-rule context users from one natural class to another " +
            "class created earlier in this Draft. Supply each environment's exact typed left and right contexts; " +
            "each relink depends on the replacement class creation. RelatedObjects lists the source class's current " +
            "users for scope review. This only stages a Draft; only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("source", Schema.String("Canonical id of the class users currently reference."), true),
                ("replacement", Schema.String("Canonical id of the replacement class."), true),
                ("replacementCreationOperation", Schema.String("Operation id returned by the class create earlier in this Draft."), true),
                ("environments", Schema.Array("Explicit environment users and their exact current typed contexts.",
                    Schema.Object(("target", Schema.String("Environment id."), true),
                        ("expectedLeft", NaturalClassRelinkContexts(), true),
                        ("expectedRight", NaturalClassRelinkContexts(), true))), true),
                ("ruleContexts", Schema.StringArray("Explicit rewrite-rule context object ids using the source class."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeRelinkNaturalClass(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("source", "replacement", "replacementCreationOperation", "environments", "ruleContexts"))),
                r => $"Draft now has {r.OperationCount} relinks. Review RelatedObjects against the declared scope; only a person can Apply."))),

        new("motif_add_environment",
            "Authors a phonological environment in a Draft from typed left and right contexts. Each item names one " +
            "phoneme, naturalClass, boundary ('word' or 'morpheme'), or boundaryMarker id; word boundaries belong at the outer edge. " +
            "A boundaryMarker is emitted as a literal code, and syntax codes such as # or + cannot be emitted literally. " +
            "Class references use unique abbreviations; phoneme references use explicit vernacular codes. " +
            "Returns the environment entityId. Allomorph attachment requires a subsequent composer. " +
            "Next: finish the Proposal, inspect motif_dry_run, and test contrasts with motif_trial.",
            AgentClass.Draft, Schema.Object(("draft", Schema.String("Draft name."), true),
                ("name", Schema.String("Environment name."), true), ("left", Schema.Contexts(), true), ("right", Schema.Contexts(), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorEnvironment(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "left", "right"))),
                r => "Finish the Proposal, inspect motif_dry_run, and test positive and negative words with motif_trial."))),

        new("motif_add_phonological_rule",
            "Stages one simple regular rewrite rule in a Draft. Input and output each contain at most one phoneme or " +
            "natural class; an empty side means insertion or deletion. left and right are ordered lists of one to eight " +
            "phoneme, class, boundary, or boundaryMarker contexts. A boundaryMarker id stays literal even when its code is #; " +
            "use boundary='word' for the word edge. placement names the adjacent existing rule or rules. Alpha variables, " +
            "metathesis and allomorph environments are unsupported. Returns created rule identities and operation details. " +
            "Next: finish the Proposal, inspect motif_dry_run, and test parse and generate behavior with motif_trial.",
            AgentClass.Draft, RuleComposerSchema(), false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorPhonologicalRule(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.IntentJson("name", "direction", "input",
                    "output", "left", "right", "placement"))),
                r => $"Draft now has {r.OperationCount} operations. Finish it, then inspect motif_dry_run."))),

        new("motif_add_affix_slot",
            "Stages a new inflectional affix slot owned by an existing category, gives it a name and optionality, " +
            "and can assign existing inflectional affixes to it in the same Draft. Assignments must fit the slot " +
            "category. This only stages a Proposal, and only a person can Apply it. Next: finish the Draft, inspect " +
            "motif_dry_run, and run a bounded motif_trial on affected words.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("category", Schema.String("Canonical id of the existing category that owns the new slot."), true),
                ("name", Schema.String("Slot name in the project's writing system."), true),
                ("ws", Schema.String("Writing system tag for the slot name."), true),
                ("optional", Schema.Boolean("Whether forms may omit this slot."), true),
                ("assignments", Schema.StringArray("Optional canonical ids of existing inflectional affix MSAs to assign."), false)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorAffixSlot(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("category", "name", "ws", "optional", "assignments"))),
                r => $"Draft now has {r.OperationCount} changes. Finish it, inspect motif_dry_run, and run a bounded motif_trial; only a person can Apply."))),

        new("motif_add_affix_template",
            "Stages an active alternative affix template in a Draft, preserving the category's existing template order. " +
            "prefix_slots and suffix_slots are ordered canonical ids for existing slots or slots created earlier in this " +
            "Draft; each must belong to the template category or an ancestor, and no slot may be repeated. final records " +
            "whether the template requires further derivation. This only stages a Proposal, and only a person can Apply it. " +
            "Next: finish the Draft, inspect motif_dry_run, and run a bounded motif_trial on affected words.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("category", Schema.String("Canonical id of the existing category that owns the template."), true),
                ("name", Schema.String("Template name in the project's writing system."), true),
                ("ws", Schema.String("Writing system tag for the template name."), true),
                ("prefix_slots", Schema.StringArray("Ordered canonical ids of prefix slots."), true),
                ("suffix_slots", Schema.StringArray("Ordered canonical ids of suffix slots."), true),
                ("final", Schema.Boolean("Whether the template requires further derivation."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeAuthorAffixTemplate(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), AuthorAffixTemplateIntent(a))),
                r => $"Draft now has {r.OperationCount} operations. Finish it, inspect motif_dry_run, and run a bounded motif_trial; only a person can Apply."))),

        new("motif_retire_allomorph",
            "Stages removal of an ordinary prefix or suffix alternate after every live use has an exact destination " +
            "among the entry's duplicate forms. It refuses changed forms, unsupported references and owned dependents; " +
            "the Draft can be checked with motif_dry_run before a person Applies it.",
            AgentClass.Draft, Schema.Object(("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("intent_json", Schema.String("Closed motif-retire-allomorph/v1 JSON with exact source, destination and reference identities."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeRetireAllomorph(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Required("intent_json"))),
                r => $"Draft now has {r.OperationCount} operations. Finish it, then inspect motif_dry_run."))),
        new("motif_retire_redundant_zero_affix",
            "Stages removal of one complete, unused zero-affix entry when its only loaded realization is empty, its " +
            "analysis adds no feature or class, every slot is optional and unused by templates, and no external " +
            "object refers to the graph. " +
            "The operation removes optional slot links first, preserves the slots, and refuses meaningful or referenced zeros.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("intent_json", Schema.String("Closed motif-retire-redundant-zero-affix/v1 JSON naming one canonical entry id."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeRetireRedundantZeroAffix(
                new(c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Required("intent_json"))),
                r => $"Draft now has {r.OperationCount} operations. Finish it, inspect motif_dry_run, and have a person Apply."))),
        new("motif_edit_adhoc_prohibition",
            "Stages one explicit Disabled value for a flat allomorph or morpheme prohibition in a Draft. " +
            "expected_disabled must match the target's current value; grouped prohibitions are refused. This only " +
            "stages a Proposal, and only a person can Apply it. Next: finish the Draft, inspect motif_dry_run, and " +
            "compare bounded before and after Trials with the same words.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of one flat allomorph or morpheme prohibition."), true),
                ("expected_disabled", Schema.Boolean("The value the target must currently have."), true),
                ("disabled", Schema.Boolean("The explicit Disabled value to write."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeEditAdhocProhibition(new(c.ProjectPath, c.ProductVersion,
                    a.Required("draft"), EditAdhocProhibitionIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Finish it, inspect motif_dry_run, and run a bounded motif_trial; only a person can Apply."))),

        new("motif_edit_affix_slot",
            "Changes whether one existing affix slot is optional. The response lists every inflectional affix and template using that shared slot; this only stages a Draft, and only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing affix slot."), true),
                ("expected_optional", Schema.Boolean("The optionality the project must currently have."), true),
                ("optional", Schema.Boolean("The requested optionality."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAffixSlot(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), EditAffixSlotIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Finish it, inspect motif_dry_run, then run a bounded motif_trial; only a person can Apply."))),
        new("motif_edit_affix_template",
            "Reorders the existing prefix and suffix slots of one affix template using explicit slot identities. It cannot add or remove slots or create a template; this only stages a Draft, and only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing affix template."), true),
                ("expected_prefix_slots", Schema.StringArray("Current prefix slot ids in project order."), true),
                ("prefix_slots", Schema.StringArray("Requested prefix slot ids in explicit order."), true),
                ("expected_suffix_slots", Schema.StringArray("Current suffix slot ids in project order."), true),
                ("suffix_slots", Schema.StringArray("Requested suffix slot ids in explicit order."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAffixTemplate(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), EditAffixTemplateIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Finish it, inspect motif_dry_run, then run a bounded motif_trial; only a person can Apply."))),
        new("motif_edit_inflectional_affix",
            "Changes the slot membership of one existing inflectional affix MSA. Slots must belong to its category or an ancestor; other MSA subtypes are refused. This only stages a Draft, and only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing inflectional affix MSA."), true),
                ("expected_slots", Schema.StringArray("Current slot ids assigned to the MSA."), true),
                ("slots", Schema.StringArray("Requested slot ids; order has no meaning."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditInflectionalAffix(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), EditInflectionalAffixIntent(a))),
                r => $"Draft now has {r.OperationCount} changes. Finish it, inspect motif_dry_run, then run a bounded motif_trial; only a person can Apply."))),
        new("motif_edit_allomorph_condition",
            "Replaces one existing allomorph's environment references after checking the exact current list. " +
            "Use field=phoneEnv for a stem or affix OR-list, and field=position for an affix's ordered position choices. " +
            "The edit changes only this allomorph, so other users of a shared environment keep their references. " +
            "This only stages a Draft; only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the existing stem or affix allomorph."), true),
                ("field", Schema.String("LibLCM condition field.", "phoneEnv", "position"), true),
                ("expected_environments", Schema.StringArray("Current environment ids; order matters only for position."), true),
                ("environments", Schema.StringArray("Requested environment ids; order matters only for position."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeEditAllomorphCondition(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "field", "expected_environments", "environments"))),
                r => $"Draft now has {r.OperationCount} changes. Finish it, inspect motif_dry_run, then run a bounded motif_trial; only a person can Apply."))),
        new("motif_order_allomorphs",
            "Reorders the existing alternate forms within one entry using explicit neighboring anchors. " +
            "The lexeme form is outside that sequence and cannot be moved. Earlier forms have selection precedence; " +
            "a final unrestricted form may serve as an elsewhere fallback. This only stages a Draft; only a person can Apply it.",
            AgentClass.Draft, Schema.Object(
                ("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("target", Schema.String("Canonical id of the owning lexical entry."), true),
                ("expected_alternates", Schema.StringArray("Current alternate-form ids in project order."), true),
                ("alternates", Schema.StringArray("Requested order of those same alternate forms."), true)),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.ComposeOrderAllomorphs(new(
                c.ProjectPath, c.ProductVersion, a.Required("draft"),
                a.IntentJson("target", "expected_alternates", "alternates"))),
                r => $"Draft now has {r.OperationCount} moves. Finish it and inspect motif_dry_run; only a person can Apply."))),
        new("motif_record_parsimony_disposition",
            "Stages an agent-attributed keep, fix, ask or defer decision for one exact Parsimony finding in a Draft. " +
            "Use its existing Notebook record type and the finding's typed subject and evidence digest. A reason is optional; " +
            "ask requires a question. This tool only stages the Proposal; only a person can Apply it, and a pending keep does not suppress findings. " +
            "Next: finish the Draft and inspect it with motif_dry_run.",
            AgentClass.Draft, Schema.Object([
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
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeRecordParsimonyDisposition(new(c.ProjectPath, c.ProductVersion,
                    a.Required("draft"), a.IntentJson("recordTypeId", "measureId", "subject", "disposition",
                        "evidenceDigest", "evidenceContract", "subjectCaption", "measureCaption", "reason",
                        "question", "reportId"))),
                r => $"Draft now has {r.OperationCount} changes. A pending keep does not suppress findings; only a person can Apply."))),

        new("motif_parsimony_record_types",
            "Lists the saved Notebook record types and their portable ids. Choose a type by its id, never by its " +
            "English name. Use the selected id with motif_dispose_parsimony_finding.",
            AgentClass.Read, Schema.Object(), true, true, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.ListNotebookRecordTypes(
                new(c.ProjectPath, c.ProductVersion))))),

        new("motif_dispose_parsimony_finding",
            "Stages a keep, fix, ask or defer decision from one exact finding in a stored Parsimony Report. " +
            "Motif derives the subject, evidence digest, evidence contract and captions from that finding. " +
            "Choose record_type_id from motif_parsimony_record_types. A reason is optional; ask requires a question. " +
            "This only stages a Draft Proposal: only a person can Apply it, and a pending keep does not suppress findings.",
            AgentClass.Draft, Schema.Object([
                ("report_id", Schema.String("Id of a stored Parsimony Report from this project."), true),
                ("finding_id", Schema.String("Exact finding id returned by that Report."), true),
                ("disposition", Schema.String("The proposed decision.", "fix", "keep", "ask", "defer"), true),
                ("record_type_id", Schema.String("Portable id from motif_parsimony_record_types."), true),
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("reason", Schema.String("Optional explanation, stored with the readable record."), false),
                ("question", Schema.String("Required only when disposition is ask."), false),
            ]),
            false, false, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.RecordDisposition(
                new(c.ProjectPath, c.ProductVersion, a.Required("report_id"), a.Required("finding_id"),
                    a.Required("disposition"), a.Required("record_type_id"), a.Required("draft"),
                    a.Optional("reason"), a.Optional("question"))),
                r => $"Draft now has {r.OperationCount} operations. Finish the Draft, then inspect it with motif_dry_run."))),

        new("motif_set_gloss",
            "Sets the gloss of an existing sense, in one writing system, in a Draft. Find the sense id in " +
            "motif_lexicon. Setting a gloss that is already in the draft replaces the draft's earlier value.",
            AgentClass.Draft, Schema.Object([("draft", Schema.String("The draft name from motif_start_proposal."), true),
                ("sense", Schema.String("Id of the sense."), true),
                ("ws", Schema.String("Writing system tag of the gloss."), true),
                ("text", Schema.String("The new gloss."), true)]),
            false, true, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Required("sense"), a.Required("ws"),
                a.Required("text")))))),

        new("motif_remove_operations",
            "Removes changes from a Draft by operation id, to correct a mistake. Operation ids come from the result " +
            "of each motif_add_* call and from motif_proposals. It refuses when another change depends on the one " +
            "removed; remove the dependent change first.",
            AgentClass.Draft, Schema.Object([("draft", Schema.String("The draft name."), true),
                ("operation_ids", Schema.StringArray("Operation ids to remove."), true)]),
            false, true, false, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.List("operation_ids"), Force: false))))),

        // Evaluate
        new("motif_dry_run",
            "Checks that a finished Proposal's changes fit the project by applying them to a throwaway copy, and " +
            "reports what each change did. It does not touch the real project. The Proposal must be finished with " +
            "motif_finish_proposal first. If it takes longer than wait_seconds you get status 'running' and a job id: " +
            "call again with that job to keep waiting.",
            AgentClass.Evaluate, Schema.Object([("proposal", Schema.String("The proposal id."), true),
                ("job", Schema.String("A job id from an earlier 'running' result, to keep waiting for it."), false),
                ("wait_seconds", Schema.Integer("How long to wait for the result. Default 40.", 1, 300), false),
                ..Schema.Verbosity()]),
            false, false, true, true, false, DryRun),

        new("motif_trial",
            "Tests a Proposal on sample words: Motif applies the draft to a throwaway copy and parses the words you " +
            "name, with and without the changes, and reports which words now parse, which stopped parsing, and " +
            "what changed. Works on a draft or a finished Proposal. Always include words that should NOT parse so a " +
            "rule that is too broad shows up. Omit words to use the words the Proposal changes. If it takes longer " +
            "than wait_seconds you get 'running' and a job id: call again with that job. See motif_guide(topic=workflow) " +
            "for choosing positive, contrasting and negative forms.",
            AgentClass.Evaluate, Schema.Object([("proposal", Schema.String("The proposal id."), true),
                ("words", Schema.StringArray("Words to parse. Omit to use the words the Proposal affects."), false),
                ("job", Schema.String("A job id from an earlier 'running' result, to keep waiting for it."), false),
                ("wait_seconds", Schema.Integer("How long to wait for the result. Default 40.", 1, 300), false),
                ..Schema.Verbosity()]),
            false, false, true, true, false, Trial),

        new("motif_try_word",
            "Parses one word with the project's current grammar and shows how the parser handled it: the " +
            "analyses it found, or where each attempt failed. This is the project as it is now, not a draft. Use it " +
            "to learn why a word fails before you write a change. Needs the parser installed.",
            AgentClass.Evaluate, Schema.Object([("word", Schema.String("The word to parse."), true), ..Schema.Verbosity()]),
            false, true, true, true, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(WordTraceQuery.Query(
                new WordTraceRequest(c.ProjectPath, a.Required("word")), ct)))),

        new("motif_job",
            "Reads the state of one job (a dry run or trial) by its job id.",
            AgentClass.Read, Schema.Object([("job", Schema.String("The job id."), true)]),
            true, true, false, false, false,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(JobCommands.Show(
                new ShowJobRequest(c.ProjectPath, a.Required("job"), c.ProductVersion))))),

        // Hand off
        new("motif_finish_proposal",
            "Finishes a Draft so it becomes a Proposal the linguist can review in Motif's window. Do this before " +
            "a dry run or trial, which require a finished Proposal. Finishing does not change the project; the linguist decides " +
            "whether to apply it. After finishing, the draft is closed: further edits start from a copy.",
            AgentClass.Draft, Schema.Object([("draft", Schema.String("The draft name."), true),
                ("expected_revision", Schema.String("The revision you last read, to refuse if the draft changed."), false)]),
            false, false, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ProposalCommands.Finalize(new FinalizeRequest(
                c.ProjectPath, c.ProductVersion, a.Required("draft"), a.Optional("expected_revision"))),
                r => $"Proposal {r.ProposalId} is ready for the linguist to review in Motif. Tell them its label."))),

        new("motif_proposals",
            "Lists every Proposal in the project with its status (proposed, deferred, rejected, applied, " +
            "superseded) and label; or, with proposal, shows one Proposal's operations in full. Use it to see what " +
            "exists before starting a new one and to see what the linguist did with earlier ones.",
            AgentClass.Read, Schema.Object([("proposal", Schema.String("A proposal id to show in full. Omit to list."), false),
                ..Schema.Verbosity()]),
            true, true, true, true, true,
            (c, a, ct) => Task.FromResult(a.Optional("proposal") is { } id
                ? ToolOutcome.From(ProposalCommands.Show(new ShowProposalRequest(c.ProjectPath, c.ProductVersion, id)))
                : ToolOutcome.From(ProposalCommands.List(new ListProposalsRequest(c.ProjectPath, c.ProductVersion))))),

        new("motif_activity",
            "Tells you what you have already done in this project: your tool calls (counts, errors, the most recent " +
            "ones), the Proposals that exist and their states, and the jobs you started and how they ended. Call it " +
            "when you resume work or lose track of where you were.",
            AgentClass.Read, Schema.Object(), true, true, true, false, false,
            (c, a, ct) => Task.FromResult(Activity(c))),
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
        return waited.Succeeded
            ? ToolOutcome.Ok(JsonNode.Parse(ProjectionJson.Serialize(waited.Value!))!,
                "If the changes fit, run motif_trial on sample words; if not, fix the draft first.", job)
            : StillRunning(waited.Refusal!, job) ?? ToolOutcome.Refused(waited.Refusal!);
    }

    private static async Task<ToolOutcome> Trial(ServerContext context, ToolArgs args, CancellationToken cancellation)
    {
        var proposal = args.Required("proposal");
        var wait = TimeSpan.FromSeconds(Math.Clamp(args.Int("wait_seconds", 40), 1, 300));
        var job = args.Optional("job");
        if (job is null)
        {
            var words = args.List("words");
            var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(context.ProjectPath, context.ProductVersion,
                proposal, Words: words.Count > 0 ? words : null));
            if (!queued.Succeeded) return ToolOutcome.Refused(queued.Refusal!);
            job = queued.Value!.JobId;
            context.StartRunner();
        }
        var waited = await Task.Run(() => JobCommands.WaitForJob(
            new WaitForJobRequest(context.ProjectPath, job, context.ProductVersion, wait)), cancellation);
        if (!waited.Succeeded) return StillRunning(waited.Refusal!, job) ?? ToolOutcome.Refused(waited.Refusal!);
        var result = new JsonObject { ["job"] = JsonNode.Parse(ProjectionJson.Serialize(waited.Value!)) };
        var assessments = JobCommands.Assessments(new JobAssessmentsRequest(context.ProjectPath, job, context.ProductVersion));
        if (assessments.Succeeded) result["assessments"] = JsonNode.Parse(ProjectionJson.Serialize(assessments.Value!));
        return ToolOutcome.Ok(result, "Read which words changed. Revise the draft, then Trial again, or finish with " +
            "motif_finish_proposal. If the evidence leaves an encoding choice open, ask one question using " +
            "motif_guide(topic=ask-the-linguist).", job);
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
