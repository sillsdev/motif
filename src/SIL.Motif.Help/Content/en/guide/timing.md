# Timing

**Timing** opens on **All** and shows recorded parse times for the current Selection. It reads timing saved by [Parse all words](term:parse-all-words); opening the page does not start a parse. If there is no timing for those words, choose **Parse again**. If **Step limit** or **Slowest** has no words, choose **Show all words** to return to the full Selection.

Under **Look at**, choose **Step limit**, **Slowest**, or **All**. Open **More word sources** to use words checked in Texts, a Matrix cell, a Texts list, or words you enter by hand. Every rule share is of the total word time; **Not attributed** is time the parser did not assign to a rule or lookup. **Overrun** is object time measured beyond total word time, shown separately without an assumed cause. Open **Show calls** to see each kind's recorded call count; a kind that does not count calls says **Not counted**.

The compact word row names PanGloss's outcome as **Same**, **Different**, **No parse**, or **Stopped**. The meaning is separate and hidden in this list; a matching morphology alone does not imply agreement with a Disapproved opinion.

Select a rule row to see its costliest words; open **Inspect** on the row to see its FieldWorks facts and link. A warning mark means the stored rule identity is structural, synthetic, or unavailable. The inspector separates words that use a morpheme in a stored analysis from words where the parser tried it. Its word and call counts come from that item’s recorded timers; if none were recorded, it says so. In **Detailed statistics**, word time keeps one shading scale across the selected words. **By word** uses those same words; other groups show the complete parse and say so.

To measure selected words again, set **Seconds per word** and **Step limit** in the controls at the top of Timing, then choose **Re-run words**. The step limit starts at **1,000,000**. **Cancel** stops that rerun. When you retry, raise **Step limit** after a step-limit stop or increase **Seconds per word** after a time-limit stop. When the current totals include re-runs, Timing labels the number of separate measurements. The **Slowest** list includes stopped searches when they have a recorded time. The Detailed statistics card says whether its slowest measured word finished or stopped; when completion is unavailable, it says **Slowest measured word**. Open **Detailed statistics** to load its rows; changing the group loads the new group.

The **Readings** count in Detailed statistics reports how many words produced more than one reading. That count does not tell you why. It may reflect different words with the same spelling or several analyses the grammar allows; inspect the words' readings to tell them apart. A rule's time share says how much time its timer recorded, not what caused a word to parse or stop.

![Slowest words in Timing](shot:timing-slow-words/slowest-words)
