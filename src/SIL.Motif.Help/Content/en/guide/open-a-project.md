# Opening a project

Open the project menu and choose **Select new…** to browse for a FieldWorks `.fwdata` file. To return to another project Motif knows, choose **Open recent** and select it, or choose it from the list on the initial **Choose a project** page. The window opens on **Overview** and shows the results from the last run. Opening a project doesn't parse anything.

You can also choose a FieldWorks backup, a `.fwbackup` file. Motif restores it into a new folder beside the backup, named after the project, and opens that copy, so the backup itself is never changed. If a folder of that name is already there, Motif uses the next free name, such as `Koro 2`, and leaves the earlier copy as it was.

If this project has no Baseline yet, Motif captures one when you open it and then opens setup. Later, press **Refresh** to read the project as FieldWorks last saved it. Save your latest edits in FieldWorks first if you want them included: unsaved edits in FieldWorks are not part of the file Motif reads. You do not have to close FieldWorks just to open a project. If another program is using the project, Refresh may say “the project 'Koro' is in use by another program” (using your project’s name); close the other program and try again.

On a new project, Motif captures the first Baseline and asks which words to measure. See [Choosing what to measure](guide:first-run-setup).

![A project open on the Overview](shot:open-project-overview/overview)
