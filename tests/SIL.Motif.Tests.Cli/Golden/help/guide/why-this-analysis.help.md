# Why This Analysis?

**Why This Analysis?** is a grey fold in the word panel on **Texts**. It is visible only when **View → Diagnostics** is on. With Diagnostics off the word panel is unchanged and Motif does no diagnostic work.

The fold starts folded. Open it to ask the parser for a trace of the selected word, against the Baseline the window is showing. Motif does not run the parser until you open the fold. Folding it, selecting another word, switching projects, refreshing to a different Baseline or turning Diagnostics off cancels the request and discards what it returned. A trace for another word or another Baseline is never shown.

## What the fold shows

- **Analyses**: the recorded analyses, each with its morphemes. Each name has an **Open in FieldWorks** link when the trace recorded a verified FieldWorks target. Motif shows names only; rules and entries are read and edited in FieldWorks.
- **Rules on the best path**: each rule's name, with an **Open in FieldWorks** link. The fold shows no rule definitions.
- **Failed branches**: the recorded attempts that did not parse, with where each stopped. The first 20 are listed and the rest are counted.
- **Parser work**: counted in steps, never in milliseconds.

When the search stopped at its work limit or its safety net, the fold says so and shows **at least** the failed branches it recorded. An incomplete or missing trace never means that there were no failed branches, and a missing count is not zero.

## Full Trace

Choose **Full Trace** to read every recorded event inside the word panel. It never opens a page or a new window. **Copy Trace** copies the diagnostic exactly as recorded; review it before sharing, because it can contain real project data. Each event shows its address, which identifies it within this saved trace only.

A **Tried** rule was not necessarily applied, and **Blocked** names a recorded event without naming an unrecorded blocker. A reason PanGloss did not record is shown as not recorded.

![Diagnostics for a project](shot:try-word-typing/trace)

To trace a word saved earlier or to run a trace from the command line, see the [trace command](cmd:trace).
