# Read grammar objects

`grammar read` reads one kind of authored project object from the current [Baseline](term:baseline). Choose `phoneme`, `natural_class`, `environment`, `part_of_speech`, `inflection_feature`, `lexical_entry` or `phonological_rule` with `--kind`. A `phonological_rule` read returns each rule's `id`, `disabled` flag, `rightHandSideCount` and `intent`, the whole intent that [Edit a sound rule](cmd:compose-edit-phonological-rule) copies into `expected`; read it with `--json`. To change the order of rules, use [Reorder sound rules](cmd:compose-order-phonological-rules) with the ids read here.

Use `--id` with an exact canonical id to inspect one object and its links, `--exact` to fetch the object whose name or form equals the text, or `--query` to match names, forms, representations or sense glosses that merely contain it. `--exact ka` returns the entry `ka` and not every entry containing "ka". Equality ignores case and Unicode normalization form. A bare form also equals an affix whose headword carries a marker, so `--exact zi` returns the stem `zi` and the prefix `zi-`, while `--exact zi-` returns only the prefix. For lexical entries, `--morph-type prefix` (names separated by commas, or morph type ids) keeps entries whose lexeme form has that morph type, so `--exact zi --morph-type prefix` returns only the prefix. `--id` stands alone; `--exact` and `--query` are alternatives. The command returns the id of each object and relevant referenced ids, including natural-class members, parent categories, feature values, lexical senses, grammatical information and form environments. Lists are paged with `--offset` and `--limit`; the default page contains 25 objects and the maximum page contains 100.

The response identifies the Baseline and says whether FieldWorks has saved the project since it was captured. This reads the saved copy; it does not open or change the live FieldWorks project. Capture a Baseline before using this command.

```powershell
motif grammar read --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --kind lexical_entry --query "kat" --limit 10 --json
```
