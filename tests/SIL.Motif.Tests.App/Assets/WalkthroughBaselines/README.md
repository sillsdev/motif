# Walkthrough screenshot baselines

The App test project stores one clean PNG and one numbered-callout PNG for each authored capture. Update both with `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1` when a deliberate UI change should become the new reference.

The comparison requires a 1280 by 720 image. A pixel is considered changed when any RGB channel differs by more than 12; at most 1% of pixels may be changed. This allows small rasterization differences while still detecting layout or content movement.
