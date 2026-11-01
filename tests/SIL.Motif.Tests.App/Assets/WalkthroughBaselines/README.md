# Walkthrough screenshot baselines

The App test project stores one clean PNG and one numbered-callout PNG for each authored capture. Update both with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1` when a deliberate UI change should become the new reference.

Each capture is 1280 by 720. A pixel is changed when an RGB channel differs by more than 3 or its alpha differs at all; at most 0.1% of the image (922 pixels) may change, and pixels inside numbered callouts must match exactly. Every replay measures this comparison. The Explained Word Card replay fails on pixel differences on Windows, where it uses the embedded Andika font and fixed capture dimensions; other platforms report pixel differences as advisory because text layout varies by renderer. Set `MOTIF_WALKTHROUGH_STRICT_BASELINES=1` to make any replay strict on a matching reference machine. Actual and diff PNGs are written under `bin/<Configuration>/test-results/walkthrough-diffs/` for CI to upload with test logs and TRX files.

When a capture exceeds the pixel tolerance with the strict gate off, the test writes the actual and diff PNGs under the temporary `SIL.Motif.WalkthroughDiffs` directory and reports the changed-pixel count and file paths in the test output. To check the references on the machine that maintains them, set the strict gate and run the walkthrough tests:

```powershell
$env:MOTIF_WALKTHROUGH_STRICT_BASELINES = '1'
./test.ps1
```

After refreshing references with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1`, run the strict check separately with the update variable unset.

## What is fixed in a replay, and which sets pass strict

Every replay runs with the en-US culture. Before each capture it waits for the project open, evidence publication, any parse or Refresh, the automatic grammar answer and the retained Parsimony read to finish, moves the pointer off every control, and waits until two frames drawn one after the other are identical. Stored parse times are pinned to the replay's fixed clock, and the AI Handoff names the parse in that clock's time zone, so no time needs masking there. Text that names the run, not the product, is replaced with a fixed stand-in before the frame is drawn: the walkthrough folder path (its separators are written as backslashes) a Receipt's UTC stamp, which the Apply command takes from the machine's clock because it has no clock seam, and the two times in the Proposal bar's "Tried ... on Baseline of ..." sentence, which the Trial's own run takes from the machine's clock (the images show 12:00 PM). These three are masks, not clocks.

Proven where: on the Windows maintainer machine with `MOTIF_WALKTHROUGH_STRICT_BASELINES=1`, all thirteen replay classes pass, twice in a row. Strict has not been run on Linux or macOS, and text layout differs there, so treat strict as holding on Windows only.

overview-with-proposal (in proposals-open) is drawn in Proposal mode: the bar names the Proposal, gives the Tried sentence, offers the Today, Today → Proposal and Proposal switch with Try Again and Leave Proposal on one row, and the Text Coverage and Grammar Warnings headlines read today's value, then the Proposal's. The bar takes 28 px and appears only in Proposal mode, so only this set is affected.
