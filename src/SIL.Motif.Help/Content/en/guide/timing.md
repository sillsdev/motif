# Timing

**Timing** shows the measured total word time and how that time is divided among parser kinds, rules, and words. It reads timing saved by [Parse all words](term:parse-all-words); opening the page does not start a parse. If there is no timing yet, choose **Parse all words** first.

Under **Look at**, choose **Step limit**, **Slowest**, or **All**. Open **More word sources** to use words checked in Texts, a Matrix cell, a Texts list, or words you enter by hand. Every rule share is of the total word time; **Not attributed** is time the parser did not assign to a rule or lookup. **Overrun** is object time measured beyond total word time, shown separately without an assumed cause. Open **Show calls** to see each kind's recorded call count; a kind that does not count calls says **Not counted**.

Select a rule row to see its costliest words; open **Inspect** on the row to see its FieldWorks facts and link. A warning mark means the stored rule identity is structural, synthetic, or unavailable. In **Detailed statistics**, word time keeps one shading scale across the selected words. **By word** uses those same words; other groups show the complete parse and say so.

To measure selected words again, use the rerun controls to choose a per-word time or step limit, then choose **Re-run chosen words**. **Cancel** stops that rerun. A word stopped by the step limit needs a larger step limit; more time alone does not change that outcome. When the current totals include re-runs, Timing labels the number of separate measurements.

![Slowest words in Timing](shot:timing-slow-words/slowest-words)
