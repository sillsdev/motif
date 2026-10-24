# Trial a Proposal

Measures a Draft or finalized Proposal on a throwaway copy, producing a Dry Run and Assessments. Read negatives now parsing, positives lost, unfinished cases, other changes and counts before revising.

While a Motif update is installing, the Trial is refused with `trial.update-in-progress` and exit code 3; try it again in a moment.

Name the words to try inline, or in a UTF-8 file with one word per line: `--words <file>` on the command line, `wordsFile` on the `motif_trial` tool. On the tool, giving both `words` and `wordsFile` is refused with `assess.words-ambiguous`; a file that is missing, unreadable or empty is refused with `assess.words-file-unreadable` or `assess.words-file-empty`.

Word files must be at most 8 MiB of valid UTF-8 text without NUL bytes; an initial UTF-8 signature is accepted. A larger file is refused with `assess.words-file-too-large`; invalid UTF-8 or NUL bytes with `assess.words-file-not-text`. Split a large list into smaller files, or save a non-text file as UTF-8 with one word per line, then repeat the call.
