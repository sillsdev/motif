# Find lexical entries

`lexicon find` lists lexical entries from the current [Baseline](term:baseline) of a project, ordered by headword, with the same filters as the `motif_lexicon` tool plus the form filters a scan of the project file used to supply. Each entry carries its canonical id, headword, morph type, lexeme form, alternate forms, inflection classes, categories and senses. `--json` returns the page with facet counts over the whole matched set; without it each entry is one tab-separated line (id, headword, morph type, forms, inflection classes, categories, first gloss), and the lines `# Showing ...` and `# Next: ...` follow.

Filters combine, and every text comparison ignores case and Unicode normalization form except `--form-regex`:

- `--form-regex <regex>` keeps entries where the headword, the lexeme form or an alternate form matches the regular expression, searched in NFC text; anchor with `^` and `$` for a whole form. A pattern that is invalid or takes over two seconds on one form is refused with `lexicon-find.invalid-request`.
- `--suffix <text>` keeps entries where some form or the headword ends with the text.
- `--class <name|id>` keeps entries with an analysis in that inflection class, by name, abbreviation, canonical id or guid. A class also matches through its parent, so naming a parent finds the stems of its subclasses.
- `--query`, `--exact`, `--morph-type`, `--affix`, `--category`, `--headword`, `--headword-contains` and `--has-gloss` read as in [Read grammar objects](cmd:grammar%20read). `--affix` and `--has-gloss` take `true` or `false`; alone they mean true.
- `--at baseline|<proposalId>` reads the grammar a Proposal's latest Trial ran against.

`--id-format canonical|guid` spells every id in the result as a canonical id (the default, the form a Proposal's intent names) or as a hyphenated guid (the form the project file stores). Both spellings are accepted by `--class` and `--category`.

Pages hold 25 entries by default and at most 200; page with `--offset`, `--page` or `--limit`, or give `--all` for every match. `nextOffset` in the JSON result and the `# Next:` line name the next page.

For many small questions, give `--batch <file|->`: one JSON object per line, keyed by the flag names (`"form-regex"`, `"suffix"`, `"class"`, `"limit"`, ...). The Baseline is opened once and each line is answered by one JSON line holding `query` and either `result` or `refusal`. Opening the Baseline is the cost of a call, so a batch of 200 questions costs about one.

```powershell
motif lexicon find --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --form-regex "^ka.*an$" --class "Class 1" --id-format guid --json
```
