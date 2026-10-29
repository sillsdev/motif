# Choosing what to measure

Motif measures words from the same texts and any added words each time, so its numbers can be compared from run to run. On a new project, Motif captures its first Baseline when you open the project, then opens setup. Setup takes four steps.

On **What Should Motif Measure Every Time?**, choose one or more texts, or add words in the box labeled **Add Words:**. A long list of added words scrolls inside the box. Choose texts you have interlinearized: Motif compares its parses with the analyses you approved there. Words you add yourself are parsed too; if the saved project has an analysis for one, Motif can compare the parse with it. Choose at least one text or add at least one word to continue. On the Texts step and in **Analyze texts**, choose **Select All Texts** to check every text. On a long list, choose **Clear** to uncheck them all.

On **How Much Work Should Each Word Get?**, set the **Analysis Attempt Limit per Word**. The default is 200,000 attempts; PanGloss also derives an inner work limit from it. You can change the shared parsing limits later as the **Step Limit per Word** in Analysis options or Settings. **No Attempt Limit** lets every word run to the end, which can take a very long time; you can still **Cancel**.

This becomes the project's [Default Selection](term:default-selection), the words Motif measures every time. **Start First Run** saves your choice and starts parsing; the progress panel shows how far it has got and stays on a final stage while Motif reads PanGloss's statistics. **Skip for Now** closes first-time setup without saving these choices and does not offer it again until you choose **Configure…**. **Cancel**, or Escape, also closes setup without saving; setup is offered again the next time the project is opened or refreshed. See [Settings](guide:settings) for the saved parsing policy.

Choose **Back** or **Next: Texts**, **Next: Limits**, and **Next: First Run** to move through setup.

![Choosing texts and added words in first-time setup](shot:first-run-setup-parse/selection)
