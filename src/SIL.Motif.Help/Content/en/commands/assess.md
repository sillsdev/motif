# Measure a Selection

`assess` sends a Selection of words through PanGloss and stores the resulting Assessments. It uses the project's stored Baseline when one exists and captures one only when the project has none.

## When to use it

Use this to measure how a grammar parses the project's usual words, named Texts, every wordform, or a supplied word list. After FieldWorks saves grammar changes, capture a new Baseline before measuring them.

To parse some words again and update their main results, pass `--replaces <assessment-id>` with the id of the complete ParseTime Assessment they belong to. The replacement must use that same Baseline and contain only words in that Assessment. Each replaced word keeps the new run's id and measurement time; untouched words keep theirs. A run without `--replaces` records a separate measurement without replacing words in an existing run.

To select words whose recorded parser work exceeds a threshold, use `--retry-work-over <parser-work> --retry-source-assessment <id>`. Words without a recorded work count cannot match this selector.

## Example

```powershell
motif assess "C:\FieldWorks\Projects\Koro\Koro.fwdata" --all-wordforms --json
```

For an explicit reparse, save the chosen words in a file and name their original run:

```powershell
motif assess "C:\FieldWorks\Projects\Koro\Koro.fwdata" --words words.txt --replaces <assessment-id> --json
```

## What it prints

Human output reports the Selection and stored Assessment ids, followed by each word's comparison headline and any qualification on a second line. For example, when a completed search builds a Disapproved analysis but misses the word's undecided analysis, it prints “Rebuilt an analysis you Disapproved” and “Your undecided analysis wasn't built”.

Both human output and JSON begin with `counts`: how many words were measured, how many came out `analysed`, `noAnalysis`, `capped` (stopped at the work limit), `timedOut` (stopped at the time safety net) or `skipped`, and among the analysed words how many are `accepted` (some reading identifies every morph in the lexicon) and how many are `guessOnly` (every reading contains a morph the parser had to guess because it could not identify it, such as `a+?baga`). An analysed word is not necessarily an accepted one.

JSON returns the Baseline, Selection, and Assessment results. Each word's `comparison` carries its standing, outcome, stable `meaningCode`, headline, detail, completion flag and tone, plus matched analysis identities and their individual opinions, missing Approved analyses, rebuilt Disapproved analyses and extra readings. This is the same comparison used by the window; display wording is not a grouping identity.

The comparison's `availability` distinguishes `Available` (analysis identities were compared), `RecordedGradesOnly` (recorded reading grades are known, but matched analysis identities are unknown), and `Unavailable` (no morphology comparison was recorded or reconstructed). Unavailable evidence is not a known empty set. An incomplete search makes no claim that an Approved or undecided analysis was not built. No grammar verdict is inferred from the measurements.

## Limits

Assess and Trial use the Default Selection's per-word parser work limit and time policy, including for a supplied word list. A Trial's automatic before Assessment uses the exact limits frozen for the Trial. A stop at the work limit is the same on every computer. A stop at the time safety net varies with machine load, so compare `capped` and `timedOut` separately, and inspect the recorded limits before comparing independent runs. In JSON each stopped word's `stoppedBy` is `work-limit`, `time-safety-net`, or `both` when the work limit and the time safety net both fired inside that word's search; it is read from the word's outcome and the parser's evidence together, so a word that returned readings before a limit fired still names it. A word that was not stopped has no `stoppedBy`. A stopped word is not evidence that no analysis exists.

When object statistics are available, Motif keeps whole-run timing totals and per-object detail for the 2,000 words with the most parser work by default. If parser work are unavailable, it uses time on this computer. A named scope can set `timing-detail-words` in `<project>.motif.toml` to a number from 0 to 10,000. Results identify when word-level detail is incomplete. To keep detail for every word in an explicitly supplied Selection of at most 2,000 words, pass `--full-object-timing` with the Selection. A retry records object timings from the initial pass; it does not supply object timings for the retried result.

Each Assessment has a whole-run time limit, including retries; it is a time on this computer, not a measure of parser work. The project Parsing setting defaults to 3,600 seconds; `--max-seconds <seconds>` overrides it for this run, with a minimum of 60 seconds. If the limit is reached, Motif stops the parser, reports the number of words done and not done, records a `stopped-by-budget` job, and saves no Assessment. The command exits with code 5.

## A large list

`--words <file>` reads a UTF-8 file with one word per line, so a list of thousands of words needs no other preparation. The full JSON result grows with the list. Add `--summary` to print only the counts and one page of words, each with its outcome, whether its reading was accepted or guess-only, its `parserWork` when the parser counted them (a skipped word has none) and its `stoppedBy` when a limit stopped it; `--offset <n>` and `--limit <n>` choose the page (default 25 words from the start), and the output says which words it shows, such as “showing 1-25 of 7206”. The Assessment is stored whole either way.

An agent reads the same summary through the `motif_assess` tool. It returns the counts, one page of words and the stored Assessment id, and pages through that Assessment with `assessment`, `offset` and `limit` rather than measuring again. `wordsFile` names the word file as `--words` does, and giving both `words` and `wordsFile` is refused with `assess.words-ambiguous`. A file that is missing or unreadable is refused with `assess.words-file-unreadable`, and one with no words with `assess.words-file-empty`.

Word files must be at most 8 MiB of valid UTF-8 text without NUL bytes; an initial UTF-8 signature is accepted. A larger file is refused with `assess.words-file-too-large`; invalid UTF-8 or NUL bytes with `assess.words-file-not-text`. Split a large list into smaller files, or save a non-text file as UTF-8 with one word per line, then repeat the call.

## Related commands

- [Show the Default Selection](cmd:selection%20show) reads the usual word list.
- [Read the project Overview](cmd:overview) displays evidence already stored.
