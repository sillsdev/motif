# Installing Motif

Download the Windows x64 **Setup.exe** asset from the [Motif Releases page](https://github.com/sillsdev/motif/releases), choosing the beta version named in your invitation. Beta downloads are marked as prereleases. Run Setup and follow the installer. It installs Motif for your Windows user and creates a Start menu shortcut. Launch **Motif** from that shortcut, then choose **Select new…** and browse to your FieldWorks `.fwdata` file. Motif reads your project and changes nothing in it until you choose **Apply to FieldWorks project**. Motif is a tech demo. Keep a FieldWorks backup and try it on a copy of your project first.

The official Windows release includes the matching PanGloss parser. You do not need to download PanGloss separately.

Motif keeps workflow records in a file beside the FieldWorks project: for `Koro.fwdata`, the file is `Koro.motif.db`. It stores selections, pending changes, and measurement metadata; it is not a saved copy of the FieldWorks project. Motif does not currently support moving an existing project workspace: the database is bound to the `.fwdata` path and refers to files stored separately. Keep the project at its original path. The `.motif.db` alone is not a complete backup; include the `.fwdata`, `.motif.db`, and Motif worker root, which holds file-backed Baselines and parser artifacts. On Windows, the worker root defaults to `%LOCALAPPDATA%\SIL\Motif`; `MOTIF_WORKER_ROOT` can select another location.

Beta Windows builds may be unsigned. An unsigned Setup can show **Windows protected your PC** and **Unknown publisher**. Confirm that the version and download came from the Motif GitHub Releases page and match your invitation. If you trust that source, Windows may offer **More info** and then **Run anyway**; your organization's policy may prevent continuing. A signed build can also show a SmartScreen warning while it is new. If you cannot verify the download or Windows blocks it, ask the person running the trial for help. See [Microsoft's explanation of SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

The same release offers preview downloads for Linux x64 and macOS with Apple silicon. On Linux, make the AppImage executable and run it. On macOS, extract the portable ZIP and open the Motif app. These previews are not notarized macOS distributions or a promise of support for every Linux distribution. If your system blocks the preview, report the message to the person running the trial. An Intel Mac preview is not included.

Save in FieldWorks before **Refresh**; Motif reads the last saved project and can do that while FieldWorks remains open. Close FieldWorks before **Apply to FieldWorks project**. Motif refuses Apply if FieldWorks still owns the project.

Before 1.0, incompatible Motif stores are refused rather than upgraded. A confirmed **Delete this file and reopen** recreates a refused project store and loses changes not applied yet. For damaged project or machine stores, follow [the recovery instructions](guide:when-something-goes-wrong); remove only the identified Motif file, never the FieldWorks project.

Motif keeps a local usage log of command names and argument shapes, not the supplied values. It has no usage-upload feature. Writing-system loading can make online language-tag lookups. **Report a problem** lets you review a report that excludes project and language data by default; optional local details and AI Handoff files can contain private data, so review them before sharing.

Automatic updates are not enabled; download and install a newer release yourself. Uninstalling Motif removes its installation and command registration while leaving your FieldWorks project and Motif data in place.
