# Changing an analysis

In **Texts** or **Words**, find the word, then choose the analysis and the opinion you want for it: approve or disapprove it, or make it Unknown. Opinions change one analysis at a time, in the text. To keep one reading at several places of the same word, use the word panel described in [Passage](guide:passage). The available action may vary with the word’s current state and the reading you select.

Each word has two buttons. The first is its opinion mark: **A**, **U**, **D**, or a dashed mark when FieldWorks holds no analysis. Choose **Set analysis** to see each stored analysis and choose **Set analysis: Approved**, **Set analysis: Unknown**, **Set analysis: Disapproved** or **Set analysis: Remove**, or to mark the spelling Incorrect. The current Opinion is shown without staging it again. Once you stage a change, the mark turns solid. The second button is a number: how many readings PanGloss found that FieldWorks does not hold. Choose it to open the word panel for that word at that place, where you choose one reading and approve it for the places you check. Ctrl+ZCtrl+Z (⌘+Z on macOS) undoes the last staged choice.

The change is kept as pending work for this project. It applies to the word form everywhere, not only the occurrence you were looking at. Open **Proposals** and look under **Your Changes Not Applied Yet** to inspect it. Nothing is written to FieldWorks while you are choosing or reviewing.

To measure the effect before applying, open that document and choose **Check These Changes**. When you are ready, close FieldWorks and choose **Apply…**, then confirm. See [Apply to FieldWorks](guide:apply-to-fieldworks) for the conditions that must be met first.

## Inside a Proposal

When a Proposal is open, a word card adds a block called **In PanGloss · with this Proposal**. It shows the analyses PanGloss built for the word when the Proposal was last tried, and for each one the changes in the Proposal that built it. If the Proposal has not been tried yet the block says **Not tried yet**, while it is being tried it says **Trying…**, and if the project or the Proposal changed since, it says **Needs another try**. Nothing is tried again on its own.

Choose **Set analysis** to stage a change into the open Proposal instead of your pending word changes. **Approve … in** the Proposal's name approves an analysis FieldWorks already holds as Unknown that the Proposal now builds. **Add an expected parse to** the Proposal's name adds an analysis FieldWorks does not hold, as Approved, and the Proposal shows it as **Will add as Approved**. Either way the change is checked against the Proposal's own copy of the project, and it is applied together with the Proposal's other changes. If that copy has been cleaned up, the change is marked as needing another check; choose **Try again** on the Proposal bar.

Outside a Proposal nothing changes: your choices go to the pending word changes as before.

![A pending choice shown on its word card](shot:explained-word-card/unknown-staged)
