# Walkthrough screenshot baselines

The App test project stores one clean PNG and one numbered-callout PNG for each authored capture. Update both with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1` when a deliberate UI change should become the new reference.

Each capture is 1280 by 720. A pixel is changed when an RGB channel differs by more than 3 or its alpha differs at all; at most 0.1% of the image (922 pixels) may change, and pixels inside numbered callouts must match exactly. The test still measures this comparison on every machine, but it does not fail on pixel differences unless `MOTIF_WALKTHROUGH_STRICT_BASELINES=1` is set. Rasterization varies between machines, including machines running the same operating system, so CI leaves the strict gate off while continuing to run every walkthrough and its non-pixel assertions.

When a capture exceeds the pixel tolerance with the strict gate off, the test writes the actual and diff PNGs under the temporary `SIL.Motif.WalkthroughDiffs` directory and reports the changed-pixel count and file paths in the test output. To check the references on the machine that maintains them, set the strict gate and run the walkthrough tests:

```powershell
$env:MOTIF_WALKTHROUGH_STRICT_BASELINES = '1'
./test.ps1
```

After refreshing references with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1`, run the strict check separately with the update variable unset.
