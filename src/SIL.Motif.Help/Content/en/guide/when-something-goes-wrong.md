# When something goes wrong

If a command or action is refused, first read the message in Motif. It often explains what needs to change, such as saving in FieldWorks, refreshing the Baseline, or removing a change that no longer fits.

For a problem report, choose **Report a problem** and review the preview. By default it includes Motif and PanGloss versions, your operating system, the window action, the refusal code, the command's exit status when available, and a stack without file locations. It leaves out project paths and names, words, grammar, and raw parser output. You can choose **Include local details** to add the error text and facts shown under Details; those may contain project or language data. Copying the report or opening the issue form happens only after you review the preview, and the issue form is not submitted automatically.

Send feedback through [GitHub Issues](https://github.com/sillsdev/motif/issues). If you cannot use GitHub, copy the reviewed report and email it to [john_lambert@sil.org](mailto:john_lambert@sil.org). Review any local details before sharing them.

If Motif says the project's file was made by a different version and offers **Delete this file and reopen**, read the confirmation before continuing. That action deletes Motif's file and recreates it; changes not applied yet are lost, but the FieldWorks project is not touched.

If Motif says its file for this project is damaged, close every Motif window. Keep a copy of the matching `.motif.db` file beside the project's `.fwdata` file if support may need it. Remove only that Motif file, then reopen the project; changes not applied yet will be lost, but the FieldWorks project is unchanged. If you cannot confirm the file belongs to Motif, leave it in place and ask for help.

If Motif cannot read its machine store, close every Motif window and keep a copy of the `motif.db` file at the path shown in the recovery instructions. Move or remove that file only if you can confirm it is Motif's machine store, then reopen Motif. Its Known projects and local usage history will be cleared; FieldWorks projects and their pending changes are unchanged. If you cannot confirm the file belongs to Motif, leave it in place and ask for help.

For a parser trace, open **Diagnostic tools** from **Try a Word**. You can save or copy diagnostic information for a particular word. It can include real word forms and morphological details, so review it before sharing.

If the app stops after an error, the separate **Motif has stopped** window keeps the full error under **Details** and offers **Report a problem**. The same preview shows what will be copied or placed in the issue form. The local **Details** may contain project paths or linguistic information; they are added to a report only when you choose **Include local details**.

![A recorded parser trace to inspect in Try a Word](shot:try-word-typing/trace)
