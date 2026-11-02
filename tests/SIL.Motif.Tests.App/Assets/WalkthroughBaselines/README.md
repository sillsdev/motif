# Walkthrough reference images

The App test project stores one clean PNG and one numbered-callout PNG for each authored capture. Update both with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1` when a deliberate UI change should become the new reference.

Each capture is 1280 by 720. A replay in the test run draws no picture: it runs every step and assertion, including the layout check made before each capture, and only checks that every reference image exists. No picture can fail a test (ADR 0075). A replay draws, annotates and compares its captures only when `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1`, `MOTIF_WALKTHROUGH_OUTPUT` or `MOTIF_WALKTHROUGH_STRICT_BASELINES=1` is set. A pixel is changed when an RGB channel differs by more than 3 or its alpha differs at all; at most 0.1% of the image (922 pixels) may change, and pixels inside numbered callouts must match exactly. With the strict setting on, any difference fails the replay; without it the difference is only reported, and text layout differs between renderers. Actual and diff PNGs are written under `bin/<Configuration>/test-results/walkthrough-diffs/` for CI to upload with test logs and TRX files.

When a capture exceeds the pixel tolerance with the strict gate off (and a picture was asked for), the test writes the actual and diff PNGs under the temporary `SIL.Motif.WalkthroughDiffs` directory and reports the changed-pixel count and file paths in the test output. To compare the reference images by hand on the machine that maintains them, set the strict gate and run the walkthrough tests:

```powershell
$env:MOTIF_WALKTHROUGH_STRICT_BASELINES = '1'
./test.ps1
```

After refreshing references with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1`, run the strict check separately with the update variable unset.

## What is fixed in a replay, and which sets pass strict

Every replay runs with the en-US culture. Before each capture it waits for the project open, evidence publication, any parse or Refresh, the automatic grammar answer and the retained Parsimony read to finish, moves the pointer off every control, and, when pictures are drawn, waits until two frames drawn one after the other are identical. Stored parse times are pinned to the replay's fixed clock, and the AI Handoff names the parse in that clock's time zone, so no time needs masking there. Text that names the run, not the product, is replaced with a fixed stand-in before the frame is drawn: the walkthrough folder path (its separators are written as backslashes) a Receipt's UTC stamp, which the Apply command takes from the machine's clock because it has no clock seam, and the two times in the Proposal bar's "Tried ... on Baseline of ..." sentence, which the Trial's own run takes from the machine's clock (the images show 12:00 PM). These three are masks, not clocks.

Proven where: on the Windows maintainer machine with `MOTIF_WALKTHROUGH_STRICT_BASELINES=1`, all thirteen replay classes passed, twice in a row. Strict has not been run on Linux or macOS, and text layout differs there, so treat strict as holding on Windows only.

overview-with-proposal (in proposals-open) is drawn in Proposal mode: the bar names the Proposal, gives the Tried sentence, offers the Today, Today → Proposal and Proposal switch with Try Again and Leave Proposal on one row, and the Text Coverage and Grammar Warnings headlines read today's value, then the Proposal's. The bar takes 28 px and appears only in Proposal mode, so only this set is affected.
