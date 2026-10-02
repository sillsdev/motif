# Installing Motif

Download the Windows x64 **Setup.exe** asset from the [latest Motif release](https://github.com/sillsdev/motif/releases/latest), run it, and follow the installer. It installs Motif for your Windows user and creates a Start menu shortcut. Launch **Motif** from that shortcut, then choose **Select new…** and browse to your FieldWorks `.fwdata` file.

The official Windows release includes the matching PanGloss parser. You do not need to download PanGloss separately. If you are using a development build instead, parsing requires the `pangloss` executable supplied with that build.

Motif stores project workflow data in a file beside the FieldWorks project: for `Koro.fwdata`, the file is `Koro.motif.db`. It holds Motif’s Baseline, Selection, measurements and pending workflow records; it does not replace the FieldWorks project. When moving or backing up a project, close Motif and copy both files together to the same folder. Open the `.fwdata` file at its new location to continue with the matching Motif data.
