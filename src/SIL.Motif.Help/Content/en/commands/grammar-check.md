# Check a grammar

`grammar check` runs the grammar checker against the project's current Baseline and stores its findings. If the project has no Baseline, the command refuses the request; run `motif baseline capture --project <fwdata>` and try again.

## When to use it

Use this to collect diagnostics about the grammar, then read them with `warnings`. It is separate from an Assessment, which measures parsing over a Selection.

## Example

```powershell
motif grammar check --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## How much of the comparison runs

One part of the check re-parses each word to see whether its stored analyses still parse, so it is the part that slows down as a project grows. `--stored-analyses` chooses how much of it runs, for this check only:

- `budget` compares words in order until a fixed amount of work is spent, then reports how many words were left. This is the default.
- `all` compares every word. A large project can take half a minute or more.
- `off` skips the comparison; the checks that need no parsing still run.

Without the flag, the check follows the **Compare stored analyses** choice under *Default proposal checks* in Settings. The same choice applies to the grammar check a Trial runs for a Proposal, so a Proposal's check and the project's are comparable only when they used the same choice.

## What it prints

The response includes the Baseline's lexical entry and rule counts when that inventory is available. Motif advisories are separate from PanGloss findings, so an empty-lexicon note does not change parser severity or Assessment readiness. Error-level findings appear separately from warnings and information. PanGloss can exit nonzero after writing a report with errors; Motif still reads and stores that report. A nonzero exit without a report is a refusal.

Motif also warns when a required inflectional slot in a template has no affixes. The finding names the template, part of speech and slot, and includes the slot's canonical ID. The slot is marked required, but FieldWorks and PanGloss currently treat an empty required slot as optional. Add affixes to express the intended requirement, or mark the slot optional when empty is allowed.

Motif also warns, with code `grammar.negated-feature-value`, about each stored negated feature value (a feature set to "not" a value). FieldWorks' parser loader throws when it reads one, so FieldWorks cannot parse with the project, while PanGloss parses on and leaves the value out of the stem's features, so what carries it is not held back by it. The finding names the feature, the value and the entry or object that holds it. On a stem or an inflectional affix, [remove the feature value](cmd:compose-remove-feature-value) (it removes a negated one like a closed one), then author a closed value if one is wanted. Motif has no command for a negated value in a phoneme, natural class or part of speech, and whether FieldWorks can clear one was not checked.

Motif also warns, with code `grammar.derived-stem-keeps-from-class`, about each derivational affix that takes a From Inflection Class and names no To Inflection Class: the stems it derives keep the From class, so an inflectional affix that attaches only to other classes finds no stem. The finding names the affix and the class. Set the class the derived stem should take with [Restrict a derivational affix](cmd:compose-edit-derivational-affix-restriction) (`toInflectionClass`), or leave it when the derived stem does keep its class.

Motif also warns, with code `grammar.template-has-no-slots`, about each affix template that has no slot. LibLCM can hold one, so Motif authors it without refusing, but it holds no affix position and adds nothing the parser can use. The finding names the template and its part of speech. Add a slot with [Add an affix slot](cmd:compose-author-affix-slot) and put the template's affixes in it; a slot that may be empty is an optional slot.

Motif also warns, with code `grammar.phoneme-spelling-ambiguous`, about each phoneme whose spelling is the spellings of two or more other phonemes joined, as `ng` beside `n` and `g`. The parser reads letters as the longest spelling at each point and keeps one reading, so `ng` is always the one phoneme and a word where `n` and `g` meet across a morpheme boundary is never read as those two. A digraph whose parts are not phonemes (`sh` with no `s` or `h`) is not flagged. The finding names the phoneme and the spellings it is made of. Write the affected form of the affix or stem as an allomorph spelled with the digraph, or change the orthography to remove the ambiguity (Black, section 6.3.3). Do not remove the phoneme: every rule that uses it would break. Every phoneme set is checked. The spelling may be intended, as `aa` beside `a`, and then nothing needs changing.

Motif also warns, with code `grammar.allomorph-environment-ignored-by-parser`, about each stem or affix form that names an environment the parser cannot read, as one written with a morpheme boundary `+`. The parser drops such an environment, so the form is not restricted by it and may appear anywhere. The check uses the same recognizer as FieldWorks' parser loader over the project's phonemes and natural classes. PanGloss drops the environment too, but its own grammar-health report does not list it. Entries marked do-not-use-for-parsing are skipped. The finding names the form and the environment. Rewrite the environment with phonemes and natural classes only. A restriction that needs a morpheme boundary can be stated as a phonological rule with a boundary context ([Author a phonological rule](cmd:compose-author-phonological-rule)); Motif has not checked that this gives the same readings as the environment, so try it in a Trial.

Motif also warns, with code `grammar.allomorph-repeats-environment-material`, about each stem or affix form that already ends with the literal material to the right of the slot in every environment it names (or starts with the material to its left), as `batan-ung` before the linker `ng`, where the form meant to stop before the linker was `batan-u`. It refuses nothing: an affix such as Tagalog `pang` before `ng` is correct, because the environment restricts the neighbour. Natural classes, boundaries and optional parts are ignored. The finding names the form and the environment.

Motif also reports, with code `fieldworks.do-not-parse-ignored` and level information, each entry marked do-not-parse. FieldWorks' parsers do not read the flag, so FieldWorks parses such an entry; the PanGloss Motif pins today leaves it out, and the next PanGloss release parses it too. The finding names the entry. To stop a stem parsing, retype it with `compose-set-stem-part-of-speech` or delete it.

Motif also reports lexicon hygiene as warnings, with three codes. It only reports; it changes nothing, and it includes entries marked do-not-parse, which FieldWorks still parses. `lexicon.stem-typed-as-content-word` names each stem whose part of speech is a Noun, Verb or Adjective (or a sub-category of one) and whose form is a single letter or whose gloss begins with `=`, `see:`, `see also` or `see under` (a cross-reference stub), begins with `letter`, or names a function word (`marker`, `particle`, `interjection`, `conjunction`, `preposition`, `determiner`, `linker`, `pause word`). Such a stem takes that category's inflection and derivation, so it lets short unrelated readings parse. `lexicon.duplicate-entry` names entries that share a lexeme form, a first gloss, a morph type and a category. `lexicon.affix-hyphen-on-wrong-side` names a prefix, proclitic or prefixing-interfix form written `-i` and a suffix, enclitic or suffixing-interfix form written `i-`; the hyphen belongs on the side that touches the stem. When there are more than 20 findings of one kind, one finding gives the total and a sample of five. To fix the first kind in bulk, put `compose-set-stem-part-of-speech` intents in one [`compose-batch`](cmd:compose-batch) file.

## Related commands

- [Read grammar warnings](cmd:warnings) filters and displays stored findings.
- [Measure a Selection](cmd:assess) measures parser behavior rather than grammar diagnostics.
