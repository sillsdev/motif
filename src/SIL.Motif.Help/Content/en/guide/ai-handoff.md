# AI Handoff

**AI Handoff** writes a folder you can share with a chat model. It includes project context for the words you parsed and a starter question. Settings shows what it plans to write and a data-sensitivity notice before you start; the folder can include real grammar rules, lexicon entries, and sentences from the selected Texts.

Only one parse can run for a project at a time. A Handoff that needs a fresh parse runs it before writing. Motif refuses another parse request for the project until the Handoff finishes.

A Handoff requested for a traced word keeps the displayed trace and the Baseline captured with it. Motif does not parse the word again or make a replacement trace; this Handoff includes the raw diagnostic and leaves out measured results from a separate parse.

1. Open **Settings** at the bottom of the navigation rail. Turn on **Show Hand Off by Copying Files** and scroll to the Handoff controls. Read the notice. If it says the files will include only words chosen on the Texts page, choose **Hand off every parsed word instead** to use every word from the last parse.
2. Choose **Write the AI Handoff**. If you have already written one, the button says **Write the AI Handoff again**.
3. Choose an output folder when prompted and wait for Motif to finish writing.
4. Review the files, then drag the files into your chat or use the copy actions for the prompt and paths.

The utility shows when the files were last written and marks them out of date when a newer parse finishes. Times use your local clock; a timestamp from another day includes its weekday and date. The `read_results.py` reader works over `parse-results.json` and `texts.json`. Write the Handoff again when you want the chat model to receive newer results. Only share the files if you are comfortable sending their linguistic data to that service.

![AI Handoff files ready to share](shot:handoff-cancel-retry/completed-files)

A Handoff from a parse records refused words with a refusal code and reason in `parse-results.json`. Word forms use composed Unicode for reading and searching. `texts.json` is an empty array when no Texts were chosen; added words can still have measured parse results.
