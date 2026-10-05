# Read grammar warnings

`warnings` reads grammar-check findings and allomorph refusals already recorded by parsing for a project. It can filter by diagnostic code or show only findings at warning level.

PanGloss reports an unreadable phonological environment as a warning. Roots ignore that restriction but keep valid ones; ordinary affixes add an unrestricted pass beside valid passes; an infix with no valid position is skipped.

## When to use it

Use it after [Check a grammar](cmd:grammar%20check) when you want to inspect reported findings. Combine `--kind` and `--left-out` to narrow a large result without rerunning the check.

## Example

```powershell
motif warnings --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --left-out --json
```

## What it prints

The response includes separate error, warning, and information counts, plus findings with PanGloss-owned titles, explanations, guidance, optional background, verified FieldWorks places, subjects, and links where available. Each finding retains the report locale. Missing references and project settings have explicit statuses and unavailable-link reasons. `--left-out` keeps warning-level findings only. With no Baseline, the command says to capture one; when a Baseline has no stored check, it says the grammar has not been checked yet.

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
