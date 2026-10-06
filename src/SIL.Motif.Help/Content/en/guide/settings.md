# Settings

Open **Settings** from the window to change how Motif looks and to find its keyboard shortcuts, project parsing limits, Help, problem reports, and version information. Display choices are remembered for this user on this computer; a project's parsing limits are saved with that project's [Default Selection](term:default-selection).

## Display

Zoom changes the Motif window's layout and text together. At higher zoom the usable page area is smaller, so scroll within a page to reach its content. Display shows the available zoom choices; [Keyboard shortcuts](guide:keyboard-shortcuts) lists their keys.

Theme can follow the operating system or use Light or Dark. The change applies while Settings is open.

## Parsing

The Parsing group edits the current project's Default Selection. Setup, Settings, and **Analysis options** edit that same saved limit policy; the next parse uses it, while earlier results keep the limits from their own run. See [Default Selection](guide:default-selection) and [first-run setup](guide:first-run-setup) for choosing what to parse.

When the step limit is finite, the time limit can follow Motif's estimate or use an explicit value. An explicit value remains in force when the step limit changes until you choose the estimate again. With no step limit there is no time limit, and an explicit override is cleared. Enter commits a number; leaving the field also commits it. If Motif refuses the value, the saved Selection stays as it was. Escape restores the last accepted value before it closes Settings.

## Writing systems

For the project's writing-system settings and how to change them in FieldWorks, see [Writing systems in the window](guide:writing-systems).

## Help and reports

Settings opens Help for the current page and lets you preview a problem report. Read [When something goes wrong](guide:when-something-goes-wrong) for what a report includes and how local details are handled.

## About and data folders

About shows Motif, PanGloss, and operating-system versions and can copy those lines. Its folder button opens the managed data root, which holds Motif's paired project data and parser artifacts; FieldWorks projects remain in their own folders. See [When something goes wrong](guide:when-something-goes-wrong) for report contents and privacy choices.
