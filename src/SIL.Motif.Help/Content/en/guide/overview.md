# Overview

**Overview** is the first page you see. It shows the results of your last run: how many of your words parse, how many words with approved analyses were rebuilt exactly, which words are slow, and what the grammar check found. Opening it doesn't parse anything. Nothing here changes your FieldWorks project.

The figure strip shows the project's counts: words you chose to measure, places in your texts, wordforms, rules, and lexemes. **Look first** points to recorded words or timings worth inspecting. Five table sections follow:

- **Speed**: how many words were timed, their total and median time, and the time recorded for each kind of grammar work.
- **Slowest Words**: the measured words with the longest times. **Stopped** marks a word explicitly recorded as hitting a parser search limit.
- **Text Coverage**: how many of your chosen words parse, and how many places in your texts they cover (each time a word occurs in a text). If no chosen words occur in the chosen Texts, it says there are no places to cover.
- **Approved Analyses Kept**: how many words with an approved analysis the grammar rebuilt exactly. When no analysis is approved yet, it says there is nothing to rebuild.
- **Grammar Warnings**: what the grammar check found, with errors counted separately.

The **Grammar Warnings** section gives the warning count and any nonzero error count. Its word line counts how many different chosen words exactly use an item named by a warning. Before parsing, it says **Parse to see which of your words they touch**. The table lists the three largest warning kinds. Where the evidence is complete, each kind shows the word count; matches based only on spelling are labelled separately. Open **Warnings →** to see each kind's subjects.

These are separate measures, not one overall score. The project's counts appear after you press **Refresh**, which also checks the grammar. **Speed**, **Text Coverage**, and **Approved Analyses Kept** show parse results after you choose **Parse all words**.

The top bar says whether these numbers are current. If you have saved in FieldWorks since the last Refresh, the top bar says when, and offers **Refresh**. Times use your local clock; a save from another day includes its weekday and date. Press **Refresh** to read the saved project, then **Parse all words** to measure your [chosen words](term:default-selection) again. If Motif cannot read the numbers, the page says so in one line, with **Try again** and **Report a problem**.

Choose a section's link, such as **Slowest Words →**, **Texts →**, **Matrix →**, or **Warnings →**, to see more on that page. The links stay visible. Each **Look first** row opens the words or timings it names; its word-count link stays visible. Choose a slow word to open it in Texts. The Overview Inspector shows recorded Lost words and shared morphemes; **Open in Matrix** opens those Approved words in the Matrix. Expand **Project history** to see recorded activity. For a closer explanation of each measure and how to read its breakdowns, see [Reading the Overview](guide:reading-the-overview).

![Overview showing the project's stored results](shot:open-project-overview/overview)

Words marked **can’t read** were sent to PanGloss, which declined them. They are separate from completed searches that found no analysis and from words with no recorded parse. Choose that segment to read those words and their recorded reasons.
