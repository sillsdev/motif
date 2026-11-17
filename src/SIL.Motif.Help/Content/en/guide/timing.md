# Speed by Rule

Turn on **View → Diagnostics**, open **Diagnostics**, and choose **Speed by Rule**. This tab reads recorded rule work for the current parse and its contributing reruns. Opening it never starts a parse.

Each count is the rule's recorded parser steps divided by the number of words that recorded parser work. The denominator states both the measured-word count and the requested word count. Motif does not turn milliseconds or statistics call counts into parser steps. Missing work or a missing denominator stays unavailable. The unattributed remainder is retained.

Complete aggregate totals may remain available when individual-word detail was retained for only part of the run. The tab labels that limit. If aggregates do not cover the selected words, the tab does not show them as complete totals. Outdated or mismatched parse results are discarded.

When Motif cannot attribute parser work to objects, the [Timing command](cmd:timing) reports `parserWorkByObjectAvailable: false` and a `parserWorkByObjectAbsentReason`, one of: `retried-words` (some words were retried), `word-without-work` (a counted word recorded no work), `object-without-work-or-identity` (an object total has no work or no usable identity), `no-unattributed-row` (the unattributed remainder is missing), `sums-do-not-match` (the objects do not add up to the words' work), `not-whole-run` (the words are not the whole run) or `statistics-unavailable` (the run kept no statistics). Raw counts are never printed.

Select a rule to resolve its exact **Open in FieldWorks** destination. An unresolved or synthetic identity has no inferred link. Motif displays no rule definition or editing form. Use FieldWorks to inspect or edit the rule, then measure again to compare its recorded work. No Fast or Slow classification is inferred from these counts.

The [Timing command](cmd:timing) remains available independently of this window tab. See [Diagnostics](guide:diagnostics) for warning and Proposal evidence.
