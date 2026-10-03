# Installing Motif

Download the Windows x64 **Setup.exe** asset from the [latest Motif release](https://github.com/sillsdev/motif/releases/latest), run it, and follow the installer. It installs Motif for your Windows user and creates a Start menu shortcut. Launch **Motif** from that shortcut, then choose **Select new…** and browse to your FieldWorks `.fwdata` file. Motif reads your project and changes nothing in it until you choose **Apply to FieldWorks project**. While Motif is in beta, try it on a copy of your project first.

The official Windows release includes the matching PanGloss parser. You do not need to download PanGloss separately.

Motif stores project data in a file beside the FieldWorks project: for `Koro.fwdata`, the file is `Koro.motif.db`. It holds Motif’s saved copy of the project, the words you chose to measure, the results, and any changes you haven't applied yet; it does not replace the FieldWorks project. When moving or backing up a project, close Motif and copy both files together to the same folder. Open the `.fwdata` file at its new location to continue with the matching Motif data.
