# Find lexical entries

`lexicon find` lists lexical entries from the current [Baseline](term:baseline) of a project, ordered by headword, with the same filters as the `motif_lexicon` tool plus the form filters a scan of the project file used to supply. Each entry carries its canonical id, headword, morph type, lexeme form, alternate forms, inflection classes, categories and senses. `--json` returns the page with facet counts over the whole matched set; without it each entry is one tab-separated line (id, headword, morph type, forms, inflection classes, categories, first gloss), and the lines `# Showing ...` and `# Next: ...` follow.

Filters combine, and every text comparison ignores case and Unicode normalization form except `--form-regex`:

- `--form-regex <regex>` keeps entries where the headword, the lexeme form or an alternate form matches the regular expression, which uses .NET regex syntax (named groups are `(?<name>...)`, not `(?P<name>...)`), searched in NFC text; anchor with `^` and `$` for a whole form. A pattern that is invalid or takes over two seconds on one form is refused with `lexicon-find.invalid-request`.
- `--suffix <text>` keeps entries where some form or the headword ends with the text.
- `--class <name|id>` keeps entries with an analysis in that inflection class, by name, abbreviation, canonical id or guid. A class also matches through its parent, so naming a parent finds the stems of its subclasses.
- `--environment <text>` keeps entries where some allomorph has an environment whose text contains it (for example `_ a`); `--stem-name <name|id>` keeps entries where a stem allomorph carries that stem name.
- `--used-by <name|id>` keeps entries whose inflectional analysis fills an affix slot with that name or id, or that a template lists; naming a template finds every entry filling one of its slots. A name shared by more than one slot or template is refused with `lexicon.used-by-ambiguous`, which lists each candidate's id and kind; pass an id. Phonological and compound rules do not reference an entry, so they are not searched.
- `--detail` adds to each entry `allomorphs` (the lexeme form then each alternate: id, kind, morph type, text in every writing system, stem name and environments) and `usedBy` (the slots and templates it fills). The tab-separated lines are unchanged; use `--json`.
- `--query`, `--exact`, `--morph-type`, `--affix`, `--category`, `--headword`, `--headword-contains` and `--has-gloss` read as in [Read grammar objects](cmd:grammar%20read). `--affix` and `--has-gloss` take `true` or `false`; alone they mean true.
- `--at baseline|<proposalId>` reads the grammar a Proposal's latest Trial ran against.

`--id-format canonical|guid` spells every id in the result as a canonical id (the default, the form a Proposal's intent names) or as a hyphenated guid (the form the project file stores). Both spellings are accepted by `--class` and `--category`.

Pages hold 25 entries by default and at most 200; page with `--cursor` and `--limit`. `nextCursor` in the JSON result and the `# Next:` line name the next page.

For many small questions, give `--batch <file|->`: one JSON object per line, keyed by the flag names (`"form-regex"`, `"suffix"`, `"class"`, `"limit"`, ...). The Baseline is opened once and each line is answered, in order, by one JSON line holding `query` and either `result` or `refusal`; a line that is not a JSON object or names an unknown key gets its own refusal and the batch goes on. `--batch -` reads standard input as UTF-8. Opening the Baseline is the cost of a call, so a batch of 200 questions costs about one.

```powershell
motif lexicon find --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --form-regex "^ka.*an$" --class "Class 1" --id-format guid --json
```
