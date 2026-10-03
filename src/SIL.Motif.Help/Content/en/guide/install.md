# Installing Motif

Download the Windows x64 **Setup.exe** asset from the [latest Motif release](https://github.com/sillsdev/motif/releases/latest), run it, and follow the installer. It installs Motif for your Windows user and creates a Start menu shortcut. Launch **Motif** from that shortcut, then choose **Select new…** and browse to your FieldWorks `.fwdata` file. Motif reads your project and changes nothing in it until you choose **Apply to FieldWorks project**. While Motif is in beta, try it on a copy of your project first.

The official Windows release includes the matching PanGloss parser. You do not need to download PanGloss separately.

Motif keeps workflow records in a file beside the FieldWorks project: for `Koro.fwdata`, the file is `Koro.motif.db`. It stores selections, pending changes, and measurement metadata; it is not a saved copy of the FieldWorks project. Motif does not currently support moving an existing project workspace: the database is bound to the `.fwdata` path and refers to files stored separately. Keep the project at its original path. The `.motif.db` alone is not a complete backup; include the `.fwdata`, `.motif.db`, and Motif worker root, which holds file-backed Baselines and parser artifacts. On Windows, the worker root defaults to `%LOCALAPPDATA%\SIL\Motif`; `MOTIF_WORKER_ROOT` can select another location.
