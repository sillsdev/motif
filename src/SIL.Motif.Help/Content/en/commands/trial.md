# Trial a Proposal

Measures a Draft or finalized Proposal on a throwaway copy, producing a Dry Run and Assessments. Read negatives now parsing, positives lost, unfinished cases, other changes and counts before revising.

While a Motif update is installing, the Trial is refused with `trial.update-in-progress` and exit code 3; try it again in a moment.

Name the words to try inline, or in a UTF-8 file with one word per line: `--words <file>` on the command line, `wordsFile` on the `motif_trial` tool. On the tool, giving both `words` and `wordsFile` is refused with `assess.words-ambiguous`; a file that is missing, unreadable or empty is refused with `assess.words-file-unreadable` or `assess.words-file-empty`.
