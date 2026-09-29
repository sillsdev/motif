# Velopack for Motif and FieldWorks

Velopack can package Motif and deliver updates on Windows, macOS, and Linux, but it does not cover every way FieldWorks installs software. The strongest fit is a per-user Motif installation first, while FieldWorks keeps control of machine-wide deployment until its native components and update rules are covered.

Research checked on 2026-09-26. Velopack packaging details come from its [packaging overview](https://docs.velopack.io/packaging/overview), OS guides, CLI reference, and repository. Product-specific observations come from this repository's [release script](../tools/package-release.ps1), [PanGloss release pin](../pangloss-release.json), [README](../README.md), [ICU findings](three-paths-report.md), and [ADR 0048](adr/0048-cross-platform-parser-containment.md).

## 1. Outputs, locations, and install scope

The pack command takes one staged directory for one platform and one main executable. It writes a full package, channel metadata (releases.<channel>.json and assets.<channel>.json), and a portable package; it writes a delta when the previous release package is available. The platform outputs are:

| Target | Normal deliverables | Install model |
|---|---|---|
| Windows | Setup.exe, Portable.zip, full/delta .nupkg, feed metadata; optionally an MSI | Setup is per-user under %LocalAppData%\<packId> and does not need elevation. MSI supports per-user or per-machine installs; a per-machine install under Program Files/HKLM needs elevation. |
| macOS | .pkg, portable .zip, full/delta packages and feed metadata | The package can target /Applications or ~/Applications; the system location may need elevation. The portable zip can be unpacked into a user-writable directory. The .app is the application bundle, not a separate Velopack installer output. |
| Linux | .AppImage, full/delta packages and feed metadata | No conventional installer is produced. The user puts the executable image where desired, marks it executable, and runs it; a user directory needs no elevation. |

Windows pack options include --noPortable, --noInst, and --msi; macOS includes --noPortable and --noInst; Linux produces an AppImage. See [Windows](https://docs.velopack.io/packaging/operating-systems/windows), [macOS](https://docs.velopack.io/packaging/operating-systems/macos), [Linux](https://docs.velopack.io/packaging/operating-systems/linux), and [installer options](https://docs.velopack.io/packaging/installer). Velopack can make a macOS .app bundle with vpk bundle or package a prepared bundle. It does not produce a Windows-style setup wizard for Linux.

An AppImage is a single relocatable file and does not scatter files through the OS. Velopack replaces that file on update. AppImage execution normally uses FUSE; Type 2 images also support --appimage-extract-and-run on systems without FUSE. Ubuntu 24.04 uses the libfuse2t64 package name for the FUSE 2 compatibility library. See Velopack's [Linux guide](https://docs.velopack.io/packaging/operating-systems/linux) and AppImage's [FUSE troubleshooting guide](https://docs.appimage.org/user-guide/troubleshooting/fuse.html).

## 2. Four executables, one main executable, and update safety

Velopack packages the files staged in the input directory and preserves their relative layout. A package can contain motif, SIL.Motif.App, SIL.Motif.Worker, pangloss, and supporting files together. It does not combine separate publish trees or discover unstaged dependencies. Each package has exactly one main executable; it is the launch target used by the installer and handles Velopack command-line hooks. Windows Setup and the macOS .pkg launch that target after installation, and each OS launcher needs one chosen entry point. Choosing the CLI means installer/desktop launch follows CLI behavior; choosing the GUI means FieldWorks must use the CLI companion separately. See the [pack command](https://docs.velopack.io/packaging/overview).

On Windows, Velopack puts the versioned payload below <install-root>\current, stores Update.exe at the install root, and creates a stable root-level stub for the selected main executable. Updates replace the contents of current as a unit, so the directory path remains while its files change. A running CLI or Worker, an app whose working directory is in current, or an external process reading a file there can hold a lock. Velopack attempts to stop processes using the old payload; an unrecognized or elevated process can prevent replacement. See the [Windows layout and locking notes](https://docs.velopack.io/packaging/operating-systems/windows).

For FieldWorks, selecting motif as the main executable gives it the stable Windows root stub. The current payload also contains the CLI and its adjacent Worker and PanGloss files. FieldWorks should use a configured install root or a Motif-owned discovery record, then invoke the CLI through a documented entry point; it should not treat replaceable current contents as a versioned API. macOS has an .app bundle entry in Info.plist, and Linux has a generated .desktop/AppImage entry point. The sibling arrangement can be staged inside those bundles, but verify their internal paths against real packages before FieldWorks relies on them.

There is an important difference between the brief's intended layout and today's release script. [package-release.ps1](../tools/package-release.ps1) currently publishes separate app and cli directories, duplicates pangloss into each, includes SIL.Motif.Worker.dll but deliberately excludes a Worker apphost, and does not make the four requested executables siblings. [pangloss-release.json](../pangloss-release.json) currently pins only win-x64, although the release script accepts the four stated RIDs. Velopack adoption therefore first needs a staging decision: publish one common directory per RID with all four executable entry points, or preserve separate app/CLI layouts and document how each frontend locates its companions.

Velopack auto-applies a prepared newer update at startup by default when the app runs its integration startup. That is a poor time to replace files if an earlier Motif process still owns a Worker or is writing project/store state. Disable automatic apply with SetAutoApplyOnStartup(false), then apply explicitly after quiescence: no active motif apply --all-pending, Worker job, or PanGloss invocation. ADR 0048 gives Motif process-tree and worker ownership rules across OSes, but those locks coordinate Motif processes; they do not by themselves make a package update safe. See [Velopack integration](https://docs.velopack.io/integrating/overview) and [hooks](https://docs.velopack.io/integrating/hooks).

## 3. Hooks, PATH, and external discovery

The main executable should call VelopackApp.Build().Run() early. Hooks handle install, obsolete-version, updated-version, and uninstall arguments; normal lifecycle handlers work cross-platform, while fast callbacks are Windows-only. Hooks have short execution limits and should not show UI. VelopackLocator exposes paths to the running Velopack app; it is not an external-process discovery service. See [integration overview](https://docs.velopack.io/integrating/overview), [hook contracts](https://docs.velopack.io/integrating/hooks), and [preserved paths](https://docs.velopack.io/integrating/preserved-files).

Velopack has no documented built-in option that adds motif to PATH or registers a cross-platform CLI command. Practical Motif choices are:

- **Windows:** in an install hook, add the stable install-root CLI stub to the current user's PATH and remove that exact entry in an uninstall hook. This is Motif-owned behavior, not a VPK feature; account for duplicates and notify the user's shell that PATH changed. For FieldWorks, prefer a configured absolute executable path over PATH search.
- **macOS:** provide a small user-owned symlink or wrapper in a user bin directory that targets the installed app's CLI. Keep it outside the .app so replacing the bundle does not erase it, and handle app relocation or removal.
- **Linux:** provide a user-owned symlink or wrapper pointing to the selected AppImage or an explicit CLI payload. AppImage CLI dispatch through extra command-line arguments is plausible but is not a documented Velopack contract; verify it before relying on it.

An external program should obtain the install path from its own configuration, an explicit user setting, or an installer-owned record. A per-user Windows default path can be derived from the package ID, but a custom --installto path and macOS/Linux user placement cannot safely be guessed by another application.

## 4. ICU, rpaths, notarization, and Linux FUSE

Staging native libraries beside Motif is possible, but Velopack does not repair native loader paths or validate that LibLCM found the intended ICU. On macOS, embed required dylibs in the .app and build the Mach-O @loader_path/rpath relationships before packaging. Velopack's macOS pack flow signs nested code by default, and notarization covers the finished bundle; the packer is not an rpath editor. Test the final signed and notarized app on a clean supported macOS host, including pangloss and every dylib it loads. See [macOS packaging](https://docs.velopack.io/packaging/operating-systems/macos) and [signing](https://docs.velopack.io/packaging/signing).

The stated plan to bundle “ICU major 72” needs a semantic check before it becomes a release rule. LibLCM's custom initialization expects SIL's SilIcuInit export and FieldWorks-specific nfc_fw/nfkc_fw normalization tables; stock ICU with the same major version is not equivalent. The repository's [ICU findings](three-paths-report.md) explain that a missing custom library can fall back silently to stock normalization, which affects string identity. The Host project currently references Microsoft.ICU.ICU4C.Runtime 72.1.0.3 on Windows only; the README describes Ubuntu's SIL package and Homebrew icu4c on macOS. **UNVERIFIED:** whether the intended macOS/Linux bundle currently contains the custom SIL ICU binary and data for each RID. Confirm exact library names, exports, data files, and loader paths for all four RIDs; do not infer correctness from version 72 alone.

Velopack describes an AppImage as containing libraries not present in the base OS, but Motif still needs to stage the right SIL ICU files and confirm they load from the image. Linux runners and target systems also need working FUSE or a Type 2 AppImage invocation using --appimage-extract-and-run. The official AppImage guidance documents FUSE requirements; Velopack's Linux guide does not promise target hosts have FUSE installed. Include a clean-host AppImage smoke check on supported distributions.

## 5. Signing and CI credentials

**Windows:** Velopack can invoke signtool through --signParams; it also supports Azure Trusted Signing through a metadata file and --azureTrustedSignFile. For certificate-based signing, keep the PFX and password in CI secret storage. For Azure Trusted Signing, provide account/profile/endpoint configuration and short-lived Azure identity credentials; GitHub Actions can use OIDC when configured. Do not put passwords or tokens directly in the command line. Unsigned Windows apps are more likely to trigger SmartScreen or antivirus warnings. See [Velopack signing](https://docs.velopack.io/packaging/signing).

**macOS:** code-sign with a Developer ID Application identity, use a Developer ID Installer identity for the .pkg, enable the hardened runtime, then notarize with notarytool. VPK exposes --signAppIdentity, --signInstallIdentity, --notaryProfile, and related options. CI needs signing certificates and import passwords, Apple team identity, and notarization credentials (or a preconfigured notarytool profile in a temporary keychain). Follow Apple's [notarization requirements](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution) and [notarytool workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

**Linux:** I found no Velopack package-signing stage documented for Linux. Use HTTPS for feeds and publish a separately signed checksum or provenance record if release authenticity must be verified. Whether a future Velopack version adds AppImage signing is **UNVERIFIED**; the current project discussion says this is planned without an ETA ([discussion](https://github.com/velopack/velopack.docs/discussions/27)).

## 6. Feeds, channels, deltas, rollback, and Motif's store

Velopack supports local directories/network shares and hosted feeds such as static HTTP storage, S3/Azure, and GitHub Releases. A feed consists of channel metadata and referenced package assets; channels separate stable/beta or OS/architecture streams. The default channel name is the OS, so Motif should choose explicit channel names when publishing multiple architectures or release rings. See [update sources](https://docs.velopack.io/integrating/update-sources), [channels](https://docs.velopack.io/packaging/channels), and [self-hosting](https://docs.velopack.io/distributing/self-hosting).

Velopack generates a delta when the previous full package is available to the pack step; its delta guide documents a 2 GB per-file ceiling and fallback to a full download if a delta cannot be used. I found no documented overall package or AppImage size ceiling. **UNVERIFIED:** total multi-gigabyte payload support and practical build, signing, upload, and update times. Measure the actual Motif and PanGloss payloads; files over the delta limit may require full-package updates. See [delta updates](https://docs.velopack.io/packaging/deltas).

Updates move forward by default. The app can opt into a specific version or downgrade if the older package remains available. Velopack Flow describes rollback by changing the managed release list; that is separate from automatically detecting an unhealthy app and reverting its files. Reverting binaries does not revert Motif data. The Motif store is under per-user application data (Windows: LocalApplicationData/SIL/Motif), outside the Velopack install root, and therefore survives a binary update; uninstall cleanup also needs an explicit policy. Motif remains responsible for refusing unsupported stored shapes and explaining how to reset the store. The repository's pre-1.0 rule prohibits compatibility bridges or migration code before 1.0. Treat data compatibility and binary rollback as separate release decisions. See [version targeting](https://docs.velopack.io/integrating/specific-version), [preserved paths](https://docs.velopack.io/integrating/preserved-files), and [Velopack Flow](https://docs.velopack.io/distributing/self-hosting).

## 7. Headless CI: install, local feed, N to N+1, uninstall

Build packages on a runner for their target OS. Velopack can cross-package Windows and Linux, but macOS packaging needs macOS tooling for signing and package construction. Use one feed/output directory for releases N and N+1 so N's full package is available when producing N+1's delta.

The examples select motif as the package entry point because FieldWorks calls the CLI. If desktop launch should open the GUI, use SIL.Motif.App (or SIL.Motif.App.exe) as the main executable and separately verify how callers launch the CLI companion. Repeat each platform's pack command with version 0.9.1 and its version-specific stage directory into the same feed; do not clear the feed between versions.

Example Windows package and unattended lifecycle (replace stage paths with the self-contained publish directory for the RID):

    $feed = Join-Path $env:RUNNER_TEMP 'motif-feed'
    $install = Join-Path $env:RUNNER_TEMP 'motif-install'
    vpk pack --packId SIL.Motif --packTitle Motif --packVersion 0.9.0 --packDir .\publish\win-x64 --mainExe motif.exe --runtime win-x64 --channel stable-win-x64 --outputDir $feed
    $setup = Get-ChildItem -LiteralPath $feed -Filter '*-Setup.exe' | Select-Object -First 1
    & $setup.FullName --silent --installto $install
    vpk pack --packId SIL.Motif --packTitle Motif --packVersion 0.9.1 --packDir .\publish\win-x64-v2 --mainExe motif.exe --runtime win-x64 --channel stable-win-x64 --outputDir $feed
    # Run the installed Motif update smoke mode here; it checks, downloads, applies, restarts, and asserts 0.9.1.
    & (Join-Path $install 'Update.exe') uninstall --silent

For a machine MSI, add --msi --instLocation PerMachine to the first pack call. The machine-wide install requires an elevated runner. Exercise it with standard quiet MSI commands:

    $msi = (Get-ChildItem -LiteralPath $feed -Filter '*.msi' | Select-Object -First 1).FullName
    msiexec.exe /i $msi /qn /norestart "VELOPACK_INSTALLDIR=$install"
    msiexec.exe /x $msi /qn /norestart

For macOS, the no-admin CI path extracts the portable zip into a test user's Applications directory and removes the app bundle afterward:

    feed="$RUNNER_TEMP/motif-feed"
    vpk pack --packId SIL.Motif --packTitle Motif --packVersion 0.9.0 --packDir ./publish/osx-arm64 --mainExe motif --icon ./assets/motif.icns --runtime osx-arm64 --channel stable-osx-arm64 --outputDir "$feed"
    mkdir -p "$HOME/Applications"
    archive=$(find "$feed" -maxdepth 1 -name '*-Portable.zip' -print -quit)
    ditto -x -k "$archive" "$HOME/Applications"
    # Invoke the main executable at the path declared in the generated .app Info.plist.
    rm -R "$HOME/Applications/Motif.app"

The .pkg can be tested separately when the runner can install to /Applications. Velopack documents no macOS-specific silent uninstall command, and the app bundle must be removed. See the [macOS guide](https://docs.velopack.io/packaging/operating-systems/macos).

For Linux, AppImage install/uninstall is file placement and removal:

    feed="$RUNNER_TEMP/motif-feed"
    vpk pack --packId SIL.Motif --packTitle Motif --packVersion 0.9.0 --packDir ./publish/linux-x64 --mainExe motif --icon ./assets/motif.png --runtime linux-x64 --channel stable-linux-x64 --outputDir "$feed"
    mkdir -p "$HOME/Applications"
    image=$(find "$feed" -maxdepth 1 -name '*.AppImage' -print -quit)
    install -m 0755 "$image" "$HOME/Applications/Motif.AppImage"
    "$HOME/Applications/Motif.AppImage" --appimage-extract-and-run --help
    rm -- "$HOME/Applications/Motif.AppImage"

Omit --appimage-extract-and-run on a FUSE-equipped runner. The --help call only smoke-checks startup; it does not prove Motif's CLI can dispatch through an AppImage. **UNVERIFIED:** Velopack documents no equivalent Linux updater uninstall command.

For a real N→N+1 update, the installed Motif app needs an update smoke mode that points its UpdateManager at the local feed, calls CheckForUpdatesAsync() and DownloadUpdatesAsync(), then applies/restarts and asserts the relaunched version. The local directory can be passed directly to the manager:

    var manager = new UpdateManager(feedDirectory);
    var update = await manager.CheckForUpdatesAsync();
    if (update is not null)
    {
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update);
    }

The commands vpk upload local --path <feed> and vpk download local --path <feed> can move feed assets in and out of CI. Velopack's test locator covers check/download behavior but does not perform a real update install. The current Motif repository packages portable directories and has no Velopack integration or update smoke mode, so a real headless N→N+1 test requires adding that mode to an installed Motif executable. See [Velopack testing](https://docs.velopack.io/integrating/testing), [update sources](https://docs.velopack.io/integrating/update-sources), and [local feed commands](https://docs.velopack.io/distributing/self-hosting).

## 8. FieldWorks fit

Velopack supports .NET Framework 4.8 apps and Windows prerequisite bootstrapping, including VC++ redistributables. Motif is self-contained net10.0, so it should not need a .NET runtime bootstrapper. Arbitrary native files can be packaged, but they must match the target architecture and have dependencies staged. See [bootstrapping](https://docs.velopack.io/packaging/bootstrapping).

Velopack's installers are intentionally light-touch. Windows MSI is available and Velopack documents Group Policy deployment; Microsoft Intune can deploy MSI line-of-business apps or wrap MSI/EXE installers as Win32 apps. This is Windows package-deployment compatibility, not a Velopack-specific Intune integration, and should be exercised with the exact quiet switches and install scope. References: [Velopack installer options](https://docs.velopack.io/packaging/installer), Microsoft's [Group Policy MSI guidance](https://learn.microsoft.com/en-us/troubleshoot/windows-server/group-policy/use-group-policy-to-install-software), [Intune Windows LOB apps](https://learn.microsoft.com/en-us/intune/app-management/deployment/add-lob-windows), and [Intune Win32 app management](https://learn.microsoft.com/en-us/mem/intune/apps/apps-win32-add).

The normal Velopack pack options do not provide a rich WiX-style model for arbitrary registry values, COM registration, file associations, Windows services, or optional features. A maintainer says registry values and file associations can be written from after-install/before-uninstall hooks until native support exists; that is custom application code, not declarative installer support. Hook privilege behavior needs validation for machine-wide registration. See the [installer customization notes](https://docs.velopack.io/packaging/installer), [hooks](https://docs.velopack.io/integrating/hooks), and the [maintainer response on registry integration](https://github.com/velopack/velopack.docs/discussions/50). If FieldWorks still needs machine-wide COM or registry registration, retain a dedicated MSI/WiX layer or make those components registration-free; do not rely on a per-user Motif updater to own system registration.

FieldWorks should have one owner for the Motif files. If its per-machine installer copies Motif into Program Files, that installer should own the bundled version and updates. If Velopack owns a separate install, FieldWorks should locate and call that user-scoped CLI explicitly. Letting both systems update or replace the same directory risks mismatched package metadata, permissions, and rollback behavior. Velopack can gradually replace genericinstaller for the parts it handles; complex native registration remains a separate installer responsibility unless FieldWorks simplifies that contract.

Multi-gigabyte payloads have no documented total-size cap I could find, but delta generation is limited per file, and macOS signing/notarization of large bundles has cost. **UNVERIFIED:** maximum supported total package size and performance at FieldWorks scale. Benchmark the actual payload and release feed before choosing a packaging format.

## 9. Project health and sharp edges

The repository is MIT-licensed ([LICENSE](https://github.com/velopack/velopack/blob/develop/LICENSE)). GitHub lists release 1.2.158 from 2026-09-21, after 1.2.110 on 2026-06-03, showing recent maintenance ([releases](https://github.com/velopack/velopack/releases)). The generated CLI reference still labels itself 1.2.0, so pin the CLI/SDK version and verify options against that version. I found no published maintainer roster or support SLA; long-term support coverage and response times are **UNVERIFIED**.

Current open issue reports worth validating against Motif's intended MSI path include an orphaned Programs and Features entry after uninstall ([#1006](https://github.com/velopack/velopack/issues/1006)), MSI product registration not reconciled during updates ([#1004](https://github.com/velopack/velopack/issues/1004)), and a leftover LocalAppData directory after per-machine MSI uninstall ([#997](https://github.com/velopack/velopack/issues/997)). Other relevant open reports include macOS update signature verification ([#981](https://github.com/velopack/velopack/issues/981)) and an approximately 14-second update delay during Windows process checks ([#954](https://github.com/velopack/velopack/issues/954)). Issue reports are evidence of cases to test, not proof that every installation is affected.

## Recommendation

**Conditional go for standalone Motif distribution through Velopack; do not make it FieldWorks' sole installer yet.** A no-admin per-user Motif package and static update feed match the standalone product well. The Windows MSI option and cross-platform packaging are useful building blocks, but FieldWorks' system-wide C++/COM integration still needs an explicit installer owner. Keep that responsibility in its existing MSI/WiX path until an equivalent deployment has passed install, update, rollback, and uninstall validation. Resolve the package staging layout, PanGloss artifacts for every RID, and the SIL ICU payload before the first Velopack release.

### Gotchas ranked by severity

1. **ICU semantic mismatch — release blocker.** Stock ICU 72 is not necessarily the custom SIL ICU LibLCM expects. Missing custom exports/tables can silently change normalization and string identity.
2. **Updating while Motif work is live — data-integrity risk.** Velopack replaces the Windows current payload and tries to stop processes using it. Updates must wait for CLI, Worker, and PanGloss work to finish.
3. **Package's single main executable — deployment design decision.** Choose whether the stable entry point is motif or the GUI and prove how FieldWorks launches the other entry point on each OS.
4. **Competing install owners — enterprise deployment risk.** Do not let Velopack and FieldWorks MSI manage the same install directory or disagree about machine/user scope.
5. **Native registration — FieldWorks integration gap.** Registry, COM, services, and rich optional-feature authoring need separate MSI/WiX work or a simpler component model.
6. **AppImage FUSE and macOS loader paths — platform acceptance risk.** Test on clean supported hosts; packaging alone does not prove native libraries load.
7. **Store compatibility versus binary rollback — recovery risk.** A binary downgrade will not restore Motif's user data. Keep the current pre-1.0 refusal policy explicit across update failures.
8. **Large payload and MSI lifecycle — operational risk.** Measure full package/delta times and validate install/update/uninstall for the chosen MSI scope.

### Open questions for the owner

- Should motif or SIL.Motif.App be the Velopack main executable, and what exact stable path will FieldWorks call?
- Will FieldWorks own a bundled per-machine Motif version, or should it discover a separate per-user Motif installation?
- Is the required release payload actually four sibling executables, given today's separate app/CLI trees and Worker library-only layout?
- Which PanGloss artifact is pinned for Linux and macOS, and which exact SIL ICU library/data files will ship for each RID?
- Which Windows registry, COM, file-association, or prerequisite actions must FieldWorks preserve when genericinstaller is retired?
- Which macOS and Linux versions/distributions must run Motif, and can CI test signing, notarization, ICU loading, and AppImage FUSE behavior there?
- What is the measured package size, and which CI system owns the signing identities and update feed credentials?
- Should the release gate require a real N→N+1 update/restart/uninstall test for all four RIDs before Velopack becomes the default?
