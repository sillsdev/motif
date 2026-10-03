# Choosing what to measure

Motif measures words from the same texts and any added words each time, so its numbers can be compared from run to run. You choose these when you first press **Refresh** on a new project. Setup opens by itself and takes four steps.

On **What should Motif measure every time?**, choose one or more texts, or add words in the box labeled **Add words:**. Choose texts you have interlinearized: Motif compares its parses with the analyses you approved there. Words you add yourself are parsed too; if the saved project has an analysis for one, Motif can compare the parse with it. Choose at least one text or add at least one word to continue. On the Texts step and in **Analyze texts**, choose **Select all texts** to check every text. On a long list, choose **Clear** to uncheck them all.

On **How much work should each word get?**, choose how many steps the parser may take on one word before it stops. The default, 1,000,000, suits most grammars. **No step limit** lets every word run to the end, which on a slow grammar can take a very long time; you can still **Cancel**.

This becomes the project's [Default Selection](term:default-selection), the words Motif measures every time. **Start first run** saves your choice and starts parsing; the progress panel shows how far it has got. **Skip for now** closes first-time setup without saving these choices.

Choose **Back** or **Next: texts**, **Next: limits**, and **Next: first run** to move through setup.

![Choosing texts and added words in first-time setup](shot:first-run-setup-parse/selection)
