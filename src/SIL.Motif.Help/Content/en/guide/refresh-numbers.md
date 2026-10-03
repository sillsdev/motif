# Refresh the project

Use **Refresh** after saving changes in FieldWorks when you want Motif to read that saved state. Refresh captures a new [Baseline](term:baseline) and checks the grammar for **Warnings**. It does not parse words. Choose [Parse all words](term:parse-all-words) after Refresh to measure the current [Default Selection](term:default-selection).

1. Save your work in FieldWorks.
2. In Motif, choose **Refresh** in the top bar and let it finish.
3. If setup opens after the first Baseline, choose what to measure and press **Start first run**. Otherwise, choose **Parse all words** in the top bar.
4. Check **Overview**. Refresh updates the project counts and checks the grammar; parsing updates the parse results.

If you cancel **Parse all words** or the parser refuses it, Refresh's new Baseline remains; parse results do not describe it until a parse completes. Opening Motif again does not perform a Refresh for you.

![Refresh captures a Baseline before a separate parse updates the numbers](shot:review-apply-refresh-parse/refreshed)
