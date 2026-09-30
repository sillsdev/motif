# Reading why a word fails

**Try a Word** traces a word through the grammar in the current [Baseline](term:baseline). The word can be new; it does not have to occur in the project’s Texts or lexicon. Enter it in **Type any word...**, choose **Try it**, and read the result. You can cancel a running trace.

Start with the answer and search status:

- **Parsed** means the parser found an analysis.
- **No parse** with **Search complete** means it finished and found no analysis.
- **Search incomplete** gives the stop reason when the parser did not finish. A limit is not evidence that no analysis exists.
- **Nothing to parse** means the word has a character the grammar’s character table does not define.

For a failed word, look at **THE PARSER'S FURTHEST TRY** and the rule steps. The attempt shows the pieces it built and the rule that stopped it, when recorded. If it never gets a stem, begin with the lexicon. If it builds a stem but stops at an affix, check the slot order and the affix’s allomorph or environment. If a rule changes an unexpected sound, inspect the phonological rules and their order.

The trace is a record of the parser’s search, not a verdict about the word. A rule can stop one attempt while another attempt succeeds. A completed **No parse** can have several plausible causes, so compare the trace with the form and analysis you know are right. If the search was incomplete, adjust the relevant limit and try again before changing the grammar.
