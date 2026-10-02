# Try a Word

Use **Try a Word** to trace one word through the grammar in the current [Baseline](term:baseline). The word does not need to appear in a chosen text or even exist in the FieldWorks project.

1. Enter a word in **Type any word...**.
2. Choose **Try it** and wait for the trace. You can choose **Cancel** while it is running.
3. Read whether the word parsed, the project’s expected analyses, and the parser’s furthest attempt. The trace lists recorded rule steps and terminal reasons. Nearby rejections appear as tree context; they do not establish what caused a attempt to stop. A word that parsed leads with its analyses, each shown once with the number of times it was recorded. The other paths the parser tried and dropped wait folded under their count, because dropping them is normal. The rules read in building order, outward from the stem, each with the affix’s own form, such as **ma- · tin → matin**.
4. Use **Open in Texts** to inspect a project word there, or follow a timing link when one is available.

Capture details name the Baseline that actually produced the trace, including its capture time and the source project's save time. A Refresh published while a request waits can change which Baseline it uses. An older workspace description cannot relabel the returned evidence.

A Blocked event records an intermediate result. In the supported parser it means replacement by a compatible entry in the same lexical family; search can continue. Missing reasons, rejection details and grammar sources are shown as not recorded, and unfamiliar reason codes keep their raw notation with an unavailable explanation.

The result distinguishes a completed search with no parse from a search that did not complete; a limit or interrupted search is not evidence that the grammar cannot parse the word. For a detailed trace file, use the diagnostic tools available from this page. A diagnostic can show real word forms and morphology, so inspect it before sharing.

![A word parsed in Try a Word](shot:try-a-word-parses/parsed-word)

The main analysis cards combine source records only when available projections establish equal ordered allomorph and grammatical information identities, including inflection type. Equal spellings alone do not establish that they are the same analysis. The recorded source list keeps every producer record.
