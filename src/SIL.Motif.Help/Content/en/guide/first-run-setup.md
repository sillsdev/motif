# Choosing what to measure

For a new project, choose **Refresh** after opening it. The first Refresh captures a Baseline and opens setup if no Selection has been saved and setup has not been skipped. Before the first Baseline, **Configure…** is disabled and its detail reads **Refresh first to choose Texts**. Setup has four steps: **Found the project**, choose what to measure, choose parsing limits, and confirm the first run.

On **What should Motif measure every time?**, choose one or more texts, or add words in the box labeled **Add words:**. A text’s interlinearized words are what Motif can compare with the analyses already approved in FieldWorks. You can add words that do not occur in the chosen texts. Choose at least one text or add at least one word to continue.

On **How much work should each word get?**, choose a parser step limit. Motif estimates how long a word may take and gives it extra time to finish. The default is 1,000,000 steps. Choose **No step limit** to remove the parser's step limit; with no step limit, Motif applies no time limit.

The last step saves this as the project’s [Default Selection](term:default-selection) and starts the first [parse](term:parse-all-words). Choose **Back** or **Next: texts**, **Next: limits**, and **Next: first run** to move through setup. Choose **Start first run** to save and measure. If you are editing an existing project default, the final action is **Use this Selection**; it saves the choice without starting a run. **Skip for now** closes first-time setup without saving these choices.

On the Texts step and in **Analyze texts**, choose **Select all texts** to check every Text. On a long list, choose **Clear** to uncheck them all. The next parse uses the resulting Selection.

![Choosing texts and added words in first-time setup](shot:first-run-setup-parse/selection)
