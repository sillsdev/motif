import argparse
from pathlib import Path


def synchronize(root, check):
    canonical = (root / "plugin/references/crosswalk.md").read_bytes()
    targets = sorted((root / "plugin/skills").glob("*/references/crosswalk.md"))
    if not targets:
        raise ValueError("No skill crosswalk copies found")
    stale = [path for path in targets if path.read_bytes() != canonical]
    if check and stale:
        raise ValueError("Stale crosswalk copies: " + ", ".join(str(p.relative_to(root)) for p in stale))
    for path in stale:
        path.write_bytes(canonical)
    print(f"Crosswalk copies {'checked' if check else 'synchronized'}: {len(targets)}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Keep standalone skill crosswalks identical to their source.")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    synchronize(Path(__file__).resolve().parent.parent, args.check)
