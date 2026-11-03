# Read grammar warnings

`warnings` reads grammar-check findings and allomorph refusals already recorded by parsing for a project. It can filter by diagnostic code or show only findings at warning level. Add `--proposal <id>` to read the check stored with that Proposal's latest Trial. When the current Baseline has not been checked, `warnings` refuses and tells you to run `grammar check`; it does not return an empty findings list.

PanGloss reports an unreadable phonological environment as a warning. Roots ignore that restriction but keep valid ones; ordinary affixes add an unrestricted pass beside valid passes; an infix with no valid position is skipped.

## When to use it

Use it after [Check a grammar](cmd:grammar%20check) when you want to inspect reported findings. Combine `--kind` and `--left-out` to narrow a large result without rerunning the check.

## Example

```powershell
motif warnings --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --left-out --json
```

## What it prints

The response includes separate error, warning, and information counts, plus findings with PanGloss-owned titles, explanations, guidance, optional background, verified FieldWorks places, subjects, and links where available. Information can identify a stored analysis that PanGloss no longer reproduces, name the rule that changed its surface, or explain why the loss could not be attributed. Each finding retains the report locale. Missing references and project settings have explicit statuses and unavailable-link reasons. `--left-out` keeps warning-level findings only. Without a Baseline, the command refuses and tells you to run `motif baseline capture --project <fwdata>`. When a Baseline has no stored check, it refuses and tells you to run `motif grammar check --project <fwdata>`.

The response includes the Baseline's lexical entry and rule counts when that inventory is available. Motif advisories are listed separately from parser findings.

Motif findings include `grammar.required-slot-empty` when a required inflectional slot in a template has no affixes. Each finding names the template, part of speech, slot and canonical slot ID. The slot is marked required, but FieldWorks and PanGloss currently treat an empty required slot as optional.

Motif findings also include `grammar.negated-feature-value` for a stored negated feature value, which FieldWorks' parser loader cannot read. Each finding names the feature, the value and the object that holds it, and, for a stem or inflectional affix, points to [removing the feature value](cmd:compose-remove-feature-value); for other objects it says Motif has no command for it.

Motif findings also include `grammar.derived-stem-keeps-from-class` for a derivational affix with a From Inflection Class and no To Inflection Class. Each finding names the affix and the class its derived stems keep, and says to set a To Inflection Class when they belong to another class.

Motif findings also include `grammar.template-has-no-slots` for an affix template with no slot, which the parser cannot use. Each finding names the template and its part of speech and says to add a slot.

Motif findings also include `grammar.phoneme-spelling-ambiguous` for a phoneme whose spelling is other phonemes' spellings joined (`ng` beside `n` and `g`), which the parser always reads as the longest spelling, and `grammar.allomorph-environment-ignored-by-parser` for a form whose environment the parser cannot read and so drops, leaving the form unrestricted by it. Each finding names the phoneme or form and says what to change.

Motif findings also include `grammar.allomorph-repeats-environment-material` for a form that already spells the material its environment asks of its neighbour (`batan-ung` before `ng`); it is a second look, not a refusal.

Motif findings also include `fieldworks.do-not-parse-ignored` (information) for each entry marked do-not-parse: FieldWorks does not read the flag and parses the entry; the pinned PanGloss leaves it out until the next PanGloss release parses it too.

Motif findings also include three lexicon hygiene warnings: `lexicon.stem-typed-as-content-word` for a Noun, Verb or Adjective stem that is a single letter, a cross-reference stub or a function word by its gloss, `lexicon.duplicate-entry` for entries with the same form and first gloss, and `lexicon.affix-hyphen-on-wrong-side` for a prefix written `-i` or a suffix written `i-`. They report only; see [`grammar-check`](cmd:grammar%20check) for what each looks for and how to fix many at once.

When a stored Assessment matches the Baseline and Selection, all recorded allomorph segmentation refusals are included under `parse.allomorph.unsegmentable`, labelled **Parse all words**. Their descriptions preserve the parser's reason, without truncating the allomorph list or inventing guidance. These findings can differ from the whole-grammar check's findings.

## Your words

When a stored Assessment from [Measure a Selection](cmd:assess) matches the current Baseline and Selection, each finding lists the words in the Selection reached by its subjects, with their meanings, such as Lost or Kept. The headline counts exact uses only, once per word, and says how many of those don't parse. Membership and spelling candidates have separate counts and never enter that headline. Each kind of finding gets the same separate counts.

Motif finds the words from the object the finding names, by its FieldWorks identity:

- an allomorph or grammatical info: the FieldWorks or parser analyses that use it;
- an entry or an environment: the FieldWorks or parser analyses that use its allomorphs;
- a sense: the FieldWorks or parser analyses that use its grammatical info;
- a natural class: the allomorphs its environments condition, and the words the phonological rules that name it ran in;
- a rule: the words its stored per-word timings say it ran in.
- a feature definition, value or specification: the allomorphs, grammatical infos and rules whose feature specifications name it;
- a stem name or inflection class: the allomorphs or grammatical infos that refer to it;
- a boundary: the allomorphs and rules whose contexts name it.

Templates and slots reach words using their affixes as **membership candidates**. Inflection types, ad hoc prohibitions, phoneme sets and feature systems also reach members as candidates: using a member does not prove a parse selected the named resource.

Letters and phonemes reach **spelling candidates; not confirmed uses of the phoneme**. Spelling uses case-insensitive, canonically decomposed substring matching, which cannot establish phoneme identity. A feature owned by a phoneme keeps that same limit, including when reached through its feature system. A phoneme set follows its members' grammar references and omits spelling candidates.

For a word with no analysis, an allomorph or entry also reaches spelling candidates through the vernacular forms recorded for the Baseline. This includes typed words the parser refused. The window calls these **spelling matches**, lists the words, and never presents the spelling alone as an exact use.

For each finding, exact uses take precedence over membership candidates, then spelling candidates. The lists never share a word. The overall counts apply that precedence across all findings, too. The words show use, not cause: a word that uses what a warning names may fail for some other reason.

Every finding has an attribution state. A supported route with no matching word says **none in this Selection**. A project resource without word attribution says **project-wide**. A **missing object** distinguishes a GUID absent from the checked Baseline from one belonging to a different FieldWorks class. **Unresolved identity** distinguishes no subject, a named subject without a project GUID, and a class with no supported route.

Features also follow shared references to their enclosing complex specifications. If some feature owners have no supported route or no word attribution, the finding retains those attribution limits alongside any exact uses or candidates. When no owner has a supported route, the finding reports the limit rather than claiming there are no words in the Selection.

Without a matching Assessment, supported routes say **evidence unavailable**; their counts are absent rather than zero. Missing and unresolved subjects retain their specific reasons. Motif refuses stores with an older shape and asks the developer to delete the database so it can recreate it.

## Related commands

- [Check a grammar](cmd:grammar%20check) records fresh findings.
- [Read the project Overview](cmd:overview) includes the warning count with other stored evidence.
