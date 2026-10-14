# Settings

Open **Settings** from the bottom of the left navigation rail to change how Motif looks and to find its keyboard shortcuts, project parsing limits, grammar-check defaults, problem reports, and version information. **Help** beside it opens guidance for the current page. Display choices are remembered for this user on this computer; a project's parsing limits are saved with that project's [Default Selection](term:default-selection).

Settings opens as a page. **Back** returns to the page you were using; Escape does the same after handling any field edit. The section navigator collapses in a narrow window. Select **Settings** again to open it, then choose a section or collapse the side bar to use the page's full width.

## Display

Zoom changes the Motif window's layout and text together. At higher zoom the usable page area is smaller, so scroll within a page to reach its content. Display shows the available zoom choices; [Keyboard shortcuts](guide:keyboard-shortcuts) lists their keys.

**Reset to 100%** is always visible and is disabled when zoom is already 100%.

Theme can follow the operating system or use Light or Dark. The change applies while Settings is open.

## Parsing

The Parsing group edits the current project's Default Selection. Setup, Settings, and **Analysis options** edit that same saved limit policy; the next parse uses it, while earlier results keep the limits from their own run. See [Default Selection](guide:default-selection) and [first-run setup](guide:first-run-setup) for choosing what to parse.

**Texts to parse** shows the selected Text count; **Use all texts** saves every project Text in the Default Selection while keeping its added words and parsing limits.

When the step limit is finite, the time limit can follow Motif's estimate or use an explicit value. An explicit value remains in force when the step limit changes until you choose the estimate again. With no step limit there is no time limit, and an explicit override is cleared. Enter commits a number; leaving the field also commits it. If Motif refuses the value, the saved Selection stays as it was. Escape restores the last accepted value before it closes Settings.

## Default proposal checks

Default proposal checks sets how thorough Motif's grammar check is when it runs by itself, from Warnings, or for a Proposal. **Compare stored analyses** checks that each word's saved analyses still parse, which is the one part of a grammar check that slows down as a project grows. Choose one:

- **Within a work budget (recommended)** compares words until a fixed amount of work is spent, then says how many were left. It gives the same result on every computer.
- **Every word** compares all of them. A large project can take half a minute or more.
- **Off** skips the comparison; the other grammar checks still run.

The choice is remembered for this user on this computer and applies to the next grammar check. Results already shown keep the choice they were checked with. The command line's [Check a grammar](cmd:grammar%20check) accepts `--stored-analyses` to override it for one run.

## Writing systems

For the project's writing-system settings and how to change them in FieldWorks, see [Writing systems in the window](guide:writing-systems).

## Texts

**Use all texts** saves every Text in the current project as its Default Selection. Parsing limits and added words keep their saved values.

## Help and reports

Settings lets you preview a problem report. Read [When something goes wrong](guide:when-something-goes-wrong) for what a report includes and how local details are handled.

## About and data folders

About shows Motif, PanGloss, and operating-system versions and can copy those lines. Its folder button opens the managed data root, which holds Motif's paired project data and parser artifacts; FieldWorks projects remain in their own folders. See [When something goes wrong](guide:when-something-goes-wrong) for report contents and privacy choices.
