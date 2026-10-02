# Reading why a word fails

**Try a Word** records the parser's search for one word. Enter it in **Type any word...**, choose **Try it**, and read the result. The word can be new; it does not have to occur in the project's texts or lexicon.

Start with the result and search status:

- **Parsed** means a completed search found an analysis.
- **No parse** with **Search complete** means the search finished without finding an analysis.
- **Search incomplete** means the search did not finish. Read its recorded stop reason; an unfinished search is not evidence that no analysis exists, even when the trace contains no analysis.
- **Nothing to parse** means the grammar's character table does not define a character in the word.

Read any **Analysis summaries** before the **Recorded trace**. Expand tree context to see the recorded events. Select an event to read its exact input, output, raw reason code and captured rejection operands. A rule that was **Tried** was not necessarily **Applied**. **Blocked** names the recorded event without identifying an unrecorded blocker or treating it as the whole word's failure.

**Reason not recorded** and **Rejection details not recorded** mark missing evidence. A raw reason code is separate from the actual features or environment the parser captured. Do not infer a responsible rule from a nearby rejection or assume that every failed path explains the word's result. One attempt may fail while another succeeds.

**Grammar source not recorded** means the page cannot establish the trace's source save. **FieldWorks word context unavailable** means its stored analyses and opinions are unavailable here; it does not mean FieldWorks has no analysis. Use **Analyze texts**, revealed by hover or keyboard focus on the result, to read the word in its text. Use **Full derivation** for the complete recorded tree and filters. The diagnostic tools save the original trace independently of filters.
