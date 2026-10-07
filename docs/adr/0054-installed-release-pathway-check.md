# ADR 0054 — Check release pathways in the installed App

Before a release reaches testers, CI installs Motif on Windows, Ubuntu 22.04 and Apple Silicon macOS, then proves that the installed App and CLI can complete the main tasks. Publishing waits for all three systems to pass, so a download that starts but cannot do useful work is caught before users receive it.

**Status:** accepted. Records the release-check decisions for Motif 0.2.0-beta001 and later.

## Context

The package workflow already installs, starts and uninstalls packages, but its `--smoke` mode renders a window headlessly and exits. It does not prove that a person can use the installed window, that the installed `motif` executable can complete the same work, or that uninstall leaves language-project and user data intact.

The repository already has declarative Walkthrough scripts driven by tests against a real Avalonia window and seeded LibLCM projects. Reusing those scripts keeps the installed-product check tied to the same project-opening, parsing, review and Handoff behavior exercised by the developer suite.

## Decisions

### 1. Replay the shipped App from inside the App

The installed App accepts an internal `--release-pathways` switch. It enters replay only when `MOTIF_RELEASE_PATHWAYS` also names the script folder. If either switch is absent, startup continues normally and no replay input is read. The window drives the Walkthrough scripts against a seeded project created in a temporary directory, writes a result file and screenshots, and returns a non-zero exit code when a step fails. The ordinary launch check remains separate and verifies that the installed window appears and starts cleanly.

The App reads Walkthrough scripts only from the directory named by `MOTIF_RELEASE_PATHWAYS`. Replay accepts only the seeded sample created for that run under the operating-system temporary directory. It never searches for or opens another project. The check uses the parser bundled with the installed package.

One script remains the source for both test and release replay. The release check runs the five owner-selected paths: open a project and complete Text setup; Analyze texts and read a word card and Word list; Try a Word and inspect its trace; stage, Review and Apply a change to a copy, then read it after Refresh; and write an AI Handoff. On Windows it also verifies that Apply is refused while another process holds the project open.

This is an implementation and release check, not a documented user feature. Its switch is inert without the matching environment variable, and the paired gate prevents an ordinary launch from interpreting an accidental argument or environment value as permission to replay.

### 2. Keep actions in the installed product

No external UI automation driver operates the installed window. The application itself interprets the Walkthrough against its own controls and captures screenshots, so the checked binary performs the actions and observes the results. This follows established in-process patterns: LibreOffice's smoketest client asks a running LibreOffice instance to execute its test macro; JetBrains IDE Starter passes a scenario for commands to run in the IDE; Signal documents running its release tests after building production packages. [LibreOffice smoketest](https://docs.libreoffice.org/smoketest.html), [JetBrains IDE Starter](https://github.com/JetBrains/intellij-community/blob/master/tools/intellij.tools.ide.starter/README.md), [Signal Desktop release testing](https://github.com/signalapp/Signal-Desktop/blob/main/CONTRIBUTING.md#testing-production-builds).

External automation adds another installed tool and a second control path to maintain. Microsoft describes WinAppDriver development as paused, and Avalonia's testing guidance distinguishes fast in-process control tests from slower Appium tests that need a real window. The release check uses the real installed window but keeps replay inside the App, where the existing Walkthrough identifiers and behavior are available. [WinAppDriver status](https://github.com/microsoft/WinAppDriver/issues/1371), [Avalonia testing guidance](https://docs.avaloniaui.net/docs/testing/).

### 3. Gate publishing on the three installed-package checks

The package workflow runs release-check jobs on Windows, Ubuntu 22.04 and Apple Silicon macOS against the commit being packaged. A release cannot publish unless all three checks pass; there is no override. The checks also run on demand and nightly on `main` when `main` has advanced since the last successful nightly check.

During the installed-product checks, CI hides the .NET SDK from `PATH`. It installs the real Windows Setup, Linux AppImage and macOS ZIP packages, confirms the app and CLI can run from those packages, executes the five Walkthrough paths in the installed App and CLI, then uninstalls and verifies the seeded FieldWorks project and user data remain present.

## Consequences

- A Walkthrough consumed by release replay remains the same JSON script used by the App tests; release-only setup does not create another authored action list.
- The installed App contains a narrowly gated internal replay entrypoint and writes evidence only to the run's temporary output location.
- The three OS checks become required release dependencies. Manual release publication must be dispatched from the matching version tag and wait for all three jobs to pass.
- Nightly checks avoid rebuilding an unchanged `main` commit, while a manual dispatch can run the check on demand.

## Amendment: release twins for scripted-parser walkthroughs

The installed check runs the real bundled PanGloss against the synthetic sample, so a walkthrough that the
tests drive with a scripted fake parser, such as the explained word card's no-parse and capped words, cannot
reach its scripted states there. Such a walkthrough keeps its test script and gains a twin under
`walkthroughs/release/<id>.walkthrough.json`, written against the sample's real results. Replay prefers the
twin when one exists, so a walkthrough that already works against the real parser has no twin and stays one
script. Fixture setup that has no Released command (staging a change and running its trial, which testers do
from the window) may use developer commands; every step a tester takes uses Released ones.
