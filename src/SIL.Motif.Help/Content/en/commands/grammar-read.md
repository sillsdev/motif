# Read grammar objects

`grammar read` reads one kind of authored project object from the current [Baseline](term:baseline). Choose `phoneme`, `natural_class`, `environment`, `part_of_speech`, `inflection_feature` or `lexical_entry` with `--kind`.

Use `--id` with an exact canonical id to inspect one object and its links, or `--query` to match names, forms, representations or sense glosses. The command returns the id of each object and relevant referenced ids, including natural-class members, parent categories, feature values, lexical senses, grammatical information and form environments. Lists are paged with `--offset` and `--limit`; the default page contains 25 objects and the maximum page contains 100.

The response identifies the Baseline and says whether FieldWorks has saved the project since it was captured. This reads the saved copy; it does not open or change the live FieldWorks project. Capture a Baseline before using this command.

```powershell
motif grammar read --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --kind lexical_entry --query "kat" --limit 10 --json
```
