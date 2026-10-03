# What Motif does

Motif tells you how well the FieldWorks parser handles your words: which ones parse, which of your approved analyses the grammar still builds, how long each word takes, and what the grammar check found. You can also look at one word closely and collect corrections to its analyses.

Motif reads your saved FieldWorks project. It writes nothing to it until you open **Review changes** and choose **Apply to FieldWorks project**. FieldWorks is where you edit the project.

To start, [install Motif](guide:install), then [open your project](guide:open-a-project).

Motif uses PanGloss to parse words. [Parse all words](term:parse-all-words) keeps the results of each run. The [Overview](term:overview) shows stored results such as Text Coverage, **Approved analyses kept**, timing, and grammar warnings. Opening the window shows the last results; it doesn't parse again.

1. [Open your project](guide:open-a-project) and press **Refresh**.
2. [Choose the words to measure](guide:first-run-setup) and start the first run.
3. Read the [Overview](guide:overview).
4. After your first run, open **Texts**, choose a word that did not parse, and use [Try a Word](guide:try-a-word) to inspect it.
5. In **Texts**, look at words and choose analysis changes.
6. On **Review changes**, check them, then choose **Apply to FieldWorks project**. See [Applying changes](guide:apply-to-fieldworks) first.
7. After you edit the grammar in FieldWorks, save, press **Refresh**, then **Parse all words**.

For details about individual steps, see the [Refresh](guide:refresh-numbers), [Texts](guide:texts) and [Review changes](guide:review-changes) guides.

For keyboard navigation and shortcuts, see [Keyboard shortcuts](guide:keyboard-shortcuts).

You can also make an **AI Handoff** folder with project context and a question for a chat model. Review the data-sensitivity notice before sharing it: the folder can contain real grammar, lexicon, and text data.

![Motif’s Overview after opening a project](shot:open-project-overview/overview)
