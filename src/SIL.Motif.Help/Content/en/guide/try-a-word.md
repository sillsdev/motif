# Try a Word

Use **Try a Word** to trace one word through the grammar. The word does not need to appear in a chosen text or exist in the FieldWorks project.

1. Enter a word in **Type any word...**.
2. Choose **Try it** and wait for the trace. Choose **Cancel** to interrupt a running search.
3. Read the result and any search limit. **Search incomplete** takes precedence even if the parser found an analysis; an unfinished search cannot establish that no analysis exists.
4. Read the analysis summaries, then **Recorded trace**. Each summary groups analyses with proven equal morphology. **Recorded twice** counts source records; it does not establish two different derivations.
5. Expand a recorded event and select it to read its input, output, reason code and rejection details. The small notation column keeps the parser's recorded event type and outcome visible beside the readable label. **Tried** is different from **Applied**. **Producer:** names the source recorded by the parser; a different captured project name is labelled **Captured FieldWorks:**. Saving and reopening retain both.

**Reason not recorded**, **Rejection details not recorded**, **Grammar source not recorded**, and **FieldWorks word context unavailable** identify facts the page cannot establish. Missing word context does not mean the word is absent from FieldWorks. The recorded tree shows search context; neighboring events alone do not establish which rule caused an attempt to fail.

**FieldWorks beside the parser** shows every stored analysis for the returned word, with each recorded opinion. Its source line names the captured FieldWorks save; a later-save warning belongs to that context, not to the traced grammar. A word absent from that Baseline differs from a wordform with no stored analyses. Unknown membership and unavailable context remain explicit.

**No terminal attempt was recorded before the search stopped** marks an interrupted search without a terminal attempt. Its recorded progress remains available in the tree. Recorded terminal attempts show only their returned reasons and attribution. **Steps on this path** lists actual ancestors and the terminal event; **Recorded tree context** shows nearby events whose membership is not established. Event addresses identify repeated occurrences within this saved trace, and the small notation names the recorded pass and outcome.

An earlier parse's measured times appear in a separate block named by the word's recorded measurement time. The block stays hidden when that source time was not recorded. They cover that parse's whole search for the word, including stopped attempts. They are not times for a step in this trace. Shares use that measurement's word time; **Other time** and any timer overrun appear only when the measurement recorded them.

Hover over the result or use keyboard focus to reveal **Analyze texts**, **Timing**, and **AI Handoff**. **Analyze texts** opens the returned word in its text, where opinions are changed. These actions keep addressing the displayed result when you edit or clear the input for another Try. The menu beside **Try it** contains recent words and tools for copying, saving or opening a trace. **Full derivation** starts folded and provides all recorded steps and filters. Saving keeps the original diagnostic regardless of display filters.

Capture details name the Baseline that actually produced the trace, including its capture time and the source project's save time. A Refresh published while a request waits can change which Baseline it uses. An older workspace description cannot relabel the returned evidence.

A Blocked event records an intermediate result. In the supported parser it means replacement by a compatible entry in the same lexical family; search can continue. Missing reasons, rejection details and grammar sources are shown as not recorded, and unfamiliar reason codes keep their raw notation with an unavailable explanation.


![A word parsed in Try a Word](shot:try-a-word-parses/parsed-word)

The main analysis cards combine source records only when available projections establish equal ordered allomorph and grammatical information identities, including inflection type. Equal spellings alone do not establish that they are the same analysis. The recorded source list keeps every producer record.
