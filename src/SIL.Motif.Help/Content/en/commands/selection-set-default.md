# Choose a Default Selection

`selection set-default` saves which project Texts and added words Motif measures when a command does not name another Selection. It also saves the Selection's step cap and estimated or explicit time policy.

## When to use it

Use this to focus repeatable Assessments on a linguist-chosen set of material. Texts are identified by GUID, not by title, and added words can be included even when they do not occur in a Text.

## Example

```powershell
motif selection set-default --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --name "Verb examples" --texts <textGuid> --add-words "mala,ma" --step-cap 200000 --time-mode estimated
```

Use `--time-mode explicit --time-limit-ms <positive>` for a chosen time safety net, in milliseconds on this computer. `--time-mode estimated` rejects `--time-limit-ms`; `--step-cap none` stores no step or time cap. To replace an existing Selection, first read its revision with [selection show](cmd:selection%20show) and pass it as `--expected-revision`.

## A word list with counts

`--words-file <path>` takes the added words from a UTF-8 file, in place of `--add-words`. Each line is a word, or a word, a tab and a whole-number count. There is no header line, and the file is at most 8 MiB.

- A plain list of words gives no counts. A word with no count has a count that is not known, which is not the same as zero.
- Words are stored in NFD. A word on several lines has its counts added up.
- Motif stores the file's name and its SHA-256 with the Selection, so you can tell which list the counts came from.
- A later save that names no file keeps the stored list when the Texts and added words are the same, and any other change to the Texts or words drops it.
- A Selection of chosen Texts ranks its words by places in those Texts. A Selection with no Texts ranks them by the file's counts, and the Overview then names its bands By Frequency Band, never Text Coverage.

```powershell
motif selection set-default --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --name "Frequent words" --words-file .\frequencies.tsv
```

## What it prints

The command reports the saved Selection name and its settings. Add `--json` to receive the structured Selection response.

## Related commands

- [Show the Default Selection](cmd:selection%20show) reads what was saved.
- [List project Texts](cmd:texts%20list) finds Text GUIDs to select.
- [Set Selection limits](cmd:selection%20set-limits) changes limits without replacing the word list.
