# Try a Word

Use **Try a Word** to trace one word through the grammar. The word does not need to appear in a chosen text or exist in the FieldWorks project.

1. Enter a word in **Type any word...**.
2. Choose **Try it** and wait for the trace. Choose **Cancel** to interrupt a running search.
3. Read the result and any search limit. **Search incomplete** takes precedence even if the parser found an analysis; an unfinished search cannot establish that no analysis exists.
4. Read the analysis summaries, then **Recorded trace**. Each summary groups analyses with proven equal morphology. **Recorded twice** counts source records; it does not establish two different derivations.
5. Expand a recorded event and select it to read its input, output, reason code and rejection details. The small notation column keeps the parser's recorded event type and outcome visible beside the readable label. **Tried** is different from **Applied**. **Producer:** names the source recorded by the parser; a different captured project name is labelled **Captured FieldWorks:**. Saving and reopening retain both.

Choose **Plain** or **Expert** beside **Try it**. The installed window remembers your choice. Switching keeps the same recorded attempt, selected event address and open inspector; it does not run the parser again. Addresses identify occurrences within this saved trace, not across newly generated traces.

**Expert** keeps event names, subrule indices and input/output shapes in the parser's order, including both recorded passes. Choose an attempt and turn off **Whole tree** to read its recorded ancestors and terminal event. Whole-tree rows include other search context; neighboring events alone never establish attempt membership or a cause. **Phonological rules applied** lists recorded phonological events in the chosen view, with each event's own outcome. **Tried** does not mean **Applied**, and an unchanged shape does not prove application.

Select an event to see **Readable** beside **Parser record**. The record preserves unknown producer fields and omits its children from this detail view; saving still keeps the whole diagnostic. Tokens explain themselves on hover or keyboard focus. **Environment notation** keeps the recorded diagnostic operand intact and shows **Authored environment notation unavailable**. Decoding requires the trace to explicitly identify authored environment text; the supported trace formats do not establish it. Strings, structured operands and punctuation such as `/`, `_`, `#`, brackets, parentheses and `?` cannot establish that language by themselves, so they remain raw.

**Reason not recorded**, **Rejection details not recorded**, **Grammar source not recorded**, and **FieldWorks word context unavailable** identify facts the page cannot establish. Missing word context does not mean the word is absent from FieldWorks. The recorded tree shows search context; neighboring events alone do not establish which rule caused an attempt to fail.

**FieldWorks beside the parser** shows every stored analysis for the returned word, with each recorded opinion. Its source line names the captured FieldWorks save; a later-save warning belongs to that context, not to the traced grammar. A word absent from that Baseline differs from a wordform with no stored analyses. Unknown membership and unavailable context remain explicit.

**No terminal attempt was recorded before the search stopped** marks an interrupted search without a terminal attempt. Its recorded progress remains available in the tree. Recorded terminal attempts show only their returned reasons and attribution. **Steps on this path** lists actual ancestors and the terminal event; **Recorded tree context** shows nearby events whose membership is not established. Event addresses identify repeated occurrences within this saved trace, and the small notation names the recorded pass and outcome.

An earlier parse's measured times appear in a separate block named by the word's recorded measurement time. The block stays hidden when that source time was not recorded. They cover that parse's whole search for the word, including stopped attempts. They are not times for a step in this trace. Shares use that measurement's word time; **Other time** and any timer overrun appear only when the measurement recorded them.

Hover over the result or use keyboard focus to reveal **Analyze texts**, **Timing**, and **AI Handoff**. **Analyze texts** opens the returned word in its text, where opinions are changed. These actions keep addressing the displayed result when you edit or clear the input for another Try. **AI Handoff** keeps this diagnostic and the Baseline recorded with it; it does not parse the word again or make a replacement trace. The menu beside **Try it** contains recent words and tools for copying, saving or opening a trace. **Copy for a chat model** includes the instructions, one-line summary, and original diagnostic JSON. **Full derivation** starts folded and provides all recorded steps and filters. Saving keeps the original diagnostic regardless of display filters.

Capture details name the Baseline that actually produced the trace, including its capture time and the source project's save time. A Refresh published while a request waits can change which Baseline it uses. An older workspace description cannot relabel the returned evidence.

Recorded details can include the replacement lexical entry for a Blocked event, the template slots a branch applied or skipped, and the completion gate that ended a partial attempt. A completed lookup records how many possible roots it returned; those are not counts of successful analyses. Typed rejection details retain the operands PanGloss actually tested, including any authored environment identity and text. A phonological step with an unavailable reason remains unknown.

A Blocked event records an intermediate result. In the supported parser it means replacement by a compatible entry in the same lexical family; search can continue. Missing reasons, rejection details and grammar sources are shown as not recorded, and unfamiliar reason codes keep their raw notation with an unavailable explanation.


![A word parsed in Try a Word](shot:try-a-word-parses/parsed-word)

The main analysis cards combine source records only when available projections establish equal ordered allomorph and grammatical information identities, including inflection type. Equal spellings alone do not establish that they are the same analysis. The recorded source list keeps every producer record.
