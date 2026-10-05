# Parse all words

**Parse all words** asks PanGloss to parse the words you chose and keeps the result with the version of the project and the words it measured.

While it runs, the progress panel stays visible at the top of the window. It shows how many words are done, the word currently being parsed, elapsed time and an estimate of the time left. It also shows the slowest word so far and lists words that stopped at a time or search limit. If no word finishes for a long time, the panel explains that the parser may have stalled and offers **Cancel** and **Report a problem**.

The result tells you which words parsed, which approved analyses PanGloss built again, and how much time parsing took. A search that was interrupted or stopped at a limit is saved as not finished; it should not be read as a completed “no parse.” The **Overview** and **Texts** pages read the saved result. Opening either page does not run PanGloss.

After saving in FieldWorks, choose **Refresh** to read the saved project, then **Parse all words** in the top bar to parse your [Default Selection](term:default-selection) again. A parse is evidence about one run, not a verdict that the grammar is good or bad.

Motif allows only one Parse all words run for a project at a time, even across windows and processes. If another window is already parsing that project, a second request is refused; use the running window to follow progress or cancel the run.
